using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Schroeder-Moorer reverb: eight parallel comb filters, each with a one-pole lowpass in its
/// feedback loop, then four all-pass sections in series.
///
/// <para>The combs make the tail — each is a resonator ringing at its own spacing, and eight
/// mutually prime lengths ring densely enough that the individual echoes stop being countable. The
/// all-passes then smear what comes out without changing its magnitude spectrum, which is what
/// turns a set of eight distinct ringing pitches into something that reads as a room rather than as
/// a chord. The lowpass in each comb loop is why a real room's tail goes dull as it dies: air and
/// soft surfaces eat the top end on every bounce, and a reverb without it sounds like tile.</para>
///
/// <para>The whole network is inside one module because it has to be: <c>ModuleGraph</c> rejects
/// cycles, so no arrangement of Delay and Mixer nodes can build a feedback comb, let alone twelve
/// of them.</para>
///
/// <para><b>Mono, deliberately.</b> The usual objection to a mono reverb is fair — half of what a
/// real room does is arrive at your two ears differently, and that half is simply absent here.
/// Tomáš decided on 2026-08-30 that this engine is not getting stereo, so there is nothing to wait
/// for and nothing to write twice; what a mono reverb still does well is the thing the DJ recipes
/// actually ask for, which is a tail (see <c>dj_zvuky_zdroje/recepty/</c> 02, 04 and 10 — impact,
/// downlifter and reverse cymbal all wanted decay, not width).</para>
///
/// <para><b>Measured, not assumed.</b> The Dozvuk knob is honest across its range: asking for 0.3,
/// 0.5, 1, 2, 5 and 10 s and measuring the decay curve back gives 0.31, 0.50, 1.00, 2.01, 5.01 and
/// 10.02 s — within 0.4% everywhere except the shortest setting, which runs 2.7% long because a
/// 0.3 s tail is only eight bounces of the longest comb and there is not much curve left to fit.
/// Damping shortens it, as it must: at the default Tlumení 0.4 a Dozvuk of 2 s measures 1.71 s
/// broadband, while the tail's spectral centroid falls from 2415 Hz to 478 Hz over its length —
/// that darkening IS the damping, and the knob is exact only at DC.</para>
///
/// <para>Level behaves, which for anything with feedback in it is the thing worth checking (see
/// <see cref="ResonatorModule"/>, whose default gain is 20-50x too loud). Fully wet, a percussive
/// hit peaking at 0.8 comes back as a 0.29 tail; the worst case — a sustained 220 Hz tone at 0.8
/// into a 10 s decay, where energy piles up for as long as you let it — reaches 0.90, still under
/// full scale. At the default Mix 0.3 nothing in those tests passed 0.68. Over 108 combinations
/// spanning every knob's full range there was no NaN, no infinity and no runaway.</para>
///
/// <para>Costs 3.81 ms per second of rendered audio against Delay's 1.18 (Release build, 44.1 kHz)
/// — about 0.08 ms per 20 ms block, so a full 16-voice chord spends under 1.5 ms of its 20 ms
/// budget here. Note that each voice clones its own reverb, so a chord gets sixteen small rooms
/// rather than one room with sixteen sources in it; put the reverb after the mix if that matters.
/// </para>
/// </summary>
public sealed class ReverbModule : ModuleBase
{
    /// <summary>
    /// Freeverb's comb lengths in samples at 44.1 kHz, and its all-pass lengths. Mutually prime on
    /// purpose: lengths that share a factor ring at overlapping frequencies, and the gaps between
    /// their echoes line up into a flutter you can hear as a pitch.
    /// </summary>
    private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };

    private static readonly int[] AllpassTuning = { 556, 441, 341, 225 };

    /// <summary>The rate <see cref="CombTuning"/> is quoted at; every length is scaled from here so
    /// the room keeps its size at a different sample rate instead of shrinking.</summary>
    private const double TuningSampleRate = 44100;

    /// <summary>Highest rate the buffers are sized for, matching <see cref="DelayModule"/>'s
    /// budget.</summary>
    private const int MaxSampleRate = 96000;

    private const double MinSize = 0.2;
    private const double MaxSize = 1.5;

    /// <summary>Ceiling on pre-delay. 120 ms is already a stadium; the buffer is cloned into every
    /// voice, so the range stops where the musical use does.</summary>
    private const double MaxPredelayMs = 120;

    /// <summary>Schroeder's all-pass coefficient. Not a knob: it sets how much the section smears
    /// rather than how much it colours, and 0.5 is the value the topology is written around.</summary>
    private const float AllpassGain = 0.5f;

    /// <summary>
    /// Hard ceiling on any comb's feedback, for a modulation input driving Dozvuk somewhere absurd:
    /// nothing clamps a modulated value to its knob's range, and feedback 1 is a tail that never
    /// ends.
    ///
    /// <para>The number has to clear what the knob itself can legitimately ask for, and the
    /// demanding case is not the longest decay but the SHORTEST comb at the SMALLEST Velikost —
    /// a line that comes round every 5 ms has to lose very little each time to still be ringing ten
    /// seconds later. That corner needs 0.9965, so a first attempt at 0.97 quietly capped the top
    /// of the range: Dozvuk 10 s measured 7.4 s. At 0.999 the whole range is honest, and the worst
    /// a runaway modulation can buy is about 35 seconds.</para>
    /// </summary>
    private const double MaxFeedback = 0.999;

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _decay;
    private readonly ModuleParameter _size;
    private readonly ModuleParameter _damping;
    private readonly ModuleParameter _predelay;
    private readonly ModuleParameter _mix;

    private readonly float[][] _combs;
    private readonly int[] _combPos;

    /// <summary>One-pole lowpass state per comb — the "air" in the loop.</summary>
    private readonly float[] _combLowpass;

    private readonly float[][] _allpasses;
    private readonly int[] _allpassPos;

    private readonly float[] _predelayLine;
    private int _predelayPos;

    // Lengths and feedbacks derived from Dozvuk/Velikost. Recomputing them costs eight Math.Pow
    // calls, so they are cached and only redone when either knob actually moves — which for an
    // unmodulated patch is once per block rather than 44 100 times a second.
    private readonly int[] _combLength;
    private readonly float[] _combFeedback;
    private readonly int[] _allpassLength;
    private double _cachedDecay = double.NaN;
    private double _cachedSize = double.NaN;
    private int _cachedSampleRate;

    public ReverbModule() : base("Reverb")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        // Seconds to fall 60 dB, which is what "reverb time" means everywhere else, rather than
        // Freeverb's opaque 0-1 "room size" knob that is really just the feedback coefficient. The
        // recipes are written in seconds too — recipe 10 asks for 2-5 s — so the number on the knob
        // is the number in the instructions.
        _decay = AddParameter("Dozvuk", 1.8, 0.1, 10, "s", tempoSync: TempoSyncKind.Seconds);

        // Scales every delay length, so it changes the room's SHAPE while Dozvuk holds its length:
        // short combs ring close together and sound like a tiled box, long ones like a hall.
        _size = AddParameter("Velikost", 1, MinSize, MaxSize);

        _damping = AddParameter("Tlumení", 0.4, 0, 1);
        _predelay = AddParameter("Předzpoždění", 0, 0, MaxPredelayMs, "ms", tempoSync: TempoSyncKind.Milliseconds);

        // A true crossfade, unlike DelayModule's "input + tap * Mix" where the dry signal is always
        // present at full level. Reverse reverb (recipe 10) needs a 100% wet tail with no dry at
        // all, and that is unreachable any other way.
        _mix = AddParameter("Mix", 0.3, 0, 1);

        _combs = new float[CombTuning.Length][];
        _combPos = new int[CombTuning.Length];
        _combLowpass = new float[CombTuning.Length];
        _combLength = new int[CombTuning.Length];
        _combFeedback = new float[CombTuning.Length];
        for (var c = 0; c < CombTuning.Length; c++)
            _combs[c] = new float[MaxLength(CombTuning[c])];

        _allpasses = new float[AllpassTuning.Length][];
        _allpassPos = new int[AllpassTuning.Length];
        _allpassLength = new int[AllpassTuning.Length];
        for (var a = 0; a < AllpassTuning.Length; a++)
            _allpasses[a] = new float[MaxLength(AllpassTuning[a])];

        _predelayLine = new float[(int)(MaxPredelayMs / 1000 * MaxSampleRate) + 2];
    }

    /// <summary>Buffer big enough for one tuning at the largest Velikost and the highest sample
    /// rate. All twelve lines plus the pre-delay come to about 210 kB, cloned per voice — between
    /// <see cref="DelayLineModule"/>'s 94 kB and <see cref="DelayModule"/>'s 750 kB.</summary>
    private static int MaxLength(int tuning) => (int)(tuning / TuningSampleRate * MaxSampleRate * MaxSize) + 2;

    /// <summary>Spaces for the game's world, reasoned from size and surfaces (stone keeps highs, wood
    /// and earth eat them); measured for level, not yet listened to.</summary>
    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Space("Malá místnost", "Komora, malý pokoj — krátký, hustý.", 0.4, 0.4, 0.5, 5, 0.2),
        Space("Světnice", "Dřevěná světnice — středně dlouhý, dřevo tlumí výšky.", 0.7, 0.6, 0.65, 8, 0.25),
        Space("Sklep", "Kamenný sklep — tvrdé stěny, výšky zůstávají.", 1.2, 0.7, 0.3, 12, 0.3),
        Space("Kostel", "Velká kamenná loď — dlouhý, prostorný.", 4.5, 1.4, 0.3, 30, 0.35),
        Space("Jeskyně", "Jeskyně — dlouhý, temný, výrazné předzpoždění.", 3, 1.2, 0.6, 40, 0.4),
        Space("Venku / les", "Otevřený prostor — jen náznak odrazů, silně tlumený.", 0.5, 1.5, 0.85, 25, 0.12),
    };

    private static ModulePreset Space(string name, string description, double decay, double size,
        double damping, double predelayMs, double mix)
        => new(name, description, new Dictionary<string, double>
        {
            ["Dozvuk"] = decay, ["Velikost"] = size, ["Tlumení"] = damping, ["Předzpoždění"] = predelayMs, ["Mix"] = mix,
        });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override string Description =>
        "Dozvuk (Schroeder): osm paralelních hřebenových filtrů s tlumením a za nimi čtyři " +
        "all-passy. Na rozdíl od modulu Delay, který dělá jednotlivé opakovatelné ozvěny, " +
        "je tohle hustý difuzní ocas. Dozvuk je čas poklesu o 60 dB, Velikost mění tvar prostoru " +
        "(malý = plechový, velký = sál) při stejné délce, Tlumení ubírá výškám s každým odrazem " +
        "jako v opravdové místnosti. Mix 1 = jen dozvuk bez suchého signálu — to je to, co potřebuje " +
        "obrácený dozvuk. Je MONO: šířku prostoru neudělá, délku ano.";

    public override void ResetState()
    {
        foreach (var comb in _combs) Array.Clear(comb);
        foreach (var allpass in _allpasses) Array.Clear(allpass);
        Array.Clear(_combPos);
        Array.Clear(_allpassPos);
        Array.Clear(_combLowpass);
        Array.Clear(_predelayLine);
        _predelayPos = 0;

        // Force the derived lengths to be rebuilt: a clone can be reset before its first block, and
        // a stale cache would hand it another patch's geometry.
        _cachedDecay = double.NaN;
        _cachedSize = double.NaN;
    }

    /// <summary>Below this a tail sample is −400 dB and is written as exactly zero. Measured
    /// 2026-11-14: once the input stops, the comb low-passes and all-passes ring down into subnormal
    /// floats, which the CPU handles in microcode — 25 s after one impulse the Reverb cost 4.6 % of
    /// a core against 0.9 % while it rang. The textbook Freeverb trap.</summary>
    private const float DenormalFloor = 1e-20f;

    private static float Flush(float x) => Math.Abs(x) < DenormalFloor ? 0f : x;

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var decay = GetModulatedValues(_decay, ctx, inputBuffers);
        var size = GetModulatedValues(_size, ctx, inputBuffers);
        var damping = GetModulatedValues(_damping, ctx, inputBuffers);
        var predelay = GetModulatedValues(_predelay, ctx, inputBuffers);
        var mix = GetModulatedValues(_mix, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var maxPredelaySamples = Math.Min(_predelayLine.Length - 1, (int)(MaxPredelayMs / 1000 * ctx.SampleRate));

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            UpdateGeometry(decay[i], size[i], ctx.SampleRate);

            var dry = input[i];

            // Pre-delay first: the gap between the direct sound and the start of the tail is what
            // says how far away the walls are, and it also keeps a transient audible instead of
            // being buried under its own reverb.
            _predelayLine[_predelayPos] = dry;
            var offset = Math.Clamp((int)(predelay[i] / 1000 * ctx.SampleRate), 0, maxPredelaySamples);
            var wet = _predelayLine[(_predelayPos - offset + _predelayLine.Length) % _predelayLine.Length];
            _predelayPos = (_predelayPos + 1) % _predelayLine.Length;

            var damp = (float)Math.Clamp(damping[i], 0, 1);
            var sum = 0f;

            for (var c = 0; c < _combs.Length; c++)
            {
                var line = _combs[c];
                var tap = line[(_combPos[c] - _combLength[c] + line.Length) % line.Length];

                // Lowpass the signal on its way BACK into the line, not on its way out: the loss has
                // to accumulate once per bounce, which is what makes the tail go dull gradually
                // instead of the whole reverb just sounding muffled.
                _combLowpass[c] = Flush(tap * (1 - damp) + _combLowpass[c] * damp);

                line[_combPos[c]] = wet + _combLowpass[c] * _combFeedback[c];
                _combPos[c] = (_combPos[c] + 1) % line.Length;

                sum += tap;
            }

            // Average, not sum: eight combs fed the same signal add coherently at the start of the
            // tail, so summing would make the wet path eight times the dry one before any of the
            // knobs got a say.
            wet = sum / _combs.Length;

            for (var a = 0; a < _allpasses.Length; a++)
            {
                var line = _allpasses[a];
                var delayed = line[(_allpassPos[a] - _allpassLength[a] + line.Length) % line.Length];

                // Textbook Schroeder all-pass. Flat magnitude response by construction, so these
                // four sections thicken the tail without adding any colour of their own.
                var stored = Flush(wet + AllpassGain * delayed);
                wet = delayed - AllpassGain * stored;

                line[_allpassPos[a]] = stored;
                _allpassPos[a] = (_allpassPos[a] + 1) % line.Length;
            }

            var blend = (float)Math.Clamp(mix[i], 0, 1);
            output[i] = dry * (1 - blend) + wet * blend;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    /// <summary>
    /// Turns Dozvuk and Velikost into per-comb delay lengths and feedback coefficients. A comb that
    /// bounces every <c>t</c> seconds and multiplies by <c>g</c> each time falls 60 dB after
    /// <c>t·log(0.001)/log(g)</c> seconds, so the feedback a given decay time needs is
    /// <c>g = 10^(−3t/RT60)</c> — which is why the long combs get a higher coefficient than the
    /// short ones and all eight still die together.
    ///
    /// <para>The decay is exact at DC. Above it the lowpass takes an extra bite per bounce, so a
    /// heavily damped tail ends sooner than the knob says — which is the point of damping, not an
    /// error in the arithmetic.</para>
    /// </summary>
    private void UpdateGeometry(float decaySeconds, float size, int sampleRate)
    {
        if (decaySeconds == _cachedDecay && size == _cachedSize && sampleRate == _cachedSampleRate)
            return;

        _cachedDecay = decaySeconds;
        _cachedSize = size;
        _cachedSampleRate = sampleRate;

        var scale = sampleRate / TuningSampleRate * Math.Clamp(size, MinSize, MaxSize);
        var rt60 = Math.Max(0.01, decaySeconds);

        for (var c = 0; c < CombTuning.Length; c++)
        {
            _combLength[c] = Math.Clamp((int)(CombTuning[c] * scale), 1, _combs[c].Length - 1);
            var bounceSeconds = _combLength[c] / (double)sampleRate;
            _combFeedback[c] = (float)Math.Clamp(Math.Pow(10, -3 * bounceSeconds / rt60), 0, MaxFeedback);
        }

        for (var a = 0; a < AllpassTuning.Length; a++)
            _allpassLength[a] = Math.Clamp((int)(AllpassTuning[a] * scale), 1, _allpasses[a].Length - 1);
    }
}
