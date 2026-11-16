using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// State-variable filter (topology-preserving transform, trapezoidal integration): produces
/// low-pass, high-pass or band-pass output from the same core loop, with modulatable cutoff and
/// resonance.
///
/// This replaced a Chamberlin SVF, which was not a tuning change but a bug fix. Chamberlin is only
/// stable while <c>2·sin(pi·fc/SR) + 1/Resonance &lt; 2</c>, and the knob let you walk straight past
/// that: at Resonance 0.5 the old filter blew up to NaN from 6 kHz upwards, at Resonance 1 from
/// 10 kHz — both inside the 20-8000 Hz range it advertised. A patch that hit it went silent or
/// exported garbage, with nothing on screen to say why. Measured before the swap, at 44.1 kHz:
///
/// <code>
///   Cutoff | Res 0.5   Res 1   Res 2   Res 10
///     4000 |   ok        ok      ok      ok
///     6000 |  4.9e16     ok      ok      ok
///     8000 |   NaN       ok      ok      ok
///    12000 |   NaN      NaN      ok      ok
///    16000 |   NaN      NaN     NaN      ok
/// </code>
///
/// The trapezoidal form below is unconditionally stable for any cutoff below Nyquist at any
/// resonance, which is what makes the 16 kHz ceiling honest rather than a NaN waiting to happen.
///
/// What this does to an existing patch, measured rather than assumed: the passband, the cutoff
/// point and the height of the resonant peak are unchanged — at the cutoff itself the two agree to
/// 0.00 dB, and a Resonance of 3.5 peaks at 10.88 dB either way. Everything that moved is in the
/// STOPBAND, where the old one stopped rolling off and leaked:
///
/// <code>
///   Cutoff 2600 Res 1     at 8 kHz: old -16.2 dB, new -21.0 dB    at 15 kHz: -24.9 vs -39.5
///   Cutoff 5500 Res 0.8   at 8 kHz: old  -3.5 dB, new  -7.6 dB    at 15 kHz:  -7.0 vs -25.7
/// </code>
///
/// So a patch keeps its character and loses high end it was never meant to pass — most audible on
/// noise sources with a high cutoff, inaudible on a low one (at Cutoff 250 the two match within
/// 0.2 dB everywhere below 1 kHz).
///
/// Resonance keeps its old meaning — it is 1/damping either way, so a saved patch loads with the
/// same number and the same intent.
/// </summary>
public sealed class FilterModule : ModuleBase
{
    /// <summary>New types go on the END of this list. The index is what a .zvuky file stores, so
    /// inserting one anywhere else would silently re-point every saved Type at a different
    /// response — the same rule as <see cref="Waveform.CustomIndex"/>.</summary>
    private static readonly string[] TypeNames = { "Low-pass", "High-pass", "Band-pass", "Notch", "Peak", "All-pass" };

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _type;
    private readonly ModuleParameter _cutoff;
    private readonly ModuleParameter _resonance;

    /// <summary>The two integrator states. Named for what they are in the trapezoidal form — the
    /// equivalent capacitor voltages — rather than "low" and "band", because neither one is an
    /// output on its own here.</summary>
    private double _ic1eq;
    private double _ic2eq;

    public FilterModule() : base("Filter")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _type = AddParameter("Type", 0, 0, TypeNames.Length - 1, options: TypeNames);

        // Up to 16 kHz, not the 8 kHz this used to stop at. A riser is a cutoff sweep and every
        // recipe for one ends around 10 kHz (dj_zvuky_zdroje/recepty/01) — at 8 kHz the top of the
        // sweep has no air left in it. Only worth raising once the topology above could survive it.
        _cutoff = AddParameter("Cutoff", 1000, 20, 16000, "Hz");
        _resonance = AddParameter("Resonance", 1, 0.5, 10);
    }

    public override string Description =>
        "Filtr se stavovým prostorem s laditelným mezním kmitočtem (Cutoff) a rezonancí. " +
        "Dolní, horní a pásmová propust propouštějí jen část pásma; Notch naopak jednu " +
        "frekvenci vyřízne (na sweepu zní jako phaser), Peak ji zvýrazní a zbytek nechá být " +
        "(Resonance je tu výška zvonu, ne strmost — při 10 je to +26 dB) a All-pass propustí " +
        "všechno beze změny hlasitosti a otočí jen fázi. Stabilní na celém rozsahu Cutoffu " +
        "i při nejnižší Resonance.";

    public override void ResetState()
    {
        _ic1eq = 0;
        _ic2eq = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var cutoff = GetModulatedValues(_cutoff, ctx, inputBuffers);
        var resonance = GetModulatedValues(_resonance, ctx, inputBuffers);
        var type = (int)_type.Value;

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Just short of Nyquist, where tan runs off to infinity. Stability no longer depends on
            // this clamp — only the arithmetic staying finite does. Modulation is not limited to the
            // knob's range (see ModuleBase.GetModulatedValues), so a deep sweep does reach here.
            var clampedCutoff = Math.Clamp(cutoff[i], 20, ctx.SampleRate * 0.49);

            var g = Math.Tan(Math.PI * clampedCutoff / ctx.SampleRate);
            var k = 1.0 / Math.Max(0.5, resonance[i]);

            var a1 = 1.0 / (1.0 + g * (g + k));
            var a2 = g * a1;
            var a3 = g * a2;

            var v0 = (double)input[i];
            var v3 = v0 - _ic2eq;
            var v1 = a1 * _ic1eq + a2 * v3;
            var v2 = _ic2eq + a2 * _ic1eq + a3 * v3;

            _ic1eq = 2 * v1 - _ic1eq;
            _ic2eq = 2 * v2 - _ic2eq;

            // The last three are the same state read differently, not extra filters: notch is
            // low+high, peak is low-high, all-pass is the same magnitude everywhere with the phase
            // rotated. Nothing above this line changes, so they cost three lines and no arithmetic.
            output[i] = (float)(type switch
            {
                0 => v2,                        // low
                1 => v0 - k * v1 - v2,          // high
                2 => v1,                        // band
                3 => v0 - k * v1,               // notch  = low + high
                4 => k * v1 + 2 * v2 - v0,      // peak   = low - high
                _ => v0 - 2 * k * v1            // all-pass
            });
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
