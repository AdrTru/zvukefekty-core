using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Terminal node of a patch. Rendering the graph means rendering this module —
/// its "Out" buffer is what gets played back or exported.
///
/// <para>Also the last resort for the Nota module's Držení. A patch that shapes its own amplitude
/// — anything with an envelope in it — is left alone; one that does not, such as the three-module
/// Nota → Oscilátor → Output, has nothing anywhere in it that can produce silence, so without this
/// the oscillator runs flat out for the whole clip and Držení does nothing whatsoever. See
/// <see cref="ApplyNoteHold"/>.</para>
/// </summary>
public sealed class OutputModule : ModuleBase
{
    /// <summary>How long the cut at the end of Držení takes. A step straight to zero is a click,
    /// and the note ends on a full-amplitude sample far more often than not — the hold is a round
    /// number of seconds and the waveform is not. Short enough not to be heard as a fade.</summary>
    private const double CutSeconds = 0.005;

    /// <summary>How far into the cut the live fade has got, or −1 while the key is down. Held
    /// across blocks, because a ~20 ms block is longer than the cut but the key comes up whenever
    /// it comes up — so the block the fade starts in has to hand its progress to the next one.</summary>
    private int _liveCutProgress = -1;

    private readonly Port _input;
    private readonly Port _output;

    public OutputModule() : base("Output")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");
    }

    public override string Description => "Koncový bod sestavy — signál na jeho vstupu je to, co se přehraje nebo exportuje.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);

        // Only when nothing upstream shaped the note: an envelope that has already released it
        // would otherwise be cut a second time. Live play asks the key, a batch render asks Držení
        // — the same question, answered by whichever of the two exists.
        if (!ctx.NoteHoldConsumed)
        {
            if (ctx.LiveKeyDown is { } keyDown)
                input = ApplyLiveKey(input, ctx, keyDown);
            else if (ctx.NoteHoldSeconds > 0)
                input = ApplyNoteHold(input, ctx);
        }

        return new Dictionary<Port, float[]> { [_output] = input };
    }

    public override void ResetState() => _liveCutProgress = -1;

    /// <summary>
    /// Silences a live voice once its key comes up, over <see cref="CutSeconds"/>.
    ///
    /// <para>Without it a patch with no envelope never stops: nothing in it can produce silence, so
    /// the note went on sounding at full amplitude until <c>Voice</c>'s five-second safety timeout
    /// noticed it was never going quiet and took the voice away. Reaching actual zero here is what
    /// lets that same timeout do its real job — <c>Voice.TrackSilenceSinceRelease</c> retires the
    /// voice after two silent blocks, so the slot comes back immediately instead of five seconds
    /// later.</para>
    /// </summary>
    private float[] ApplyLiveKey(float[] input, RenderContext ctx, bool keyDown)
    {
        if (keyDown)
        {
            _liveCutProgress = -1;
            return input;
        }

        if (_liveCutProgress < 0)
            _liveCutProgress = 0;

        var cut = Math.Max(1, (int)Math.Round(CutSeconds * ctx.SampleRate));
        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount && _liveCutProgress < cut; i++, _liveCutProgress++)
            output[i] = input[i] * (float)(1 - _liveCutProgress / (double)cut);

        return output;
    }

    /// <summary>
    /// Silences the render once Držení has run out, over <see cref="CutSeconds"/>.
    ///
    /// <para>Copied rather than written in place: the input buffer belongs to whichever module
    /// produced it, and that module's output may be read by something else in the same pass — a
    /// second chain, or a monitor. Writing through it would silence those too.</para>
    ///
    /// <para>A gate rather than an envelope on purpose. This is the floor, not a feature: it makes
    /// the knob do the one thing its name promises in a patch that has nothing else to do it with.
    /// A shape worth the name wants an Envelope module, which is also what makes this code stand
    /// aside — see <see cref="RenderContext.NoteHoldConsumed"/>.</para>
    /// </summary>
    private static float[] ApplyNoteHold(float[] input, RenderContext ctx)
    {
        var held = (int)Math.Round(ctx.NoteHoldSeconds * ctx.SampleRate);
        if (held >= ctx.SampleCount)
            return input;

        var cut = Math.Max(1, (int)Math.Round(CutSeconds * ctx.SampleRate));
        var output = NewBuffer(ctx);
        Array.Copy(input, output, Math.Min(held, ctx.SampleCount));

        for (var i = held; i < ctx.SampleCount; i++)
        {
            var into = i - held;
            if (into >= cut)
                break;

            output[i] = input[i] * (float)(1 - into / (double)cut);
        }

        return output;
    }
}
