namespace ZvukEfekty.Core.Graph;

/// <summary>A named starting shape offered in the curve editor.</summary>
/// <param name="Name">What the list calls it.</param>
/// <param name="Create">Built on demand rather than held, because applying one hands the module a
/// curve it then owns and edits — a shared instance would be fine (curves are immutable) but a
/// fresh one keeps "the preset" and "what is on screen" from ever looking like the same object.</param>
/// <param name="UntieEndpoints">Whether this shape only makes sense with a wrapping curve's ends
/// broken apart — true for the sawtooth, whose reset IS the wrap. See
/// <see cref="ModuleCurve.EndpointsUntied"/>.</param>
public sealed record CurvePreset(string Name, Func<Curve> Create, bool UntieEndpoints = false);

/// <summary>
/// The shapes the curve editor can drop in ready-made, so a common waveform is one click rather
/// than a dozen dragged points.
///
/// <para>Drawing a sine by hand was the thing that made this necessary: it is thirteen points of
/// fitted bend, or five of fitted tangent, and neither is a thing to do with a mouse. The presets
/// are the same factories the engine itself uses for its built-in shapes (<see cref="Curve.Sine"/>
/// and friends), so what you get from the list is exactly what an Oscillator would have played
/// without the curve — which makes a preset a legible starting point rather than an approximation
/// of one.</para>
///
/// <para>Everything here is drawn across the full -1..1. A curve whose consumer wants less conforms
/// on assignment (see <see cref="CurveOutput.Conform"/>), so applying a sine to an envelope gives
/// its top half rather than an error — honest, if not always what was wanted.</para>
/// </summary>
public static class CurvePresets
{
    public static IReadOnlyList<CurvePreset> All { get; } = new[]
    {
        new CurvePreset("Sinus", Curve.Sine),
        new CurvePreset("Trojúhelník", Curve.Triangle),
        new CurvePreset("Obdélník", Curve.Square),
        new CurvePreset("Pulz (10 %)", () => Curve.Pulse(0.1)),
        new CurvePreset("Pila ↑", () => Curve.Saw(), UntieEndpoints: true),
        new CurvePreset("Pila ↓", () => Curve.Saw(rising: false), UntieEndpoints: true),
        new CurvePreset("Čára", Curve.Identity),
        // Not a waveform at all: the shape an envelope decays with, and the one thing a straight
        // line is measurably wrong for (see EnvelopeModule — its ADSR decay is linear, and the ear
        // expects exponential). One segment, because that is all a handle needs.
        new CurvePreset("Doznívání", () => new Curve(new[]
        {
            new CurvePoint(0, 1, Out: new CurveHandle(0.08, -1.2)),
            new CurvePoint(1, -1),
        })),
    };
}
