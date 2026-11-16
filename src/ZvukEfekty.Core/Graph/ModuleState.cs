using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A named <see cref="Graph.Curve"/> belonging to a module — a knob whose value is a shape rather
/// than a number.
///
/// <see cref="ModuleParameter"/> cannot carry this: it is built on a single <see cref="double"/>,
/// and everything downstream of it (the modulation input port, <see cref="ModuleBase.GetModulatedValues"/>,
/// the inspector's slider) assumes that. A curve has no single value to modulate and no range to
/// slide along, so it lives beside the parameters instead of pretending to be one.
/// </summary>
public sealed class ModuleCurve : INotifyPropertyChanged
{
    private readonly Curve _default;
    private Curve _curve;
    private bool _endpointsUntied;

    public ModuleCurve(string name, Curve curve, CurveOutput output, string xLabel, string yLabel)
    {
        Name = name;
        Output = output;
        XLabel = xLabel;
        YLabel = yLabel;
        _curve = output.Conform(curve, output.TieEndpoints);
        _default = _curve;
    }

    public string Name { get; }

    /// <summary>The range and continuity the module downstream of this curve needs — see
    /// <see cref="CurveOutput"/>. Enforced on every assignment, including the one the deserializer
    /// makes, so a hand-written file cannot introduce a value the consumer cannot use.</summary>
    public CurveOutput Output { get; }

    /// <summary>What the axes mean here — the one thing that differs between a transfer function, a
    /// waveform and an envelope, and the only thing an editor needs to be told to serve all three.</summary>
    public string XLabel { get; }

    public string YLabel { get; }

    /// <summary>Ready-made shapes the editor offers for this curve — see <see cref="CurvePresets"/>.
    /// Exposed here rather than reached for statically by the view, so a curve that one day wants a
    /// different list can say so without the editor learning about module types.</summary>
    public IReadOnlyList<CurvePreset> Presets => CurvePresets.All;

    /// <summary>
    /// Replaces the curve with a preset shape.
    ///
    /// Goes through here rather than through <see cref="Curve"/> directly because a preset can need
    /// the ends broken apart to mean anything — a sawtooth's reset IS the wrap — and setting the
    /// curve first would let <see cref="Conform"/> tie the ramp's ends together before the flag
    /// arrives, quietly flattening the shape the user just picked.
    /// </summary>
    public void Apply(CurvePreset preset)
    {
        if (preset.UntieEndpoints && Output.TieEndpoints)
            EndpointsUntied = true;

        Curve = preset.Create();
    }

    /// <summary>True when the user has deliberately broken a wrapping curve's ends apart. Only
    /// meaningful where <see cref="CurveOutput.TieEndpoints"/> is set; an untied cycle is a sawtooth,
    /// which is a legitimate shape to want rather than a mistake — hence a choice and not a lock.</summary>
    public bool EndpointsUntied
    {
        get => _endpointsUntied;
        set
        {
            if (_endpointsUntied == value)
                return;

            _endpointsUntied = value;
            OnPropertyChanged();

            // Re-tying has to pull the ends back together right away; leaving that until the next
            // edit would let the checkbox claim something the curve does not yet do.
            Curve = _curve;
        }
    }

    /// <summary>Whether this curve's ends are currently held together.</summary>
    public bool AreEndpointsTied => Output.TieEndpoints && !_endpointsUntied;

    /// <summary>
    /// True while this still holds exactly what the module constructed it with.
    ///
    /// The patch file leaves such a curve out entirely. Without that, adding a curve to Oscillator
    /// and LFO would have written a nine-point default sine into every one of them in every saved
    /// patch — measured on ufon.zvuky, 243 lines becoming 470, none of it describing anything the
    /// user did. Same principle as leaving a zero tension out: the file should record decisions,
    /// not restate defaults.
    /// </summary>
    public bool IsDefault
    {
        get
        {
            if (_endpointsUntied)
                return false;

            var current = _curve.Points;
            var original = _default.Points;
            if (current.Count != original.Count)
                return false;

            for (var i = 0; i < current.Count; i++)
                if (current[i] != original[i])
                    return false;

            return true;
        }
    }

    public Curve Curve
    {
        get => _curve;
        set
        {
            var conformed = Output.Conform(value, AreEndpointsTied);
            if (ReferenceEquals(_curve, conformed))
                return;

            _curve = conformed;
            OnPropertyChanged(nameof(Curve));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// A named path to a file a module reads — a sampler's source recording, say. Same reason as
/// <see cref="ModuleCurve"/> for not being a <see cref="ModuleParameter"/>: there is no number here.
/// </summary>
public sealed class ModuleFile : INotifyPropertyChanged
{
    private string _path = "";
    private string? _status;

    public ModuleFile(string name, string dialogFilter)
    {
        Name = name;
        DialogFilter = dialogFilter;
    }

    public string Name { get; }

    /// <summary>Filter string for the editor's file dialog, so the module decides what it accepts
    /// rather than the UI hard-coding a guess.</summary>
    public string DialogFilter { get; }

    /// <summary>
    /// What happened last time the module tried to read this file — null when it is fine.
    ///
    /// Shown in the inspector rather than swallowed. A missing or unreadable file makes a module
    /// render silence, and silence is exactly the kind of failure this project keeps getting bitten
    /// by: the patch loads, nothing throws, and the only symptom is that you hear nothing.
    /// </summary>
    public string? Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Empty until the user picks something. A module with no file must render silence
    /// rather than throw — a patch can legitimately be saved half-built.</summary>
    public string Path
    {
        get => _path;
        set
        {
            var incoming = value ?? "";
            if (_path == incoming)
                return;

            _path = incoming;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// A named piece of text a module reads as its content — today the one formula
/// <see cref="Modules.FormulaModule"/> evaluates. The fourth kind of non-knob state, next to
/// <see cref="ModuleCurve"/>, <see cref="ModuleFile"/> and <see cref="ModuleSequence"/>, and here
/// for the same reason as all three: there is no single number to slide, no range to slide it
/// along and nothing for a modulation port to drive.
///
/// <para><b>Always written to the patch file</b>, like a sequence and unlike a curve. A default
/// curve is a shape nobody chose, so leaving it out keeps files about decisions; a formula IS the
/// module's content, and one left out of the file would load back computing something else.</para>
/// </summary>
public sealed class ModuleText : INotifyPropertyChanged
{
    private string _text;
    private string? _error;

    public ModuleText(string name, string text, string hint)
    {
        Name = name;
        _text = text;
        Hint = hint;
    }

    public string Name { get; }

    /// <summary>What may be written here, shown under the box. Comes from the module rather than
    /// from the inspector, so the help and the thing that parses the text stay together.</summary>
    public string Hint { get; }

    public string Text
    {
        get => _text;
        set
        {
            var incoming = value ?? "";
            if (_text == incoming)
                return;

            _text = incoming;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Why the module could not use this text — null when it is fine.
    ///
    /// Shown in the inspector rather than swallowed, for the same reason
    /// <see cref="ModuleFile.Status"/> exists: text typed by hand is wrong sometimes, and the
    /// failure mode this project keeps getting bitten by is the one where nothing throws and the
    /// only symptom is that you hear nothing. A module whose text will not parse is expected to
    /// keep passing its input through and put the reason here.
    /// </summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (_error == value)
                return;

            _error = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
