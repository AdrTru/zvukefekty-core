using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Pass-through monitor that draws a spectrogram — frequency against TIME, rather than the single
/// instant a <see cref="ScopeModule"/> spectrum shows. What it adds over the Scope is history: a
/// glide, a decay tail or a wobble is a shape you can see here and cannot see at all in a spectrum
/// that redraws itself thirty times a second.
///
/// <para>Publishing works exactly as <see cref="ScopeModule"/>'s does, and for the same reasons —
/// raw windows go to <see cref="VisualizationHub"/>, keyed by module Id so voice clones and the
/// editor's own instance agree on a slot, and the display settings stay out of what is published
/// (a clone captures whatever they were at note-on, so baking them in would make a held chord's
/// display depend on which voice published last). The scrolling history is assembled by the node's
/// UI control, not here.</para>
///
/// <para>For comparing a patch against a target RECORDING there is the offline
/// <c>tools/Zvuk</c> utility instead: a module only ever sees the signal flowing through the
/// patch, and half of that comparison is a file the patch knows nothing about.</para>
/// </summary>
public sealed class SpectrogramModule : ModuleBase
{
    /// <summary>
    /// Power of two, as the FFT requires. 2048 rather than the Scope's 1024: a spectrogram is read
    /// for where partials sit, and 21.5 Hz bins at 44.1 kHz keep the low end legible where 43 Hz
    /// bins smear the first few harmonics of anything under a few hundred Hz together. The cost is
    /// time resolution — one column per 46 ms — which is the trade every spectrogram makes.
    /// </summary>
    private const int WindowSize = 2048;

    private static readonly string[] AxisNames = { "Logaritmicky", "Lineárně" };

    /// <summary>Display order must match <see cref="SpectrogramPalette"/>.</summary>
    private static readonly string[] PaletteNames = { "Šedá", "Inferno", "Viridis", "Duha" };

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _axis;
    private readonly ModuleParameter _palette;
    private readonly ModuleParameter _floorDb;
    private readonly ModuleParameter _maxHz;

    private readonly float[] _window = new float[WindowSize];
    private int _writePos;
    private long _totalSamplesRendered;

    public SpectrogramModule() : base("Spektrogram")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        // Display-only knobs, so none of them is modulatable — a per-sample signal driving the
        // colour range of a picture is meaningless.
        _axis = AddParameter("Osa", 0, 0, AxisNames.Length - 1, options: AxisNames);
        _palette = AddParameter("Barvy", 0, 0, PaletteNames.Length - 1, options: PaletteNames);
        _maxHz = AddParameter("Strop", 12000, 500, 20000, "Hz", modulatable: false);
        _floorDb = AddParameter("Práh", -85, -110, -40, "dB", modulatable: false);
    }

    /// <summary>Read off this instance by the node's own UI, never off a published snapshot — same
    /// reason <see cref="ScopeModule.IsSpectrumMode"/> is.</summary>
    public bool IsLogarithmicAxis => (int)_axis.Value == 0;

    public SpectrogramPalette Palette => (SpectrogramPalette)(int)_palette.Value;

    public double MaxHz => _maxHz.Value;

    public double FloorDb => _floorDb.Value;

    public override string Description =>
        "Průchozí monitor, který kreslí spektrogram — čas vodorovně, frekvence svisle, jas podle " +
        "hlasitosti. Na rozdíl od Spektra v modulu Scope je vidět VÝVOJ v čase: klouzavá výška, " +
        "doznívání, kolísání. Logaritmická osa je výchozí, protože na lineární se všechno pod " +
        "1 kHz vejde do spodních pár procent obrázku. Barvy jsou jen pro oko — Šedá je výchozí a " +
        "je to ta, ve které se obrázek dá spolehlivě ČÍST, protože jas roste monotónně s hlasitostí. " +
        "Neovlivňuje zvuk.";

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

/// <summary>
/// Which colour map the node's picture is drawn in. Purely a display choice — no colour map changes
/// what was measured, and the offline <c>tools/Zvuk</c> (the one whose PNGs get READ, by a
/// person or by a model) stays on <see cref="Grey"/> for that reason: a hue map is not monotone in
/// lightness, so "brighter" stops meaning "louder" and a hue transition invents an edge that gets
/// read as structure which is not in the data.
///
/// <para>The other three are here because on a small live node the grey ramp is genuinely hard to
/// read: the eye resolves maybe a dozen greys but hundreds of hues, so a quiet partial that is one
/// shade off the background in <see cref="Grey"/> is plainly visible in <see cref="Inferno"/>.
/// Inferno and Viridis still rise monotonically in lightness and only add hue on top of that;
/// <see cref="Rainbow"/> does not, and is the classic loud spectrogram look — easiest to spot
/// something in, least trustworthy to judge a level from.</para>
/// </summary>
public enum SpectrogramPalette
{
    Grey,
    Inferno,
    Viridis,
    Rainbow,
}
