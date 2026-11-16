using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Modal resonator: a narrow-band ringing filter tuned to one resonant frequency, with an
/// explicit decay time rather than an abstract "resonance" knob — the way a struck material
/// actually rings. Feed it a short excitation (e.g. Noise gated by a fast Envelope) and it
/// keeps ringing on its own afterward. Stack a few, each tuned to a different (inharmonic)
/// frequency/decay and summed via a Mixer, for glass/wood/metal-style impact synthesis —
/// the classic "modal synthesis" approach to breaking/struck-object sounds.
/// </summary>
public sealed class ResonatorModule : ModuleBase
{
    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _frequency;
    private readonly ModuleParameter _decay;
    private readonly ModuleParameter _gain;
    private double _y1;
    private double _y2;

    public ResonatorModule() : base("Resonator")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _frequency = AddParameter("Frequency", 800, 20, 8000, "Hz");
        _decay = AddParameter("Decay", 0.3, 0.01, 4, "s", tempoSync: TempoSyncKind.Seconds);
        _gain = AddParameter("Gain", 1.0, 0, 4);
    }

    public override string Description =>
        "Modální rezonátor — úzkopásmový \"zvonící\" filtr laděný na jednu frekvenci s nastavitelnou " +
        "dobou doznívání (Decay). Nakrmte ho krátkým impulzem (např. Noise přes rychlou Envelope) " +
        "pro zvuky úderu do skla/dřeva/kovu.";

    public override void ResetState()
    {
        _y1 = 0;
        _y2 = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var frequency = GetModulatedValues(_frequency, ctx, inputBuffers);
        var decay = GetModulatedValues(_decay, ctx, inputBuffers);
        var gain = GetModulatedValues(_gain, ctx, inputBuffers);

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var freq = Math.Clamp(frequency[i], 20, ctx.SampleRate * 0.45);
            var decaySeconds = Math.Max(0.01, decay[i]);

            // Pole radius tuned so the impulse response decays ~60dB (to 1/1000) within decaySeconds.
            var r = Math.Exp(Math.Log(0.001) / (decaySeconds * ctx.SampleRate));
            var theta = 2 * Math.PI * freq / ctx.SampleRate;

            var y0 = input[i] + 2 * r * Math.Cos(theta) * _y1 - r * r * _y2;
            _y2 = _y1;
            _y1 = y0;

            // No (1-r)-style auto-normalization: with a brief excitation burst (the intended use —
            // e.g. Noise gated by a fast Envelope) the resonator never reaches steady state, so a
            // normalization sized for continuous driving would crush the output to near-silence.
            // A longer Decay legitimately rings louder, like a real struck object; use Gain to taste.
            output[i] = (float)(y0 * gain[i]);
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
