using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Rounds off the edges of a signal: a step is followed at a limited speed instead of instantly.
/// Portamento on a melody, and the thing that turns the staircase out of
/// <see cref="SampleHoldModule"/> or <see cref="SequencerModule"/> into a shape that does not click.
///
/// <para><b>The times are per transition, not per unit.</b> The knobs set how long it takes to
/// cover 90% of whatever distance is in front of it, so the same setting glides the same way
/// whether the jump is 0.2 on a control signal or 400 Hz on a melody. A rack's slew limiter is
/// usually the other kind — a constant rate in volts per second — and that kind is unusable here:
/// the same knob would take two hundred times longer on a Hz signal than on a 0-1 one, since this
/// engine's modulation carries each parameter's own unit rather than a normalised voltage (see
/// <see cref="ModuleBase.GetModulatedValues"/>).</para>
///
/// <para>Rise and fall are separate because that is what makes it more than a smoother: a short
/// rise with a long fall follows every peak and lets go of it slowly, which is the shape of a
/// level follower rather than of a glide.</para>
/// </summary>
public sealed class SlewModule : ModuleBase
{
    /// <summary>How much of the remaining distance is covered within the time on the knob. The
    /// approach is exponential, so it never formally arrives and some fraction has to be named for
    /// the number on the knob to mean anything. 90% is the usual choice for a glide, and it makes
    /// the knob read as "how long it takes to get there" to about a tenth of the distance.</summary>
    private const double ConvergenceFraction = 0.9;

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _rise;
    private readonly ModuleParameter _fall;

    private double _current;
    private bool _started;

    // Last time-to-coefficient conversion, kept because Math.Pow is far and away the most expensive
    // thing in the loop and the knob usually holds still. A modulated knob simply misses the cache
    // every sample and pays what it would have paid anyway.
    private double _cachedSeconds = double.NaN;
    private int _cachedSampleRate;
    private double _cachedKeep;

    public SlewModule() : base("Sklouznutí")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _rise = AddParameter("Nahoru", 0.05, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
        _fall = AddParameter("Dolů", 0.05, 0, 10, "s", tempoSync: TempoSyncKind.Seconds);
    }

    public override string Description =>
        "Zaobluje skoky v signálu — místo okamžitého přeskoku se hodnota k nové plynule " +
        "dojede. Na melodii je to portamento, na schodech z Vzorkovače nebo Sekvenceru je to " +
        "rozdíl mezi cvaknutím a přejezdem. Nahoru a Dolů jsou časy, za které signál urazí 90 % " +
        "vzdálenosti k nové hodnotě, a platí bez ohledu na to, jak je ten skok velký: stejné " +
        "nastavení zní stejně na signálu 0–1 i na výšce v hertzech. Různé časy nahoru a dolů " +
        "z toho udělají sledovač špiček — rychle nahoru, pomalu dolů. Nula vypne vyhlazení " +
        "v daném směru.";

    public override void ResetState()
    {
        _current = 0;
        _started = false;
        _cachedSeconds = double.NaN;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var rise = GetModulatedValues(_rise, ctx, inputBuffers);
        var fall = GetModulatedValues(_fall, ctx, inputBuffers);

        var output = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var target = (double)input[i];

            // The first sample is taken as-is. Starting from zero instead would put a glide up from
            // silence at the head of every sound, and on a Hz signal that glide is a swoop from
            // 0 Hz that nobody asked for.
            if (!_started)
            {
                _started = true;
                _current = target;
            }
            else
            {
                var seconds = Math.Max(0, target > _current ? rise[i] : fall[i]);
                if (seconds <= 0)
                {
                    _current = target;
                }
                else
                {
                    // Fraction of the remaining distance kept per sample, such that after
                    // `seconds` only (1 - ConvergenceFraction) of it is left.
                    if (seconds != _cachedSeconds || ctx.SampleRate != _cachedSampleRate)
                    {
                        _cachedSeconds = seconds;
                        _cachedSampleRate = ctx.SampleRate;
                        _cachedKeep = Math.Pow(1 - ConvergenceFraction, 1.0 / (seconds * ctx.SampleRate));
                    }

                    _current = target + (_current - target) * _cachedKeep;
                }
            }

            output[i] = (float)_current;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }
}
