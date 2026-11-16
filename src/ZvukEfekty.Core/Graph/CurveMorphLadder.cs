namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A pre-built row of shapes stepping from one curve to another by <see cref="Curve.Morph"/>, so a
/// render can read a morphed wave per sample without building a curve per sample.
///
/// <para><b>Why a ladder and not the morph itself.</b> <see cref="Curve.Morph"/> costs about 121 µs
/// — it builds a 4096-entry lookup table, and every segment of a morphed curve is bent, so it takes
/// the slow path. That is twelve times a whole 10 ms block, so it cannot happen inside the render at
/// any rate at all: not per sample, and not per block either. Building the whole row up front, on a
/// thread that can afford it, turns render time back into what the sample-level crossfade already
/// cost — two table reads and a lerp.</para>
///
/// <para><b>What the steps cost in accuracy.</b> Between two rungs the value is interpolated per
/// sample, which is a sample-level crossfade over one step's width — the very thing the vector morph
/// exists to avoid, but confined to 1/<see cref="Steps"/> of the sweep. Measured against the exact
/// morph over the built-in sine-to-square, at various points along the sweep:</para>
///
/// <list type="table">
///   <listheader><term>rungs</term><description>error, worst / typical · memory · build</description></listheader>
///   <item><term>8</term><description>−19 dB / −48 dB · 144 kB · 4,0 ms</description></item>
///   <item><term>16</term><description>−26 dB / −55 dB · 272 kB · 1,9 ms</description></item>
///   <item><term>32</term><description>−34 dB / −65 dB · 528 kB · 3,9 ms</description></item>
///   <item><term>64</term><description>−49 dB / −75 dB · 1040 kB · 7,7 ms</description></item>
///   <item><term>128</term><description>−54 dB / −83 dB · 2064 kB · 14,5 ms</description></item>
/// </list>
///
/// <para>The worst figure is always at the very top of the sweep, where a square's edge goes from a
/// dozen table cells wide to one; the typical figure is everywhere else. 64 is the pick: −49 dB at
/// the sharp end and −63 dB or better across the rest of the sweep, for a megabyte that is shared
/// rather than paid per voice.</para>
///
/// <para><b>And the error does not grow when the sweep speeds up.</b> That is the whole difference
/// from resolving the shape once per block, which was measured at −50 dB over a 1 s sweep but −10 dB
/// over a 10 ms one (see <see cref="Modules.WaveMorphModule"/>). A ladder is indexed by HOW FAR the
/// morph has got, not by when the last block started, so a per-note sweep is as accurate as a slow
/// one.</para>
///
/// <para><b>Keyed by the points, not by the object.</b> Every note-on clones the whole patch through
/// the serializer, so each voice holds its own <see cref="Curve"/> instances with equal contents and
/// different references — the same reason <see cref="Audio.AudioSampleCache"/> exists for the
/// sampler. Keying on the breakpoints is what lets sixteen voices share one megabyte instead of
/// building sixteen.</para>
/// </summary>
public sealed class CurveMorphLadder
{
    /// <summary>How many steps the row is cut into — see the type's remarks for the sweep this was
    /// picked from. One more shape than this is held, since both ends are included.</summary>
    public const int Steps = 64;

    /// <summary>Ceiling on how many rows are kept, least-recently-asked-for dropped first. Each is
    /// (<see cref="Steps"/> + 1) × 16 kB, so eight is about 8,3 MB — room for a patch with several
    /// morphing oscillators, and for a few edits' worth of superseded shapes, while still
    /// bounded.</summary>
    private const int MaxCachedLadders = 8;

    private static readonly object Gate = new();
    private static readonly Dictionary<Key, CurveMorphLadder> Entries = new();

    // Least recently asked for first, so eviction drops the row nothing is playing.
    private static readonly LinkedList<Key> Usage = new();

    private readonly Curve[] _rungs;

    private CurveMorphLadder(Curve[] rungs) => _rungs = rungs;

    /// <summary>
    /// The row stepping from <paramref name="from"/> to <paramref name="to"/>, building it if this
    /// is the first ask for those two shapes.
    ///
    /// <para><b>Never call this from the audio thread.</b> A miss builds sixty-five curves, which is
    /// most of a hundredth of a second — call it from <see cref="ModuleBase.Prepare"/>, which is the
    /// hook that exists to be on a thread that can afford exactly this.</para>
    /// </summary>
    public static CurveMorphLadder For(Curve from, Curve to)
    {
        var key = new Key(from, to);

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var cached))
            {
                Touch(key);
                return cached;
            }
        }

        // Built outside the lock: two threads asking for the same new row at once would each build
        // one, and the loser's copy is simply dropped. Wasting a build is cheaper than holding the
        // lock across 7,7 ms of work while another voice starts.
        var rungs = new Curve[Steps + 1];
        rungs[0] = from;
        rungs[Steps] = to;
        for (var i = 1; i < Steps; i++)
            rungs[i] = Curve.Morph(from, to, i / (double)Steps);

        var ladder = new CurveMorphLadder(rungs);

        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var raced))
            {
                Touch(key);
                return raced;
            }

            Entries[key] = ladder;
            Usage.AddLast(key);

            while (Usage.Count > MaxCachedLadders)
            {
                var oldest = Usage.First!.Value;
                Usage.RemoveFirst();
                Entries.Remove(oldest);
            }
        }

        return ladder;
    }

    /// <summary>
    /// The morphed wave at <paramref name="x"/>, <paramref name="mix"/> of the way across — 0 being
    /// the shape this row starts on and 1 the shape it ends on. Two table reads and a lerp, which is
    /// what makes it usable per sample.
    /// </summary>
    public float Sample(double mix, double x)
    {
        var position = Math.Clamp(mix, 0, 1) * Steps;
        var index = Math.Min((int)position, Steps - 1);
        var fraction = (float)(position - index);

        var low = _rungs[index].Sample(x);
        return low + (_rungs[index + 1].Sample(x) - low) * fraction;
    }

    private static void Touch(Key key)
    {
        Usage.Remove(key);
        Usage.AddLast(key);
    }

    /// <summary>The two shapes a row was built from, compared by their breakpoints rather than by
    /// reference — see the type's remarks on why the reference is useless here.</summary>
    private sealed class Key : IEquatable<Key>
    {
        private readonly CurvePoint[] _from;
        private readonly CurvePoint[] _to;
        private readonly int _hash;

        public Key(Curve from, Curve to)
        {
            _from = from.Points.ToArray();
            _to = to.Points.ToArray();

            var hash = new HashCode();
            foreach (var point in _from)
                hash.Add(point);
            hash.Add(-1);
            foreach (var point in _to)
                hash.Add(point);
            _hash = hash.ToHashCode();
        }

        public bool Equals(Key? other)
            => other is not null
               && _from.AsSpan().SequenceEqual(other._from)
               && _to.AsSpan().SequenceEqual(other._to);

        public override bool Equals(object? obj) => Equals(obj as Key);

        public override int GetHashCode() => _hash;
    }
}
