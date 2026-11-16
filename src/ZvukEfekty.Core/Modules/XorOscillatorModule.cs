using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Three square-wave oscillators combined by bitwise XOR instead of mixed — a classic cheap
/// "digital chaos" generator (used in cymbal/noise circuits). Combining several inharmonic
/// square waves this way gives a dense, harsh, effectively-aperiodic texture (its true period
/// is the LCM of the three individual periods, astronomically long for non-simple frequency
/// ratios) — richer and less "clean/electronic"-sounding than a single periodic oscillator,
/// but still pitched and controllable, unlike plain noise.
/// </summary>
public sealed class XorOscillatorModule : ModuleBase
{
    private readonly Port _output;
    private readonly ModuleParameter _frequency1;
    private readonly ModuleParameter _frequency2;
    private readonly ModuleParameter _frequency3;
    private readonly ModuleParameter _amplitude;

    // Instance fields, not locals inside Process: a live Voice renders a stream of small blocks,
    // and phases living on the stack would restart at zero on every block boundary — the wave
    // would re-trigger a few hundred times a second instead of running continuously. Every other
    // oscillator keeps phase this way for the same reason (Waveform.GenerateBuffer takes `ref phase`).
    private double _phase1;
    private double _phase2;
    private double _phase3;

    public XorOscillatorModule() : base("XOR Oscillator")
    {
        _output = AddOutput("Out");

        _frequency1 = AddParameter("Frequency 1", 220, 20, 5000, "Hz");
        _frequency2 = AddParameter("Frequency 2", 311, 20, 5000, "Hz");
        _frequency3 = AddParameter("Frequency 3", 417, 20, 5000, "Hz");
        _amplitude = AddParameter("Amplitude", 0.8, 0, 1);
    }

    public override string Description =>
        "Tři čtvercové oscilátory kombinované bitovým XOR místo mixování — hustý, drsný, prakticky " +
        "neperiodický zvuk (klasický \"digital chaos\" generátor pro cimbál/šum), ale pořád laditelný.";

    public override void ResetState()
    {
        _phase1 = 0;
        _phase2 = 0;
        _phase3 = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var frequency1 = GetModulatedValues(_frequency1, ctx, inputBuffers);
        var frequency2 = GetModulatedValues(_frequency2, ctx, inputBuffers);
        var frequency3 = GetModulatedValues(_frequency3, ctx, inputBuffers);
        var amplitude = GetModulatedValues(_amplitude, ctx, inputBuffers);

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var bit1 = _phase1 < 0.5;
            var bit2 = _phase2 < 0.5;
            var bit3 = _phase3 < 0.5;
            var combined = bit1 ^ bit2 ^ bit3;

            output[i] = amplitude[i] * (combined ? 1f : -1f);

            _phase1 += frequency1[i] / ctx.SampleRate;
            _phase1 -= Math.Floor(_phase1);
            _phase2 += frequency2[i] / ctx.SampleRate;
            _phase2 -= Math.Floor(_phase2);
            _phase3 += frequency3[i] / ctx.SampleRate;
            _phase3 -= Math.Floor(_phase3);
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
