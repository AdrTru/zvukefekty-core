using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Plucked string (Karplus–Strong): a delay line one period long, fed back into itself through a
/// gentle low-pass. A burst of noise poured into that loop comes out as a struck string — the noise
/// loses its high partials a little more on every round trip, so the sound starts bright and settles
/// into a tone, which is what a string actually does.
///
/// <para>Why it had to be a module. <c>todo.md</c> 12b lists "krátká linka + feedback + tlumení" as
/// the one combination the boxes cannot make: <see cref="DelayModule"/> has feedback but forces its
/// dry signal through at full level, <see cref="DelayLineModule"/> has the right (sub-millisecond)
/// range but no feedback at all, and a loop drawn on the canvas is refused at render time by
/// <see cref="ModuleGraph.TopologicalOrder"/>. So the loop has to live inside a single box.</para>
///
/// <para>And a send/receive pair that dodged that check would not help, which is worth writing down
/// because it is the obvious idea. <see cref="ModuleGraph.RenderModule"/> runs each module exactly
/// once per pass and hands it its whole output array, so a loop closed OUTSIDE a module is a loop
/// one buffer long — 20 ms in a live block, and the entire clip in a batch render, where there is
/// no previous buffer at all. This loop is 200 samples at A3 and 25 at A6. Sample-accurate feedback
/// and buffer-at-a-time rendering are the same decision, and the module is which side of it this
/// engine picked.</para>
///
/// <para>Not the same thing as <see cref="ResonatorModule"/>, which is the other half of that
/// answer. The resonator is a two-pole filter with ONE resonant frequency: stack several, tuned to
/// inharmonic ratios, for modal synthesis (bell, glass, wood — <c>Sestavy/nastroje/zvon.zvuky</c>).
/// This one rings at a frequency AND every harmonic of it at once, because a delay line's comb has
/// a peak at every multiple of 1/delay. That whole harmonic series is what makes it a string
/// rather than a struck plate, and it is why <c>kytara.zvuky</c> needed four resonators to
/// approximate what one of these does directly.</para>
///
/// <para>With nothing wired to it the module plucks itself once at the start of every note, so a
/// freshly dropped box makes a sound (the failure this project keeps mistaking for a bug). Wire
/// <c>Nota.Hz</c> into Frequency — base 0, depth 1, the convention from <see cref="NoteModule"/> —
/// and it is a playable instrument on its own.</para>
/// </summary>
public sealed class KarplusStrongModule : ModuleBase
{
    /// <summary>Lowest note the loop can hold, and therefore what sizes the buffer. 20 Hz at
    /// 96 kHz is 4800 samples (19 kB), cloned into every live voice — the same accounting
    /// <see cref="DelayLineModule"/> does, and the reason neither of them reaches for seconds.</summary>
    private const double MinFrequency = 20;

    private const double MaxFrequency = 5000;

    private const int MaxDelaySamples = (int)(96000 / MinFrequency) + 4;

    /// <summary>ln(0.001) — the 60 dB the Decay knob promises, as a natural log.</summary>
    private static readonly double Minus60Db = Math.Log(0.001);

    /// <summary>Darkest the loop filter is allowed to get: an equal-weight average of this sample
    /// and the last, which is the original 1983 loop filter exactly.
    ///
    /// <para>A one-pole was tried here first, for a Tlumení knob with more reach at the top. It has
    /// none worth having: a pole steep enough to matter takes the FUNDAMENTAL down with it (at 0.9
    /// an A5 loses 36% of its amplitude per round trip), so every note above about 400 Hz died
    /// inside 20 ms and the whole top half of the knob turned into a click whose pitch could not
    /// even be measured. A zero costs the fundamental almost nothing — cos(ω/2), 0.2% at A5 — while
    /// still taking 46 dB off the twentieth harmonic in half a second, and that gap between what it
    /// costs the note and what it costs the harmonics IS the string.</para></summary>
    private const double MaxShelf = 0.5;

    /// <summary>How loud the pluck fills the line, and so how loud the note starts. 0.8 because
    /// that is what <see cref="OscillatorModule"/> and <see cref="XorOscillatorModule"/> put out
    /// unaltered, and a source that is louder than the other sources is a source every patch has to
    /// correct for. Over 2592 renders (every dial crossed with twelve seeds) the note peaks at 0.89
    /// on the default knobs and never above 1.14 anywhere Tlumení is above zero. AT zero it reaches
    /// 1.71: nothing is ever taken out of the loop there, so the allpass keeps redistributing the
    /// noise's crest with nothing damping the result. That corner is what the Gain knob and the
    /// documentation are for.</summary>
    private const double PluckAmplitude = 0.8;

    /// <summary>How much longer than Decay anything at DC is allowed to ring — see the cap in
    /// <see cref="RecomputeCoefficients"/>. 1.5 rather than 1 because forbidding the loop gain to
    /// exceed the fundamental's own target would forbid the normalisation entirely, and that
    /// normalisation is what makes Decay exact for every note below about 1 kHz.</summary>
    private const double MaxDcTailRatio = 1.5;

    private readonly Port _input;
    private readonly Port _trigger;
    private readonly Port _output;
    private readonly ModuleParameter _frequency;
    private readonly ModuleParameter _decay;
    private readonly ModuleParameter _damping;
    private readonly ModuleParameter _gain;
    private readonly SeededRandomSource _noise;

    private readonly float[] _line = new float[MaxDelaySamples];
    private int _writePos;
    private double _filterState;
    private double _allpassInput;
    private double _allpassOutput;
    private double _previousTrigger;

    // Coefficients, plus the three knob readings they were derived from. Recomputed only when one
    // of those moves, which for an unmodulated knob is once per render rather than once per sample:
    // the derivation costs an exp, a sin, a cos and an atan2, more than the whole rest of the loop
    // put together. See RecomputeCoefficients.
    private double _cachedFrequency = double.NaN;
    private double _cachedDecay = double.NaN;
    private double _cachedDamping = double.NaN;
    private int _integerDelay = 1;
    private double _allpassCoefficient;
    private double _loopGain;
    private double _shelf;

    /// <summary>Set on a fresh instance, not only in <see cref="ResetState"/>: a live voice clones
    /// the patch and renders it without ever calling ResetState (see <c>Audio.Voice</c>), so a flag
    /// that only ResetState raised would leave every played note silent.</summary>
    private bool _pendingPluck = true;

    public KarplusStrongModule() : base("Struna")
    {
        _input = AddInput("In");
        _trigger = AddInput("Trig");
        _output = AddOutput("Out");

        _frequency = AddParameter("Frequency", 220, MinFrequency, MaxFrequency, "Hz");

        // Time for the FUNDAMENTAL to fall 60 dB, the same definition Reverb's Dozvuk and
        // Resonator's Decay already use. The harmonics above it fall faster, by exactly as much as
        // Tlumení says — and that difference is the whole sound.
        _decay = AddParameter("Decay", 2.0, 0.05, 10, "s", tempoSync: TempoSyncKind.Seconds);

        // Same name and same direction as Reverb's: 0 = nothing taken off the top, 1 = the most
        // this loop can take. Not a cutoff in Hz — it is loss per round trip, so what it costs a
        // partial depends on how many round trips a second the note makes, which is why a high
        // string dies sooner than a low one at one setting of the knob.
        _damping = AddParameter("Tlumení", 0.5, 0, 1);

        _gain = AddParameter("Gain", 1.0, 0, 4);

        _noise = AddSeed();
    }

    public override string Description =>
        "Drnkaná struna (Karplus–Strong): zpožďovací linka dlouhá jednu periodu, zapojená sama do " +
        "sebe přes tlumení výšek. Nezapojená se sama drnkne na začátku každé noty — stačí pustit " +
        "Nota.Hz do Frequency (základ 0, hloubka 1) a je to hotový nástroj. Decay je doba poklesu " +
        "základního tónu o 60 dB, Tlumení říká, oč rychleji nad ním doznívají vyšší harmonické " +
        "(0 = kovový hřeben bez tlumení, 1 = měkká struna). Vysoké tóny dozní dřív, než Decay " +
        "slibuje — utlumená struna prostě tak dlouho nevydrží. Do Trig se dá drnknout znovu, do In pustit vlastní " +
        "buzení (šum přes obálku, úder, cokoli) místo vnitřního šumu. Na výstupu je jen struna, bez " +
        "suchého signálu — hřebenový filtr z ní uděláte smícháním se vstupem v Mixeru.";

    public override void ResetState()
    {
        Array.Clear(_line);
        _writePos = 0;
        _filterState = 0;
        _allpassInput = 0;
        _allpassOutput = 0;
        _previousTrigger = 0;
        _pendingPluck = true;
        _noise.Reset();

        _cachedFrequency = double.NaN;
        _cachedDecay = double.NaN;
        _cachedDamping = double.NaN;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        // Asking the dictionary rather than GetInputOrSilence, the way SampleHoldModule does: an
        // unwired socket and a wired-but-silent one have to mean different things here. Nothing
        // wired to either socket is what licenses the module to pluck itself.
        var hasExcitation = inputBuffers.TryGetValue(_input, out var excitation);
        var hasTrigger = inputBuffers.TryGetValue(_trigger, out var trigger);

        var frequency = GetModulatedValues(_frequency, ctx, inputBuffers);
        var decay = GetModulatedValues(_decay, ctx, inputBuffers);
        var damping = GetModulatedValues(_damping, ctx, inputBuffers);
        var gain = GetModulatedValues(_gain, ctx, inputBuffers);

        var random = _noise.GetGenerator(out _);
        var output = NewBuffer(ctx);

        // A sample rate above 96 kHz would want a longer buffer than there is; the clamp turns that
        // into a floor under the pitch (the string cannot go below SampleRate/MaxDelaySamples)
        // rather than into an index out of range.
        var maxDelay = Math.Min(MaxDelaySamples - 2, ctx.SampleRate / MinFrequency);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var freq = Math.Clamp(frequency[i], MinFrequency, Math.Min(MaxFrequency, ctx.SampleRate * 0.45));
            var decaySeconds = Math.Max(0.05, decay[i]);
            var damp = Math.Clamp(damping[i], 0, 1);

            if (freq != _cachedFrequency || decaySeconds != _cachedDecay || damp != _cachedDamping)
                RecomputeCoefficients(freq, decaySeconds, damp, ctx.SampleRate, maxDelay);

            if (hasTrigger)
            {
                // Rising edge only, threshold and reasoning as in SampleHoldModule: a gate that
                // stays open is one pluck, not a stream of them.
                if (trigger![i] >= 0.5 && _previousTrigger < 0.5)
                    Pluck(random);

                _previousTrigger = trigger[i];
            }
            else if (_pendingPluck && !hasExcitation)
            {
                Pluck(random);
            }

            // Read before write: the tap sits at least one sample behind the write head, so the two
            // never touch the same slot.
            var readPos = (_writePos - _integerDelay + MaxDelaySamples) % MaxDelaySamples;
            var raw = (double)_line[readPos];

            // The sub-sample part of the delay, as a first-order allpass rather than the linear
            // interpolation DelayLineModule uses. Inside a feedback loop the difference is not
            // cosmetic: linear interpolation is itself a low-pass whose strength depends on the
            // fractional part, that fraction moves when Tlumení moves (the loop filter's phase
            // delay is part of the same sum), and the two damping mechanisms then fight. Measured
            // on the linear version, the twentieth harmonic came out 10 dB BRIGHTER at Tlumení 1
            // than at 0.5 — a tone knob that reverses in its own top quarter. An allpass passes
            // every frequency at full level and only delays, so what Tlumení says is all there is.
            var delayed = _allpassCoefficient * (raw - _allpassOutput) + _allpassInput;
            _allpassInput = raw;
            _allpassOutput = delayed;

            // The loop's low-pass: an average of this sample and the last, weighted by Tlumení.
            // One zero rather than one pole, for the reason MaxShelf gives — and gentle where it
            // has to be: at the darkest setting this costs an A3's fundamental 0.01% per round trip
            // while costing its twentieth harmonic 5%.
            var filtered = (1 - _shelf) * delayed + _shelf * _filterState;
            _filterState = delayed;

            _line[_writePos] = (float)((hasExcitation ? excitation![i] : 0) + _loopGain * filtered);
            _writePos = (_writePos + 1) % MaxDelaySamples;

            output[i] = (float)(filtered * gain[i]);
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    /// <summary>
    /// Fills the whole line with zero-mean noise — the original 1983 pluck, and still the reason
    /// this costs one delay line rather than an oscillator bank: every harmonic of the note is
    /// already present in white noise, so the loop never has to synthesise them, only take them
    /// away again.
    ///
    /// <para>Zero-mean matters, and over the right window. Whatever DC the burst carries sits at
    /// the very top of the loop's response (a low-pass passes DC untouched) and survives the whole
    /// Decay as an offset under the note. The window that decides it is not the buffer but the
    /// <see cref="_integerDelay"/> slots the tap will actually read back — a few dozen of them at
    /// the top of the range. Averaging the whole 4804-sample buffer instead leaves that short
    /// window carrying a random offset of its own, measured as a −0.0148 mean under a plucked A2.</para>
    /// </summary>
    private void Pluck(Random random)
    {
        for (var i = 0; i < MaxDelaySamples; i++)
            _line[i] = (float)((random.NextDouble() * 2 - 1) * PluckAmplitude);

        var active = Math.Min(MaxDelaySamples, _integerDelay + 2);
        var sum = 0.0;
        for (var k = 1; k <= active; k++)
            sum += _line[(_writePos - k + MaxDelaySamples) % MaxDelaySamples];

        var mean = (float)(sum / active);
        for (var i = 0; i < MaxDelaySamples; i++)
            _line[i] -= mean;

        _filterState = 0;
        _allpassInput = 0;
        _allpassOutput = 0;
        _pendingPluck = false;
    }

    /// <summary>
    /// Turns the three knobs into the numbers the loop actually runs on: how long the line is, and
    /// how much of it comes back.
    ///
    /// <para><b>Tuning.</b> The loop's delay is not the line alone — the low-pass in it holds the
    /// signal back too, by its phase delay at the fundamental. Half a sample at most, which sounds
    /// ignorable and is not: it is a fixed number of SAMPLES, so against A6's 25-sample period it
    /// is a fifth of a semitone. Subtracting it keeps the string in tune across the whole keyboard
    /// rather than only in the octave it happened to be checked in.</para>
    ///
    /// <para><b>Decay.</b> One round trip has to leave the fundamental at 0.001^(period/Decay) —
    /// that is what "60 dB in Decay seconds" means. The low-pass already takes part of it, so the
    /// loop gain is that target DIVIDED by what the filter passes at the fundamental, which leaves
    /// Decay meaning the same thing at every setting of Tlumení instead of drifting shorter as the
    /// sound darkens. The division is capped just under 1: past that the loop would grow instead of
    /// decaying. That cap is the one place the knob stops being exact — a high note asked to ring
    /// for ten seconds through heavy damping rings for as long as the damping allows and no longer,
    /// which is also what a real string does.</para>
    /// </summary>
    private void RecomputeCoefficients(double freq, double decaySeconds, double damp, int sampleRate, double maxDelay)
    {
        _cachedFrequency = freq;
        _cachedDecay = decaySeconds;
        _cachedDamping = damp;

        _shelf = MaxShelf * damp;

        // Magnitude and phase delay of (1-s) + s·z⁻¹ at the fundamental, both exact. The phase
        // delay is only half a sample at its worst, but half a sample is a fifth of a semitone at
        // A6 and the tuning table shows it: 0.4 cents across the whole keyboard with this term,
        // and drifting sharp with pitch without it.
        var omega = 2 * Math.PI * freq / sampleRate;
        var real = (1 - _shelf) + _shelf * Math.Cos(omega);
        var imaginary = -_shelf * Math.Sin(omega);

        var filterGain = Math.Max(Math.Sqrt(real * real + imaginary * imaginary), 1e-12);
        var filterDelay = Math.Atan2(-imaginary, real) / omega;

        // What is left for the line and the allpass between them, in samples.
        var remaining = Math.Clamp(sampleRate / freq - filterDelay, 2, maxDelay);

        // Split so the allpass carries between half a sample and one and a half, rather than
        // whatever is left over. Its coefficient blows up towards 1 as the fraction approaches 0 —
        // a pole that close to the unit circle rings for hundreds of samples after every change of
        // pitch — and holding the fraction away from that end costs nothing but one whole sample of
        // line, which the line has.
        _integerDelay = Math.Max(1, (int)(remaining - 0.5));
        var fraction = remaining - _integerDelay;

        // Solve for the fraction the allpass has to be ASKED for to actually deliver that delay.
        // (1-d)/(1+d) is only the low-frequency answer; at A6 the true phase delay is several
        // percent off it, and several percent of a sample is audible as detuning. Three passes of
        // "measure what it gives, correct by the error" get it under a thousandth of a sample.
        var request = fraction;
        for (var pass = 0; pass < 3; pass++)
        {
            var coefficient = (1 - request) / (1 + request);
            var error = AllpassPhaseDelay(coefficient, omega) - fraction;
            request -= error;
        }

        _allpassCoefficient = (1 - request) / (1 + request);

        var perPeriod = Math.Exp(Minus60Db / (freq * decaySeconds));

        // The cap, and why it is not simply "just under 1". A low-pass passes DC untouched, so
        // whatever the loop gain is, DC gets ALL of it — and the delay line's comb has a peak at
        // 0 Hz just as it has one at every harmonic. Pinned at 0.9999 the loop therefore held a
        // 39-second hum under a 1760 Hz note that had itself died in 0.65 s (measured, before this
        // line existed). Bounding the gain bounds that hum to MaxDcTailRatio × Decay.
        var dcTailCap = Math.Exp(Minus60Db / (freq * decaySeconds * MaxDcTailRatio));

        _loopGain = Math.Clamp(perPeriod / filterGain, 0, Math.Min(0.9999, dcTailCap));
    }

    /// <summary>Phase delay, in samples, of the allpass (c + z⁻¹)/(1 + c·z⁻¹) at
    /// <paramref name="omega"/>. Exact, because the usual approximation is what the caller is
    /// trying to correct.</summary>
    private static double AllpassPhaseDelay(double coefficient, double omega)
    {
        var sin = Math.Sin(omega);
        var cos = Math.Cos(omega);

        var numerator = Math.Atan2(-sin, coefficient + cos);
        var denominator = Math.Atan2(-coefficient * sin, 1 + coefficient * cos);

        return -(numerator - denominator) / omega;
    }
}
