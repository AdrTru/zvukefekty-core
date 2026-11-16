using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZvukEfekty.Core.Audio;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// One entry of a <see cref="ModuleSequence"/>: a pitch, how many steps it occupies, and whether
/// it sounds at all.
///
/// <para>A note LIST rather than a fixed grid of cells. A grid needs a "tie" flag to say that a
/// cell continues the note before it, and then the same musical fact — how long this note is — is
/// spread over several cells that have to agree with each other. Here it is one number on one
/// entry, which is also what makes the sequence readable in the patch file: the melody is written
/// down as the melody, not as a bitmap of it.</para>
/// </summary>
public sealed class SequenceStep : INotifyPropertyChanged
{
    /// <summary>Longest a single entry may occupy. Sixty-four steps is four bars of sixteenths —
    /// past that the number stops being a note length and starts being a mistake.</summary>
    public const int MaxLength = 64;

    private int _note = MidiNotes.MiddleC;
    private int _length = 1;
    private bool _rest;

    public SequenceStep()
    {
    }

    public SequenceStep(int note, int length = 1, bool rest = false)
    {
        _note = Math.Clamp(note, 0, 127);
        _length = Math.Clamp(length, 1, MaxLength);
        _rest = rest;
    }

    /// <summary>MIDI note number, the same units <see cref="MidiNotes"/> and the sampler's
    /// "Základní nota" use. Semitones rather than hertz because a melody is written in semitones,
    /// and because a Hz box invites 329.63 where the user meant E4.</summary>
    public int Note
    {
        get => _note;
        set
        {
            var clamped = Math.Clamp(value, 0, 127);
            if (_note == clamped)
                return;

            _note = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NoteName));
        }
    }

    /// <summary>How many steps this note occupies. One is a plain step; two is a note twice as
    /// long, which is what a tie would have said in a grid.</summary>
    public int Length
    {
        get => _length;
        set
        {
            var clamped = Math.Clamp(value, 1, MaxLength);
            if (_length == clamped)
                return;

            _length = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>A gap of the same length instead of a note. The pitch is kept rather than
    /// cleared, so switching a rest back on brings the note it used to be — and so anything
    /// tracking Hz does not lurch during the silence.</summary>
    public bool Rest
    {
        get => _rest;
        set
        {
            if (_rest == value)
                return;

            _rest = value;
            OnPropertyChanged();
        }
    }

    /// <summary>"E4", "F#3" — for the inspector's picker.</summary>
    public string NoteName => MidiNotes.Name(_note);

    public double FrequencyHz => MidiNotes.ToFrequencyHz(_note);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// A named list of <see cref="SequenceStep"/>s belonging to a module — a knob whose value is a
/// melody rather than a number.
///
/// <para>Same reason as <see cref="ModuleCurve"/> for not being a <see cref="ModuleParameter"/>:
/// that type is built on a single <see cref="double"/>, and the modulation port, the depth and the
/// slider that hang off it are all meaningless here. Same reason as <see cref="ModuleCurve"/>, too,
/// for living beside the parameters rather than being smuggled into them.</para>
///
/// <para>This is deliberately NOT a curve. A curve stores a shape sampled through a 4096-cell
/// lookup table, so a step edge lands within a cell rather than on a sample, and its Y axis is a
/// continuum where a melody wants twelve semitones. Both showed up in practice: the first two
/// melody patches in <c>Sestavy/</c> spell their notes out as curve breakpoints and delay taps
/// respectively, and neither can be edited without recomputing coordinates.</para>
/// </summary>
public sealed class ModuleSequence : INotifyPropertyChanged
{
    /// <summary>All 128 MIDI notes by name, so the inspector picks E4 rather than 64. Shared
    /// rather than built per row: a thirty-note sequence would otherwise hold thirty copies of the
    /// same list.</summary>
    public static readonly IReadOnlyList<string> NoteNames =
        Enumerable.Range(0, 128).Select(MidiNotes.Name).ToArray();

    public ModuleSequence(string name, IEnumerable<SequenceStep> steps)
    {
        Name = name;
        Steps = new ObservableCollection<SequenceStep>(steps);
        Steps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Steps));
            OnPropertyChanged(nameof(TotalSteps));
        };
    }

    public string Name { get; }

    public ObservableCollection<SequenceStep> Steps { get; }

    /// <summary>Length of the whole sequence in steps — what the module wraps at when it loops.</summary>
    public int TotalSteps => Steps.Sum(s => s.Length);

    /// <summary>Adds a step after <paramref name="after"/>, or at the end when it is null. Copies
    /// the pitch of the step it follows: a melody is written by moving away from the note before
    /// it, so repeating that note is the cheapest useful default.</summary>
    public SequenceStep Add(SequenceStep? after = null)
    {
        var index = after is null ? Steps.Count : Steps.IndexOf(after) + 1;
        var template = after ?? Steps.LastOrDefault();
        var step = new SequenceStep(template?.Note ?? MidiNotes.MiddleC, template?.Length ?? 1);
        Steps.Insert(Math.Clamp(index, 0, Steps.Count), step);
        return step;
    }

    public void Remove(SequenceStep step) => Steps.Remove(step);

    /// <summary>Replaces the whole list in one go — what the deserializer uses, so a load raises
    /// one change rather than one per note.</summary>
    public void Replace(IEnumerable<SequenceStep> steps)
    {
        Steps.Clear();
        foreach (var step in steps)
            Steps.Add(step);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
