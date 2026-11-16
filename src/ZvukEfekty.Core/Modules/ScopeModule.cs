using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Pass-through monitor: forwards its input unchanged, while publishing a rolling raw window of it
/// to <see cref="VisualizationHub"/> for the node's live display in the editor. Drop it anywhere in
/// a chain to see what's flowing through that point without changing the sound.
///
/// <see cref="Mode"/> only controls how the UI draws the published window (waveform vs. running it
/// through an FFT for a spectrum) — it's deliberately not baked into what gets published. A voice
/// clone (see Audio.Voice) captures whatever Mode was set at the moment its note started, so if
/// that were part of the published data, holding a chord across a Mode change would flip the
/// display between voices depending on which clone's window happened to complete last.
/// </summary>
public sealed class ScopeModule : ModuleBase
{
    // Power of two (FFT requirement) — big enough for ~85Hz frequency resolution at 44.1kHz,
    // small enough to fill up (and so refresh the display) several times a second even from the
    // short blocks a real-time voice renders.
    private const int WindowSize = 1024;

    private static readonly string[] ModeNames = { "Waveform", "Spektrum" };

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _mode;

    // Filled sample-by-sample across possibly many small real-time blocks (see DelayModule for the
    // same cross-call pattern), or in one pass for a big batch Play/Export block.
    private readonly float[] _window = new float[WindowSize];
    private int _writePos;

    // Samples processed since the last ResetState — used only to timestamp windows while
    // VisualizationHub.IsRecording (a batch render), so VisualizationHub.AdvancePlayback can later
    // find the right one; ignored otherwise.
    private long _totalSamplesRendered;

    public ScopeModule() : base("Scope")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _mode = AddParameter("Mode", 0, 0, ModeNames.Length - 1, options: ModeNames);
    }

    /// <summary>Read directly off this instance (not a voice clone's) by the node's own UI control,
    /// so the displayed mode always matches what's currently selected, never a snapshot from
    /// whichever voice happened to publish last.</summary>
    public bool IsSpectrumMode => (int)_mode.Value == 1;

    public override string Description =>
        "Průchozí monitor — zobrazuje procházející signál jako osciloskop (tvar vlny) nebo spektrální " +
        "analyzátor (FFT), podle zvoleného Mode. Neovlivňuje zvuk.";

    public override void ResetState()
    {
        Array.Clear(_window);
        _writePos = 0;
        _totalSamplesRendered = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            _window[_writePos] = input[i];
            _writePos++;
            _totalSamplesRendered++;

            if (_writePos == WindowSize)
            {
                var elapsedSeconds = (double)_totalSamplesRendered / ctx.SampleRate;
                VisualizationHub.Contribute(Id, (float[])_window.Clone(), elapsedSeconds);
                _writePos = 0;
            }
        }

        return new Dictionary<Port, float[]> { [_output] = input };
    }
}
