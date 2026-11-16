using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Bends a control signal between "even in value" and "even in what you hear", in either
/// direction. The socket you wire says which signal you have; the socket you take says which you
/// want.
///
/// <para><b>Why it has to exist at all.</b> Modulation is added in the destination's own unit
/// (<c>base + mod × amount</c>, see <see cref="ModuleBase.GetModulatedValues"/>), and for a
/// <c>Hz</c> knob that unit is not the one the ear works in. Measured on a plain linear Attack of
/// 0,55 s driving Frequency at base 110 / depth 2400: the pitch climbed <b>+17,94 semitones in the
/// first step and +1,44 in the last</b>, so the first quarter of the time covered 59,6 % of the
/// range and the last quarter 8,7 % — heard as a shot upwards followed by crawling. The same patch
/// hand-drawn the old way (<c>Sestavy/laser_vystrel.zvuky</c>) hides an exact exponential in a
/// curve: +5,15 semitones per step, twelve times over.</para>
///
/// <para><b>Why a knob and not a drawn curve.</b> The shape that makes the steps even depends on
/// ONE number and nothing else — how far the destination sweeps. Verified to eight decimals:
/// <c>base·(2^(K·x) − 1)/depth</c> is identically <c>(2^(K·x) − 1)/(2^K − 1)</c>, so base and depth
/// cancel and only <c>K</c> survives.</para>
///
/// <para><b>The case for this is reproducibility, not accuracy.</b> A single drawn Bézier segment
/// can match it: searched and then refined, handles at <c>Out(0,487; 0,070)</c> and
/// <c>In(−0,223; −0,730)</c> reach a step ratio of <b>1,0045</b>, as good as this module's 1,004.
/// The trouble is that those four numbers come out of a search — no one finds them by eye, the
/// editor shows no readout that would say whether you landed on 1,004 or on 3,0, and a different
/// sweep needs them looked for again. This module is set from one number read straight off the
/// destination and is exact by construction, which is a different virtue from being closer.</para>
///
/// <para><b>What the names mean, because the shape is the other way round.</b> <c>Lin</c> is even
/// in VALUE — a plain ramp, equal steps of the number. <c>Log</c> is even in PERCEPTION — equal
/// steps sound like equal intervals. So the <c>Log</c> output is, as a curve, an EXPONENTIAL: the
/// name says what the signal is for, not what it is shaped like. Send an envelope in through
/// <c>Lin</c>, take <c>Log</c> out, and a Hz knob finally rises evenly.</para>
///
/// <para><b>Where it still ends, and it is the reason this is second best.</b> <see cref="RatioName"/>
/// has to be copied from the destination, and nothing keeps the two in step: move the oscillator's
/// base from 110 to 200 and this knob is quietly wrong, exactly as a hand-fitted curve would be
/// (measured on the same curve at base 200: the step ratio goes from 1,00 to 1,97). The fix that
/// cannot go stale is a modulation depth expressed in semitones ON the destination's own knob,
/// which already knows both numbers — cross-cutting limitation 3 in todo.md. This module is the
/// box you can have today, not the end of the story.</para>
/// </summary>
public sealed class LinLogModule : ModuleBase
{
    public const string RatioName = "Poměr";

    /// <summary>Below this the sweep is too small to bend and the maths goes to 0/0 — at a ratio of
    /// exactly 1 the span is zero octaves. Both directions fall back to passing the signal through,
    /// which is also what the bottom of the knob should mean.</summary>
    private const double FlatRatio = 1.0001;

    private readonly Port _linInput;
    private readonly Port _logInput;
    private readonly Port _linOutput;
    private readonly Port _logOutput;
    private readonly ModuleParameter _ratio;
    private readonly ModuleParameter _gain;

    public LinLogModule() : base("Převodník")
    {
        _linInput = AddInput("Lin");
        _logInput = AddInput("Log");
        _linOutput = AddOutput("Lin");
        _logOutput = AddOutput("Log");

        // Read straight off the destination: (base + depth) ÷ base, which for the laser's
        // 110 + 2400 is 22,8. Deliberately NOT octaves, though the two say the same thing — a
        // ratio is the division you can do in your head from two numbers already on the knob in
        // front of you, and a logarithm is not. Default 16, i.e. four octaves: a sweep wide enough
        // to hear the difference the moment the box is dropped in.
        _ratio = AddParameter(RatioName, 16, 1, 1024, "×");

        // Applied LAST, to whichever output is taken, so "before or after the conversion" has one
        // answer instead of depending on which socket was used. That ambiguity is the reason this
        // is its own module rather than two more sockets on Gain — the same call WaveshaperModule
        // makes about not being another Type on Distortion.
        _gain = AddParameter("Zisk", 1, 0, 2);
    }

    public override string Description =>
        "Ohne řídicí signál mezi „rovnoměrně v hodnotě“ (Lin) a „rovnoměrně v tom, co slyšíš“ " +
        "(Log). Kam zapojíš, říká, jaký signál máš; odkud bereš, říká, jaký chceš. Na výšku: " +
        "obálku pusť do Lin, ber z Log a teprve to veď do Frequency — lineární obálka je totiž " +
        "lineární v HERTZÍCH, takže sama vystřelí nahoru a pak se plazí. Poměr nastav na " +
        "(základ + hloubka) ÷ základ toho knoflíku, do kterého to teče: u 110 + 2400 je to 22,8. " +
        "Poměr 1 znamená průchod beze změny. Zisk se uplatní až na konci, na obou výstupech " +
        "stejně. Záporné hodnoty se ohýbají symetricky, takže to funguje i na obousměrné LFO " +
        "(vibrato pak drží stejný interval nahoře i dole); mimo −1…1 se drží krajní hodnota.";

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        // Asking the dictionary rather than GetInputOrSilence: an unwired socket and a wired-but-
        // silent one have to mean different things here, the way SampleHoldModule needs it. A Log
        // input left unwired must not be read as "a log-domain zero", or every patch that uses only
        // Lin would pay for a conversion of nothing.
        var hasLin = inputBuffers.TryGetValue(_linInput, out var lin);
        var hasLog = inputBuffers.TryGetValue(_logInput, out var log);

        var ratio = GetModulatedValues(_ratio, ctx, inputBuffers);
        var gain = GetModulatedValues(_gain, ctx, inputBuffers);

        var linOut = NewBuffer(ctx);
        var logOut = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var r = Math.Max(1, (double)ratio[i]);
            var span = Math.Log2(r);
            var flat = r < FlatRatio;

            // Everything is carried in the Lin domain inside, so the two outputs cannot disagree
            // about what is being converted, and a patch that wires both inputs gets their sum
            // rather than one of them silently winning.
            var value = 0.0;
            if (hasLin)
                value += lin![i];
            if (hasLog)
                value += flat ? log![i] : Compress(log![i], span);

            var g = gain[i];
            linOut[i] = (float)(value * g);
            logOut[i] = (float)((flat ? value : Expand(value, span)) * g);
        }

        return new Dictionary<Port, float[]>
        {
            [_linOutput] = linOut,
            [_logOutput] = logOut,
        };
    }

    /// <summary>Even-in-value to even-in-perception: <c>(2^(K·x) − 1)/(2^K − 1)</c>, which maps
    /// 0 to 0 and 1 to 1 whatever <paramref name="span"/> is. Odd-symmetric, so a bipolar LFO bends
    /// the same amount either way and vibrato keeps its interval above and below the note; past
    /// ±1 the end value is held rather than extrapolated, as <c>Curve.Evaluate</c> does.</summary>
    private static double Expand(double x, double span)
    {
        var sign = Math.Sign(x);
        var magnitude = Math.Min(Math.Abs(x), 1);
        return sign * (Math.Pow(2, span * magnitude) - 1) / (Math.Pow(2, span) - 1);
    }

    /// <summary>The inverse of <see cref="Expand"/>: <c>log2(1 + y·(2^K − 1))/K</c>.</summary>
    private static double Compress(double y, double span)
    {
        var sign = Math.Sign(y);
        var magnitude = Math.Min(Math.Abs(y), 1);
        return sign * Math.Log2(1 + magnitude * (Math.Pow(2, span) - 1)) / span;
    }
}
