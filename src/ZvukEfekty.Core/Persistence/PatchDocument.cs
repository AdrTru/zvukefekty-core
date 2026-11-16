using System.Text.Json.Serialization;

namespace ZvukEfekty.Core.Persistence;

/// <summary>Serializable snapshot of a whole patch: every module, its parameters, canvas position, and wiring.</summary>
public sealed class PatchDocument
{
    public int Version { get; set; } = 1;
    public double DurationSeconds { get; set; } = 1.5;

    // Tempo fields, absent from files written before tempo-locked lengths existed. Those files
    // deserialize to the defaults below and stay free-running, which is what they always were.
    public double Bpm { get; set; } = 128.0;
    public int Bars { get; set; } = 4;
    public bool IsTempoLocked { get; set; }

    public List<ModuleRecord> Modules { get; set; } = new();
    public List<ConnectionRecord> Connections { get; set; } = new();

    /// <summary>Organisational frames drawn on the canvas. Layout only — nothing here reaches the
    /// render, so a file without them sounds exactly the same. Null rather than an empty list, and
    /// skipped when null, so adding this rewrote no existing patch file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GroupRecord>? Groups { get; set; }
}

/// <summary>A titled frame on the canvas. It references no modules: what a frame holds is worked
/// out from geometry when the patch is opened, so moving a module in or out of one needs nothing
/// recorded here.</summary>
public sealed class GroupRecord
{
    public string Title { get; set; } = "";

    /// <summary>The note under the title. Null when the frame has none, and left out of the file
    /// then, like every other optional field in this format.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Whether the frame was left folded up. Written only when true.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Collapsed { get; set; }

    /// <summary>Whether the frame's contents were frozen: locked, no module joins it or leaves it
    /// however anything is dragged. Written only when true.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Locked { get; set; }

    /// <summary>
    /// Which modules' readouts a folded frame keeps showing, by <see cref="ModuleRecord.InstanceId"/>.
    /// Null when the frame was never told, which means "every Vizualizace module in it" — so a patch
    /// that leaves the default alone carries nothing about this. An EMPTY list is a different answer:
    /// a frame deliberately told to show none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Guid>? Visuals { get; set; }

    /// <summary>
    /// The frame's own connectors — which inner ports are its outside. Null for a frame that
    /// declares none, which is every frame written before this existed.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<GroupPortRecord>? Ports { get; set; }
}

/// <summary>
/// One connector on a frame's edge. It is an ALIAS, not a port of its own: wiring into it makes an
/// ordinary connection to the inner port it names, which is why nothing about rendering or about
/// the modules section of this format has to know frames exist.
/// </summary>
public sealed class GroupPortRecord
{
    /// <summary>What the connector is called on the frame — the block's word for it, not the inner
    /// module's. Half the point of declaring a connector is that it can be called `Vstup` or
    /// `Sidechain` instead of `In` after whichever box happens to sit behind it.</summary>
    public string Name { get; set; } = "";

    /// <summary>Which side of the frame it sits on.</summary>
    public bool IsInput { get; set; }

    /// <summary>The inner module, by <see cref="ModuleRecord.Id"/> — the same handle
    /// <see cref="ConnectionRecord"/> uses, so a frame's connectors survive whatever the modules'
    /// <see cref="ModuleRecord.InstanceId"/>s do. That matters for a block file meant to be
    /// inserted twice: it can be written without InstanceIds so each insert gets fresh ones, and
    /// these references still hold.</summary>
    public int ModuleId { get; set; }

    /// <summary>Name of that module's port.</summary>
    public string Port { get; set; } = "";
}

public sealed class ModuleRecord
{
    /// <summary>Index used only to cross-reference <see cref="ConnectionRecord"/> within this document.</summary>
    public int Id { get; set; }

    /// <summary>CLR type name (e.g. "OscillatorModule"), matched against <see cref="Modules.ModuleCatalog"/> on load.</summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// The dovětek — see <see cref="Graph.ModuleBase.Label"/>. Null, and left out of the file, for
    /// a module that has none, which is most of them. Only this half is stored: the other half is
    /// the type's own name, which the type already knows and which nothing may overwrite.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>
    /// The whole label as files written before the dovětek existed carry it — read, never written.
    /// Those files store the finished name (`Gain (vstup)`), so loading strips the type's own name
    /// off the front and keeps the rest as <see cref="Label"/>.
    ///
    /// <para>A name that does NOT start with the type's name — the block labels of the day before,
    /// `Hlava 1` or `Vstup (citlivost)` — becomes the dovětek whole, so the box reads
    /// `Zpoždění Hlava 1`. It gains the type it was missing and says the same as before, and the
    /// dovětek can be trimmed by hand.</para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>The module's <see cref="Graph.ModuleBase.Id"/>, so a clone made via this document
    /// (a saved/loaded file, or the per-voice clone in Audio.Voice) can be matched back to the
    /// original instance — e.g. so a Scope/VU meter node keeps publishing to the same live-monitor
    /// slot its original instance used. Empty (default) on older files, which just means the loaded
    /// module keeps the fresh Id it was constructed with.</summary>
    public Guid InstanceId { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public List<ParameterRecord> Parameters { get; set; } = new();

    /// <summary>Shape-valued state (see <see cref="Graph.ModuleCurve"/>), absent from files written
    /// before curves existed and from every module that has none.
    ///
    /// Null rather than an empty list, and skipped when null, so adding this did not rewrite a
    /// single existing patch file: a module without curves serializes exactly as it did before,
    /// which keeps file diffs about what actually changed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CurveRecord>? Curves { get; set; }

    /// <summary>File paths this module reads (see <see cref="Graph.ModuleFile"/>). Same null-when-
    /// empty treatment as <see cref="Curves"/>, for the same reason.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FileRecord>? Files { get; set; }

    /// <summary>Written melodies (see <see cref="Graph.ModuleSequence"/>). Same null-when-empty
    /// treatment as the two above.
    ///
    /// <para>Unlike <see cref="Curves"/> this is written even when it still holds what the module
    /// was constructed with. A default curve is a shape nobody chose and leaving it out keeps
    /// patch files about decisions; a sequence IS the module's content, and a Sequencer whose
    /// notes were left out of the file would load back playing something else.</para></summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SequenceRecord>? Sequences { get; set; }

    /// <summary>Text this module runs (see <see cref="Graph.ModuleText"/>). Written even when it
    /// still holds the default, for the same reason as <see cref="Sequences"/>: it is the module's
    /// content, and a formula left out of the file would load back computing something else.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TextRecord>? Texts { get; set; }
}

public sealed class TextRecord
{
    public string Name { get; set; } = "";

    /// <summary>The text itself. Not trimmed or validated here — whether it means anything is the
    /// module's business, and a file holding an unparseable formula has to load so that the reason
    /// can be shown in the inspector rather than swallowed at load time.</summary>
    public string Text { get; set; } = "";
}

public sealed class SequenceRecord
{
    public string Name { get; set; } = "";
    public List<StepRecord> Steps { get; set; } = new();
}

public sealed class StepRecord
{
    /// <summary>MIDI note number — 60 is C4, the same scale <see cref="Graph.ModuleSequence"/> and
    /// the sampler's "Základní nota" use.</summary>
    public int Note { get; set; }

    /// <summary>How many steps the note occupies. Always written, unlike the flags below: this is
    /// half of what a note IS here, and WhenWritingDefault could not have hidden it anyway — that
    /// condition tests against 0, the one value this field must never take. Omitted in a
    /// hand-written file it deserializes to 0 and is clamped back up to one step on load.</summary>
    public int Length { get; set; } = 1;

    /// <summary>A gap instead of a note. Written only when true, like the other flags in this
    /// file, so a melody without rests stays free of the noise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Rest { get; set; }
}

public sealed class CurveRecord
{
    public string Name { get; set; } = "";
    public List<CurvePointRecord> Points { get; set; } = new();

    /// <summary>Set only when the user has deliberately broken a wrapping curve's ends apart. False
    /// is both the default and the overwhelmingly common case, so it is left out of the file
    /// otherwise — see <see cref="Graph.ModuleCurve.EndpointsUntied"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool EndpointsUntied { get; set; }
}

public sealed class CurvePointRecord
{
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Tangent leaving this point, as an offset from it — see <see cref="Graph.CurveHandle"/>.
    /// All four handle fields are absent on an untouched point, since zero is both the default and
    /// by far the common case; writing them everywhere would put five numbers on every breakpoint of
    /// a hand-read patch file to say nothing.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double OutDx { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double OutDy { get; set; }

    /// <summary>Tangent arriving at this point, as an offset from it. Negative <see cref="InDx"/>,
    /// since it reaches back into the segment before.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double InDx { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double InDy { get; set; }

    /// <summary>The single per-segment bend this format used before 2026-09-04, kept so files
    /// written then still open. Read only — never written back, so a patch saved once comes out in
    /// handles and this stops appearing. Converted by <see cref="PatchSerializer"/>; see there for
    /// what the conversion can and cannot preserve.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public double Tension { get; set; }
}

public sealed class FileRecord
{
    public string Name { get; set; } = "";

    /// <summary>Stored as written. Absolute paths do not survive being sent to someone else, but
    /// resolving that is the sampler's problem to solve when it exists, not the format's.</summary>
    public string Path { get; set; } = "";
}

public sealed class ParameterRecord
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public double ModulationAmount { get; set; }

    /// <summary>Whether this knob reads its note value instead of <see cref="Value"/>. Written only
    /// when true, so patches saved before tempo sync existed load back byte-for-byte the same and
    /// every patch that does not use the feature stays free of the noise. Attributed rather than
    /// set globally on the serializer, so existing fields that DO write null keep doing it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TempoSynced { get; set; }

    /// <summary>The picked note value's name — see <see cref="Graph.NoteDivision"/>. Written
    /// alongside <see cref="TempoSynced"/> and null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Division { get; set; }

    /// <summary>Set only on a Seed knob told to draw itself every time — see
    /// <see cref="Graph.ModuleParameter.IsAutoSeeded"/>. Written only when true, like
    /// <see cref="TempoSynced"/>, so a patch that never touches the feature stays byte-identical
    /// to one written before it existed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AutoSeed { get; set; }
}

public sealed class ConnectionRecord
{
    public int SourceModuleId { get; set; }
    public string SourcePort { get; set; } = "";
    public int TargetModuleId { get; set; }
    public string TargetPort { get; set; } = "";
}
