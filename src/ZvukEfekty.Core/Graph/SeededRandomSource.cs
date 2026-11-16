namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A random generator tied to a module's "Seed" knob, so anything built on randomness renders the
/// same way twice.
///
/// Without this, a module holding a bare <see cref="Random"/> kept advancing between renders, and
/// the same patch measured differently every time — on a high-Q resonator chain the peak moved by
/// more than 10 dB from one render to the next, enough for a patch to clip on one export and not
/// on the next, and enough to make "does this patch clip?" an unanswerable question.
///
/// The seed's default is drawn per instance rather than being a shared constant, so two modules of
/// the same kind in one patch stay uncorrelated instead of emitting identical samples and summing
/// coherently. Saving the patch records whichever seed the module is on, which is what carries
/// reproducibility across sessions; a patch saved before seeds existed simply gets a fresh default
/// on load and is stable from then on.
///
/// <para>Reproducibility can also be turned OFF per knob, which is the point of
/// <see cref="ModuleParameter.IsAutoSeeded"/>: some material is supposed to come out different
/// every time. That is handled entirely in <see cref="Reset"/>, so all five seeded modules get it
/// without knowing about it — they already call Reset from their own ResetState.</para>
/// </summary>
public sealed class SeededRandomSource
{
    /// <summary>Upper bound for the knob. Wide enough that a randomize is unlikely to repeat,
    /// small enough to stay readable in the inspector.</summary>
    public const int MaxSeed = 1_000_000;

    private readonly ModuleParameter _seed;
    private Random _random;
    private int _seededWith;

    public SeededRandomSource(ModuleParameter seed)
    {
        _seed = seed;
        _seededWith = CurrentSeed;
        _random = new Random(_seededWith);
    }

    private int CurrentSeed => (int)Math.Round(Math.Clamp(_seed.Value, 0, MaxSeed));

    /// <summary>Restarts the sequence from the top of the current seed — or, when the knob is set
    /// to draw itself (<see cref="ModuleParameter.IsAutoSeeded"/>), from a brand new one.
    ///
    /// <para>This is the whole of the auto-seed feature. Every module that owns randomness calls
    /// this from its own <c>ResetState</c>, and ResetState is exactly "the sound is starting":
    /// once per batch Play/Export, and once per live voice as it is built. Putting it here rather
    /// than in five ResetState overrides is the same move <c>GetModulatedValues</c> made for tempo
    /// sync — one funnel, so nothing can be left out by being forgotten.</para></summary>
    public void Reset()
    {
        _seededWith = _seed.IsAutoSeeded ? Random.Shared.Next(0, MaxSeed) : CurrentSeed;
        _random = new Random(_seededWith);
    }

    /// <summary>
    /// The generator, rebuilt first if the Seed knob has moved since it was last built — which
    /// covers both a change made in the inspector and the value the deserializer applies after the
    /// module was constructed, neither of which is followed by a <see cref="Reset"/> in a live voice.
    /// <paramref name="wasReseeded"/> reports whether that rebuild just happened, so a caller
    /// holding state derived from the generator (a sample-and-hold value) knows to redraw it.
    /// </summary>
    public Random GetGenerator(out bool wasReseeded)
    {
        // Not while the knob draws itself. The comparison asks "has the knob moved since we seeded
        // from it", and with auto-seed we did not seed from it — the drawn number will almost never
        // equal it, so this would read as a knob move on every single block and reseed the sequence
        // continuously, which is noise rather than the once-per-sound variation asked for.
        wasReseeded = !_seed.IsAutoSeeded && CurrentSeed != _seededWith;
        if (wasReseeded)
            Reset();

        return _random;
    }
}
