using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// A population of small hard collisions — gravel underfoot, pebbles in a hand, crunching, a
/// rattle (todo 18d, P05). Own simplification of the principle behind Perry Cook's PhISEM (STK
/// <c>Shakers.cpp</c>, studied, no code taken): a pool of energy that excitation fills and that
/// leaks away, collisions whose rate follows the energy, and each collision a short burst.
///
/// <para><b>Why not Poisson → filter, as the gravel V6 did.</b> There every collision was a
/// one-sample impulse into the SAME band-pass, so all stones had one spectrum and the result read
/// as a texture, not as stones. Here every collision is a different stone: its pitch is drawn
/// log-uniformly between Výška min and max, and size ties the rest together — a lower (bigger)
/// stone is louder and rings longer, by <c>sqrt(fMin/f)</c>. The same lesson as the bubbles,
/// where independent draws gave a xylophone.</para>
///
/// <para>Per collision: a decaying sine (the stone ringing) plus a decaying noise click (the
/// contact), mixed by Podíl šumu; Tvrdost shortens the click from 2 ms to 0.1 ms. Energy follows
/// Aktivita with the time constant of Doznívání energie (T60), and each rising edge on Trig adds
/// Energie on top — so a step is either a curve into Aktivita or one Trig.</para>
/// </summary>
public sealed class CollisionPopulationModule : ModuleBase
{
    private const int MaxVoices = 128;
    private const double SilentEnvelope = 1e-4;
    private const double MaxEnergy = 4;

    private static readonly string[] WhenFullNames = { "Ukrást nejslabší", "Zahodit kolizi" };

    private readonly Port _trigger;
    private readonly Port _output;
    private readonly ModuleParameter _activity;
    private readonly ModuleParameter _energy;
    private readonly ModuleParameter _energyDecay;
    private readonly ModuleParameter _rate;
    private readonly ModuleParameter _pitchMin;
    private readonly ModuleParameter _pitchMax;
    private readonly ModuleParameter _ring;
    private readonly ModuleParameter _hardness;
    private readonly ModuleParameter _noiseShare;
    private readonly ModuleParameter _variation;
    private readonly ModuleParameter _level;
    private readonly ModuleParameter _capacity;
    private readonly ModuleParameter _whenFull;
    private readonly SeededRandomSource _randomSource;

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

    private double _poolEnergy;
    private double _progress;
    private double _interval;
    private double _previousTrigger;

    /// <summary>Click noise has its own generator, seeded from the module's seed at reset: drawing it
    /// from the collision generator would make the collision sequence depend on how many voices are
    /// sounding, and so on Strop.</summary>
    private ulong _noiseState;

    public CollisionPopulationModule() : base("Kolize")
    {
        _trigger = AddInput("Trig");
        _output = AddOutput("Out");

        _activity = AddParameter("Aktivita", 0.5, 0, 1);
        _energy = AddParameter("Energie", 0.5, 0, 1);
        _energyDecay = AddParameter("Doznívání energie", 0.3, 0.02, 5, "s");
        _rate = AddParameter("Četnost", 80, 0, 2000, "1/s");
        _pitchMin = AddParameter("Výška min", 1200, 100, 12000, "Hz");
        _pitchMax = AddParameter("Výška max", 5000, 100, 12000, "Hz");
        _ring = AddParameter("Doznění", 0.015, 0.001, 0.5, "s");
        _hardness = AddParameter("Tvrdost", 0.5, 0, 1);
        _noiseShare = AddParameter("Podíl šumu", 0.4, 0, 1);
        _variation = AddParameter("Variabilita", 0.5, 0, 1);
        _level = AddParameter("Úroveň", 1, 0, 2);
        _capacity = AddParameter("Strop", 32, 1, MaxVoices, modulatable: false);
        _whenFull = AddParameter("Plný strop", 0, 0, WhenFullNames.Length - 1, options: WhenFullNames);
        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Populace drobných tvrdých kolizí — štěrk pod nohou, kamínky v dlani, křupání, chrastítko. " +
        "Energie se plní Aktivitou (plynule, sem patří křivka zatížení) a rázem na Trig (o Energii) " +
        "a uniká s Doznívání energie; kolizí je Četnost × energie za sekundu. Každá kolize je jiný " +
        "kámen: výška náhodně mezi Výška min a max, větší (nižší) kámen je hlasitější a zní déle. " +
        "Zní krátkým tónem (Doznění) a šumovým klikem (Podíl šumu); Tvrdost zkracuje klik. Bez " +
        "Aktivity i Trig je ticho.";

    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Material("Štěrk", "Hrubší štěrk pod nohou — kameny různé velikosti, tvrdý kontakt. Neposlouchané.",
            decay: 0.25, rate: 140, fMin: 900, fMax: 5500, ring: 0.012, hard: 0.6, noise: 0.45, variation: 0.6, level: 0.63, cap: 48),
        Material("Drobné kamínky", "Jemný štěrk, oblázky — výš, hustěji, kratší. Neposlouchané.",
            decay: 0.2, rate: 300, fMin: 2500, fMax: 9000, ring: 0.006, hard: 0.7, noise: 0.4, variation: 0.5, level: 0.6, cap: 64),
        Material("Velké kameny", "Řídké dopady větších kamenů — sypání suti. Neposlouchané.",
            decay: 0.5, rate: 25, fMin: 300, fMax: 1800, ring: 0.05, hard: 0.5, noise: 0.35, variation: 0.6, level: 0.72, cap: 16),
        Material("Písek", "Hodně slabých, skoro jen šumových kolizí. Neposlouchané.",
            decay: 0.15, rate: 1500, fMin: 3000, fMax: 11000, ring: 0.002, hard: 0.9, noise: 0.85, variation: 0.7, level: 0.59, cap: 128),
        Material("Chrastítko", "Semínka v tykvi — na Trig zatřást, energie pak doznívá. Neposlouchané.",
            decay: 0.35, rate: 400, fMin: 2500, fMax: 7000, ring: 0.004, hard: 0.6, noise: 0.6, variation: 0.4, level: 0.53, cap: 64),
    };

    private static ModulePreset Material(string name, string description, double decay, double rate,
        double fMin, double fMax, double ring, double hard, double noise, double variation, double level, double cap)
        => new(name, description, new Dictionary<string, double>
        {
            ["Doznívání energie"] = decay, ["Četnost"] = rate, ["Výška min"] = fMin, ["Výška max"] = fMax,
            ["Doznění"] = ring, ["Tvrdost"] = hard, ["Podíl šumu"] = noise, ["Variabilita"] = variation,
            ["Úroveň"] = level, ["Strop"] = cap,
        });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override void ResetState()
    {
        _randomSource.Reset();
        Array.Clear(_active);
        _poolEnergy = 0;
        _progress = 0;
        _previousTrigger = 0;
        var random = _randomSource.GetGenerator(out _);
        _noiseState = (ulong)random.NextInt64() | 1;
        _interval = DrawInterval(random);
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var activity = GetModulatedValues(_activity, ctx, inputBuffers);
        var energy = GetModulatedValues(_energy, ctx, inputBuffers);
        var energyDecay = GetModulatedValues(_energyDecay, ctx, inputBuffers);
        var rate = GetModulatedValues(_rate, ctx, inputBuffers);
        var pitchMin = GetModulatedValues(_pitchMin, ctx, inputBuffers);
        var pitchMax = GetModulatedValues(_pitchMax, ctx, inputBuffers);
        var ring = GetModulatedValues(_ring, ctx, inputBuffers);
        var hardness = GetModulatedValues(_hardness, ctx, inputBuffers);
        var noiseShare = GetModulatedValues(_noiseShare, ctx, inputBuffers);
        var variation = GetModulatedValues(_variation, ctx, inputBuffers);
        var level = GetModulatedValues(_level, ctx, inputBuffers);
        var hasTrigger = inputBuffers.TryGetValue(_trigger, out var trigger);

        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
        {
            _noiseState = (ulong)random.NextInt64() | 1;
            _interval = DrawInterval(random);
        }

        var capacity = Math.Clamp((int)Math.Round(_capacity.Value), 1, MaxVoices);
        var steal = (int)Math.Round(_whenFull.Value) == 0;
        var output = NewBuffer(ctx);
        var fs = (double)ctx.SampleRate;
        var dt = 1.0 / fs;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            if (hasTrigger)
            {
                if (trigger![i] >= 0.5 && _previousTrigger < 0.5)
                    _poolEnergy = Math.Min(MaxEnergy, _poolEnergy + Math.Clamp(energy[i], 0, 1));
                _previousTrigger = trigger[i];
            }

            // E relaxes towards Aktivita with τ = T60 / ln(1000): a steady Aktivita a gives E = a,
            // a Trig bump on top leaks away in Doznívání energie.
            var tau = Math.Max(0.02, energyDecay[i]) / 6.907755;
            var pull = 1 - Math.Exp(-dt / tau);
            _poolEnergy += (Math.Clamp(activity[i], 0, 1) - _poolEnergy) * pull;
            if (_poolEnergy < 1e-9)
                _poolEnergy = 0;

            while (_progress >= _interval)
            {
                _progress -= _interval;
                _interval = DrawInterval(random);
                Spawn(random, capacity, steal, fs, _poolEnergy, pitchMin[i], pitchMax[i], ring[i],
                    hardness[i], noiseShare[i], variation[i]);
            }

            double sum = 0;
            for (var v = 0; v < capacity; v++)
            {
                if (!_active[v])
                    continue;

                // Rotating phasor: one multiply-add pair per sample instead of a sin call.
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
            _progress += Math.Max(0, rate[i]) * _poolEnergy * dt;
        }

        for (var v = capacity; v < MaxVoices; v++)
            _active[v] = false;

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    private void Spawn(Random random, int capacity, bool steal, double fs, double energy,
        double pitchMin, double pitchMax, double ring, double hardness, double noiseShare, double variation)
    {
        // Draws first, so Strop changes how many collisions sound, not which ones.
        var lo = Math.Clamp(Math.Min(pitchMin, pitchMax), 100, 0.45 * fs);
        var hi = Math.Clamp(Math.Max(pitchMin, pitchMax), lo, 0.45 * fs);
        var f = lo * Math.Pow(hi / lo, random.NextDouble());
        var randomGain = 1 - Math.Clamp(variation, 0, 1) * random.NextDouble();
        var phase = random.NextDouble() * 2 * Math.PI;

        var slot = FindSlot(capacity, steal);
        if (slot < 0)
            return;

        var size = Math.Sqrt(lo / f); // 1 for the biggest stone, smaller for the high ones
        var amplitude = Math.Min(energy, MaxEnergy) * randomGain * size;
        var share = Math.Clamp(noiseShare, 0, 1);

        var toneT60 = Math.Max(0.001, ring) * size;
        var clickT60 = 0.002 - 0.0019 * Math.Clamp(hardness, 0, 1);

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

    private int FindSlot(int capacity, bool steal)
    {
        for (var v = 0; v < capacity; v++)
            if (!_active[v])
                return v;
        if (!steal)
            return -1;

        var quietest = 0;
        var quietestLevel = double.MaxValue;
        for (var v = 0; v < capacity; v++)
        {
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
