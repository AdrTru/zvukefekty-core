using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>Feedback delay line ("echo"): repeats the input at a fixed time offset, decaying each repeat.</summary>
public sealed class DelayModule : ModuleBase
{
    /// <summary>Longest echo. Four seconds so "1 takt" resolves down to 60 BPM — a bar-length
    /// echo is the most ordinary tempo-synced delay there is, and at 2 s it clamped below 120 BPM,
    /// which covers most of the range the tempo slider offers.</summary>
    private const double MaxSeconds = 4;

    // Backs the whole Time range at any real-world sample rate, with headroom. Widening the range
    // doubled this to 1.5 MB, and it is per instance per voice: a patch with two delays played
    // 16-note polyphonic carries 48 MB of line. Acceptable because a delay is rarely doubled up
    // and the app renders at 44.1 kHz, but it is the reason DelayLineModule's 250 ms was NOT
    // widened to match — see todo 7b2.
    private const int MaxDelaySamples = (int)(96000 * MaxSeconds);

    /// <summary>How Mix combines dry and echo. Index 0 is the original behaviour and the default,
    /// so a patch saved before the knob existed loads bit-identical: dry at full level and the echo
    /// added on top. "Prolnout" is a true crossfade, which the additive mode cannot do — Mix 1
    /// there still carries the full dry signal, and summing both at full level is what clipped
    /// the "nabití artefaktu" patch from the game tests (todo 18b).</summary>
    private static readonly string[] MixModeNames = { "Přičíst (dosavadní)", "Prolnout" };

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _time;
    private readonly ModuleParameter _feedback;
    private readonly ModuleParameter _mix;
    private readonly ModuleParameter _mixMode;

    // Circular buffer instead of a per-call array: a real-time voice calls Process repeatedly
    // on small blocks (way shorter than a typical delay Time), so the line has to outlive one call.
    private readonly float[] _line = new float[MaxDelaySamples];
    private int _writePos;

    public DelayModule() : base("Delay")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _time = AddParameter("Time", 0.3, 0.01, MaxSeconds, "s", tempoSync: TempoSyncKind.Seconds);
        _feedback = AddParameter("Feedback", 0.4, 0, 0.95);
        _mix = AddParameter("Mix", 0.5, 0, 1);
        _mixMode = AddParameter("Režim mixu", 0, 0, MixModeNames.Length - 1, options: MixModeNames);
    }

    /// <summary>Common echo kinds; measured for level, not yet listened to.</summary>
    private static readonly IReadOnlyList<ModulePreset> PresetList = new[]
    {
        Echo("Slapback", "Jedno krátké odražené opakování — dvůr, zeď blízko.", 0.11, 0.1, 0.35, 0),
        Echo("Ozvěna v horách", "Vzdálená stěna — dlouhá mezera, pár opakování.", 0.45, 0.35, 0.4, 0),
        Echo("Studna", "Úzká šachta — rychlá hustá opakování.", 0.06, 0.6, 0.35, 0),
        Echo("Dub", "Dlouhý ohon opakování, ozvěna se přelévá přes originál.", 0.375, 0.75, 0.5, 1),
        Echo("Kovová dutina", "10–15 ms se silnou zpětnou vazbou = kovové zabarvení (hřeben), ne ozvěna.", 0.012, 0.8, 0.5, 1),
    };

    private static ModulePreset Echo(string name, string description, double time, double feedback, double mix, double mode)
        => new(name, description, new Dictionary<string, double>
        {
            ["Time"] = time, ["Feedback"] = feedback, ["Mix"] = mix, ["Režim mixu"] = mode,
        });

    public override IReadOnlyList<ModulePreset> Presets => PresetList;

    public override string Description =>
        "Zpětnovazební zpožďovací linka (echo) s nastavitelným časem, zpětnou vazbou (Feedback) a mixem. " +
        "Režim mixu Přičíst (výchozí) nechá suchý signál vždy naplno a ozvěnu přičte × Mix — součet " +
        "může být hlasitější než vstup. Prolnout dělá skutečný poměr: Mix 0 jen suchý, 1 jen ozvěna.";

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
        var mix = GetModulatedValues(_mix, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var crossfade = (int)Math.Round(_mixMode.Value) == 1;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var delaySamples = Math.Clamp((int)(time[i] * ctx.SampleRate), 1, MaxDelaySamples - 1);
            var readPos = (_writePos - delaySamples + MaxDelaySamples) % MaxDelaySamples;
            var tap = _line[readPos];

            _line[_writePos] = input[i] + tap * (float)Math.Clamp(feedback[i], 0, 0.95);
            var m = (float)Math.Clamp(mix[i], 0, 1);
            // Additive branch kept as the exact original expression so old patches stay bit-identical.
            output[i] = crossfade ? input[i] * (1 - m) + tap * m : input[i] + tap * m;

            _writePos = (_writePos + 1) % MaxDelaySamples;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
