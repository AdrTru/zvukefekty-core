using System.Globalization;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// A bank of modal resonators sharing one input — a struck or excited body in one box. Each row
/// of the "Módy" text is one mode: frequency, decay (to −60 dB) and gain, with exactly the meaning
/// <see cref="ResonatorModule"/> gives its knobs, so a net of Resonators summed in Mixers converts
/// row for row and renders the same to float rounding (todo 18c).
///
/// <para>Why a text and not knobs: a body has 3 to 30 modes, and 90 knobs in the inspector is no
/// editor. The rows are parsed once, off the audio thread (<see cref="Prepare"/> and on every edit),
/// never per sample. What stays a knob is what wants to move while it rings: the whole body's
/// pitch (Výška), its damping (Doznívání) and level — so a Nota or a curve can play the body.</para>
///
/// <para>Per mode it is the Resonator's two-pole recursion <c>y = x + 2r·cosθ·y1 − r²·y2</c>, whose
/// impulse peak grows like 1/sinθ — a low mode rings far louder than a high one at the same Gain.
/// "Hlasitost módů = Váha = špička" multiplies by sinθ, which makes the impulse response
/// <c>Gain·rⁿ·sin((n+1)θ)</c>: the Gain column is then the peak, at any frequency.</para>
/// </summary>
public sealed class ModalBankModule : ModuleBase
{
    public const int MaxModes = 64;

    /// <summary>Gains are peaks (the default loudness is "Váha = špička") summing to under 1, so the
    /// self-strike of a freshly dropped bank does not clip. Under "Jako Resonator" rows like these
    /// peaked near 9 — the reason that is not the default.</summary>
    private const string DefaultModes =
        "# kovová tyč: poměry 1 : 2,76 : 5,40\n" +
        "800   0.6   0.5\n" +
        "2208  0.35  0.25\n" +
        "4320  0.2   0.12";

    /// <summary>Materials. Kovadlina and Keramika are the bodies of accepted game patches; the rest are
    /// textbook mode ratios (free bar 1 : 2.756 : 5.404 : 8.933, minor-third church bell, wine glass)
    /// with judged decays — starting points, measured for level only, not listened to.</summary>
    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Body("Kovová tyč", "Volná kovová tyč (poměry 1 : 2,756 : 5,404 : 8,933) — triangl, kovová tyčka. Neposlouchané.",
            "# volná tyč\n600 1.2 0.4\n1654 0.7 0.25\n3242 0.4 0.15\n5360 0.25 0.08"),
        Body("Kovadlina", "Tělo přijaté kovadliny V3, šest módů. Kontakt kladiva (šum) patří vedle, ne sem.",
            "# kovadlina V3\n840 0.0487 0.12\n1517 0.0832 0.11\n2267 0.1730 0.16\n3079 0.3803 0.23\n3643 0.2767 0.18\n4831 0.1799 0.17"),
        Body("Zvon", "Malý zvon s malou tercií (hum, prima, tercie, kvinta, nominál…). Neposlouchané.",
            "# zvon, základ 440 Hz\n220 4 0.18\n440 3 0.15\n520.5 2.5 0.18\n662.6 1.5 0.07\n880 1.8 0.15\n1106 1 0.07\n1171 0.8 0.06"),
        Body("Sklenice", "Sklenice na víno (poměry 1 : 2,32 : 4,25 : 6,63), dlouhé čisté zvonění. Neposlouchané.",
            "# sklenice\n900 1.5 0.4\n2088 0.9 0.2\n3825 0.5 0.1\n5967 0.3 0.05"),
        Body("Dřevo", "Prkno, dřevěná tyčka — krátké tlumené módy. Neposlouchané.",
            "# prkno\n350 0.12 0.5\n900 0.06 0.3\n1630 0.035 0.15\n2560 0.02 0.08"),
        Body("Keramika", "Tělo přijaté keramické nádoby V9, bez useknutí rozbitím.",
            "# keramická nádoba V9\n790 0.259 0.24\n1427 0.1871 0.16\n3260 0.1511 0.46\n4630 0.0576 0.15"),
    };

    private static ModulePreset Body(string name, string description, string modes)
        => new(name, description,
            new Dictionary<string, double> { ["Výška"] = 1, ["Doznívání"] = 1, ["Hlasitost módů"] = 1 },
            new Dictionary<string, string> { ["Módy"] = modes });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    private static readonly string[] LoudnessNames = { "Jako Resonator", "Váha = špička" };

    private readonly Port _input;
    private readonly Port _trigger;
    private readonly Port _hz;
    private readonly Port _output;
    private readonly ModuleParameter _pitch;
    private readonly ModuleParameter _decay;
    private readonly ModuleParameter _level;
    private readonly ModuleParameter _loudness;
    private readonly ModuleText _modesText;

    /// <summary>Parsed rows, swapped as a whole so the editor thread can replace them between two
    /// samples without the audio thread ever seeing half a table.</summary>
    private Mode[] _modes = Array.Empty<Mode>();
    private string? _parsedFrom;
    private bool _parseFailed;

    private readonly double[] _y1 = new double[MaxModes];
    private readonly double[] _y2 = new double[MaxModes];
    private readonly double[] _a1 = new double[MaxModes];
    private readonly double[] _a2 = new double[MaxModes];
    private readonly double[] _outGain = new double[MaxModes];

    /// <summary>Set by <see cref="ResetState"/>: with nothing wired to In or Trig the bank strikes
    /// itself once at the very first sample, like the Struna plucks itself — so a body is heard the
    /// moment it is dropped on the canvas, and a one-shot hit needs no impulse source in front.</summary>
    private bool _pendingStrike;
    private double _previousTrigger;

    /// <summary>Last non-zero frequency on the Hz input. Held through a 0 so the tail of a released
    /// note keeps ringing at its pitch instead of jumping back to the body's own tuning.</summary>
    private double _heldHz;

    internal readonly record struct Mode(double Frequency, double Decay, double Gain);

    public ModalBankModule() : base("Banka módů")
    {
        _input = AddInput("In");
        _trigger = AddInput("Trig");
        _hz = AddInput("Hz");
        _output = AddOutput("Out");

        _pitch = AddParameter("Výška", 1, 0.25, 4, "×");
        _decay = AddParameter("Doznívání", 1, 0.1, 4, "×");
        _level = AddParameter("Úroveň", 1, 0, 2);
        _loudness = AddParameter("Hlasitost módů", 1, 0, LoudnessNames.Length - 1, options: LoudnessNames);

        _modesText = AddText("Módy", DefaultModes, string.Join(Environment.NewLine, new[]
        {
            "Jeden mód na řádek: frekvence [Hz], Decay [s do −60 dB], Gain — jako u Resonatoru.",
            "Oddělovač mezera, tabulátor nebo středník; desetinná tečka i čárka. # = poznámka.",
            $"Řádek tabulky MÓDY ze `zvuk analyza` jde vložit, jak je. Nejvýš {MaxModes} módů.",
        }));
        _modesText.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModuleText.Text))
                EnsureParsed();
        };
    }

    public ModuleText Modes => _modesText;

    public override string Description =>
        "Banka modálních rezonátorů se společným vstupem — rozeznělé těleso (kov, dřevo, sklo, " +
        "keramika) v jedné krabičce místo sítě Resonatorů a Mixerů. Každý řádek Módů je jeden " +
        "mód: frekvence, Decay a Gain se stejným významem jako u Resonatoru. Výška posouvá celé " +
        "těleso (křivka, LFO). Do vstupu Hz patří Nota.Hz: nejnižší mód pak zní na zahrané notě, " +
        "ostatní drží své poměry a Výška násobí navrch; Nota.Gate do Trig udeří při stisku. Doznívání násobí všechny " +
        "Decay. Hlasitost módů Váha = špička (výchozí) = Gain je přímo špička módu " +
        "při jakékoli frekvenci; Jako Resonator = hluboké módy hlasitější, na převod sítě Resonatorů. Nezapojená se na začátku " +
        "sama jednou udeří (jednovzorkový impuls), Trig udeří znovu na náběžnou hranu; do In patří " +
        "vlastní buzení — šum přes rychlou obálku, škrábání, cokoli. Nečitelný text pustí vstup beze změny a napíše proč.";

    public override void Prepare() => EnsureParsed();

    public override void ResetState()
    {
        Array.Clear(_y1);
        Array.Clear(_y2);
        _pendingStrike = true;
        _previousTrigger = 0;
        _heldHz = 0;
    }

    private void EnsureParsed()
    {
        var text = _modesText.Text;
        if (_parsedFrom == text)
            return;

        _parsedFrom = text;
        if (TryParse(text, out var modes, out var error))
        {
            _modes = modes;
            _parseFailed = false;
            _modesText.Error = null;
        }
        else
        {
            _parseFailed = true;
            _modesText.Error = error;
        }
    }

    /// <summary>Reads the rows. A row is three numbers (Hz, Decay, Gain), or a row of the MÓDY table
    /// <c>zvuk analyza</c> prints (index, Hz, Decay, dB, Gain, shoda…) — recognised by a whole-number
    /// first column and at least five numbers, from which Hz, Decay and Gain are taken.</summary>
    internal static bool TryParse(string text, out Mode[] modes, out string? error)
    {
        var list = new List<Mode>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash];

            var fields = line.Split(new[] { ' ', '\t', ';', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new List<double>();
            foreach (var field in fields)
            {
                if (double.TryParse(field.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    numbers.Add(value);
                else if (numbers.Count < 3)
                {
                    // Trailing marks like the analysis table's "⚠" are fine; a word in the middle is not.
                    modes = Array.Empty<Mode>();
                    error = $"Řádek {lineNumber}: „{field}“ není číslo.";
                    return false;
                }
            }

            if (numbers.Count == 0)
                continue;

            double frequency, decay, gain;
            if (numbers.Count >= 5 && numbers[0] == Math.Floor(numbers[0]))
                (frequency, decay, gain) = (numbers[1], numbers[2], numbers[4]);
            else if (numbers.Count == 3)
                (frequency, decay, gain) = (numbers[0], numbers[1], numbers[2]);
            else
            {
                modes = Array.Empty<Mode>();
                error = $"Řádek {lineNumber}: čekám tři čísla (frekvence, Decay, Gain), je jich {numbers.Count}.";
                return false;
            }

            if (frequency <= 0 || decay <= 0 || double.IsNaN(gain) || double.IsInfinity(gain))
            {
                modes = Array.Empty<Mode>();
                error = $"Řádek {lineNumber}: frekvence i Decay musí být kladné.";
                return false;
            }

            list.Add(new Mode(frequency, decay, gain));
            if (list.Count > MaxModes)
            {
                modes = Array.Empty<Mode>();
                error = $"Víc než {MaxModes} módů.";
                return false;
            }
        }

        modes = list.ToArray();
        error = list.Count == 0 ? "Žádný mód — banka mlčí." : null;
        return true;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        EnsureParsed();
        var modes = _modes;

        // Asked of the dictionary rather than through GetInputOrSilence, as in the Struna: an
        // unwired socket and a wired-but-silent one must mean different things here.
        var hasInput = inputBuffers.TryGetValue(_input, out var wiredInput);
        var hasTrigger = inputBuffers.TryGetValue(_trigger, out var trigger);
        var hasHz = inputBuffers.TryGetValue(_hz, out var hz);
        var input = hasInput ? wiredInput! : GetInputOrSilence(inputBuffers, _input, ctx);
        var output = NewBuffer(ctx);

        if (_parseFailed)
        {
            Array.Copy(input, output, ctx.SampleCount);
            return new Dictionary<Port, float[]> { [_output] = output };
        }

        var pitch = GetModulatedValues(_pitch, ctx, inputBuffers);
        var decayScale = GetModulatedValues(_decay, ctx, inputBuffers);
        var level = GetModulatedValues(_level, ctx, inputBuffers);
        var peakNormalized = (int)Math.Round(_loudness.Value) == 1;
        var fs = (double)ctx.SampleRate;
        var count = modes.Length;

        // Nota.Hz plays the lowest mode — the body's fundamental — on the note; the others keep
        // their ratios to it. Výška still multiplies on top, so an octave knob keeps working.
        var lowest = double.PositiveInfinity;
        foreach (var mode in modes)
            lowest = Math.Min(lowest, mode.Frequency);

        // Coefficients cost an exp and a cos per mode, so they are only recomputed when the two
        // knobs that feed them actually move — once per block when nothing modulates them.
        var lastPitch = double.NaN;
        var lastDecay = double.NaN;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var p = Math.Clamp(pitch[i], 0.25, 4);
            if (hasHz)
            {
                if (hz![i] > 0)
                    _heldHz = hz[i];
                if (_heldHz > 0 && count > 0)
                    p *= _heldHz / lowest;
            }
            var d = Math.Clamp(decayScale[i], 0.1, 4);
            if (p != lastPitch || d != lastDecay)
            {
                lastPitch = p;
                lastDecay = d;
                for (var m = 0; m < count; m++)
                    Coefficients(m, modes[m], p, d, fs, peakNormalized);
            }

            double x = input[i];
            if (_pendingStrike)
            {
                _pendingStrike = false;
                if (!hasInput && !hasTrigger)
                    x += 1;
            }
            if (hasTrigger)
            {
                if (trigger![i] >= 0.5 && _previousTrigger < 0.5)
                    x += 1;
                _previousTrigger = trigger[i];
            }

            double sum = 0;
            for (var m = 0; m < count; m++)
            {
                var y0 = x + _a1[m] * _y1[m] - _a2[m] * _y2[m];
                _y2[m] = _y1[m];
                _y1[m] = y0;
                // Rounded to float per mode, like a Resonator's output buffer, so a converted net
                // of Resonators matches this to float rounding rather than drifting by summation order.
                sum += (float)(y0 * _outGain[m]);
            }

            output[i] = (float)(sum * Math.Max(0, level[i]));
        }

        // A rung-out mode sinks into subnormal doubles, which the CPU handles in microcode
        // (measured 2026-11-14: 0.6 % of a core 20 s after one strike against 0.4 % while ringing).
        // Once per block is enough: from 1e-30 to the subnormal range takes far longer than a block.
        for (var m = 0; m < count; m++)
        {
            if (Math.Abs(_y1[m]) < 1e-30 && Math.Abs(_y2[m]) < 1e-30)
                _y1[m] = _y2[m] = 0;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    private void Coefficients(int m, Mode mode, double pitch, double decayScale, double fs, bool peakNormalized)
    {
        // Same clamps as ResonatorModule, so converted patches match it.
        var freq = Math.Clamp(mode.Frequency * pitch, 20, fs * 0.45);
        var decaySeconds = Math.Max(0.01, mode.Decay * decayScale);
        var r = Math.Exp(Math.Log(0.001) / (decaySeconds * fs));
        var theta = 2 * Math.PI * freq / fs;

        _a1[m] = 2 * r * Math.Cos(theta);
        _a2[m] = r * r;

        // A mode Výška pushes past 0.40·fs fades out by 0.45·fs instead of sitting clamped at
        // 0.45·fs (where the Resonator parks it) and ringing at the wrong pitch.
        var nominal = mode.Frequency * pitch;
        var alias = nominal <= 0.40 * fs ? 1.0 : nominal >= 0.45 * fs ? 0.0 : (0.45 * fs - nominal) / (0.05 * fs);
        _outGain[m] = mode.Gain * alias * (peakNormalized ? Math.Sin(theta) : 1);
    }
}
