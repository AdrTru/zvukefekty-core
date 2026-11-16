using System.Globalization;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A tiny arithmetic language, compiled once and evaluated per sample — what
/// <see cref="Modules.FormulaModule"/> runs.
///
/// <para><b>An expression, deliberately not a script.</b> Patches are shared: the project's own
/// examples are published for people to take apart. A scripting language would mean that opening
/// somebody else's `.zvuky` runs their code. This cannot: there is no assignment, no loop, no call
/// out of the table below, so the worst a hostile formula can do is return NaN — which the module
/// clamps. The second reason is weight: a real scripting engine would be tens of megabytes of
/// dependency on a program that stands on one NAudio.</para>
///
/// <para><b>Four signal inputs, not one.</b> With a single input this language would mostly be a
/// <see cref="Modules.WaveshaperModule"/> that cannot be drawn. What it is FOR is the arithmetic
/// between signals that the catalog's boxes cannot express — a difference, a product, a comparison,
/// a crossfade — so it takes the same four inputs a <see cref="Modules.MixerModule"/> does and
/// names them <c>a</c>, <c>b</c>, <c>c</c>, <c>d</c>.</para>
///
/// <para><b>Compiled into closures, not walked as a tree.</b> Parsing emits a
/// <see cref="Func{Double}"/> per node, so evaluating is a chain of delegate calls rather than a
/// tree walk with a type switch at every step. Measured on a 14-node formula: 65,5 ns per sample
/// compiled against 81,0 ns walked, where a hand-written C# line of the same maths costs 17,2 ns
/// and <c>FilterModule.Process</c> costs 34,2 ns. Parsing itself is 8,2 µs and belongs in
/// <see cref="ModuleBase.Prepare"/>, which is the hook for exactly this kind of work.</para>
///
/// <para><b>No randomness on purpose.</b> There is no <c>rand</c>, and there will not be: every
/// random source in this program hangs off a saved <see cref="SeededRandomSource">Seed</see> so
/// that one patch renders one sound. A formula that could draw its own random numbers would be the
/// only thing in the engine that renders differently every time, and nothing would say so in the
/// file. Noise belongs on an input, where it comes from a module that has a seed.</para>
/// </summary>
public sealed class Formula
{
    /// <summary>Where each variable lives in <see cref="Slots"/>. The module writes the sample's
    /// values into the array and calls <see cref="Evaluate"/>; the compiled closures read straight
    /// out of it, so nothing is looked up by name once the formula is running.</summary>
    public static class Slot
    {
        public const int A = 0;
        public const int B = 1;
        public const int C = 2;
        public const int D = 3;

        /// <summary>First of the eight history slots, laid out as a1, a2, b1, b2, c1, c2, d1, d2 —
        /// i.e. <c>History + input * 2 + (age - 1)</c>. Contiguous so the module can shift the whole
        /// lot in one loop instead of eight named assignments.</summary>
        public const int History = 4;

        public const int Y1 = 12;
        public const int Y2 = 13;
        public const int T = 14;
        public const int P = 15;
        public const int Q = 16;
        public const int SampleRate = 17;
        public const int Count = 18;

        /// <summary>Slot holding <paramref name="input"/> (0-3) as it was <paramref name="age"/>
        /// (1-2) samples ago.</summary>
        public static int Past(int input, int age) => History + input * 2 + (age - 1);
    }

    /// <summary>What each name means, in the order the documentation lists them. Kept here rather
    /// than in the module or the inspector so the language has one description and the help text
    /// cannot drift away from what the parser accepts.</summary>
    public static readonly IReadOnlyList<(string Name, string Meaning)> Variables = new[]
    {
        ("a, b, c, d", "vzorky ze čtyř vstupů (nezapojený vstup je 0)"),
        ("a1, a2", "vstup a o jeden a o dva vzorky zpět — totéž má b, c i d (b1, b2, …)"),
        ("y1, y2", "vlastní výstup o jeden a o dva vzorky zpět — tudy vede zpětná vazba"),
        ("t", "čas v sekundách od začátku zvuku"),
        ("p, q", "dva knoflíky, oba modulovatelné"),
        ("sr", "vzorkovací kmitočet (48000)"),
        ("pi, e", "konstanty"),
    };

    /// <summary>Every function the language has, with its arity — the list the inspector shows and
    /// the documentation copies.</summary>
    public static readonly IReadOnlyList<(string Name, string Meaning)> Functions = new[]
    {
        ("sin(v), cos(v), tan(v)", "goniometrie, v radiánech"),
        ("abs(v), sign(v)", "velikost a znaménko"),
        ("sqrt(v), exp(v), log(v)", "odmocnina, e^v, přirozený logaritmus"),
        ("tanh(v)", "měkké omezení do ±1"),
        ("floor(v), round(v), frac(v)", "dolů, na nejbližší, desetinná část"),
        ("min(u, v), max(u, v)", "menší a větší ze dvou"),
        ("pow(u, v)", "u na v, totéž co u^v"),
        ("clamp(v, lo, hi)", "seřízne v do rozsahu"),
        ("if(podmínka, pak, jinak)", "podmínka je 1 nebo 0 z porovnání"),
    };

    /// <summary>Operators, for the same reason as the two lists above.</summary>
    public const string OperatorHelp =
        "+ − * / ^ (mocnina), % (zbytek po dělení), závorky, " +
        "porovnání < > <= >= == != vrací 1 nebo 0. Desetinná čísla s TEČKOU (0.5) — " +
        "čárka odděluje hodnoty ve funkcích";

    private readonly Func<double> _root;

    private Formula(Func<double> root, double[] slots)
    {
        _root = root;
        Slots = slots;
    }

    /// <summary>The variables the compiled formula reads, indexed by <see cref="Slot"/>. Owned by
    /// this instance and captured by its closures, so two modules never share one.</summary>
    public double[] Slots { get; }

    public double Evaluate() => _root();

    /// <summary>
    /// Compiles <paramref name="source"/>, or explains why it could not.
    ///
    /// Never throws for bad input: a formula is typed by hand, so being wrong is a normal state
    /// for it to be in, and the module keeps passing its input through while the message is shown
    /// in the inspector. <paramref name="error"/> names the character position, because "unknown
    /// name" without one is useless in a line that has three of them.
    /// </summary>
    public static Formula? TryCompile(string source, out string? error)
    {
        var slots = new double[Slot.Count];

        try
        {
            var parser = new Parser(source ?? "", slots);
            var root = parser.ParseAll();
            error = null;
            return new Formula(root, slots);
        }
        catch (FormulaException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private sealed class FormulaException : Exception
    {
        public FormulaException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Recursive descent straight into closures — there is no AST type in between. One pass, one
    /// representation, and the precedence is the shape of the call chain rather than a table:
    /// comparison is loosest, then + −, then * / %, then unary minus, then ^ (right-associative,
    /// so <c>2^3^2</c> is 512 like everywhere else), then atoms.
    /// </summary>
    private sealed class Parser
    {
        private readonly string _s;
        private readonly double[] _slots;
        private int _i;

        public Parser(string source, double[] slots)
        {
            _s = source;
            _slots = slots;
        }

        public Func<double> ParseAll()
        {
            if (_s.Trim().Length == 0)
                throw Error("formule je prázdná");

            var root = ParseComparison();
            SkipSpace();
            if (_i < _s.Length)
                throw Error(_s[_i] == ','
                    ? "desetinná čárka se tady nepoužívá — čísla se píšou s tečkou (0.5) " +
                      "a čárka odděluje hodnoty ve funkcích"
                    : $"nerozumím tomu, co je za výrazem: „{_s[_i..]}\"");

            return root;
        }

        private Func<double> ParseComparison()
        {
            var left = ParseSum();
            while (true)
            {
                SkipSpace();
                var op = MatchAny("<=", ">=", "==", "!=", "<", ">");
                if (op is null)
                    return left;

                var l = left;
                var r = ParseSum();
                left = op switch
                {
                    "<" => () => l() < r() ? 1 : 0,
                    ">" => () => l() > r() ? 1 : 0,
                    "<=" => () => l() <= r() ? 1 : 0,
                    ">=" => () => l() >= r() ? 1 : 0,
                    "==" => () => l() == r() ? 1 : 0,
                    _ => () => l() != r() ? 1 : 0,
                };
            }
        }

        private Func<double> ParseSum()
        {
            var left = ParseTerm();
            while (true)
            {
                SkipSpace();
                if (_i >= _s.Length || (_s[_i] != '+' && _s[_i] != '-'))
                    return left;

                var op = _s[_i++];
                var l = left;
                var r = ParseTerm();
                left = op == '+' ? () => l() + r() : () => l() - r();
            }
        }

        private Func<double> ParseTerm()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipSpace();
                if (_i >= _s.Length || (_s[_i] != '*' && _s[_i] != '/' && _s[_i] != '%'))
                    return left;

                var op = _s[_i++];
                var l = left;
                var r = ParseUnary();
                left = op switch
                {
                    '*' => () => l() * r(),
                    '/' => () => l() / r(),
                    // Floored, not truncated: `t % 1` has to be a rising 0..1 phasor, and C#'s own
                    // % hands back negative values for negative input, which would break the ramp.
                    _ => () => { var d = r(); var n = l(); return d == 0 ? 0 : n - d * Math.Floor(n / d); },
                };
            }
        }

        private Func<double> ParseUnary()
        {
            SkipSpace();
            if (_i < _s.Length && _s[_i] == '-')
            {
                _i++;
                var inner = ParseUnary();
                return () => -inner();
            }

            return ParsePower();
        }

        private Func<double> ParsePower()
        {
            var b = ParseAtom();
            SkipSpace();
            if (_i >= _s.Length || _s[_i] != '^')
                return b;

            _i++;
            var e = ParseUnary();
            return () => Math.Pow(b(), e());
        }

        private Func<double> ParseAtom()
        {
            SkipSpace();
            if (_i >= _s.Length)
                throw Error("výraz končí dřív, než dává smysl");

            if (_s[_i] == '(')
            {
                _i++;
                var inner = ParseComparison();
                Expect(')');
                return inner;
            }

            if (char.IsDigit(_s[_i]) || _s[_i] == '.')
                return ParseNumber();

            if (char.IsLetter(_s[_i]) || _s[_i] == '_')
                return ParseName();

            throw Error($"tomuhle znaku nerozumím: „{_s[_i]}\"");
        }

        private Func<double> ParseNumber()
        {
            var start = _i;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
                _i++;

            // A DOT, and only a dot. The rest of this program takes a Czech decimal comma
            // everywhere, and it was accepted here too until the first `min(3, 4)` came out as one
            // argument of 3.4 — a comma cannot be a decimal point and an argument separator in the
            // same language, and of the two readings the silently-wrong one is the decimal. The
            // messages below name the tripwire, since typing 0,5 here is the natural mistake.
            var text = _s[start.._i];
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw Error($"tohle není číslo: „{text}\"", start);

            return () => value;
        }

        private Func<double> ParseName()
        {
            var start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                _i++;

            var name = _s[start.._i].ToLowerInvariant();
            SkipSpace();

            if (_i < _s.Length && _s[_i] == '(')
                return ParseCall(name, start);

            var slots = _slots;

            // The four inputs and their history share one shape — a, a1, a2, b, b1, b2, … — so they
            // are resolved by arithmetic rather than by twelve cases. Adding a fifth input would be
            // one more letter here and one more port on the module, and nothing else.
            if (name.Length is 1 or 2 && name[0] >= 'a' && name[0] <= 'd')
            {
                var input = name[0] - 'a';
                if (name.Length == 1)
                {
                    var slot = Slot.A + input;
                    return () => slots[slot];
                }

                if (name[1] is '1' or '2')
                {
                    var slot = Slot.Past(input, name[1] - '0');
                    return () => slots[slot];
                }
            }

            switch (name)
            {
                case "y1": return () => slots[Slot.Y1];
                case "y2": return () => slots[Slot.Y2];
                case "t": return () => slots[Slot.T];
                case "p": return () => slots[Slot.P];
                case "q": return () => slots[Slot.Q];
                case "sr": return () => slots[Slot.SampleRate];
                case "pi": return () => Math.PI;
                case "e": return () => Math.E;
                default:
                    throw Error($"neznámé jméno „{name}\" — dovolené je a, b, c, d (a jejich a1, a2, …), " +
                                "y1, y2, t, p, q, sr, pi, e", start);
            }
        }

        private Func<double> ParseCall(string name, int nameStart)
        {
            Expect('(');
            var args = new List<Func<double>>();
            SkipSpace();
            if (_i < _s.Length && _s[_i] == ')')
                _i++;
            else
                while (true)
                {
                    args.Add(ParseComparison());
                    SkipSpace();
                    if (_i < _s.Length && _s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    Expect(')');
                    break;
                }

            switch (name)
            {
                case "sin": Arity(name, args, 1, nameStart); return Unary(args, Math.Sin);
                case "cos": Arity(name, args, 1, nameStart); return Unary(args, Math.Cos);
                case "tan": Arity(name, args, 1, nameStart); return Unary(args, Math.Tan);
                case "abs": Arity(name, args, 1, nameStart); return Unary(args, Math.Abs);
                case "sign": Arity(name, args, 1, nameStart); return Unary(args, v => Math.Sign(v));
                case "sqrt": Arity(name, args, 1, nameStart); return Unary(args, Math.Sqrt);
                case "exp": Arity(name, args, 1, nameStart); return Unary(args, Math.Exp);
                case "log": Arity(name, args, 1, nameStart); return Unary(args, Math.Log);
                case "tanh": Arity(name, args, 1, nameStart); return Unary(args, Math.Tanh);
                case "floor": Arity(name, args, 1, nameStart); return Unary(args, Math.Floor);
                case "round": Arity(name, args, 1, nameStart); return Unary(args, v => Math.Round(v));
                case "frac": Arity(name, args, 1, nameStart); return Unary(args, v => v - Math.Floor(v));

                case "min": Arity(name, args, 2, nameStart); return Binary(args, Math.Min);
                case "max": Arity(name, args, 2, nameStart); return Binary(args, Math.Max);
                case "pow": Arity(name, args, 2, nameStart); return Binary(args, Math.Pow);

                case "clamp":
                    {
                        Arity(name, args, 3, nameStart);
                        var v = args[0];
                        var lo = args[1];
                        var hi = args[2];
                        // Ordered, not trusted: clamp(v, 1, 0) would throw out of Math.Clamp, and an
                        // exception inside a per-sample loop is a crash rather than a wrong sound.
                        return () => { var l = lo(); var h = hi(); return Math.Clamp(v(), Math.Min(l, h), Math.Max(l, h)); };
                    }

                // Both branches are evaluated — there is no short-circuit here, and there does not
                // need to be: everything in this language is arithmetic on doubles, so evaluating
                // the branch not taken cannot do anything but waste a few nanoseconds. What it CAN
                // produce is a NaN out of the unused half, which is why the module guards the
                // result rather than trusting the formula.
                case "if":
                    {
                        Arity(name, args, 3, nameStart);
                        var cond = args[0];
                        var then = args[1];
                        var other = args[2];
                        return () => cond() != 0 ? then() : other();
                    }

                default:
                    throw Error($"neznámá funkce „{name}\"", nameStart);
            }
        }

        private static Func<double> Unary(List<Func<double>> args, Func<double, double> f)
        {
            var a = args[0];
            return () => f(a());
        }

        private static Func<double> Binary(List<Func<double>> args, Func<double, double, double> f)
        {
            var a = args[0];
            var b = args[1];
            return () => f(a(), b());
        }

        private void Arity(string name, List<Func<double>> args, int expected, int position)
        {
            if (args.Count == expected)
                return;

            // The extra-argument case is almost always a decimal comma, so it says so rather than
            // leaving the reader to count brackets.
            var tip = args.Count > expected
                ? " — nenapsal jsi v čísle desetinnou čárku místo tečky? (0.5, ne 0,5)"
                : "";

            throw Error($"{name} chce {expected} {(expected == 1 ? "hodnotu" : "hodnoty")}, " +
                        $"dostal jich {args.Count}{tip}", position);
        }

        private string? MatchAny(params string[] options)
        {
            foreach (var option in options)
            {
                if (_i + option.Length > _s.Length)
                    continue;

                if (_s.AsSpan(_i, option.Length).SequenceEqual(option))
                {
                    _i += option.Length;
                    return option;
                }
            }

            return null;
        }

        private void Expect(char c)
        {
            SkipSpace();
            if (_i >= _s.Length || _s[_i] != c)
                throw Error($"chybí „{c}\"");

            _i++;
        }

        private void SkipSpace()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
                _i++;
        }

        private FormulaException Error(string message, int? position = null)
            => new($"znak {(position ?? _i) + 1}: {message}");
    }
}
