using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Opens a swarm: everything wired after it, up to its <see cref="SwarmEndModule"/>, is played as a
/// new copy on every trigger and the copies are summed — a cluster of particles from one chain of
/// boxes. The mechanics are in <see cref="SwarmRunner"/>; this module only collects what a trigger
/// freezes.
///
/// <para>The four values pass straight through (no knob, no depth): what comes out of
/// <c>Hodnota N</c> in an instance is whatever was on <c>Vstup N</c> at its trigger. That keeps the
/// ranges the business of whatever generates them — usually a <see cref="SampleHoldModule"/> on the
/// same trigger — instead of a knob whose default modulation depth would rescale them.</para>
///
/// <para>The envelope is applied to every instance where it leaves the swarm, and is also on
/// <c>Env</c> for anything inside that wants to follow it. Its knobs are read at the trigger too,
/// so a modulated Release gives every particle its own.</para>
/// </summary>
public sealed class SwarmModule : ModuleBase, ISwarmOpener
{
    private const int ValueCount = 4;

    private static readonly string[] WhenFullNames = { "Ukrást nejstarší", "Zahodit impuls" };
    private static readonly string[] NormalizeNames = { "Žádná", "1/√Strop" };

    private readonly Port _trigger;
    private readonly Port[] _valueInputs;
    private readonly Port[] _valueOutputs;
    private readonly Port _envelopeOutput;
    private readonly ModuleParameter _attack;
    private readonly ModuleParameter _decay;
    private readonly ModuleParameter _sustain;
    private readonly ModuleParameter _hold;
    private readonly ModuleParameter _release;
    private readonly ModuleParameter _capacity;
    private readonly ModuleParameter _whenFull;
    private readonly ModuleParameter _normalize;
    private readonly SwarmPass _pass = new();
    private bool _hasPass;

    private static readonly IReadOnlyDictionary<Port, float[]> NoOutputs = new Dictionary<Port, float[]>();

    public SwarmModule() : base("Roj")
    {
        _trigger = AddInput("Trig");
        _valueInputs = Enumerable.Range(1, ValueCount).Select(n => AddInput($"Vstup {n}")).ToArray();
        _valueOutputs = Enumerable.Range(1, ValueCount).Select(n => AddOutput($"Hodnota {n}")).ToArray();
        _envelopeOutput = AddOutput("Env");

        _attack = AddParameter("Attack", 0.005, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
        _decay = AddParameter("Decay", 0.05, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
        _sustain = AddParameter("Sustain", 0.6, 0, 1);
        _hold = AddParameter("Držení", 0.05, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
        _release = AddParameter("Release", 0.3, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
        _capacity = AddParameter("Strop", 8, 1, 128, modulatable: false);
        _whenFull = AddParameter("Plný strop", 0, 0, WhenFullNames.Length - 1, options: WhenFullNames);

        // Off by default so the patches tuned so far keep their level. By the cap and not by how many
        // instances actually sound: that would follow the density and pump the quiet stretches up.
        _normalize = AddParameter("Normalizace", 0, 0, NormalizeNames.Length - 1, options: NormalizeNames);
    }

    public override string Description =>
        "Otevírá roj: všechno, co je zapojené za ním až po Konec roje, se při každém impulsu na Trig " +
        "zahraje jako nová kopie a kopie se sečtou — shluk partiklů z jedné řady krabiček. Co vede " +
        "přes Vstup → Hodnota, si kopie zamrazí v okamžiku svého impulsu (rozladění z Vzorkovače na " +
        "stejném spouštěči); drát zapojený dovnitř odjinud zůstává živý a společný. Každá kopie má " +
        "vlastní obálku (Attack, Decay, Sustain, Držení, Release — čtou se při impulsu) a po jejím " +
        "doběhnutí skončí. Strop je kolik kopií zní naráz; když jsou všechny obsazené, impuls nejstarší " +
        "kopii ukradne (ztlumí ji za 5 ms), nebo se podle volby Plný strop zahodí — zahazování ale " +
        "hustý roj rozpulzuje do nárazů. Kopie se sčítají a hlasitost s jejich počtem roste; " +
        "Normalizace 1/√Strop ji ztlumí tak, aby plný roj zněl v průměru zhruba jako jedna kopie — řídký " +
        "roj s velkým stropem pak ale zní potichu. Bez Konce roje končí roj na Outputu — pak se ale kopíruje i dozvuk a efekty za ním.";

    SwarmRunner ISwarmOpener.Runner => Runner;
    internal SwarmRunner Runner { get; } = new();

    int ISwarmOpener.Capacity => (int)Math.Round(_capacity.Value);
    bool ISwarmOpener.StealWhenFull => (int)Math.Round(_whenFull.Value) == 0;

    double ISwarmOpener.OutputGain =>
        (int)Math.Round(_normalize.Value) == 1 ? 1 / Math.Sqrt(Math.Max(1, Math.Round(_capacity.Value))) : 1;
    IReadOnlyList<Port> ISwarmOpener.ValueOutputs => _valueOutputs;
    Port ISwarmOpener.EnvelopeOutput => _envelopeOutput;
    SwarmPass? ISwarmOpener.Pass => _hasPass ? _pass : null;

    public override void Prepare() => Runner.Invalidate();

    public override void ResetState()
    {
        _hasPass = false;
        Runner.Reset();
    }

    /// <summary>Only collects: the instances are rendered when the swarm's closer comes up, by
    /// which point everything they read from outside has been rendered too.</summary>
    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        _pass.Trigger = inputBuffers.GetValueOrDefault(_trigger);
        if (_pass.Values.Length != ValueCount)
            _pass.Values = new float[]?[ValueCount];
        for (var i = 0; i < ValueCount; i++)
            _pass.Values[i] = inputBuffers.GetValueOrDefault(_valueInputs[i]);
        _pass.Attack = GetModulatedValues(_attack, ctx, inputBuffers);
        _pass.Decay = GetModulatedValues(_decay, ctx, inputBuffers);
        _pass.Sustain = GetModulatedValues(_sustain, ctx, inputBuffers);
        _pass.Hold = GetModulatedValues(_hold, ctx, inputBuffers);
        _pass.Release = GetModulatedValues(_release, ctx, inputBuffers);
        _hasPass = true;

        return NoOutputs;
    }
}

/// <summary>
/// Closes a swarm opened by <see cref="SwarmModule"/>: what reaches its input is every instance,
/// each under its own envelope, already summed — the summing is done by <see cref="SwarmRunner"/>
/// before this module runs. It passes that on, by default through a soft clip that leaves
/// everything under <see cref="ClipKnee"/> bit for bit as it was.
/// </summary>
public sealed class SwarmEndModule : ModuleBase, ISwarmCloser
{
    /// <summary>Where the soft clip starts bending. Below it the signal is not touched at all, so a
    /// swarm that does not reach it renders exactly as without the clip.</summary>
    private const double ClipKnee = 0.8;

    /// <summary>What the soft clip flattens out at — under full scale with room to spare, because a
    /// curve that only approaches 1 still lands a heavily overdriven swarm on the stop.</summary>
    private const double ClipCeiling = 0.94;

    private static readonly string[] ClipNames = { "Ne", "Ano" };

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _clip;

    public SwarmEndModule() : base("Konec roje")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        // A rescue, not a limiter: no look-ahead, so what it catches it distorts — softly. It sits on
        // the sum because that is where a dense swarm goes over; a Gain behind it that brings the
        // level back down would find the peaks already bent, and is the case for switching it off.
        _clip = AddParameter("Měkký ořez", 1, 0, ClipNames.Length - 1, options: ClipNames);
    }

    public override string Description =>
        "Uzavírá roj: na vstup dostane všechny kopie roje sečtené, každou pod její obálkou, a pošle " +
        "je dál. Za něj patří dozvuk, delay a další zpracování celku — spočítají se jednou, ne pro " +
        "každou kopii zvlášť. Z vnitřku roje nesmí vést drát ven jinudy než sem. Měkký ořez je záchrana " +
        "pro hustý roj: pod 0,8 se signálu nedotkne, nad tím ho plynule ohne tak, aby nepřesáhl 0,94 (−0,54 dBFS). " +
        "Pokud za Koncem roje hlasitost stahujete Gainem, vypněte ho — ohnul by špičky, které by " +
        "jinak prošly čistě.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        if ((int)Math.Round(_clip.Value) == 0)
            return new Dictionary<Port, float[]> { [_output] = input };

        var output = NewBuffer(ctx);
        for (var i = 0; i < ctx.SampleCount; i++)
            output[i] = SoftClip(input[i]);

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    /// <summary>Unchanged up to the knee, then a tanh that leaves it with slope 1 and flattens out
    /// at the ceiling — no corner, so nothing new is added at the knee itself.</summary>
    internal static float SoftClip(float x)
    {
        var magnitude = Math.Abs(x);
        if (magnitude <= ClipKnee)
            return x;

        const double headroom = ClipCeiling - ClipKnee;
        var bent = ClipKnee + headroom * Math.Tanh((magnitude - ClipKnee) / headroom);
        return (float)(Math.Sign(x) * bent);
    }
}
