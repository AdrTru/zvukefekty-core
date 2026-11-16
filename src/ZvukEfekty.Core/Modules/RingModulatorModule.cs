using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>Multiplies two audio signals sample-by-sample — classic sci-fi/robotic tone.</summary>
public sealed class RingModulatorModule : ModuleBase
{
    private readonly Port _input;
    private readonly Port _modulator;
    private readonly Port _output;
    private readonly ModuleParameter _mix;

    public RingModulatorModule() : base("Ring Modulator")
    {
        _input = AddInput("In");
        _modulator = AddInput("Modulator");
        _output = AddOutput("Out");

        _mix = AddParameter("Mix", 1.0, 0, 1);
    }

    public override string Description => "Násobí dva signály vzorek po vzorku — klasický kovový/robotický zvuk.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var modulator = GetInputOrSilence(inputBuffers, _modulator, ctx);
        var mix = GetModulatedValues(_mix, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var ringed = input[i] * modulator[i];
            output[i] = input[i] * (1 - mix[i]) + ringed * mix[i];
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
