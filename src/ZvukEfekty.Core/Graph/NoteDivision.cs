namespace ZvukEfekty.Core.Graph;

/// <summary>
/// What a tempo-synced parameter's note value means once it is turned into a number.
///
/// The unit cannot be guessed from <see cref="ModuleParameter.Unit"/> alone: an LFO's "Hz" is a
/// speed that belongs on the beat grid, an oscillator's "Hz" is a pitch that emphatically does
/// not. So each parameter states its own kind, and parameters that say <see cref="None"/> simply
/// never offer the sync switch.
/// </summary>
public enum TempoSyncKind
{
    /// <summary>Not a musical duration — pitches, gains, mixes, seeds. No sync switch.</summary>
    None,

    /// <summary>A length in seconds: envelope stages, delay times, decays.</summary>
    Seconds,

    /// <summary>A length in milliseconds. Same as <see cref="Seconds"/> times a thousand.</summary>
    Milliseconds,

    /// <summary>A speed rather than a length — an LFO in Hz, a trigger in 1/s. The note value is
    /// the period, so the number handed to the module is its reciprocal.</summary>
    Rate,
}

/// <summary>
/// One entry in the note-value list a tempo-synced parameter picks from, measured in beats.
///
/// Beats, not fractions of a bar, because BPM is literally beats per minute — a quarter note is
/// one beat and needs no conversion factor at all. Everything here assumes 4/4, the same
/// assumption <see cref="Persistence.PatchTiming"/> already makes and the one club music and CDJ
/// beat grids are built on.
/// </summary>
/// <param name="Name">What the picker shows, and what the patch file stores. Stored by name
/// rather than by list index so inserting a division later cannot silently retune saved patches.</param>
/// <param name="Beats">Length in quarter notes. A dot is x1.5, a triplet x2/3.</param>
public sealed record NoteDivision(string Name, double Beats)
{
    /// <summary>Offered longest first, the way a synth's sync list reads. Ends at 1/64: below that
    /// a "note value" at club tempo is under 8 ms and stops being a rhythm at all.</summary>
    public static IReadOnlyList<NoteDivision> All { get; } = new NoteDivision[]
    {
        new("4 takty", 16),
        new("2 takty", 8),
        new("1 takt", 4),
        new("1/2.", 3),
        new("1/2", 2),
        new("1/2T", 4.0 / 3.0),
        new("1/4.", 1.5),
        new("1/4", 1),
        new("1/4T", 2.0 / 3.0),
        new("1/8.", 0.75),
        new("1/8", 0.5),
        new("1/8T", 1.0 / 3.0),
        new("1/16.", 0.375),
        new("1/16", 0.25),
        new("1/16T", 1.0 / 6.0),
        new("1/32", 0.125),
        new("1/64", 0.0625),
    };

    public static IReadOnlyList<string> Names { get; } = All.Select(d => d.Name).ToArray();

    /// <summary>What a freshly switched-on parameter starts at: one beat, the pulse everything
    /// else on the grid is counted against.</summary>
    public static NoteDivision Default { get; } = All.Single(d => d.Name == "1/4");

    /// <summary>The division saved under <paramref name="name"/>, or <see cref="Default"/> for a
    /// name this build does not know — a patch written by a later version keeps opening.</summary>
    public static NoteDivision ByName(string? name)
        => All.FirstOrDefault(d => d.Name == name) ?? Default;

    public double ToSeconds(double bpm) => Beats * 60.0 / bpm;
}
