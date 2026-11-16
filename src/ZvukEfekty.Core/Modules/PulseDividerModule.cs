using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Counts incoming impulses and lets every N-th one through — the piece that turns one pulse
/// stream into several rhythms that cannot drift apart.
///
/// <para><b>Why this and not a Clock module.</b> `todo.md` 12a asks for "hodiny a dělič", and half
/// of that turned out to be here already: <see cref="PoissonTriggerModule"/> with Nepravidelnost
/// at 0 is an exact metronome — <see cref="IrregularTiming.DrawInterval"/> returns a constant 1,
/// so the gap is exactly 1/Četnost, and Četnost is tempo-syncable. Measured on a 4/s stream: every
/// gap 11025 samples, no drift over 40 pulses. A second module with a period knob would have been
/// the same counter under a different name. What nothing here could do is relate two rhythms to
/// each other, and that is the counter below.</para>
///
/// <para><b>Why relating them matters, when two triggers at 4/s and 2/s already line up.</b> They
/// line up only while nobody touches them: both start at sample 0 and both count in their own
/// units, so a modulated rate, a different seed or a tempo change moves one and not the other. A
/// divider has no rate of its own — it can only fire on a pulse that already happened — so its
/// output is locked to its input by construction, at every tempo and under any modulation. And
/// <see cref="_offset"/> is what a second oscillator could never give: two dividers of 4 at offsets
/// 0 and 2 interleave on the same grid, which is a backbeat.</para>
///
/// <para><b>The input does not have to be regular.</b> Every third raindrop from
/// <see cref="PoissonTriggerModule"/> is a thinner, still irregular stream — the divider counts
/// events, not time.</para>
/// </summary>
public sealed class PulseDividerModule : ModuleBase
{
    /// <summary>Where an impulse counts as having arrived. The same fixed 0.5 as
    /// <see cref="SampleHoldModule"/>, and for the same reason: everything that emits a trigger in
    /// this engine emits 0 or 1.</summary>
    private const double TriggerThreshold = 0.5;

    private readonly Port _input;
    private readonly Port _reset;
    private readonly Port _trigOutput;
    private readonly Port _gateOutput;
    private readonly ModuleParameter _divisor;
    private readonly ModuleParameter _offset;
    private readonly ModuleParameter _gateLength;

    /// <summary>Which impulse of the current cycle the next one will be. Zero after a reset, so
    /// the pulse right after a reset is the one offset 0 lets through.</summary>
    private int _count;

    private double _previousTrigger;
    private double _previousReset;
    private int _gateSamplesLeft;

    public PulseDividerModule() : base("Dělič impulsů")
    {
        _input = AddInput("Trig");
        _reset = AddInput("Reset");

        _trigOutput = AddOutput("Trig");
        _gateOutput = AddOutput("Gate");

        _divisor = AddParameter("Dělitel", 4, 1, 16);

        // Which impulse of the cycle passes. Bounded by the largest divisor rather than by the
        // current one, because the divisor can be modulated and a knob whose range moves under the
        // hand is worse than one that is sometimes larger than it needs to be — an offset past the
        // divisor wraps (it is read modulo), it does not go silent.
        _offset = AddParameter("Posun", 0, 0, 15);

        // In seconds and tempo-syncable, exactly like PoissonTriggerModule's gate: the divider
        // cannot express this as a fraction of its own period, because it does not have one. It
        // only ever knows that an impulse just happened.
        _gateLength = AddParameter("Délka gate", 0.05, 0.0005, 2, "s", tempoSync: TempoSyncKind.Seconds);
    }

    public override string Description =>
        "Počítá impulsy na vstupu Trig a pustí dál každý N-tý — z jedněch hodin udělá pomalejší, " +
        "které se s nimi nemůžou rozejít. Posun říká, KTERÝ impuls cyklu projde: dva děliče čtyřmi " +
        "s posunem 0 a 2 se střídají, tedy protiváha. Reset vrátí počítadlo na začátek, takže další " +
        "impuls je zase první. Hodiny k tomu je Nepravidelný spouštěč s Nepravidelností 0 — pak je " +
        "to přesný metronom a Četnost se dá navázat na tempo. Vstup ale nemusí být pravidelný: " +
        "z nepravidelného spouštěče projde každá třetí kapka. Dělitel i Posun se zaokrouhlují.";

    public override void ResetState()
    {
        _count = 0;
        _previousTrigger = 0;
        _previousReset = 0;
        _gateSamplesLeft = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var trigger = GetInputOrSilence(inputBuffers, _input, ctx);
        var reset = GetInputOrSilence(inputBuffers, _reset, ctx);
        var divisor = GetModulatedValues(_divisor, ctx, inputBuffers);
        var offset = GetModulatedValues(_offset, ctx, inputBuffers);
        var gateLength = GetModulatedValues(_gateLength, ctx, inputBuffers);

        var trig = NewBuffer(ctx);
        var gate = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Reset first, so a reset and an impulse landing on the same sample mean "this impulse
            // is the first one" rather than "this impulse is counted and then forgotten".
            if (reset[i] >= TriggerThreshold && _previousReset < TriggerThreshold)
                _count = 0;
            _previousReset = reset[i];

            if (trigger[i] >= TriggerThreshold && _previousTrigger < TriggerThreshold)
            {
                var n = Math.Max(1, (int)Math.Round(divisor[i]));
                var wanted = ((int)Math.Round(offset[i]) % n + n) % n;

                if (_count % n == wanted)
                {
                    trig[i] = 1;
                    // At least one sample, so a gate shorter than the sample period is still an
                    // event instead of nothing — same call PoissonTriggerModule makes.
                    _gateSamplesLeft = Math.Max(1, (int)Math.Round(Math.Max(0, gateLength[i]) * ctx.SampleRate));
                }

                // Counted modulo the divisor that was in force at this impulse, so a modulated
                // divisor changes the cycle from here on instead of leaving the counter beyond it.
                _count = (_count + 1) % n;
            }
            _previousTrigger = trigger[i];

            if (_gateSamplesLeft > 0)
            {
                gate[i] = 1;
                _gateSamplesLeft--;
            }
        }

        return new Dictionary<Port, float[]>
        {
            [_trigOutput] = trig,
            [_gateOutput] = gate,
        };
    }
}
