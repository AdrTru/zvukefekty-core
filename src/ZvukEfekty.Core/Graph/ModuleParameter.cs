using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// An editable knob on a module, shown in the property inspector.
/// For enum-style parameters (e.g. waveform shape) <see cref="Options"/> is set
/// and <see cref="Value"/> holds the selected option's index.
/// </summary>
public sealed class ModuleParameter : INotifyPropertyChanged
{
    private double _value;
    private double _modulationAmount;
    private bool _isTempoSynced;
    private bool _isAutoSeeded;
    private string _divisionName = NoteDivision.Default.Name;

    public ModuleParameter(string name, double value, double min, double max, string unit = "", string[]? options = null)
    {
        Name = name;
        _value = value;
        Min = min;
        Max = max;
        Unit = unit;
        Options = options;
        ModulationAmountLimit = Math.Max(Math.Abs(max - min), 1) * 2;

        // Default depth is a fraction of the parameter's own range, not a flat "1" —
        // a flat unit would be inaudible on a wide-range parameter like Frequency (20-5000Hz)
        // and overwhelming on a narrow one like a 0-1 Amplitude, so a fresh connection is
        // immediately audible without the user having to find the right knob position first.
        _modulationAmount = Math.Max(Math.Abs(max - min), 1) * 0.2;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>Raises change notification, so a value set from code — the randomize button on a
    /// seed, say — reaches the inspector's slider and text box instead of only travelling the
    /// other way.</summary>
    public double Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
    public double Min { get; }
    public double Max { get; }
    public string Unit { get; }
    public string[]? Options { get; }

    public bool IsEnum => Options is not null;

    /// <summary>
    /// Optional input port letting another module's signal drive this parameter
    /// (added to that value sample-by-sample on top of <see cref="Value"/>). Null for enum parameters.
    /// </summary>
    public Port? ModulationInput { get; internal set; }

    /// <summary>
    /// Multiplies the incoming modulation signal (roughly -1..1) before it's added to
    /// <see cref="Value"/> — the "depth" of the modulation. Defaults to 20% of the
    /// parameter's own range (see constructor), not a flat raw-signal multiplier.
    /// </summary>
    public double ModulationAmount
    {
        get => _modulationAmount;
        set => SetField(ref _modulationAmount, value);
    }

    /// <summary>Suggested +/- bound for a ModulationAmount slider, sized to this parameter's own range.</summary>
    public double ModulationAmountLimit { get; }

    /// <summary>Whether this knob is a musical duration or speed, and in which unit — see
    /// <see cref="TempoSyncKind"/>. <see cref="TempoSyncKind.None"/> (the default) means the
    /// inspector shows no sync switch and <see cref="ResolveValue"/> is a no-op.</summary>
    public TempoSyncKind TempoSync { get; init; }

    public bool IsTempoSyncable => TempoSync != TempoSyncKind.None;

    /// <summary>When on, <see cref="Value"/> stops being the number the module reads and
    /// <see cref="Division"/> takes over — see <see cref="ResolveValue"/>. Per parameter on
    /// purpose: a riser wants its sweep on the bar grid and its click at a fixed 5 ms, and one
    /// switch for the whole patch could not give you both.</summary>
    public bool IsTempoSynced
    {
        get => _isTempoSynced;
        set
        {
            if (!SetField(ref _isTempoSynced, value))
                return;

            OnPropertyChanged(nameof(ShowsPlainValue));
            OnPropertyChanged(nameof(ShowsDivisionPicker));
        }
    }

    /// <summary>Whether the inspector shows this knob's slider and number box. Computed here rather
    /// than assembled from three converters in XAML, where the combination would have to be spelled
    /// out again at every control it applies to.</summary>
    public bool ShowsPlainValue => !IsEnum && !IsTempoSynced && !IsAutoSeeded;

    public bool ShowsDivisionPicker => IsTempoSyncable && IsTempoSynced;

    /// <summary>Name of the picked note value, one of <see cref="NoteDivision.Names"/>. Held as a
    /// name rather than an index so the picker's order can change without retuning saved patches.</summary>
    public string DivisionName
    {
        get => _divisionName;
        set => SetField(ref _divisionName, value ?? NoteDivision.Default.Name);
    }

    public NoteDivision Division => NoteDivision.ByName(_divisionName);

    /// <summary>The note values the picker offers. An instance property purely so the inspector's
    /// item template can bind it the same way it binds <see cref="Options"/>.</summary>
    public IReadOnlyList<string> DivisionNames => NoteDivision.Names;

    /// <summary>The number the module actually reads at <paramref name="bpm"/>: the note value
    /// converted into this parameter's own unit when sync is on, plain <see cref="Value"/>
    /// otherwise.
    ///
    /// Clamped to the parameter's range, which CAN pull the result off the beat — 1 takt at 60 BPM
    /// is 4 s and Delay only reaches 2. Clamping rather than widening every range, because the
    /// limits are there for a reason (a delay line allocates its buffer from its maximum, per
    /// voice) and because a clamp is at least visible: the inspector shows the resolved time, so a
    /// value that stopped short of its note reads as wrong instead of drifting unnoticed.</summary>
    public double ResolveValue(double bpm)
    {
        if (!IsTempoSynced || !IsTempoSyncable)
            return Value;

        return Math.Clamp(RawTempoValue(bpm), Min, Max);
    }

    /// <summary>True when the note value does not fit this parameter's range at
    /// <paramref name="bpm"/> and <see cref="ResolveValue"/> had to clamp it, so the inspector can
    /// say so rather than quietly showing a time that is not the beat asked for.</summary>
    public bool IsTempoValueClamped(double bpm)
    {
        if (!IsTempoSynced || !IsTempoSyncable)
            return false;

        var raw = RawTempoValue(bpm);
        return raw < Min || raw > Max;
    }

    /// <summary>The note value in this parameter's unit, before the range is applied.</summary>
    private double RawTempoValue(double bpm)
    {
        var seconds = Division.ToSeconds(bpm);
        return TempoSync switch
        {
            TempoSyncKind.Seconds => seconds,
            TempoSyncKind.Milliseconds => seconds * 1000.0,
            // A note value is a period; a rate parameter wants how many of them fit in a second.
            TempoSyncKind.Rate => 1.0 / seconds,
            _ => Value,
        };
    }

    /// <summary>True when this parameter has an input port, i.e. another module's signal can drive
    /// it. False for enum parameters and for values a per-sample signal makes no sense for, such as
    /// a noise seed.</summary>
    public bool IsModulatable => ModulationInput is not null;

    /// <summary>True for the "Seed" knob behind a <see cref="SeededRandomSource"/>. Lets a caller
    /// find the randomness knobs in a patch without matching on the parameter's name -- a live
    /// <c>Voice</c> offsets them so simultaneously-held notes do not share one random sequence.</summary>
    public bool IsSeed { get; init; }

    /// <summary>
    /// When on, this seed is drawn afresh every time the sound starts instead of being the fixed
    /// number under it — see <see cref="SeededRandomSource.Reset"/>, which is the single place all
    /// five seeded modules go through. The patch then sounds a little different on each Play,
    /// Export and live note, on purpose: a robot voice, a crowd, wind.
    ///
    /// <para>It has to be a stored flag rather than "just don't write the Seed", which is how the
    /// one patch that wanted this (test_promptu/E_robot_kov) got the behaviour by accident. That
    /// only lasted until someone pressed Save: the serializer writes every parameter, so the seed
    /// the module happened to draw on load got recorded and the randomness froze for good. A flag
    /// survives the round trip, and therefore survives being saved.</para>
    ///
    /// <para>Deliberately NOT written back into <see cref="Value"/> when a draw happens. The knob
    /// is bound to the inspector and a render must not push values into the UI — a live voice
    /// renders on the audio thread, and its clone would be writing to a bound object from there.
    /// The consequence to accept: while this is on, the number under the knob is not what you are
    /// hearing, which is why the inspector hides it (see <see cref="ShowsPlainValue"/>). Keeping a
    /// draw you liked means turning this off and rolling the knob by hand.</para>
    /// </summary>
    public bool IsAutoSeeded
    {
        get => _isAutoSeeded;
        set
        {
            if (!SetField(ref _isAutoSeeded, value))
                return;

            OnPropertyChanged(nameof(ShowsPlainValue));
        }
    }

    /// <summary>True when the inspector should offer a "roll a new one" button — for values picked
    /// arbitrarily rather than dialled in, where the useful gesture is a fresh draw, not a nudge.</summary>
    public bool IsRandomizable { get; init; }

    /// <summary>Replaces this parameter's value with a fresh draw from its own range, rounded to a
    /// whole number since the randomizable parameters so far are integer seeds.</summary>
    public void Randomize() => Value = Math.Round(Min + Random.Shared.NextDouble() * (Max - Min));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
