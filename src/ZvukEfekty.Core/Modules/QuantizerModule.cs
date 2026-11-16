using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Rounds a control signal to the nearest note of a scale and puts the result out as "Hz" — the
/// module that makes a random value musical instead of merely out of tune.
///
/// <para><b>Why nothing here could stand in for it.</b> Anything random driving an oscillator's
/// Frequency lands between the notes: the modulation is added in hertz (cross-cutting limitation 3
/// in `todo.md`), so an evenly spaced signal is evenly spaced in hertz, which is not evenly spaced
/// in pitch. There is no rounding anywhere in the engine that knows what a scale is, and
/// <see cref="FormulaModule"/> is deliberately not allowed to close a catalog item (12d). Wire
/// <see cref="SampleHoldModule"/> into this and its staircase becomes a melody.</para>
///
/// <para><b>The input is read as semitones around the root</b>, scaled by "Rozsah": ±1 in covers
/// ±Rozsah semitones, so the bipolar −1…1 that noise, an LFO and a sample-and-hold all produce
/// spans symmetrically above and below the key. An unwired input is 0, which is the root note held
/// — a legitimate use in itself, since the module is then a note-picked constant Hz source.</para>
///
/// <para><b>Output is hertz, like <see cref="NoteModule"/> and <see cref="SequencerModule"/></b>,
/// so whatever a sequencer drives, this drives too: an Oscillator's Frequency with base 0 and
/// depth 1. "Trig" and "Gate" both mark the sample the note CHANGES — the pitch itself is held
/// continuously, so without them nothing downstream can tell that a new note has started.</para>
///
/// <para><b>Why both, when they mark the same instant.</b> Measured: a one-sample impulse wired
/// into <see cref="EnvelopeModule"/>'s Gate renders exact silence — the falling edge arrives on
/// the next sample and sends the envelope into release from a level of 0, so it never rises at
/// all. An envelope needs a gate with a LENGTH, which is what "Délka gate" is for and why
/// <see cref="PoissonTriggerModule"/> carries the same pair. "Trig" is for what only needs the
/// instant: a second sample-and-hold, a divider, a re-seed.</para>
///
/// <para><b>Where it ends.</b> A signal sitting on the boundary between two notes chatters, and
/// every wobble across it is another "Trig". The cure is not hysteresis in here but a
/// <see cref="SampleHoldModule"/> in front, which is where the decision "a new note starts now"
/// belongs — see the Description.</para>
/// </summary>
public sealed class QuantizerModule : ModuleBase
{
    /// <summary>Every MIDI note by name, so the key is picked as "C4" rather than as 60 — the same
    /// call, and the same array, as the sampler's "Základní nota".</summary>
    private static readonly string[] NoteNames =
        Enumerable.Range(0, 128).Select(MidiNotes.Name).ToArray();

    private static readonly string[] ScaleNames =
    {
        "Chromatická", "Dur", "Moll", "Harmonická moll", "Pentatonika dur", "Pentatonika moll",
        "Blues", "Dórská", "Frygická", "Lydická", "Mixolydická", "Celotónová", "Zmenšená",
        "Kvinty", "Oktávy",
    };

    /// <summary>Each scale as its steps in semitones from the root, in the order
    /// <see cref="ScaleNames"/> lists them. Written out rather than derived by rotating the major
    /// scale, because the last four are not modes of anything.</summary>
    private static readonly int[][] Scales =
    {
        new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
        new[] { 0, 2, 4, 5, 7, 9, 11 },
        new[] { 0, 2, 3, 5, 7, 8, 10 },
        new[] { 0, 2, 3, 5, 7, 8, 11 },
        new[] { 0, 2, 4, 7, 9 },
        new[] { 0, 3, 5, 7, 10 },
        new[] { 0, 3, 5, 6, 7, 10 },
        new[] { 0, 2, 3, 5, 7, 9, 10 },
        new[] { 0, 1, 3, 5, 7, 8, 10 },
        new[] { 0, 2, 4, 6, 7, 9, 11 },
        new[] { 0, 2, 4, 5, 7, 9, 10 },
        new[] { 0, 2, 4, 6, 8, 10 },
        new[] { 0, 1, 3, 4, 6, 7, 9, 10 },
        new[] { 0, 7 },
        new[] { 0 },
    };

    /// <summary>For every scale, where each of the twelve semitones goes. Built once for the whole
    /// program: it depends on nothing but the scale, and the alternative is searching the interval
    /// list on every sample.</summary>
    private static readonly int[][] SnapTables = Scales.Select(BuildSnapTable).ToArray();

    /// <summary>MIDI note to hertz, precomputed. The conversion is a <see cref="Math.Pow"/> and
    /// there are only 128 answers.</summary>
    private static readonly float[] Frequencies =
        Enumerable.Range(0, 128).Select(n => (float)MidiNotes.ToFrequencyHz(n)).ToArray();

    private readonly Port _input;
    private readonly Port _hzOutput;
    private readonly Port _trigOutput;
    private readonly Port _gateOutput;
    private readonly ModuleParameter _root;
    private readonly ModuleParameter _scale;
    private readonly ModuleParameter _range;
    private readonly ModuleParameter _gateLength;

    /// <summary>The note that came out last, so "Trig" can fire on a change. Held across blocks for
    /// the same reason the sequencer's position is — a live voice arrives here in short pieces.</summary>
    private int _lastNote;

    private int _gateSamplesLeft;

    public QuantizerModule() : base("Kvantizér")
    {
        _input = AddInput("In");

        _hzOutput = AddOutput("Hz");
        _trigOutput = AddOutput("Trig");
        _gateOutput = AddOutput("Gate");

        _root = AddParameter("Základní tón", MidiNotes.MiddleC, 0, NoteNames.Length - 1, options: NoteNames);

        // Minor pentatonic by default, not chromatic. A chromatic quantizer rounds a random signal
        // to twelve notes, and twelve notes in random order still sound wrong — the module would
        // look broken to whoever dropped it in to find out what it does. The pentatonic has no
        // semitone in it, so nothing it produces can clash.
        _scale = AddParameter("Stupnice", 5, 0, ScaleNames.Length - 1, options: ScaleNames);

        // Two octaves each way at full swing. Modulatable, and that is not decoration: a slow
        // envelope on it opens the melody out as it goes, which is a range, not a transposition.
        _range = AddParameter("Rozsah", 24, 1, 60, "půltónů");

        // How long the note is held open for whatever is downstream. A length rather than a share
        // of the note, because a quantizer has no idea how long the note will last — the next one
        // arrives when the signal moves, which may be never.
        _gateLength = AddParameter("Délka gate", 0.05, 0.0005, 2, "s", tempoSync: TempoSyncKind.Seconds);

        ResetState();
    }

    public override string Description =>
        "Zaokrouhlí řídicí signál na nejbližší tón stupnice a vydá ho jako Hz — zapoj na Frequency " +
        "oscilátoru se základem 0 a hloubkou 1, stejně jako Sekvencer. Bez něj hraje cokoli " +
        "náhodného mimo, protože modulace se přičítá v hertzích, ne v půltónech. Vstup se čte jako " +
        "půltóny kolem Základního tónu: ±1 na vstupu je ±Rozsah půltónů, takže −1 až 1 z Vzorkovače " +
        "nebo LFO sahá symetricky nad i pod zvolenou notu. Nezapojený vstup drží Základní tón. " +
        "Trig a Gate označí chvíli, kdy se nota změní: Trig je jeden vzorek, Gate drží nastavenou " +
        "dobu. Do Gate obálky patří Gate — jednovzorkový impuls obálku nespustí vůbec, změřeno " +
        "jako přesné ticho. " +
        "Před kvantizér patří Vzorkovač (S&H): sám o sobě kvantizér notu nedrží, takže signál " +
        "kmitající přesně na hranici dvou tónů přeskakuje sem a tam.";

    public override void ResetState()
    {
        _lastNote = int.MinValue;
        _gateSamplesLeft = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var range = GetModulatedValues(_range, ctx, inputBuffers);
        var gateLength = GetModulatedValues(_gateLength, ctx, inputBuffers);

        // Both are pickers, so they cannot move within a block and are read once.
        var root = Math.Clamp((int)Math.Round(_root.Value), 0, NoteNames.Length - 1);
        var table = SnapTables[Math.Clamp((int)Math.Round(_scale.Value), 0, SnapTables.Length - 1)];

        var hz = NewBuffer(ctx);
        var trig = NewBuffer(ctx);
        var gate = NewBuffer(ctx);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Floor(x + 0.5) rather than Math.Round, which rounds halves to even: on a staircase
            // input that would send 1.5 up and 2.5 down, so the same distance from a note would
            // resolve one way in one octave and the other way in the next.
            // Clamped first, because the input is whatever someone wired in: an audio signal at
            // full range times a wide Rozsah is still only a few hundred semitones, but a runaway
            // Formule can hand over an infinity, and that would saturate the cast to int.MaxValue
            // and overflow the octave arithmetic below. Anything past ±1524 is off the MIDI range
            // in either direction anyway, so the clamp cannot change an audible result.
            var semitones = (int)Math.Floor(Math.Clamp(input[i] * range[i], -1524f, 1524f) + 0.5);

            // Floor, not integer division: division truncates towards zero, so everything in the
            // octave below the root would fold into the root's own.
            var octave = (int)Math.Floor(semitones / 12.0);
            var note = Math.Clamp(root + octave * 12 + table[semitones - octave * 12], 0, Frequencies.Length - 1);

            hz[i] = Frequencies[note];

            if (note != _lastNote)
            {
                trig[i] = 1;
                // At least one sample, so a gate shorter than the sample period is still an event
                // — the same call PoissonTriggerModule and PulseDividerModule make.
                _gateSamplesLeft = Math.Max(1, (int)Math.Round(Math.Max(0, gateLength[i]) * ctx.SampleRate));
                _lastNote = note;
            }

            if (_gateSamplesLeft > 0)
            {
                gate[i] = 1;
                _gateSamplesLeft--;
            }
        }

        return new Dictionary<Port, float[]> { [_hzOutput] = hz, [_trigOutput] = trig, [_gateOutput] = gate };
    }

    /// <summary>
    /// Where each of the twelve semitones lands in <paramref name="intervals"/>: the nearest step
    /// of the scale, which may be the one below (0 for a semitone under the root) or the octave
    /// above (12 for a semitone under it). The neighbouring octaves are searched precisely so those
    /// two cases come out right — a table clamped inside one octave would pull the top semitone
    /// down a seventh instead of up to the root.
    /// </summary>
    private static int[] BuildSnapTable(int[] intervals)
    {
        var table = new int[12];

        for (var pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            var best = 0;
            var bestDistance = int.MaxValue;

            foreach (var octave in new[] { -12, 0, 12 })
            foreach (var interval in intervals)
            {
                var candidate = interval + octave;
                var distance = Math.Abs(candidate - pitchClass);

                // Exact ties go up, the same direction the rounding in Process goes, so a value
                // halfway between two scale notes cannot land on different ones depending on which
                // side of the arithmetic it came through.
                if (distance < bestDistance || (distance == bestDistance && candidate > best))
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            table[pitchClass] = best;
        }

        return table;
    }
}
