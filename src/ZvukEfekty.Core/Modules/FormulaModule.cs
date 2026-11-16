using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Evaluates a hand-written arithmetic expression on every sample — see <see cref="Formula"/> for
/// the language, which is where the security and performance reasoning lives.
///
/// <para><b>This is the escape hatch, not a way of building.</b> Standard patching stays on the
/// boxes: anything that can be wired out of the catalog should be wired out of the catalog, where
/// it can be taken apart, re-drawn and understood. The rule this module was accepted under (see
/// `todo.md` 12d) is that <b>it closes no gap</b> — a comb filter or a quantiser being writable as
/// a formula does not make either of them done. It exists for the arithmetic the graph cannot
/// express at all, and its own defaults push it that way: it starts as a pass-through and it lives
/// at the end of Zpracování rather than among the modules used daily.</para>
///
/// <para><b>What only this module can do: feedback.</b> <see cref="ModuleGraph"/> forbids cycles,
/// so a signal cannot reach its own input anywhere in a patch. Inside one formula there is no cycle
/// to forbid — <c>y1</c> and <c>y2</c> are just the previous two results — which is how a one-pole
/// filter, a DC blocker, an integrator or a resonator become writable here and nowhere else.</para>
///
/// <para><b>Four inputs, because the point is arithmetic BETWEEN signals.</b> With one input this
/// would mostly be a <see cref="WaveshaperModule"/> that cannot be drawn. The difference, the
/// product and the comparison of two signals are what the boxes cannot express — the same reason
/// <see cref="InverterModule"/> had to exist for subtraction alone.</para>
///
/// <para><b>The formula is drawn on the node.</b> Every other box says what it does by its name;
/// this one cannot, so hiding its text in the inspector would make it the black box that this
/// project rejects elsewhere. Whoever opens someone else's patch reads the maths off the canvas.</para>
/// </summary>
public sealed class FormulaModule : ModuleBase
{
    /// <summary>What a freshly placed module computes: its first input, unchanged. A pass-through
    /// rather than a demonstration — a module that starts out doing something clever is one nobody
    /// reads before wiring it up.</summary>
    private const string DefaultFormula = "a";

    /// <summary>Results smaller than this are snapped to zero. Nothing audible lives at −600 dB;
    /// what does live down there is the tail of a feedback formula decaying towards denormal
    /// numbers, which is a shape this module makes easy to write by accident.</summary>
    private const double SilenceFloor = 1e-30;

    private readonly Port[] _inputs;
    private readonly Port _output;
    private readonly ModuleParameter _p;
    private readonly ModuleParameter _q;
    private readonly ModuleText _formula;

    private Formula? _program;
    private string? _compiledFrom;

    private double _y1, _y2;
    private readonly double[] _past = new double[8];
    private long _sample;

    public FormulaModule() : base("Formule")
    {
        _inputs = new[] { AddInput("a"), AddInput("b"), AddInput("c"), AddInput("d") };
        _output = AddOutput("Out");

        _p = AddParameter("p", 0, -1, 1);
        _q = AddParameter("q", 0, -1, 1);

        // Built out of the language's own tables rather than retyped, so the help under the box
        // cannot promise a name the parser does not know.
        _formula = AddText("Formule", DefaultFormula, string.Join(Environment.NewLine, new[]
        {
            "Proměnné: " + string.Join(" · ", Formula.Variables.Select(v => v.Name)),
            "Funkce: " + string.Join(" · ", Formula.Functions.Select(f => f.Name)),
            "Operátory: " + Formula.OperatorHelp,
        }));

        // Recompiled as it is typed, so the inspector can say what is wrong with the formula while
        // it is being written rather than at the next Play. Costs microseconds and happens on
        // whichever thread edited the text, which is never the audio one.
        _formula.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModuleText.Text))
                EnsureCompiled();
        };
    }

    /// <summary>The expression itself. Exposed so the node's face can draw it and the inspector can
    /// show what is wrong with it.</summary>
    public ModuleText Expression => _formula;

    public override string Description =>
        "Spočítá na každý vzorek to, co je napsané ve Formuli. Vstupy jsou a, b, c, d (nezapojený " +
        "je 0), knoflíky p a q, čas t, vzorkovací kmitočet sr — a hlavně y1 a y2, tedy vlastní " +
        "výstup o jeden a dva vzorky zpět, kudy vede zpětná vazba, kterou graf jinde zakazuje. " +
        "Tím se dá napsat jednopólový filtr, odstraňovač stejnosměrné složky nebo integrátor, " +
        "což z krabiček poskládat nejde. ⚠️ Je to nouzový východ, ne způsob stavění: co jde " +
        "zapojit z krabiček, patří do krabiček — ty jdou rozebrat a pochopit, formule ne. " +
        "Nesmyslná formule (dělení nulou) nezhavaruje, vrátí nulu; nepřeložitelná pustí vstup a " +
        "beze změny dál a napíše proč.";

    public override void ResetState()
    {
        _y1 = 0;
        _y2 = 0;
        Array.Clear(_past);
        _sample = 0;
    }

    /// <summary>Compiles the text, off the audio thread — this is the hook that exists for exactly
    /// this kind of work (measured: 8,2 µs for a long formula). <see cref="Process"/> re-checks and
    /// can compile too, so editing a formula while a sound is held does not wait for the next note,
    /// but that path costs once per edit and never per sample.</summary>
    public override void Prepare() => EnsureCompiled();

    private void EnsureCompiled()
    {
        if (_compiledFrom == _formula.Text)
            return;

        _compiledFrom = _formula.Text;
        _program = Formula.TryCompile(_formula.Text, out var error);
        _formula.Error = error;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        EnsureCompiled();

        // Taken once, because the editor can swap it from another thread between two samples. Same
        // treatment a patched curve gets in CurveModule, and for the same reason: a single
        // reference read is cheap and cannot tear.
        var program = _program;

        var a = GetInputOrSilence(inputBuffers, _inputs[0], ctx);
        var b = GetInputOrSilence(inputBuffers, _inputs[1], ctx);
        var c = GetInputOrSilence(inputBuffers, _inputs[2], ctx);
        var d = GetInputOrSilence(inputBuffers, _inputs[3], ctx);

        var output = NewBuffer(ctx);

        // A formula that will not parse passes its first input through rather than rendering
        // silence. Silence is the failure this project keeps mistaking for a bug, and the reason is
        // already on screen: the inspector is showing the parse error next to the box.
        if (program is null)
        {
            Array.Copy(a, output, ctx.SampleCount);
            return new Dictionary<Port, float[]> { [_output] = output };
        }

        var p = GetModulatedValues(_p, ctx, inputBuffers);
        var q = GetModulatedValues(_q, ctx, inputBuffers);

        var slots = program.Slots;
        slots[Formula.Slot.SampleRate] = ctx.SampleRate;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            slots[Formula.Slot.A] = a[i];
            slots[Formula.Slot.B] = b[i];
            slots[Formula.Slot.C] = c[i];
            slots[Formula.Slot.D] = d[i];

            for (var h = 0; h < _past.Length; h++)
                slots[Formula.Slot.History + h] = _past[h];

            slots[Formula.Slot.Y1] = _y1;
            slots[Formula.Slot.Y2] = _y2;
            slots[Formula.Slot.P] = p[i];
            slots[Formula.Slot.Q] = q[i];
            // Counted in samples and divided here, not accumulated by adding 1/sr: a float sum
            // drifts, and t is the one variable a formula uses to build something periodic.
            slots[Formula.Slot.T] = (double)_sample / ctx.SampleRate;

            var value = program.Evaluate();

            // Division by zero, log of a negative, a feedback term over 1 — all of them are one
            // keystroke away, and any of them would otherwise spread NaN through the rest of the
            // patch and out into the exported file.
            //
            // The range check is against FLOAT, not double, and that is not pedantry: `y1 * 2 + 1`
            // runs away to 1e300, which is a perfectly finite double and an infinity the moment it
            // is cast on the next line. Measured before this was here: 8321 infinities in a fifth
            // of a second of output. Zeroing the value also clears the feedback, so a runaway
            // formula collapses and starts over instead of staying stuck at infinity.
            if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue || Math.Abs(value) < SilenceFloor)
                value = 0;

            output[i] = (float)value;

            // Shift AFTER evaluating: y1 has to be the previous result, not this one.
            for (var input = 0; input < 4; input++)
            {
                _past[input * 2 + 1] = _past[input * 2];
                _past[input * 2] = input switch { 0 => a[i], 1 => b[i], 2 => c[i], _ => d[i] };
            }

            _y2 = _y1;
            _y1 = value;
            _sample++;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
