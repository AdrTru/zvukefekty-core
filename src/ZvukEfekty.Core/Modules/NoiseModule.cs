using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Noise generator in seven colours — a colour is how the energy is spread across the spectrum,
/// not what it sounds like in one word.
///
/// <para>The colours exist as a parameter rather than as "run white through the Filter" because
/// that substitution does not work: <see cref="FilterModule"/> is a state-variable filter with a
/// -12 dB/oct slope, pink needs -3 dB/oct and brown -6 dB/oct, and neither is reachable from the
/// other. Wind, thunder, rumble and surf all sit on pink or brown, and before this they had to be
/// approximated with a low-passed white that keeps too much fizz on top.</para>
///
/// <para><b>Five of the seven are one slope each</b>, and they are made the cheapest way that
/// gives that slope exactly rather than approximately — integration for the falling ones,
/// differentiation for the rising ones. Measured slope per octave between 500 Hz and 8 kHz
/// (Welch, 20 s): white 0.0, pink -3.0, brown -5.8, blue +2.9, violet +5.9, black -11.6. The two
/// rising ones flatten in the top octave, where differencing runs into Nyquist: blue reads +1.2
/// and violet +4.0 between 8 and 16 kHz. Below 500 Hz the two integrated ones flatten instead,
/// where the leak takes over — brown -3.3 and black -6.6 across the 125 Hz octave.</para>
///
/// <para><b>Grey is the odd one</b>: it has no slope, because its target is not a spectrum but an
/// ear — see <see cref="Grey"/>. Black is the other odd one, in that it is nearly inaudible on
/// purpose — see <see cref="Black"/>.</para>
///
/// <para><b>Adding colours could not move the old ones.</b> The index is what a patch stores, so
/// the four new names go on the end of <see cref="ColourNames"/>; white keeps its seeded sequence
/// untouched because <see cref="ResetState"/> still runs no warm-up for it. Verified by rendering
/// white, pink and brown at two seeds through the old code and the new one: identical byte for
/// byte.</para>
/// </summary>
public sealed class NoiseModule : ModuleBase
{
    /// <summary>New colours go on the END of this list — the index is what a .zvuky file stores.
    /// A patch saved before this parameter existed has no "Barva" record at all, so it keeps the
    /// default index 0 and stays white, which is what it always was.</summary>
    private static readonly string[] ColourNames =
        { "Bílý", "Růžový", "Hnědý", "Modrý", "Fialový", "Šedý", "Černý" };

    private const int WhiteIndex = 0;
    private const int PinkIndex = 1;
    private const int BrownIndex = 2;
    private const int BlueIndex = 3;
    private const int VioletIndex = 4;
    private const int GreyIndex = 5;
    private const int BlackIndex = 6;

    /// <summary>
    /// Both colours are scaled so that a ten-second render at Amplitude 1 peaks at 1.0 — the same
    /// bound white has, so switching colour can never make a chain clip that did not clip before.
    ///
    /// <para>The alternative, matching white's RMS, was measured and rejected: it puts pink at a
    /// peak of 2.60 and brown at 2.71. That is not a tuning failure but the shape of the
    /// distribution — white here is uniform and leans on its bound every few samples, while pink
    /// and brown are sums of many draws and so Gaussian, wandering out to about 4.5 sigma. Holding
    /// the bound therefore costs about 8 dB of RMS (measured: white -4.8 dB, pink -13.1, brown
    /// -13.4), and that gap is real rather than something a constant could remove.</para>
    ///
    /// <para>Because the tail is Gaussian it has no hard ceiling: a render much longer than ten
    /// seconds reaches a little further out. Measured over 30 s and six seeds, pink tops out at
    /// 1.08 and brown at 1.07 rather than at 1.00.</para>
    /// </summary>
    private const double PinkNormalisation = 0.1268;

    private const double BrownNormalisation = 3.704;

    /// <summary>The four colours added later, held to the same rule: a ten-second render at
    /// Amplitude 1 peaks at 1.0. Measured rather than derived — see the class remarks for why the
    /// peak, not the RMS, is what is held.</summary>
    private const double BlueNormalisation = 2.792;

    /// <summary>Violet's is exact rather than measured: it is a difference of two draws from
    /// -1..1, so it cannot leave -2..2, and half of that is the bound every other colour is
    /// scaled to. The one colour here with a hard ceiling instead of a Gaussian tail — the same
    /// property white has, and for the same reason.</summary>
    private const double VioletNormalisation = 0.5;

    private const double GreyNormalisation = 0.1062;
    private const double BlackNormalisation = 5.397;

    /// <summary>Leak of the brown integrator. Without it the sum is a random walk with no bound and
    /// a long render drifts off the scale; with it the walk is pulled back to zero by a pole at
    /// 1/(1+leak), which is a one-pole low-pass cornering at 137 Hz.
    ///
    /// <para>So "brown" here means -6 dB/oct above the corner and flat below it, which is what
    /// keeps it usable rather than a slow wander. Measured slope per octave (Welch, four seeds):
    /// -0.6 -1.9 -3.7 at 62/125/250 Hz through the corner, then -5.1 -5.7 -5.9 -5.9 -5.6 up to
    /// 8 kHz. A smaller leak would carry the slope lower, at the cost of a wilder walk and worse
    /// peaks.</para></summary>
    private const double BrownLeak = 0.02;

    /// <summary>Samples run through the colour filters by <see cref="ResetState"/> before the first
    /// audible one, so a sound does not begin with the filters empty. 5000 samples is about six
    /// time constants of the slowest pink section (0.99886, tau = 877 samples).
    ///
    /// <para>What it is worth, measured as an ensemble average over 200 seeds rather than off one
    /// render — a single 20 ms window of low-frequency noise scatters by ±5 dB, which is enough to
    /// invent an effect that is not there. Cold, the first 10 ms sits 0.83 dB low on pink and
    /// 0.46 dB low on brown, and is level from 20 ms on. Warm, the same window reads +0.39 and
    /// +0.21 dB, i.e. flat inside the measurement's own spread. So this is a small correction, not
    /// a fade-in being removed; it is here because it costs nothing at render time and makes the
    /// first block statistically the same as every other one.</para>
    ///
    /// <para>Counted in samples rather than seconds because ResetState is not handed a sample rate —
    /// which costs nothing here, since the filter coefficients are fixed numbers rather than
    /// something derived from one. It does mean the draws happen per voice on every note-on: about
    /// 0.4 ms for a sixteen-note chord, and only for a coloured noise.</para></summary>
    private const int WarmupSamples = 5000;

    private readonly Port _output;
    private readonly ModuleParameter _colour;
    private readonly ModuleParameter _amplitude;
    private readonly SeededRandomSource _noise;

    // Pink filter state (Paul Kellet's refined method): six one-pole sections summed, each an
    // octave apart, which is how a -3 dB/oct slope gets approximated by whole-numbered poles.
    private double _p0, _p1, _p2, _p3, _p4, _p5, _p6;

    // Brown integrator state.
    private double _brown;

    // Second integrator, for black — see Black.
    private double _black;

    // One-sample memory for the two differentiated colours, blue and violet.
    private double _previousPink, _previousWhite;

    // Grey shaping sections, in the order the cascade runs them.
    private readonly Biquad _greyLowShelf = new(1.013148637, -1.992275485, 0.979485379, -1.992445766, 0.992463735);
    private readonly Biquad _greyPeak = new(0.775119772, -1.029397982, 0.323218349, -1.029397982, 0.098338121);
    private readonly Biquad _greyHighShelf = new(4.895354529, -4.565412232, 0.308606172, -0.068730204, -0.292721328);

    public NoiseModule() : base("Noise")
    {
        _output = AddOutput("Out");
        _colour = AddParameter("Barva", WhiteIndex, 0, ColourNames.Length - 1, options: ColourNames);
        _amplitude = AddParameter("Amplitude", 0.5, 0, 1);
        _noise = AddSeed();
    }

    public override string Description =>
        "Generátor šumu v sedmi barvách; barva říká, jak je energie rozložená po spektru. " +
        "Bílý má všude stejně — sykot, dech, praskání. Růžový ubývá o 3 dB na oktávu, což ucho " +
        "slyší jako vyrovnaný šum — vítr, moře, déšť, vzdálený dav. Hnědý ubývá o 6 dB, takže " +
        "zbude skoro jen spodek — hřmění, rachot, dunění. Modrý naopak o 3 dB přibývá a fialový " +
        "o 6: tenký sykot bez těla, vzduch v místnosti, dither, plíšek činelu. " +
        "Šedý je tvarovaný tak, aby zněl stejně hlasitě ve VŠECH pásmech — ucho slyší nejlíp " +
        "kolem 3 kHz, takže tam má nejmíň a na okrajích víc; je to ten nejméně únavný šum na " +
        "dlouhé plochy a na maskování. Černý ubývá o 12 dB na oktávu, takže ho skoro neuslyšíte: " +
        "81 % energie je pod 137 Hz. Je na rachot pod něčím jiným — a hlavně jako řídicí signál, " +
        "který se sám pomalu plazí, což jinak neumí nic bez LFO a jeho pravidelnosti. " +
        "Barevné šumy jsou při stejné Amplitude tišší než bílý: mají stejný strop, ale bílý se " +
        "o něj opírá pořád, kdežto ty ostatní ho jen občas dosáhnou. Seed určuje konkrétní " +
        "podobu šumu – stejný seed dá při každém vykreslení stejný zvuk, tlačítkem 🎲 " +
        "vylosujete jinou variantu.";

    /// <summary>Restarts the noise from the top of its seeded sequence — this is what makes a batch
    /// render repeatable, since it is called before every one. The colour filters are cleared with
    /// it (they are state carried between samples, so leaving them behind would make the start of
    /// one render depend on the end of the previous one) and then filled again from the fresh
    /// sequence — see <see cref="WarmupSamples"/> for why an empty filter is not good enough.</summary>
    public override void ResetState()
    {
        _noise.Reset();
        _p0 = _p1 = _p2 = _p3 = _p4 = _p5 = _p6 = 0;
        _brown = 0;
        _black = 0;
        _previousPink = _previousWhite = 0;
        _greyLowShelf.Reset();
        _greyPeak.Reset();
        _greyHighShelf.Reset();

        var colour = (int)_colour.Value;

        // White and violet have no filter to fill: white has no memory at all, and violet's is a
        // single sample. Skipping them also leaves white's seeded sequence starting exactly where
        // it always did, which is what keeps every patch saved before the colours existed sounding
        // the same.
        if (colour is WhiteIndex or VioletIndex)
            return;

        var random = _noise.GetGenerator(out _);
        for (var i = 0; i < WarmupSamples; i++)
            Sample(colour, random.NextDouble() * 2 - 1);
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var random = _noise.GetGenerator(out _);
        var amplitude = GetModulatedValues(_amplitude, ctx, inputBuffers);
        var colour = (int)_colour.Value;
        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var white = random.NextDouble() * 2 - 1;
            output[i] = (float)(amplitude[i] * Sample(colour, white));
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    /// <summary>Paul Kellet's refined pink filter — six one-pole sections plus a direct term,
    /// accurate to within about 0.05 dB of a true -3 dB/oct slope from 10 Hz up to Nyquist. Kept
    /// as literal coefficients rather than derived ones because they are a published fit, and
    /// rounding them is what turns the slope wrong.
    ///
    /// <para>The fit is for 44.1 kHz and there is no sample rate in it, so at 48 kHz every corner
    /// sits 8.8% higher than intended. That moves the whole ladder together, so the slope is what
    /// survives it: measured at 48 kHz the level shifts by 0.04 dB (RMS 0.2207 against 0.2217) and
    /// the peak not at all.</para></summary>
    private double Pink(double white)
    {
        _p0 = 0.99886 * _p0 + white * 0.0555179;
        _p1 = 0.99332 * _p1 + white * 0.0750759;
        _p2 = 0.96900 * _p2 + white * 0.1538520;
        _p3 = 0.86650 * _p3 + white * 0.3104856;
        _p4 = 0.55000 * _p4 + white * 0.5329522;
        _p5 = -0.7616 * _p5 - white * 0.0168980;

        var pink = _p0 + _p1 + _p2 + _p3 + _p4 + _p5 + _p6 + white * 0.5362;
        _p6 = white * 0.115926;

        return pink * PinkNormalisation;
    }

    /// <summary>Leaky integration of white noise, which is what -6 dB/oct means in the time
    /// domain. See <see cref="BrownLeak"/> for why it leaks at all.</summary>
    private double Brown(double white) => BrownRaw(white) * BrownNormalisation;

    /// <summary>The integrator itself, unscaled, because black runs a second one on top of it.</summary>
    private double BrownRaw(double white)
    {
        _brown = (_brown + BrownLeak * white) / (1 + BrownLeak);
        return _brown;
    }

    /// <summary>
    /// One value of whichever colour is selected. The single place a colour is computed, so the
    /// warm-up in <see cref="ResetState"/> cannot run a different filter from the render.
    /// </summary>
    private double Sample(int colour, double white) => colour switch
    {
        PinkIndex => Pink(white),
        BrownIndex => Brown(white),
        BlueIndex => Blue(white),
        VioletIndex => Violet(white),
        GreyIndex => Grey(white),
        BlackIndex => Black(white),
        _ => white,
    };

    /// <summary>
    /// Pink, differentiated: +3 dB/oct instead of -3.
    ///
    /// <para>Differentiating — a sample minus the one before it — has a response of 2·sin(πf/fs),
    /// which rises at +6 dB/oct through most of the band and levels off approaching Nyquist. On
    /// pink's -3 dB/oct that lands on +3, the definition of blue. Doing it this way rather than
    /// with a rising filter means blue costs one subtraction on top of pink.</para>
    ///
    /// <para>What it is for: hiss with no body under it — dither, a cymbal's top end, the air in a
    /// room. Ears hear it as brighter and thinner than white, because white already sounds bright.</para>
    /// </summary>
    private double Blue(double white)
    {
        var pink = Pink(white);
        var blue = pink - _previousPink;
        _previousPink = pink;
        return blue * BlueNormalisation;
    }

    /// <summary>White, differentiated: +6 dB/oct. The steepest rise in the list, and the mirror of
    /// brown. Almost all of it sits in the top octave, so it is the one colour a small speaker
    /// reproduces completely.</summary>
    private double Violet(double white)
    {
        var violet = white - _previousWhite;
        _previousWhite = white;
        return violet * VioletNormalisation;
    }

    /// <summary>
    /// Noise shaped to sound EQUALLY LOUD across the spectrum, rather than to carry equal energy.
    /// The ear is far more sensitive around 3 kHz than at either end, so a spectrum that measures
    /// flat (white) is heard as a hiss with no bottom; grey puts back what the ear takes away.
    ///
    /// <para><b>The target is the inverse of A-weighting</b> (IEC 61672), which is a formula rather
    /// than a table and can therefore be checked at any frequency instead of interpolated. Built on
    /// pink rather than on white because pink already supplies -3 dB/oct of the tilt: fitted
    /// against the target, pink alone is 10.4 dB out and white 17.9 dB, so starting from pink is
    /// most of the work done.</para>
    ///
    /// <para><b>The three sections were fitted, not guessed</b> — a search over shelf and peak
    /// settings, scoring the worst deviation from the target between 40 Hz and 16 kHz after the
    /// best constant offset (a constant is what Amplitude is for, so only the SHAPE counts). The
    /// winner is a low shelf at 63 Hz +26 dB (S 0.8), a peaking cut at 2.5 kHz -6 dB (Q 0.3) and a
    /// high shelf at 6.3 kHz +22 dB (S 0.25), and it holds the whole band to <b>0.52 dB</b>.</para>
    ///
    /// <para>Coefficients are literal and fitted at 44.1 kHz, exactly as <see cref="Pink"/>'s are
    /// and with the same consequence: at 48 kHz every corner sits 8.8% higher. On a shaping this
    /// gentle that is a fraction of a dB.</para>
    /// </summary>
    private double Grey(double white)
        => _greyHighShelf.Process(_greyPeak.Process(_greyLowShelf.Process(Pink(white)))) * GreyNormalisation;

    /// <summary>
    /// Brown integrated a second time: -12 dB/oct above the corner, twice brown's slope.
    ///
    /// <para><b>"Black noise" is not a standardised term</b> — it is used for anything falling
    /// faster than brown, and sometimes for silence with occasional bursts. This one is the literal
    /// continuation of the series: white is 0 dB/oct, pink -3, brown -6, black -12, each one an
    /// integration further.</para>
    ///
    /// <para>⚠️ <b>It is nearly inaudible, and that is the point rather than a fault.</b> Above the
    /// 137 Hz corner it loses 12 dB per octave, so by 4 kHz there is nothing left; what comes out
    /// is a slow wander that mostly moves the cone. Two things it is genuinely good for: a rumble
    /// laid under something else, and — wired into a parameter rather than into the output — a
    /// control signal that drifts by itself, which is what nothing else in the palette produces
    /// without an LFO's periodicity.</para>
    /// </summary>
    private double Black(double white)
    {
        _black = (_black + BrownLeak * BrownRaw(white)) / (1 + BrownLeak);
        return _black * BlackNormalisation;
    }

    /// <summary>
    /// A direct-form-I biquad, for the grey shaping cascade. Held as literal coefficients rather
    /// than computed from a frequency and a gain, because they were fitted offline and recomputing
    /// them at render time would be arithmetic in service of numbers that never change.
    /// </summary>
    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _x1, _x2, _y1, _y2;

        public double Process(double x)
        {
            var y = b0 * x + b1 * _x1 + b2 * _x2 - a1 * _y1 - a2 * _y2;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;
    }
}
