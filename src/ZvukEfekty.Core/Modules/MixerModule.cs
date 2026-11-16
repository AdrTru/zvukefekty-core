using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>Combines up to four incoming audio signals into one, with a choice of combining mode.</summary>
public sealed class MixerModule : ModuleBase
{
    private static readonly string[] ModeNames = { "Sum", "Average", "Multiply" };

    private readonly Port[] _inputs;
    private readonly Port _output;
    private readonly ModuleParameter _mode;

    public MixerModule() : base("Mixer")
    {
        _inputs = new[] { AddInput("In 1"), AddInput("In 2"), AddInput("In 3"), AddInput("In 4") };
        _output = AddOutput("Out");

        _mode = AddParameter("Mode", 0, 0, ModeNames.Length - 1, options: ModeNames);
    }

    public override string Description =>
        "Sloučí až čtyři vstupní signály do jednoho — volitelně součtem, průměrem nebo násobením.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var connected = new List<float[]>(_inputs.Length);
        foreach (var input in _inputs)
        {
            if (inputBuffers.TryGetValue(input, out var buffer))
                connected.Add(buffer);
        }

        var output = NewBuffer(ctx);
        if (connected.Count == 0)
            return new Dictionary<Port, float[]> { [_output] = output };

        var mode = (int)_mode.Value;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            switch (mode)
            {
                case 1: // Average — keeps level stable regardless of how many inputs are connected
                    {
                        float sum = 0;
                        foreach (var buffer in connected)
                            sum += buffer[i];
                        output[i] = sum / connected.Count;
                        break;
                    }
                case 2: // Multiply — like a multi-input ring modulator
                    {
                        float product = 1;
                        foreach (var buffer in connected)
                            product *= buffer[i];
                        output[i] = product;
                        break;
                    }
                default: // Sum
                    {
                        float sum = 0;
                        foreach (var buffer in connected)
                            sum += buffer[i];
                        output[i] = sum;
                        break;
                    }
            }
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
