namespace ZvukEfekty.Core.Graph;

/// <summary>
/// The per-parameter buffers one render pass hands out, kept from block to block instead of being
/// allocated again for each one.
///
/// <para><b>Why.</b> <see cref="ModuleBase.GetModulatedValues"/> fills a <c>float[SampleCount]</c>
/// for every parameter of every module in every block — even for a knob nothing is patched into,
/// where the whole array is one repeated number. A live voice renders some thirty of those a
/// second: measured across <c>Sestavy/nastroje</c>, between 154 and 528 kB per voice per 30 ms
/// block, all of it garbage by the end of the block.</para>
///
/// <para><b>Why a cursor and not a buffer per parameter.</b> A parameter-owned buffer would be held
/// for as long as the module is, and a batch render's SampleCount is the whole clip — a minute of
/// audio is 10 MB per knob. Handing buffers out in pass order and rewinding at the start of the
/// next pass keeps exactly as many arrays as one pass asks for, and they belong to the pass's owner
/// (a <see cref="Audio.Voice"/>), so they are freed when the note is.</para>
///
/// <para><b>What makes it safe.</b> Everything rented here dies with the pass that rented it. A
/// parameter buffer is a local inside one <c>Process</c> call — checked across every module: none
/// stores one in a field. An output buffer is read by the modules downstream of it and, for the
/// last one, by the mix — but the mix runs before that voice renders its next block, so the pass
/// is over either way. The two buffers that a module deliberately keeps ACROSS blocks (an
/// envelope's synthesised live gate, a delay line) are allocated the old way and stay that way:
/// rent one of those and the next pass would hand the same array to somebody else.</para>
///
/// <para>Two ways to take one: <see cref="Rent"/> hands it over dirty, for a caller that writes
/// every sample of it, and <see cref="RentCleared"/> zeroes it first, which is what a module output
/// needs to behave exactly like the <c>new float[]</c> it replaced.</para>
///
/// <para><b>Threading.</b> Not shared: one of these belongs to one voice, and a voice renders on
/// one thread at a time. A batch render passes none and goes on allocating per call.</para>
/// </summary>
public sealed class BlockScratch
{
    private readonly List<float[]> _buffers = new();
    private int _sampleCount = -1;
    private int _next;

    /// <summary>Rewinds to the first buffer — called by <see cref="ModuleGraph.RenderModule"/> for
    /// the pass it is about to run. A block of a different length throws the buffers away rather
    /// than trying to fit the old ones: live play settles on one block size and stays there.</summary>
    public void BeginPass(int sampleCount)
    {
        if (sampleCount != _sampleCount)
        {
            _buffers.Clear();
            _sampleCount = sampleCount;
        }

        _next = 0;
    }

    /// <summary>The next buffer of this pass, allocated the first time the pass reaches this far and
    /// reused by every pass after. Comes back <b>dirty</b> — for a caller that writes every sample
    /// of it, such as <see cref="ModuleBase.GetModulatedValues"/>.</summary>
    public float[] Rent()
    {
        if (_next == _buffers.Count)
            _buffers.Add(new float[_sampleCount]);

        return _buffers[_next++];
    }

    /// <summary>The next buffer if this pass's blocks are <paramref name="length"/> samples long,
    /// and null if they are not — for a caller that has a length of its own to satisfy and would
    /// rather allocate than be handed the wrong size. Dirty, like <see cref="Rent"/>.</summary>
    public float[]? RentIfLength(int length) => length == _sampleCount ? Rent() : null;

    /// <summary>The next buffer, zeroed — what a module's output buffer needs, since
    /// <c>new float[]</c> is zeroed and a fair number of modules count on that (they add into their
    /// output rather than assigning every sample of it, or leave the tail of it alone). The clear
    /// is the same memset the allocation used to do, so this costs nothing the old code did not
    /// already pay; what it saves is the allocation and the collection after it.</summary>
    public float[] RentCleared()
    {
        var buffer = Rent();
        Array.Clear(buffer);
        return buffer;
    }
}
