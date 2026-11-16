using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Impulse generator whose TIMING is irregular, not just its value. The gap between one impulse
/// and the next is drawn fresh each time from an exponential distribution — a Poisson process —
/// so the result is chaotic rubbing, crackling and dripping rather than the periodic modulation
/// every other source in this engine produces.
///
/// <para>This is the piece that was missing for the door-creak recipe in <c>poznamky_syntezy.md</c>:
/// the "Random" (sample and hold) shape in <see cref="Waveform"/> does draw a random VALUE, but it
/// draws it on a fixed beat, so what comes out still sounds like an LFO. Also good for fire
/// crackle, water drops, sparking, a Geiger counter.</para>
///
/// <para>Three outputs off the same event. "Trig" is a single sample at 1, for anything that only
/// needs the instant; "Gate" holds at 1 for <c>Délka gate</c> seconds, which is what an
/// <see cref="EnvelopeModule"/>'s Gate input or a Gain wants; "Síla" holds how LONG the gap before
/// this event was, which is the one thing a friction model cannot do without — see
/// <see cref="StrengthOf"/>.</para>
/// </summary>
public sealed class PoissonTriggerModule : ModuleBase
{
    private readonly Port _trigOutput;
    private readonly Port _gateOutput;
    private readonly Port _strengthOutput;
    private readonly ModuleParameter _rate;
    private readonly ModuleParameter _jitter;
    private readonly ModuleParameter _gateLength;
    private readonly SeededRandomSource _randomSource;

    /// <summary>How far the process has advanced towards the next impulse, in whole mean intervals:
    /// it grows by <c>rate * dt</c> each sample, so one unit of it is one average gap however the
    /// rate moved along the way.</summary>
    private double _progress;

    /// <summary>The drawn length of the CURRENT gap, in the same units — 1 is "exactly average".</summary>
    private double _interval;

    private int _gateSamplesLeft;

    /// <summary>Current value of the "Síla" output, held between events.</summary>
    private float _strength;

    public PoissonTriggerModule() : base("Nepravidelný spouštěč")
    {
        _trigOutput = AddOutput("Trig");
        _gateOutput = AddOutput("Gate");
        _strengthOutput = AddOutput("Síla");

        _rate = AddParameter("Četnost", 8, 0.1, 200, "1/s", tempoSync: TempoSyncKind.Rate);
        _jitter = AddParameter("Nepravidelnost", 1, 0, 1);
        _gateLength = AddParameter("Délka gate", 0.02, 0.0005, 2, "s", tempoSync: TempoSyncKind.Seconds);
        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Impulsy s náhodnými rozestupy (Poissonův proces) — praskání ohně, kapky, jiskření, " +
        "skřípání. Četnost je PRŮMĚRNÝ počet impulsů za sekundu, ne pevný takt. " +
        "Nepravidelnost 0 = přesný metronom, 1 = plná náhoda; průměrná četnost zůstává stejná. " +
        "Trig je jednovzorkový impuls, Gate drží nastavenou dobu, Síla drží 0-1 podle toho, " +
        "jak dlouhá byla mezera před impulsem (delší čekání = silnější impuls).";

    public override void ResetState()
    {
        _randomSource.Reset();
        _interval = DrawInterval(_randomSource.GetGenerator(out _), _jitter.Value);
        // Start already at the end of the first gap, so the very first sample fires. Otherwise a
        // slow rate renders a silent buffer (at 0.5/s nothing at all happens in a two-second
        // one-shot) and the module reads as broken rather than as slow.
        _progress = _interval;
        _gateSamplesLeft = 0;
        _strength = StrengthOf(0);
    }

    /// <summary>
    /// Turns the length of the gap that just ended into the 0-1 level the "Síla" output holds.
    ///
    /// <para>This exists because of how friction actually works, and because a synthesis model of
    /// it is unbuildable without it: the longer two surfaces stay stuck, the more energy the joint
    /// stores, and the louder the slip is when it finally lets go. A trigger whose every event is
    /// equally loud sounds mechanical no matter how irregular its timing is.</para>
    ///
    /// <para>The curve is taken from the creaking-door practical in Andy Farnell's <i>Designing
    /// Sound</i> (<c>sqrt(sqrt(gap/100ms + 0.1))</c>), including its saturation at 100 ms — past
    /// that a longer wait stores no more energy. The fourth root is what keeps this a shading
    /// rather than a switch: the whole span from a 1 ms gap to a saturated one is 0.56 to 1.02,
    /// about 5 dB.</para>
    /// </summary>
    private static float StrengthOf(double gapSeconds)
    {
        const double saturation = 0.1;
        var normalized = Math.Clamp(gapSeconds, 0, saturation) / saturation;
        return (float)Math.Pow(normalized + 0.1, 0.25);
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var rate = GetModulatedValues(_rate, ctx, inputBuffers);
        var jitter = GetModulatedValues(_jitter, ctx, inputBuffers);
        var gateLength = GetModulatedValues(_gateLength, ctx, inputBuffers);

        // A seed moved in the inspector invalidates the gap currently being counted down, since it
        // came out of the previous sequence — same reasoning as the LFO's held sample.
        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            _interval = DrawInterval(random, _jitter.Value);

        var trig = NewBuffer(ctx);
        var gate = NewBuffer(ctx);
        var strength = NewBuffer(ctx);
        var sampleDuration = 1.0 / ctx.SampleRate;

        // Seconds since the last event, tracked alongside the rate-relative progress because
        // "Síla" is about real elapsed time, not about how far through the drawn gap we are.
        var secondsSinceEvent = 0.0;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            if (_progress >= _interval)
            {
                _progress -= _interval;
                _interval = DrawInterval(random, jitter[i]);

                _strength = StrengthOf(secondsSinceEvent);
                secondsSinceEvent = 0;

                trig[i] = 1;
                // At least one sample, so a gate shorter than the sample period is still audible
                // as an event instead of silently doing nothing.
                _gateSamplesLeft = Math.Max(1, (int)Math.Round(Math.Max(0, gateLength[i]) * ctx.SampleRate));
            }

            if (_gateSamplesLeft > 0)
            {
                gate[i] = 1;
                _gateSamplesLeft--;
            }

            strength[i] = _strength;
            secondsSinceEvent += sampleDuration;

            // Advancing by the INSTANTANEOUS rate is what lets Četnost be modulated: the gap was
            // drawn in mean intervals rather than in seconds, so speeding the process up mid-gap
            // shortens the gap that is already running instead of only the ones after it.
            _progress += Math.Max(0, rate[i]) * sampleDuration;
        }

        return new Dictionary<Port, float[]>
        {
            [_trigOutput] = trig,
            [_gateOutput] = gate,
            [_strengthOutput] = strength,
        };
    }

    /// <summary>Length of the next gap, in mean intervals — see
    /// <see cref="IrregularTiming.DrawInterval"/>, which the granulator's own Nepravidelnost knob
    /// shares so the two read the same.</summary>
    private static double DrawInterval(Random random, double jitter)
        => IrregularTiming.DrawInterval(random, jitter);
}
