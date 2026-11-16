namespace ZvukEfekty.Core.Graph;

/// <summary>
/// The full patch: modules and the wires between them.
/// Rendering is batch/topological (no feedback loops) — see <see cref="RenderModule"/>.
/// </summary>
public sealed class ModuleGraph
{
    public List<ModuleBase> Modules { get; } = new();
    public List<Connection> Connections { get; } = new();

    // What RenderModule needs to know about the wiring, worked out once and kept until something
    // rewires the patch. Both used to be re-derived with LINQ over Connections for every module of
    // every block: a live voice renders some thirty blocks a second, and the wiring it renders can
    // never change at all (a voice holds its own clone, built at note-on — see Audio.Voice).
    private readonly Dictionary<ModuleBase, List<ModuleBase>> _orderCache = new();
    private readonly Dictionary<ModuleBase, SwarmLayout> _swarmCache = new();
    private Dictionary<Port, Connection>? _incomingByTarget;

    /// <summary>Every module in dependency order — what <see cref="RenderAll"/> runs. Only a swarm
    /// instance's graph asks for it, and that graph is never rewired.</summary>
    private List<ModuleBase>? _fullOrder;

    /// <summary>Called by everything that adds or removes a module or a wire. <see cref="Modules"/>
    /// and <see cref="Connections"/> are public lists, so a caller mutating them directly instead of
    /// going through <see cref="AddModule"/>/<see cref="RemoveModule"/>/<see cref="Connect"/>/
    /// <see cref="Disconnect"/> would leave these caches describing the patch as it used to be.
    /// Nothing in the app or the tools does; the editor builds every wire through Connect.</summary>
    private void WiringChanged()
    {
        _orderCache.Clear();
        _swarmCache.Clear();
        _incomingByTarget = null;
        _fullOrder = null;
    }

    /// <summary>The one wire into each input port, if any. An input takes a single wire (see
    /// <see cref="Connect"/>), so this is a complete answer rather than a first match.</summary>
    private Dictionary<Port, Connection> IncomingByTarget
    {
        get
        {
            if (_incomingByTarget is not null)
                return _incomingByTarget;

            var map = new Dictionary<Port, Connection>(Connections.Count);
            foreach (var connection in Connections)
                map[connection.Target] = connection;

            return _incomingByTarget = map;
        }
    }

    /// <summary>Wiring the graph refuses to render. A base rather than one exception per rule, so
    /// a caller showing the reason to the user catches every kind of it in one place.</summary>
    public class InvalidWiringException : Exception
    {
        public InvalidWiringException(string message)
            : base(message)
        {
        }
    }

    public sealed class CycleDetectedException : InvalidWiringException
    {
        public CycleDetectedException()
            : base("Graf modulů obsahuje cyklus – dávkové vykreslování zpětnou vazbu nepodporuje.")
        {
        }
    }

    public void AddModule(ModuleBase module)
    {
        Modules.Add(module);
        WiringChanged();
    }

    public void RemoveModule(ModuleBase module)
    {
        Modules.Remove(module);
        Connections.RemoveAll(c => c.Source.Owner == module || c.Target.Owner == module);
        WiringChanged();
    }

    /// <summary>
    /// Connects an output port to an input port. Each input accepts only one
    /// incoming wire, so any existing connection into <paramref name="target"/> is replaced.
    /// </summary>
    public Connection Connect(Port source, Port target)
    {
        if (source.Direction != PortDirection.Output || target.Direction != PortDirection.Input)
            throw new InvalidOperationException("Spoj musí vést z výstupního portu do vstupního.");
        if (source.Owner == target.Owner)
            throw new InvalidOperationException("Modul nelze propojit sám se sebou.");
        if (source.Kind != target.Kind)
            throw new InvalidOperationException("Zvukový a křivkový port nejdou propojit — viz PortKind.");

        Connections.RemoveAll(c => c.Target == target);
        var connection = new Connection(source, target);
        Connections.Add(connection);
        WiringChanged();
        return connection;
    }

    public void Disconnect(Connection connection)
    {
        Connections.Remove(connection);
        WiringChanged();
    }

    /// <summary>
    /// Hands every curve input the shape currently wired to it. <see cref="RenderModule"/> does this
    /// as it goes, but the editor needs it the moment a wire changes: otherwise a node's curve
    /// preview would keep showing the old shape until something was rendered.
    /// </summary>
    public void ResolveCurveInputs()
    {
        foreach (var module in Modules)
            ResolveCurveInputs(module);
    }

    private void ResolveCurveInputs(ModuleBase module)
    {
        foreach (var curveInput in module.CurveInputs)
        {
            var incoming = IncomingByTarget.GetValueOrDefault(curveInput.Port);
            curveInput.Accept(incoming?.Source.Owner.GetCurveOutput(incoming.Source));
        }
    }

    /// <summary>How long this patch says a note is held in a batch render, at <paramref name="bpm"/>,
    /// or 0 when nothing in it says. The longest wins when a patch splits the keyboard across two
    /// Nota modules: taking the shortest would cut the other row's tone off mid-sustain.
    ///
    /// <para>Public because the same number decides three things that have to agree — what an
    /// ungated envelope holds for (through <see cref="RenderContext.NoteHoldSeconds"/>), how long
    /// <c>ClipLengthFitter</c> thinks the patch needs, and what the editor warns about. Three
    /// separate readings of the same knob is how they would drift apart.</para></summary>
    public double BatchNoteHoldSeconds(double bpm)
    {
        var longest = 0.0;
        foreach (var module in Modules)
            longest = Math.Max(longest, module.BatchNoteHoldSeconds(bpm));
        return longest;
    }

    /// <summary>
    /// Renders <paramref name="targetModule"/> and everything feeding into it,
    /// in dependency order, and returns every produced output buffer.
    /// </summary>
    public IReadOnlyDictionary<Port, float[]> RenderModule(ModuleBase targetModule, RenderContext ctx)
    {
        var order = OrderFor(targetModule);
        var swarms = SwarmsFor(targetModule);
        var outputBuffers = new Dictionary<Port, float[]>();

        // Rewinds this pass's parameter buffers to the first one — see BlockScratch. A batch render
        // carries none and simply allocates as it goes.
        ctx.Scratch?.BeginPass(ctx.SampleCount);

        // Before the first module runs, not during: an ungated envelope reads this, and it may sit
        // anywhere in the order — including on a branch the Nota module does not feed, where
        // waiting for the Nota to publish it would leave it at zero.
        ctx.NoteHoldSeconds = BatchNoteHoldSeconds(ctx.Bpm);

        // Cleared per pass, not just set: the same context can be handed to RenderModule twice
        // (the zvuk tool renders a named module and then the output), and a flag left standing
        // from the first pass would tell the second that an envelope it never ran had already
        // taken care of the hold.
        ctx.NoteHoldConsumed = false;

        foreach (var module in order)
        {
            // The inside of a swarm is not rendered here at all: its clones are, one per sounding
            // instance, when the swarm's closing module comes up below.
            if (swarms.Inside.Contains(module))
                continue;

            // Curves first: a shape is resolved BEFORE the module runs, not sampled while it runs.
            // Nothing is read per sample here — the module is handed the shape and asks it for
            // values itself, which is the whole reason a curve port is not an audio port.
            ResolveCurveInputs(module);

            // By now everything the swarm's instances read from outside has been rendered — it is
            // all upstream of this module — and the summed instances become ordinary buffers on the
            // ports that wire into it.
            if (swarms.ByCloser.TryGetValue(module, out var closing))
                foreach (var plan in closing)
                    plan.Opener.Runner.Render(this, plan, ctx, outputBuffers);

            RenderOne(module, ctx, outputBuffers, injected: null);
        }

        return outputBuffers;
    }

    /// <summary>
    /// Renders every module of this graph, with some of its inputs fed from outside — the way one
    /// instance of a swarm is rendered (see <see cref="SwarmRunner"/>). This graph is a clone of
    /// the swarm's inside, so the wires coming into it from the rest of the patch are not in it;
    /// <paramref name="injected"/> carries their buffers, and <paramref name="curveSources"/> says
    /// which outside module a curve socket should read its shape from.
    /// </summary>
    internal Dictionary<Port, float[]> RenderAll(RenderContext ctx, IReadOnlyDictionary<Port, float[]> injected,
        IReadOnlyDictionary<Port, Port> curveSources)
    {
        _fullOrder ??= TopologicalOrder(Modules.ToHashSet());
        var outputBuffers = new Dictionary<Port, float[]>();

        ctx.Scratch?.BeginPass(ctx.SampleCount);
        ctx.NoteHoldConsumed = false;

        foreach (var module in _fullOrder)
        {
            ResolveCurveInputs(module);
            foreach (var curveInput in module.CurveInputs)
                if (curveSources.TryGetValue(curveInput.Port, out var source))
                    curveInput.Accept(source.Owner.GetCurveOutput(source));

            RenderOne(module, ctx, outputBuffers, injected);
        }

        return outputBuffers;
    }

    private void RenderOne(ModuleBase module, RenderContext ctx, Dictionary<Port, float[]> outputBuffers,
        IReadOnlyDictionary<Port, float[]>? injected)
    {
        var inputBuffers = new Dictionary<Port, float[]>();
        foreach (var input in module.Inputs)
        {
            if (injected is not null && injected.TryGetValue(input, out var fed))
                inputBuffers[input] = fed;
            else if (IncomingByTarget.TryGetValue(input, out var incoming)
                && outputBuffers.TryGetValue(incoming.Source, out var buffer))
                inputBuffers[input] = buffer;
        }

        foreach (var kvp in module.Process(ctx, inputBuffers))
            outputBuffers[kvp.Key] = kvp.Value;
    }

    /// <summary>
    /// Builds every swarm's instances ahead of rendering towards <paramref name="target"/>. A batch
    /// render does not need to call it — the first <see cref="RenderModule"/> builds them anyway —
    /// but a live voice does: there that first call is on the audio thread, and building the
    /// instances means deserializing the swarm's inside once per instance.
    /// </summary>
    public void PrepareSwarms(ModuleBase target)
    {
        foreach (var plan in SwarmsFor(target).Plans)
            plan.Opener.Runner.EnsureBuilt(this, plan);
    }

    private SwarmLayout SwarmsFor(ModuleBase target)
    {
        if (!_swarmCache.TryGetValue(target, out var layout))
            _swarmCache[target] = layout = SwarmLayout.Plan(this, target, OrderFor(target));

        return layout;
    }

    /// <summary>The render order for a target, worked out on the first block and kept for every
    /// block after — <see cref="WiringChanged"/> throws it away when the patch is rewired.</summary>
    private List<ModuleBase> OrderFor(ModuleBase target)
    {
        if (!_orderCache.TryGetValue(target, out var order))
            _orderCache[target] = order = TopologicalOrder(target);

        return order;
    }

    private List<ModuleBase> TopologicalOrder(ModuleBase target)
    {
        var relevant = new HashSet<ModuleBase>();
        CollectAncestors(target, relevant, new HashSet<ModuleBase>());
        return TopologicalOrder(relevant);
    }

    private List<ModuleBase> TopologicalOrder(HashSet<ModuleBase> relevant)
    {
        var inDegree = relevant.ToDictionary(m => m, _ => 0);
        foreach (var conn in Connections)
        {
            if (relevant.Contains(conn.Source.Owner) && relevant.Contains(conn.Target.Owner))
                inDegree[conn.Target.Owner]++;
        }

        var queue = new Queue<ModuleBase>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<ModuleBase>();

        while (queue.Count > 0)
        {
            var module = queue.Dequeue();
            result.Add(module);
            foreach (var conn in Connections.Where(c => c.Source.Owner == module && relevant.Contains(c.Target.Owner)))
            {
                if (--inDegree[conn.Target.Owner] == 0)
                    queue.Enqueue(conn.Target.Owner);
            }
        }

        if (result.Count != relevant.Count)
            throw new CycleDetectedException();

        return result;
    }

    private void CollectAncestors(ModuleBase module, HashSet<ModuleBase> visited, HashSet<ModuleBase> visiting)
    {
        if (visited.Contains(module))
            return;
        if (!visiting.Add(module))
            throw new CycleDetectedException();

        foreach (var conn in Connections.Where(c => c.Target.Owner == module))
            CollectAncestors(conn.Source.Owner, visited, visiting);

        visiting.Remove(module);
        visited.Add(module);
    }
}
