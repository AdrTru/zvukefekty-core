namespace ZvukEfekty.Core.Graph;

/// <summary>
/// The gap-drawing rule behind every "Nepravidelnost" knob in the engine.
///
/// <para>Shared rather than copied because the knob has to mean the same thing wherever it appears:
/// <see cref="Modules.PoissonTriggerModule"/> spaces impulses with it and
/// <see cref="Modules.GranulatorModule"/> spaces grains with it, and a user who has learned that
/// 0 is a metronome and 1 is a crackle should not have to relearn it per module.</para>
/// </summary>
internal static class IrregularTiming
{
    /// <summary>
    /// Length of the next gap, in mean intervals. Blends the constant 1 (a metronome) with an
    /// exponential draw (a true Poisson process) so <paramref name="jitter"/> slides continuously
    /// between the two. The exponential's own mean is 1, so the blend has mean 1 at every setting
    /// — turning the knob changes the character without changing how often the thing fires.
    /// </summary>
    public static double DrawInterval(Random random, double jitter)
    {
        var amount = Math.Clamp(jitter, 0, 1);
        if (amount <= 0)
            return 1;

        // 1 - NextDouble() lands in (0, 1], never 0, so the logarithm stays finite.
        var exponential = -Math.Log(1 - random.NextDouble());
        return (1 - amount) + amount * exponential;
    }
}
