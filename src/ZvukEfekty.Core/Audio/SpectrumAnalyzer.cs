namespace ZvukEfekty.Core.Audio;

/// <summary>Turns one time-domain window into a normalized magnitude spectrum for display, via a
/// self-contained radix-2 FFT (no extra package for something this small).</summary>
internal static class SpectrumAnalyzer
{
    /// <summary>
    /// <paramref name="window"/>.Length must be a power of two. Returns Length/2 magnitude bins
    /// (the other half mirrors it for a real input), each mapped from dB into a 0..1 range so the
    /// UI doesn't need to know sample rate or dB scale to draw it.
    /// </summary>
    public static float[] ComputeMagnitudes(float[] window)
    {
        var n = window.Length;
        var real = new double[n];
        var imag = new double[n];

        for (var i = 0; i < n; i++)
        {
            // Hann window: the slice is an arbitrary, non-periodic chunk of a continuous signal;
            // without tapering its edges, the FFT reads that discontinuity as broadband noise
            // (spectral leakage) that swamps the real content.
            var taper = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (n - 1));
            real[i] = window[i] * taper;
        }

        FftInPlace(real, imag);

        var bins = n / 2;
        var magnitudes = new float[bins];
        for (var i = 0; i < bins; i++)
        {
            var magnitude = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / n;
            var db = 20 * Math.Log10(Math.Max(magnitude, 1e-6));
            // -90dB..0dB mapped to 0..1 — floor chosen so a quiet-but-present tone still shows above
            // the noise floor without a louder one clipping the display.
            magnitudes[i] = (float)Math.Clamp((db + 90) / 90, 0, 1);
        }

        return magnitudes;
    }

    private static void FftInPlace(double[] real, double[] imag)
    {
        var n = real.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wr = Math.Cos(angle);
            var wi = Math.Sin(angle);

            for (var start = 0; start < n; start += len)
            {
                double curWr = 1, curWi = 0;
                var half = len / 2;

                for (var k = 0; k < half; k++)
                {
                    var evenIndex = start + k;
                    var oddIndex = evenIndex + half;

                    var vr = real[oddIndex] * curWr - imag[oddIndex] * curWi;
                    var vi = real[oddIndex] * curWi + imag[oddIndex] * curWr;

                    real[oddIndex] = real[evenIndex] - vr;
                    imag[oddIndex] = imag[evenIndex] - vi;
                    real[evenIndex] += vr;
                    imag[evenIndex] += vi;

                    var nextWr = curWr * wr - curWi * wi;
                    var nextWi = curWr * wi + curWi * wr;
                    curWr = nextWr;
                    curWi = nextWi;
                }
            }
        }
    }
}
