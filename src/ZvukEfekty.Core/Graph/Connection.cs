namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A wire dragged in the UI between an output port and an input port.
/// </summary>
public sealed class Connection
{
    public Connection(Port source, Port target)
    {
        if (source.Direction != PortDirection.Output)
            throw new ArgumentException("Connection source must be an output port.", nameof(source));
        if (target.Direction != PortDirection.Input)
            throw new ArgumentException("Connection target must be an input port.", nameof(target));

        Source = source;
        Target = target;
    }

    public Port Source { get; }
    public Port Target { get; }
}
