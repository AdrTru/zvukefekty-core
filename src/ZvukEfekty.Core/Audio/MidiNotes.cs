using System.Globalization;

namespace ZvukEfekty.Core.Audio;

/// <summary>
/// Pitch arithmetic and naming for MIDI note numbers, kept in one place because three callers
/// need the same answers: a live <see cref="Voice"/> (frequency), a batch render sampling one
/// pitch (frequency again), and the UI (a name for the on-screen key and for the exported file).
/// </summary>
public static class MidiNotes
{
    /// <summary>Middle C. Scientific pitch notation calls it C4, which is what <see cref="Name"/>
    /// produces and what sample libraries are named by.</summary>
    public const int MiddleC = 60;

    private static readonly string[] NamesInOctave =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>Equal temperament with A4 (MIDI 69) at 440 Hz.</summary>
    public static double ToFrequencyHz(int midiNote) => 440.0 * Math.Pow(2, (midiNote - 69) / 12.0);

    /// <summary>"C4", "F#3". International names, not the Czech ones — a B here is a B, not an H —
    /// because the files land in rekordbox, which sorts by the international convention.</summary>
    public static string Name(int midiNote)
        => NamesInOctave[PitchClass(midiNote)] + Octave(midiNote).ToString(CultureInfo.InvariantCulture);

    /// <summary>True for the notes a piano draws as black keys.</summary>
    public static bool IsSharp(int midiNote) => NamesInOctave[PitchClass(midiNote)].Length == 2;

    private static int PitchClass(int midiNote) => ((midiNote % 12) + 12) % 12;

    private static int Octave(int midiNote) => (int)Math.Floor(midiNote / 12.0) - 1;
}
