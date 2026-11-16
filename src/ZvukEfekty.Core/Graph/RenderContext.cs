using ZvukEfekty.Core.Persistence;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// Parameters shared by every module for a single render pass.
/// </summary>
public sealed class RenderContext
{
    public RenderContext(int sampleRate, int sampleCount, double bpm = PatchTiming.DefaultBpm)
    {
        SampleRate = sampleRate;
        SampleCount = sampleCount;
        Bpm = bpm;
    }

    public int SampleRate { get; }
    public int SampleCount { get; }
    public double DurationSeconds => (double)SampleCount / SampleRate;

    /// <summary>The patch's tempo, which is what turns a parameter's note value into seconds —
    /// see <see cref="ModuleParameter.ResolveValue"/>.
    ///
    /// Carried here rather than read from a shared setting because a live voice renders from its
    /// OWN clone of the patch (see <c>Audio.Voice</c>), and the clone has no way back to whatever
    /// the editor currently shows. Always a real tempo, never null: the patch has a BPM whether or
    /// not its LENGTH is locked to bars, so a synced delay keeps its note value either way.</summary>
    public double Bpm { get; }

    /// <summary>How long the note is held in this render, in seconds, or 0 when the patch says
    /// nothing about it. Filled in by <see cref="ModuleGraph.RenderModule"/> from the patch's Nota
    /// module, so every caller gets it without having to know to pass it.
    ///
    /// <para>Read by an envelope with nothing wired into its Gate, which is otherwise deaf to the
    /// Nota module entirely: before this it had to guess the length from the clip, so the Držení
    /// the user set was silently ignored unless they had also patched the Gate wire. With Gate
    /// wired nothing here is read — the gate signal already carries the same answer, per sample.</para></summary>
    public double NoteHoldSeconds { get; internal set; }

    /// <summary>Set by any envelope that runs in this pass, to say that the note's length is
    /// already being shaped and nothing downstream should shape it again.
    ///
    /// <para>What it protects: a patch with no envelope at all — Nota → Oscilátor → Output — has
    /// nothing that can make silence, so the oscillator simply runs for the whole clip and Držení
    /// does nothing at all. <c>OutputModule</c> closes that gap by applying the hold itself, and
    /// this flag is how it knows not to: an envelope that has already released the note would
    /// otherwise be cut a second time, chopping off the release it just drew.</para>
    ///
    /// <para>Reliable because <see cref="ModuleGraph.RenderModule"/> renders in dependency order
    /// and the Output is by construction last, so every envelope feeding it has already run.</para></summary>
    public bool NoteHoldConsumed { get; internal set; }

    /// <summary>Whether the key is down, for a block rendered by a live <c>Audio.Voice</c>; null in
    /// a batch render, where there is no key to be down and Držení answers the question instead.
    ///
    /// <para>Set by the voice itself rather than gathered from the modules, because it is a fact
    /// about the voice: the voice IS one note, and its gate is the note's gate. A patch with two
    /// Nota modules split across keyboard rows would otherwise have to be asked which of them the
    /// answer comes from, and for a single voice the answer is the same either way.</para>
    ///
    /// <para>Two readers, both of them things that are deaf to the key without it: an envelope with
    /// nothing wired into its Gate, and an Output in a patch that has no envelope at all. Before
    /// this the first replayed its whole shape once per ~20 ms block, and the second never went
    /// quiet at all until a safety timeout took the voice away.</para></summary>
    public bool? LiveKeyDown { get; internal set; }

    /// <summary>Where <see cref="ModuleBase.GetModulatedValues"/> takes its per-parameter buffers
    /// from, or null to allocate one per call. Set by a live <c>Audio.Voice</c>, which renders the
    /// same graph at the same block size some thirty times a second and owns the buffers for as
    /// long as the note lasts — see <see cref="BlockScratch"/> for why a batch render leaves it
    /// null.</summary>
    internal BlockScratch? Scratch { get; init; }
}
