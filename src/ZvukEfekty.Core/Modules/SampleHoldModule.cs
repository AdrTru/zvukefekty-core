using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Reads its input at the moment a trigger arrives and holds that value until the next one — the
/// staircase generator of a modular rack.
///
/// This is not the same thing as the "Random" waveform in <see cref="Waveform"/>, which is also a
/// sample and hold but takes its own steps at a FIXED rate. That distinction is the whole reason
/// this module exists: with a fixed rate the result is heard as periodic modulation however random
/// the values are, which is what four attempts at a creaking door failed on (see todo.md 9a).
/// Driven from <see cref="PoissonTriggerModule"/> the steps land at irregular times, and the same
/// values stop sounding like a rhythm.
/// </summary>
public sealed class SampleHoldModule : ModuleBase
{
    /// <summary>Where a trigger counts as having arrived. Fixed rather than a knob because every
    /// trigger in this engine is already 0-or-1: <see cref="PoissonTriggerModule"/> emits a single
    /// sample of 1, gates are 0 or 1, and a square LFO swings past this on every edge.</summary>
    private const double TriggerThreshold = 0.5;

    private readonly Port _input;
    private readonly Port _trigger;
    private readonly Port _output;
    private readonly SeededRandomSource _noise;

    private double _held;
    private double _previousTrigger;

    public SampleHoldModule() : base("Vzorkovač")
    {
        _input = AddInput("In");
        _trigger = AddInput("Trig");
        _output = AddOutput("Out");

        _noise = AddSeed();
    }

    public override string Description =>
        "Přečte hodnotu na vstupu ve chvíli, kdy dorazí impuls na Trig, a drží ji až do dalšího " +
        "impulsu — ze spojitého signálu udělá schody. Když do In nic nezapojíte, vzorkuje si " +
        "vlastní bílý šum, takže sám o sobě je to generátor náhodných hodnot (a Seed říká kterých; " +
        "se zapojeným In je Seed bez vlivu). Spouštěč určuje RYTMUS schodů: z LFO jsou pravidelné, " +
        "z Nepravidelného spouštěče nepravidelné. Ostré hrany schodů se dají zaoblit modulem " +
        "Sklouznutí.";

    public override void ResetState()
    {
        _noise.Reset();
        _previousTrigger = 0;

        // Held from the first sample, not silent until the first trigger arrives. A patch whose
        // trigger runs at 2/s would otherwise open with half a second of nothing, and read as a
        // broken module rather than a slow one — the same call PoissonTriggerModule makes about
        // firing its first pulse at sample 0.
        _held = _noise.GetGenerator(out _).NextDouble() * 2 - 1;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var trigger = GetInputOrSilence(inputBuffers, _trigger, ctx);
        var random = _noise.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            _held = random.NextDouble() * 2 - 1;

        // An unconnected In is normalled to an internal noise source, the way a rack's sample and
        // hold has noise wired to it from the factory. Asking the dictionary is how "unconnected"
        // is told apart from "connected to something silent" — GetInputOrSilence cannot tell those
        // two apart, and here they have to mean different things.
        var hasInput = inputBuffers.TryGetValue(_input, out var input);

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var source = hasInput ? input![i] : random.NextDouble() * 2 - 1;

            // Rising edge only. A gate that stays open is one step, not a stream of them.
            if (trigger[i] >= TriggerThreshold && _previousTrigger < TriggerThreshold)
                _held = source;

            _previousTrigger = trigger[i];
            output[i] = (float)_held;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
