namespace ZvukEfekty.Core.Graph;

public enum PortDirection
{
    Input,
    Output
}

/// <summary>
/// What travels down a wire. Audio ports carry a per-sample <c>float[]</c>; curve ports carry a
/// <see cref="Curve"/> — a SHAPE, not a signal, resolved once before a render rather than sampled.
///
/// The distinction has to exist at the port because the two cannot be joined: there is no sensible
/// reading of "a buffer of samples plugged in where a shape is expected". <see cref="ModuleGraph.Connect"/>
/// refuses the mismatch rather than letting it fail quietly at render time.
/// </summary>
public enum PortKind
{
    Audio,
    Curve
}

/// <summary>
/// A single connection point on a module ("krabička"). What it carries is its <see cref="Kind"/>.
/// </summary>
public sealed class Port
{
    public Port(ModuleBase owner, string name, PortDirection direction, PortKind kind = PortKind.Audio)
    {
        Owner = owner;
        Name = name;
        Direction = direction;
        Kind = kind;
    }

    public ModuleBase Owner { get; }
    public string Name { get; }
    public PortDirection Direction { get; }
    public PortKind Kind { get; }

    public override string ToString() => $"{Owner.Name}.{Name}";
}
