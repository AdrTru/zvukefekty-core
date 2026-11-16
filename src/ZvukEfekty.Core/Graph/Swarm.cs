using ZvukEfekty.Core.Persistence;

namespace ZvukEfekty.Core.Graph;

/// <summary>
/// The module that opens a swarm — <c>Modules.SwarmModule</c>. An interface rather than a lookup by
/// type, because <see cref="ModuleGraph"/> knows nothing about the module types (the same reason
/// <see cref="ModuleBase.GetCurveOutput"/> is a hook).
///
/// <para>A swarm is a stretch of the patch that is rendered once per trigger instead of once: every
/// module downstream of the opener and upstream of its closer is cloned into a pool of instances,
/// each trigger starts a free one, and the closer hears them summed. What an instance reads through
/// the opener's own outputs is frozen at its trigger; a wire into the inside from anywhere else is
/// live and shared. See <c>todo.md</c> 17.</para>
/// </summary>
internal interface ISwarmOpener
{
    SwarmRunner Runner { get; }

    /// <summary>How many instances may sound at once, not counting those fading out after being
    /// stolen.</summary>
    int Capacity { get; }

    /// <summary>What a trigger does when <see cref="Capacity"/> instances are sounding: true takes
    /// the oldest away under a short fade, false drops the trigger.</summary>
    bool StealWhenFull { get; }

    /// <summary>What the summed instances are multiplied by before the closer hears them.</summary>
    double OutputGain { get; }

    /// <summary>The outputs carrying a value frozen at the trigger, in the order of
    /// <see cref="SwarmPass.Values"/>.</summary>
    IReadOnlyList<Port> ValueOutputs { get; }

    /// <summary>The output carrying the instance's own envelope.</summary>
    Port EnvelopeOutput { get; }

    /// <summary>What the opener was handed in the current pass, or null before its first one.</summary>
    SwarmPass? Pass { get; }
}

/// <summary>The module that closes a swarm — <c>Modules.SwarmEndModule</c>. A marker only: the
/// summing happens in <see cref="SwarmRunner"/> before the closer runs, so the closer itself just
/// passes on what it is given. A swarm without one closes at the render target (the Output).</summary>
internal interface ISwarmCloser
{
}

/// <summary>The opener's inputs for one pass, read per sample by <see cref="SwarmRunner"/> at the
/// sample a trigger lands on. Buffers belong to the pass, like every other one in it.</summary>
internal sealed class SwarmPass
{
    public float[]? Trigger;
    public float[]?[] Values = Array.Empty<float[]?>();
    public float[] Attack = Array.Empty<float>();
    public float[] Decay = Array.Empty<float>();
    public float[] Sustain = Array.Empty<float>();
    public float[] Hold = Array.Empty<float>();
    public float[] Release = Array.Empty<float>();
}

/// <summary>Where one swarm starts and stops, worked out from the wiring.</summary>
internal sealed class SwarmPlan
{
    public SwarmPlan(ISwarmOpener opener, ModuleBase openerModule, ModuleBase closer, List<ModuleBase> inside,
        List<Connection> entries, List<Connection> exits)
    {
        Opener = opener;
        OpenerModule = openerModule;
        Closer = closer;
        Inside = inside;
        Entries = entries;
        Exits = exits;
    }

    public ISwarmOpener Opener { get; }
    public ModuleBase OpenerModule { get; }
    public ModuleBase Closer { get; }

    /// <summary>The modules every instance gets its own copy of, in the graph's order.</summary>
    public List<ModuleBase> Inside { get; }

    /// <summary>Wires into the inside from the opener or from anywhere else.</summary>
    public List<Connection> Entries { get; }

    /// <summary>Wires from the inside (or straight from the opener) into the closer.</summary>
    public List<Connection> Exits { get; }
}

/// <summary>Every swarm on the way to one render target.</summary>
internal sealed class SwarmLayout
{
    private static readonly SwarmLayout None = new(new List<SwarmPlan>());

    private SwarmLayout(List<SwarmPlan> plans)
    {
        Plans = plans;
        Inside = plans.SelectMany(p => p.Inside).ToHashSet();
        ByCloser = plans.GroupBy(p => p.Closer).ToDictionary(g => g.Key, g => g.ToList());
    }

    public List<SwarmPlan> Plans { get; }
    public HashSet<ModuleBase> Inside { get; }
    public Dictionary<ModuleBase, List<SwarmPlan>> ByCloser { get; }

    /// <summary>
    /// Finds each opener among <paramref name="relevant"/> (what the target needs) and its inside:
    /// everything downstream of the opener that is also upstream of its closer. Refuses wiring that
    /// would leave it unclear which instance a signal belongs to, rather than rendering it some
    /// arbitrary way.
    /// </summary>
    public static SwarmLayout Plan(ModuleGraph graph, ModuleBase target, IReadOnlyList<ModuleBase> relevant)
    {
        if (!relevant.Any(m => m is ISwarmOpener))
            return None;

        var needed = relevant.ToHashSet();
        var plans = new List<SwarmPlan>();

        foreach (var openerModule in relevant)
        {
            if (openerModule is not ISwarmOpener opener)
                continue;

            var downstream = Reach(graph, openerModule, forward: true);
            var closers = downstream.Where(m => m is ISwarmCloser).ToList();
            if (closers.Count > 1)
                throw new ModuleGraph.InvalidWiringException(
                    $"Roj „{openerModule.Name}“ vede do víc než jednoho Konce roje — nebylo by jasné, kde končí.");

            var closer = closers.Count == 1 ? closers[0] : target;
            var upstreamOfCloser = Reach(graph, closer, forward: false);
            var insideSet = downstream.Where(upstreamOfCloser.Contains).ToHashSet();
            insideSet.Remove(closer);

            if (insideSet.Any(m => m is ISwarmOpener))
                throw new ModuleGraph.InvalidWiringException(
                    $"Roj „{openerModule.Name}“ obsahuje další Roj — roj v roji zatím nejde.");

            var entries = new List<Connection>();
            var exits = new List<Connection>();
            foreach (var connection in graph.Connections)
            {
                var from = connection.Source.Owner;
                var to = connection.Target.Owner;
                var fromSwarm = from == openerModule || insideSet.Contains(from);

                if (insideSet.Contains(to) && !insideSet.Contains(from))
                    entries.Add(connection);
                else if (fromSwarm && to == closer)
                    exits.Add(connection);
                else if (fromSwarm && !insideSet.Contains(to) && needed.Contains(to))
                    throw new ModuleGraph.InvalidWiringException(
                        $"Z roje „{openerModule.Name}“ vede drát do „{to.Name}“ mimo jeho konec — " +
                        "nebylo by jasné, která instance ho krmí. Vyveďte ho přes Konec roje.");
            }

            var inside = graph.Modules.Where(insideSet.Contains).ToList();
            plans.Add(new SwarmPlan(opener, openerModule, closer, inside, entries, exits));
        }

        return new SwarmLayout(plans);
    }

    private static HashSet<ModuleBase> Reach(ModuleGraph graph, ModuleBase start, bool forward)
    {
        var seen = new HashSet<ModuleBase>();
        var queue = new Queue<ModuleBase>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var module = queue.Dequeue();
            foreach (var connection in graph.Connections)
            {
                var (from, to) = forward
                    ? (connection.Source.Owner, connection.Target.Owner)
                    : (connection.Target.Owner, connection.Source.Owner);
                if (from == module && seen.Add(to))
                    queue.Enqueue(to);
            }
        }

        seen.Remove(start);
        return seen;
    }
}

/// <summary>
/// The pool of instances behind one opener, and the rendering of them. Owned by the opener module,
/// so its state lives exactly as long as the module does — and a live voice, which clones the whole
/// patch, gets a pool of its own with it.
///
/// <para><b>CPU.</b> The instances are cloned up front (<see cref="EnsureBuilt"/>), never on the
/// audio thread; a trigger only resets one and reseeds it. Only a sounding instance is rendered, and
/// only up to the sample its envelope ends on — a batch render is one block the length of the whole
/// clip, and rendering every particle to the end of it made the render time grow with the square of
/// the clip's length.</para>
///
/// <para><b>Full pool.</b> By default a trigger that finds <see cref="ISwarmOpener.Capacity"/>
/// instances sounding takes the oldest away under a 5 ms fade. Dropping the trigger instead (the
/// other setting) makes a dense swarm pulse: the instances free up in the rhythm they started in,
/// so once the pool fills the starts bunch up and the bunch repeats every particle length. A fading
/// instance cannot be restarted until it is silent, so the pool holds a few spare ones for the new
/// particle; a trigger that finds even those taken is dropped.</para>
///
/// <para><b>Timing.</b> An instance starts on the trigger's own sample, not on the next block.
/// Rounded to a 30 ms block, irregular triggers would come out on a 33 Hz grid you can hear. An
/// instance is rendered lazily, in stretches: up to the end of the block, up to the sample it is
/// stolen on, or up to its end before it is started again.</para>
/// </summary>
internal sealed class SwarmRunner
{
    /// <summary>Step between one instance's seed offset and the next — the same prime
    /// <c>Audio.Voice</c> uses for the same job, for the same reason.</summary>
    private const int SeedStride = 104_729;

    /// <summary>The engine's triggers are 0-or-1; see <c>SampleHoldModule</c>.</summary>
    private const double TriggerThreshold = 0.5;

    /// <summary>Long enough not to click, short enough that a stolen particle is gone before the
    /// next steal at 200 triggers a second.</summary>
    private const double StealFadeSeconds = 0.005;

    /// <summary>Instances beyond the capacity, for new particles while stolen ones fade. At the
    /// trigger module's 200 a second with a 5 ms fade about one is fading at a time.</summary>
    private const int StealSpares = 4;

    private SwarmPlan? _builtFor;
    private bool _stale = true;
    private SwarmInstance[] _pool = Array.Empty<SwarmInstance>();
    private double _previousTrigger;
    private long _spawned;

    /// <summary>The sample the current block starts on, counted from the last <see cref="Reset"/>.</summary>
    private long _clock;

    /// <summary>Throws the pool away, so the next render clones the inside again — called from the
    /// opener's <c>Prepare</c>, which runs before every batch render, so knob changes inside the
    /// swarm are heard on the next Play.</summary>
    public void Invalidate() => _stale = true;

    /// <summary>Silences every instance and restarts the seed sequence, so a batch render of the
    /// same patch comes out the same twice.</summary>
    public void Reset()
    {
        _previousTrigger = 0;
        _spawned = 0;
        _clock = 0;
        foreach (var instance in _pool)
            instance.Silence();
    }

    public void EnsureBuilt(ModuleGraph graph, SwarmPlan plan)
    {
        if (!_stale && ReferenceEquals(_builtFor, plan))
            return;

        var json = PatchSerializer.Serialize(plan.Inside, graph.Connections,
            new Dictionary<ModuleBase, (double X, double Y)>(), new PatchTiming { DurationSeconds = 0 });

        var capacity = Math.Max(1, plan.Opener.Capacity);
        var pool = new SwarmInstance[capacity + (plan.Opener.StealWhenFull ? StealSpares : 0)];
        for (var i = 0; i < pool.Length; i++)
            pool[i] = new SwarmInstance(plan, PatchSerializer.Deserialize(json));

        _pool = pool;
        _builtFor = plan;
        _stale = false;
        Reset();
    }

    public void Render(ModuleGraph graph, SwarmPlan plan, RenderContext ctx, Dictionary<Port, float[]> outputBuffers)
    {
        EnsureBuilt(graph, plan);

        var exitSources = _pool[0].ExitSources;
        var sums = new float[exitSources.Count][];
        for (var i = 0; i < sums.Length; i++)
            sums[i] = ctx.Scratch?.RentCleared() ?? new float[ctx.SampleCount];

        var blockEnd = _clock + ctx.SampleCount;
        var pass = plan.Opener.Pass;
        var trigger = pass?.Trigger;
        for (var i = 0; trigger is not null && i < ctx.SampleCount; i++)
        {
            if (trigger[i] >= TriggerThreshold && _previousTrigger < TriggerThreshold)
                Spawn(plan, pass!, ctx, i, outputBuffers, sums);

            _previousTrigger = trigger[i];
        }

        foreach (var instance in _pool)
            instance.RenderTo(ctx, _clock, blockEnd, outputBuffers, sums);

        _clock = blockEnd;

        // Skipped at 1 rather than multiplied, so a swarm without normalization renders bit for bit
        // as it did before there was any.
        var gain = (float)plan.Opener.OutputGain;
        for (var i = 0; i < sums.Length; i++)
        {
            if (gain != 1)
                for (var s = 0; s < ctx.SampleCount; s++)
                    sums[i][s] *= gain;
            outputBuffers[exitSources[i]] = sums[i];
        }
    }

    private void Spawn(SwarmPlan plan, SwarmPass pass, RenderContext ctx, int sample,
        Dictionary<Port, float[]> outputBuffers, float[][] sums)
    {
        var now = _clock + sample;

        SwarmInstance? oldest = null;
        var sounding = 0;
        foreach (var instance in _pool)
        {
            if (instance.EndsAt <= now || instance.IsFading)
                continue;
            sounding++;
            if (oldest is null || instance.StartedAt < oldest.StartedAt)
                oldest = instance;
        }

        if (sounding >= Math.Max(1, plan.Opener.Capacity))
        {
            if (!plan.Opener.StealWhenFull)
                return;

            // Rendered up to here with the envelope it had; the fade applies from here on.
            oldest!.RenderTo(ctx, _clock, now, outputBuffers, sums);
            oldest.FadeOut(now, (long)Math.Ceiling(StealFadeSeconds * ctx.SampleRate));
        }

        var free = Array.Find(_pool, instance => instance.EndsAt <= now);
        if (free is null)
            return;

        // What is left of its previous particle, before that is overwritten.
        free.RenderTo(ctx, _clock, now, outputBuffers, sums);
        free.Start(pass, sample, now, ctx.SampleRate, _spawned++ * SeedStride);
    }
}

/// <summary>One clone of a swarm's inside, plus what it froze at its trigger.</summary>
internal sealed class SwarmInstance
{
    private enum SourceKind { Outside, Value, Envelope, Inner }

    private readonly record struct Feed(Port Target, SourceKind Kind, Port? Source, int Index);

    private readonly ModuleGraph _graph;
    private readonly List<Feed> _entries = new();
    private readonly List<Feed> _exits = new();
    private readonly Dictionary<Port, Port> _curveSources = new();
    private readonly List<(ModuleParameter Parameter, double Base)> _seeds = new();
    private readonly Dictionary<Port, float[]> _injected = new();
    private readonly BlockScratch _scratch = new();

    private double[] _values = Array.Empty<double>();
    private float[][] _valueBuffers = Array.Empty<float[]>();
    private float[] _envelope = Array.Empty<float>();
    private float[][] _wholeValueBuffers = Array.Empty<float[]>();
    private float[] _wholeEnvelope = Array.Empty<float>();
    private double _attack, _decay, _sustain, _hold, _release;
    private long _gateLength;
    private long _fadeStart = long.MaxValue;
    private long _fadeLength;

    public SwarmInstance(SwarmPlan plan, PatchLoadResult clone)
    {
        _graph = clone.Graph;

        // The serializer numbered the inside in the order it was given, so file id i IS Inside[i].
        var cloneOf = new Dictionary<ModuleBase, ModuleBase>();
        for (var i = 0; i < plan.Inside.Count; i++)
            cloneOf[plan.Inside[i]] = clone.ModulesByFileId[i];

        foreach (var entry in plan.Entries)
        {
            var target = cloneOf[entry.Target.Owner].Inputs.First(p => p.Name == entry.Target.Name);
            if (entry.Target.Kind == PortKind.Curve)
                _curveSources[target] = entry.Source;
            else
                _entries.Add(FeedFrom(plan, entry.Source, target, cloneOf));
        }

        // One sum per source port, however many closer inputs it feeds.
        foreach (var source in plan.Exits.Select(e => e.Source).Distinct())
        {
            _exits.Add(FeedFrom(plan, source, source, cloneOf));
            ExitSources.Add(source);
        }

        foreach (var module in _graph.Modules)
        {
            foreach (var parameter in module.Parameters.Where(p => p.IsSeed))
                _seeds.Add((parameter, parameter.Value));
        }

        _graph.ResolveCurveInputs();
        foreach (var module in _graph.Modules)
            module.Prepare();

        _values = new double[plan.Opener.ValueOutputs.Count];
    }

    /// <summary>The original ports the closer reads, in the order this instance adds into them.</summary>
    public List<Port> ExitSources { get; } = new();

    /// <summary>The sample (on the runner's clock) the current particle started on.</summary>
    public long StartedAt { get; private set; }

    /// <summary>The sample its envelope ends on — from here the instance is free, whether or not it
    /// has been rendered that far yet. Free by this and not by having been rendered, or the cap
    /// would hold in live play and not in a batch render, where the block is the whole clip.</summary>
    public long EndsAt { get; private set; }

    /// <summary>How far the current particle has been rendered.</summary>
    private long RenderedTo { get; set; }

    public bool IsFading => _fadeStart != long.MaxValue;

    public void Silence()
    {
        StartedAt = EndsAt = RenderedTo = 0;
        _fadeStart = long.MaxValue;
    }

    /// <summary>Ends the particle with a linear fade from <paramref name="now"/>, which it must not
    /// have been rendered past.</summary>
    public void FadeOut(long now, long fadeSamples)
    {
        _fadeStart = now - StartedAt;
        _fadeLength = Math.Max(1, fadeSamples);
        EndsAt = Math.Min(EndsAt, now + _fadeLength);
    }

    private static Feed FeedFrom(SwarmPlan plan, Port source, Port target, Dictionary<ModuleBase, ModuleBase> cloneOf)
    {
        if (source.Owner == plan.OpenerModule)
        {
            return source == plan.Opener.EnvelopeOutput
                ? new Feed(target, SourceKind.Envelope, null, 0)
                : new Feed(target, SourceKind.Value, null, plan.Opener.ValueOutputs.ToList().IndexOf(source));
        }

        return cloneOf.TryGetValue(source.Owner, out var clone)
            ? new Feed(target, SourceKind.Inner, clone.Outputs.First(p => p.Name == source.Name), 0)
            : new Feed(target, SourceKind.Outside, source, 0);
    }

    /// <summary>Freezes the opener's values and envelope at <paramref name="sample"/>, gives the
    /// clone fresh seeds and resets it — a new particle, not the last one carried on.</summary>
    public void Start(SwarmPass pass, int sample, long now, int sampleRate, long seedOffset)
    {
        for (var i = 0; i < _values.Length; i++)
            _values[i] = i < pass.Values.Length && pass.Values[i] is { } values ? values[sample] : 0;

        _attack = Math.Max(0, pass.Attack[sample]);
        _decay = Math.Max(0, pass.Decay[sample]);
        _sustain = Math.Clamp(pass.Sustain[sample], 0, 1);
        _hold = Math.Max(0, pass.Hold[sample]);
        _release = Math.Max(0, pass.Release[sample]);
        _gateLength = (long)Math.Ceiling((_attack + _decay + _hold) * sampleRate);
        StartedAt = RenderedTo = now;
        EndsAt = now + (long)Math.Ceiling((_attack + _decay + _hold + _release) * sampleRate);
        _fadeStart = long.MaxValue;

        foreach (var (parameter, seed) in _seeds)
            parameter.Value = (seed + seedOffset) % SeededRandomSource.MaxSeed;
        foreach (var module in _graph.Modules)
            module.ResetState();
    }

    /// <summary>Renders the particle from where it got to up to <paramref name="upTo"/> or its end,
    /// whichever comes first, and adds it, under its envelope, into <paramref name="sums"/>. Both
    /// samples are on the runner's clock; <paramref name="blockStart"/> places them in the block.</summary>
    public void RenderTo(RenderContext ctx, long blockStart, long upTo, Dictionary<Port, float[]> outside, float[][] sums)
    {
        var to = Math.Min(upTo, EndsAt);
        if (to <= RenderedTo)
            return;

        var offset = (int)(RenderedTo - blockStart);
        var length = (int)(to - RenderedTo);
        var elapsed = RenderedTo - StartedAt;
        var whole = offset == 0 && length == ctx.SampleCount;

        // A stretch shorter than the block has its own length, and BlockScratch drops its buffers
        // whenever the length changes — so that stretch allocates, and the whole blocks reuse.
        var instanceCtx = new RenderContext(ctx.SampleRate, length, ctx.Bpm)
        {
            Scratch = whole ? _scratch : null,
            LiveKeyDown = elapsed < _gateLength,
        };

        FillOwnBuffers(length, whole, elapsed, ctx.SampleRate);

        _injected.Clear();
        foreach (var entry in _entries)
        {
            var buffer = BufferOf(entry, outside, null, offset, length);
            if (buffer is not null)
                _injected[entry.Target] = buffer;
        }

        var rendered = _graph.RenderAll(instanceCtx, _injected, _curveSources);

        for (var e = 0; e < _exits.Count; e++)
        {
            var buffer = BufferOf(_exits[e], outside, rendered, offset, length);
            if (buffer is null)
                continue;

            var sum = sums[e];
            for (var i = 0; i < length; i++)
                sum[offset + i] += buffer[i] * _envelope[i];
        }

        RenderedTo = to;
    }

    private float[]? BufferOf(Feed feed, Dictionary<Port, float[]> outside, Dictionary<Port, float[]>? rendered,
        int offset, int length)
    {
        switch (feed.Kind)
        {
            case SourceKind.Value:
                return feed.Index >= 0 && feed.Index < _valueBuffers.Length ? _valueBuffers[feed.Index] : null;
            case SourceKind.Envelope:
                return _envelope;
            case SourceKind.Inner:
                return rendered is not null && rendered.TryGetValue(feed.Source!, out var inner) ? inner : null;
            default:
                if (!outside.TryGetValue(feed.Source!, out var shared))
                    return null;
                if (offset == 0 && length == shared.Length)
                    return shared;

                var slice = new float[length];
                Array.Copy(shared, offset, slice, 0, length);
                return slice;
        }
    }

    /// <summary>Whole blocks reuse one set of buffers; a shorter stretch (the start or end of a
    /// particle) gets a throwaway set, so it does not make the next whole block reallocate.</summary>
    private void FillOwnBuffers(int length, bool whole, long elapsed, int sampleRate)
    {
        if (!whole)
        {
            _envelope = new float[length];
            _valueBuffers = _values.Select(_ => new float[length]).ToArray();
        }
        else
        {
            if (_wholeEnvelope.Length != length)
            {
                _wholeEnvelope = new float[length];
                _wholeValueBuffers = _values.Select(_ => new float[length]).ToArray();
            }

            _envelope = _wholeEnvelope;
            _valueBuffers = _wholeValueBuffers;
        }

        for (var v = 0; v < _values.Length; v++)
            Array.Fill(_valueBuffers[v], (float)_values[v]);

        for (var i = 0; i < length; i++)
        {
            var sample = elapsed + i;
            var level = EnvelopeAt(sample / (double)sampleRate);
            if (sample >= _fadeStart)
                level *= Math.Max(0, 1 - (sample - _fadeStart) / (double)_fadeLength);
            _envelope[i] = (float)level;
        }
    }

    /// <summary>Linear attack, decay to sustain, the plateau for Držení, linear release — the same
    /// stages <c>EnvelopeModule</c> plays with nothing in its Gate.</summary>
    private double EnvelopeAt(double t)
    {
        if (t < _attack)
            return t / _attack;
        t -= _attack;
        if (t < _decay)
            return 1 - (1 - _sustain) * t / _decay;
        t -= _decay;
        if (t < _hold)
            return _sustain;
        t -= _hold;
        if (t < _release)
            return _sustain * (1 - t / _release);
        return 0;
    }
}
