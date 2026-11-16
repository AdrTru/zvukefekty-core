namespace ZvukEfekty.Core.Persistence;

/// <summary>
/// How long a one-shot render is, and whether that length runs free or is derived from a tempo.
///
/// A free-running effect (a door creak, an explosion) is simply a length in seconds. Material
/// meant to sit inside a DJ set — risers, sweeps, loops — has to line up with a bar grid instead,
/// or it drifts against the track it is layered over. In <see cref="IsTempoLocked"/> mode the
/// length therefore follows from <see cref="Bpm"/> and <see cref="Bars"/> instead of being set
/// directly, and <see cref="DurationSeconds"/> is whatever those two work out to.
/// </summary>
public sealed class PatchTiming
{
    public const double MinDurationSeconds = 0.1;

    /// <summary>Upper limit for a free-running length. Tempo-locked lengths are deliberately not
    /// clamped to it — clipping 16 bars down to fit would silently break the bar grid, which is
    /// the one thing that mode exists to guarantee (16 bars at 60 BPM run to 64 s).</summary>
    public const double MaxFreeDurationSeconds = 60.0;

    public const double MinBpm = 60.0;
    public const double MaxBpm = 200.0;
    public const double DefaultBpm = 128.0;

    /// <summary>Beats per bar. Everything here assumes 4/4 — the time signature essentially all
    /// club music is in, and the one CDJ/XDJ beat grids are built around.</summary>
    private const int BeatsPerBar = 4;

    public double DurationSeconds { get; init; } = 1.5;
    public double Bpm { get; init; } = DefaultBpm;
    public int Bars { get; init; } = 4;
    public bool IsTempoLocked { get; init; }

    /// <summary>Length of <paramref name="bars"/> bars of 4/4 at <paramref name="bpm"/>, in seconds.</summary>
    public static double SecondsForBars(double bpm, int bars)
        => bars * BeatsPerBar * 60.0 / Math.Clamp(bpm, MinBpm, MaxBpm);
}
