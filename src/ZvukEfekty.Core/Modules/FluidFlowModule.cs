using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// A population of resonating air bubbles in water — splashing, gurgling, a puddle stepped in, a
/// whirlpool. One module for what the accepted game patches (todo 18c) built out of two Poisson
/// triggers, two sample-and-holds, eight Formulas and a Swarm.
///
/// <para>The single-bubble model is van den Doel's, as used by Luca Turchet, <i>Footstep sounds
/// synthesis</i> (2016), section 3.2.5 — own implementation of the published equations, no code
/// taken. A bubble of radius r (metres) rings at <c>f0 = 3/r</c>, decays at
/// <c>d = 0.13/r + 0.0072/r^1.5</c> per second, and its pitch rises as it goes:
/// <c>p(t) = A·exp(−d·t)·sin(2π·f0·(1 + ξ·d·t)·t)</c>. ⚠️ The phase is f(t)·t, NOT the integral of
/// f(t), so the instantaneous frequency is <c>f0·(1 + 2ξ·d·t)</c>; kept exactly like this because
/// it is what the accepted patches sound like.</para>
///
/// <para>Radius and excitation are drawn per bubble from power-law densities (<c>r^−α</c>,
/// <c>e^−β</c>) by inverse CDF. Radius is what ties pitch, decay and loudness together — a big
/// bubble is low, long and loud — and that correlation is the whole point; drawing them
/// independently gives the "xylophone" the early attempts sounded like.</para>
/// </summary>
public sealed class FluidFlowModule : ModuleBase
{
    /// <summary>Hard ceiling on simultaneous bubbles; the arrays are this big so nothing grows
    /// while rendering. "Strop" picks how many of them are used.</summary>
    private const int MaxBubbles = 128;

    /// <summary>Scale of A = k·r·e, with r in metres. 1600 is what the accepted splash (V8) and
    /// puddle (V10) used, so Úroveň 1 reproduces their level; the whirlpool used 900 (Úroveň 0.56).</summary>
    private const double AmplitudeScale = 1600;

    /// <summary>A bubble is dropped once its own envelope falls below this, −80 dB of its start.</summary>
    private const double SilentEnvelope = 1e-4;

    private static readonly string[] WhenFullNames = { "Ukrást nejslabší", "Zahodit bublinu" };
    private static readonly string[] FirstBubbleNames = { "Náhodně", "Hned" };

    private readonly Port _trigger;
    private readonly Port _output;
    private readonly ModuleParameter _activity;
    private readonly ModuleParameter _rate;
    private readonly ModuleParameter _radiusMin;
    private readonly ModuleParameter _radiusMax;
    private readonly ModuleParameter _radiusExponent;
    private readonly ModuleParameter _excitationMin;
    private readonly ModuleParameter _excitationMax;
    private readonly ModuleParameter _excitationExponent;
    private readonly ModuleParameter _damping;
    private readonly ModuleParameter _rise;
    private readonly ModuleParameter _level;
    private readonly ModuleParameter _capacity;
    private readonly ModuleParameter _whenFull;
    private readonly ModuleParameter _firstBubble;
    private readonly SeededRandomSource _randomSource;

    // One slot per bubble. Phase is advanced by a first difference that itself grows by a constant
    // second difference — exact for the quadratic phase 2π·f0·(t + ξ·d·t²), and one add per sample
    // instead of a multiply-heavy re-evaluation.
    private readonly bool[] _active = new bool[MaxBubbles];
    private readonly double[] _amplitude = new double[MaxBubbles];
    private readonly double[] _envelope = new double[MaxBubbles];
    private readonly double[] _envelopeStep = new double[MaxBubbles];
    private readonly double[] _phase = new double[MaxBubbles];
    private readonly double[] _phaseStep = new double[MaxBubbles];
    private readonly double[] _phaseStepGrowth = new double[MaxBubbles];

    /// <summary>Progress of the birth process in mean intervals, as in the Poisson trigger: it grows
    /// by λ·dt, so modulating Aktivita or Četnost mid-gap shortens the gap already running.</summary>
    private double _progress;
    private double _interval;

    /// <summary>Set by <see cref="ResetState"/> when První bublina is Hned: one bubble at the first
    /// sample, so a single drop needs no trigger source and does not wait out a random gap.</summary>
    private bool _pendingFirst;
    private double _previousTrigger;

    public FluidFlowModule() : base("Bubliny")
    {
        _trigger = AddInput("Trig");
        _output = AddOutput("Out");

        _activity = AddParameter("Aktivita", 0.5, 0, 1);
        _rate = AddParameter("Četnost", 350, 0, 2000, "1/s");
        _radiusMin = AddParameter("Poloměr min", 0.45, 0.2, 20, "mm");
        _radiusMax = AddParameter("Poloměr max", 4.5, 0.2, 20, "mm");
        _radiusExponent = AddParameter("Exponent poloměru", 2.2, 0, 5);
        _excitationMin = AddParameter("Buzení min", 0.01, 0, 1);
        _excitationMax = AddParameter("Buzení max", 0.1, 0, 1);
        _excitationExponent = AddParameter("Exponent buzení", 2, 0, 5);
        _damping = AddParameter("Útlum", 1, 0.1, 10, "×");
        _rise = AddParameter("Stoupání", 0.1, 0, 0.3);
        _level = AddParameter("Úroveň", 1, 0, 2);
        _capacity = AddParameter("Strop", 32, 1, MaxBubbles, modulatable: false);
        _whenFull = AddParameter("Plný strop", 0, 0, WhenFullNames.Length - 1, options: WhenFullNames);
        _firstBubble = AddParameter("První bublina", 0, 0, FirstBubbleNames.Length - 1, options: FirstBubbleNames);
        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Populace bublin ve vodě (van den Doel / Turchet) — šplouchnutí, klokotání, kaluž, vír. " +
        "Každá bublina zvoní podle svého poloměru: malá vysoko a krátce, velká hluboko, dlouho a " +
        "hlasitě, a během doznívání trochu stoupá (Stoupání). Aktivita 0–1 řídí, kolik bublin " +
        "vzniká (Četnost × Aktivita za sekundu) a do 0,25 i jak silné jsou; 0 = žádné nové. " +
        "Poloměr, buzení, útlum a stoupání se čtou v okamžiku zrodu bubliny. Úroveň 1 odpovídá " +
        "přijatému šplouchnutí V8 — za modul dej low-pass kolem 6 kHz jako tam. " +
        "Trig = jedna bublina hned na náběžnou hranu, První bublina Hned = jedna hned na začátku; " +
        "obě v plné síle bez ohledu na Aktivitu. Jedna samotná bublina: Četnost 0 a První bublina Hned.";

    /// <summary>Kinds of water. Aktivita is deliberately left out of every one: it is "how much is
    /// happening right now" and is usually driven by a curve, which a preset must not fight. Only
    /// Šplouchnutí and Vodní vír are the accepted patches' numbers; the rest are starting points
    /// chosen from the physics (bubble sizes, rates) and measured for level, not yet listened to.</summary>
    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Water("Šplouchnutí", "Přijaté šplouchnutí a kaluž (V8, V10): hodně malých bublin, rychle odezní.",
            rate: 350, rMin: 0.45, rMax: 4.5, rExp: 2.2, eMin: 0.01, eMax: 0.1, damping: 1, rise: 0.1, level: 1, cap: 32),
        Water("Vodní vír", "Přijatý vodní vír (exp. 10): totéž jako šplouchnutí, o 5 dB tišší na delší proud.",
            rate: 350, rMin: 0.45, rMax: 4.5, rExp: 2.2, eMin: 0.01, eMax: 0.1, damping: 1, rise: 0.1, level: 0.5625, cap: 32),
        Water("Potok", "Hustý tok drobných bublin — potok, tekoucí voda. Výchozí bod, neposlouchané.",
            rate: 900, rMin: 0.3, rMax: 3, rExp: 2.6, eMin: 0.005, eMax: 0.05, damping: 1, rise: 0.1, level: 1.6, cap: 64),
        Water("Vroucí voda", "Mnoho malých, sušších bublin bez velkého stoupání. Výchozí bod, neposlouchané.",
            rate: 600, rMin: 0.3, rMax: 1.6, rExp: 1.8, eMin: 0.01, eMax: 0.08, damping: 1.3, rise: 0.05, level: 2, cap: 64),
        Water("Klokotání / lektvar", "Řídké velké bubliny, dlouho zvoní a stoupají — kotlík, lektvar. Výchozí bod, neposlouchané.",
            rate: 25, rMin: 2, rMax: 9, rExp: 1.2, eMin: 0.03, eMax: 0.15, damping: 0.6, rise: 0.15, level: 0.3, cap: 16),
        Water("Kapky do vody", "Jednotlivé velké kapky s výrazným stoupnutím — kapání do kbelíku. Výchozí bod, neposlouchané.",
            rate: 6, rMin: 2.5, rMax: 5, rExp: 1, eMin: 0.1, eMax: 0.3, damping: 1, rise: 0.25, level: 0.27, cap: 8),
    };

    private static ModulePreset Water(string name, string description, double rate, double rMin, double rMax,
        double rExp, double eMin, double eMax, double damping, double rise, double level, double cap)
        => new(name, description, new Dictionary<string, double>
        {
            ["Četnost"] = rate, ["Poloměr min"] = rMin, ["Poloměr max"] = rMax, ["Exponent poloměru"] = rExp,
            ["Buzení min"] = eMin, ["Buzení max"] = eMax, ["Exponent buzení"] = 2, ["Útlum"] = damping,
            ["Stoupání"] = rise, ["Úroveň"] = level, ["Strop"] = cap,
        });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override void ResetState()
    {
        _randomSource.Reset();
        Array.Clear(_active);
        _progress = 0;
        _interval = DrawInterval(_randomSource.GetGenerator(out _));
        _pendingFirst = (int)Math.Round(_firstBubble.Value) == 1;
        _previousTrigger = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var activity = GetModulatedValues(_activity, ctx, inputBuffers);
        var rate = GetModulatedValues(_rate, ctx, inputBuffers);
        var radiusMin = GetModulatedValues(_radiusMin, ctx, inputBuffers);
        var radiusMax = GetModulatedValues(_radiusMax, ctx, inputBuffers);
        var radiusExponent = GetModulatedValues(_radiusExponent, ctx, inputBuffers);
        var excitationMin = GetModulatedValues(_excitationMin, ctx, inputBuffers);
        var excitationMax = GetModulatedValues(_excitationMax, ctx, inputBuffers);
        var excitationExponent = GetModulatedValues(_excitationExponent, ctx, inputBuffers);
        var damping = GetModulatedValues(_damping, ctx, inputBuffers);
        var rise = GetModulatedValues(_rise, ctx, inputBuffers);
        var level = GetModulatedValues(_level, ctx, inputBuffers);
        var hasTrigger = inputBuffers.TryGetValue(_trigger, out var trigger);

        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            _interval = DrawInterval(random);

        var capacity = Math.Clamp((int)Math.Round(_capacity.Value), 1, MaxBubbles);
        var steal = (int)Math.Round(_whenFull.Value) == 0;
        var output = NewBuffer(ctx);
        var fs = (double)ctx.SampleRate;
        var dt = 1.0 / fs;
        // Instantaneous frequency is faded out between these, so a small bubble whose pitch rises
        // towards Nyquist disappears smoothly instead of folding back down as an alias.
        var fadeStart = 0.40 * fs;
        var fadeEnd = 0.45 * fs;
        var radiansToHz = fs / (2 * Math.PI);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var act = Math.Clamp(activity[i], 0, 1);

            // A bubble asked for outright (first sample, Trig edge) sounds at full strength: below
            // Aktivita 0.25 births are scaled down, and a single drop must not be quiet for it.
            var asked = _pendingFirst;
            _pendingFirst = false;
            if (hasTrigger)
            {
                if (trigger![i] >= 0.5 && _previousTrigger < 0.5)
                    asked = true;
                _previousTrigger = trigger[i];
            }
            if (asked)
                Spawn(random, capacity, steal, fs, 1,
                    radiusMin[i], radiusMax[i], radiusExponent[i],
                    excitationMin[i], excitationMax[i], excitationExponent[i],
                    damping[i], rise[i], fadeEnd);

            // A while, not an if: at 2000/s a sample can (rarely) hold two births.
            while (_progress >= _interval)
            {
                _progress -= _interval;
                _interval = DrawInterval(random);
                Spawn(random, capacity, steal, fs, act,
                    radiusMin[i], radiusMax[i], radiusExponent[i],
                    excitationMin[i], excitationMax[i], excitationExponent[i],
                    damping[i], rise[i], fadeEnd);
            }

            double sum = 0;
            for (var b = 0; b < capacity; b++)
            {
                if (!_active[b])
                    continue;

                var instantHz = _phaseStep[b] * radiansToHz;
                var alias = instantHz <= fadeStart ? 1.0
                    : instantHz >= fadeEnd ? 0.0
                    : (fadeEnd - instantHz) / (fadeEnd - fadeStart);

                sum += _amplitude[b] * _envelope[b] * alias * Math.Sin(_phase[b]);

                _phase[b] += _phaseStep[b];
                if (_phase[b] > 2 * Math.PI)
                    _phase[b] -= 2 * Math.PI;
                _phaseStep[b] += _phaseStepGrowth[b];
                _envelope[b] *= _envelopeStep[b];

                if (_envelope[b] < SilentEnvelope || (alias == 0 && instantHz >= fadeEnd))
                    _active[b] = false;
            }

            output[i] = (float)(sum * Math.Max(0, level[i]));
            _progress += Math.Max(0, rate[i]) * act * dt;
        }

        // Slots above a lowered Strop would otherwise keep ringing unheard and come back when it rises.
        for (var b = capacity; b < MaxBubbles; b++)
            _active[b] = false;

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    private void Spawn(Random random, int capacity, bool steal, double fs, double activity,
        double radiusMinMm, double radiusMaxMm, double radiusExponent,
        double excitationMin, double excitationMax, double excitationExponent,
        double dampingScale, double rise, double nyquistGuard)
    {
        // Draw everything first so the random sequence does not depend on whether a slot is free —
        // otherwise Strop would change which bubbles come out, not just how many.
        var radius = DrawPowerLaw(random.NextDouble(), radiusMinMm, radiusMaxMm, radiusExponent) * 0.001;
        var excitation = DrawPowerLaw(random.NextDouble(),
            Math.Clamp(excitationMin, 0, 1), Math.Clamp(excitationMax, 0, 1), excitationExponent);

        var f0 = 3 / radius;
        if (f0 >= nyquistGuard)
            return;

        var slot = FindSlot(capacity, steal);
        if (slot < 0)
            return;

        var d = Math.Clamp(dampingScale, 0.1, 10) * (0.13 / radius + 0.0072 / Math.Pow(radius, 1.5));
        var xi = Math.Clamp(rise, 0, 0.3);
        var dtSquared = 1.0 / (fs * fs);

        // θ(n) = 2π·f0·(n/fs + ξ·d·n²/fs²); first difference at n = 0 and its constant growth.
        _phase[slot] = 0;
        _phaseStep[slot] = 2 * Math.PI * f0 * (1 / fs + xi * d * dtSquared);
        _phaseStepGrowth[slot] = 2 * Math.PI * f0 * xi * d * 2 * dtSquared;
        _envelope[slot] = 1;
        _envelopeStep[slot] = Math.Exp(-d / fs);
        // min(1, 4·Aktivita): a slow trickle also makes quieter bubbles — kept from V8, where it
        // is what made the splash tail off naturally instead of thinning out at full strength.
        _amplitude[slot] = AmplitudeScale * radius * excitation * Math.Min(1, 4 * activity);
        _active[slot] = true;
    }

    private int FindSlot(int capacity, bool steal)
    {
        for (var b = 0; b < capacity; b++)
            if (!_active[b])
                return b;

        if (!steal)
            return -1;

        // The quietest one right now, not the oldest: a bubble that has nearly died away is
        // inaudible when cut, while the oldest one may be the big low bubble still ringing.
        var quietest = 0;
        var quietestLevel = double.MaxValue;
        for (var b = 0; b < capacity; b++)
        {
            var current = _amplitude[b] * _envelope[b];
            if (current < quietestLevel)
            {
                quietestLevel = current;
                quietest = b;
            }
        }
        return quietest;
    }

    /// <summary>Inverse CDF of a density proportional to x^−exponent on [min, max]. Exponent 1 is
    /// log-uniform; for exponent ≥ 1 the lower bound must stay above zero, so it is floored.</summary>
    internal static double DrawPowerLaw(double u, double min, double max, double exponent)
    {
        if (min > max)
            (min, max) = (max, min);
        min = Math.Max(min, 1e-6);
        max = Math.Max(max, min);
        if (max - min < 1e-12)
            return min;

        if (Math.Abs(exponent - 1) < 1e-9)
            return min * Math.Pow(max / min, u);

        var k = 1 - exponent;
        var lo = Math.Pow(min, k);
        var hi = Math.Pow(max, k);
        return Math.Pow(lo + (hi - lo) * u, 1 / k);
    }

    /// <summary>Exponential gap in mean intervals — births are a Poisson process.</summary>
    private static double DrawInterval(Random random) => -Math.Log(1 - random.NextDouble());
}
