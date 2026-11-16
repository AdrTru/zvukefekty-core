using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Generates a periodic waveform. Frequency and Amplitude each get an automatic
/// modulation input (see <see cref="ModuleBase.AddParameter"/>), so patching another
/// module's output into them drives pitch/volume over time.
/// </summary>
public sealed class OscillatorModule : ModuleBase
{
    public const string AntialiasName = "Vyhlazení hran";

    private static readonly string[] AntialiasNames = { "Ne", "Ano" };

    private readonly Port _output;
    private readonly ModuleParameter _waveform;
    private readonly ModuleParameter _antialias;
    private readonly ModuleParameter _frequency;
    private readonly ModuleParameter _amplitude;
    private readonly SeededRandomSource _randomSource;
    private readonly CurveInput _customWave;
    private double _phase;
    private double _held;

    public OscillatorModule() : base("Oscillator")
    {
        _output = AddOutput("Out");

        _waveform = AddParameter("Waveform", 0, 0, Waveform.Names.Length - 1, options: Waveform.Names);

        // Off by default, and that is not a preference — it is what keeps every patch saved so far
        // rendering the sound it was tuned to. Switching it on changes the timbre of Square, Saw
        // and Pulse, so it has to be a decision recorded in the patch rather than a setting of the
        // app: an export must not depend on which way a toolbar toggle happened to be left.
        _antialias = AddParameter(AntialiasName, 0, 0, AntialiasNames.Length - 1, options: AntialiasNames);
        _frequency = AddParameter("Frequency", 440, 20, 5000, "Hz");
        _amplitude = AddParameter("Amplitude", 0.8, 0, 1);
        // Only the "Random" (sample & hold) shape actually draws from this, but the knob is added
        // unconditionally so switching to that shape does not change the module's parameter list.
        _randomSource = AddSeed();
        _held = _randomSource.GetGenerator(out _).NextDouble() * 2 - 1;

        // Declared as a Cycle, not a bare bipolar curve: this one is read once per period, so its
        // ends have to meet or the wave steps every cycle and buzzes at the fundamental. The
        // default is a sine, so switching Waveform to Custom changes nothing until the shape is
        // actually drawn.
        // Ends tied: a waveform is read round and round, and a step where the last value meets
        // the first is heard as buzz on the fundamental. The built-in sine is what plays while
        // nothing is patched in, so switching to Custom before wiring anything changes nothing.
        _customWave = AddCurveInput("Vlastní vlna", Curve.Sine(), CurveOutput.Cycle);
    }

    public override string Description =>
        "Generátor periodického průběhu (Sine/Square/Saw/Triangle/Pulse/Random) s nastavitelnou " +
        "frekvencí a hlasitostí — základní zdroj tónu. Vyhlazení hran potlačí aliasování " +
        "u Square, Saw a Pulse: bez něj se harmonické nad polovinou vzorkovací frekvence " +
        "překlopí zpátky dolů na neharmonické frekvence a tón je nahoře drsný. Trojúhelník " +
        "a sinus to nepotřebují. Výchozí je vypnuto, aby starší sestavy zněly jako dřív.";

    public override void ResetState()
    {
        _phase = 0;
        _randomSource.Reset();
        _held = _randomSource.GetGenerator(out _).NextDouble() * 2 - 1;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var frequency = GetModulatedValues(_frequency, ctx, inputBuffers);
        var amplitude = GetModulatedValues(_amplitude, ctx, inputBuffers);
        // A seed changed in the inspector invalidates the held sample-and-hold value too, since it
        // was drawn from the previous sequence.
        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            _held = random.NextDouble() * 2 - 1;

        var output = Waveform.GenerateBuffer((int)_waveform.Value, amplitude, frequency, ctx.SampleRate, random,
            _customWave.Curve, (int)_antialias.Value == 1, ref _phase, ref _held, ctx.Scratch);

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
