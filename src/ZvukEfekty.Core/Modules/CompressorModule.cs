using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Holds a signal's loudness together: measures what comes in, and turns the gain down by a stated
/// amount whenever the level is over a stated threshold. The last item of todo.md 12a, and the one
/// that aims straight at what this project is for — the difference between a correct render and a
/// sample that survives being dropped into a sampler bank next to commercial material.
///
/// <para><b>This is a feed-forward compressor, and that is why it can exist at all.</b> The
/// roadmap said for a while that a compressor was impossible here because the graph forbids cycles
/// and "the output cannot drive its own attenuation". True of a FEEDBACK compressor; a feed-forward
/// one measures the signal BEFORE the point it acts on, which is a fan-out, not a cycle. Measured
/// 2026-09-06: the same thing patched by hand out of <see cref="EnvelopeFollowerModule"/>, a
/// <see cref="WaveshaperModule"/> and a <see cref="GainModule"/> compresses correctly, tracking a
/// drawn gain curve to within 0.05 dB.</para>
///
/// <para><b>So what this module adds is not the ability, it is the units.</b> The patched version
/// asks for the gain curve to be DRAWN, in linear level: threshold and ratio are not numbers there
/// but a shape, only the upper half of the drawing area is reachable (a waveshaper maps −1…1 and a
/// detector reads 0…1), and a curve drawn to look like 4:1 measured as 2,00:1 — a trap that caught
/// the author of this file. Here they are knobs in dB, which is the only unit in which a
/// compression ratio is a straight line and therefore the only one a person can aim.</para>
///
/// <para><b>Attack and release act on the GAIN, not on the level</b>, which is what separates this
/// from wiring a follower into a Gain. The target gain is recomputed from every sample with no
/// smoothing at all; what is smoothed is the reduction on its way to that target. That is what
/// makes the times mean what their names say — how fast the compressor grabs, and how fast it lets
/// go — and it removes the follower's ripple problem for free, because between two peaks of a wave
/// the release is what holds the gain down, and a release of tens of milliseconds does not notice a
/// gap of five.</para>
///
/// <para><b>Předstih (look-ahead) is off by default, on purpose.</b> It works by delaying the audio
/// while the detector keeps looking at it undelayed, so the gain is already down when the peak
/// lands — the one thing no attack time can do. It is not free: the whole signal leaves this module
/// that much later, and a one-shot exported for a sampler bank with 5 ms of new silence in front of
/// it has had its cue point moved. Measured, the case for it is narrower than it looks: with no
/// look-ahead at all, an attack of 0,5 ms already holds a step to 0,22 dB over the settled level,
/// and the distortion that a fast attack is supposed to cause is −45 dB against −51 dB at a 60×
/// slower one. Reach for it when a peak must not get through, not to make anything punchier — it
/// does the opposite, since a transient the compressor sees coming is a transient it flattens.</para>
/// </summary>
public sealed class CompressorModule : ModuleBase
{
    /// <summary>Same 90% convention as <see cref="SlewModule"/> and <see cref="EnvelopeFollowerModule"/>:
    /// the time on the knob is how long it takes to cover this much of the distance in front of it.
    /// Deliberately the same number in all three boxes — someone who has learned what 0,05 s does on
    /// one of them should not have to learn it again here.</summary>
    private const double ConvergenceFraction = 0.9;

    /// <summary>Longest look-ahead offered, and the size the delay line is built for. 20 ms is far
    /// past the point where the delay itself is the bigger problem than the peak it catches.</summary>
    private const double MaxLookaheadMs = 20;

    /// <summary>Floor under the level before its logarithm is taken, i.e. −120 dB. Silence has no
    /// logarithm, and a compressor asked for one on a signal that has not started yet would take
    /// its detector to negative infinity.</summary>
    private const double LevelFloor = 1e-6;

    private const double LnTenOverTwenty = 2.302585092994046 / 20.0;

    private readonly Port _input;
    private readonly Port _output;
    private readonly Port _gainOutput;
    private readonly ModuleParameter _threshold;
    private readonly ModuleParameter _ratio;
    private readonly ModuleParameter _knee;
    private readonly ModuleParameter _attack;
    private readonly ModuleParameter _release;
    private readonly ModuleParameter _lookahead;
    private readonly ModuleParameter _makeup;

    // The look-ahead delay line, built once at the maximum length rather than resized whenever the
    // knob moves: the knob is a read offset into it, so changing it costs nothing and allocates
    // nothing in the render loop.
    private float[] _delay = Array.Empty<float>();
    private int _writeIndex;

    /// <summary>Reduction currently applied, in dB and never above zero. Held in dB because that is
    /// the domain attack and release are smoothing in — a fixed time per dB is what makes a
    /// compressor sound the same on a quiet passage as on a loud one.</summary>
    private double _reductionDb;

    // Time-to-coefficient conversions, cached the same way and for the same reason as in
    // EnvelopeFollower: Math.Pow dwarfs the rest of the loop, and this picks between grabbing and
    // letting go on every sample, so one slot would be evicted at every direction change.
    private double _cachedAttackSeconds = double.NaN;
    private double _cachedReleaseSeconds = double.NaN;
    private double _cachedAttackKeep;
    private double _cachedReleaseKeep;
    private int _cachedSampleRate;

    public CompressorModule() : base("Kompresor")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");
        _gainOutput = AddOutput("Zisk");

        _threshold = AddParameter("Práh", -18, -60, 0, "dB");
        _ratio = AddParameter("Poměr", 4, 1, 20);
        _knee = AddParameter("Koleno", 6, 0, 24, "dB");
        _attack = AddParameter("Nástup", 0.01, 0, 1, "s", tempoSync: TempoSyncKind.Seconds);
        _release = AddParameter("Dozvuk", 0.1, 0, 2, "s", tempoSync: TempoSyncKind.Seconds);
        _makeup = AddParameter("Vyrovnání", 0, 0, 24, "dB");

        // Not modulatable: the look-ahead is how long the delay line is, not a value read per
        // sample. A signal driving it would be asking the module to be a different length of module
        // at every sample, and what a listener would hear is the read head jumping about.
        _lookahead = AddParameter("Předstih", 0, 0, MaxLookaheadMs, "ms", modulatable: false);
    }

    public override string Description =>
        "Srovná hlasitost: co je nad Prahem, ztlumí v poměru, který nastavíš — 4:1 znamená, že " +
        "z každých 4 dB nad prahem projde 1 dB. Koleno prahu ohýbá, takže se komprese nezapíná " +
        "skokem. Nástup je, jak rychle chytne, Dozvuk jak rychle pustí; krátký nástup drží špičky, " +
        "delší pustí úder napřed a srovná až tělo zvuku. Vyrovnáním se vrátí hlasitost, kterou " +
        "komprese ubrala. Výstup Zisk vydává samotné zeslabení (1 = neztlumeno) — zapoj ho do " +
        "Gainu jiné větve a máš ducking: co projde kompresorem, uhýbá podkladu z cesty. Předstih " +
        "nech na nule, dokud přes tebe nepropadají špičky: zdrží celý signál o svou hodnotu.";

    public override void ResetState()
    {
        Array.Clear(_delay);
        _writeIndex = 0;
        _reductionDb = 0;
        _cachedAttackSeconds = double.NaN;
        _cachedReleaseSeconds = double.NaN;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var threshold = GetModulatedValues(_threshold, ctx, inputBuffers);
        var ratio = GetModulatedValues(_ratio, ctx, inputBuffers);
        var knee = GetModulatedValues(_knee, ctx, inputBuffers);
        var attack = GetModulatedValues(_attack, ctx, inputBuffers);
        var release = GetModulatedValues(_release, ctx, inputBuffers);
        var makeup = GetModulatedValues(_makeup, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var gains = NewBuffer(ctx);

        if (ctx.SampleRate != _cachedSampleRate)
        {
            _cachedSampleRate = ctx.SampleRate;
            _cachedAttackSeconds = double.NaN;
            _cachedReleaseSeconds = double.NaN;
            _delay = new float[Math.Max(1, (int)Math.Round(MaxLookaheadMs / 1000 * ctx.SampleRate)) + 1];
            _writeIndex = 0;
        }

        var lookaheadSamples = Math.Clamp(
            (int)Math.Round(_lookahead.Value / 1000 * ctx.SampleRate), 0, _delay.Length - 1);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // The delay line is written and read even at zero look-ahead, where it hands back the
            // sample just written. One branch fewer in the loop, and turning the knob to zero then
            // gives back exactly the undelayed signal rather than something one sample off.
            _delay[_writeIndex] = input[i];
            var readIndex = _writeIndex - lookaheadSamples;
            if (readIndex < 0)
                readIndex += _delay.Length;
            var delayed = _delay[readIndex];
            _writeIndex = _writeIndex + 1 == _delay.Length ? 0 : _writeIndex + 1;

            // Detector on the UNDELAYED sample: that is the whole of what look-ahead is. With the
            // knob at zero the two are the same sample and this costs nothing.
            var targetDb = TargetReductionDb(Math.Abs((double)input[i]), threshold[i], ratio[i], knee[i]);

            // Attack pulls the gain DOWN (more reduction), release lets it back up. Which one is
            // running is decided by the direction, not by whether the signal is over the threshold,
            // so a compressor that is already holding 6 dB and is asked for 9 attacks the extra 3.
            var grabbing = targetDb < _reductionDb;
            var seconds = Math.Max(0, grabbing ? attack[i] : release[i]);
            if (seconds <= 0)
            {
                _reductionDb = targetDb;
            }
            else
            {
                double keep;
                if (grabbing)
                {
                    if (seconds != _cachedAttackSeconds)
                    {
                        _cachedAttackSeconds = seconds;
                        _cachedAttackKeep = KeepFactor(seconds, ctx.SampleRate);
                    }

                    keep = _cachedAttackKeep;
                }
                else
                {
                    if (seconds != _cachedReleaseSeconds)
                    {
                        _cachedReleaseSeconds = seconds;
                        _cachedReleaseKeep = KeepFactor(seconds, ctx.SampleRate);
                    }

                    keep = _cachedReleaseKeep;
                }

                _reductionDb = targetDb + (_reductionDb - targetDb) * keep;
            }

            // exp(x * ln10/20) rather than Pow(10, x/20): same number, and Pow is the one thing in
            // this loop that would be worth its own line in a profile.
            var reduction = _reductionDb >= 0 ? 1.0 : Math.Exp(_reductionDb * LnTenOverTwenty);

            // Zisk carries the reduction WITHOUT the makeup. It exists to duck something else, and
            // a ducking signal that also carried this module's makeup would raise the other branch
            // every time this one was turned up — which is not what the wire is for.
            gains[i] = (float)reduction;

            var makeupDb = makeup[i];
            var gain = makeupDb == 0 ? reduction : reduction * Math.Exp(makeupDb * LnTenOverTwenty);
            output[i] = (float)(delayed * gain);
        }

        return new Dictionary<Port, float[]>
        {
            [_output] = output,
            [_gainOutput] = gains,
        };
    }

    /// <summary>
    /// The static curve: how many dB of reduction this level asks for, before any attack or release
    /// has had a say. Zero or negative, never positive — a compressor makes things quieter and the
    /// makeup is a separate knob on purpose, so that turning the ratio up cannot make a passage
    /// louder than it was.
    /// </summary>
    private static double TargetReductionDb(double level, double thresholdDb, double ratio, double kneeDb)
    {
        // Below the knee there is nothing to compute, and skipping the logarithm here is worth
        // doing rather than tidy: most of most signals is under the threshold, and this turns those
        // samples into a comparison.
        var kneeWidth = Math.Max(0, kneeDb);
        var quietEnoughDb = thresholdDb - kneeWidth / 2;
        if (level <= LevelFloor || level <= Math.Exp(quietEnoughDb * LnTenOverTwenty))
            return 0;

        var levelDb = Math.Log10(level) * 20;
        var over = levelDb - thresholdDb;

        // 1 dB in, 1/ratio dB out: the slope of everything above the threshold, and the fraction
        // that gets thrown away is what is left of it.
        var slope = 1 - 1 / Math.Max(1, ratio);

        if (over >= kneeWidth / 2)
            return -slope * over;

        // Inside the knee. The quadratic is the curve whose value AND slope match the two straight
        // lines it joins, which is what makes a soft knee inaudible as it engages rather than
        // merely gradual: a join that matched only the values would still corner.
        var into = over + kneeWidth / 2;
        return -slope * into * into / (2 * kneeWidth);
    }

    /// <summary>Fraction of the remaining distance kept per sample, such that after
    /// <paramref name="seconds"/> only <c>1 - ConvergenceFraction</c> of it is left.</summary>
    private static double KeepFactor(double seconds, int sampleRate)
        => Math.Pow(1 - ConvergenceFraction, 1.0 / (seconds * sampleRate));
}
