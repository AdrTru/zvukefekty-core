using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Plain delay: the input, later. No dry signal mixed back in — just the signal shifted in time,
/// and optionally fed back into itself.
///
/// <para><see cref="DelayModule"/> cannot do this, which is why this exists. Its output is
/// <c>input + tap * Mix</c>: the dry signal sits at full level unconditionally, so no setting of
/// Mix yields the delayed signal alone. That is right for an echo and useless for everything
/// below.</para>
///
/// <para>What it is for: nudging one layer against another; feeding a mixer alongside the dry
/// signal to build a comb filter (a metallic colour the engine otherwise has no way to make);
/// pre-delay ahead of a reverb; and, with <c>Čas</c> modulated by an LFO, flanging and chorus.
/// Those last three all need delays far shorter than <see cref="DelayModule"/>'s 10 ms minimum,
/// which is the other reason a second module was cheaper than widening the first.</para>
///
/// <para><b>Zpětná vazba</b> arrived later (2026-09-09) and is what turns the comb above into a
/// RESONANT one — the last open row of <c>todo.md</c> 12b. It defaults to 0, where this module is
/// bit for bit what it was before, so it adds a capability rather than changing a behaviour. The
/// knob runs both ways on purpose: fed back positively the comb peaks at every multiple of 1/Čas
/// (including nought hertz), negatively it peaks at the ODD multiples of 1/2Čas instead, which is
/// the hollow half of what a hardware flanger's polarity switch does. See the <c>Rezonanční
/// flanger</c> block in <see cref="BlockCatalog"/> for the wiring this was added for.</para>
/// </summary>
public sealed class DelayLineModule : ModuleBase
{
    /// <summary>Longest delay, in milliseconds. Deliberately far shorter than
    /// <see cref="DelayModule"/>'s two seconds: this module is for alignment and colour, and the
    /// buffer is cloned into every voice, so 250 ms at 96 kHz (94 kB) beats 2 s (750 kB) sixteen
    /// times over.</summary>
    private const double MaxMs = 250;

    private const int MaxDelaySamples = (int)(MaxMs / 1000 * 96000) + 4;

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _time;
    private readonly ModuleParameter _feedback;

    private readonly float[] _line = new float[MaxDelaySamples];
    private int _writePos;

    public DelayLineModule() : base("Zpoždění")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        // Milliseconds, not seconds. The useful settings here start below a tenth of a millisecond
        // (a comb filter at 10 kHz is 0.05 ms) and a knob reading "0,00005 s" is unreadable.
        _time = AddParameter("Čas", 5, 0.02, MaxMs, "ms", tempoSync: TempoSyncKind.Milliseconds);

        // Both ways, unlike DelayModule's 0…0.95: an echo has no use for inverted repeats, a comb
        // does — it is which half of the harmonic series the resonance lands on. 0.95 rather than
        // 1 at either end because at 1 the loop never loses anything and a swept comb parked on a
        // steady tone would climb without limit.
        _feedback = AddParameter("Zpětná vazba", 0, -0.95, 0.95);
    }

    public override string Description =>
        "Čisté zpoždění signálu — žádný suchý signál navíc. Na srovnání vrstev mezi sebou, " +
        "na hřebenový filtr (smíchej se suchým přes Mixer) nebo jako pre-delay. S Časem " +
        "modulovaným z LFO dělá flanger/chorus. Zpětná vazba nechá signál kroužit dokola a " +
        "tím hřeben rozezní: kladná zvýrazní násobky 1/Čas, záporná ty poloviční liché. " +
        "Pro ozvěnu použij modul Delay.";

    public override void ResetState()
    {
        Array.Clear(_line);
        _writePos = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var time = GetModulatedValues(_time, ctx, inputBuffers);
        var feedback = GetModulatedValues(_feedback, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var maxDelay = Math.Min(MaxDelaySamples - 2, ctx.SampleRate * MaxMs / 1000);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Fractional, unlike DelayModule's whole-sample read. A swept whole-sample delay jumps
            // between integer taps and clicks on every jump, which is exactly what a flanger does
            // all the time — so interpolation is what makes a modulated Čas usable at all.
            var delaySamples = Math.Clamp(time[i] / 1000.0 * ctx.SampleRate, 1, maxDelay);
            var whole = (int)delaySamples;
            var fraction = delaySamples - whole;

            // Read before write, which the old order (write, then read) did not have to care about:
            // the tap is at least one whole sample behind the write head either way, so with the
            // feedback knob at 0 this is the same arithmetic it always was.
            var readPos = (_writePos - whole + MaxDelaySamples) % MaxDelaySamples;
            var olderPos = (readPos - 1 + MaxDelaySamples) % MaxDelaySamples;
            var tap = _line[readPos] * (1 - fraction) + _line[olderPos] * fraction;

            _line[_writePos] = (float)(input[i] + tap * Math.Clamp(feedback[i], -0.95, 0.95));
            output[i] = (float)tap;

            _writePos = (_writePos + 1) % MaxDelaySamples;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
