namespace ZvukEfekty.Core.Graph;

/// <summary>
/// A single sound-processing box ("krabička") in the patch graph.
/// </summary>
public abstract class ModuleBase
{
    protected ModuleBase(string name)
    {
        Id = Guid.NewGuid();
        TypeName = name;
    }

    public Guid Id { get; private set; }

    /// <summary>What the type is called — the name it stands under in the palette. Fixed: a box
    /// named something that is in no palette cannot be looked up, and whoever takes a block apart
    /// has no way to tell what is inside it or to build the same thing themselves.</summary>
    public string TypeName { get; }

    /// <summary>The dovětek: what THIS box does, where the type alone does not say it apart from
    /// its neighbours — `(vstup)` on one Gain and `(hlasitost)` on the other, `1` on the first of
    /// three Zpoždění. Empty on a module that is the only one of its type around, which is most of
    /// them. Set by <see cref="Modules.BlockCatalog"/> when a block needs to tell two boxes apart,
    /// and freely editable afterwards — it is the user's half of the name.</summary>
    public string Label { get; set; } = "";

    /// <summary>What the node's title bar and the property panel show: the type, then the dovětek
    /// when there is one. Composed rather than stored, so the type's own name cannot be edited away
    /// — the whole point of splitting the two.</summary>
    public string Name => Label.Trim().Length == 0 ? TypeName : $"{TypeName} {Label.Trim()}";
    public List<Port> Inputs { get; } = new();
    public List<Port> Outputs { get; } = new();
    public List<ModuleParameter> Parameters { get; } = new();

    /// <summary>Shape-valued state — see <see cref="ModuleCurve"/>. Separate from
    /// <see cref="Parameters"/> because a curve has no single value to modulate and no range to
    /// slide along, so every mechanism hanging off a parameter would be dead weight on it.
    ///
    /// Only a module that DEFINES a shape has one. A module that merely uses a shape takes it
    /// through a <see cref="CurveInputs">curve input</see> instead, so the drawing tool lives in
    /// one place rather than on every box that happens to want a curve.</summary>
    public List<ModuleCurve> Curves { get; } = new();

    /// <summary>Sockets for shapes supplied by another module — see <see cref="CurveInput"/>.
    /// Resolved by <see cref="ModuleGraph.RenderModule"/> before this module runs.</summary>
    public List<CurveInput> CurveInputs { get; } = new();

    /// <summary>Paths to files this module reads — see <see cref="ModuleFile"/>.</summary>
    public List<ModuleFile> Files { get; } = new();

    /// <summary>Text-valued state — see <see cref="ModuleText"/>. A list like the three around it,
    /// even though only one module has one and only ever needs a single entry: the kinds of non-knob
    /// state are read, written and rendered by the same shape of code in four places, and making
    /// this one the exception would be four asymmetries to remember.</summary>
    public List<ModuleText> Texts { get; } = new();

    /// <summary>Melody-valued state — see <see cref="ModuleSequence"/>. A list, like
    /// <see cref="Curves"/> and <see cref="Files"/>, even though nothing needs two yet: the three
    /// kinds of non-knob state are read, written and rendered by the same shape of code in four
    /// places, and making this one the exception would be four asymmetries to remember.</summary>
    public List<ModuleSequence> Sequences { get; } = new();

    /// <summary>Short explanation of what this module does, shown in the editor's property
    /// panel under the selected node's parameters.</summary>
    public virtual string Description => "Bez popisu.";

    /// <summary>Ready-made starting points the inspector offers for this module — see
    /// <see cref="ModulePreset"/>. Empty for modules where a preset would not mean anything.</summary>
    public virtual IReadOnlyList<ModulePreset> Presets => Array.Empty<ModulePreset>();

    /// <summary>
    /// Sets the knobs and texts the preset names; everything else stays. Values are clamped to the
    /// knob's range (a preset must never write what the inspector could not) and a knob switched
    /// to a note value is switched back to plain, since the preset's number is in its own units.
    /// Seeds are skipped even if named: a preset is a sound, not a particular random draw.
    /// </summary>
    public void ApplyPreset(ModulePreset preset)
    {
        foreach (var (name, value) in preset.Values)
        {
            var parameter = Parameters.FirstOrDefault(p => p.Name == name);
            if (parameter is null || parameter.IsSeed)
                continue;

            parameter.IsTempoSynced = false;
            parameter.Value = Math.Clamp(value, parameter.Min, parameter.Max);
        }

        if (preset.Texts is null)
            return;

        foreach (var (name, text) in preset.Texts)
        {
            var moduleText = Texts.FirstOrDefault(t => t.Name == name);
            if (moduleText is not null)
                moduleText.Text = text;
        }
    }

    /// <summary>
    /// Restores the Id an earlier instance had at save/clone time, so a module surviving a
    /// round-trip through PatchSerializer (patch load, or the per-voice clone in Voice) keeps
    /// publishing live-monitor data to the same slot its original instance used.
    /// </summary>
    internal void RestoreId(Guid id) => Id = id;

    protected Port AddInput(string name)
    {
        var port = new Port(this, name, PortDirection.Input);
        Inputs.Add(port);
        return port;
    }

    protected Port AddOutput(string name)
    {
        var port = new Port(this, name, PortDirection.Output);
        Outputs.Add(port);
        return port;
    }

    /// <summary>
    /// Adds a socket for a shape another module draws, plus the shape to use while nothing is
    /// wired to it. See <see cref="CurveInput"/> for why the fallback exists and why the socket
    /// conforms what arrives.
    /// </summary>
    /// <param name="fallback">What this module reads when the socket is empty — normally the shape
    /// it used to carry itself, so an unwired module behaves exactly as it did before.</param>
    /// <param name="output">The range and continuity this module needs out of a curve.</param>
    protected CurveInput AddCurveInput(string name, Curve fallback, CurveOutput output)
    {
        var port = new Port(this, name, PortDirection.Input, PortKind.Curve);
        Inputs.Add(port);

        var curveInput = new CurveInput(port, fallback, output);
        CurveInputs.Add(curveInput);
        return curveInput;
    }

    /// <summary>Adds an output that hands out a shape rather than samples.</summary>
    protected Port AddCurveOutput(string name)
    {
        var port = new Port(this, name, PortDirection.Output, PortKind.Curve);
        Outputs.Add(port);
        return port;
    }

    /// <summary>
    /// The shape this module publishes on <paramref name="port"/>, for a module defining one.
    /// Returns null everywhere else, which is what an unconnected socket falls back on.
    /// Called by <see cref="ModuleGraph.RenderModule"/>, never per sample.
    /// </summary>
    public virtual Curve? GetCurveOutput(Port port) => null;

    /// <summary>
    /// How long this module says the note is held in a batch render, at <paramref name="bpm"/>;
    /// 0 for a module that has no opinion, which is all of them but <c>NoteModule</c>.
    ///
    /// <para>A hook rather than a direct lookup because <see cref="ModuleGraph"/> knows nothing
    /// about the module types — the same shape as <see cref="GetCurveOutput"/>. What it is for:
    /// an ungated envelope cannot see the Nota module, so without this the length the user typed
    /// there would reach only the patches that happen to have Gate wired. See
    /// <see cref="ModuleGraph.BatchNoteHoldSeconds"/>.</para>
    /// </summary>
    public virtual double BatchNoteHoldSeconds(double bpm) => 0;

    /// <summary>
    /// Adds a knob to the property inspector. Numeric (non-enum) parameters also get a
    /// same-named input port, so another module's signal can drive that value at
    /// render time — see <see cref="GetModulatedValues"/>.
    /// </summary>
    /// <param name="modulatable">Set false for a value a per-sample signal cannot sensibly drive
    /// (a noise seed, say) — no input port is created for it.</param>
    /// <param name="randomizable">Set true to give the parameter a "roll a new one" button in the inspector.</param>
    /// <param name="isSeed">Set true only by <see cref="AddSeed"/>, which is what tags the knob as
    /// the entry point to a module's randomness.</param>
    /// <param name="tempoSync">Set for a knob that is a musical duration or speed, so the inspector
    /// offers note values for it and <see cref="GetModulatedValues"/> reads it off the beat grid.
    /// Says which unit the note value has to be converted into — see <see cref="TempoSyncKind"/>.
    /// Leave alone for pitches: an oscillator's Hz is not a tempo.</param>
    protected ModuleParameter AddParameter(string name, double value, double min, double max, string unit = "",
        string[]? options = null, bool modulatable = true, bool randomizable = false, bool isSeed = false,
        TempoSyncKind tempoSync = TempoSyncKind.None)
    {
        var parameter = new ModuleParameter(name, value, min, max, unit, options)
        {
            IsRandomizable = randomizable,
            IsSeed = isSeed,
            TempoSync = tempoSync,
        };
        Parameters.Add(parameter);

        if (options is null && modulatable)
            parameter.ModulationInput = AddInput(name);
        else
            // Nothing can drive this knob, so it has no depth. The constructor derives one from the
            // range without knowing that, and it was being written into every saved patch — a Seed
            // carrying "ModulationAmount: 200000" is dead data that also reads, to anyone opening
            // the file, like a claim that a seed can be modulated.
            parameter.ModulationAmount = 0;

        return parameter;
    }

    /// <summary>Adds a "Seed" knob plus the random source it drives, for a module whose output
    /// depends on randomness. Not modulatable — a per-sample signal driving a seed is meaningless —
    /// and randomizable, so the inspector offers a button to draw a new variation.</summary>
    protected SeededRandomSource AddSeed()
        => new(AddParameter("Seed", Random.Shared.Next(0, SeededRandomSource.MaxSeed),
            0, SeededRandomSource.MaxSeed, modulatable: false, randomizable: true, isSeed: true));

    /// <summary>
    /// Adds a curve to the inspector, and to what the patch file records.
    ///
    /// <paramref name="output"/> states what THIS module needs out of the curve — its range, and
    /// whether the ends have to meet. It is enforced on every assignment, so the curve can never
    /// hand back a value this module cannot use, however it was defined. The axis labels say what
    /// the two axes mean here, which is all an editor needs to serve a transfer function, a
    /// waveform and an envelope with one control.
    /// </summary>
    protected ModuleCurve AddCurve(string name, Curve curve, CurveOutput output, string xLabel, string yLabel)
    {
        var moduleCurve = new ModuleCurve(name, curve, output, xLabel, yLabel);
        Curves.Add(moduleCurve);
        return moduleCurve;
    }

    /// <summary>Adds a file slot to the inspector, and to what the patch file records.</summary>
    protected ModuleFile AddFile(string name, string dialogFilter)
    {
        var file = new ModuleFile(name, dialogFilter);
        Files.Add(file);
        return file;
    }

    /// <summary>Adds a text box to the inspector, and to what the patch file records.
    /// <paramref name="text"/> is what a freshly dropped module runs, so it should be something
    /// harmless and legible — a pass-through rather than a demonstration, since a module that
    /// starts out doing something clever is a module nobody reads before wiring it up.</summary>
    protected ModuleText AddText(string name, string text, string hint)
    {
        var moduleText = new ModuleText(name, text, hint);
        Texts.Add(moduleText);
        return moduleText;
    }

    /// <summary>Adds a step sequence to the inspector, and to what the patch file records.
    /// <paramref name="steps"/> is what a freshly dropped module plays, so it should be something
    /// audible — an empty sequencer is silent, which is the failure mode this project keeps
    /// mistaking for a bug.</summary>
    protected ModuleSequence AddSequence(string name, IEnumerable<SequenceStep> steps)
    {
        var sequence = new ModuleSequence(name, steps);
        Sequences.Add(sequence);
        return sequence;
    }

    /// <summary>
    /// Renders this module's outputs for one buffer.
    /// <paramref name="inputBuffers"/> contains one entry per input port that has
    /// an incoming connection; unconnected inputs are absent (treat as silence).
    /// </summary>
    public abstract IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers);

    /// <summary>
    /// Gets expensive, one-off work out of the way before rendering starts — decoding a file,
    /// building a table, anything that touches disk.
    ///
    /// Always called from a thread that can afford to be slow: the batch renderer calls it before
    /// a render, and <c>Audio.Voice</c> calls it right after cloning the patch, which happens on
    /// whichever thread pressed the key and never on the audio callback. That is the whole point.
    /// Doing the same work lazily inside <see cref="Process"/> would put it on the audio thread,
    /// where a single file read is many times a block's entire budget and comes out as a dropout.
    ///
    /// Must be safe to call repeatedly; implementations are expected to be cheap after the first
    /// time, usually because whatever they produced is cached somewhere shared. No-op by default.
    /// </summary>
    public virtual void Prepare()
    {
    }

    /// <summary>
    /// Clears any state carried between <see cref="Process"/> calls (oscillator phase, filter
    /// history, envelope stage, ...). Real-time voices call <see cref="Process"/> repeatedly on
    /// small blocks and rely on that state persisting across calls; a one-shot batch render
    /// instead calls this first so each Play/Export starts from silence, matching the old
    /// (always-fresh-instance) behavior. No-op for stateless modules.
    /// </summary>
    public virtual void ResetState()
    {
    }

    protected static float[] GetInputOrSilence(IReadOnlyDictionary<Port, float[]> inputBuffers, Port port, int sampleCount)
        => inputBuffers.TryGetValue(port, out var buffer) ? buffer : new float[sampleCount];

    /// <summary>The same, for a caller that has the context: the silence a live voice gets for an
    /// unwired input is borrowed rather than allocated — see <see cref="NewBuffer"/>.</summary>
    protected static float[] GetInputOrSilence(IReadOnlyDictionary<Port, float[]> inputBuffers, Port port, RenderContext ctx)
        => inputBuffers.TryGetValue(port, out var buffer) ? buffer : NewBuffer(ctx);

    /// <summary>
    /// A zeroed buffer one block long, for a module to write its output into.
    ///
    /// <para>Identical to the <c>new float[ctx.SampleCount]</c> it replaced — zeroed, and nobody
    /// else's — except that a live voice lends it from one block to the next instead of leaving it
    /// to the collector. A batch render has no pool and allocates exactly as before; see
    /// <see cref="BlockScratch"/> for why, and for the two buffers that must NOT come from here.</para>
    /// </summary>
    protected static float[] NewBuffer(RenderContext ctx) => ctx.Scratch?.RentCleared() ?? new float[ctx.SampleCount];

    /// <summary>
    /// Resolves a parameter to a per-sample array: its connected modulation signal, scaled by
    /// <see cref="ModuleParameter.ModulationAmount"/>, added to the base <see cref="ModuleParameter.Value"/>
    /// — or just the base value if unconnected.
    /// </summary>
    protected static float[] GetModulatedValues(ModuleParameter parameter, RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        // Every sample of it is written below, either way, so a buffer handed back dirty by a
        // previous block is as good as a fresh one — see BlockScratch.
        var result = ctx.Scratch?.Rent() ?? new float[ctx.SampleCount];
        // ResolveValue, not Value: a tempo-synced knob's number comes from its note value and the
        // context's BPM. Doing it here means every module reading a parameter through this funnel
        // gets sync for free -- see ModuleParameter.ResolveValue.
        var baseValue = (float)parameter.ResolveValue(ctx.Bpm);

        if (parameter.ModulationInput is not null && inputBuffers.TryGetValue(parameter.ModulationInput, out var modulation))
        {
            var amount = (float)parameter.ModulationAmount;
            for (var i = 0; i < ctx.SampleCount; i++)
                result[i] = baseValue + modulation[i] * amount;
        }
        else
        {
            for (var i = 0; i < ctx.SampleCount; i++)
                result[i] = baseValue;
        }

        return result;
    }
}
