using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>One module inside a block, and where it sits relative to the block's top-left corner.</summary>
public sealed record BlockModule(ModuleBase Module, double X, double Y);

/// <summary>One connector the block publishes on its edge, and the inner port it stands for.</summary>
public sealed record BlockPort(string Name, Port Port);

/// <summary>
/// A ready-made arrangement of modules: what boxes, wired how, with which knobs turned where, and
/// which of their ports the block publishes on its edge.
///
/// <para>Built fresh on every insert — the modules in here are real instances, so a definition is
/// produced by calling the catalog entry again rather than by copying one around.</para>
/// </summary>
public sealed class BlockDefinition
{
    internal BlockDefinition(string title, string description,
        IReadOnlyList<BlockModule> modules,
        IReadOnlyList<(Port Source, Port Target)> connections,
        IReadOnlyList<BlockPort> ports)
    {
        Title = title;
        Description = description;
        Modules = modules;
        Connections = connections;
        Ports = ports;
    }

    /// <summary>Goes on the frame's title bar.</summary>
    public string Title { get; }

    /// <summary>Goes in the frame's note, under the title. This is where a block explains itself —
    /// the whole reason a block exists rather than a module is that it can be taken apart and read,
    /// and the note is the first half of that.</summary>
    public string Description { get; }

    public IReadOnlyList<BlockModule> Modules { get; }
    public IReadOnlyList<(Port Source, Port Target)> Connections { get; }
    public IReadOnlyList<BlockPort> Ports { get; }
}

/// <summary>Collects modules, wires and connectors while a block definition is being written.</summary>
public sealed class BlockBuilder
{
    private readonly List<BlockModule> _modules = new();
    private readonly List<(Port Source, Port Target)> _connections = new();
    private readonly List<BlockPort> _ports = new();
    private readonly string _title;
    private readonly string _description;

    public BlockBuilder(string title, string description)
    {
        _title = title;
        _description = description;
    }

    public T Add<T>(T module, double x, double y) where T : ModuleBase
    {
        _modules.Add(new BlockModule(module, x, y));
        return module;
    }

    public void Wire(Port source, Port target) => _connections.Add((source, target));

    /// <summary>Publishes an inner port on the block's edge under a name of the block's choosing.</summary>
    public void Publish(string name, Port port) => _ports.Add(new BlockPort(name, port));

    /// <summary>Convenience for the knob-plus-its-modulation-input pair, which every block that
    /// drives a parameter from another module has to set: the base value the signal is added to,
    /// and how much of it counts.</summary>
    public static void Drive(ModuleBase module, string parameter, double baseValue, double amount)
    {
        var knob = Knob(module, parameter);
        knob.Value = baseValue;
        knob.ModulationAmount = amount;
    }

    public static ModuleParameter Knob(ModuleBase module, string name)
        => module.Parameters.First(p => p.Name == name);

    public static Port In(ModuleBase module, string name)
        => module.Inputs.First(p => p.Name == name);

    public static Port Out(ModuleBase module, string name)
        => module.Outputs.First(p => p.Name == name);

    public BlockDefinition Build() => new(_title, _description, _modules, _connections, _ports);
}

/// <summary>Describes a block the UI palette can drop onto the canvas, whole.</summary>
public sealed record BlockCatalogEntry(string DisplayName, string Summary, Func<BlockDefinition> Create)
    : IPaletteEntry
{
    public string Category => BlockCatalog.CategoryName;
}

/// <summary>
/// The blocks the program ships with: wirings that work, that nobody would guess, and that are
/// deliberately NOT modules.
///
/// <para><b>Why these are blocks and not modules.</b> Every one of them can already be built out of
/// what is in <see cref="ModuleCatalog"/> — the engine is not missing anything. What is missing is
/// knowing HOW, and a module would answer that by hiding it. A block arrives as the same boxes and
/// wires the user could have dropped themselves, so it can be opened, taken apart, retuned and
/// learned from. See todo.md 11a.</para>
///
/// <para><b>Why a block sometimes carries a pass-through Gain.</b> A block connector stands for
/// exactly one inner port (see the App's GroupPortViewModel), so a block whose input has to reach
/// two boxes splits it inside, on an ordinary Gain left at 1.0 — a multiply by one, bit for bit.
/// That fan-out is the very thing a first-timer does not think of, so hiding it in the model rather
/// than drawing it would throw away the reason the block exists.</para>
/// </summary>
public static class BlockCatalog
{
    public const string CategoryName = "Bloky";

    public static IReadOnlyList<BlockCatalogEntry> Entries { get; } = new List<BlockCatalogEntry>
    {
        new("Tón z klávesy", "Nota, oscilátor, obálka, výstup — celá cesta od klávesy ke zvuku.", KeyTone),
        new("Brána", "Pustí zvuk, jen když je dost hlasitý — a nelupne u toho.", Gate),
        new("Ducking", "Úder uhne podkladu z cesty — sidechain přes výstup Zisk na Kompresoru.", Ducking),
        new("Chorus", "Tři rozladěné hlavy proti suchému signálu. Zpoždění samo tohle neumí.", Chorus),
        new("Rezonanční flanger", "Ježdící hřeben, který si zvoní. Zpětná vazba je ten rozdíl proti chorusu.", ResonantFlanger),
        new("Phaser", "Čtyři All-passy proti suchému signálu. Filtr sám nezní — slyšet je až ten součet.", Phaser),
        new("Unisono", "Čtyři rozladěné pily z jedné výšky. Detune je hloubka modulace, ne knoflík.", Unison),
        new("Hodiny", "Přesný takt a k němu pomalejší, který se s ním nemůže rozejít.", Clock),
        new("Náhodná melodie", "Náhoda, která hraje ve stupnici — a spouští u toho obálku.", RandomMelody),
        new("Dva díly", "Náběh a po něm rána, obojí z jedné obálky. Hrana mezi nimi se nekreslí.", TwoSections),
    };

    /// <summary>
    /// The shortest complete path from a key to a sound: Nota → Oscillator → Envelope → Output.
    ///
    /// <para><b>Why this one exists at all.</b> Every other block here is a wiring nobody would
    /// guess. This one is the opposite — it is the wiring everybody needs FIRST, and the one a
    /// blank canvas gives no hint about. It is also the only block that ends in an `Output`, so it
    /// makes a sound the moment it lands.</para>
    ///
    /// <para><b>The pitch is a modulation depth of 1 on a base of nought</b>, the same idiom the
    /// Unison block uses: `Hz` carries hertz, and the oscillator's own Frequency knob has to be out
    /// of the way for the key to be the pitch.</para>
    ///
    /// <para><b>Triangle, and that is a measured choice.</b> It is the only shape that stays clean
    /// at the top of the keyboard without any switch. Energy landing off the harmonic series, at
    /// MIDI 48/72/91: a sine (the measurement floor) −45,3/−45,5/−41,0 dB, triangle
    /// −45,3/−45,2/−38,6, a saw with Vyhlazení hran on −39,4/−34,3/−30,0 and a raw saw
    /// −24,4/−18,4/−13,8. A beginner's first sound should not need a flag turned on to be clean.</para>
    ///
    /// <para><b>Attack is 0,01 s because 0 clicks</b> — and not for the reason it usually does.
    /// A triangle starts its cycle at its MINIMUM, so with an instant attack the very first sample
    /// is −0,8: a jump straight out of silence. Measured x[0] is −0,8000 at Attack 0 and −0,0000
    /// from 0,005 s up, and at 0,01 s the steepest step in the first 5 ms (0,0097) is gentler than
    /// the wave's own slope later on (0,0133).</para>
    ///
    /// <para>Measured on the finished block: the peak is 0,794–0,799 across MIDI 36–91, so no key
    /// clips and none is conspicuously quieter; live, one note peaks at −2,0 dBFS and both a triad
    /// and a six-note chord at −1,0 dBFS, which is the master limiter doing its job.</para>
    ///
    /// <para><b>Two connectors, and the input one is the interesting half.</b> `Zvuk` taps the
    /// envelope's output for a meter, a scope or an effect; `Do výstupu` is the Output's own input,
    /// already wired from inside. Plugging anything into it REPLACES that inner wire (a target
    /// takes one connection — see <see cref="ModuleGraph.Connect"/>), so an effect goes between the
    /// two connectors without opening the block or deleting anything.</para>
    /// </summary>
    private static BlockDefinition KeyTone()
    {
        var builder = new BlockBuilder("Tón z klávesy",
            "Nejkratší celá cesta od klávesy ke zvuku: Nota dá výšku a stisk, Oscillator z výšky " +
            "udělá tón, Envelope ho podle stisku otevře a zase pustí, Output ho vyvede ven. " +
            "Výšku nese hloubka modulace 1 na základu 0 — Frequency na oscilátoru musí jít z cesty.");

        var note = builder.Add(new NoteModule(), 0, 0);
        BlockBuilder.Knob(note, "Držení").Value = 0.8;      // sedne se i do výchozích 1,5 s i s doznením

        var osc = builder.Add(new OscillatorModule(), 240, 0);
        BlockBuilder.Knob(osc, "Waveform").Value = 3;       // Triangle
        BlockBuilder.Drive(osc, "Frequency", baseValue: 0, amount: 1.0);

        var env = builder.Add(new EnvelopeModule(), 480, 0);
        BlockBuilder.Knob(env, "Attack").Value = 0.01;
        BlockBuilder.Knob(env, "Decay").Value = 0.25;
        BlockBuilder.Knob(env, "Sustain").Value = 0.7;
        BlockBuilder.Knob(env, "Release").Value = 0.35;

        var output = builder.Add(new OutputModule(), 720, 0);

        builder.Wire(BlockBuilder.Out(note, "Hz"), BlockBuilder.In(osc, "Frequency"));
        builder.Wire(BlockBuilder.Out(note, "Gate"), BlockBuilder.In(env, "Gate"));
        builder.Wire(BlockBuilder.Out(osc, "Out"), BlockBuilder.In(env, "In"));
        builder.Wire(BlockBuilder.Out(env, "Out"), BlockBuilder.In(output, "In"));

        builder.Publish("Zvuk", BlockBuilder.Out(env, "Out"));
        builder.Publish("Do výstupu", BlockBuilder.In(output, "In"));

        return builder.Build();
    }

    /// <summary>
    /// A noise gate: the level opens a gate, the gate is smoothed, the smoothed gate is the volume.
    ///
    /// <para>Three things about it are not obvious, which is exactly why it ships as a block.
    /// The source has to reach BOTH the detector and the amplifier, so the block splits it on its
    /// own input Gain. The gate is a hard 0/1 and multiplying audio by it clicks — measured, the
    /// biggest step between two samples is 0.9304 without the Sklouznutí in the middle and 0.0282
    /// with it. And the output Gain has to sit at 0 with a modulation depth of 1, or the gate would
    /// be added to a volume that is already fully open.</para>
    /// </summary>
    private static BlockDefinition Gate()
    {
        var builder = new BlockBuilder("Brána",
            "Pustí zvuk, jen když je hlasitější než Práh na Sledovači obálky. " +
            "Vstupní Gain je zároveň citlivost: zesílí-li se, otevře se brána dřív. " +
            "Sklouznutí mezi Gate a hlasitostí tam není pro efekt — bez něj brána cvaká.");

        // Splits the input: the detector has to hear the same signal the amplifier passes. Left at
        // 1.0, where a Gain is bit-transparent, so an untouched block cannot colour anything.
        var input = builder.Add(new GainModule { Label = "(vstup)" }, 0, 0);

        var detector = builder.Add(new EnvelopeFollowerModule(), 210, 0);
        BlockBuilder.Knob(detector, "Nástup").Value = 0.005;
        BlockBuilder.Knob(detector, "Dozvuk").Value = 0.030;
        BlockBuilder.Knob(detector, "Práh").Value = 0.2;

        // Rounds the gate's 0-to-1 step into a ramp. Faster opening than closing, so a hit is not
        // blunted on the way in but the tail is let go gently.
        var smoothing = builder.Add(new SlewModule(), 210, 250);
        BlockBuilder.Knob(smoothing, "Nahoru").Value = 0.005;
        BlockBuilder.Knob(smoothing, "Dolů").Value = 0.020;

        var output = builder.Add(new GainModule { Label = "(hlasitost)" }, 420, 0);
        BlockBuilder.Drive(output, "Gain", baseValue: 0, amount: 1);

        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(detector, "In"));
        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(output, "In"));
        builder.Wire(BlockBuilder.Out(detector, "Gate"), BlockBuilder.In(smoothing, "In"));
        builder.Wire(BlockBuilder.Out(smoothing, "Out"), BlockBuilder.In(output, "Gain"));

        builder.Publish("Vstup", BlockBuilder.In(input, "In"));
        builder.Publish("Výstup", BlockBuilder.Out(output, "Out"));

        return builder.Build();
    }
    /// <summary>
    /// Sidechain ducking: the hit pulls the backing down out of its way and lets it back up.
    ///
    /// <para>The <see cref="CompressorModule"/> here is a DETECTOR, not an effect — its audio
    /// output goes nowhere and only its `Zisk` is used, which is why the hit reaches the mix
    /// untouched through the same input Gain that feeds the detector. That is the whole reason
    /// `Zisk` carries the reduction WITHOUT the makeup: it is a number to multiply another branch
    /// by, not this branch's volume.</para>
    ///
    /// <para>Built out of the compressor rather than out of `Sledovač obálky` with a negative
    /// modulation depth, which is the other recipe and the one the documentation used to give. The
    /// follower's level is an amplitude, so how much it ducks depends on how loud the hit happens
    /// to be, and a depth past 1/level takes the backing's gain NEGATIVE — it comes back
    /// phase-inverted rather than quiet. `Zisk` cannot do that: it is a gain, it is bounded by
    /// nought and one, and its threshold and ratio are in decibels.</para>
    /// </summary>
    private static BlockDefinition Ducking()
    {
        var builder = new BlockBuilder("Ducking",
            "Co přijde do Úderu, ztlumí Podklad — a pustí ho zpátky. Sílu a rychlost řídí " +
            "Práh, Poměr, Nástup a Dozvuk na Kompresoru; ten tady nic nezesiluje, jen měří. " +
            "Zvuk z Úderu jde do mixu nedotčený.");

        // Splits the hit: the detector has to hear it, and the mix has to get it unchanged.
        var input = builder.Add(new GainModule { Label = "(úder)" }, 0, 0);

        var detector = builder.Add(new CompressorModule(), 230, 0);
        BlockBuilder.Knob(detector, "Práh").Value = -24;
        BlockBuilder.Knob(detector, "Poměr").Value = 8;
        BlockBuilder.Knob(detector, "Koleno").Value = 6;
        BlockBuilder.Knob(detector, "Nástup").Value = 0.005;
        BlockBuilder.Knob(detector, "Dozvuk").Value = 0.150;

        var ducked = builder.Add(new GainModule { Label = "(podklad)" }, 230, 250);
        BlockBuilder.Drive(ducked, "Gain", baseValue: 0, amount: 1);

        // Average, not Sum: a block that can clip the moment it is dropped in is a bad block. Both
        // halves come out at half level and the user sets the balance outside.
        var mix = builder.Add(new MixerModule(), 460, 60);
        BlockBuilder.Knob(mix, "Mode").Value = 1;

        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(detector, "In"));
        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(mix, "In 1"));
        builder.Wire(BlockBuilder.Out(detector, "Zisk"), BlockBuilder.In(ducked, "Gain"));
        builder.Wire(BlockBuilder.Out(ducked, "Out"), BlockBuilder.In(mix, "In 2"));

        builder.Publish("Úder", BlockBuilder.In(input, "In"));
        builder.Publish("Podklad", BlockBuilder.In(ducked, "In"));
        builder.Publish("Výstup", BlockBuilder.Out(mix, "Out"));

        return builder.Build();
    }

    /// <summary>
    /// Chorus: three delayed copies whose delays drift, mixed against the dry signal.
    ///
    /// <para>todo.md 12b lists this as the thing `Zpoždění` is advertised for and cannot actually
    /// do alone. One modulated delay is a vibrato; what makes a chorus is SEVERAL of them drifting
    /// at different rates against a dry signal they keep sliding in and out of phase with. Three
    /// wet heads plus the dry is exactly the four inputs a `Mixer` has, which is where the number
    /// three comes from.</para>
    ///
    /// <para>The three LFOs are not one LFO fanned out, and that is the point: a single rate would
    /// move all three heads together and the whole thing would be a vibrato again. Their rates are
    /// deliberately unrelated so the pattern never repeats on a musical timescale.</para>
    /// </summary>
    private static BlockDefinition Chorus()
    {
        var builder = new BlockBuilder("Chorus",
            "Tři zpožděné kopie, kterým se čas plynule rozjíždí, proti suchému signálu. " +
            "Jedno modulované Zpoždění je vibrato — chorus dělá až víc hlav s různou rychlostí. " +
            "Hloubka modulace u Času je rozkmit v milisekundách, Frequency na LFO jeho rychlost.");

        // The dry copy is one of the four things the Mixer adds up, so the input has to reach four
        // places: three delays and the mixer.
        var input = builder.Add(new GainModule(), 0, 0);

        var mix = builder.Add(new MixerModule(), 500, 0);
        BlockBuilder.Knob(mix, "Mode").Value = 1;
        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(mix, "In 1"));

        // Rates with no common multiple worth hearing, and depths that grow with the delay, so the
        // three heads never line up and each one wobbles in its own proportion.
        var heads = new (double Time, double Depth, double Rate)[]
        {
            (11.0, 2.5, 0.31), (16.0, 3.0, 0.47), (21.0, 3.5, 0.63),
        };
        for (var i = 0; i < heads.Length; i++)
        {
            var (time, depth, rate) = heads[i];

            var lfo = builder.Add(new LfoModule { Label = $"{i + 1}" }, 0, 150 + i * 150);
            BlockBuilder.Knob(lfo, "Frequency").Value = rate;

            var head = builder.Add(new DelayLineModule { Label = $"{i + 1}" }, 260, 150 * i);
            BlockBuilder.Drive(head, "Čas", baseValue: time, amount: depth);

            builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(head, "In"));
            builder.Wire(BlockBuilder.Out(lfo, "Out"), BlockBuilder.In(head, "Čas"));
            builder.Wire(BlockBuilder.Out(head, "Out"), BlockBuilder.In(mix, $"In {i + 2}"));
        }

        builder.Publish("Vstup", BlockBuilder.In(input, "In"));
        builder.Publish("Výstup", BlockBuilder.Out(mix, "Out"));

        return builder.Build();
    }

    /// <summary>
    /// Resonant flanger: one short delay swept against the dry signal, with its own output fed
    /// back into it.
    ///
    /// <para><b>The last open row of todo.md 12b, and it needed one knob rather than a module.</b>
    /// A comb was always buildable — `Zpoždění` into a `Mixer` alongside the dry signal — and the
    /// sweep was too, from an LFO on `Čas`. What could not be built was the RESONANCE, because
    /// that is the delay hearing itself, and a wire from a box back to its own input is refused at
    /// render. `Zpoždění` therefore grew a `Zpětná vazba` knob (0 by default, where it is bit for
    /// bit the module it was), and with that in place this block is ordinary boxes again — which
    /// is the rule this whole catalog lives by.</para>
    ///
    /// <para><b>What the feedback actually does, and it is the opposite of what it sounds like it
    /// should.</b> It does not deepen the comb — it FILLS the notches and grows the peaks. Measured
    /// on white noise against the dry signal, at Čas 3 ms:</para>
    ///
    /// <para><c>Zpětná vazba 0: peaks +1,4 dB, notches −46,7 dB · 0,3: +3,9 / −22,7 · 0,5: +6,8 /
    /// −17,8 · 0,7: +12,2 / −15,1 · 0,9: +25,9 / −13,5.</c></para>
    ///
    /// <para>So a plain comb is nearly all notch: two equal copies cancel almost perfectly and
    /// barely reinforce. Feedback trades that away — the delayed copy keeps re-entering, so the
    /// peaks turn into resonances that ring while the nulls silt up. THAT is the flanger's whistle,
    /// and it is what a chorus cannot do however many heads it has.</para>
    ///
    /// <para><b>Triangle at 0,2 Hz, and both numbers are the flanger idiom rather than taste.</b>
    /// The sweep wants to cross its range evenly (the same measurement the Phaser block records for
    /// its own triangle) and it wants to be slow: a flanger is heard as a jet going over, and at
    /// the Chorus block's rates the same wiring is a vibrato instead.</para>
    ///
    /// <para><b>Čas 3 ms ± 2,4 puts the sweep between 0,6 and 5,4 ms</b>, and the comb rides it:
    /// the lowest peak measures 1598 Hz at one end of the travel and 180 Hz at the other, against
    /// 1/Čas of 1667 and 185 — the whole stack of peaks above it slides through the band with it.
    /// Longer than about 10 ms stops being a flanger and becomes a chorus; shorter than half a
    /// millisecond takes the first peak out of the audible band altogether.</para>
    ///
    /// <para><b>The Mixer is on Average, for the Phaser's reason:</b> 50/50 is what makes a notch
    /// a notch. Any other balance leaves a residue of the louder copy sitting in it.</para>
    ///
    /// <para><b>Turning Zpětná vazba negative moves the resonances but does not simply invert the
    /// effect</b>, and the measurement says why. Negative feedback puts the loop's peaks on the ODD
    /// multiples of 1/2Čas — measured at −0,7 and Čas 3 ms, 167/500/833 Hz come out +2,0/+1,7/+0,4
    /// dB while 333/667/1000 sit at −2,0/−2,0/−2,2 — but the dry-plus-wet sum has its own comb, and
    /// that one still peaks at the multiples of 1/Čas. The two disagree, each landing in the
    /// other's null, so the whole response flattens to about ±2 dB instead of resonating. It is a
    /// real and usable colour — thin and phasey rather than whistly — but the block ships positive,
    /// which is what "flanger" means to anyone reaching for one.</para>
    /// </summary>
    private static BlockDefinition ResonantFlanger()
    {
        var builder = new BlockBuilder("Rezonanční flanger",
            "Krátké Zpoždění, kterému LFO plynule ježdí Časem, proti suchému signálu — a hlavně " +
            "se Zpětnou vazbou, díky které hřeben nejen krájí, ale i zvoní. Bez ní je to chorus " +
            "s jednou hlavou. Zápornou Zpětnou vazbou se hřeben přeladí na liché půlnásobky " +
            "a zvuk ztenčí.");

        // The dry copy is half the effect, so the input has to reach two places: the delay and the
        // mixer. Same pass-through Gain the Chorus and Phaser blocks use.
        var input = builder.Add(new GainModule(), 0, 0);

        var lfo = builder.Add(new LfoModule(), 0, 170);
        BlockBuilder.Knob(lfo, "Waveform").Value = 3;      // triangle — crosses the range evenly
        BlockBuilder.Knob(lfo, "Frequency").Value = 0.2;

        var head = builder.Add(new DelayLineModule(), 280, 60);
        BlockBuilder.Drive(head, "Čas", baseValue: 3, amount: 2.4);   // sweeps 0,6-5,4 ms

        // Half, not the 0,7 this shipped with for an afternoon. A resonant comb has gain by
        // construction, and it is worth knowing how much before turning the knob up: measured on a
        // 0,7-peak input, the block's own peak runs 0,70 at feedback 0 · 0,85 at 0,3 · 0,93 at
        // 0,4 · 1,05 at 0,5 · 1,22 at 0,6 · 1,51 at 0,7 · 3,43 at 0,9. Half is where the resonance
        // is unmistakable (+6,8 dB peaks) while the output still lands about where it came in.
        // Above it, put a Gain after the block.
        BlockBuilder.Knob(head, "Zpětná vazba").Value = 0.5;

        var mix = builder.Add(new MixerModule(), 560, 0);
        BlockBuilder.Knob(mix, "Mode").Value = 1;          // Average — 50/50 is what makes a notch

        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(head, "In"));
        builder.Wire(BlockBuilder.Out(lfo, "Out"), BlockBuilder.In(head, "Čas"));
        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(mix, "In 1"));
        builder.Wire(BlockBuilder.Out(head, "Out"), BlockBuilder.In(mix, "In 2"));

        builder.Publish("Vstup", BlockBuilder.In(input, "In"));
        builder.Publish("Výstup", BlockBuilder.Out(mix, "Out"));

        return builder.Build();
    }

    /// <summary>
    /// Phaser: four all-pass stages against the dry signal, all swept by one LFO.
    ///
    /// <para><b>The chain on its own is inaudible</b> — measured flat to ±0,064 dB between 30 Hz
    /// and 16 kHz, which is what all-pass means. What is heard is the SUM with the dry copy: where
    /// a stage has rotated the phase by 180°, the two cancel. Four second-order stages cross 180°
    /// four times, so there are four notches, measured at 0,36×, 0,81×, 1,23× and 2,74× the Cutoff,
    /// between −40 and −63 dB deep. That is also why the Mixer is on Average: 50/50 is what makes
    /// a notch a notch, and any other balance leaves a residue behind it.</para>
    ///
    /// <para><b>One LFO for all four, which is the opposite of what the Chorus block does.</b>
    /// There the three rates are deliberately unrelated; here the shared sweep IS the effect. The
    /// notch frequencies are proportional to Cutoff, so one LFO slides the whole pattern with its
    /// ratios intact — measured 0,36/0,81/1,23/2,77 at Cutoff 200 Hz and 0,36/0,82/1,22/2,68
    /// at 1800 Hz.</para>
    ///
    /// <para><b>The four stages share one Cutoff, and that is not laziness.</b> A hardware phaser
    /// staggers them, but modulation here is additive in the parameter's own unit (todo.md 12,
    /// cross-cutting limitation 3), so one LFO adds the same hertz to every stage. Measured with
    /// stages staggered over 300/700/1600/3600 Hz: at the bottom of the sweep the low stage runs
    /// into the 20 Hz floor and two of the four notches leave the band, while uniform stages keep
    /// all four and simply scale.</para>
    ///
    /// <para><b>Resonance pulls the notches together instead of sharpening them</b>, so it is left
    /// at the module default of 1. Measured at Cutoff 800: Res 0,5 spreads them over 159–3921 Hz,
    /// Res 1 over 288–2204 Hz, Res 10 over 710–902 Hz and 20 dB shallower. It is the knob for how
    /// wide the comb is, and it reads backwards from its name.</para>
    ///
    /// <para><b>⚠️ The block can raise the peak even though its magnitude response is flat.</b>
    /// All-pass rotates phase, and that reshapes the waveform: a 110 Hz saw peaking at 1,0 comes
    /// out at 1,879 (+5,5 dB) while its RMS DROPS 2,35 dB; white noise grows +4,4 dB. Flat is not
    /// the same as unchanged. The input Gain is the knob to turn if it clips.</para>
    /// </summary>
    private static BlockDefinition Phaser()
    {
        var builder = new BlockBuilder("Phaser",
            "Čtyři All-pass filtry za sebou proti suchému signálu. Samotný řetěz zní přesně jako " +
            "vstup — zádrže dělá až ten součet, a jedno LFO hýbe Cutoffem všech čtyř naráz, jinak " +
            "se vzor rozpadne. Resonance zádrže stahuje k sobě, nezostřuje je.");

        // The dry copy is half the effect, so the input has to reach two places: the first stage
        // and the mixer.
        var input = builder.Add(new GainModule(), 0, 0);

        // Triangle, not the default sine: the sweep is meant to pass evenly through the range and
        // a sine dawdles at the ends. Measured over 20 s, the share of samples in the bottom,
        // middle and top third of the range is 39,2/21,6/39,2 % for a sine and 33,3/33,3/33,3 %
        // for a triangle.
        var lfo = builder.Add(new LfoModule(), 440, 240);
        BlockBuilder.Knob(lfo, "Waveform").Value = 3;
        BlockBuilder.Knob(lfo, "Frequency").Value = 0.35;

        ModuleBase previous = input;
        for (var i = 0; i < 4; i++)
        {
            var stage = builder.Add(new FilterModule { Label = $"{i + 1}" }, 240 + i * 200, 0);
            BlockBuilder.Knob(stage, "Type").Value = 5;                          // All-pass
            BlockBuilder.Drive(stage, "Cutoff", baseValue: 1000, amount: 800);   // sweeps 200-1800 Hz

            builder.Wire(BlockBuilder.Out(previous, "Out"), BlockBuilder.In(stage, "In"));
            builder.Wire(BlockBuilder.Out(lfo, "Out"), BlockBuilder.In(stage, "Cutoff"));
            previous = stage;
        }

        var mix = builder.Add(new MixerModule(), 1080, 0);
        BlockBuilder.Knob(mix, "Mode").Value = 1;
        builder.Wire(BlockBuilder.Out(input, "Out"), BlockBuilder.In(mix, "In 1"));
        builder.Wire(BlockBuilder.Out(previous, "Out"), BlockBuilder.In(mix, "In 2"));

        builder.Publish("Vstup", BlockBuilder.In(input, "In"));
        builder.Publish("Výstup", BlockBuilder.Out(mix, "Out"));

        return builder.Build();
    }

    /// <summary>
    /// Unison: one pitch, four saws a few cents apart.
    ///
    /// <para><b>The detune is the modulation depth, not a knob.</b> Each oscillator's `Frequency`
    /// sits at a base of nought with the incoming pitch scaled by a ratio — 1,00405 is seven cents
    /// up — so the spread is a RATIO and stays the same interval at every pitch. A detune added in
    /// hertz would be a wide beat in the bass and inaudible at the top, which is cross-cutting
    /// limitation 3 in todo.md 12 doing exactly what it says it does.</para>
    ///
    /// <para>With nothing plugged into `Hz` the oscillators fall to the lowest note they will
    /// take — a low buzz, not silence, which is the block saying it wants a pitch.</para>
    /// </summary>
    private static BlockDefinition Unison()
    {
        var builder = new BlockBuilder("Unisono",
            "Čtyři pily z jedné výšky, každá o pár centů vedle. Rozladění se nastavuje " +
            "hloubkou modulace u Frequency: je to poměr, ne přičtené hertze, takže drží " +
            "stejný interval nahoře i dole. Zapoj do Hz Notu nebo Sekvencer.");

        var pitch = builder.Add(new GainModule(), 0, 0);

        var mix = builder.Add(new MixerModule(), 480, 0);
        BlockBuilder.Knob(mix, "Mode").Value = 1;

        // -14, -5, +5 and +14 cents as ratios: 2^(cents/1200).
        var spread = new (int Cents, double Ratio)[]
        {
            (-14, 0.991944), (-5, 0.997116), (5, 1.002890), (14, 1.008117),
        };
        for (var i = 0; i < spread.Length; i++)
        {
            var (cents, ratio) = spread[i];

            var saw = builder.Add(new OscillatorModule { Label = $"{cents:+0;-0} c" }, 240, 150 * i);
            BlockBuilder.Knob(saw, "Waveform").Value = 2;          // Saw
            BlockBuilder.Knob(saw, "Amplitude").Value = 0.8;

            // Vyhlazení hran zapnuté, i když modul ho má vypnuté. Měřeno na jedné pile 220 Hz:
            // mimo její harmonickou řadu je -22,2 dB energie bez vyhlazení a -38,3 dB s ním. Na
            // jedné pile se to dá přejít, tady ne — čtyři rozladěné pily mají čtyři různé sady
            // ohnutých složek, ty spolu nejsou v žádném vztahu a tlučou proti sobě.
            BlockBuilder.Knob(saw, OscillatorModule.AntialiasName).Value = 1;
            BlockBuilder.Drive(saw, "Frequency", baseValue: 0, amount: ratio);

            builder.Wire(BlockBuilder.Out(pitch, "Out"), BlockBuilder.In(saw, "Frequency"));
            builder.Wire(BlockBuilder.Out(saw, "Out"), BlockBuilder.In(mix, $"In {i + 1}"));
        }

        builder.Publish("Hz", BlockBuilder.In(pitch, "In"));
        builder.Publish("Výstup", BlockBuilder.Out(mix, "Out"));

        return builder.Build();
    }

    /// <summary>
    /// A clock and a slower pulse locked to it.
    ///
    /// <para><b>The whole block is one thing nobody guesses:</b> the metronome is
    /// <see cref="PoissonTriggerModule"/> with Nepravidelnost at 0. Its name says the opposite, and
    /// yet at 0 the gap it draws is a constant (<see cref="IrregularTiming.DrawInterval"/>) —
    /// measured on a 4/s stream, every gap is exactly 11 025 samples and forty pulses later the
    /// drift is 0. That is why 12a asks for "hodiny a dělič" and only the divider is a new module:
    /// a Clock module would have been this box with a different label on it.</para>
    ///
    /// <para>Both pulses come out twice, as Trig and as Gate, because they are not
    /// interchangeable: a one-sample Trig wired into an Envelope's Gate renders exact silence
    /// (measured — the falling edge lands on the next sample and the envelope releases from 0).
    /// Trig is for a Vzorkovač, Gate for an obálka.</para>
    /// </summary>
    private static BlockDefinition Clock()
    {
        var builder = new BlockBuilder("Hodiny",
            "Takt a k němu pomalejší puls, který se s ním nemůže rozejít. Hodiny jsou Nepravidelný " +
            "spouštěč s Nepravidelností 0 — pak je to přesný metronom a Četnost je navázaná na " +
            "tempo. Dělič pouští každý N-tý impuls; Posunem se dá vybrat, který z nich, takže druhý " +
            "dělič se stejným dělitelem a jiným posunem hraje protiváhu. Do Vzorkovače veď Trig, " +
            "do obálky Gate: jednovzorkový Trig obálku nespustí vůbec.");

        var clock = builder.Add(new PoissonTriggerModule { Label = "(hodiny)" }, 0, 0);
        BlockBuilder.Knob(clock, "Nepravidelnost").Value = 0;
        var rate = BlockBuilder.Knob(clock, "Četnost");
        rate.IsTempoSynced = true;
        rate.DivisionName = "1/8";
        BlockBuilder.Knob(clock, "Délka gate").Value = 0.03;

        var divider = builder.Add(new PulseDividerModule(), 260, 0);
        BlockBuilder.Knob(divider, "Dělitel").Value = 4;

        builder.Wire(BlockBuilder.Out(clock, "Trig"), BlockBuilder.In(divider, "Trig"));

        builder.Publish("Reset", BlockBuilder.In(divider, "Reset"));
        builder.Publish("Krok", BlockBuilder.Out(clock, "Trig"));
        builder.Publish("Krok gate", BlockBuilder.Out(clock, "Gate"));
        builder.Publish("Dělené", BlockBuilder.Out(divider, "Trig"));
        builder.Publish("Dělené gate", BlockBuilder.Out(divider, "Gate"));

        return builder.Build();
    }

    /// <summary>
    /// Random notes that stay in a key: a metronome steps a sample-and-hold, the held value is
    /// quantized to a scale, and the note change opens a gate.
    ///
    /// <para>Three things here are each invisible on their own and fatal together. The
    /// <see cref="SampleHoldModule"/> needs a trigger or it holds one value for ever — its own
    /// noise is normalled to its input, so nothing feeds it but the clock. The
    /// <see cref="QuantizerModule"/> is what stops the result being microtonal: measured on the
    /// same staircase, a raw sample-and-hold on a Frequency knob lands 26.2 cents from the nearest
    /// semitone on average and up to 49.0, and through the quantizer it is 0.0001 cents. And the
    /// envelope has to be driven from Gate, not Trig, for the reason spelt out in
    /// <see cref="Clock"/>.</para>
    /// </summary>
    private static BlockDefinition RandomMelody()
    {
        var builder = new BlockBuilder("Náhodná melodie",
            "Hodiny krokují Vzorkovač, ten drží náhodnou hodnotu a Kvantizér ji zaokrouhlí na tón " +
            "stupnice. Hz veď do Frequency oscilátoru se základem 0 a hloubkou 1, Gate do obálky. " +
            "Vzorkovač si bere vlastní šum, takže melodii mění Seed; stupnici a tóninu nastavíš " +
            "na Kvantizéru. Bez Kvantizéru by tatáž náhoda hrála mimo — měřeno 26 centů vedle.");

        var clock = builder.Add(new PoissonTriggerModule { Label = "(hodiny)" }, 0, 0);
        BlockBuilder.Knob(clock, "Nepravidelnost").Value = 0;
        var rate = BlockBuilder.Knob(clock, "Četnost");
        rate.IsTempoSynced = true;
        rate.DivisionName = "1/8";

        var sample = builder.Add(new SampleHoldModule(), 260, 0);

        var quantizer = builder.Add(new QuantizerModule(), 520, 0);
        // An octave each way rather than the module's own two: a melody that leaps four octaves
        // reads as a malfunction, and the point of the block is to sound like music at once.
        BlockBuilder.Knob(quantizer, "Rozsah").Value = 12;
        BlockBuilder.Knob(quantizer, "Délka gate").Value = 0.15;

        builder.Wire(BlockBuilder.Out(clock, "Trig"), BlockBuilder.In(sample, "Trig"));
        builder.Wire(BlockBuilder.Out(sample, "Out"), BlockBuilder.In(quantizer, "In"));

        builder.Publish("Hz", BlockBuilder.Out(quantizer, "Hz"));
        builder.Publish("Gate", BlockBuilder.Out(quantizer, "Gate"));

        return builder.Build();
    }

    /// <summary>
    /// A sound in two sections that do not overlap, timed by one envelope instead of by drawn
    /// curves: the first section is the conductor's own <c>Env</c>, the second is gated by its
    /// <c>S</c>.
    ///
    /// <para><b>The whole trick is Sustain at 0, and it is a knob, not a module.</b> Sustain here
    /// is a LEVEL, so at 0 the conductor's <c>Env</c> rises through Attack, falls back to nothing
    /// through Decay and stays there — the first section ends by itself, with no second curve drawn
    /// to silence it. The plateau that follows is silent in <c>Env</c> and is exactly where
    /// <c>S</c> holds at 1, so the two sections cannot overlap however the knobs are moved. That is
    /// the difference from drawing it: in <c>Sestavy/laser_vystrel.zvuky</c> the seam is the number
    /// 0,75 typed into three separate curves, and moving it means editing all three by hand.</para>
    ///
    /// <para><b>Why S and none of the others.</b> <c>A</c>, <c>D</c> and <c>R</c> ramp 1 → 0 across
    /// their stage, so wired straight into a Gate they would shut it half way through themselves.
    /// <c>S</c> is the one socket that holds — see <c>EnvelopeModule.WriteStage</c> and todo.md
    /// 13a — which is what makes it the only usable gate of the four without a threshold in
    /// between. (With one it is four; the recipe is in `poznamky_syntezy.md`.)</para>
    ///
    /// <para><b>Measured</b> on <c>Sestavy/laser_vystrel_obalka.zvuky</c>, which is this
    /// arrangement with sound hung off it: the conductor's <c>S</c> stands at 1 from 0,640 s to
    /// 1,140 s — Attack plus Decay, then Držení long — the first section sounds from 0,02 s to
    /// 0,63 s and the second from 0,64 s, and a <see cref="KarplusStrongModule"/> on Spoušť plucks
    /// at 0,6407 s. The two never sound together, and one turn of Attack moves both.</para>
    ///
    /// <para><b>And measured on the block itself</b>, with a carrier tone through one Gain per
    /// connector: Díl 1 sounds 0,005-0,595 s, Díl 2 0,600-1,245 s, and the two share not one
    /// 5 ms window. Both start where the arithmetic says — Attack plus Decay — rather than near
    /// it.</para>
    ///
    /// <para><b>The trap this block exists to spell out:</b> Držení belongs on the CONDUCTOR. On
    /// the second envelope it does nothing at all, because its Gate is wired and a wired gate
    /// decides the length (<see cref="EnvelopeModule"/>) — a knob that looks like the answer and
    /// is not.</para>
    /// </summary>
    private static BlockDefinition TwoSections()
    {
        var builder = new BlockBuilder("Dva díly",
            "Zvuk o dvou dílech, které se nepřekrývají, časovaný jednou obálkou místo kreslených " +
            "křivek. Trik je Sustain na 0: je to HLADINA, takže Díl 1 sám dozní a plošinka za ním " +
            "je přesně to místo, kde drží S — odtud běží Díl 2. Jak je dlouhý, říká Držení " +
            "DIRIGENTA; na druhé obálce Držení nedělá nic, protože má zapojený Gate a délku pak " +
            "určuje brána. Díly veď do Gainu se základem 0 a hloubkou 1, Spoušť do Trig (Struna, " +
            "Vzorkovač). Hranu mezi díly nekreslíš — posuneš Attack a posunou se oba. Dozvuk je " +
            "Release dirigenta; začíná přesně na konci plošinky, ale s doznívajícím Dílem 2 se " +
            "překrývá — každý má vlastní Release.");

        var conductor = builder.Add(new EnvelopeModule { Label = "(dirigent)" }, 0, 0);
        BlockBuilder.Knob(conductor, "Attack").Value = 0.5;
        BlockBuilder.Knob(conductor, "Decay").Value = 0.1;

        // The knob the whole block turns on. Sustain is a level, so 0 is what makes the first
        // section end on its own instead of being cut off by something drawn.
        BlockBuilder.Knob(conductor, EnvelopeModule.HoldName).Value = 0.4;
        BlockBuilder.Knob(conductor, "Sustain").Value = 0;
        BlockBuilder.Knob(conductor, "Release").Value = 0.3;

        // Gated, never free-running: with nothing in its Gate this would fall back to the one-shot
        // arithmetic, start at the beginning of the buffer and land on top of the first section —
        // the exact overlap the block is built to prevent.
        var second = builder.Add(new EnvelopeModule { Label = "(díl 2)" }, 300, 220);
        BlockBuilder.Knob(second, "Attack").Value = 0.002;
        BlockBuilder.Knob(second, "Decay").Value = 0.25;
        BlockBuilder.Knob(second, "Sustain").Value = 0.07;
        BlockBuilder.Knob(second, "Release").Value = 0.3;

        builder.Wire(BlockBuilder.Out(conductor, "S"), BlockBuilder.In(second, "Gate"));

        builder.Publish("Díl 1", BlockBuilder.Out(conductor, "Env"));
        builder.Publish("Spoušť", BlockBuilder.Out(conductor, "S"));
        builder.Publish("Díl 2", BlockBuilder.Out(second, "Env"));

        // The third section this arrangement has for free: the conductor's release, which begins
        // the sample its plateau ends and so cannot start early. NOT a third non-overlapping
        // section though — measured at these settings it runs 1,000-1,295 s while Díl 2 is still
        // releasing until 1,245 s, because the two releases are separate knobs. Published anyway,
        // because nothing else in the block would show that the socket is there at all.
        builder.Publish("Dozvuk", BlockBuilder.Out(conductor, "R"));

        return builder.Build();
    }
}
