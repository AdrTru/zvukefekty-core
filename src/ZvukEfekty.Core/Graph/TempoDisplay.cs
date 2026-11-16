using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// The editor's current tempo, for things that have to SHOW a synced time without rendering audio:
/// the resolved seconds under a knob in the inspector, and the ADSR shape drawn on a node.
///
/// Deliberately NOT what <see cref="ModuleBase.Process"/> reads. Audio takes its tempo from
/// <see cref="RenderContext.Bpm"/>, which travels with the render and is therefore correct inside a
/// live voice's own clone of the patch — a clone this object knows nothing about. Reading from here
/// on the audio path would work right up until two things wanted different tempos, and then fail
/// silently. One writer (the main view model, when its Bpm changes); everyone else only reads.
/// </summary>
public sealed class TempoDisplay : INotifyPropertyChanged
{
    private double _bpm = Persistence.PatchTiming.DefaultBpm;

    private TempoDisplay()
    {
    }

    /// <summary>Single shared instance, so a control can bind to it without being handed one.</summary>
    public static TempoDisplay Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public double Bpm
    {
        get => _bpm;
        set
        {
            if (_bpm.Equals(value))
                return;

            _bpm = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
