using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Defines one drawn shape and lends it to the rest of the patch. The only module with a curve
/// editor in it.
///
/// <para>Curves used to be state on every module that wanted one, which put the same drawing tool
/// on four different boxes and meant a shape had to be redrawn in each of them to be shared. Here
/// the shape is drawn once and patched wherever it is needed, exactly like any other wire.</para>
///
/// <para><b>Two outputs, because a shape and a signal are different things.</b>
/// "Tvar" is a <see cref="PortKind.Curve"/> port: it hands the curve itself to a consumer that
/// knows what to do with a shape — an oscillator reads it as one cycle, a waveshaper as a transfer
/// function, an envelope as a contour. Nothing is sampled; the consumer gets the curve and asks it
/// for values on its own terms.
/// "Out" is an ordinary audio port that sweeps the shape across <see cref="LengthName"/> seconds.
/// That is what makes the module reach everything else in the program: a module does not need to
/// know anything about curves to have a drawn shape driving its Cutoff, Gain or Frequency — it
/// just receives a signal like any other.</para>
///
/// <para>The curve is held at the widest range (−1…1, ends free) on purpose. Each consumer conforms
/// it to what it needs when it arrives (see <see cref="CurveInput"/>), so one drawing can feed a
/// waveform and an envelope at the same time; holding it narrower here would only throw away shape
/// the other consumer could have used.</para>
/// </summary>
public sealed class CurveModule : ModuleBase
{
    public const string LengthName = "Délka";

    private static readonly string[] LoopNames = { "Ne", "Ano" };

    private readonly Port _curveOutput;
    private readonly Port _signalOutput;
    private readonly ModuleParameter _length;
    private readonly ModuleParameter _amplitude;
    private readonly ModuleParameter _loop;
    private readonly ModuleCurve _shape;

    // Carried across blocks so the sweep is continuous for a live voice, which renders the same
    // signal in a stream of short blocks rather than in one pass.
    private double _position;

    public CurveModule() : base("Křivka")
    {
        _curveOutput = AddCurveOutput("Tvar");
        _signalOutput = AddOutput("Out");

        _length = AddParameter(LengthName, 1.0, 0.001, 60, "s", tempoSync: TempoSyncKind.Seconds);
        _amplitude = AddParameter("Amplitude", 1.0, 0, 1);
        _loop = AddParameter("Opakovat", 0, 0, LoopNames.Length - 1, options: LoopNames);

        _shape = AddCurve("Tvar", Curve.Line(-1, 1), CurveOutput.Bipolar, "Průběh", "Hodnota");
    }

    public override string Description =>
        "Nakreslí tvar a půjčí ho zbytku sestavy. Výstup Tvar zapoj do křivkového vstupu " +
        "(Oscillator, LFO, Waveshaper, Obálka z křivky) — tam se použije jako tvar, ne jako signál, " +
        "a každý si ho srovná do rozsahu, který potřebuje. Výstup Out je obyčejný signál: tvar " +
        "rozvinutý přes Délku sekund, takže jde pustit do jakéhokoli modulačního vstupu v programu. " +
        "Jedna křivka může krmit libovolně mnoho modulů zároveň.";

    public override void ResetState() => _position = 0;

    /// <summary>Hands the drawn shape to whoever is wired to "Tvar".</summary>
    public override Curve? GetCurveOutput(Port port) => port == _curveOutput ? _shape.Curve : null;

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var length = GetModulatedValues(_length, ctx, inputBuffers);
        var amplitude = GetModulatedValues(_amplitude, ctx, inputBuffers);
        var loops = (int)_loop.Value == 1;

        // Read once: the curve is a shape held by reference, and swapping it from the editor is a
        // single reference write, so taking it once per block is both cheap and free of tearing.
        var shape = _shape.Curve;
        var signal = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            signal[i] = shape.Sample(_position) * amplitude[i];

            var seconds = Math.Max(length[i], 0.001);
            _position += 1.0 / (seconds * ctx.SampleRate);

            if (_position >= 1.0)
                // Held at the end rather than wrapped when not looping, so the signal settles on
                // the shape's last value instead of snapping back to its first.
                _position = loops ? _position - Math.Floor(_position) : 1.0;
        }

        return new Dictionary<Port, float[]> { [_signalOutput] = signal };
    }
}
