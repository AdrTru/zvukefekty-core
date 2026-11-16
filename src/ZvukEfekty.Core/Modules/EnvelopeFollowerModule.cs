using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Turns a signal into its own loudness: rectify, then smooth. The one thing this engine could not
/// do before is let a sound control itself — auto-wah, ducking, "a harder hit opens the filter" —
/// because every control signal came from a generator that has no idea what the audio is doing.
///
/// <para><b>Gate and Trig are the comparator</b>, which todo.md 12c calls the only bridge from
/// audio back into triggering. It is bolted on here rather than sold separately because a
/// comparator on raw audio is useless: audio crosses any threshold thousands of times a second, so
/// the rectify-and-smooth in front of it is not a convenience but the part that makes the
/// comparison mean anything. Downstream the gate feeds an <see cref="EnvelopeModule"/>'s Gate, a
/// <see cref="SampleHoldModule"/>'s Trig, or a Gain, like any other gate in this engine.</para>
///
/// <para>The times use the same convention as <see cref="SlewModule"/> — seconds to cover 90% of
/// the distance in front of them — so a number means the same thing in both boxes. What makes this
/// module more than a Slew with abs() in front is the pair of outputs after the detector; the
/// detector itself really is that small.</para>
/// </summary>
public sealed class EnvelopeFollowerModule : ModuleBase
{
    /// <summary>Same 90% as <see cref="SlewModule"/>, and deliberately the same number: the two
    /// modules smooth with identical arithmetic, and someone who has learned what 0,05 s does on
    /// one of them should not have to learn it again on the other.</summary>
    private const double ConvergenceFraction = 0.9;

    /// <summary>How far the level has to fall back below <c>Práh</c> before the gate closes, as a
    /// fraction of it. Without it the gate chatters, because the level always carries some ripple
    /// at twice the input's frequency — 0,48% of the level on a 100 Hz sine even at half-second
    /// times, and more as the times shorten. Measured on a signal whose level settles exactly on
    /// the threshold: 167 openings in two seconds without this, one with it. 10% clears the ripple
    /// of anything above a few tens of hertz while staying far below the levels a threshold is
    /// normally set between. Fixed rather than a knob for the same reason as
    /// <see cref="SampleHoldModule"/>'s trigger threshold — a number with only one sensible setting
    /// is not a decision worth handing over.</summary>
    private const double GateHysteresis = 0.9;

    private readonly Port _input;
    private readonly Port _output;
    private readonly Port _gateOutput;
    private readonly Port _trigOutput;
    private readonly ModuleParameter _attack;
    private readonly ModuleParameter _release;
    private readonly ModuleParameter _threshold;

    private double _level;
    private bool _gateOpen;

    // Time-to-coefficient conversions, cached because Math.Pow dwarfs everything else in the loop.
    // TWO caches, unlike Slew's one: this module picks between rise and fall on every sample, so a
    // single slot would be evicted at every direction change. Measured, those are far more frequent
    // than a glide's: 1 759 per second on a 440 Hz sine, 16 000 on a 4 kHz one, and on white noise
    // 37% of all samples at 10/100 ms and 51% at 0,5/0,5 ms. Two slots turn all of that into two
    // Math.Pow calls for the whole render, as long as the knobs hold still.
    private double _cachedAttackSeconds = double.NaN;
    private double _cachedReleaseSeconds = double.NaN;
    private double _cachedAttackKeep;
    private double _cachedReleaseKeep;
    private int _cachedSampleRate;

    public EnvelopeFollowerModule() : base("Sledovač obálky")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");
        _gateOutput = AddOutput("Gate");
        _trigOutput = AddOutput("Trig");

        _attack = AddParameter("Nástup", 0.01, 0, 1, "s", tempoSync: TempoSyncKind.Seconds);
        _release = AddParameter("Dozvuk", 0.1, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
        _threshold = AddParameter("Práh", 0.1, 0, 1);
    }

    public override string Description =>
        "Změří hlasitost signálu a vydá ji jako řídicí signál — zvuk tak může řídit sám sebe: " +
        "auto-wah (hlasitější úder otevře filtr), ducking (bicí ztlumí podklad), dynamické " +
        "zkreslení. Nástup je čas, za který se obálka vyšplhá za hlasitostí, Dozvuk čas, za který " +
        "ji zase pouští; krátký nástup a dlouhý dozvuk sleduje špičky, obojí dlouhé měří průměrnou " +
        "hlasitost. Gate se otevře, když obálka překročí Práh, a Trig v tu chvíli vydá jeden " +
        "impuls — to je jediná cesta, jak ze zvuku udělat spouštěč pro Obálku nebo Vzorkovač. " +
        "Pozor: obálka sinusovky o amplitudě 1 se ustálí na 0,64, ne na 1.";

    public override void ResetState()
    {
        _level = 0;
        _gateOpen = false;
        _cachedAttackSeconds = double.NaN;
        _cachedReleaseSeconds = double.NaN;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var attack = GetModulatedValues(_attack, ctx, inputBuffers);
        var release = GetModulatedValues(_release, ctx, inputBuffers);
        var threshold = GetModulatedValues(_threshold, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var gate = NewBuffer(ctx);
        var trig = NewBuffer(ctx);

        if (ctx.SampleRate != _cachedSampleRate)
        {
            _cachedSampleRate = ctx.SampleRate;
            _cachedAttackSeconds = double.NaN;
            _cachedReleaseSeconds = double.NaN;
        }

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Rectified, not squared. |x| is what keeps one knob usable on audio and on a control
            // signal alike: a squared detector would read a control signal of 0,2 as 0,04, and the
            // depth needed at the destination would then depend on how loud the source happens
            // to be rather than on what it is wired to.
            var target = Math.Abs((double)input[i]);

            var rising = target > _level;
            var seconds = Math.Max(0, rising ? attack[i] : release[i]);
            if (seconds <= 0)
            {
                _level = target;
            }
            else
            {
                double keep;
                if (rising)
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

                _level = target + (_level - target) * keep;
            }

            output[i] = (float)_level;

            // Strictly above, so that a Práh of 0 on silence leaves the gate shut instead of
            // latching it open for the whole render — the one setting where "exactly at the
            // threshold" has to count as closed rather than open.
            var open = Math.Max(0, (double)threshold[i]);
            if (_gateOpen)
            {
                if (_level <= open * GateHysteresis)
                    _gateOpen = false;
            }
            else if (_level > open)
            {
                _gateOpen = true;
                trig[i] = 1;
            }

            gate[i] = _gateOpen ? 1 : 0;
        }

        return new Dictionary<Port, float[]>
        {
            [_output] = output,
            [_gateOutput] = gate,
            [_trigOutput] = trig,
        };
    }

    /// <summary>Fraction of the remaining distance kept per sample, such that after
    /// <paramref name="seconds"/> only <c>1 - ConvergenceFraction</c> of it is left.</summary>
    private static double KeepFactor(double seconds, int sampleRate)
        => Math.Pow(1 - ConvergenceFraction, 1.0 / (seconds * sampleRate));
}
