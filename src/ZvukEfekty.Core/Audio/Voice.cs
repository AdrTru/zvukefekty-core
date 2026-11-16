using ZvukEfekty.Core.Graph;
using ZvukEfekty.Core.Modules;
using ZvukEfekty.Core.Persistence;

namespace ZvukEfekty.Core.Audio;

/// <summary>
/// One currently-sounding (or releasing) note: an independent clone of the instrument patch
/// being played, so several notes held at once render as separate parallel signal chains
/// (polyphony) instead of fighting over one set of module parameters.
/// </summary>
public sealed class Voice
{
    // Step between one voice's seed offset and the next. Prime, and coprime with
    // SeededRandomSource.MaxSeed, so the offsets run through a million distinct values before
    // any of them comes round again.
    private const int SeedStride = 104_729;

    /// <summary>Block peak below which this voice counts as having gone quiet — −66 dBFS.
    ///
    /// <para>It lives here because deciding a release has finished is the voice's own job, and
    /// because this is the number the decision is actually made on (see
    /// <see cref="TrackSilenceSinceRelease"/>). It was borrowed from the player's
    /// <c>NoteActivity.AudibleThreshold</c> until 2026-09-19, which had the dependency the wrong
    /// way round: a type describing the drawn keyboard owned a fact about rendering. The keyboard
    /// now derives its threshold from this one, so there is still a single number.</para></summary>
    public const float SilenceThreshold = 0.0005f;

    private static long _voiceSequence;

    private readonly ModuleGraph _graph;
    private readonly OutputModule _output;
    private readonly List<NoteModule> _noteModules;

    // The parameter buffers this voice's blocks are rendered through — one set, reused block after
    // block, rather than a fresh set of arrays thirty times a second. Owned by the voice, so they
    // go when the note does; see BlockScratch.
    private readonly BlockScratch _scratch = new();

    private double _frequencyHz;
    private bool _gateOpen;
    private int _silentBlocksSinceRelease;
    private double _secondsSinceRelease;

    /// <summary>The tempo this voice's tempo-synced knobs resolve against. Captured once, at
    /// note-on, and not followed afterwards — the same rule every other knob already lives by,
    /// since a voice renders its own CLONE of the patch and stops hearing about edits the moment
    /// it is built. Nudging BPM mid-note therefore lands on the next note, not this one.</summary>
    private readonly double _bpm;

    // -1 while this voice is playing normally; otherwise how much of the steal fade is left.
    private int _stealFadeRemaining = -1;
    private int _stealFadeTotal;

    public Voice(ModuleGraph templatePatch, int midiNote, double frequencyHz, double bpm)
    {
        MidiNote = midiNote;
        _frequencyHz = frequencyHz;
        _bpm = bpm;
        _graph = ClonePatch(templatePatch);
        DecorrelateSeeds(_graph);

        // Before Prepare, not after: a module that prepares something out of a shape needs the
        // shape to be the one actually wired to it, and until this runs every curve socket still
        // reports its own fallback. WaveMorphModule builds a whole row of morphed waves off this.
        _graph.ResolveCurveInputs();

        // On the calling thread, never the audio callback — same reason the clone itself is built
        // here (see VoiceManager.NoteOn). A sampler decodes its file in here.
        foreach (var module in _graph.Modules)
            module.Prepare();
        _output = FindOutput(_graph)
            ?? throw new InvalidOperationException("Nástrojová sestava neobsahuje modul Output.");

        // Here and not on the first block: building a swarm's instances deserializes its inside
        // once per instance, and the first block is rendered on the audio thread.
        _graph.PrepareSwarms(_output);
        _noteModules = _graph.Modules.OfType<NoteModule>().ToList();
        _gateOpen = true;
    }

    public int MidiNote { get; }

    /// <summary>When this voice started sounding, counted in note-ons rather than in time. The
    /// <c>VoiceManager</c> sets it and uses it to decide which voice to take away when the
    /// polyphony cap is reached. Named in prose, not <c>cref</c>: that class lives in the player
    /// and this assembly cannot see it — see VlastnostiSestaveni.cs.</summary>
    internal long StartOrder { get; set; }

    /// <summary>True once the key has been let go and only the tail is left. A voice already in its
    /// release is the one a listener misses least when something has to be taken away.</summary>
    public bool IsReleasing => !_gateOpen;

    /// <summary>True once <see cref="Steal"/> has begun taking this voice out of service.</summary>
    public bool IsBeingStolen => _stealFadeRemaining >= 0;

    /// <summary>True once the note has been released and rendered silence for a while (or a
    /// safety timeout elapsed) — the <c>VoiceManager</c> drops it after that.</summary>
    public bool IsFinished { get; private set; }

    /// <summary>Loudest sample of the block this voice rendered last, 0..1 — how much of the mix
    /// this note is actually responsible for at the moment. Zero until the first block.</summary>
    public float LastBlockPeak { get; private set; }

    public void Release() => _gateOpen = false;

    /// <summary>
    /// Takes this voice out of service over <paramref name="fadeSamples"/>, then finishes it.
    ///
    /// A fade rather than a cut: a voice removed the instant its slot is needed leaves whatever
    /// level it was at as a step to zero, which is a click — and the cap is reached exactly when a
    /// lot is already sounding, so that is the worst moment to add one. A few milliseconds is
    /// short enough that the slot is free almost at once and long enough to be inaudible.
    /// </summary>
    public void Steal(int fadeSamples)
    {
        if (IsBeingStolen)
            return;

        _stealFadeTotal = Math.Max(1, fadeSamples);
        _stealFadeRemaining = _stealFadeTotal;
        _gateOpen = false;
    }

    /// <summary>Re-opens the gate on a voice that was mid-release (fast repeated key presses),
    /// instead of spawning a duplicate voice for the same note.</summary>
    public void Retrigger()
    {
        _gateOpen = true;
        _silentBlocksSinceRelease = 0;
        _secondsSinceRelease = 0;
        IsFinished = false;
    }

    public float[] RenderBlock(int sampleCount, int sampleRate)
    {
        foreach (var noteModule in _noteModules)
        {
            noteModule.CurrentFrequencyHz = _frequencyHz;
            noteModule.CurrentMidiNote = MidiNote;
            noteModule.IsGateOpen = _gateOpen;

            // Says "a voice is driving you", which is how the module tells live play from a batch
            // render — the latter has to close its own gate after Držení. Set every block rather
            // than once in the constructor, so it cannot be left stale by anything that rebuilds
            // or resets the clone's modules.
            noteModule.IsDrivenLive = true;
        }

        // Says which way the key is, for the modules that have no wire telling them — see
        // RenderContext.LiveKeyDown. The NoteModules above get the same answer pushed in directly.
        var ctx = new RenderContext(sampleRate, sampleCount, _bpm) { LiveKeyDown = _gateOpen, Scratch = _scratch };
        var result = _graph.RenderModule(_output, ctx);
        var buffer = result[_output.Outputs[0]];

        // Measured on every block, not just after a release: the drawn keyboard asks a HELD voice
        // whether it is still making a sound, which is the one question the gate cannot answer.
        // Taken before the steal fade below, so it is the level the voice actually rendered — a
        // stolen voice is off the keyboard anyway (see VoiceManager.PublishNoteActivity).
        LastBlockPeak = PeakOf(buffer, sampleCount);

        if (IsBeingStolen)
            ApplyStealFade(buffer, sampleCount);
        else if (!_gateOpen)
            TrackSilenceSinceRelease(sampleCount, sampleRate);

        return buffer;
    }

    /// <summary>Ramps the block down to silence and finishes the voice when the ramp runs out.
    /// Written straight into the rendered block: OutputModule passes its input through by
    /// reference, so this array traces back to a module's own freshly allocated buffer for this
    /// block and nothing reads it after the mix.</summary>
    private void ApplyStealFade(float[] buffer, int sampleCount)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            if (_stealFadeRemaining <= 0)
            {
                buffer[i] = 0;
                continue;
            }

            buffer[i] *= (float)(_stealFadeRemaining / (double)_stealFadeTotal);
            _stealFadeRemaining--;
        }

        if (_stealFadeRemaining <= 0)
            IsFinished = true;
    }

    private static float PeakOf(float[] buffer, int sampleCount)
    {
        var peak = 0f;
        for (var i = 0; i < sampleCount; i++)
        {
            var abs = Math.Abs(buffer[i]);
            if (abs > peak) peak = abs;
        }

        return peak;
    }

    private void TrackSilenceSinceRelease(int sampleCount, int sampleRate)
    {
        _silentBlocksSinceRelease = LastBlockPeak < SilenceThreshold ? _silentBlocksSinceRelease + 1 : 0;
        _secondsSinceRelease += (double)sampleCount / sampleRate;

        // Two consecutive near-silent blocks after release is a real fade-out; the 5s cap is
        // just a safety net against a voice that (mis-patched) never actually goes quiet.
        if (_silentBlocksSinceRelease >= 2 || _secondsSinceRelease > 5.0)
            IsFinished = true;
    }

    /// <summary>
    /// Nudges every Seed knob in this voice's clone by a per-voice offset.
    ///
    /// Without it each clone deserializes the same seeds, so two notes held at once render bit-
    /// identical noise. Identical signals sum coherently -- amplitude grows as N, not as sqrt(N)
    /// -- so a noise-based patch gets exactly twice as loud on the second voice (measured 2.00x
    /// on bici.zvuky and zvon.zvuky) and clips that much sooner.
    ///
    /// Only live play is touched: a batch render and an export drive the graph directly and never
    /// build a Voice, so the reproducibility the seeds exist for is unaffected. Live play has no
    /// use for it anyway -- nobody diffs two performances sample by sample.
    /// </summary>
    private static void DecorrelateSeeds(ModuleGraph graph)
    {
        var offset = Interlocked.Increment(ref _voiceSequence) * SeedStride % SeededRandomSource.MaxSeed;

        foreach (var parameter in graph.Modules.SelectMany(module => module.Parameters))
            if (parameter.IsSeed)
                parameter.Value = (parameter.Value + offset) % SeededRandomSource.MaxSeed;
    }

    private static ModuleGraph ClonePatch(ModuleGraph source)
    {
        // A live voice renders in a stream of small blocks and has no clip length of its own,
        // so the timing carried by the clone is meaningless — zero it rather than letting the
        // default 1.5 s suggest a duration nothing here would honour.
        var timing = new PatchTiming { DurationSeconds = 0 };
        var json = PatchSerializer.Serialize(source, new Dictionary<ModuleBase, (double X, double Y)>(), timing);
        var clone = PatchSerializer.Deserialize(json).Graph;
        return clone;
    }

    private static OutputModule? FindOutput(ModuleGraph graph)
    {
        var outputs = graph.Modules.OfType<OutputModule>().ToList();
        var wired = outputs.FirstOrDefault(o => graph.Connections.Any(c => c.Target.Owner == o));
        return wired ?? outputs.FirstOrDefault();
    }
}
