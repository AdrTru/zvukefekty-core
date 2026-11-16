namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A module's socket for a shape supplied by another module — the consumer side of a
/// <see cref="PortKind.Curve"/> wire.
///
/// <para><b>Why the shape is not drawn here.</b> A curve used to be state on every module that
/// wanted one, which meant the same drawing tool appeared on four different boxes and the same
/// shape had to be redrawn in each of them. One <c>CurveModule</c> holds the drawing and any number
/// of consumers read it, which is what a patch cable is for.</para>
///
/// <para><b>Why it still owns a fallback.</b> Nothing is obliged to be patched in. Left empty, the
/// consumer uses the shape it was built with — a sine for a waveform, the identity for a transfer
/// function, an ADSR for an envelope — so an unwired module sounds like it always did rather than
/// going silent. The socket is an override, never a requirement.</para>
///
/// <para><b>Why it conforms.</b> Consumers disagree about what a curve must be: a waveform is read
/// round and round, so its ends have to meet or every period breaks with an audible click; an
/// envelope cannot go negative. One drawn shape can feed all of them because each socket conforms
/// the incoming curve to <see cref="Output"/> on arrival. What that costs is honest to state: a
/// curve drawn below zero reaches an envelope clamped to zero, so what you drew is not always
/// literally what a given consumer plays.</para>
/// </summary>
public sealed class CurveInput
{
    private readonly Curve _fallback;

    // The last incoming curve and what it conformed to. Conforming rebuilds a 4096-entry lookup
    // table, and the graph re-resolves every input before every block — a live voice renders
    // hundreds a second — so the result is kept until the source reference actually changes.
    private Curve? _acceptedSource;
    private Curve? _acceptedConformed;

    internal CurveInput(Port port, Curve fallback, CurveOutput output)
    {
        Port = port;
        Output = output;
        _fallback = output.Conform(fallback, output.TieEndpoints);
    }

    public Port Port { get; }

    /// <summary>Raised when the shape this socket hands out actually changes — a wire plugged in or
    /// pulled out. Lets a node's preview show what the module WILL use, not what it last rendered.
    ///
    /// Only ever raised on the thread that resolved the input. A live voice renders a clone of the
    /// patch (see <see cref="Audio.Voice"/>), so the instances the editor is bound to are touched
    /// by the UI thread alone and never by the audio callback.</summary>
    public event EventHandler? Changed;

    /// <summary>The range and continuity this consumer needs. Every incoming curve is conformed to
    /// it, so a module can read <see cref="Curve"/> without checking anything.</summary>
    public CurveOutput Output { get; }

    /// <summary>True while a curve is actually patched in, i.e. the shape is not the built-in one.
    /// For the inspector, so an empty socket can say so instead of looking identical to a full one.</summary>
    public bool IsConnected => _acceptedConformed is not null;

    /// <summary>The shape to use right now: whatever is patched in, conformed, or the built-in
    /// fallback. Never null, which is what lets a consumer read it without a branch.</summary>
    public Curve Curve => _acceptedConformed ?? _fallback;

    /// <summary>Called by <see cref="ModuleGraph.RenderModule"/> before the owning module runs.
    /// Null means nothing is wired to this socket.</summary>
    internal void Accept(Curve? incoming)
    {
        if (ReferenceEquals(incoming, _acceptedSource))
            return;

        _acceptedSource = incoming;
        _acceptedConformed = incoming is null ? null : Output.Conform(incoming, Output.TieEndpoints);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
