using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Waveshaping distortion: hard/soft clipping or bit-depth reduction (bitcrush), followed by an
/// optional sample-rate reduction.
///
/// The two halves of what people mean by "bitcrush" are separate knobs here because they are
/// separate defects. <see cref="Bitcrush"/> quantises amplitude — fewer steps between -1 and 1 —
/// and that alone sounds gritty but stays wideband. Holding each sample for several output samples
/// quantises TIME instead, which folds everything above the new Nyquist back down as aliasing, and
/// that is where the lo-fi/telephone character actually comes from. Bits without the hold could
/// never produce it, which is what the DJ-sound survey found missing
/// (dj_zvuky_zdroje/recepty/13_glitch_stutter.md).
/// </summary>
public sealed class DistortionModule : ModuleBase
{
    private static readonly string[] TypeNames = { "Hard Clip", "Soft Clip", "Bitcrush" };

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _type;
    private readonly ModuleParameter _drive;
    private readonly ModuleParameter _bits;
    private readonly ModuleParameter _downsample;

    /// <summary>How many input samples have gone by since the held value was last refreshed. Fractional,
    /// so a non-integer factor — or a modulated one — averages out to the right rate instead of
    /// snapping between whole divisors.</summary>
    private double _holdPhase;
    private float _held;
    private bool _hasHeld;

    public DistortionModule() : base("Distortion")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _type = AddParameter("Type", 0, 0, TypeNames.Length - 1, options: TypeNames);
        _drive = AddParameter("Drive", 3, 0.1, 20);
        _bits = AddParameter("Bits", 4, 1, 16);

        // A divisor rather than a target rate in Hz, for two reasons: 1 is exactly "off" and needs no
        // special case, and a linear slider over 1-64 puts the useful settings in the middle instead
        // of squashing them into the bottom tenth of a 100 Hz - 48 kHz sweep.
        // At the engine's 44.1 kHz: 4 = 11 kHz, 10 = 4.4 kHz, which is the 4-11 kHz the lo-fi recipes ask for.
        _downsample = AddParameter("Podvzorkování", 1, 1, 64, "x");
    }

    public override string Description =>
        "Waveshaping zkreslení — tvrdé/měkké oříznutí (Clip) nebo bitcrush (snížení bitové hloubky). " +
        "Podvzorkování drží každý vzorek několikrát po sobě, čímž sníží vzorkovací frekvenci a přidá " +
        "aliasing — to je ta \"lo-fi/telefonní\" barva, kterou samotné Bits neudělají. " +
        "1 = vypnuto, při 44,1 kHz je 4 ≈ 11 kHz a 10 ≈ 4,4 kHz. Působí až za oříznutím, takže jde " +
        "kombinovat i s Clip typy.";

    public override void ResetState()
    {
        _holdPhase = 0;
        _held = 0;
        _hasHeld = false;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var drive = GetModulatedValues(_drive, ctx, inputBuffers);
        var bits = GetModulatedValues(_bits, ctx, inputBuffers);
        var downsample = GetModulatedValues(_downsample, ctx, inputBuffers);
        var type = (int)_type.Value;

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var x = input[i] * drive[i];

            var shaped = type switch
            {
                0 => Math.Clamp(x, -1f, 1f),
                1 => (float)Math.Tanh(x),
                _ => Bitcrush(x, bits[i])
            };

            // Modulation is not clamped to the knob's range (see ModuleBase.GetModulatedValues), so a
            // deep enough LFO could otherwise ask for a factor below 1 — which would mean holding a
            // sample for less than one sample.
            var factor = Math.Max(1.0, downsample[i]);

            _holdPhase += 1.0;
            if (!_hasHeld || _holdPhase >= factor)
            {
                _held = shaped;
                _hasHeld = true;

                // Carry the remainder rather than zeroing it, or a fractional factor drifts towards
                // the next whole number instead of averaging to the rate that was asked for.
                _holdPhase = factor <= 1 ? 0 : _holdPhase % factor;
            }

            output[i] = _held;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    private static float Bitcrush(float x, float bits)
    {
        var steps = MathF.Pow(2, Math.Clamp(bits, 1, 16));
        var clamped = Math.Clamp(x, -1f, 1f);
        return MathF.Round(clamped * steps) / steps;
    }
}
