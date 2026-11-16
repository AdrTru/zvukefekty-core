namespace ZvukEfekty.Core.Graph;

/// <summary>
/// One side of a breakpoint's tangent: how far, and in which direction, the curve leans away from
/// that point before it starts heading for its neighbour. <see cref="Dx"/>/<see cref="Dy"/> are an
/// offset in curve coordinates from the point that owns the handle, so the pair carries direction
/// and strength at once — which is exactly what a dragged handle is.
///
/// <para>An out-handle governs the segment AFTER its point and an in-handle the segment before it,
/// so an out-handle's <see cref="Dx"/> is never negative and an in-handle's never positive.</para>
///
/// <para><b>Zero means "follow the chord".</b> An untouched handle behaves as if it sat a third of
/// the way along the straight line to its neighbour, which draws exactly that straight line — every
/// control point lying on the chord does. So an untouched curve is straight and bit-for-bit what it
/// always was, the patch file records nothing for it, and the editor can draw the handle where it
/// genuinely is instead of hiding it inside the point.</para>
/// </summary>
public readonly record struct CurveHandle(double Dx, double Dy)
{
    /// <summary>The untouched handle — see the type's remarks.</summary>
    public static readonly CurveHandle Auto = default;

    public bool IsAuto => Dx == 0 && Dy == 0;
}

/// <summary>
/// One breakpoint of a <see cref="Curve"/>, and the two tangents meeting at it.
/// </summary>
/// <param name="X">Position along the curve, 0..1. What that means is the module's business:
/// time for an envelope, phase for a waveform, input level for a waveshaper.</param>
/// <param name="Y">Value at this point, -1..1. Uses that only want 0..1 simply stay in the
/// upper half rather than each carrying their own range.</param>
/// <param name="Out">Tangent leaving this point, into the segment that follows. Meaningless on the
/// last point, which has no segment after it, and dropped there.</param>
/// <param name="In">Tangent arriving at this point, out of the segment before it. Meaningless on
/// the first point, and dropped there.</param>
/// <remarks>
/// This replaced a single "tension" number per segment on 2026-09-04. Tension was picked
/// originally because free bezier handles can be dragged into a curve that doubles back, giving one
/// X two Y values and making evaluation ambiguous — a real problem, just not one that costs the
/// handles. <see cref="Curve"/> keeps the guarantee instead: each handle's <see cref="Dx"/> is held
/// inside its own segment, and the two control points of a segment are kept in x order, which
/// together make x(t) non-decreasing and so the curve single-valued. What tension could not do, and
/// this can, is bend the two ENDS of a segment differently — a fast attack into a slow settle is
/// one segment here and was two before.
/// </remarks>
public readonly record struct CurvePoint(double X, double Y, CurveHandle Out = default, CurveHandle In = default)
{
    public CurvePoint Clamped() => new(Math.Clamp(X, 0, 1), Math.Clamp(Y, -1, 1), Out, In);
}

/// <summary>
/// What the module downstream of a curve needs its output to be — the range of values that mean
/// anything there, and whether its two ends have to meet.
///
/// The three uses a <see cref="Curve"/> serves disagree about exactly this. A transfer function
/// and a waveform swing either side of zero; an envelope's level does not, and a negative one is
/// not a quiet envelope but a phase-inverted signal. A waveform's cycle repeats, so its ends must
/// line up or the wave steps discontinuously once per period and buzzes at the fundamental; a
/// transfer function has no next period and no such constraint.
///
/// Declared by the module that owns the curve and enforced on every assignment, which is what
/// makes it a guard rather than a convention: a curve typed by hand into a .zvuky file goes
/// through the same check as one dragged in the editor, so an envelope cannot arrive negative and
/// a waveform cannot arrive with a step in it.
/// </summary>
/// <param name="Min">Lowest meaningful output value.</param>
/// <param name="Max">Highest meaningful output value.</param>
/// <param name="TieEndpoints">Whether y(0) and y(1) must match by default.</param>
public sealed record CurveOutput(double Min, double Max, bool TieEndpoints)
{
    /// <summary>Swings both ways and does not repeat — a waveshaper's transfer function.</summary>
    public static readonly CurveOutput Bipolar = new(-1, 1, false);

    /// <summary>Swings both ways and repeats — one cycle of an oscillator or LFO waveform.</summary>
    public static readonly CurveOutput Cycle = new(-1, 1, true);

    /// <summary>Never goes below zero — an envelope's level.</summary>
    public static readonly CurveOutput Unipolar = new(0, 1, false);

    /// <summary>
    /// Returns <paramref name="curve"/> conformed to this output: every point pulled inside the
    /// range, every tangent shortened until it too stays inside it, and the last point's value
    /// pulled onto the first when the ends are tied. Gives back the same instance when nothing had
    /// to change, so a conforming curve costs nothing and the editor does not see its own object
    /// replaced on every mouse move.
    ///
    /// <para>The tangents have to be conformed as well as the points, and that is new with handles:
    /// tension could only ever bend a segment BETWEEN its two ends, so clamping the ends was enough
    /// to bound the whole curve. A handle can throw the curve past its own points — which is half
    /// the reason to want one — so an envelope drawn with an overshoot would have dipped below zero
    /// between two perfectly legal points. A Bézier stays inside the convex hull of its four control
    /// points, so pulling the control points into range is exact, not a safety margin.</para>
    /// </summary>
    public Curve Conform(Curve curve, bool tieEndpoints)
    {
        var points = curve.Points;

        var needsWork = points.Any(p => p.Y < Min || p.Y > Max || Overshoots(p))
            || (tieEndpoints && points.Count > 1 && points[0].Y != points[^1].Y);

        if (!needsWork)
            return curve;

        var conformed = points.Select(p =>
        {
            var y = Math.Clamp(p.Y, Min, Max);
            return p with { Y = y, Out = Reined(p.Out, y), In = Reined(p.In, y) };
        }).ToList();

        if (tieEndpoints && conformed.Count > 1)
        {
            var y = conformed[0].Y;
            conformed[^1] = conformed[^1] with { Y = y, In = Reined(conformed[^1].In, y) };
        }

        return new Curve(conformed);
    }

    private bool Overshoots(CurvePoint p)
    {
        var y = Math.Clamp(p.Y, Min, Max);
        return !Equals(p.Out, Reined(p.Out, y)) || !Equals(p.In, Reined(p.In, y));
    }

    /// <summary>The handle with its tip pulled back inside the range, keeping its direction.</summary>
    private CurveHandle Reined(CurveHandle handle, double anchorY)
    {
        if (handle.IsAuto)
            return handle;

        var tip = anchorY + handle.Dy;
        var clamped = Math.Clamp(tip, Min, Max);
        return tip == clamped ? handle : handle with { Dy = clamped - anchorY };
    }
}

/// <summary>
/// A hand-drawn shape: breakpoints joined by cubic Bézier segments, evaluated as a single-valued
/// function of X over 0..1.
///
/// One type serves three jobs that differ only in what the axes mean — a waveshaper's transfer
/// function, a custom oscillator/LFO waveform, and an envelope. Keeping the meaning out of here is
/// what lets one editor and one persistence format cover all three; the modules map their own axis
/// onto 0..1 and interpret Y in their own range.
///
/// <para><b>Why the curve can never double back.</b> A segment is a cubic Bézier whose two control
/// points are held inside the segment's own x span and kept in x order (see
/// <see cref="ControlsFor"/>). Under exactly those two conditions x'(t) is a sum of three
/// non-negative terms, so x(t) never decreases and every x has one y. This is the guarantee that
/// the old per-segment "tension" number was chosen to get, kept while allowing the handles that
/// tension could not offer.</para>
///
/// Immutable: an edit builds a new curve rather than mutating in place. That matters because a
/// live voice may be reading one on the audio thread at the moment the mouse moves — swapping a
/// reference is atomic where mutating a shared point array is not — and because it lets
/// <see cref="Lookup"/> be built once up front instead of being invalidated and rechecked.
///
/// Each instance carries its own table, so a per-voice clone builds its own. That is affordable
/// (a straight-line curve tables in single-digit microseconds, a fully bent one in tens) and it
/// happens on the thread that clones the patch, not on the audio thread. Sharing tables between
/// clones by content hash is the remaining optimisation — see todo.md B2, which wants the same
/// treatment for the sampler's decoded audio.
/// </summary>
public sealed class Curve
{
    /// <summary>
    /// Resolution of the lookup table: 4096 points over 0..1, which is 16 kB.
    ///
    /// Measured against exact evaluation: every table ENTRY matches to 3e-8, i.e. float precision,
    /// so nothing is lost in building it. What the table cannot follow is the slope BETWEEN two
    /// entries where the curve is near-vertical — a heavily bent segment right at a breakpoint, or
    /// a deliberate step. There the interpolated value can sit up to ~5% off, but only across a
    /// couple of cells: measured over four representative shapes, the region where the error
    /// exceeds 1% is 0.02% of the x range, and everywhere else the error is below float precision.
    /// Raising the size would narrow that region without removing it, since the limit is the
    /// straight line between entries, not how many there are.
    /// </summary>
    public const int LookupSize = 4096;

    /// <summary>Halvings used to invert x(t). 30 puts t within 1e-9, which is far below what a
    /// table cell or a screen pixel can show; the bisection is used rather than Newton because
    /// x(t) is only guaranteed non-DEcreasing, so a flat stretch would stall a derivative step.</summary>
    private const int SolveSteps = 30;

    private readonly CurvePoint[] _points;
    private readonly float[] _lookup;

    public Curve(IEnumerable<CurvePoint> points)
    {
        var sorted = points.Select(p => p.Clamped()).OrderBy(p => p.X).ToArray();

        _points = sorted.Length switch
        {
            0 => new[] { new CurvePoint(0, 0), new CurvePoint(1, 0) },
            1 => new[] { sorted[0] with { X = 0 }, sorted[0] with { X = 1 } },
            _ => sorted,
        };

        ReinHandles(_points);

        // Built here rather than on first use. A live voice clones its patch on the UI thread
        // precisely so the audio thread never pays for construction (see VoiceManager.NoteOn), and
        // a lazy table would have undone that -- the first Process call after a note-on happens ON
        // the audio thread, so the build would land inside a 10 ms block budget instead of beside
        // it. Paying it at construction puts it back on the thread that can afford it.
        _lookup = BuildLookup();
    }

    public IReadOnlyList<CurvePoint> Points => _points;

    /// <summary>A straight line from (0,<paramref name="from"/>) to (1,<paramref name="to"/>).</summary>
    public static Curve Line(double from, double to)
        => new(new[] { new CurvePoint(0, from), new CurvePoint(1, to) });

    /// <summary>The identity y = x mapped onto -1..1 — a waveshaper that does nothing, which is the
    /// only sensible default for one: a fresh module should be audible and unchanged, not silent.</summary>
    public static Curve Identity() => Line(-1, 1);

    /// <summary>
    /// One cycle of a sine, as breakpoints. The default for a custom WAVEFORM, so that switching an
    /// oscillator to Custom before drawing anything sounds like the Sine it was, rather than going
    /// silent or changing character before the user has touched anything.
    ///
    /// <para>Five points, one per quarter turn, with each handle pointing along the true tangent of
    /// the sine at its own point — steeply up or down at the zero crossings, flat at the peaks.
    /// Handle STRENGTH was swept rather than guessed, and the same length serves both ends of every
    /// segment because a quarter sine is symmetric about its own diagonal; see
    /// <see cref="SineHandle"/> for what the sweep found.</para>
    ///
    /// <para>This used to take thirteen points and a fitted bend each, for 0,82 % error energy
    /// against a true sine. The same shape now costs five points and measures 0,077 % — handles are
    /// what buys that, because a bend could only pull a straight segment towards one side while a
    /// tangent can match the slope the sine actually leaves the point with. Fewer points also
    /// matters directly to the user, who has to drag or right-click away every one of them when
    /// redrawing.</para>
    /// </summary>
    public static Curve Sine()
    {
        // Slope of sin(2*pi*x) at a zero crossing, in curve units: dy/dx = 2*pi.
        const double slope = 2 * Math.PI;
        const double quarter = 0.25;
        var reach = SineHandle * quarter;

        var rising = new CurveHandle(reach, reach * slope);
        var falling = new CurveHandle(reach, -reach * slope);
        var flat = new CurveHandle(reach, 0);

        return new(new[]
        {
            new CurvePoint(0, 0, Out: rising),
            new CurvePoint(0.25, 1, Out: flat, In: Negated(flat)),
            new CurvePoint(0.5, 0, Out: falling, In: Negated(falling)),
            new CurvePoint(0.75, -1, Out: flat, In: Negated(flat)),
            new CurvePoint(1, 0, In: Negated(rising)),
        });
    }

    /// <summary>Handle length for <see cref="Sine"/>, as a fraction of a quarter cycle. Swept over
    /// 0,30–0,42 in steps of 1e-4 against a true sine: the plain "third of the chord" 0,3333 leaves
    /// 1,079 % peak error, 0,4 leaves 2,937 %, and the best is 0,3519 at 0,083 % peak and 0,077 %
    /// error energy. Worth stating because the classic 4/3·tan(θ/4) constant for circular arcs is
    /// NOT it — a sine is not an arc, and using that number here costs an order of magnitude.</summary>
    private const double SineHandle = 0.3519;

    /// <summary>
    /// One cycle of a square. Aligned to <see cref="Sine"/> rather than starting at its own edge:
    /// positive over the first half of the cycle, negative over the second, which is exactly the
    /// sign of that sine. Crossfading the two therefore keeps the zero crossings where they are and
    /// only adds harmonics — an offset square would instead beat against the sine through the
    /// middle of a morph (see <see cref="Modules.WaveMorphModule"/>).
    ///
    /// <para>The rising edge sits one table cell BEFORE x=1, not on it. A waveform's ends are tied
    /// (<see cref="CurveOutput.Cycle"/>), so y(1) is forced onto y(0) = +1; an edge written at x=1
    /// would leave the whole second half as a ramp from -1 up to +1, i.e. a triangle rather than a
    /// square. Giving the falling edge at x=0.5 the same width costs nothing and keeps the two
    /// halves symmetric. One cell is as narrow as <see cref="LookupSize"/> can represent anyway.</para>
    /// </summary>
    public static Curve Square() => Pulse(0.5);

    /// <summary>
    /// One cycle of a rectangle that spends <paramref name="duty"/> of its time high. 0.5 is
    /// <see cref="Square"/>; narrow values are the buzzier, harmonically richer spike that
    /// <see cref="Modules.Waveform"/> calls Pulse.
    /// </summary>
    public static Curve Pulse(double duty)
    {
        const double edge = 1.0 / (LookupSize - 1);
        var high = Math.Clamp(duty, edge * 2, 1 - edge * 2);

        return new(new[]
        {
            new CurvePoint(0, 1),
            new CurvePoint(high, 1),
            new CurvePoint(high + edge, -1),
            new CurvePoint(1 - edge, -1),
            new CurvePoint(1, 1),
        });
    }

    /// <summary>One cycle of a triangle, aligned to <see cref="Sine"/> — same zero crossings, peak
    /// in the same place, so it can be swapped for one without the phase moving.</summary>
    public static Curve Triangle() => new(new[]
    {
        new CurvePoint(0, 0),
        new CurvePoint(0.25, 1),
        new CurvePoint(0.75, -1),
        new CurvePoint(1, 0),
    });

    /// <summary>
    /// One cycle of a sawtooth: a straight ramp with the jump at the wrap point.
    /// <paramref name="rising"/> false gives the falling ramp, which is a different sound in a
    /// waveshaper and a different sweep in an envelope even though it is the same harmonics.
    ///
    /// <para>The ramp runs the full cycle and the reset is the wrap itself, so as a WAVEFORM this
    /// wants "Rozpojit konce": tied ends turn the last cell into the reset and leave the ramp
    /// intact, which is nearly the same shape, but untied is the honest one.</para>
    /// </summary>
    public static Curve Saw(bool rising = true) => Line(rising ? -1 : 1, rising ? 1 : -1);

    public Curve WithPoints(IEnumerable<CurvePoint> points) => new(points);

    /// <summary>
    /// The shape partway from <paramref name="from"/> to <paramref name="to"/>, built by sliding
    /// the BREAKPOINTS — every point and every tangent moves from where one curve has it to where
    /// the other does — instead of by blending the two curves' sampled values.
    ///
    /// <para><b>Why that is a different thing.</b> Blending samples adds a fixed residual shape,
    /// <c>to − from</c>, at rising level: a step anywhere in either curve is a step in the result at
    /// EVERY amount above zero, only quieter. Measured on the built-in sine-to-square, at 1 % the
    /// blended shape already carries a 0,0215 discontinuity and h3/h1 of 0,0038 — the edge is there
    /// from the first moment and merely gets louder, which is heard as a buzz fading in over a sine
    /// rather than as one wave getting brighter. Sliding the breakpoints instead holds the largest
    /// step in the same shape at 0,002 — i.e. none — all the way to 0,9, because the square's edge
    /// is a pair of points converging in x and is only vertical once they meet. Over the same sweep
    /// the spectral centroid goes 1,00 / 1,00 / 1,13 / 1,28 / 1,97 against the blend's
    /// 1,00 / 1,02 / 1,35 / 1,59 / 1,97, and the energy above the fifth harmonic 0,000 / 0,000 /
    /// 0,001 / 0,008 / 0,056 against 0,000 / 0,001 / 0,020 / 0,034 / 0,056.</para>
    ///
    /// <para><b>What it costs.</b> The result is honest about where a hard edge lives: an edge
    /// exists only AT the end that has one, so a sweep starting or ending on a square does most of
    /// its edge-forming in the last few percent. That is the geometry, not a defect — and it is
    /// what the transition curve is for, since drawing a slow approach to 1 spreads it out.</para>
    ///
    /// <para><b>Point counts need not match.</b> The curve with fewer points has its widest segment
    /// split, repeatedly, until the two agree. A Bézier split reproduces the same shape exactly, so
    /// this adds correspondence without moving either end of the morph; points then pair by index.
    /// Measured over sine→square, triangle→pulse, saw→sine, square→saw and a 17-point hand-drawn
    /// shape→square, every intermediate keeps x non-decreasing and peaks at or below the louder
    /// end.</para>
    ///
    /// <para><b>The result cannot double back.</b> Each parent holds its handles inside their own
    /// segments and in x order (see the type's remarks), and both properties survive interpolation,
    /// because a value between two values satisfying an inequality satisfies it too. So a morphed
    /// curve is single-valued for exactly the reason its parents are.</para>
    ///
    /// <para>Costs about 121 µs to build against 35,8 µs for an ordinary curve, because every
    /// segment comes out bent and so takes <see cref="BuildLookup"/>'s slow path. Far too much to
    /// do per sample — <see cref="CurveMorphLadder"/> is what makes it affordable at render
    /// time.</para>
    /// </summary>
    public static Curve Morph(Curve from, Curve to, double amount)
    {
        // Written as a negated comparison so a NaN amount lands on "not yet started" rather than
        // falling through to build a curve full of NaN.
        if (!(amount > 0))
            return from;
        if (amount >= 1)
            return to;

        var count = Math.Max(from._points.Length, to._points.Length);
        var a = Aligned(from, count);
        var b = Aligned(to, count);

        var points = new CurvePoint[count];
        for (var i = 0; i < count; i++)
        {
            var p = a.Points[i];
            var q = b.Points[i];

            // Left untouched only where BOTH sides were straight there. Splitting a straight
            // segment gives two straight ones, so the flag survives the alignment — and it matters:
            // a morph between two straight-segment shapes stays straight, which is both what was
            // drawn and the fast path when the table gets built.
            var outHandle = i < count - 1 && a.Straight[i] && b.Straight[i]
                ? CurveHandle.Auto
                : new CurveHandle(Mix(p.Out.Dx, q.Out.Dx), Mix(p.Out.Dy, q.Out.Dy));
            var inHandle = i > 0 && a.Straight[i - 1] && b.Straight[i - 1]
                ? CurveHandle.Auto
                : new CurveHandle(Mix(p.In.Dx, q.In.Dx), Mix(p.In.Dy, q.In.Dy));

            points[i] = new CurvePoint(Mix(p.X, q.X), Mix(p.Y, q.Y), outHandle, inHandle);
        }

        return new Curve(points);

        double Mix(double u, double v) => u + (v - u) * amount;
    }

    /// <summary>
    /// This curve's points with every handle written out as the offset it is actually DRAWN with —
    /// untouched handles resolved onto the chord, crossed-over pairs collapsed exactly as
    /// <see cref="ControlsFor"/> collapses them — and then split until there are
    /// <paramref name="count"/> of them. Interpolating the drawn control points rather than the
    /// stored ones is what keeps a morph agreeing with the picture at both of its ends.
    ///
    /// <para>The flags say which SEGMENTS were straight, one per segment, so the caller can hand
    /// back the untouched handle instead of a materialised copy of it.</para>
    /// </summary>
    private static (List<CurvePoint> Points, List<bool> Straight) Aligned(Curve curve, int count)
    {
        var source = curve._points;
        var points = new List<CurvePoint>(count);
        var straight = new List<bool>(count);

        for (var i = 0; i < source.Length; i++)
        {
            var point = source[i];
            var outHandle = point.Out;
            var inHandle = point.In;

            if (i < source.Length - 1)
            {
                var (control, _) = curve.ControlsFor(i);
                outHandle = new CurveHandle(control.X - point.X, control.Y - point.Y);
                straight.Add(curve.IsStraight(i));
            }

            if (i > 0)
            {
                var (_, control) = curve.ControlsFor(i - 1);
                inHandle = new CurveHandle(control.X - point.X, control.Y - point.Y);
            }

            points.Add(point with { Out = outHandle, In = inHandle });
        }

        while (points.Count < count)
        {
            var widest = 0;
            var span = -1.0;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var width = points[i + 1].X - points[i].X;
                if (width > span)
                {
                    span = width;
                    widest = i;
                }
            }

            Split(points, straight, widest);
        }

        return (points, straight);
    }

    /// <summary>
    /// Cuts one segment in two at its x midpoint, de Casteljau, leaving the drawn shape exactly as
    /// it was. Used only to give two curves the same number of points to pair up. The x midpoint
    /// rather than t = 0,5 because the correspondence should follow the axis the two curves are
    /// read on, which is the one that has to line up.
    /// </summary>
    private static void Split(List<CurvePoint> points, List<bool> straight, int segment)
    {
        var start = points[segment];
        var end = points[segment + 1];

        var first = (X: start.X + start.Out.Dx, Y: start.Y + start.Out.Dy);
        var second = (X: end.X + end.In.Dx, Y: end.Y + end.In.Dy);
        var t = SolveT(start.X, first.X, second.X, end.X, (start.X + end.X) / 2);

        (double X, double Y) Between((double X, double Y) u, (double X, double Y) v)
            => (u.X + (v.X - u.X) * t, u.Y + (v.Y - u.Y) * t);

        var left = Between((start.X, start.Y), first);
        var middle = Between(first, second);
        var right = Between(second, (end.X, end.Y));
        var before = Between(left, middle);
        var after = Between(middle, right);
        var cut = Between(before, after);

        points[segment] = start with { Out = new CurveHandle(left.X - start.X, left.Y - start.Y) };
        points[segment + 1] = end with { In = new CurveHandle(right.X - end.X, right.Y - end.Y) };
        points.Insert(segment + 1, new CurvePoint(cut.X, cut.Y,
            Out: new CurveHandle(after.X - cut.X, after.Y - cut.Y),
            In: new CurveHandle(before.X - cut.X, before.Y - cut.Y)));

        // Both halves of a straight segment are straight: the cut lands on the chord and its
        // handles come out as the halves' own chord thirds, exactly.
        straight.Insert(segment, straight[segment]);
    }

    /// <summary>
    /// The two Bézier control points the segment starting at <paramref name="segment"/> is actually
    /// drawn with: untouched handles resolved onto the chord, and the pair put in x order. The
    /// editor draws THESE rather than the raw offsets, so the handle you grab is the one the curve
    /// is using — the alternative is a picture that quietly disagrees with the sound.
    /// </summary>
    public ((double X, double Y) Out, (double X, double Y) In) Controls(int segment)
        => ControlsFor(segment);

    /// <summary>
    /// Value at <paramref name="x"/>. Outside 0..1 the curve holds its end values rather than
    /// extrapolating, so a signal overshooting the input range flattens instead of running away.
    /// </summary>
    public double Evaluate(double x)
    {
        if (x <= _points[0].X)
            return _points[0].Y;

        var last = _points[^1];
        if (x >= last.X)
            return last.Y;

        var index = FindSegment(x);
        var start = _points[index];
        var end = _points[index + 1];

        var span = end.X - start.X;
        if (span <= 0)
            return end.Y;

        if (IsStraight(index))
            return start.Y + (end.Y - start.Y) * (x - start.X) / span;

        var (c1, c2) = ControlsFor(index);
        var t = SolveT(start.X, c1.X, c2.X, end.X, x);
        return Cubic(start.Y, c1.Y, c2.Y, end.Y, t);
    }

    /// <summary>
    /// The curve sampled into a table of <see cref="LookupSize"/> points over 0..1, built once at
    /// construction. Reading through the table instead of calling <see cref="Evaluate"/> per sample
    /// turns a binary search plus an inversion into two array reads and a lerp.
    /// </summary>
    public float[] Lookup => _lookup;

    /// <summary>Table value at <paramref name="x"/>, linearly interpolated between entries.</summary>
    public float Sample(double x)
    {
        var table = Lookup;
        var position = Math.Clamp(x, 0, 1) * (table.Length - 1);
        var index = (int)position;
        if (index >= table.Length - 1)
            return table[^1];

        var fraction = (float)(position - index);
        return table[index] + (table[index + 1] - table[index]) * fraction;
    }

    /// <summary>
    /// Fills the table by walking the segments in order instead of calling <see cref="Evaluate"/>
    /// per entry, which would binary-search and then invert the same segment thousands of times
    /// over. A straight segment is filled by plain interpolation, skipping the Bézier entirely —
    /// and straight is the common case, so the default line costs almost nothing to table.
    ///
    /// <para>A curved segment is walked in t rather than solved per entry, which is what keeps the
    /// build affordable: inverting x(t) for all 4096 entries would be thirty cubics each. Four
    /// t-samples per table cell puts the entries between two samples closer together than the
    /// entries themselves, so the straight line drawn between them is below the table's own
    /// resolution.</para>
    /// </summary>
    private float[] BuildLookup()
    {
        var table = new float[LookupSize];
        var last = LookupSize - 1;

        var first = (int)Math.Ceiling(_points[0].X * last);
        for (var i = 0; i < Math.Min(first, LookupSize); i++)
            table[i] = (float)_points[0].Y;

        for (var segment = 0; segment < _points.Length - 1; segment++)
        {
            var start = _points[segment];
            var end = _points[segment + 1];
            var span = end.X - start.X;

            var from = Math.Max(0, (int)Math.Ceiling(start.X * last));
            var to = Math.Min(last, (int)Math.Floor(end.X * last));
            if (to < from)
                continue;

            if (span <= 0)
            {
                for (var i = from; i <= to; i++)
                    table[i] = (float)end.Y;
                continue;
            }

            if (IsStraight(segment))
            {
                var delta = end.Y - start.Y;
                for (var i = from; i <= to; i++)
                    table[i] = (float)(start.Y + delta * ((i / (double)last - start.X) / span));
                continue;
            }

            var (c1, c2) = ControlsFor(segment);
            var steps = Math.Max(8, (to - from + 1) * 4);
            var previousX = start.X;
            var previousY = start.Y;
            var index = from;

            for (var step = 1; step <= steps && index <= to; step++)
            {
                var t = step / (double)steps;
                var x = Cubic(start.X, c1.X, c2.X, end.X, t);
                var y = Cubic(start.Y, c1.Y, c2.Y, end.Y, t);

                while (index <= to && index / (double)last <= x)
                {
                    var gap = x - previousX;
                    var u = gap <= 0 ? 0 : (index / (double)last - previousX) / gap;
                    table[index] = (float)(previousY + (y - previousY) * u);
                    index++;
                }

                previousX = x;
                previousY = y;
            }

            // Rounding can leave the entry that lands exactly on the segment's end unwritten.
            for (; index <= to; index++)
                table[index] = (float)end.Y;
        }

        var afterLast = (int)Math.Floor(_points[^1].X * last);
        for (var i = Math.Max(0, afterLast + 1); i <= last; i++)
            table[i] = (float)_points[^1].Y;

        return table;
    }

    /// <summary>
    /// Holds every handle inside the segment it belongs to, and inside the -1..1 that
    /// <see cref="CurvePoint.Y"/> is bounded by. Done once here rather than at evaluation so the
    /// stored curve — and therefore the patch file and the editor's own drawing — says what the
    /// curve actually does.
    ///
    /// <para>The cost of that choice: dragging a neighbouring point closer shortens a handle that no
    /// longer fits, and dragging the neighbour back does not restore it. The alternative, keeping an
    /// oversized handle on file and quietly clamping it at render time, hides the constraint
    /// somewhere the user cannot see it.</para>
    /// </summary>
    private static void ReinHandles(CurvePoint[] points)
    {
        for (var i = 0; i < points.Length; i++)
        {
            var outHandle = i < points.Length - 1
                ? Rein(points[i].Out, 0, points[i + 1].X - points[i].X, points[i].Y)
                : CurveHandle.Auto;
            var inHandle = i > 0
                ? Rein(points[i].In, points[i - 1].X - points[i].X, 0, points[i].Y)
                : CurveHandle.Auto;

            points[i] = points[i] with { Out = outHandle, In = inHandle };
        }
    }

    private static CurveHandle Rein(CurveHandle handle, double minDx, double maxDx, double anchorY)
    {
        if (handle.IsAuto)
            return handle;

        return new CurveHandle(
            Math.Clamp(handle.Dx, minDx, maxDx),
            Math.Clamp(anchorY + handle.Dy, -1, 1) - anchorY);
    }

    /// <summary>True when both of a segment's handles are untouched, which is the straight line —
    /// checked by hand rather than falling out of the Bézier so the common case skips it entirely.
    /// A handle explicitly dragged onto the chord also draws a straight line; it just pays for the
    /// general path to find that out.</summary>
    private bool IsStraight(int segment) => _points[segment].Out.IsAuto && _points[segment + 1].In.IsAuto;

    private ((double X, double Y) Out, (double X, double Y) In) ControlsFor(int segment)
    {
        var start = _points[segment];
        var end = _points[segment + 1];
        var span = end.X - start.X;
        var rise = end.Y - start.Y;

        var outHandle = start.Out.IsAuto ? new CurveHandle(span / 3, rise / 3) : start.Out;
        var inHandle = end.In.IsAuto ? new CurveHandle(-span / 3, -rise / 3) : end.In;

        var c1x = start.X + outHandle.Dx;
        var c2x = end.X + inHandle.Dx;

        // The two control points crossing over in x is the one way a segment could still double
        // back: x'(t) is a sum of three non-negative terms only while c1x <= c2x. Both are already
        // inside the span (see ReinHandles), so collapsing them onto their midpoint keeps them
        // there and gives the flattest S the two handles can agree on. Left as a live adjustment
        // rather than baked in by ReinHandles, so dragging a handle past its opposite and back
        // returns to where it was instead of being trimmed on the way out.
        if (c1x > c2x)
            c1x = c2x = (c1x + c2x) / 2;

        return ((c1x, start.Y + outHandle.Dy), (c2x, end.Y + inHandle.Dy));
    }

    private static double Cubic(double p0, double p1, double p2, double p3, double t)
    {
        var u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    /// <summary>The t at which the segment's x reaches <paramref name="x"/>. Bisection, which needs
    /// only that x(t) does not decrease — see the type's remarks for why that always holds.</summary>
    private static double SolveT(double x0, double c1, double c2, double x1, double x)
    {
        double low = 0, high = 1;
        for (var i = 0; i < SolveSteps; i++)
        {
            var mid = (low + high) / 2;
            if (Cubic(x0, c1, c2, x1, mid) < x)
                low = mid;
            else
                high = mid;
        }

        return (low + high) / 2;
    }

    private static CurveHandle Negated(CurveHandle handle) => new(-handle.Dx, -handle.Dy);

    private int FindSegment(double x)
    {
        var low = 0;
        var high = _points.Length - 1;

        while (low < high - 1)
        {
            var mid = (low + high) / 2;
            if (_points[mid].X <= x)
                low = mid;
            else
                high = mid;
        }

        return low;
    }
}
