using System.Text.Encodings.Web;
using System.Text.Json;
using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;
using ZvukEfekty.Core.Modules;

namespace ZvukEfekty.Core.Persistence;

/// <summary>
/// Saves/loads a <see cref="ModuleGraph"/> — plus each module's canvas position, which the
/// graph itself doesn't track — as JSON. Modules are matched by CLR type name against
/// <see cref="ModuleCatalog"/>; unknown parameters/ports found in an older or newer file
/// are skipped rather than failing the whole load.
/// </summary>
public static class PatchSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Without this, every non-ASCII name is escaped: a patch using the irregular trigger
        // writes "\u010Cetnost" instead of "Četnost", and so does anything with "Základní nota"
        // or "Nástup". A .zvuky file is meant to be read and hand-edited (the whole generator
        // prompt in Sestavy/ is written around that), and escapes make it unreadable exactly
        // where the module is Czech-named. Relaxed escaping is safe here because a patch file is
        // read back as JSON, never embedded in HTML; older files with escapes still load.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Lazy<Dictionary<string, Func<ModuleBase>>> FactoriesByTypeName = new(() =>
        ModuleCatalog.Entries.ToDictionary(e => e.Create().GetType().Name, e => e.Create));

    public static void Save(string path, ModuleGraph graph, IReadOnlyDictionary<ModuleBase, (double X, double Y)> positions, PatchTiming timing, IReadOnlyList<GroupRecord>? groups = null)
        => File.WriteAllText(path, Serialize(graph, positions, timing, groups));

    /// <param name="freshInstanceIds">See <see cref="Deserialize"/> — true when the result is being
    /// spliced into a graph that already exists.</param>
    public static PatchLoadResult Load(string path, bool freshInstanceIds = false)
    {
        var result = Deserialize(File.ReadAllText(path), freshInstanceIds);

        // A relative file path in a Sampler is resolved against the .zvuky FILE, not against the
        // process working directory. Without this a patch could only carry an absolute path, which
        // FileRecord itself admits does not survive being handed to someone else — and a relative
        // one would find its sample only when the program happened to be started from the right
        // folder, so it worked from the repo root and failed in the app, whose working directory is
        // its bin folder. Absolute paths are left exactly as they are, so no existing patch moves.
        //
        // ⚠️ Vzorek z paměti (`pamet:jméno`) tudy nesmí projít. Windows ho za rootovanou cestu
        // nepovažuje — dvojtečka není na druhém znaku — takže by se z něj tiše stalo
        // `C:\sestavy\pamet:jméno`, a to ani nespadne: na NTFS je to platný zápis alternativního
        // datového proudu. Sestava by pak hlásila chybějící soubor místo chybějícího vzorku.
        // Změřeno 2026-09-19 na net8.
        var zaklad = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(zaklad))
            foreach (var file in result.Graph.Modules.SelectMany(m => m.Files))
                if (!string.IsNullOrEmpty(file.Path) && !Path.IsPathRooted(file.Path)
                    && !AudioSampleCache.JeVPameti(file.Path))
                    file.Path = Path.GetFullPath(Path.Combine(zaklad, file.Path));

        return result;
    }

    /// <param name="groups">Organisational frames, which the graph itself does not track — the same
    /// arrangement as <paramref name="positions"/>, and for the same reason: pure canvas layout.
    /// Optional, so the round-trip that clones a patch for a live voice can ignore it.</param>
    public static string Serialize(ModuleGraph graph, IReadOnlyDictionary<ModuleBase, (double X, double Y)> positions, PatchTiming timing, IReadOnlyList<GroupRecord>? groups = null)
        => Serialize(graph.Modules, graph.Connections, positions, timing, groups);

    /// <summary>
    /// Writes a PART of a graph — the modules given and nothing else — in the same format a whole
    /// patch uses. That is what a copied selection and a saved block are: an ordinary
    /// <c>.zvuky</c> document that happens to hold a few boxes and no <c>Output</c>, so it can be
    /// opened and read like any other file rather than being a second format to maintain.
    ///
    /// <para>A connection with an end outside <paramref name="modules"/> is dropped rather than
    /// written, because the id it would point at does not exist in this document. Filtering here
    /// as well as at the call site is deliberate: a half-written wire loads as a connection to
    /// whatever module happens to hold that index.</para>
    /// </summary>
    public static string Serialize(IReadOnlyList<ModuleBase> modules, IReadOnlyList<Connection> connections,
        IReadOnlyDictionary<ModuleBase, (double X, double Y)> positions, PatchTiming timing,
        IReadOnlyList<GroupRecord>? groups = null)
    {
        var ids = modules
            .Select((module, index) => (module, index))
            .ToDictionary(t => t.module, t => t.index);

        var document = new PatchDocument
        {
            DurationSeconds = timing.DurationSeconds,
            Bpm = timing.Bpm,
            Bars = timing.Bars,
            IsTempoLocked = timing.IsTempoLocked,
            Modules = modules.Select(module =>
            {
                positions.TryGetValue(module, out var position);
                return new ModuleRecord
                {
                    Id = ids[module],
                    Type = module.GetType().Name,
                    Label = string.IsNullOrWhiteSpace(module.Label) ? null : module.Label.Trim(),
                    InstanceId = module.Id,
                    X = position.X,
                    Y = position.Y,
                    Parameters = module.Parameters.Select(p => new ParameterRecord
                    {
                        Name = p.Name,
                        Value = p.Value,
                        ModulationAmount = p.ModulationAmount,

                        // Null unless the knob is actually synced, so a patch that never touches
                        // the feature reads exactly as it did before it existed. This is not only
                        // about files: a live voice is built by round-tripping the patch through
                        // this serializer (Audio.Voice.ClonePatch), so a note value that is not
                        // written here is a note value the played sound does not have.
                        TempoSynced = p.IsTempoSynced ? true : null,
                        Division = p.IsTempoSynced ? p.DivisionName : null,

                        // The one flag that has to be written for the sound NOT to be reproducible.
                        // Without it the seed the module drew on load would be recorded as a plain
                        // number and the patch would freeze on the first save — which is exactly
                        // how E_robot_kov's deliberate randomness used to be one Save from gone.
                        AutoSeed = p.IsAutoSeeded ? true : null,
                    }).ToList(),

                    // Only curves the user actually changed, and null rather than an empty list
                    // when there are none — see ModuleCurve.IsDefault and ModuleRecord.
                    Curves = EditedCurves(module).Count == 0 ? null : EditedCurves(module).Select(c => new CurveRecord
                    {
                        Name = c.Name,
                        EndpointsUntied = c.EndpointsUntied,
                        Points = c.Curve.Points.Select(point => new CurvePointRecord
                        {
                            X = point.X,
                            Y = point.Y,
                            OutDx = point.Out.Dx,
                            OutDy = point.Out.Dy,
                            InDx = point.In.Dx,
                            InDy = point.In.Dy
                        }).ToList()
                    }).ToList(),
                    Files = module.Files.Count == 0 ? null : module.Files.Select(f => new FileRecord
                    {
                        Name = f.Name,
                        Path = f.Path
                    }).ToList(),

                    // Written whether or not it was edited, unlike Curves above — see
                    // ModuleRecord.Sequences. A live voice is cloned by round-tripping through
                    // here, so a note left out of this is a note the played sound does not have.
                    Sequences = module.Sequences.Count == 0 ? null : module.Sequences.Select(q => new SequenceRecord
                    {
                        Name = q.Name,
                        Steps = q.Steps.Select(step => new StepRecord
                        {
                            Note = step.Note,
                            Length = step.Length,
                            Rest = step.Rest
                        }).ToList()
                    }).ToList(),

                    // Written like Sequences and unlike Curves — see ModuleRecord.Texts.
                    Texts = module.Texts.Count == 0 ? null : module.Texts.Select(x => new TextRecord
                    {
                        Name = x.Name,
                        Text = x.Text
                    }).ToList()
                };
            }).ToList(),
            Connections = connections
                .Where(c => ids.ContainsKey(c.Source.Owner) && ids.ContainsKey(c.Target.Owner))
                .Select(c => new ConnectionRecord
                {
                    SourceModuleId = ids[c.Source.Owner],
                    SourcePort = c.Source.Name,
                    TargetModuleId = ids[c.Target.Owner],
                    TargetPort = c.Target.Name
                }).ToList(),

            Groups = groups is null || groups.Count == 0 ? null : groups.ToList()
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <param name="freshInstanceIds">
    /// True when the result is going to be SPLICED INTO a graph that already exists — a pasted
    /// selection, a block loaded onto a canvas, an instrument imported under a Nota — rather than
    /// replacing it. The stored <see cref="ModuleRecord.InstanceId"/>s are then ignored and every
    /// module keeps the fresh Id it was constructed with.
    ///
    /// <para>It has to be an option rather than the rule, and the rule rather than the option,
    /// because the Id is the slot a Scope or VU meter publishes live data to: opening a patch must
    /// keep the slots it was saved with, and inserting the same file twice must NOT hand two
    /// modules the same slot. Restoring ids on an insert is exactly the collision <c>todo.md</c> 11a
    /// warned about.</para>
    ///
    /// <para>A frame's "show this when folded" set names modules by that same Id, so it is
    /// translated to the fresh ones here — otherwise a pasted block would come up showing nothing,
    /// silently, because every Guid in it would point at the original's modules.</para>
    /// </param>
    public static PatchLoadResult Deserialize(string json, bool freshInstanceIds = false)
    {
        var document = JsonSerializer.Deserialize<PatchDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Soubor se sestavou je poškozený nebo prázdný.");

        var graph = new ModuleGraph();
        var positions = new Dictionary<ModuleBase, (double X, double Y)>();
        var modulesById = new Dictionary<int, ModuleBase>();
        var freshIdByStoredId = new Dictionary<Guid, Guid>();

        foreach (var record in document.Modules)
        {
            var module = CreateByTypeName(record.Type);
            if (freshInstanceIds)
            {
                if (record.InstanceId != Guid.Empty)
                    freshIdByStoredId[record.InstanceId] = module.Id;
            }
            else if (record.InstanceId != Guid.Empty)
            {
                module.RestoreId(record.InstanceId);
            }

            module.Label = LabelOf(record, module.TypeName);

            foreach (var paramRecord in record.Parameters)
            {
                var parameter = module.Parameters.FirstOrDefault(p => p.Name == paramRecord.Name);
                if (parameter is null)
                    continue;

                parameter.Value = paramRecord.Value;

                // Only where something can actually be plugged in. Patches saved before this was
                // fixed carry a depth for knobs that have no modulation input, and honouring it
                // would write the dead value straight back out on the next save.
                if (parameter.ModulationInput is not null)
                    parameter.ModulationAmount = paramRecord.ModulationAmount;

                // Guarded by IsTempoSyncable so a file claiming a note value for a knob that is not
                // a duration (an older build's parameter that has since been re-purposed, say)
                // cannot switch on a mode this parameter has no conversion for.
                if (parameter.IsTempoSyncable && paramRecord.TempoSynced == true)
                {
                    parameter.DivisionName = NoteDivision.ByName(paramRecord.Division).Name;
                    parameter.IsTempoSynced = true;
                }

                // Guarded by IsSeed for the same reason as the sync flag above: a file claiming
                // self-drawing for a knob that is not a seed would switch on a mode nothing reads.
                if (parameter.IsSeed && paramRecord.AutoSeed == true)
                    parameter.IsAutoSeeded = true;
            }

            // Matched by name and skipped when unknown, exactly like parameters: a module that
            // dropped a curve since the file was written should still load the rest of it.
            foreach (var curveRecord in record.Curves ?? Enumerable.Empty<CurveRecord>())
            {
                var curve = module.Curves.FirstOrDefault(c => c.Name == curveRecord.Name);
                if (curve is null || curveRecord.Points.Count == 0)
                    continue;

                // Untied first: it changes what conforming the curve does, and setting it after
                // would tie ends the file meant to keep apart and then leave them tied.
                curve.EndpointsUntied = curveRecord.EndpointsUntied;
                curve.Curve = new Curve(ToPoints(curveRecord.Points));
            }

            foreach (var fileRecord in record.Files ?? Enumerable.Empty<FileRecord>())
            {
                var file = module.Files.FirstOrDefault(f => f.Name == fileRecord.Name);
                if (file is not null)
                    file.Path = fileRecord.Path;
            }

            // Matched by name and skipped when unknown, like everything above. An empty Steps list
            // is skipped too rather than applied: a sequencer with no notes is silent, and a file
            // that says nothing about the sequence should leave the module's own melody alone
            // instead of emptying it.
            foreach (var sequenceRecord in record.Sequences ?? Enumerable.Empty<SequenceRecord>())
            {
                var sequence = module.Sequences.FirstOrDefault(q => q.Name == sequenceRecord.Name);
                if (sequence is null || sequenceRecord.Steps.Count == 0)
                    continue;

                sequence.Replace(sequenceRecord.Steps.Select(step =>
                    // Length clamped up rather than trusted: omitted in a hand-written file it
                    // arrives as 0, which would be a note of no duration and a sequence whose
                    // total length no longer matches the notes in it.
                    new SequenceStep(step.Note, Math.Max(step.Length, 1), step.Rest)));
            }

            // Matched by name and skipped when unknown, like everything above. An empty string IS
            // applied, unlike an empty sequence: a formula the user cleared is a decision, and the
            // module says so itself by showing "formule je prázdná" next to the box.
            foreach (var textRecord in record.Texts ?? Enumerable.Empty<TextRecord>())
            {
                var text = module.Texts.FirstOrDefault(x => x.Name == textRecord.Name);
                if (text is not null)
                    text.Text = textRecord.Text ?? "";
            }

            graph.AddModule(module);
            modulesById[record.Id] = module;
            positions[module] = (record.X, record.Y);
        }

        foreach (var connRecord in document.Connections)
        {
            if (!modulesById.TryGetValue(connRecord.SourceModuleId, out var sourceModule))
                continue;
            if (!modulesById.TryGetValue(connRecord.TargetModuleId, out var targetModule))
                continue;

            var sourcePort = sourceModule.Outputs.FirstOrDefault(p => p.Name == connRecord.SourcePort);
            var targetPort = targetModule.Inputs.FirstOrDefault(p => p.Name == connRecord.TargetPort);
            if (sourcePort is null || targetPort is null)
                continue;

            graph.Connect(sourcePort, targetPort);
        }

        var timing = new PatchTiming
        {
            DurationSeconds = document.DurationSeconds,
            Bpm = document.Bpm,
            Bars = document.Bars,
            IsTempoLocked = document.IsTempoLocked,
        };

        // A Guid that translates to nothing is dropped, not kept: it named a module this document
        // does not carry, and leaving it in would be a frame promising a readout nothing can fill.
        if (freshInstanceIds)
        {
            foreach (var group in document.Groups ?? Enumerable.Empty<GroupRecord>())
                group.Visuals = group.Visuals?
                    .Where(freshIdByStoredId.ContainsKey)
                    .Select(id => freshIdByStoredId[id])
                    .ToList();
        }

        return new PatchLoadResult(graph, positions, timing, document.Groups ?? new List<GroupRecord>())
        {
            ModulesByFileId = modulesById,
        };
    }

    private static IEnumerable<CurvePoint> ToPoints(List<CurvePointRecord> records)
    {
        var points = records.Select(r => new CurvePoint(r.X, r.Y,
            new CurveHandle(r.OutDx, r.OutDy),
            new CurveHandle(r.InDx, r.InDy))).ToArray();

        // A segment's old bend becomes the out-handle of the point it left and the in-handle of the
        // one it reached, so it needs both — which is why this is a walk over pairs and not a
        // Select. Only ever fills handles the file did not already state.
        for (var i = 0; i < points.Length - 1; i++)
        {
            if (records[i].Tension == 0 || !points[i].Out.IsAuto || !points[i + 1].In.IsAuto)
                continue;

            var (leaving, arriving) = FromTension(records[i].Tension,
                points[i + 1].X - points[i].X, points[i + 1].Y - points[i].Y);

            points[i] = points[i] with { Out = leaving };
            points[i + 1] = points[i + 1] with { In = arriving };
        }

        return points;
    }

    /// <summary>
    /// The pre-2026-09-04 per-segment bend, as the pair of tangents that replaced it. Kept so a file
    /// written then still opens looking like itself. Read only: the curve is saved back as handles,
    /// so a patch loaded and saved once stops carrying a tension at all.
    ///
    /// <para>Tension warped a segment as <c>u^(2^-2t)</c>. That leaves its point vertically (below
    /// exponent 1) or horizontally (above it) and arrives at the next one along a slope of the
    /// exponent times the chord — so the ARRIVAL is matched exactly and the departure direction is
    /// right, leaving only the strength in between to approximate.</para>
    ///
    /// <para>Measured against the old bend, as a share of the segment's rise: tension 0,25 lands
    /// 3,8 % off, 0,5 lands 1,4 %, 0,6 lands 1,7 %, -0,5 lands 6,7 % and -1 lands 3,8 %. The far
    /// end is the worst — tension 1, the hardest bend the old slider could reach, comes out 16,4 %
    /// off, which is a visibly softer knee rather than a different shape.</para>
    ///
    /// <para>Approximate on purpose, and the numbers above are why that is defensible rather than
    /// lazy: an exact fit would cost a bisection per point at load, and no saved patch in the
    /// repository carries a tension at all — all 27 were checked. This exists for hand-written and
    /// generated files, where what matters is that the shape still reads as itself.</para>
    /// </summary>
    private static (CurveHandle Out, CurveHandle In) FromTension(double tension, double span, double rise)
    {
        if (span <= 0)
            return (CurveHandle.Auto, CurveHandle.Auto);

        var exponent = Math.Pow(2, -2 * tension);

        // Arrival is the end the old bend pinned down: its slope there is `exponent` times the
        // chord's, whichever way the bend went. Length is held short enough that the tangent's tip
        // cannot leave the segment's own y range, which the old bend never did either.
        var reach = Math.Min(1.0 / 3, 2.0 / (3 * exponent));
        var arriving = new CurveHandle(-span * reach, -rise * exponent * reach);

        // Departure is where the two bends differ: below 1 the old curve left vertically, above it
        // horizontally. Strength is what is being approximated — matched at the ends, eyeballed in
        // between, and measured (see the remarks).
        var leaving = exponent < 1
            ? new CurveHandle(0, rise * (1 - exponent))
            : new CurveHandle(span * (1 - 1 / exponent), 0);

        return (leaving, arriving);
    }

    private static List<ModuleCurve> EditedCurves(ModuleBase module)
        => module.Curves.Where(c => !c.IsDefault).ToList();

    /// <summary>
    /// The dovětek a record carries, whichever way it was written.
    ///
    /// <para>New files store just the dovětek (<see cref="ModuleRecord.Label"/>). Files from before
    /// it existed store the finished name, so the type's own name comes off the front and the rest
    /// is the dovětek. A name that does not start with the type's name is kept whole — see
    /// <see cref="ModuleRecord.Name"/> for what that looks like and why it is the right answer.</para>
    /// </summary>
    private static string LabelOf(ModuleRecord record, string typeName)
    {
        if (!string.IsNullOrWhiteSpace(record.Label))
            return record.Label.Trim();

        var name = record.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name == typeName)
            return "";

        return name.StartsWith(typeName + " ", StringComparison.Ordinal)
            ? name[(typeName.Length + 1)..]
            : name;
    }

    private static ModuleBase CreateByTypeName(string typeName)
    {
        if (!FactoriesByTypeName.Value.TryGetValue(typeName, out var factory))
            throw new InvalidOperationException($"Neznámý typ modulu v souboru: {typeName}");

        return factory();
    }
}

/// <summary>
/// What comes back out of a patch file. A record rather than the tuple this used to return: the
/// canvas has grown enough layout of its own — positions, and now frames — that naming the pieces
/// beats counting them at every call site.
/// </summary>
/// <param name="Graph">The modules and their wiring.</param>
/// <param name="Positions">Where each module sits on the canvas.</param>
/// <param name="Timing">Length, tempo and whether the two are locked together.</param>
/// <param name="Groups">Organisational frames; empty for files written before they existed.</param>
public sealed record PatchLoadResult(
    ModuleGraph Graph,
    Dictionary<ModuleBase, (double X, double Y)> Positions,
    PatchTiming Timing,
    List<GroupRecord> Groups)
{
    /// <summary>
    /// The loaded modules under the ids the FILE gave them (<see cref="ModuleRecord.Id"/>), which is
    /// how <see cref="ConnectionRecord"/> and <see cref="GroupPortRecord"/> point at them. Deliberately
    /// not a positional member of this record: every existing caller deconstructs it into four
    /// pieces, and only the one resolving a frame's connectors needs this.
    /// </summary>
    public IReadOnlyDictionary<int, ModuleBase> ModulesByFileId { get; init; }
        = new Dictionary<int, ModuleBase>();
}
