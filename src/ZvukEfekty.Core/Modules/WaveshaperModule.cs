using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Distortion by a hand-drawn transfer function: the curve says, for every input level, what the
/// output level is.
///
/// A separate module rather than another Type on <see cref="DistortionModule"/>, because Drive
/// means something different here. On Distortion, Drive pushes the signal into a fixed shape and is
/// the only way to change how hard it bites; here the shape IS the control, and Drive only decides
/// how much of it the signal travels across. Folding the two together would leave one knob whose
/// meaning depends on another knob.
///
/// This is also the simplest of the three curve uses (see todo.md section 2): no time, no gate, no
/// sustain point — just y = f(x) — which makes it the one that proves the curve, its persistence
/// and its lookup table without any of the envelope's semantics on top.
/// </summary>
public sealed class WaveshaperModule : ModuleBase
{
    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _drive;
    private readonly ModuleParameter _mix;
    private readonly CurveInput _shape;

    public WaveshaperModule() : base("Waveshaper")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _drive = AddParameter("Drive", 1, 0.1, 20);
        _mix = AddParameter("Mix", 1, 0, 1);

        // Identity by default: a freshly dropped module has to pass its input through unchanged.
        // Anything else would make adding one to a working patch look like it broke the sound.
        // The identity while nothing is patched in: a fresh Waveshaper should pass the signal
        // through unchanged, not silence it or colour it before anything has been drawn.
        _shape = AddCurveInput("Tvar", Curve.Identity(), CurveOutput.Bipolar);
    }

    public override string Description =>
        "Zkreslení podle nakreslené převodní křivky — pro každou vstupní úroveň říká křivka " +
        "výstupní. Drive žene signál dál po křivce, Mix prolíná mezi čistým a tvarovaným.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var drive = GetModulatedValues(_drive, ctx, inputBuffers);
        var mix = GetModulatedValues(_mix, ctx, inputBuffers);

        // Read once per block, not per sample: the curve is immutable, so the table behind it
        // cannot change mid-buffer, and hoisting it out keeps the inner loop to a lerp.
        var shape = _shape.Curve;
        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var driven = input[i] * drive[i];

            // -1..1 is mapped onto the curve's 0..1 X axis. Beyond that the curve holds its end
            // value (see Curve.Evaluate), so an overdriven signal flattens against the shape's
            // own edge instead of wrapping or running away.
            var shaped = shape.Sample((driven + 1) * 0.5);
            var wet = Math.Clamp(mix[i], 0f, 1f);

            output[i] = input[i] * (1 - wet) + shaped * wet;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
