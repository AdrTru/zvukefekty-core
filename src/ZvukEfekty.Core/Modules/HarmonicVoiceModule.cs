using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// A harmonic voice source for animals and monsters — howl, growl, scream (todo 18e). Only the
/// excitation: the colour comes from what follows (a Filter for a first comparison, later a vocal
/// tract), so a source can be swapped against an unchanged articulation.
///
/// <para>Own implementation of principles published with Soundgen (Anikin 2019 — harmonic source
/// with spectral tilt, subharmonics, jitter, shimmer, aspiration); Soundgen is GPL and none of its
/// code or presets is taken. What failed before is why it is built like this: the experiment-13
/// voice was a pulse pair through parallel formants and was heard as noise ("frkačka").</para>
///
/// <list type="bullet">
/// <item><b>Harmonics</b> <c>a_k = k^(Sklon/6.02)</c>, k up to the chosen count and below 0.45·fs
/// (faded from 0.40·fs), summed with the Chebyshev recurrence <c>sin kφ = 2cosφ·sin(k−1)φ −
/// sin(k−2)φ</c> — one multiply-add per harmonic per sample. Scaled by 1/Σa_k over the full count,
/// a fixed number, so loudness is not normalised per note.</item>
/// <item><b>Subharmonics</b> at j·f0/R for j not a multiple of R. Their phase is built from a count
/// of whole fundamental periods plus the current phase — dividing the wrapped phase by R would
/// jump every period.</item>
/// <item><b>Jitter and shimmer</b> are drawn once per period, low-pass correlated between periods
/// and clamped; shimmer is ramped across the period. Nothing jumps per sample.</item>
/// <item><b>Dech</b> (aspiration) is band-limited noise, modulated with the glottal cycle so it
/// fuses with the voice instead of sitting beside it.</item>
/// </list>
/// </summary>
public sealed class HarmonicVoiceModule : ModuleBase
{
    private static readonly string[] SubNames = { "Vypnuto", "÷2", "÷3" };
    private static readonly string[] HarmonicCountNames = { "32", "64", "128" };
    private static readonly int[] HarmonicCounts = { 32, 64, 128 };

    private readonly Port _output;
    private readonly ModuleParameter _frequency;
    private readonly ModuleParameter _intensity;
    private readonly ModuleParameter _tilt;
    private readonly ModuleParameter _subRatio;
    private readonly ModuleParameter _subDepth;
    private readonly ModuleParameter _jitter;
    private readonly ModuleParameter _shimmer;
    private readonly ModuleParameter _breath;
    private readonly ModuleParameter _harmonics;
    private readonly SeededRandomSource _randomSource;

    private double _phase;          // fundamental phase within the current period, 0..2π
    private long _cycle;            // whole fundamental periods since reset
    private double _jitterState;    // correlated jitter, in standard deviations
    private double _periodFactor = 1;
    private double _shimmerState;
    private double _ampFrom = 1;
    private double _ampTo = 1;
    private double _intensitySmooth;
    private double _subSmooth;
    private double _noiseLow;
    private double _noiseLowPrev;
    private double _noiseBand;

    // Harmonic weights k^(Sklon/6.02), recomputed only when Sklon moves. Index = harmonic number;
    // the sub array is indexed by j for the j·f0/R components (R ≤ 3).
    private readonly double[] _weights = new double[129];
    private readonly double[] _subWeights = new double[3 * 128 + 1];
    private int _subWeightsRatio;
    private ulong _noiseState;

    public HarmonicVoiceModule() : base("Harmonický hlas")
    {
        _output = AddOutput("Out");

        _frequency = AddParameter("Frekvence", 120, 20, 4000, "Hz");
        _intensity = AddParameter("Intenzita", 0.5, 0, 1);
        _tilt = AddParameter("Sklon", -12, -30, -3, "dB/okt");
        _subRatio = AddParameter("Subharmonické", 0, 0, SubNames.Length - 1, options: SubNames);
        _subDepth = AddParameter("Hloubka sub", 0, 0, 1);
        _jitter = AddParameter("Jitter", 0, 0, 5, "%");
        _shimmer = AddParameter("Shimmer", 0, 0, 30, "%");
        _breath = AddParameter("Dech", 0, 0, 1);
        _harmonics = AddParameter("Harmonických", 1, 0, HarmonicCountNames.Length - 1, options: HarmonicCountNames);
        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Harmonický zdroj hlasu pro zvířata a nestvůry — vytí, vrčení, výkřik, sténání. Dává jen " +
        "buzení (jako hlasivky); barvu a samohlásku mu dá až filtr nebo rezonátory za ním. Frekvence " +
        "je základní tón (sem Nota.Hz nebo křivka), Intenzita hlasitost (0 = ticho), Sklon jak rychle " +
        "slábnou vyšší harmonické (−6 ostrý, −18 měkký). Subharmonické ÷2/÷3 s Hloubkou sub přidají " +
        "drsné zdvojení periody — vrčení, řev. Jitter rozkolísá délku period, Shimmer jejich " +
        "hlasitost, Dech přimíchá šum výdechu. Nic nepřesáhne 0,45 vzorkovací frekvence.";

    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Voice("Čistý hlas", "Klidná fonace bez drsnosti — základ pro vytí a zpěv.", 220, -12, 0, 0, 0.3, 2, 0.05),
        Voice("Vytí", "Vlčí vytí: měkké harmonické, mírné kolísání, trochu dechu. Výšku veďte křivkou 350–550 Hz.", 450, -14, 0, 0, 0.6, 3, 0.1),
        Voice("Vrčení", "Nízké vrčení: subharmonické ÷2, silný jitter a shimmer, dech.", 85, -9, 1, 0.5, 3, 15, 0.3),
        Voice("Výkřik", "Ostrý výkřik: bohaté harmonické, napětí, trochu dechu. Výška 500–900 Hz.", 700, -6, 0, 0, 1, 6, 0.15),
        Voice("Šepot", "Bez tónu — jen dech, modulovaný hlasivkami. Intenzitu řiďte obálkou.", 150, -18, 0, 0, 0, 0, 1),
    };

    private static ModulePreset Voice(string name, string description, double f0, double tilt, double subRatio,
        double subDepth, double jitter, double shimmer, double breath)
        => new(name, description, new Dictionary<string, double>
        {
            ["Frekvence"] = f0, ["Sklon"] = tilt, ["Subharmonické"] = subRatio, ["Hloubka sub"] = subDepth,
            ["Jitter"] = jitter, ["Shimmer"] = shimmer, ["Dech"] = breath,
        });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override void ResetState()
    {
        _randomSource.Reset();
        _phase = 0;
        _cycle = 0;
        _jitterState = 0;
        _periodFactor = 1;
        _shimmerState = 0;
        _ampFrom = _ampTo = 1;
        _intensitySmooth = Math.Clamp(_intensity.Value, 0, 1);
        _subSmooth = Math.Clamp(_subDepth.Value, 0, 1);
        _noiseLow = _noiseLowPrev = _noiseBand = 0;
        _noiseState = (ulong)_randomSource.GetGenerator(out _).NextInt64() | 1;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var frequency = GetModulatedValues(_frequency, ctx, inputBuffers);
        var intensity = GetModulatedValues(_intensity, ctx, inputBuffers);
        var tilt = GetModulatedValues(_tilt, ctx, inputBuffers);
        var subDepth = GetModulatedValues(_subDepth, ctx, inputBuffers);
        var jitter = GetModulatedValues(_jitter, ctx, inputBuffers);
        var shimmer = GetModulatedValues(_shimmer, ctx, inputBuffers);
        var breath = GetModulatedValues(_breath, ctx, inputBuffers);

        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            _noiseState = (ulong)random.NextInt64() | 1;

        var subRatio = (int)Math.Round(_subRatio.Value) switch { 1 => 2, 2 => 3, _ => 1 };
        var maxHarmonics = HarmonicCounts[Math.Clamp((int)Math.Round(_harmonics.Value), 0, HarmonicCounts.Length - 1)];

        var output = NewBuffer(ctx);
        var fs = (double)ctx.SampleRate;
        var nyquistFadeStart = 0.40 * fs;
        var nyquistFadeEnd = 0.45 * fs;
        // 5 ms smoothing of the two level knobs, in seconds so it is the same at any sample rate.
        var smooth = 1 - Math.Exp(-1 / (0.005 * fs));
        // Aspiration band: one-pole high-pass ~400 Hz and low-pass ~5 kHz on white noise.
        var hpCoef = Math.Exp(-2 * Math.PI * 400 / fs);
        var lpCoef = 1 - Math.Exp(-2 * Math.PI * 5000 / fs);

        var lastTilt = double.NaN;
        double weightSum = 1;
        double exponent = 0;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var f0 = Math.Clamp(frequency[i], 20, 4000);
            _intensitySmooth += (Math.Clamp(intensity[i], 0, 1) - _intensitySmooth) * smooth;
            _subSmooth += (Math.Clamp(subDepth[i], 0, 1) - _subSmooth) * smooth;

            var t = Math.Clamp(tilt[i], -30, -3);
            if (t != lastTilt || (subRatio > 1 && _subWeightsRatio != subRatio))
            {
                lastTilt = t;
                exponent = t / 6.0206;
                weightSum = 0;
                for (var k = 1; k <= maxHarmonics; k++)
                {
                    _weights[k] = Math.Pow(k, exponent);
                    weightSum += _weights[k];
                }
                if (subRatio > 1)
                {
                    for (var j = 1; j <= maxHarmonics * subRatio; j++)
                        // Below the fundamental the tilt is NOT extended (it would make f0/2 12 dB
                        // louder than f0 at Sklon −12 and move the heard pitch an octave down):
                        // there a subharmonic weighs what the fundamental does, so Hloubka sub 1
                        // means "as strong as the fundamental".
                        _subWeights[j] = Math.Pow(Math.Max(1.0, (double)j / subRatio), exponent);
                    _subWeightsRatio = subRatio;
                }
            }

            // Shimmer ramps from the previous period's level to this one's across the period.
            var progress = _phase / (2 * Math.PI);
            var amp = _ampFrom + (_ampTo - _ampFrom) * progress;

            // --- harmonics of the fundamental ---
            var voiced = 0.0;
            var sinPhi = Math.Sin(_phase);
            var twoCos = 2 * Math.Cos(_phase);
            double sPrev = 0, s = sinPhi;
            for (var k = 1; k <= maxHarmonics; k++)
            {
                var hz = k * f0;
                if (hz >= nyquistFadeEnd)
                    break;
                var w = _weights[k];
                if (hz > nyquistFadeStart)
                    w *= (nyquistFadeEnd - hz) / (nyquistFadeEnd - nyquistFadeStart);
                voiced += w * s;
                var next = twoCos * s - sPrev;
                sPrev = s;
                s = next;
            }

            // --- subharmonics: j·f0/R for j not divisible by R, phase from whole periods ---
            if (subRatio > 1 && _subSmooth > 1e-6)
            {
                var psi = (2 * Math.PI * (_cycle % subRatio) + _phase) / subRatio;
                var sub = 0.0;
                // One recurrence per residue r = j mod R, stepping j by R. The step angle R·ψ is
                // φ + 2π·(cycle mod R), so its cosine is the fundamental's — the same twoCos —
                // and the components that are multiples of R (already in the harmonics) are never
                // computed at all.
                for (var r = 1; r < subRatio; r++)
                {
                    var p = Math.Sin(r * psi);
                    var pPrev = Math.Sin((r - subRatio) * psi);
                    for (var j = r; j <= maxHarmonics * subRatio; j += subRatio)
                    {
                        var hz = j * f0 / subRatio;
                        if (hz >= nyquistFadeEnd)
                            break;
                        var w = _subWeights[j];
                        if (hz > nyquistFadeStart)
                            w *= (nyquistFadeEnd - hz) / (nyquistFadeEnd - nyquistFadeStart);
                        sub += w * p;
                        var next = twoCos * p - pPrev;
                        pPrev = p;
                        p = next;
                    }
                }
                // Divided so that turning the roughness up does not also make the voice ~6 dB
                // louder: at Hloubka sub 1 the subharmonics weigh as much as the harmonics.
                voiced = (voiced + _subSmooth * sub) / (1 + 0.5 * _subSmooth);
            }

            voiced /= weightSum;

            // --- aspiration, pulsed with the glottal cycle ---
            _noiseState ^= _noiseState << 13;
            _noiseState ^= _noiseState >> 7;
            _noiseState ^= _noiseState << 17;
            var white = (_noiseState >> 11) * (2.0 / (1UL << 53)) - 1;
            _noiseLow += (white - _noiseLow) * lpCoef;
            _noiseBand = hpCoef * (_noiseBand + _noiseLow - _noiseLowPrev);
            _noiseLowPrev = _noiseLow;
            var band = _noiseBand;
            var pulse = 0.55 + 0.45 * Math.Cos(_phase - 0.5);
            var aspiration = band * pulse * 0.6 * Math.Clamp(breath[i], 0, 1);

            var b = Math.Clamp(breath[i], 0, 1);
            output[i] = (float)(_intensitySmooth * (amp * voiced * (1 - 0.5 * b) + aspiration));

            // --- advance; per-period jitter and shimmer are drawn at the wrap ---
            _phase += 2 * Math.PI * f0 / (fs * _periodFactor);
            if (_phase >= 2 * Math.PI)
            {
                _phase -= 2 * Math.PI;
                _cycle++;
                var j = Math.Clamp(jitter[i], 0, 5) / 100;
                _jitterState = Math.Clamp(0.6 * _jitterState + 0.8 * Gaussian(random), -3, 3);
                _periodFactor = 1 + j * _jitterState;
                var sh = Math.Clamp(shimmer[i], 0, 30) / 100;
                _shimmerState = Math.Clamp(0.6 * _shimmerState + 0.8 * Gaussian(random), -3, 3);
                _ampFrom = _ampTo;
                _ampTo = Math.Max(0, 1 + sh * _shimmerState);
            }
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    private static double Gaussian(Random random)
    {
        var u1 = 1 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
