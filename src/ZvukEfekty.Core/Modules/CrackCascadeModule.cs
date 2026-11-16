using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// A cascade of cracks — ice freezing over, wood splitting, glaze crazing, a stone fracturing
/// (todo 18d, P08). A crack fires, then after a random delay spawns smaller cracks that spawn
/// smaller ones, each quieter, higher and shorter than its parent. Inspired by the split between
/// fracture impulses and modal ringing in Zheng &amp; James, <i>Rigid-Body Fracture Sound</i> — a
/// cheap event cascade, not their solver.
///
/// <para><b>What it replaces.</b> The ice V3 faked branching with four fixed DelayLines feeding
/// four Resonators: every main crack had the same two branches at 17/39 ms and the same two
/// twigs at 48/65 ms, always at the same pitches. Here each crack draws its own children.</para>
///
/// <para><b>Bounded by construction.</b> Children together get at most Poměr energie of the
/// parent's energy, depth stops at Max hloubka, and pending cracks live in a fixed queue that
/// drops (and counts) what does not fit. Nothing grows without a cause: no Trig and no Napětí
/// means silence.</para>
/// </summary>
public sealed class CrackCascadeModule : ModuleBase
{
    private const int MaxVoices = 64;
    private const int MaxPending = 256;
    private const double SilentEnvelope = 1e-4;

    private readonly Port _trigger;
    private readonly Port _output;
    private readonly Port _impulseOutput;
    private readonly Port _trigOutput;
    private readonly ModuleParameter _strength;
    private readonly ModuleParameter _stress;
    private readonly ModuleParameter _rootRate;
    private readonly ModuleParameter _branching;
    private readonly ModuleParameter _maxChildren;
    private readonly ModuleParameter _maxDepth;
    private readonly ModuleParameter _delayMin;
    private readonly ModuleParameter _delayMax;
    private readonly ModuleParameter _energyRatio;
    private readonly ModuleParameter _variation;
    private readonly ModuleParameter _pitch;
    private readonly ModuleParameter _ring;
    private readonly ModuleParameter _noiseShare;
    private readonly ModuleParameter _level;
    private readonly SeededRandomSource _randomSource;

    // Pending cracks: due sample, energy, depth, scale. Unordered; the queue is small and scanned.
    private readonly bool[] _pendingUsed = new bool[MaxPending];
    private readonly long[] _pendingAt = new long[MaxPending];
    private readonly double[] _pendingEnergy = new double[MaxPending];
    private readonly int[] _pendingDepth = new int[MaxPending];
    private readonly double[] _pendingScale = new double[MaxPending];
    private int _pendingCount;

    private readonly bool[] _active = new bool[MaxVoices];
    private readonly double[] _tone = new double[MaxVoices];
    private readonly double[] _toneEnv = new double[MaxVoices];
    private readonly double[] _toneStep = new double[MaxVoices];
    private readonly double[] _sin = new double[MaxVoices];
    private readonly double[] _cos = new double[MaxVoices];
    private readonly double[] _rotSin = new double[MaxVoices];
    private readonly double[] _rotCos = new double[MaxVoices];
    private readonly double[] _click = new double[MaxVoices];
    private readonly double[] _clickEnv = new double[MaxVoices];
    private readonly double[] _clickStep = new double[MaxVoices];

    private long _sample;
    private double _progress;
    private double _interval;
    private double _previousTrigger;
    private ulong _noiseState;

    /// <summary>Cracks thrown away because the pending queue was full — diagnostics, not sound.</summary>
    public long DroppedCracks { get; private set; }

    public CrackCascadeModule() : base("Trhliny")
    {
        _trigger = AddInput("Trig");
        _output = AddOutput("Out");
        _impulseOutput = AddOutput("Impuls");
        _trigOutput = AddOutput("Trig");

        _strength = AddParameter("Síla", 1, 0, 1);
        _stress = AddParameter("Napětí", 0, 0, 1);
        _rootRate = AddParameter("Četnost kořenů", 5, 0, 200, "1/s");
        _branching = AddParameter("Větvení", 0.6, 0, 0.95);
        _maxChildren = AddParameter("Max dětí", 2, 0, 4, modulatable: false);
        _maxDepth = AddParameter("Max hloubka", 3, 0, 8, modulatable: false);
        _delayMin = AddParameter("Zpoždění min", 3, 0.1, 500, "ms");
        _delayMax = AddParameter("Zpoždění max", 60, 0.1, 500, "ms");
        _energyRatio = AddParameter("Poměr energie", 0.5, 0, 1);
        _variation = AddParameter("Variabilita", 0.3, 0, 1);
        _pitch = AddParameter("Výška", 3000, 100, 12000, "Hz");
        _ring = AddParameter("Doznění", 0.022, 0.001, 0.5, "s");
        _noiseShare = AddParameter("Podíl šumu", 0.3, 0, 1);
        _level = AddParameter("Úroveň", 1, 0, 2);
        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Kaskáda trhlin — zamrzání ledu, štípání dřeva, praskání glazury, lom kamene. Trhlina " +
        "vznikne na Trig (o Síle) nebo sama z Napětí × Četnost kořenů; po náhodném zpoždění " +
        "(Zpoždění min–max) z ní s pravděpodobností Větvení vyrazí až Max dětí menších trhlin, které " +
        "dohromady nesou nejvýš Poměr energie rodiče, a tak dál do Max hloubky. Menší trhlina zní " +
        "výš, tišeji a kratší dobu. Každá zní tónem (Výška ÷ velikost, Doznění) a šumovým klikem " +
        "(Podíl šumu). Impuls a Trig hlásí každou trhlinu ven — pro Banku módů nebo Resonator. Bez " +
        "Trig a Napětí je ticho.";

    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Material("Led", "Zamrzání a praskání ledu — vysoké, krátké, hodně větví (z ledu V3). Neposlouchané.",
            branching: 0.7, children: 2, depth: 3, dMin: 5, dMax: 60, ratio: 0.55, pitch: 3800, ring: 0.022, noise: 0.3, level: 0.6),
        Material("Dřevo", "Štípání a praskání dřeva — nižší, méně větví, delší prodlevy. Neposlouchané.",
            branching: 0.5, children: 2, depth: 2, dMin: 10, dMax: 150, ratio: 0.5, pitch: 900, ring: 0.03, noise: 0.55, level: 0.7),
        Material("Glazura", "Jemné praskání glazury, skla v mrazu — vysoké, rychlé, drobné. Neposlouchané.",
            branching: 0.8, children: 3, depth: 4, dMin: 1, dMax: 25, ratio: 0.45, pitch: 5500, ring: 0.008, noise: 0.25, level: 0.5),
        Material("Kámen", "Lom kamene — hluboký hlavní lom, pár úlomků. Neposlouchané.",
            branching: 0.4, children: 2, depth: 2, dMin: 5, dMax: 80, ratio: 0.4, pitch: 600, ring: 0.04, noise: 0.6, level: 0.8),
    };

    private static ModulePreset Material(string name, string description, double branching, double children,
        double depth, double dMin, double dMax, double ratio, double pitch, double ring, double noise, double level)
        => new(name, description, new Dictionary<string, double>
        {
            ["Větvení"] = branching, ["Max dětí"] = children, ["Max hloubka"] = depth, ["Zpoždění min"] = dMin,
            ["Zpoždění max"] = dMax, ["Poměr energie"] = ratio, ["Výška"] = pitch, ["Doznění"] = ring,
            ["Podíl šumu"] = noise, ["Úroveň"] = level,
        });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override void ResetState()
    {
        _randomSource.Reset();
        Array.Clear(_active);
        Array.Clear(_pendingUsed);
        _pendingCount = 0;
        _sample = 0;
        _progress = 0;
        _previousTrigger = 0;
        DroppedCracks = 0;
        var random = _randomSource.GetGenerator(out _);
        _noiseState = (ulong)random.NextInt64() | 1;
        _interval = DrawInterval(random);
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var strength = GetModulatedValues(_strength, ctx, inputBuffers);
        var stress = GetModulatedValues(_stress, ctx, inputBuffers);
        var rootRate = GetModulatedValues(_rootRate, ctx, inputBuffers);
        var branching = GetModulatedValues(_branching, ctx, inputBuffers);
        var delayMin = GetModulatedValues(_delayMin, ctx, inputBuffers);
        var delayMax = GetModulatedValues(_delayMax, ctx, inputBuffers);
        var energyRatio = GetModulatedValues(_energyRatio, ctx, inputBuffers);
        var variation = GetModulatedValues(_variation, ctx, inputBuffers);
        var pitch = GetModulatedValues(_pitch, ctx, inputBuffers);
        var ring = GetModulatedValues(_ring, ctx, inputBuffers);
        var noiseShare = GetModulatedValues(_noiseShare, ctx, inputBuffers);
        var level = GetModulatedValues(_level, ctx, inputBuffers);
        var hasTrigger = inputBuffers.TryGetValue(_trigger, out var trigger);

        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
        {
            _noiseState = (ulong)random.NextInt64() | 1;
            _interval = DrawInterval(random);
        }

        var maxChildren = Math.Clamp((int)Math.Round(_maxChildren.Value), 0, 4);
        var maxDepth = Math.Clamp((int)Math.Round(_maxDepth.Value), 0, 8);
        var output = NewBuffer(ctx);
        var impulse = NewBuffer(ctx);
        var trigOut = NewBuffer(ctx);
        var fs = (double)ctx.SampleRate;
        var dt = 1.0 / fs;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var settings = new CrackSettings(branching[i], maxChildren, maxDepth, delayMin[i], delayMax[i],
                energyRatio[i], variation[i], pitch[i], ring[i], noiseShare[i]);

            if (hasTrigger)
            {
                if (trigger![i] >= 0.5 && _previousTrigger < 0.5)
                    Enqueue(_sample, Math.Clamp(strength[i], 0, 1), 0, 1);
                _previousTrigger = trigger[i];
            }

            while (_progress >= _interval)
            {
                _progress -= _interval;
                _interval = DrawInterval(random);
                Enqueue(_sample, Math.Clamp(strength[i], 0, 1), 0, 1);
            }

            // Fire every pending crack that is due. Children land strictly in the future (≥ 1 sample),
            // so this loop cannot feed itself within one sample.
            for (var p = 0; p < MaxPending && _pendingCount > 0; p++)
            {
                if (!_pendingUsed[p] || _pendingAt[p] > _sample)
                    continue;

                _pendingUsed[p] = false;
                _pendingCount--;
                Fire(random, fs, settings, _pendingEnergy[p], _pendingDepth[p], _pendingScale[p], impulse, trigOut, i);
            }

            double sum = 0;
            for (var v = 0; v < MaxVoices; v++)
            {
                if (!_active[v])
                    continue;

                var s = _sin[v] * _rotCos[v] + _cos[v] * _rotSin[v];
                var c = _cos[v] * _rotCos[v] - _sin[v] * _rotSin[v];
                _sin[v] = s;
                _cos[v] = c;

                _noiseState ^= _noiseState << 13;
                _noiseState ^= _noiseState >> 7;
                _noiseState ^= _noiseState << 17;
                var noise = (_noiseState >> 11) * (2.0 / (1UL << 53)) - 1;

                sum += _tone[v] * _toneEnv[v] * s + _click[v] * _clickEnv[v] * noise;
                _toneEnv[v] *= _toneStep[v];
                _clickEnv[v] *= _clickStep[v];
                if (_toneEnv[v] < SilentEnvelope && _clickEnv[v] < SilentEnvelope)
                    _active[v] = false;
            }

            output[i] = (float)(sum * Math.Max(0, level[i]));
            _progress += Math.Max(0, rootRate[i]) * Math.Clamp(stress[i], 0, 1) * dt;
            _sample++;
        }

        return new Dictionary<Port, float[]>
        {
            [_output] = output,
            [_impulseOutput] = impulse,
            [_trigOutput] = trigOut,
        };
    }

    private readonly record struct CrackSettings(double Branching, int MaxChildren, int MaxDepth,
        double DelayMinMs, double DelayMaxMs, double EnergyRatio, double Variation, double Pitch,
        double Ring, double NoiseShare);

    private void Enqueue(long at, double energy, int depth, double scale)
    {
        if (energy <= 0)
            return;

        for (var p = 0; p < MaxPending; p++)
        {
            if (_pendingUsed[p])
                continue;
            _pendingUsed[p] = true;
            _pendingAt[p] = at;
            _pendingEnergy[p] = energy;
            _pendingDepth[p] = depth;
            _pendingScale[p] = scale;
            _pendingCount++;
            return;
        }
        DroppedCracks++;
    }

    private void Fire(Random random, double fs, CrackSettings s, double energy, int depth, double scale,
        float[] impulse, float[] trigOut, int i)
    {
        impulse[i] += (float)energy;
        trigOut[i] = 1;

        // The children are drawn before the voice so the cascade's shape does not depend on whether
        // a voice was free.
        if (depth < s.MaxDepth && s.MaxChildren > 0)
        {
            var branching = Math.Clamp(s.Branching, 0, 0.95);
            Span<double> weights = stackalloc double[4];
            var count = 0;
            for (var c = 0; c < s.MaxChildren; c++)
                if (random.NextDouble() < branching)
                    weights[count++] = 0.3 + random.NextDouble();

            if (count > 0)
            {
                double total = 0;
                for (var c = 0; c < count; c++)
                    total += weights[c];

                var lo = Math.Max(0.1, Math.Min(s.DelayMinMs, s.DelayMaxMs)) / 1000;
                var hi = Math.Max(lo, Math.Max(s.DelayMinMs, s.DelayMaxMs) / 1000);
                var budget = energy * Math.Clamp(s.EnergyRatio, 0, 1);
                for (var c = 0; c < count; c++)
                {
                    var delay = lo * Math.Pow(hi / lo, random.NextDouble());
                    var at = _sample + Math.Max(1, (long)Math.Round(delay * fs));
                    // Shares sum to exactly the budget; the variation only shades who gets what.
                    var childEnergy = budget * weights[c] / total;
                    var childScale = scale * (0.55 + 0.35 * random.NextDouble());
                    Enqueue(at, childEnergy, depth + 1, childScale);
                }
            }
        }

        var randomGain = 1 - Math.Clamp(s.Variation, 0, 1) * random.NextDouble();
        var phase = random.NextDouble() * 2 * Math.PI;

        var slot = FindSlot();
        var f = Math.Clamp(s.Pitch / scale, 100, 0.45 * fs);
        var amplitude = Math.Sqrt(energy) * randomGain;
        var share = Math.Clamp(s.NoiseShare, 0, 1);
        var toneT60 = Math.Max(0.001, s.Ring) * scale;
        const double clickT60 = 0.0008;

        var omega = 2 * Math.PI * f / fs;
        _sin[slot] = Math.Sin(phase);
        _cos[slot] = Math.Cos(phase);
        _rotSin[slot] = Math.Sin(omega);
        _rotCos[slot] = Math.Cos(omega);
        _tone[slot] = amplitude * (1 - share);
        _toneEnv[slot] = 1;
        _toneStep[slot] = Math.Exp(-6.907755 / (toneT60 * fs));
        _click[slot] = amplitude * share;
        _clickEnv[slot] = 1;
        _clickStep[slot] = Math.Exp(-6.907755 / (clickT60 * fs));
        _active[slot] = true;
    }

    /// <summary>A free voice, or the quietest sounding one — a crack is short, and the quietest is
    /// almost always one that has nearly rung out.</summary>
    private int FindSlot()
    {
        var quietest = 0;
        var quietestLevel = double.MaxValue;
        for (var v = 0; v < MaxVoices; v++)
        {
            if (!_active[v])
                return v;
            var current = Math.Abs(_tone[v] * _toneEnv[v]) + Math.Abs(_click[v] * _clickEnv[v]);
            if (current < quietestLevel)
            {
                quietestLevel = current;
                quietest = v;
            }
        }
        return quietest;
    }

    private static double DrawInterval(Random random) => -Math.Log(1 - random.NextDouble());
}
