using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>Scales the incoming signal's amplitude; Gain itself can be modulated.</summary>
public sealed class GainModule : ModuleBase
{
    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _gain;

    public GainModule() : base("Gain")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _gain = AddParameter("Gain", 1.0, 0, 2);
    }

    public override string Description => "Škáluje hlasitost procházejícího signálu — jednoduché zesílení/zeslabení.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var gain = GetModulatedValues(_gain, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        for (var i = 0; i < ctx.SampleCount; i++)
            output[i] = input[i] * gain[i];

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
