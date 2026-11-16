using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Classic ADSR amplitude envelope. Attack/Decay/Sustain/Release are each modulatable,
/// evaluated sample-by-sample. Besides "Out" (the shaped "In" audio), also exposes the raw
/// 0-1 curve on "Env" — patch that into another module's parameter input for a pitch sweep
/// (e.g. Oscillator.Frequency, for a laser/spell zap) or to shape several layers with one
/// shared envelope, without needing an audio signal through "In" at all.
///
/// Three cases, and only the first of them is the one-shot arithmetic:
/// - Gate unwired, batch render: the length of the sustain plateau comes from
///   <see cref="HoldName"/>, then from the Nota module's Držení, then — with neither — from
///   whatever is left of the rendered buffer once Release has been subtracted. See
///   <see cref="ResolveNoteHold"/> for why those first two are not the same kind of number.
/// - Gate wired: a real note-on/note-off state machine driven by the incoming Gate signal's
///   rising/falling edges, tracked in instance fields so it carries on correctly across the small
///   successive blocks a live voice renders. Držení plays no part — the key says how long.
/// - Gate unwired, live voice: the same state machine, fed a gate synthesised from the key — see
///   <see cref="LiveGate"/>. The one-shot arithmetic CANNOT serve live play, because it measures
///   time from the start of the buffer and a live buffer is one ~20 ms block.
/// </summary>
public sealed class EnvelopeModule : ModuleBase
{
    public const string HoldName = "Držení";

    /// <summary>What <see cref="HoldName"/> means when it is left at zero: don't time the plateau
    /// at all, stretch it to whatever is left of the clip once Release has been subtracted — the
    /// behaviour this module had before Držení existed.
    ///
    /// <para>A sentinel rather than a second "automatic/manual" knob because the default has to BE
    /// the old behaviour: Držení is absent from every patch saved so far, so those files load with
    /// whatever this constant says, and anything but "as before" would silently retune them.
    /// A plateau of genuinely zero length is still reachable — set Držení just above zero, or take
    /// Sustain to 0 and let Decay do the work, which is what an ADSR means by it anyway.</para></summary>
    public const double AutomaticHold = 0;

    /// <summary>Ceiling for Attack/Decay/Držení/Release. Ten seconds so "2 takty" resolves rather
    /// than clamps down to 60 BPM, and so this module and <see cref="CurveEnvelopeModule"/> agree.</summary>
    private const double MaxStageSeconds = 10;

    private enum Stage { Idle, Attack, Decay, Sustain, Release }

    private readonly Port _input;
    private readonly Port _gate;
    private readonly Port _output;
    private readonly Port _envOutput;
    private readonly Port _attackStage;
    private readonly Port _decayStage;
    private readonly Port _sustainStage;
    private readonly Port _releaseStage;
    private readonly ModuleParameter _attack;
    private readonly ModuleParameter _decay;
    private readonly ModuleParameter _sustain;
    private readonly ModuleParameter _hold;
    private readonly ModuleParameter _release;

    /// <summary>Which stage the sockets reported on the previous sample. Two things read it: a
    /// stage the ungated path skipped entirely still gets its one sample, and the first sample of
    /// any stage is written as a full 1 rather than one step down the ramp. −1 before the first
    /// sample of a render, and while nothing is sounding.</summary>
    private int _previousStageIndex = -1;

    /// <summary>Scratch buffer for <see cref="LiveGate"/>; see there for why it is kept.</summary>
    private float[]? _liveGate;

    private Stage _stage = Stage.Idle;
    private double _level;
    private double _levelAtStageStart;
    private double _secondsInStage;
    private bool _gateWasHigh;

    public EnvelopeModule() : base("Envelope")
    {
        _input = AddInput("In");
        _gate = AddInput("Gate");
        _output = AddOutput("Out");
        _envOutput = AddOutput("Env");

        // Which stage is running, one socket each. Env cannot answer that: it falls during Decay
        // and again during Release, and at Sustain 0 it is flat on zero through both — so "the
        // note has been let go" is invisible in it, which is exactly the moment worth catching.
        _attackStage = AddOutput("A");
        _decayStage = AddOutput("D");
        _sustainStage = AddOutput("S");
        _releaseStage = AddOutput("R");

        // 10 s rather than the 5 s these had, to match Obálka z křivky. The two modules are meant
        // to be swapped one for the other, and a lower ceiling here meant "2 takty" resolved to a
        // different length depending on which one you happened to be using. Costs nothing: an
        // envelope allocates no buffer from its maximum, unlike a delay line.
        _attack = AddParameter("Attack", 0.02, 0, MaxStageSeconds, "s", tempoSync: TempoSyncKind.Seconds);
        _decay = AddParameter("Decay", 0.1, 0, MaxStageSeconds, "s", tempoSync: TempoSyncKind.Seconds);
        _sustain = AddParameter("Sustain", 0.7, 0, 1);

        // Not modulatable, for the same reason as NoteModule's Držení: "how long is the note held"
        // is a structural length, read once, not a per-sample signal. See AutomaticHold for why
        // zero means "fill the clip" rather than "no plateau at all".
        _hold = AddParameter(HoldName, AutomaticHold, 0, MaxStageSeconds, "s",
            modulatable: false, tempoSync: TempoSyncKind.Seconds);
        _release = AddParameter("Release", 0.3, 0, MaxStageSeconds, "s", tempoSync: TempoSyncKind.Seconds);

        ShapeParameters = new[] { _attack, _decay, _sustain, _hold, _release };
    }

    /// <summary>The knobs the shape is made of. For the node preview, which has to repaint
    /// when any of them moves.</summary>
    public IReadOnlyList<ModuleParameter> ShapeParameters { get; }

    /// <summary>
    /// This envelope's numbers as a drawn shape, for the preview on its node. Not used to
    /// render audio — <see cref="Process"/> computes the same thing per sample, and duplicating the
    /// arithmetic here would be a second place to get it wrong.
    ///
    /// <para>The horizontal axis is proportion, not seconds. The timed stages share the width in
    /// proportion to their own seconds, so "the attack is a blink and the release is long" reads
    /// straight off the picture.</para>
    ///
    /// <para>The plateau is the one part that is not always a length. At
    /// <see cref="AutomaticHold"/> nothing fixes it — ungated it runs until Release before the end
    /// of the clip, gated it lasts as long as the key is held — so it draws at a fixed share, just
    /// wide enough to read as a hold rather than a corner between decay and release. Once Držení
    /// is set it IS a length, and then it shares the width like any other stage, so a plateau
    /// shorter than the release draws shorter.</para>
    /// </summary>
    public Curve ShapeAsCurve()
    {
        // Enough plateau to read as a hold rather than a corner between decay and release. Used
        // only while Držení is automatic, where the plateau has no length of its own to scale to.
        const double untimedSustainShare = 0.28;

        // A floor per stage keeps the breakpoints strictly ordered, so a stage set to zero draws as
        // the vertical edge it really is instead of collapsing two points onto one x.
        const double minimum = 0.004;

        // ResolveValue, so a stage switched to a note value draws at the length it will actually
        // play. The editor's tempo rather than a render's, because this picture belongs to the
        // editor -- see TempoDisplay. Matters even though the axis is proportional: with some
        // stages synced and others not, their RATIO moves with the tempo.
        var bpm = TempoDisplay.Current.Bpm;
        var attack = Math.Max(0, _attack.ResolveValue(bpm));
        var decay = Math.Max(0, _decay.ResolveValue(bpm));
        var sustain = Math.Clamp(_sustain.Value, 0, 1);
        var hold = Math.Max(0, _hold.ResolveValue(bpm));
        var release = Math.Max(0, _release.ResolveValue(bpm));

        var isTimedHold = hold > AutomaticHold;
        var sustainShare = isTimedHold ? 0 : untimedSustainShare;
        var moving = attack + decay + release + hold;
        var timed = 1.0 - sustainShare;
        double Share(double seconds) => moving <= 0 ? minimum : Math.Max(minimum, timed * seconds / moving);

        var attackEnd = Share(attack);
        var decayEnd = attackEnd + Share(decay);
        // With a timed plateau every stage has a share of the width, so the release starts where
        // the plateau ends. Without one the plateau absorbs whatever the three stages did not need,
        // so an envelope with all three at zero draws as the square pulse it actually is.
        var releaseStart = isTimedHold
            ? Math.Clamp(decayEnd + Share(hold), decayEnd + minimum, 1 - minimum)
            : Math.Clamp(1 - Share(release), decayEnd + minimum, 1 - minimum);

        return new Curve(new[]
        {
            new CurvePoint(0, 0),
            new CurvePoint(attackEnd, 1),
            new CurvePoint(decayEnd, sustain),
            new CurvePoint(releaseStart, sustain),
            new CurvePoint(1, 0),
        });
    }

    /// <summary>How long this envelope needs to finish in the ungated one-shot mode, at
    /// <paramref name="bpm"/>: everything up to and including the release. Exposed so the UI can
    /// warn when it runs past the clip, which would export a file whose last sample is still part
    /// way down the release — an audible step into silence, not a finished tail.
    ///
    /// <para>Takes a tempo because any of these stages can be set as a note value. Meaningless
    /// with Gate wired, where the key decides the length; the caller checks that.</para></summary>
    public double OneShotSecondsAt(double bpm, double noteHoldSeconds = 0)
    {
        var stages = _attack.ResolveValue(bpm) + _decay.ResolveValue(bpm) + _release.ResolveValue(bpm);
        var hold = ResolveNoteHold(_hold.ResolveValue(bpm), noteHoldSeconds);

        // A key hold is counted from the start of the note, so what the envelope needs is the hold
        // plus the release — NOT the stages plus the hold. Adding the stages on would ask for room
        // for a Decay the key may well have cut short.
        if (hold.FromKey)
            return hold.Seconds + _release.ResolveValue(bpm);

        // At AutomaticHold the release is anchored to the end of the clip rather than following a
        // plateau, so the envelope needs room for the stages and nothing more.
        return hold.Seconds > AutomaticHold ? stages + hold.Seconds : stages;
    }

    /// <summary>
    /// A gate signal standing in for the Gate wire the user did not patch, while a live voice is
    /// driving this envelope.
    ///
    /// <para>Why the ungated path cannot serve live play at all: it works out the level from
    /// <c>t = i / SampleRate</c>, time from the start of the BUFFER, and a live voice's buffer is
    /// one ~20 ms block. So t returned to zero every block and the whole ADSR played again in each
    /// one — measured on piano.zvuky with Gate unpatched, the note held full amplitude (peak 0.887
    /// against 0.770 for the same patch rendered in one pass) and was still doing so two seconds
    /// after the key came up. Routing it through the gated state machine instead fixes both halves
    /// at once, because that path already carries its stage and level across blocks in fields.</para>
    ///
    /// <para>One buffer per instance, reused: a voice renders a block every few milliseconds and
    /// this would otherwise be a fresh allocation each time, on the thread where a GC pause is
    /// audible — see <c>RealtimeInstrumentPlayer.Start</c>.</para>
    /// </summary>
    private float[] LiveGate(RenderContext ctx, bool keyDown)
    {
        if (_liveGate is null || _liveGate.Length < ctx.SampleCount)
            _liveGate = new float[ctx.SampleCount];

        var value = keyDown ? 1f : 0f;
        for (var i = 0; i < ctx.SampleCount; i++)
            _liveGate[i] = value;

        return _liveGate;
    }

    /// <summary>What the ungated path holds for, and — just as important — which of the two
    /// different things "hold" can mean here.
    ///
    /// <para>This module's own Držení is a PLATEAU: how long the sustain LEVEL is held, which by
    /// definition starts once Attack and Decay are done. The Nota module's Držení is a KEY: how
    /// long the note is pressed, counted from the start of the note, and it can perfectly well run
    /// out while the Decay is still falling. Feeding the second into the first's slot looks right
    /// and is not — measured on piano.zvuky (Decay 2.2 s, Držení 0.5 s) it gave a 2.60 s note
    /// where the same patch WITH the Gate wire gives 0.80 s, and making those two agree is the
    /// entire point of reading the Nota module here at all.</para></summary>
    internal readonly record struct NoteHoldPlan(double Seconds, bool FromKey);

    /// <summary>Precedence: this module's own Držení first, because it is the more specific of the
    /// two; then the Nota module's, so the knob works without the Gate wire; then automatic, which
    /// is what a patch with no Nota module at all still gets — that is every one-shot effect saved
    /// so far, and they render exactly as before.</summary>
    internal static NoteHoldPlan ResolveNoteHold(double ownHold, double noteHoldSeconds)
    {
        var own = Math.Max(0, ownHold);
        if (own > AutomaticHold)
            return new NoteHoldPlan(own, FromKey: false);

        var fromNote = Math.Max(0, noteHoldSeconds);
        return fromNote > AutomaticHold ? new NoteHoldPlan(fromNote, FromKey: true) : default;
    }

    private NoteHoldPlan ResolveHold(RenderContext ctx) => ResolveNoteHold(_hold.ResolveValue(ctx.Bpm), ctx.NoteHoldSeconds);

    public override string Description =>
        "Klasická ADSR obálka hlasitosti. Sustain je HLADINA (0-1); jak dlouho se ta hladina drží, " +
        "říká Držení. Držení na nule znamená: převezmi ho z modulu Nota, a když v sestavě žádný " +
        "není, roztáhni plošinku přes zbytek vykreslení a uvolni Release před jeho koncem. " +
        "Se zapojeným Gate se Držení neuplatní — tam délku tónu určuje klávesa, a totéž platí " +
        "při hraní na klávesnici i bez toho drátu. " +
        "Výstup Env nese syrovou křivku 0-1 pro modulaci jiných parametrů.";

    public override void ResetState()
    {
        _stage = Stage.Idle;
        _level = 0;
        _levelAtStageStart = 0;
        _secondsInStage = 0;
        _gateWasHigh = false;
        _previousStageIndex = -1;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var attack = GetModulatedValues(_attack, ctx, inputBuffers);
        var decay = GetModulatedValues(_decay, ctx, inputBuffers);
        var sustain = GetModulatedValues(_sustain, ctx, inputBuffers);
        var release = GetModulatedValues(_release, ctx, inputBuffers);

        var output = NewBuffer(ctx);
        var envCurve = NewBuffer(ctx);
        var stages = new[]
        {
            NewBuffer(ctx), NewBuffer(ctx),
            NewBuffer(ctx), NewBuffer(ctx),
        };

        // Either branch shapes how long the note lasts, so the Output must not shape it again —
        // see RenderContext.NoteHoldConsumed.
        ctx.NoteHoldConsumed = true;

        if (inputBuffers.TryGetValue(_gate, out var wiredGate))
            ProcessGated(ctx, wiredGate, attack, decay, sustain, release, input, output, envCurve, stages);
        else if (ctx.LiveKeyDown is { } keyDown)
            ProcessGated(ctx, LiveGate(ctx, keyDown), attack, decay, sustain, release, input, output, envCurve, stages);
        else
            ProcessFixedDuration(ctx, attack, decay, sustain, release, input, output, envCurve, stages);

        return new Dictionary<Port, float[]>
        {
            [_output] = output,
            [_envOutput] = envCurve,
            [_attackStage] = stages[0],
            [_decayStage] = stages[1],
            [_sustainStage] = stages[2],
            [_releaseStage] = stages[3],
        };
    }

    /// <summary>No Gate wired — the one-shot case, which is also the sample-generating one.
    /// With Držení set the plateau is that long and the release follows it; at
    /// <see cref="AutomaticHold"/> the release is anchored to the end of the clip instead and the
    /// plateau is whatever is left, which is what this module did before Držení existed.</summary>
    private void ProcessFixedDuration(RenderContext ctx, float[] attack, float[] decay, float[] sustain, float[] release,
        float[] input, float[] output, float[] envCurve, float[][] stages)
    {
        var duration = ctx.DurationSeconds;

        // Once per buffer, not per sample: Držení is not modulatable, so there is nothing to read
        // per sample and resolving it in the loop would be the same division 44 100 times a second.
        var hold = ResolveHold(ctx);

        // The level the envelope had reached on the sample before the release began — only the key
        // branch below uses it; see where it is carried forward.
        var levelBeforeRelease = 0.0;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var t = (double)i / ctx.SampleRate;
            var a = Math.Max(0, attack[i]);
            var d = Math.Max(0, decay[i]);
            var s = Math.Clamp(sustain[i], 0, 1);
            var r = Math.Max(0, release[i]);

            // The automatic branch is left exactly as it was, down to the Max(0, …) that lets a
            // release too long for the clip start at zero: it is what every patch saved so far
            // renders through, and "nearly the same" would retune all of them. Its truncation is
            // now reported instead of fixed — see OneShotSecondsAt and MainViewModel.HoldWarning.
            var releaseStart = hold.FromKey
                ? hold.Seconds
                : hold.Seconds > AutomaticHold
                    ? a + d + hold.Seconds
                    : Math.Max(0, duration - r);

            // A key let go mid-Decay releases from wherever the level had got to, which is what the
            // gated state machine does and what makes the two paths agree. Only the key branch is
            // routed this way: in the other two the release cannot begin before the plateau, so the
            // level there is s by construction and the old order still decides.
            var keyReleased = hold.FromKey && t >= releaseStart;

            double level;
            if (keyReleased)
            {
                level = r > 0 ? levelBeforeRelease * (1 - (t - releaseStart) / r) : 0;
            }
            else if (t < a && a > 0)
            {
                level = t / a;
            }
            else if (t < a + d && d > 0)
            {
                level = 1 - (1 - s) * (t - a) / d;
            }
            else if (t < releaseStart)
            {
                level = s;
            }
            else if (r > 0)
            {
                level = s * (1 - (t - releaseStart) / r);
            }
            else
            {
                level = 0;
            }

            // Carried forward one sample at a time rather than worked out in closed form, so a
            // modulated Attack or Decay is accounted for for free — the level the envelope actually
            // reached IS the level to release from.
            if (!keyReleased)
                levelBeforeRelease = level;

            // The release moment for the STAGE sockets, which is not quite the one the level uses:
            // in the automatic branch releaseStart is duration − r, so at r = 0 it lands exactly on
            // the end of the clip and no sample belongs to Release at all. Pulling it back to the
            // last sample gives that moment somewhere to happen — measured before this line, a
            // Release of 0 s produced no edge whatsoever, which is the one thing these sockets
            // exist to prevent. The level arithmetic above is deliberately left alone: moving it
            // would retune every patch ever saved.
            var stageReleaseStart = Math.Min(releaseStart, (ctx.SampleCount - 1) / (double)ctx.SampleRate);

            // Which stage this sample belongs to, by the same boundaries the level above uses.
            // Unlike the gated path this is arithmetic on t rather than a state machine, so a
            // zero-length stage owns NO sample of its own — `t < a && a > 0` is never true when
            // a is 0 — and the pulse has to be put in by hand, below.
            var stageIndex = keyReleased ? 3
                : t < a && a > 0 ? 0
                : t < a + d && d > 0 ? 1
                : t < stageReleaseStart ? 2
                : 3;

            var stageStart = stageIndex switch { 0 => 0, 1 => a, 2 => a + d, _ => stageReleaseStart };
            var stageLength = stageIndex switch { 0 => a, 1 => d, 2 => stageReleaseStart - (a + d), _ => r };

            // Every stage that was stepped over between the previous sample and this one gets its
            // one sample at 1 here. Two can be skipped at once (Attack and Decay both at 0), and
            // then both sockets pulse on the same sample — which is right: both moments happened.
            for (var skipped = _previousStageIndex + 1; skipped < stageIndex; skipped++)
                stages[skipped][i] = 1;

            // Sustain holds rather than ramps, and the sample a stage is entered on is a full 1 —
            // see WriteStage, which does the same for the gated path.
            stages[stageIndex][i] = stageIndex != _previousStageIndex || stageIndex == 2 || stageLength <= 0
                ? 1
                : (float)Math.Clamp(1 - (t - stageStart) / stageLength, 0, 1);

            _previousStageIndex = stageIndex;

            var clampedLevel = (float)Math.Clamp(level, 0, 1);
            envCurve[i] = clampedLevel;
            output[i] = input[i] * clampedLevel;
        }
    }

    private void ProcessGated(RenderContext ctx, float[] gate, float[] attack, float[] decay, float[] sustain, float[] release,
        float[] input, float[] output, float[] envCurve, float[][] stages)
    {
        var sampleDuration = 1.0 / ctx.SampleRate;

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var gateHigh = gate[i] >= 0.5f;
            var a = Math.Max(0, attack[i]);
            var d = Math.Max(0, decay[i]);
            var s = Math.Clamp(sustain[i], 0, 1);
            var r = Math.Max(0, release[i]);

            if (gateHigh && !_gateWasHigh)
            {
                _stage = Stage.Attack;
                _levelAtStageStart = _level;
                _secondsInStage = 0;
            }
            else if (!gateHigh && _gateWasHigh)
            {
                _stage = Stage.Release;
                _levelAtStageStart = _level;
                _secondsInStage = 0;
            }

            _gateWasHigh = gateHigh;

            // Read BEFORE the switch, which is what gives a zero-length stage its sample for free:
            // a stage that collapses (Attack with a = 0, say) still owns the sample it collapsed
            // in, because the transition below only takes effect from the next one.
            var stageAtTop = _stage;

            // …and the progress with it. The switch below resets _secondsInStage the moment a
            // stage ends, so reading it afterwards reports "0 seconds into the stage" on the very
            // sample the stage is leaving — which came out as the socket jumping back to 1 and a
            // SECOND rising edge on every transition. Measured: two edges on A per note.
            var secondsAtTop = _secondsInStage;

            var stageLength = stageAtTop switch
            {
                Stage.Attack => a,
                Stage.Decay => d,
                Stage.Release => r,
                _ => 0,
            };

            switch (_stage)
            {
                case Stage.Attack when a <= 0:
                    _level = 1;
                    _stage = Stage.Decay;
                    _secondsInStage = 0;
                    break;
                case Stage.Attack:
                    _level = _levelAtStageStart + (1 - _levelAtStageStart) * (_secondsInStage / a);
                    if (_secondsInStage >= a)
                    {
                        _level = 1;
                        _stage = Stage.Decay;
                        _secondsInStage = 0;
                    }
                    break;
                case Stage.Decay when d <= 0:
                    _level = s;
                    _stage = Stage.Sustain;
                    break;
                case Stage.Decay:
                    _level = 1 - (1 - s) * (_secondsInStage / d);
                    if (_secondsInStage >= d)
                    {
                        _level = s;
                        _stage = Stage.Sustain;
                    }
                    break;
                case Stage.Sustain:
                    _level = s;
                    break;
                case Stage.Release when r <= 0:
                    _level = 0;
                    _stage = Stage.Idle;
                    break;
                case Stage.Release:
                    _level = _levelAtStageStart * (1 - _secondsInStage / r);
                    if (_secondsInStage >= r)
                    {
                        _level = 0;
                        _stage = Stage.Idle;
                    }
                    break;
                default:
                    _level = 0;
                    break;
            }

            WriteStage(stages, i, stageAtTop, secondsAtTop, stageLength);
            _secondsInStage += sampleDuration;

            var clampedLevel = (float)Math.Clamp(_level, 0, 1);
            envCurve[i] = clampedLevel;
            output[i] = input[i] * clampedLevel;
        }
    }

    /// <summary>
    /// Puts one sample into the socket of whichever stage is running.
    ///
    /// <para>Descending, 1 at the start of the stage down to 0 at its end, so one wire carries both
    /// halves of what a stage output is for: the jump from 0 to 1 is a rising edge anything that
    /// takes a trigger will catch, and the slope after it says how far through the stage we are.
    /// <see cref="Stage.Sustain"/> is the exception and holds at 1 — with a gate wired its length
    /// is not known even in principle (it lasts as long as the key is down), and a socket that
    /// ramped in one patch and held in another would be worse than one that always holds.</para>
    ///
    /// <para>A stage of zero length gets a full sample at 1 rather than nothing. That is the whole
    /// point of the guarantee: <c>Release</c> at 0 s, or an <c>Attack</c> the patch never uses, is
    /// still a moment worth triggering from, and without this it would be unreachable.</para>
    ///
    /// <para>The sample a stage is ENTERED on is a full 1 too, rather than the first step of its
    /// ramp. Measured before that rule existed: <c>Decay</c> opened at 0,999792 with the gate wired
    /// and <c>Release</c> at 0,999931 without it — one sample of slope, because the clock the ramp
    /// reads has already been advanced by the sample the previous stage ended on (gated) or the
    /// stage boundary falls a hair off a sample (ungated). Close enough to catch as a trigger, but
    /// a socket documented as running 1 → 0 should start at 1, and what reads it may compare
    /// against exactly that. The LEVEL arithmetic is deliberately untouched by this — moving that
    /// would retune every patch ever saved; these sockets feed nothing but themselves.</para>
    /// </summary>
    private void WriteStage(float[][] stages, int sample, Stage stage, double secondsInStage, double stageLength)
    {
        var index = stage switch
        {
            Stage.Attack => 0,
            Stage.Decay => 1,
            Stage.Sustain => 2,
            Stage.Release => 3,
            _ => -1,
        };

        var entering = index != _previousStageIndex;
        _previousStageIndex = index;

        if (index < 0)
            return;

        stages[index][sample] = entering || index == 2 || stageLength <= 0
            ? 1
            : (float)Math.Clamp(1 - secondsInStage / stageLength, 0, 1);
    }
}
