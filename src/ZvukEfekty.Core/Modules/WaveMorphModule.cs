using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// An oscillator whose waveform is a crossfade between two drawn shapes, with a third drawn shape
/// deciding how the crossfade runs. Sine at one end, square at the other, and everything between.
///
/// <para><b>Why this is a source and not a shape.</b> The obvious design — a module with three
/// curve inputs and one curve OUTPUT, patched into <see cref="OscillatorModule"/>'s "Vlastní vlna"
/// — runs into <see cref="PortKind"/>: a curve port carries a SHAPE that
/// <see cref="ModuleGraph.RenderModule"/> resolves once, before the block, not a signal sampled
/// while the block runs. A morph that moves would have to hand out a new <see cref="Curve"/> every
/// block, which means building a 4096-entry lookup table inside the render, on the audio thread —
/// the very thing the note on lazy tables in <see cref="Curve"/> was written to stop.</para>
///
/// <para>Measured, so the argument is not made on a hunch. Building a 97-point curve costs 35,8 µs,
/// which is 0,36 % of a 10 ms block per voice — real but survivable. The 16 kB of table per block
/// per voice is worse: 25 MB/s of garbage at sixteen voices, on the thread that must not collect.
/// What actually decides it is that the morph would then step at block rate, and the error scales
/// straight with how fast the sweep runs — against a per-sample morph at 220 Hz in 10 ms blocks it
/// sits 64 dB down over a 5 s sweep, 50 dB down over 1 s, 30 dB down over 100 ms and 10 dB down
/// over 10 ms. A slow sweep would have been fine; a per-note one, which is the interesting case,
/// would have been ruined. So the crossfade happens where it can be done per sample: here, in the
/// oscillator itself.</para>
///
/// <para><b>Both waves are read at the same phase.</b> That is what makes the shapes in between
/// sound like one wave changing rather than two waves playing at once. The built-in
/// <see cref="Curve.Square"/> is deliberately aligned to <see cref="Curve.Sine"/> — same sign over
/// the first half of the cycle — so the default sine-to-square sweep keeps its zero crossings and
/// only grows harmonics.</para>
///
/// <para><b>Two ways to be between two shapes, and they do not sound alike.</b> The "Morf" knob
/// picks which. <b>Vzorky</b> adds the two curves’ sampled values — the original, and the obvious
/// reading of "crossfade". <b>Vektory</b> slides the BREAKPOINTS instead, through
/// <see cref="Curve.Morph"/>: each point and tangent travels from where one shape has it to where
/// the other does, so the wave is a genuine shape partway between them rather than a sum of two.</para>
///
/// <para>The difference is not subtle, and it is what the sample-level mode gets wrong on any shape
/// with an edge in it. A blend of samples carries a fixed residual, <c>B − A</c>, at rising level,
/// so a step in either shape is a step in the result at every position above zero — the default
/// sine-to-square already has a 0,0215 discontinuity at 1 % of the way across. What that sounds like
/// is a buzz fading in on top of a sine, arriving abruptly, and it is why the sweep’s brightness
/// front-loads: spectral centroid 1,02 at a tenth of the way and 1,35 at halfway, against 1,00 and
/// 1,13 for the vector morph, with the two meeting again at 1,97 at the end. Sliding the points
/// keeps the largest step in the shape at 0,002 — no edge at all — until nine tenths of the way,
/// because the square’s edge is two points converging in x and is only vertical once they meet.
/// The cost is that the edge then forms fast, over the last stretch of the sweep; drawing the
/// transition curve to approach 1 slowly spreads it back out.</para>
///
/// <para>Vectors are the default, and the sample mode is kept rather than removed: it is the plainer
/// operation, it is what every patch built before 2026-09-05 sounded like, and on two shapes that
/// have no edges the two modes differ far less than the knob suggests.</para>
///
/// <para><b>What vectors cost at render time.</b> A morphed curve takes about 121 µs to build,
/// which is twelve times a whole 10 ms block, so it can be built neither per sample nor per block.
/// <see cref="CurveMorphLadder"/> builds sixty-five of them up front, off the audio thread, keyed by
/// the breakpoints so all sixteen voices share one row; the render then reads two of those tables
/// and interpolates, exactly what the sample-level crossfade already cost. The stepping that leaves
/// behind measures 49 dB down at the sharp end of the sweep and 63 dB or better across the rest —
/// and, unlike resolving the shape once per block, it does not get worse as the sweep speeds
/// up.</para>
///
/// <para><b>The sample-level crossfade is linear, not equal-power.</b> Equal-power is right for two UNCORRELATED
/// signals, whose sum would otherwise dip in the middle; here the two waves are phase-locked and
/// both fundamental-dominant, so they add rather than partly cancel, and 0,707 of each would push
/// the middle of the sweep to about 1,4 — clipping the one place the module is meant to be
/// interesting. Linear keeps the peak at or below the louder of the two ends — measured over the
/// default sine-to-square sweep, the peak stays at exactly 1,0000 the whole way across.</para>
///
/// <para>The vector morph needs no such argument — it never sums two waves — and measures the same
/// way regardless: over sine→square, triangle→pulse, saw→sine, square→saw and a 17-point
/// hand-drawn shape→square, every intermediate peaks at or below the louder of its two ends.</para>
///
/// <para>The peak holding still does not mean the LEVEL does: a square at the same peak carries
/// more energy than a sine, so the default sweep gets 3,0 dB louder from end to end (RMS 0,7073 to
/// 1,000, passing 0,764 / 0,833 / 0,913 at a quarter, half and three quarters). That is the shapes
/// being what they are, not the crossfade; if it matters, ride Amplitude with the same signal that
/// drives Pozice.</para>
///
/// <para><b>Aliasing:</b> the shapes are read straight out of their lookup tables with no
/// band-limiting, exactly as <see cref="Waveform"/>'s Custom does. Morphing towards a square at a
/// high pitch aliases; that is not a regression (the built-in Saw and Square alias too), but this
/// module makes it easy to walk into, because the sweep ends on the harshest shape.</para>
/// </summary>
public sealed class WaveMorphModule : ModuleBase
{
    public const string PositionName = "Pozice";
    public const string LengthName = "Délka";

    private static readonly string[] LoopNames = { "Ne", "Ano" };

    /// <summary>How the two shapes are blended — see the type's remarks. Vectors first because it
    /// is the one that sounds like a wave changing.</summary>
    private static readonly string[] MorphNames = { "Vektory", "Vzorky" };

    /// <summary>Breakpoints the node preview builds its blended shape from. Straight segments
    /// between 96 samples of the crossfade — the preview is 152 px wide, so a segment is under two
    /// pixels and the drawing is limited by the box, not by the count.</summary>
    private const int PreviewPoints = 96;

    /// <summary>How often the crossfade position is published for the node preview, in samples.
    /// 1024 at 44,1 kHz is 43 times a second — ahead of the preview's 30 Hz poll, so the picture is
    /// never waiting on the render. The same interval ScopeModule publishes its window on, and for
    /// the same reason it counts samples rather than blocks: a batch render arrives as ONE huge
    /// block, so publishing per block would record a single frame and the preview would jump
    /// straight to the end instead of moving through the sweep.</summary>
    private const int PublishInterval = 1024;

    private readonly Port _output;
    private readonly ModuleParameter _frequency;
    private readonly ModuleParameter _amplitude;
    private readonly ModuleParameter _position;
    private readonly ModuleParameter _length;
    private readonly ModuleParameter _loop;
    private readonly ModuleParameter _morph;
    private readonly CurveInput _waveA;
    private readonly CurveInput _waveB;
    private readonly CurveInput _transition;

    // The pre-built row of morphed shapes, and the two shapes it was built from. Built in Prepare
    // and read in Process; null whenever the crossfade is being done at sample level, or whenever
    // Prepare has not run for the shapes now in the sockets.
    private CurveMorphLadder? _ladder;
    private Curve? _ladderFrom;
    private Curve? _ladderTo;

    private double _phase;

    // How far the transition has run by itself, carried across blocks for the same reason
    // CurveModule carries its own position: a live voice renders one signal as a stream of short
    // blocks rather than in one pass.
    private double _run;

    // Samples since the last ResetState, to timestamp published positions while
    // VisualizationHub.IsRecording — same bookkeeping as ScopeModule's.
    private long _totalSamplesRendered;

    public WaveMorphModule() : base("Morfující oscilátor")
    {
        _output = AddOutput("Out");

        _frequency = AddParameter("Frequency", 440, 20, 5000, "Hz");
        _amplitude = AddParameter("Amplitude", 0.8, 0, 1);
        _position = AddParameter(PositionName, 0, 0, 1);
        _length = AddParameter(LengthName, 1.0, 0, 60, "s", tempoSync: TempoSyncKind.Seconds);
        _loop = AddParameter("Opakovat", 0, 0, LoopNames.Length - 1, options: LoopNames);
        _morph = AddParameter("Morf", 0, 0, MorphNames.Length - 1, options: MorphNames);

        // Cycles, like any waveform: read round and round, so the ends have to meet or every period
        // steps and buzzes on the fundamental. The two fallbacks are the ends of the sweep this
        // module is named for, so an unwired instance already morphs sine to square — a curve input
        // is an override here, never a requirement.
        _waveA = AddCurveInput("Vlna A", Curve.Sine(), CurveOutput.Cycle);
        _waveB = AddCurveInput("Vlna B", Curve.Square(), CurveOutput.Cycle);

        // Unipolar, because this one is a MIX, not a wave: 0 is wave A, 1 is wave B, and there is
        // no reading of "-0,4 of the way from A to B". X is how far the transition has got, so the
        // built-in straight line is the plain even sweep — and drawing it is what buys the rest:
        // ease in and out, a hold in the middle, a bounce back, a staircase.
        _transition = AddCurveInput("Přechod", Curve.Line(0, 1), CurveOutput.Unipolar);
    }

    public override string Description =>
        "Oscilátor, jehož vlna je plynulý přechod mezi dvěma nakreslenými tvary. Vlna A a Vlna B " +
        "jsou ty dva konce (nezapojené: sinus a obdélník), Přechod říká, jak se mezi nimi jede " +
        "(osa X = průběh, osa Y = 0 je A, 1 je B). Kde v přechodu zrovna jsme, určuje Pozice plus " +
        "postup za Délka sekund; Délka 0 přechod zastaví, takže pozicí pak hýbe jen modulace " +
        "Pozice. Obě vlny se čtou na stejné fázi, proto to zní jako jedna měnící se vlna, ne jako " +
        "dva tóny přes sebe. Morf říká, JAK se ty dva tvary míchají: Vektory stěhují zlomové " +
        "body a táhla jednoho tvaru na druhý, takže hrana vzniká až ke konci přejezdu a narůstá " +
        "postupně; Vzorky sčítají obě křivky vzorek po vzorku, takže hrana je výsledku hned od " +
        "začátku, jen tišší — to je ten ostrší, skokovější nástup.";

    /// <summary>The two waves and the transition, for the node preview — it has to repaint when any
    /// of the three sockets gets a different shape.</summary>
    public IReadOnlyList<CurveInput> ShapeSockets => new[] { _waveA, _waveB, _transition };

    /// <summary>The knobs the previewed shape depends on. <see cref="LengthName"/> is not among
    /// them: it says how fast the sweep runs, which a still picture cannot show.</summary>
    public IReadOnlyList<ModuleParameter> ShapeParameters => new[] { _position, _morph };

    /// <summary>
    /// Builds the row of morphed shapes the vector morph reads, or picks it up from the shared cache
    /// when another voice already built it. Off the audio thread — see
    /// <see cref="ModuleBase.Prepare"/> — because a miss is sixty-five curve builds, most of a
    /// hundredth of a second.
    ///
    /// <para>Only correct because the graph resolves curve inputs BEFORE calling this: until it
    /// does, both sockets still report their fallbacks and the row would be built for the wrong pair
    /// of shapes. <see cref="Audio.Voice"/> and the batch render both call
    /// <see cref="ModuleGraph.ResolveCurveInputs"/> first for exactly that reason. Should anything
    /// ever fail to, <see cref="Process"/> spots the mismatch and falls back to the sample-level
    /// crossfade rather than playing a wave nobody asked for.</para>
    /// </summary>
    public override void Prepare()
    {
        if ((int)_morph.Value != 0)
        {
            // The sample-level crossfade reads no row, and a megabyte of tables for a mode that
            // will not touch them is worth not building.
            _ladder = null;
            _ladderFrom = null;
            _ladderTo = null;
            return;
        }

        var from = _waveA.Curve;
        var to = _waveB.Curve;
        if (_ladder is not null && ReferenceEquals(from, _ladderFrom) && ReferenceEquals(to, _ladderTo))
            return;

        _ladderFrom = from;
        _ladderTo = to;
        _ladder = CurveMorphLadder.For(from, to);
    }

    public override void ResetState()
    {
        _phase = 0;
        _run = 0;
        _totalSamplesRendered = 0;
    }

    /// <summary>
    /// The wave this module would play at the position its <see cref="PositionName"/> knob is on,
    /// for the preview on its node. Not used to render audio — <see cref="Process"/> crossfades the
    /// same two tables per sample, and duplicating that here would be a second place to get it
    /// wrong.
    ///
    /// <para>It shows the knob, not the render: the sweep moves while a note sounds, and a still
    /// picture that tried to follow it would only ever show where the last block happened to
    /// stop.</para>
    /// </summary>
    public Curve ShapeAsCurve() => ShapeForMix(_transition.Curve.Sample(Math.Clamp(_position.Value, 0, 1)));

    /// <summary>True while the morph runs through the breakpoints rather than through the samples —
    /// the "Morf" knob, read in the one place that decides it.</summary>
    private bool MorphsVectors => (int)_morph.Value == 0;

    /// <summary>
    /// The wave this module plays at a given crossfade amount — 0 is <c>Vlna A</c>, 1 is
    /// <c>Vlna B</c>. This is the one the preview draws while the patch is actually sounding, from
    /// the position published to <see cref="VisualizationHub"/> by whichever voices are running.
    ///
    /// <para>Takes the MIX, not the position along the transition curve, which is also what gets
    /// published: publishing the position would not survive being averaged across voices, because
    /// the transition curve is free to bend. Averaging MIXES is exact for the sample-level
    /// crossfade, which is linear in the mix, and an approximation for the vector morph, which is
    /// not; what several voices at different points of the same sweep look like as one picture is a
    /// question with no exact answer anyway.</para>
    ///
    /// <para>The vector morph is drawn from <see cref="Curve.Morph"/> itself rather than from
    /// <see cref="CurveMorphLadder"/>: at the preview's 30 Hz that is 121 µs of a 33 ms frame, so
    /// the picture shows the exact shape instead of the render's stepped approximation of it.</para>
    /// </summary>
    public Curve ShapeForMix(double mix)
    {
        var a = _waveA.Curve;
        var b = _waveB.Curve;

        if (MorphsVectors)
            return Curve.Morph(a, b, mix);

        var shape = new CurvePoint[PreviewPoints + 1];
        for (var i = 0; i <= PreviewPoints; i++)
        {
            var x = i / (double)PreviewPoints;
            var low = a.Sample(x);
            shape[i] = new CurvePoint(x, low + (b.Sample(x) - low) * mix);
        }

        return new Curve(shape);
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var frequency = GetModulatedValues(_frequency, ctx, inputBuffers);
        var amplitude = GetModulatedValues(_amplitude, ctx, inputBuffers);
        var position = GetModulatedValues(_position, ctx, inputBuffers);
        var length = GetModulatedValues(_length, ctx, inputBuffers);
        var loops = (int)_loop.Value == 1;

        // Read once per block, as CurveModule does: a shape is held by reference, so taking it here
        // is both cheap and free of tearing when the editor swaps one mid-render.
        var waveA = _waveA.Curve;
        var waveB = _waveB.Curve;
        var transition = _transition.Curve;

        // The row is only good for the shapes it was built from. It normally is — Prepare runs
        // after the graph has resolved the sockets and before any block — but a row left over from
        // shapes that have since been swapped would be the wrong wave, and a wrong wave is worse
        // than the mode the user did not pick. Building one here is not an option: it is sixty-five
        // curve builds on the audio thread.
        var ladder = MorphsVectors && ReferenceEquals(waveA, _ladderFrom) && ReferenceEquals(waveB, _ladderTo)
            ? _ladder
            : null;

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Where the transition stands: what it has run to on its own, plus wherever the knob —
            // and anything modulating it — puts it. Wrapped when looping, held at the ends when
            // not. Curve.Sample would clamp anyway; doing it here keeps the two cases explicit.
            var driver = _run + position[i];
            driver = loops ? driver - Math.Floor(driver) : Math.Clamp(driver, 0, 1);

            var mix = transition.Sample(driver);

            float wave;
            if (ladder is not null)
            {
                wave = ladder.Sample(mix, _phase);
            }
            else
            {
                var low = waveA.Sample(_phase);
                wave = low + (waveB.Sample(_phase) - low) * mix;
            }

            output[i] = amplitude[i] * wave;

            // Hand the node preview the crossfade this sample was drawn at, so the picture on the
            // canvas moves through the sweep instead of standing on whatever the Pozice knob says.
            // Published from inside the sample loop, not once per block — see PublishInterval.
            _totalSamplesRendered++;
            if (_totalSamplesRendered % PublishInterval == 0)
                VisualizationHub.Contribute(Id, new[] { mix },
                    (double)_totalSamplesRendered / ctx.SampleRate, average: true);

            _phase += frequency[i] / ctx.SampleRate;
            _phase -= Math.Floor(_phase);

            // Length 0 means "does not run by itself", which is what leaves Pozice — and whatever
            // is patched into it — as the only thing moving the transition. A negative length out
            // of modulation is the same thing rather than a sweep in reverse: reverse is what
            // drawing the transition curve the other way round is for.
            var seconds = length[i];
            if (seconds <= 0)
                continue;

            _run += 1.0 / (Math.Max(seconds, 0.001) * ctx.SampleRate);
            if (_run >= 1.0)
                // Held at the end rather than wrapped when not looping, so the wave settles on
                // shape B instead of snapping back to A — the same rule CurveModule uses.
                _run = loops ? _run - Math.Floor(_run) : 1.0;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
