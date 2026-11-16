using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// An amplitude envelope whose shape is drawn instead of dialled in as four numbers.
///
/// Same ports as <see cref="EnvelopeModule"/> ("In"/"Gate" in, "Out"/"Env" out) so it drops
/// straight into a patch in its place, and the same split into two modes by whether "Gate" is
/// wired. What differs is where the shape comes from, which is also why this is a separate module
/// rather than a mode on <see cref="EnvelopeModule"/>: with a curve, Attack/Decay/Sustain/Release
/// have nothing left to describe, so keeping both sets on one module would leave half the knobs
/// dead whichever mode it was in — the same reason <see cref="WaveshaperModule"/> is not another
/// Type on <see cref="DistortionModule"/>. It also keeps every existing patch's Envelope untouched.
///
/// <para>The sustain point is the whole problem a drawn envelope has and an ADSR does not. A curve
/// is a finite shape, but a held note is not: something has to say which part of the drawn shape
/// waits for the key. <see cref="SustainPointName"/> is that x position — the curve up to it is the
/// attack and decay, the value AT it is the sustain level, and the curve after it is the release.
/// It is a parameter rather than a special breakpoint so that it can sit anywhere, including
/// between two points, and so the file already knows how to record it.</para>
///
/// <para>The default shape is the default ADSR redrawn — instant-ish attack, decay to 0.7, release
/// to zero — so swapping one module for the other changes nothing until the curve is dragged. Like
/// the Waveshaper's identity default, a fresh module should be audible and unsurprising.</para>
///
/// <para>Incidentally this is also how a non-linear decay gets into the engine at all:
/// <see cref="EnvelopeModule"/> interpolates every stage linearly, and a linear decay reads as
/// unnatural because the ear expects an exponential one. Here the bend is drawn.</para>
/// </summary>
public sealed class CurveEnvelopeModule : ModuleBase
{
    public const string SustainPointName = "Bod sustainu";

    /// <summary>Below this the sustain level is treated as zero, so the release is played as drawn
    /// instead of being scaled by a division that would blow up.</summary>
    private const double SilentSustain = 1e-4;

    private enum Stage { Idle, Onset, Sustain, Release }

    private readonly Port _input;
    private readonly Port _gate;
    private readonly Port _output;
    private readonly Port _envOutput;
    private readonly Port _onsetStage;
    private readonly Port _sustainStage;
    private readonly Port _releaseStage;
    private readonly ModuleParameter _onset;
    private readonly ModuleParameter _hold;
    private readonly ModuleParameter _tail;
    private readonly ModuleParameter _sustainPoint;
    private readonly CurveInput _shape;

    /// <summary>Which stage the sockets reported on the previous sample — see the twin field in
    /// <see cref="EnvelopeModule"/>. It gives a skipped stage its one sample, and makes the sample
    /// a stage is entered on a full 1 instead of the first step of its ramp.</summary>
    private int _previousStageIndex = -1;

    private Stage _stage = Stage.Idle;
    private double _level;
    private double _secondsInStage;
    private double _releaseScale = 1;
    private bool _gateWasHigh;

    public CurveEnvelopeModule() : base("Obálka z křivky")
    {
        _input = AddInput("In");
        _gate = AddInput("Gate");
        _output = AddOutput("Out");
        _envOutput = AddOutput("Env");

        // The twin of Envelope's stage sockets — three rather than four, because a drawn envelope
        // has no separate decay: the whole shape before the sustain point IS the onset.
        _onsetStage = AddOutput("A");
        _sustainStage = AddOutput("S");
        _releaseStage = AddOutput("R");

        _onset = AddParameter("Nástup", 0.12, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);

        // The counterpart of EnvelopeModule's Držení, with the same sentinel and for the same
        // reason — the two modules are meant to be swapped one for the other, so the knob that
        // says how long the hold lasts has to mean the same thing on both.
        _hold = AddParameter(EnvelopeModule.HoldName, EnvelopeModule.AutomaticHold, 0, 10, "s",
            modulatable: false, tempoSync: TempoSyncKind.Seconds);
        _tail = AddParameter("Dozvuk", 0.3, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);

        // Not modulatable: it is a position on a shape, read once per block, and a per-sample
        // signal moving it would mean the shape no longer describes anything. Same call as Seed.
        _sustainPoint = AddParameter(SustainPointName, 0.35, 0, 1, modulatable: false);

        // 0..1 and nothing wired: the built-in shape is the default ADSR redrawn, so swapping
        // this module in for an Envelope changes nothing until a curve is actually patched in.
        _shape = AddCurveInput("Tvar", DefaultShape(), CurveOutput.Unipolar);
    }

    public override string Description =>
        "Obálka hlasitosti nakreslená jako křivka místo čtyř čísel. Bod sustainu dělí křivku na dvě " +
        "části: co je před ním, se přehraje za Nástup sekund, hodnota v něm se drží, a co je za ním, " +
        "doznívá Dozvuk sekund. Jak dlouho se drží, říká bez zapojeného Gate parametr Držení; na " +
        "nule se převezme z modulu Nota, a bez něj se drží až do Dozvuku před koncem vykreslení, " +
        "stejně jako Envelope. " +
        "Se zapojeným Gate drží, dokud je klávesa dole. Výstup Env nese samotnou křivku 0-1.";

    /// <summary>The shape this envelope will actually play: whatever is patched into "Tvar",
    /// conformed to 0..1, or the built-in ADSR while nothing is. For the node's preview, so the
    /// canvas can show what the module will do rather than what it last did.</summary>
    public CurveInput Shape => _shape;

    /// <summary>How long this envelope needs to finish in the ungated one-shot mode, at
    /// <paramref name="bpm"/>. The twin of <see cref="EnvelopeModule.OneShotSecondsAt"/>, and used
    /// by the same warning: a tail that does not fit the clip ends the file part way down the
    /// curve, which is a step into silence rather than a finished decay.</summary>
    public double OneShotSecondsAt(double bpm, double noteHoldSeconds = 0)
    {
        var stages = _onset.ResolveValue(bpm) + _tail.ResolveValue(bpm);
        var hold = ResolveHold(_hold.ResolveValue(bpm), noteHoldSeconds);

        // A key hold is counted from the start of the note, so the tail follows it rather than
        // following the onset — same reasoning as EnvelopeModule.OneShotSecondsAt.
        if (hold.FromKey)
            return hold.Seconds + _tail.ResolveValue(bpm);

        return hold.Seconds > EnvelopeModule.AutomaticHold ? stages + hold.Seconds : stages;
    }

    /// <summary>The twin of <see cref="EnvelopeModule"/>'s resolver, and deliberately the same
    /// rule: own Držení, then the Nota module's, then automatic. The two modules are meant to be
    /// swapped one for the other, so a patch must not change length by swapping them — including
    /// the distinction between a plateau and a key hold, which is why this returns the same pair
    /// rather than a bare number. See <see cref="EnvelopeModule.NoteHoldPlan"/>.</summary>
    private static EnvelopeModule.NoteHoldPlan ResolveHold(double ownHold, double noteHoldSeconds)
        => EnvelopeModule.ResolveNoteHold(ownHold, noteHoldSeconds);

    /// <summary>The "Bod sustainu" knob. Exposed for the same reason as <see cref="Shape"/> — a
    /// drawn envelope without its sustain point marked does not say where it will hold.</summary>
    public ModuleParameter SustainPoint => _sustainPoint;

    /// <summary>The default ADSR (A 0.02, D 0.1, S 0.7, R 0.3) drawn out: the attack and decay fill
    /// the part before the sustain point, the release the part after it. The decay and the release
    /// both leave their point steeply and settle flat, so it already sounds a little more natural
    /// than the straight lines it replaces — the ear expects a decay to fall fast and then linger,
    /// and a straight one does not. Used while nothing is patched into "Tvar".</summary>
    private static Curve DefaultShape() => new(new[]
    {
        new CurvePoint(0, 0),
        new CurvePoint(0.08, 1, Out: new CurveHandle(0.03, -0.22)),
        new CurvePoint(0.35, 0.7, Out: new CurveHandle(0.08, -0.5), In: new CurveHandle(-0.12, 0)),
        new CurvePoint(1, 0, In: new CurveHandle(-0.3, 0)),
    });

    /// <summary>Scratch buffer for <see cref="LiveGate"/>, the twin of
    /// <c>EnvelopeModule.LiveGate</c> — same reason, same reuse.</summary>
    private float[]? _liveGate;

    /// <summary>Stands in for an unpatched Gate wire while a live voice drives this envelope. See
    /// <c>EnvelopeModule.LiveGate</c> for why the ungated path cannot do live play itself.</summary>
    private float[] LiveGate(RenderContext ctx, bool keyDown)
    {
        if (_liveGate is null || _liveGate.Length < ctx.SampleCount)
            _liveGate = new float[ctx.SampleCount];

        var value = keyDown ? 1f : 0f;
        for (var i = 0; i < ctx.SampleCount; i++)
            _liveGate[i] = value;

        return _liveGate;
    }

    public override void ResetState()
    {
        _stage = Stage.Idle;
        _level = 0;
        _secondsInStage = 0;
        _releaseScale = 1;
        _gateWasHigh = false;
        _previousStageIndex = -1;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var onset = GetModulatedValues(_onset, ctx, inputBuffers);
        var tail = GetModulatedValues(_tail, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var envCurve = NewBuffer(ctx);
        var stages = new[] { NewBuffer(ctx), NewBuffer(ctx), NewBuffer(ctx) };

        // Same as EnvelopeModule: this module owns the note's length from here on.
        ctx.NoteHoldConsumed = true;

        if (inputBuffers.TryGetValue(_gate, out var wiredGate))
            ProcessGated(ctx, wiredGate, onset, tail, input, output, envCurve, stages);
        else if (ctx.LiveKeyDown is { } keyDown)
            ProcessGated(ctx, LiveGate(ctx, keyDown), onset, tail, input, output, envCurve, stages);
        else
            ProcessFixedDuration(ctx, onset, tail, input, output, envCurve, stages);

        return new Dictionary<Port, float[]>
        {
            [_output] = output,
            [_envOutput] = envCurve,
            [_onsetStage] = stages[0],
            [_sustainStage] = stages[1],
            [_releaseStage] = stages[2],
        };
    }

    /// <summary>No Gate wired — the one-shot case. Laid out exactly like
    /// <see cref="EnvelopeModule"/>'s ungated branch: with Držení set, hold the sustain that long
    /// and then play the rest of the curve; at <see cref="EnvelopeModule.AutomaticHold"/>, hold it
    /// until Dozvuk before the end of the clip instead.</summary>
    private void ProcessFixedDuration(RenderContext ctx, float[] onset, float[] tail, float[] input, float[] output, float[] envCurve, float[][] stages)
    {
        var shape = _shape.Curve;
        var sustainX = Math.Clamp(_sustainPoint.Value, 0, 1);
        var duration = ctx.DurationSeconds;

        // Once per buffer: Držení is not modulatable, so there is nothing to read per sample.
        var hold = ResolveHold(_hold.ResolveValue(ctx.Bpm), ctx.NoteHoldSeconds);

        // Where along the shape the envelope had got to on the sample before the release began.
        // Only the key branch uses it; see where it is carried forward.
        var xBeforeRelease = 0.0;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var t = (double)i / ctx.SampleRate;
            var onsetSeconds = Math.Max(0, onset[i]);
            var tailSeconds = Math.Max(0, tail[i]);

            // The automatic branch is left exactly as it was, Max(onsetSeconds, …) included: a clip
            // too short for both halves gives the onset what there is and the tail the rest, rather
            // than letting the release start before the attack has finished. Every patch saved so
            // far renders through it, so it stays byte-for-byte what it was.
            var releaseStart = hold.FromKey
                ? hold.Seconds
                : hold.Seconds > EnvelopeModule.AutomaticHold
                    ? onsetSeconds + hold.Seconds
                    : Math.Max(onsetSeconds, duration - tailSeconds);

            // A key let go part way up the onset carries on into the tail from where it had got to,
            // which is what the gated state machine does. Only the key branch is routed this way:
            // in the other two the release cannot begin before the onset is finished, so x there is
            // sustainX by construction and the old order still decides.
            var keyReleased = hold.FromKey && t >= releaseStart;

            double x;
            if (keyReleased)
                x = tailSeconds > 0
                    ? xBeforeRelease + (1 - xBeforeRelease) * Math.Min(1, (t - releaseStart) / tailSeconds)
                    : 1;
            else if (t < onsetSeconds)
                x = sustainX * (t / onsetSeconds);
            else if (t < releaseStart)
                x = sustainX;
            else if (tailSeconds > 0)
                x = sustainX + (1 - sustainX) * Math.Min(1, (t - releaseStart) / tailSeconds);
            else
                x = 1;

            // Carried forward a sample at a time, so a modulated Nástup is accounted for for free —
            // the point the shape actually reached IS the point to leave from.
            if (!keyReleased)
                xBeforeRelease = x;

            // Same correction as in EnvelopeModule: at Dozvuk 0 the automatic branch puts the
            // release exactly on the end of the clip, so no sample would belong to it and the
            // moment would be uncatchable. The level above is untouched.
            var stageReleaseStart = Math.Min(releaseStart, (ctx.SampleCount - 1) / (double)ctx.SampleRate);

            var stageIndex = keyReleased ? 2
                : t < onsetSeconds && onsetSeconds > 0 ? 0
                : t < stageReleaseStart ? 1
                : 2;
            var stageStart = stageIndex switch { 0 => 0, 1 => onsetSeconds, _ => stageReleaseStart };
            var stageLength = stageIndex switch { 0 => onsetSeconds, 1 => stageReleaseStart - onsetSeconds, _ => tailSeconds };

            for (var skipped = _previousStageIndex + 1; skipped < stageIndex; skipped++)
                stages[skipped][i] = 1;

            stages[stageIndex][i] = stageIndex != _previousStageIndex || stageIndex == 1 || stageLength <= 0
                ? 1
                : (float)Math.Clamp(1 - (t - stageStart) / stageLength, 0, 1);

            _previousStageIndex = stageIndex;

            var level = shape.Sample(x);
            envCurve[i] = level;
            output[i] = input[i] * level;
        }
    }

    /// <summary>Gate wired — a real note-on/note-off state machine, carried across the small blocks
    /// a live voice renders in.</summary>
    private void ProcessGated(RenderContext ctx, float[] gate, float[] onset, float[] tail, float[] input, float[] output, float[] envCurve, float[][] stages)
    {
        var shape = _shape.Curve;
        var sustainX = Math.Clamp(_sustainPoint.Value, 0, 1);
        var sustainLevel = shape.Sample(sustainX);
        var sampleDuration = 1.0 / ctx.SampleRate;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var gateHigh = gate[i] >= 0.5f;
            var onsetSeconds = Math.Max(0, onset[i]);
            var tailSeconds = Math.Max(0, tail[i]);

            if (gateHigh && !_gateWasHigh)
            {
                _stage = Stage.Onset;
                _secondsInStage = 0;
            }
            else if (!gateHigh && _gateWasHigh)
            {
                // Releasing mid-onset must not jump: the drawn release starts at the sustain level,
                // so scale it to start where the envelope actually is. A sustain drawn at zero has
                // no ratio to take, and the curve is played as drawn.
                _releaseScale = sustainLevel > SilentSustain ? _level / sustainLevel : 1;
                _stage = Stage.Release;
                _secondsInStage = 0;
            }

            _gateWasHigh = gateHigh;

            // Stage and progress read before the switch, for the reason spelled out in
            // EnvelopeModule: the switch resets the clock as a stage ends, so reading it after
            // would put the socket back to 1 on that sample and fire a second trigger.
            var stageAtTop = _stage;
            var secondsAtTop = _secondsInStage;
            var stageLength = stageAtTop switch
            {
                Stage.Onset => onsetSeconds,
                Stage.Release => tailSeconds,
                _ => 0,
            };

            switch (_stage)
            {
                case Stage.Onset when onsetSeconds <= 0:
                    _level = sustainLevel;
                    _stage = Stage.Sustain;
                    break;
                case Stage.Onset:
                    _level = shape.Sample(sustainX * (_secondsInStage / onsetSeconds));
                    if (_secondsInStage >= onsetSeconds)
                    {
                        _level = sustainLevel;
                        _stage = Stage.Sustain;
                    }
                    break;
                case Stage.Sustain:
                    _level = sustainLevel;
                    break;
                case Stage.Release when tailSeconds <= 0:
                    _level = 0;
                    _stage = Stage.Idle;
                    break;
                case Stage.Release:
                    var progress = Math.Min(1, _secondsInStage / tailSeconds);
                    _level = shape.Sample(sustainX + (1 - sustainX) * progress) * _releaseScale;
                    if (_secondsInStage >= tailSeconds)
                    {
                        _level = 0;
                        _stage = Stage.Idle;
                    }
                    break;
                default:
                    _level = 0;
                    break;
            }

            var stageIndex = stageAtTop switch
            {
                Stage.Onset => 0,
                Stage.Sustain => 1,
                Stage.Release => 2,
                _ => -1,
            };

            // Entering a stage writes a full 1, not the first step of the ramp — same rule, and the
            // same reason, as EnvelopeModule.WriteStage.
            var entering = stageIndex != _previousStageIndex;
            _previousStageIndex = stageIndex;

            if (stageIndex >= 0)
                stages[stageIndex][i] = entering || stageIndex == 1 || stageLength <= 0
                    ? 1
                    : (float)Math.Clamp(1 - secondsAtTop / stageLength, 0, 1);

            _secondsInStage += sampleDuration;

            var clamped = (float)Math.Clamp(_level, 0, 1);
            envCurve[i] = clamped;
            output[i] = input[i] * clamped;
        }
    }
}
