using System.Globalization;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Friction with sticking and slipping — a stone dragged over stone, a door hinge, wood on wood
/// (todo 18e, P06). Own implementation of the published elasto-plastic friction model (Dupont
/// et al. 2002, as used for sound by Avanzini, Serafin &amp; Rocchesso 2005, IEEE TSAP 13(5));
/// the Sound Design Toolkit's GPL code is not used.
///
/// <para><b>Mechanics.</b> A body of mass m is pulled by a spring (the hand or the arm) whose end
/// moves at Rychlost; between body and floor sits a contact whose micro-deflection z follows
/// <c>ż = v·(1 − α(z,v)·z/z_ss(v))</c>, with the Stribeck curve
/// <c>z_ss = sgn(v)·(Fc + (Fs − Fc)·e^−(v/vs)²)/σ0</c>, Fc = μd·N, Fs = μs·N, and α = 0 below the
/// break-away deflection (true sticking), 1 above z_ss. The force <c>f = σ0·z + σ1·ż + σ2·v</c>
/// brakes the body and drives the floor's modes; −f drives the body's own modes. Their radiated
/// velocity is the sound.</para>
///
/// <para><b>Surface.</b> The normal force follows a rough profile h(x) of the distance actually
/// travelled, so a faster drag makes the roughness sound faster by itself. When roughness lifts the
/// body off, the contact lets go (z → 0); the deflection is never allowed above the static limit
/// Fs/σ0, so the force cannot exceed μs·N.</para>
///
/// <para><b>Numerics</b>, measured on the prototype: 8 substeps per output sample (352.8 kHz at
/// 44.1 kHz) are converged in the statistics that matter for a chaotic process — level within
/// 0.24 dB and spectral centroid within 2 Hz against 16 substeps. Sample-wise agreement is not a
/// criterion: stick-slip is chaotic.</para>
/// </summary>
public sealed class FrictionContactModule : ModuleBase
{
    private const int Substeps = 8;
    private const int MaxModes = 16;
    private const int MaxContacts = 32;

    /// <summary>Surface profile as a table: 65 536 points over 2 m (30 µm apart), built in linear
    /// time from filtered noise. Evaluating a 48-sine profile for each of up to 32 contacts per
    /// sample would cost more than the rest of the module.</summary>
    private const int ProfileLength = 65536;
    private const double ProfileSpan = 2.0;
    private const double OutputScale = 100;
    /// <summary>Damping ratio of the hand spring. 0.05 (the first version) let the whole body judder
    /// on it — measured as 40 dB envelope jumps several times a second on the hinge, heard as "údery";
    /// 0.7 settles it, so the sticking happens against the modes, at audio rate.</summary>
    private const double HandDampingRatio = 0.7;

    private const string DefaultFloor =
        "# kamenná deska: Hz  T60 [s]  hmotnost [kg]  vyzařování\n" +
        "380 0.07 3 1\n870 0.05 3 1\n1490 0.04 3 0.9\n2230 0.035 3 0.8\n3110 0.03 3 0.7\n4270 0.025 3 0.6\n5600 0.02 3 0.5";
    private const string DefaultBody =
        "# tažený kámen\n1150 0.04 0.6 1\n2480 0.03 0.6 0.8\n3900 0.025 0.6 0.6\n5450 0.02 0.6 0.4";

    private readonly Port _output;
    private readonly Port _forceOutput;
    private readonly ModuleParameter _speed;
    private readonly ModuleParameter _normal;
    private readonly ModuleParameter _muStatic;
    private readonly ModuleParameter _muDynamic;
    private readonly ModuleParameter _stribeck;
    private readonly ModuleParameter _stiffness;
    private readonly ModuleParameter _roughness;
    private readonly ModuleParameter _grain;
    private readonly ModuleParameter _mass;
    private readonly ModuleParameter _hand;
    private readonly ModuleParameter _level;
    private readonly ModuleParameter _clip;
    private static readonly string[] ClipNames = { "Ne", "Ano" };
    private readonly ModuleText _floorText;
    private readonly ModuleText _bodyText;
    private readonly SeededRandomSource _randomSource;

    internal readonly record struct Mode(double Hz, double T60, double Mass, double Radiation);

    private sealed class Bank
    {
        public Mode[] Modes = Array.Empty<Mode>();
        public string? ParsedFrom;
        public readonly double[] X = new double[MaxModes];
        public readonly double[] V = new double[MaxModes];
        public readonly double[] W2 = new double[MaxModes];
        public readonly double[] Damp = new double[MaxModes];
        public readonly double[] InvMass = new double[MaxModes];
        public double PreparedFs;
    }

    private readonly Bank _floor = new();
    private readonly Bank _body = new();

    private readonly float[] _profile = new float[ProfileLength];

    // Micro-contacts (asperities). Each has its own deflection and reads the profile at its own
    // offset, so they stick and slip at different moments — scattered small slips instead of one
    // big jolt of the whole body (Tomáš 2026-11-14: "velmi výrazné údery místo rozsypaných
    // zadrhávání").
    private readonly double[] _z = new double[MaxContacts];
    private readonly double[] _contactOffset = new double[MaxContacts];
    private readonly double[] _contactLoad = new double[MaxContacts];
    private readonly ModuleParameter _contacts;

    private double _xHand, _xBody, _vBody, _travelled;
    private double _hp, _hpPrev;

    public FrictionContactModule() : base("Tření")
    {
        _output = AddOutput("Out");
        _forceOutput = AddOutput("Síla");

        _speed = AddParameter("Rychlost", 0.15, -0.5, 0.5, "m/s");
        _normal = AddParameter("Přítlak", 20, 0, 100, "N");
        _muStatic = AddParameter("Statické tření", 0.7, 0, 2);
        _muDynamic = AddParameter("Dynamické tření", 0.45, 0, 2);
        _stribeck = AddParameter("Přechod tření", 0.05, 0.001, 0.5, "m/s");
        _stiffness = AddParameter("Tuhost kontaktu", 5.3, 3, 7, "log N/m");
        _roughness = AddParameter("Drsnost", 0.5, 0, 1);
        _grain = AddParameter("Zrnitost", 1, 0.1, 10, "×");
        _mass = AddParameter("Hmotnost tělesa", 1.2, 0.05, 10, "kg");
        _hand = AddParameter("Pružnost ruky", 3.6, 2, 5, "log N/m");
        _level = AddParameter("Úroveň", 1, 0, 4);
        // On by default: friction can lock onto a mode and squeal (measured: one seed of the stone
        // preset burst to 16× its usual peak for ~10 ms). Physically real, but it must not clip.
        _clip = AddParameter("Měkký ořez", 1, 0, ClipNames.Length - 1, options: ClipNames);
        _contacts = AddParameter("Počet kontaktů", 12, 1, MaxContacts, modulatable: false);

        var hint = string.Join(Environment.NewLine, new[]
        {
            "Jeden mód na řádek: frekvence [Hz], T60 [s], nepovinně hmotnost [kg] (1) a vyzařování (1).",
            $"Oddělovač mezera, tabulátor nebo středník; desetinná tečka i čárka. # = poznámka. Nejvýš {MaxModes} módů.",
        });
        _floorText = AddText("Módy podkladu", DefaultFloor, hint);
        _bodyText = AddText("Módy tělesa", DefaultBody, hint);
        _floorText.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModuleText.Text)) Parse(_floorText, _floor); };
        _bodyText.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModuleText.Text)) Parse(_bodyText, _body); };
        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Tření se zadíráním — kámen tažený po kameni, pant dveří, dřevo po dřevě, skřípění. Těleso " +
        "táhne pružina (ruka) Rychlostí, Přítlak ho tlačí k podkladu; kontakt střídá zadření a " +
        "prokluz podle Statického a Dynamického tření (Přechod tření = jak rychle se mezi nimi " +
        "přechází). Drsnost povrchu rozkmitá přítlak podle ujeté dráhy, takže rychlejší tažení zní " +
        "rychleji samo; Zrnitost mění velikost zrn. Třecí síla rozezní Módy podkladu a Módy tělesa " +
        "(řádky Hz, T60, hmotnost, vyzařování). Výstup Síla je třecí síla ÷ přítlak — buzení pro " +
        "Banku módů. Počet kontaktů = kolik drobných výstupků se dotýká; každý se zadírá sám, víc kontaktů = jemnější, rozsypané zrnění místo úderů. Model je fyzikální a chaotický: malá změna knoflíku může změnit charakter, a tření se umí " +
        "chytit na mód a zakvílet — proto je Měkký ořez (pod 0,8 beze změny, strop 0,94) zapnutý.";

    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Material("Kámen po kameni", "Kámen tažený po kamenné desce, 0,15 m/s. Výchozí bod, neposlouchané.",
            0.15, 20, 0.7, 0.45, 0.05, 5.3, 0.5, 1, 1.2, 3.6, 1.5, DefaultFloor, DefaultBody),
        Material("Kámen pomalu", "Totéž pomalu (0,04 m/s) — víc zadírání. Neposlouchané.",
            0.04, 20, 0.7, 0.45, 0.05, 5.3, 0.5, 1, 1.2, 3.6, 4, DefaultFloor, DefaultBody),
        Material("Pant dveří", "Pomalý pant pod velkým přítlakem, módy křídla z nahrávky 414493. Neposlouchané.",
            0.01, 60, 0.6, 0.3, 0.004, 6.7, 0.05, 1, 0.3, 5, 0.3,
            "# křídlo dveří\n451 0.24 0.8 1\n479 0.21 0.8 1\n518 0.22 0.8 1\n572 0.19 0.8 1\n754 0.37 0.8 0.9\n811 0.4 0.8 0.9",
            "# pant\n2900 0.05 0.05 0.3"),
        Material("Dřevo po dřevě", "Prkno tažené po prkně — měkčí, nižší, víc tlumené. Neposlouchané.",
            0.1, 15, 0.5, 0.35, 0.03, 5, 0.4, 2, 0.8, 3.5, 3,
            "# prkno\n210 0.12 1.5 1\n560 0.08 1.5 0.9\n1050 0.05 1.5 0.7\n1700 0.04 1.5 0.5\n2600 0.03 1.5 0.4",
            "# tažené dřevo\n720 0.06 0.4 1\n1650 0.04 0.4 0.7\n2900 0.03 0.4 0.4"),
        Material("Kov — skřípění", "Kov po kovu, tuhý kontakt, dlouho zvonící módy. Neposlouchané.",
            0.05, 30, 0.6, 0.4, 0.01, 6.5, 0.1, 1, 0.5, 4, 1.6,
            "# plech\n620 0.9 0.6 1\n1490 0.7 0.6 0.9\n2710 0.5 0.6 0.8\n4180 0.4 0.6 0.6",
            "# kovový předmět\n1830 0.5 0.2 1\n4370 0.4 0.2 0.6"),
    };

    private static ModulePreset Material(string name, string description, double speed, double normal,
        double muS, double muD, double stribeck, double stiffness, double roughness, double grain,
        double mass, double hand, double level, string floor, string body)
        => new(name, description, new Dictionary<string, double>
        {
            ["Rychlost"] = speed, ["Přítlak"] = normal, ["Statické tření"] = muS, ["Dynamické tření"] = muD,
            ["Přechod tření"] = stribeck, ["Tuhost kontaktu"] = stiffness, ["Drsnost"] = roughness,
            ["Zrnitost"] = grain, ["Hmotnost tělesa"] = mass, ["Pružnost ruky"] = hand, ["Úroveň"] = level,
        }, new Dictionary<string, string> { ["Módy podkladu"] = floor, ["Módy tělesa"] = body });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override void Prepare()
    {
        Parse(_floorText, _floor);
        Parse(_bodyText, _body);
    }

    public override void ResetState()
    {
        _randomSource.Reset();
        var random = _randomSource.GetGenerator(out _);
        DrawProfile(random);
        ResetMechanics();
    }

    /// <summary>Fills the profile table: three bands of low-passed noise with spatial cut-offs
    /// 0.2, 1.5 and 8 mm, weighted √λ (rougher at larger scales, as before), mean removed, unit RMS,
    /// and the last 2 % cross-faded into the start so the table wraps without a step. Also places
    /// the contacts at random offsets along the path.</summary>
    private void DrawProfile(Random random)
    {
        var dx = ProfileSpan / ProfileLength;
        Span<double> lp = stackalloc double[3];
        Span<double> coef = stackalloc double[3];
        Span<double> weight = stackalloc double[3];
        double[] lambdas = { 0.0002, 0.0015, 0.008 };
        for (var b = 0; b < 3; b++)
        {
            coef[b] = 1 - Math.Exp(-2 * Math.PI * dx / lambdas[b]);
            // A one-pole low-pass of unit white noise has variance c/(2−c); normalise each band to
            // unit RMS before weighting, so the weights are the band levels.
            weight[b] = Math.Sqrt(lambdas[b]) / Math.Sqrt(coef[b] / (2 - coef[b]));
        }
        double mean = 0;
        for (var i = 0; i < ProfileLength; i++)
        {
            double h = 0;
            for (var b = 0; b < 3; b++)
            {
                var white = (random.NextDouble() * 2 - 1) * 1.7320508; // unit variance
                lp[b] += (white - lp[b]) * coef[b];
                h += weight[b] * lp[b];
            }
            _profile[i] = (float)h;
            mean += h;
        }
        mean /= ProfileLength;
        double sumSq = 0;
        for (var i = 0; i < ProfileLength; i++)
        {
            _profile[i] -= (float)mean;
            sumSq += (double)_profile[i] * _profile[i];
        }
        var scale = 1 / Math.Sqrt(sumSq / ProfileLength + 1e-30);
        for (var i = 0; i < ProfileLength; i++)
            _profile[i] = (float)(_profile[i] * scale);
        var fade = ProfileLength / 50;
        for (var i = 0; i < fade; i++)
        {
            var w = (double)i / fade;
            var tail = ProfileLength - fade + i;
            _profile[tail] = (float)(_profile[tail] * (1 - w) + _profile[i] * w);
        }
        for (var c = 0; c < MaxContacts; c++)
            _contactOffset[c] = random.NextDouble() * ProfileSpan;
    }

    private double ProfileAt(double x, double grain)
    {
        var pos = x / (ProfileSpan / ProfileLength * grain);
        pos -= Math.Floor(pos / ProfileLength) * ProfileLength;
        var i0 = (int)pos;
        var frac = pos - i0;
        var i1 = i0 + 1 == ProfileLength ? 0 : i0 + 1;
        return _profile[i0] + (_profile[i1] - _profile[i0]) * frac;
    }

    private void Parse(ModuleText text, Bank bank)
    {
        if (bank.ParsedFrom == text.Text)
            return;
        bank.ParsedFrom = text.Text;
        if (TryParse(text.Text, out var modes, out var error))
        {
            bank.Modes = modes;
            bank.PreparedFs = 0;
        }
        text.Error = error;
    }

    internal static bool TryParse(string text, out Mode[] modes, out string? error)
    {
        var list = new List<Mode>();
        var lineNumber = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var fields = line.Split(new[] { ' ', '\t', ';', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            var numbers = new double[fields.Length];
            for (var f = 0; f < fields.Length; f++)
                if (!double.TryParse(fields[f].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[f]))
                {
                    modes = Array.Empty<Mode>();
                    error = $"Řádek {lineNumber}: „{fields[f]}“ není číslo.";
                    return false;
                }
            if (numbers.Length is < 2 or > 4 || numbers[0] <= 0 || numbers[1] <= 0 || (numbers.Length > 2 && numbers[2] <= 0))
            {
                modes = Array.Empty<Mode>();
                error = $"Řádek {lineNumber}: čekám frekvenci a T60 (kladné), nepovinně hmotnost (kladnou) a vyzařování.";
                return false;
            }
            list.Add(new Mode(numbers[0], numbers[1], numbers.Length > 2 ? numbers[2] : 1, numbers.Length > 3 ? numbers[3] : 1));
            if (list.Count > MaxModes)
            {
                modes = Array.Empty<Mode>();
                error = $"Víc než {MaxModes} módů.";
                return false;
            }
        }
        modes = list.ToArray();
        error = null;
        return true;
    }

    private static void PrepareBank(Bank bank, double fs)
    {
        if (bank.PreparedFs == fs)
            return;
        for (var k = 0; k < bank.Modes.Length; k++)
        {
            var m = bank.Modes[k];
            // Modes above 0.45·fs are left silent rather than clamped onto the wrong pitch.
            var hz = m.Hz < 0.45 * fs ? m.Hz : 0;
            bank.W2[k] = Math.Pow(2 * Math.PI * hz, 2);
            bank.Damp[k] = 2 * 6.907755 / m.T60;
            bank.InvMass[k] = hz > 0 ? 1 / m.Mass : 0;
        }
        bank.PreparedFs = fs;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        Parse(_floorText, _floor);
        Parse(_bodyText, _body);

        var speed = GetModulatedValues(_speed, ctx, inputBuffers);
        var normal = GetModulatedValues(_normal, ctx, inputBuffers);
        var muStatic = GetModulatedValues(_muStatic, ctx, inputBuffers);
        var muDynamic = GetModulatedValues(_muDynamic, ctx, inputBuffers);
        var stribeck = GetModulatedValues(_stribeck, ctx, inputBuffers);
        var stiffness = GetModulatedValues(_stiffness, ctx, inputBuffers);
        var roughness = GetModulatedValues(_roughness, ctx, inputBuffers);
        var grain = GetModulatedValues(_grain, ctx, inputBuffers);
        var mass = GetModulatedValues(_mass, ctx, inputBuffers);
        var hand = GetModulatedValues(_hand, ctx, inputBuffers);
        var level = GetModulatedValues(_level, ctx, inputBuffers);

        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            DrawProfile(random);

        var fs = (double)ctx.SampleRate;
        PrepareBank(_floor, fs);
        PrepareBank(_body, fs);
        var floor = _floor; var body = _body;
        var nFloor = floor.Modes.Length; var nBody = body.Modes.Length;

        var output = NewBuffer(ctx);
        var forceOut = NewBuffer(ctx);
        var dt = 1.0 / (fs * Substeps);
        var hpCoef = Math.Exp(-2 * Math.PI * 40 / fs);
        var contacts = Math.Clamp((int)Math.Round(_contacts.Value), 1, MaxContacts);
        var clip = (int)Math.Round(_clip.Value) == 1;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var vHand = Math.Clamp(speed[i], -0.5, 0.5);
            var n0 = Math.Clamp(normal[i], 0, 100);
            var muS = Math.Clamp(muStatic[i], 0, 2);
            var muD = Math.Min(Math.Clamp(muDynamic[i], 0, 2), muS);
            var vs = Math.Clamp(stribeck[i], 0.001, 0.5);
            var sigma0 = Math.Pow(10, Math.Clamp(stiffness[i], 3, 7));
            // Bristle damping and viscous term scaled with the stiffness, so one knob keeps the contact
            // critically damped-ish instead of ringing on its own (σ1 ≈ 2·sqrt(σ0·0.0006 kg)).
            var sigma1 = 2 * Math.Sqrt(sigma0 * 0.0006);
            const double sigma2 = 2;
            var m = Math.Clamp(mass[i], 0.05, 10);
            var kHand = Math.Pow(10, Math.Clamp(hand[i], 2, 5));
            var cHand = 2 * Math.Sqrt(kHand * m) * HandDampingRatio;

            var g = Math.Clamp(grain[i], 0.1, 10);
            var rough = Math.Clamp(roughness[i], 0, 1);
            // Each contact carries 1/K of the load, modulated by the surface under it.
            for (var c = 0; c < contacts; c++)
                _contactLoad[c] = Math.Max(0, n0 / contacts * (1 + rough * ProfileAt(_travelled + _contactOffset[c], g)));
            var sigma0c = sigma0 / contacts;
            var sigma1c = sigma1 / contacts;

            double radiated = 0, forceSum = 0;
            for (var step = 0; step < Substeps; step++)
            {
                _xHand += vHand * dt;
                double vFloor = 0;
                for (var k = 0; k < nFloor; k++) vFloor += floor.V[k];
                var v = _vBody - vFloor;

                // The Stribeck factor depends only on the shared sliding velocity: once per substep.
                var r = v / vs;
                var stribeckBlend = Math.Exp(-r * r);
                var sign = v >= 0 ? 1.0 : -1.0;
                double f = sigma2 * v;
                for (var c = 0; c < contacts; c++)
                {
                    var load = _contactLoad[c];
                    if (load < 1e-9)
                    {
                        _z[c] = 0; // lifted off: this contact lets go
                        continue;
                    }
                    var fc = muD * load;
                    var fsMax = muS * load;
                    var zss = sign * (fc + (fsMax - fc) * stribeckBlend) / sigma0c;
                    var zba = 0.7 * fc / sigma0c;
                    var zc = _z[c];
                    var az = Math.Abs(zc);
                    var azss = Math.Abs(zss);

                    double alpha;
                    if ((zc >= 0) != (v >= 0) || az < zba) alpha = 0;
                    else if (az >= azss || azss - zba < 1e-18) alpha = 1;
                    else alpha = 0.5 * (1 + Math.Sin(Math.PI * (az - 0.5 * (azss + zba)) / (azss - zba)));

                    var zdot = v * (1 - alpha * zc / zss);
                    f += sigma0c * zc + sigma1c * zdot;
                    zc += zdot * dt;
                    var zMax = fsMax / sigma0c;
                    _z[c] = zc > zMax ? zMax : zc < -zMax ? -zMax : zc;
                }

                var spring = kHand * (_xHand - _xBody) + cHand * (vHand - _vBody);
                _vBody += (spring - f) / m * dt;
                _xBody += _vBody * dt;
                _travelled += Math.Abs(_vBody) * dt;

                for (var k = 0; k < nFloor; k++)
                {
                    floor.V[k] += (f * floor.InvMass[k] - floor.W2[k] * floor.X[k] - floor.Damp[k] * floor.V[k]) * dt;
                    floor.X[k] += floor.V[k] * dt;
                    radiated += floor.Modes[k].Radiation * floor.V[k];
                }
                for (var k = 0; k < nBody; k++)
                {
                    body.V[k] += (-f * body.InvMass[k] - body.W2[k] * body.X[k] - body.Damp[k] * body.V[k]) * dt;
                    body.X[k] += body.V[k] * dt;
                    radiated += body.Modes[k].Radiation * body.V[k];
                }
                forceSum += f;
            }

            var y = radiated / Substeps;
            if (double.IsNaN(y) || double.IsInfinity(y))
            {
                // A pathological knob combination must not poison the voice for good.
                ResetMechanics();
                y = 0;
            }
            // 40 Hz high-pass: the slow rigid-body drift is not sound.
            _hp = hpCoef * (_hp + y - _hpPrev);
            _hpPrev = y;
            var sample = (float)(_hp * OutputScale * Math.Max(0, level[i]));
            output[i] = clip ? SwarmEndModule.SoftClip(sample) : sample;
            forceOut[i] = (float)(n0 > 1e-6 ? forceSum / Substeps / n0 : 0);

            FlushDenormals(floor, contacts);
            FlushDenormals(body, 0);
        }

        return new Dictionary<Port, float[]> { [_output] = output, [_forceOutput] = forceOut };
    }

    /// <summary>Below this a state is −600 dB of anything audible and is set to exactly zero.
    /// Measured 2026-11-14: once a drag stops, velocities and freely decaying modes sink into
    /// subnormal doubles, which the CPU handles in microcode — a silent, resting Tření cost
    /// 33–36 % of a core per voice against 4–8 % while it sounded. Zero is the fixed point the
    /// physics was heading for anyway, and it stays normal from there.</summary>
    private const double DenormalFloor = 1e-30;

    private static double Flush(double x) => Math.Abs(x) < DenormalFloor ? 0 : x;

    private void FlushDenormals(Bank bank, int contacts)
    {
        for (var k = 0; k < bank.Modes.Length; k++)
        {
            bank.V[k] = Flush(bank.V[k]);
            bank.X[k] = Flush(bank.X[k]);
        }
        if (contacts == 0)
            return;
        for (var c = 0; c < contacts; c++)
            _z[c] = Flush(_z[c]);
        _vBody = Flush(_vBody);
        _hp = Flush(_hp);
        _hpPrev = Flush(_hpPrev);
    }

    private void ResetMechanics()
    {
        _xHand = _xBody = _vBody = _travelled = 0;
        Array.Clear(_z);
        _hp = _hpPrev = 0;
        Array.Clear(_floor.X); Array.Clear(_floor.V);
        Array.Clear(_body.X); Array.Clear(_body.V);
    }
}
