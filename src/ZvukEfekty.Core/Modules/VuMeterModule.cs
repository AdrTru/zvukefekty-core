using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Pass-through level monitor: forwards its input unchanged, while contributing its block to
/// <see cref="LevelMeterHub"/> for the node's live meter display in the editor. The hub — not this
/// module — sums contributions across a polyphonic render pass and smooths the result, since a
/// voice clone of this module is destroyed when its note ends; see LevelMeterHub's remarks.
/// </summary>
public sealed class VuMeterModule : ModuleBase
{
    // A batch render calls Process once for the whole clip, so that one call is sliced into chunks
    // of this size while recording (see LevelMeterHub.IsRecording) — otherwise the whole render
    // would collapse into a single timeline entry instead of a reading that moves over time.
    // Matches ScopeModule's window size for a consistent refresh granularity.
    private const int RecordingChunkSize = 1024;

    private readonly Port _input;
    private readonly Port _output;
    private long _totalSamplesRendered;

    public VuMeterModule() : base("VU Metr")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");
    }

    public override string Description =>
        "Průchozí monitor hlasitosti — ukazatel špičky (Peak) a RMS úrovně procházejícího signálu. Neovlivňuje zvuk.";

    public override void ResetState() => _totalSamplesRendered = 0;

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);

        if (LevelMeterHub.IsRecording)
        {
            for (var offset = 0; offset < ctx.SampleCount; offset += RecordingChunkSize)
            {
                var length = Math.Min(RecordingChunkSize, ctx.SampleCount - offset);
                var chunk = new float[length];
                Array.Copy(input, offset, chunk, 0, length);

                LevelMeterHub.Contribute(Id, chunk, (double)_totalSamplesRendered / ctx.SampleRate);
                _totalSamplesRendered += length;
            }
        }
        else
        {
            // Real-time: one whole-block contribution per Process call, so a real-time pass sums
            // each voice's own block exactly once (chunking here would double-count a single
            // voice's block against itself within one VoiceManager.Read pass).
            LevelMeterHub.Contribute(Id, input);
        }

        return new Dictionary<Port, float[]> { [_output] = input };
    }
}
