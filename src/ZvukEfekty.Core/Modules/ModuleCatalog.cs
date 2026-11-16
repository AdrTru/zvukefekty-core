using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Anything the editor's palette can list. Two kinds implement it — a single module
/// (<see cref="ModuleCatalogEntry"/>) and a whole ready-made wiring
/// (<see cref="BlockCatalogEntry"/>) — and the palette draws both from the name alone. What
/// happens on a double-click or a drop differs, and only there is the difference looked at.
/// </summary>
public interface IPaletteEntry
{
    string DisplayName { get; }
    string Category { get; }

    /// <summary>What the entry is FOR, in the palette's own words — the editor hangs it on the row
    /// as a tooltip, so a module can be understood before it is on the canvas rather than after.</summary>
    string Summary { get; }
}

/// <summary>Describes a module type the UI palette can instantiate.</summary>
public sealed record ModuleCatalogEntry(string DisplayName, string Category, Func<ModuleBase> Create)
    : IPaletteEntry
{
    /// <summary>The module's own <see cref="ModuleBase.Description"/> — the same text the property
    /// panel shows for a placed module, so the palette and the inspector cannot drift apart.
    ///
    /// <para>Read once, at catalog construction, by building a throwaway instance: a description is
    /// a constant string on every module, and asking for it per tooltip would build a module (and,
    /// for the ones that allocate buffers up front, a few hundred kilobytes) on every hover.</para></summary>
    public string Summary { get; } = Create().Description;
}

/// <summary>One section of the editor's palette, grouping catalog entries of the same kind.</summary>
public sealed record ModuleCategory(string Name, IReadOnlyList<IPaletteEntry> Entries);

/// <summary>Registry of module types available in the editor's palette.</summary>
public static class ModuleCatalog
{
    private const string SourcesCategory = "Zdroje";
    private const string ModulationCategory = "Modulace";
    private const string ProcessingCategory = "Zpracování";
    private const string MonitoringCategory = "Vizualizace";
    private const string OutputCategory = "Výstup";

    // Grouped by category (see Categories below), in the order each category should appear.
    public static IReadOnlyList<ModuleCatalogEntry> Entries { get; } = new List<ModuleCatalogEntry>
    {
        new("Nota (klávesnice)", SourcesCategory, () => new NoteModule()),
        new("Sekvencer", SourcesCategory, () => new SequencerModule()),
        new("Oscillator", SourcesCategory, () => new OscillatorModule()),
        new("Morfující oscilátor", SourcesCategory, () => new WaveMorphModule()),
        new("XOR Oscillator", SourcesCategory, () => new XorOscillatorModule()),
        new("Noise", SourcesCategory, () => new NoiseModule()),
        new("Struna", SourcesCategory, () => new KarplusStrongModule()),
        new("Bubliny", SourcesCategory, () => new FluidFlowModule()),
        new("Kolize", SourcesCategory, () => new CollisionPopulationModule()),
        new("Trhliny", SourcesCategory, () => new CrackCascadeModule()),
        new("Harmonický hlas", SourcesCategory, () => new HarmonicVoiceModule()),
        new("Tření", SourcesCategory, () => new FrictionContactModule()),
        new("Sampler", SourcesCategory, () => new SamplerModule()),

        new("LFO", ModulationCategory, () => new LfoModule()),
        new("Envelope", ModulationCategory, () => new EnvelopeModule()),
        new("Obálka z křivky", ModulationCategory, () => new CurveEnvelopeModule()),
        new("Křivka", ModulationCategory, () => new CurveModule()),
        new("Nepravidelný spouštěč", ModulationCategory, () => new PoissonTriggerModule()),
        new("Události", ModulationCategory, () => new EventSequenceModule()),
        new("Vzorkovač (S&H)", ModulationCategory, () => new SampleHoldModule()),
        new("Sklouznutí", ModulationCategory, () => new SlewModule()),
        new("Sledovač obálky", ModulationCategory, () => new EnvelopeFollowerModule()),
        new("Invertor", ModulationCategory, () => new InverterModule()),
        new("Převodník lin/log", ModulationCategory, () => new LinLogModule()),
        new("Dělič impulsů", ModulationCategory, () => new PulseDividerModule()),
        new("Kvantizér", ModulationCategory, () => new QuantizerModule()),

        new("Gain", ProcessingCategory, () => new GainModule()),
        new("Mixer", ProcessingCategory, () => new MixerModule()),
        new("Filter", ProcessingCategory, () => new FilterModule()),
        new("Resonator", ProcessingCategory, () => new ResonatorModule()),
        new("Banka módů", ProcessingCategory, () => new ModalBankModule()),
        new("Delay", ProcessingCategory, () => new DelayModule()),
        new("Zpoždění", ProcessingCategory, () => new DelayLineModule()),
        new("Reverb", ProcessingCategory, () => new ReverbModule()),
        new("Distortion", ProcessingCategory, () => new DistortionModule()),
        new("Waveshaper", ProcessingCategory, () => new WaveshaperModule()),
        new("Ring Modulator", ProcessingCategory, () => new RingModulatorModule()),
        new("Granulátor", ProcessingCategory, () => new GranulatorModule()),
        new("Kompresor", ProcessingCategory, () => new CompressorModule()),
        new("Roj", ProcessingCategory, () => new SwarmModule()),
        new("Konec roje", ProcessingCategory, () => new SwarmEndModule()),

        // Last in its category on purpose: it is the escape hatch for arithmetic the boxes cannot
        // express, not a way of building. See FormulaModule and `todo.md` 12d.
        new("Formule", ProcessingCategory, () => new FormulaModule()),

        new("Osciloskop / Spektrum", MonitoringCategory, () => new ScopeModule()),
        new("VU Metr", MonitoringCategory, () => new VuMeterModule()),
        new("Spektrogram", MonitoringCategory, () => new SpectrogramModule()),

        new("Output", OutputCategory, () => new OutputModule()),
    };

    /// <summary>Entries grouped for display, in the same order they first appear in <see cref="Entries"/>.</summary>
    public static IReadOnlyList<ModuleCategory> Categories { get; } = Entries
        .GroupBy(e => e.Category)
        .Select(g => new ModuleCategory(g.Key, g.Cast<IPaletteEntry>().ToList()))
        .ToList();
}
