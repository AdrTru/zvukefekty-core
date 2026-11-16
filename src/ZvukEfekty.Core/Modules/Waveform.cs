using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>Shared periodic waveform shapes and oscillator loop, used by both <see cref="OscillatorModule"/> and <see cref="LfoModule"/>.</summary>
internal static class Waveform
{
    public static readonly string[] Names = { "Sine", "Square", "Saw", "Triangle", "Pulse", "Random", "Custom" };

    private const int PulseIndex = 4;
    private const int RandomIndex = 5;

    /// <summary>How much of a Pulse cycle sits high. Named rather than repeated, because
    /// <see cref="Evaluate"/> and <see cref="EdgeCorrection"/> have to agree about where the two
    /// edges are — a correction applied at the wrong phase makes aliasing worse, not better.</summary>
    private const double PulseDuty = 0.1;

    /// <summary>Index of the hand-drawn shape. Added at the END of the list on purpose: the index
    /// is what a .zvuky file stores, so inserting it anywhere else would silently re-point every
    /// saved Waveform value at a different shape.</summary>
    public const int CustomIndex = 6;

    public static double Evaluate(int waveform, double phase) => waveform switch
    {
        0 => Math.Sin(2 * Math.PI * phase),                       // Sine
        1 => phase < 0.5 ? 1.0 : -1.0,                             // Square
        2 => 2 * phase - 1,                                        // Saw
        3 => 4 * Math.Abs(phase - Math.Floor(phase + 0.5)) - 1,    // Triangle
        PulseIndex => phase < PulseDuty ? 1.0 : -1.0,              // narrow spike — buzzier/richer harmonics than Square
        _ => 0.0                                                   // Random and Custom handled in GenerateBuffer
    };

    /// <summary>
    /// Renders one buffer of a periodic waveform at a per-sample frequency. Centralizes the
    /// oscillator loop shared by <see cref="OscillatorModule"/> and <see cref="LfoModule"/>.
    /// "Random" (sample &amp; hold) needs a value held across samples within a cycle rather than
    /// a pure function of phase, hence the special case and the <paramref name="random"/> source.
    /// <paramref name="phase"/>/<paramref name="held"/> are owned by the caller (an instance field
    /// on the oscillator module) and carried across successive calls, so a real-time voice
    /// rendering many small blocks in a row gets a continuous, click-free waveform instead of
    /// restarting phase at 0 every block. A one-shot batch render behaves the same as before
    /// since the caller's fields start at 0 on a fresh module instance.
    /// </summary>
    /// <param name="custom">The shape used when <paramref name="waveform"/> is
    /// <see cref="CustomIndex"/>. Read through its lookup table rather than evaluated, since phase
    /// lands anywhere and the table turns a search plus a Pow into a lerp.</param>
    /// <param name="scratch">Where the returned buffer comes from, or null to allocate one. Taken
    /// dirty: the loop below writes every sample of it. See <see cref="Graph.BlockScratch"/>.</param>
    public static float[] GenerateBuffer(int waveform, float[] amplitude, float[] frequency, int sampleRate, Random random, Curve? custom, bool antialias, ref double phase, ref double held, Graph.BlockScratch? scratch = null)
    {
        // Rented only when the pool's blocks are this length — they always are, since the frequency
        // buffer came from the same pass; the check is there so a mismatch allocates instead of
        // handing back a buffer of the wrong size.
        var output = scratch?.RentIfLength(frequency.Length) ?? new float[frequency.Length];
        var isCustom = waveform == CustomIndex && custom is not null;

        for (var i = 0; i < frequency.Length; i++)
        {
            var increment = frequency[i] / sampleRate;
            var value = waveform == RandomIndex ? held
                : isCustom ? custom!.Sample(phase)
                : Evaluate(waveform, phase);

            // Guarded on the increment as well as the switch: the correction is a function of how
            // far one sample moves the phase, so it means nothing at zero or negative frequency
            // (modulation can push Frequency below zero) and nothing above two samples per cycle.
            if (antialias && increment > 0 && increment < 0.5)
                value += EdgeCorrection(waveform, phase, increment);

            output[i] = (float)(amplitude[i] * value);

            var previousPhase = phase;
            phase += frequency[i] / sampleRate;
            if (waveform == RandomIndex && Math.Floor(phase) > Math.Floor(previousPhase))
                held = random.NextDouble() * 2 - 1;
            phase -= Math.Floor(phase);
        }

        return output;
    }

    /// <summary>
    /// PolyBLEP correction for the shapes that have a jump in them, to be added to the naive value.
    ///
    /// <para><b>What it is for.</b> <see cref="Evaluate"/> computes the ideal shape at each phase,
    /// which is right on paper and wrong once sampled: an instant jump carries harmonics forever,
    /// and every one of them above Nyquist folds back down onto a frequency that is not a harmonic
    /// of anything. Measured on a bare oscillator, that misplaced energy is 1.1 % of a Saw at
    /// 440 Hz and 9.3 % at 3.5 kHz — it doubles with every octave, which is why it is heard as
    /// notes going gritty as they go up rather than as a constant colour.</para>
    ///
    /// <para><b>What it does.</b> A jump that lands between two samples is replaced by the two
    /// samples either side being nudged, by the amount a band-limited step would actually have put
    /// there. Two samples of polynomial is the cheap approximation of that step; hence "poly".</para>
    ///
    /// <para><b>Which shapes.</b> Square, Saw and Pulse, which jump. Triangle only breaks SLOPE, so
    /// it needs the integral of this (polyBLAMP) and does not get it here — measured, its
    /// misplaced energy is 0,00 % at 440 Hz and 0,07 % at 3,5 kHz, so there is nothing to fix.
    /// Sine has no edge. Random jumps at moments this cannot know about, and a hand-drawn Custom
    /// curve can jump anywhere; both are left alone rather than corrected at the wrong phase.</para>
    /// </summary>
    private static double EdgeCorrection(int waveform, double phase, double increment) => waveform switch
    {
        1 => Blep(phase, increment) - Blep(Wrap(phase - 0.5), increment),              // Square: up at 0, down at 0.5
        2 => -Blep(phase, increment),                                                  // Saw: down at the wrap
        PulseIndex => Blep(phase, increment) - Blep(Wrap(phase - PulseDuty), increment),
        _ => 0.0
    };

    /// <summary>The two-sample polynomial approximation of a band-limited step at phase 0, as a
    /// correction to a unit-height edge. Zero everywhere except the sample before and the sample
    /// after the edge, which is what makes it cost nothing on the other 99 % of the cycle.</summary>
    private static double Blep(double t, double dt)
    {
        if (t < dt)
        {
            t /= dt;
            return t + t - t * t - 1.0;
        }

        if (t > 1.0 - dt)
        {
            t = (t - 1.0) / dt;
            return t * t + t + t + 1.0;
        }

        return 0.0;
    }

    private static double Wrap(double phase) => phase - Math.Floor(phase);
}
