using System.Globalization;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Fires written-down events at exact times — "a shard lands at 28,4 ms with strength 0,8". The
/// non-musical counterpart of <see cref="SequencerModule"/>: times in seconds, not steps on a grid,
/// and a strength per event instead of a pitch (todo 18d, P07).
///
/// <para><b>Why not a curve.</b> A <see cref="CurveModule"/> is a 4096-point table over its whole
/// length, so on a 0,25 s clip one cell is ~6 samples and every "instant" jump is a 0,14 ms ramp
/// — measured on the anvil V3, where it was the whole difference against an exact strike. And a
/// curve cannot put out a single-sample impulse at all, which is what a modal bank or a resonator
/// wants as a strike. Here an event lands on <c>round(t·fs)</c>, exactly.</para>
///
/// <para><b>Timing is a sample counter from the start</b>, and every event's sample is computed
/// from its own time (plus the loop number × period when looping), never by adding gaps — so
/// nothing can drift, and block size cannot move an event.</para>
/// </summary>
public sealed class EventSequenceModule : ModuleBase
{
    public const int MaxEvents = 1024;

    private const string DefaultEvents =
        "# čas [s]  síla  (gate [s] nepovinně)\n" +
        "0      1\n" +
        "0.12   0.6\n" +
        "0.2    0.35\n" +
        "0.25   0.2";

    private static readonly string[] LoopNames = { "Ne", "Ano" };

    private readonly Port _reset;
    private readonly Port _impulseOutput;
    private readonly Port _trigOutput;
    private readonly Port _gateOutput;
    private readonly Port _strengthOutput;
    private readonly ModuleParameter _gateLength;
    private readonly ModuleParameter _loop;
    private readonly ModuleParameter _period;
    private readonly ModuleText _eventsText;

    internal readonly record struct Event(double Time, double Strength, double Gate);

    private Event[] _events = Array.Empty<Event>();
    private string? _parsedFrom;

    private long _sample;
    private int _next;
    private long _loopIndex;
    private int _gateSamplesLeft;
    private float _strength;
    private double _previousReset;

    public EventSequenceModule() : base("Události")
    {
        _reset = AddInput("Reset");
        _impulseOutput = AddOutput("Impuls");
        _trigOutput = AddOutput("Trig");
        _gateOutput = AddOutput("Gate");
        _strengthOutput = AddOutput("Síla");

        _gateLength = AddParameter("Délka gate", 0.02, 0.0005, 2, "s", modulatable: false);
        _loop = AddParameter("Opakovat", 0, 0, LoopNames.Length - 1, options: LoopNames);
        _period = AddParameter("Perioda", 1, 0.001, 60, "s", modulatable: false);

        _eventsText = AddText("Události", DefaultEvents, string.Join(Environment.NewLine, new[]
        {
            "Jedna událost na řádek: čas [s], síla, nepovinně délka gate [s] (jinak knoflík Délka gate).",
            "Oddělovač mezera, tabulátor nebo středník; desetinná tečka i čárka. # = poznámka. Pořadí řádků nevadí.",
            $"Dvě události na stejném vzorku se v Impulsu sečtou. Nejvýš {MaxEvents} událostí.",
        }));
        _eventsText.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModuleText.Text))
                EnsureParsed();
        };
    }

    public ModuleText Events => _eventsText;

    public override string Description =>
        "Vypálí události v přesně zapsaných časech — dopad střepu ve 28,4 ms se silou 0,8, série " +
        "přeskoků, kroky. Na rozdíl od Sekvenceru jsou časy v sekundách a místo výšky má každá " +
        "událost sílu. Impuls je jeden vzorek o velikosti síly (rovnou do Banky módů nebo " +
        "Resonatoru jako úder), Trig jeden vzorek 1, Gate drží Délku gate (nebo délku z řádku), " +
        "Síla drží sílu poslední události. Opakovat Ano přehrává dokola po Periodě; události po " +
        "konci periody se pak nepřehrají. Reset (náběžná hrana) začne od začátku. Čas se počítá " +
        "na vzorky přesně a nezávisí na velikosti bloku.";

    public override void Prepare() => EnsureParsed();

    public override void ResetState()
    {
        _sample = 0;
        _next = 0;
        _loopIndex = 0;
        _gateSamplesLeft = 0;
        _strength = 0;
        _previousReset = 0;
    }

    private void EnsureParsed()
    {
        var text = _eventsText.Text;
        if (_parsedFrom == text)
            return;

        _parsedFrom = text;
        if (TryParse(text, out var events, out var error))
            _events = events;
        _eventsText.Error = error;
    }

    internal static bool TryParse(string text, out Event[] events, out string? error)
    {
        var list = new List<Event>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0)
                line = line[..hash];

            var fields = line.Split(new[] { ' ', '\t', ';', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
                continue;

            var numbers = new double[fields.Length];
            for (var f = 0; f < fields.Length; f++)
            {
                if (!double.TryParse(fields[f].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[f])
                    || double.IsNaN(numbers[f]) || double.IsInfinity(numbers[f]))
                {
                    events = Array.Empty<Event>();
                    error = $"Řádek {lineNumber}: „{fields[f]}“ není číslo.";
                    return false;
                }
            }

            if (numbers.Length is < 2 or > 3)
            {
                events = Array.Empty<Event>();
                error = $"Řádek {lineNumber}: čekám čas a sílu (a nepovinně gate), je tu {numbers.Length} čísel.";
                return false;
            }

            if (numbers[0] < 0 || numbers[1] < 0 || (numbers.Length == 3 && numbers[2] <= 0))
            {
                events = Array.Empty<Event>();
                error = $"Řádek {lineNumber}: čas a síla nesmí být záporné, gate musí být kladný.";
                return false;
            }

            list.Add(new Event(numbers[0], numbers[1], numbers.Length == 3 ? numbers[2] : double.NaN));
            if (list.Count > MaxEvents)
            {
                events = Array.Empty<Event>();
                error = $"Víc než {MaxEvents} událostí.";
                return false;
            }
        }

        // Stable sort: equal times keep their written order, which only matters for Síla's held value.
        events = list.Select((e, i) => (e, i)).OrderBy(x => x.e.Time).ThenBy(x => x.i).Select(x => x.e).ToArray();
        error = events.Length == 0 ? "Žádná událost — modul mlčí." : null;
        return true;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        EnsureParsed();
        var events = _events;
        var hasReset = inputBuffers.TryGetValue(_reset, out var reset);

        var impulse = NewBuffer(ctx);
        var trig = NewBuffer(ctx);
        var gate = NewBuffer(ctx);
        var strength = NewBuffer(ctx);

        var fs = (double)ctx.SampleRate;
        var looping = (int)Math.Round(_loop.Value) == 1;
        var period = Math.Max(0.001, _period.Value);
        var defaultGate = Math.Max(0.0005, _gateLength.Value);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            if (hasReset)
            {
                if (reset![i] >= 0.5 && _previousReset < 0.5)
                {
                    _sample = 0;
                    _next = 0;
                    _loopIndex = 0;
                }
                _previousReset = reset[i];
            }

            // Events whose sample has come (or, after a Reset or a lowered period, has already gone)
            // fire now — a while, because several can share one sample.
            while (true)
            {
                if (_next >= events.Length)
                {
                    if (!looping)
                        break;
                    var nextLoopStart = (long)Math.Round((_loopIndex + 1) * period * fs);
                    if (_sample < nextLoopStart)
                        break;
                    _loopIndex++;
                    _next = 0;
                    continue;
                }

                var ev = events[_next];
                if (looping && ev.Time >= period)
                {
                    // Past the end of the period: never plays in a loop; wait for the next lap.
                    _next = events.Length;
                    continue;
                }

                var at = (long)Math.Round(((looping ? _loopIndex * period : 0) + ev.Time) * fs);
                if (at > _sample)
                    break;

                impulse[i] += (float)ev.Strength;
                trig[i] = 1;
                _strength = (float)ev.Strength;
                var gateSeconds = double.IsNaN(ev.Gate) ? defaultGate : ev.Gate;
                _gateSamplesLeft = Math.Max(_gateSamplesLeft, Math.Max(1, (int)Math.Round(gateSeconds * fs)));
                _next++;
            }

            if (_gateSamplesLeft > 0)
            {
                gate[i] = 1;
                _gateSamplesLeft--;
            }

            strength[i] = _strength;
            _sample++;
        }

        return new Dictionary<Port, float[]>
        {
            [_impulseOutput] = impulse,
            [_trigOutput] = trig,
            [_gateOutput] = gate,
            [_strengthOutput] = strength,
        };
    }
}
