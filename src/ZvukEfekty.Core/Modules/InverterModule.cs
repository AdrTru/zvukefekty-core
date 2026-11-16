using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Mirrors the signal about a level: <c>out = 2·Level − in</c>. Whatever sits exactly on the level
/// stays where it is, everything else swaps sides at the same distance.
///
/// <para><b>The one primitive the catalog was missing.</b> With Level 0 this is a sign flip, and
/// a sign flip is subtraction: <see cref="MixerModule"/>'s Sum has no gains and no signs
/// (<c>MixerModule.cs:60</c>) and <see cref="GainModule"/>'s range starts at 0, so up to now
/// nothing in the program could compute A − B. Two things `todo.md` files as "cannot be assembled"
/// hang off exactly that — a transient shaper (fast envelope minus slow one) and a crossfader
/// (one control signal, two gains moving against each other).</para>
///
/// <para><b>Why a level and not a bare inverter.</b> A sign flip alone is useless on a control
/// signal: a follower's 0…1 flipped about zero is −1…0, which drives nothing. What a control
/// signal needs is its complement inside its own range, and that is a mirror about the middle of
/// that range — 0.5 for the usual 0…1. One knob covers both jobs, and it is the same knob:
/// audio mirrors about 0, control mirrors about its midpoint.</para>
///
/// <para><b>The level is modulatable, and that is not decoration.</b> A moving mirror axis is
/// how the flip itself gets played — the axis can follow another envelope, an LFO, or the very
/// signal being flipped.</para>
///
/// <para><b>Where it ends.</b> The knob spans −1…1, which is audio and normalised control. A pitch
/// signal in Hz cannot be mirrored about 440 with it (a melodic inversion); the level's own
/// modulation input is the only way past the slider's range, because pitch here is a modulation
/// depth on a Hz parameter rather than a signal in volts (see cross-cutting limitation 3 in
/// `todo.md`).</para>
/// </summary>
public sealed class InverterModule : ModuleBase
{
    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _level;

    public InverterModule() : base("Invertor")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _level = AddParameter("Hladina", 0, -1, 1);
    }

    public override string Description =>
        "Překlopí signál kolem nastavené hladiny: co je nad ní, jde o stejný kus pod ni, a co je " +
        "přesně na ní, zůstane na místě. S hladinou 0 je to obrácení fáze — a tím i odčítání, " +
        "protože Mixer umí jen sčítat: rozdíl dvou signálů vznikne jedině tak, že se jeden pošle " +
        "přes Invertor. S hladinou 0,5 se z řídicího signálu 0–1 stane jeho doplněk (1−x), tedy " +
        "crossfader: jeden hlas ubývá přesně tak rychle, jak druhý přibývá. Hladina se dá " +
        "modulovat, takže osa překlopení se sama hýbe. Nezapojený vstup dělá z modulu konstantu " +
        "o hodnotě dvojnásobku hladiny.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var level = GetModulatedValues(_level, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        for (var i = 0; i < ctx.SampleCount; i++)
            output[i] = 2 * level[i] - input[i];

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
