using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Plays a written-down melody: walks a list of notes in time and puts out the same two signals
/// <see cref="NoteModule"/> does — "Hz" for the pitch and "Gate" for whether a note is sounding.
/// Wire them into an Oscillator's Frequency (base 0, depth 1) and an Envelope's Gate and the rest
/// of the patch neither knows nor cares that a sequence is driving it.
///
/// <para><b>Why this exists.</b> Before it, the engine had no way to place notes in time, and the
/// two melody patches in <c>Sestavy/</c> show what that cost. One spells the tune out as
/// breakpoints of two <see cref="CurveModule"/> shapes — a pitch staircase and a rectangle per
/// note — where every note is a pair of x coordinates computed against the clip length. The other
/// walks a trigger down a chain of twenty-eight <see cref="DelayLineModule"/>s, because that
/// module is capped at 250 ms and a six-second melody therefore needs a shift register. Both
/// play correctly and neither can be edited: changing one note means recomputing coordinates or
/// rewiring taps.</para>
///
/// <para><b>Pitch is a MIDI note, not hertz</b>, and length is a count of steps, not seconds. Both
/// are the units the melody is actually written in, so transposing is adding to a number and
/// changing the tempo is one knob — and "Délka kroku" is tempo-syncable, so the sequence can be
/// pinned to the grid rather than to a stopwatch.</para>
///
/// <para><b>Timing is an accumulator, not a sample count</b>, so that a modulated or tempo-synced
/// step length changes the speed from here on instead of moving the notes already played. The
/// price is that a step boundary lands where the accumulated position crosses it rather than on
/// an exact sample: measured at one sample late (23 µs), and measured NOT to grow — still one
/// sample after two hundred steps.</para>
///
/// <para><b>"Clock" and "Reset", and what they are NOT for.</b> `todo.md` 9d put the clock input
/// off with "two sequencers cannot be tied together", and that turned out to be the wrong reason:
/// measured over 60 s, two sequencers of different lengths on the same step length keep every
/// boundary within <b>0 samples</b> of each other, and so do 1/8 against 1/4 at 128 BPM (a step of
/// 10 335.9375 samples — not a whole number, and still exact) and two whose step lengths are
/// modulated by one shared LFO. They accumulate identically, so they have no way to drift.</para>
///
/// <para>What could not be done is being stepped by something that is not a metronome — the new
/// <see cref="PulseDividerModule"/>, an irregular <see cref="PoissonTriggerModule"/>, a Trig off
/// <see cref="EnvelopeFollowerModule"/>. Faking it by modulating the step length fails visibly:
/// with a square LFO trying to make swing, the first gaps alternate 12 843 / 7 497 samples and
/// then fall apart into 11 333 / 9 006 / 9 723, because the LFO runs against boundaries it is
/// itself moving. A clock cannot do that: a step lands where a pulse lands.</para>
///
/// <para><b>A wired Clock takes over the time</b> and "Délka kroku" stops being read for the step
/// — the same either/or <see cref="EnvelopeModule"/> makes on its Gate. The one thing that still
/// needs a duration is <see cref="_articulation"/>, which is a fraction OF THE STEP and therefore
/// needs to know how long a step is going to be; "Délka kroku podle" picks where that comes from,
/// the measured gap between the last two pulses or the knob. See the Description.</para>
///
/// <para>State lives in fields rather than locals because a live voice renders in a stream of
/// short blocks and the sequence has to carry on across them; a batch render calls
/// <see cref="ResetState"/> first and gets the same result in one pass. Same split as
/// <see cref="CurveModule"/>'s sweep position, and it is what the batch-vs-blocks assertion in the
/// test harness checks.</para>
/// </summary>
public sealed class SequencerModule : ModuleBase
{
    public const string StepLengthName = "Délka kroku";
    public const string SequenceName = "Kroky";

    private static readonly string[] LoopNames = { "Ne", "Ano" };

    /// <summary>Where the length of a step comes from while a clock is running — see the class
    /// remarks. "Hodin" measures the gap between the last two pulses, "Knoflíku" keeps reading
    /// <see cref="_stepLength"/>, which is what gives every note the same duration however
    /// irregularly the pulses arrive.</summary>
    private static readonly string[] LengthSourceNames = { "Hodin", "Knoflíku" };

    /// <summary>Where a pulse on Clock or Reset counts as having arrived. The same fixed 0.5 as
    /// everywhere else in the engine — every trigger here is already 0 or 1.</summary>
    private const double TriggerThreshold = 0.5;

    /// <summary>How far into a step the position may creep between two clock pulses. Not all the
    /// way to the next whole step: crossing that boundary is the clock's job, and a position that
    /// arrived there on its own would make the following pulse skip a step.</summary>
    private const double StepCeiling = 1 - 1e-9;

    /// <summary>How often the node's step display gets a frame while a batch render is being
    /// recorded — the same 1024 samples <see cref="ScopeModule"/> and <see cref="WaveMorphModule"/>
    /// use, and for the same reason: a batch render arrives as ONE enormous block, so publishing
    /// per block would record a single frame and the display would jump straight to the last step
    /// instead of walking the sequence.</summary>
    private const int PublishInterval = 1024;

    /// <summary>What a freshly dropped Sequencer plays: a major triad up an octave. Chosen so the
    /// module is audible the moment it is wired, rather than silent until the user has typed a
    /// melody — silence is the failure mode that looks like a broken patch.</summary>
    private static IEnumerable<SequenceStep> DefaultSteps() => new[]
    {
        new SequenceStep(MidiNotes.MiddleC),
        new SequenceStep(MidiNotes.MiddleC + 4),
        new SequenceStep(MidiNotes.MiddleC + 7),
        new SequenceStep(MidiNotes.MiddleC + 12),
    };

    private readonly Port _clock;
    private readonly Port _reset;
    private readonly Port _hzOutput;
    private readonly Port _gateOutput;
    private readonly ModuleParameter _stepLength;
    private readonly ModuleParameter _lengthSource;
    private readonly ModuleParameter _articulation;
    private readonly ModuleParameter _octave;
    private readonly ModuleParameter _loop;
    private readonly ModuleSequence _sequence;

    // Position along the sequence, in steps, fractional. Carried across blocks — see the class
    // remarks. Counted in steps rather than seconds so that a modulated or tempo-synced step
    // length changes the speed from here on without moving the notes already played.
    private double _position;
    private int _cursor;
    private double _lastHz;

    private double _previousClock;
    private double _previousReset;

    /// <summary>Samples since the last clock pulse, and the gap between the two before it. Zero
    /// means "not measured yet", which is what makes the first step fall back to the knob: there
    /// is nothing to measure until a second pulse has arrived.</summary>
    private int _samplesSinceClock;
    private int _clockInterval;

    /// <summary>True while a clocked sequencer has not been stepped yet, after a reset or at the
    /// start of a sound. The first pulse then STARTS the sequence rather than advancing it —
    /// otherwise the first note of every sequence would be the one nobody ever hears.</summary>
    private bool _awaitingFirstClock;

    /// <summary>Which step is sounding, or −1 for none — what the node's display is handed. Held
    /// as a field rather than computed at the end of a block because the two <c>continue</c> paths
    /// in the loop (waiting for the first pulse, run off the end) are exactly the cases the
    /// display has to show as "nothing", and they never reach the end of an iteration.</summary>
    private int _activeStep = -1;

    private long _totalSamplesRendered;

    public SequencerModule() : base("Sekvencer")
    {
        _clock = AddInput("Clock");
        _reset = AddInput("Reset");

        _hzOutput = AddOutput("Hz");
        _gateOutput = AddOutput("Gate");

        // Tempo-syncable, unlike an oscillator's Frequency: this one IS a duration. It is the
        // knob that makes a sequence sit on the grid instead of near it.
        _stepLength = AddParameter(StepLengthName, 0.25, 0.02, 4, "s", tempoSync: TempoSyncKind.Seconds);

        // Only ever read while a clock is wired, so it sits right under the knob it qualifies —
        // the same place "Držení" sits under the envelope's stages, and for the same reason: a
        // switch that changes what the knob above it means belongs next to that knob.
        _lengthSource = AddParameter("Délka kroku podle", 0, 0, LengthSourceNames.Length - 1,
            options: LengthSourceNames);

        // A fraction of the note rather than a time, so long notes stay long and short ones short.
        // What it decides is RETRIGGERING: below 1 the gate shuts before the note ends, so the next
        // one gets a rising edge and its own attack; at exactly 1 the gate never closes and the
        // envelope rides straight through, which is legato. It does NOT decide where the pitch
        // moves — that happens on the step boundary, at the same sample the gate opens, measured.
        // Which is why no gap has to be engineered to hide a portamento: at that sample the
        // envelope is at zero and on its way up.
        _articulation = AddParameter("Artikulace", 0.85, 0.05, 1);

        _octave = AddParameter("Oktáva", 0, -3, 3);
        _loop = AddParameter("Opakovat", 0, 0, LoopNames.Length - 1, options: LoopNames);

        _sequence = AddSequence(SequenceName, DefaultSteps());
    }

    public override string Description =>
        "Přehraje zapsanou melodii: výstup Hz zapoj na Frequency oscilátoru (základ 0, hloubka 1) " +
        "a výstup Gate na Gate obálky. Výška se zapisuje jako nota, délka jako počet kroků, takže " +
        "transpozice je přičtení a změna tempa je jeden knoflík. Artikulace říká, jakou část tónu " +
        "drží gate: pod 1 se každý tón spustí znovu (a mezi nimi je mezera), na 1 gate nezavře " +
        "vůbec a tóny jdou legato. Přeladí se vždy na hranici kroku, kde je obálka na nule, takže " +
        "přejezd výšky slyšet není. Délka kroku se dá navázat na tempo.\n\n" +
        "Zapojený Clock přebírá čas: na každý impuls se postoupí o krok a Délka kroku se pro " +
        "krokování přestane číst. Tím jde sekvenci hnát z Děliče impulsů, z Nepravidelného " +
        "spouštěče nebo z Trig na Sledovači obálky — tedy nepravidelně, se swingem nebo od zvuku. " +
        "Artikulace ale pořád potřebuje vědět, jak dlouhý krok je: Délka kroku podle = Hodin ji " +
        "odvodí z mezery mezi posledními dvěma impulsy (tón se tedy zkracuje s tempem), = Knoflíku " +
        "ji vezme z Délky kroku (každý tón je pak stejně dlouhý, ať impulsy chodí jakkoli). " +
        "Reset vrátí sekvenci na začátek; s hodinami ji rozehraje až další impuls.";

    /// <summary>The written melody. Exposed so the inspector can edit it and the serializer can
    /// write it; the module itself only ever reads it.</summary>
    public ModuleSequence Sequence => _sequence;

    /// <summary>How long the whole sequence takes at <paramref name="bpm"/>, in seconds. Exposed
    /// so the UI can say when a sequence runs past the clip — the same warning
    /// <see cref="EnvelopeModule.OneShotSecondsAt"/> exists for, and the same silent truncation if
    /// nobody checks.</summary>
    public double TotalSecondsAt(double bpm) => _sequence.TotalSteps * _stepLength.ResolveValue(bpm);

    public override void ResetState()
    {
        _position = 0;
        _cursor = 0;
        _lastHz = 0;

        _previousClock = 0;
        _previousReset = 0;
        _samplesSinceClock = 0;
        _clockInterval = 0;
        _awaitingFirstClock = true;

        _activeStep = -1;
        _totalSamplesRendered = 0;
    }

    /// <summary>
    /// The frame the node's display draws: one value per step, 1 on the step that is sounding.
    ///
    /// <para>One-hot rather than "the step number", because <see cref="VisualizationHub"/> SUMS
    /// what every sounding voice contributes. Sixteen held keys each running their own copy of the
    /// sequence are at sixteen different steps, and the average of those step numbers is a step
    /// none of them is on; summed one-hot frames instead light every step that any voice is on, and
    /// light it the more brightly the more voices are there.</para>
    /// </summary>
    private float[] StepFrame(int stepCount)
    {
        var frame = new float[stepCount];
        if (_activeStep >= 0 && _activeStep < stepCount)
            frame[_activeStep] = 1;

        return frame;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var stepLength = GetModulatedValues(_stepLength, ctx, inputBuffers);
        var articulation = GetModulatedValues(_articulation, ctx, inputBuffers);
        var octave = GetModulatedValues(_octave, ctx, inputBuffers);
        var loops = (int)_loop.Value == 1;

        // Asking the dictionary rather than GetInputOrSilence: "no clock" and "a clock that is
        // silent right now" have to mean different things, exactly as they do for the envelope's
        // Gate. Reset needs no such distinction — an unwired Reset simply never fires.
        var clocked = inputBuffers.TryGetValue(_clock, out var clock);
        var reset = GetInputOrSilence(inputBuffers, _reset, ctx);
        var lengthFromClock = (int)_lengthSource.Value == 0;

        var hz = NewBuffer(ctx);
        var gate = NewBuffer(ctx);

        // Snapshotted once per block, for the same reason CurveModule reads its shape once: the
        // inspector can edit this list from the UI thread while the audio thread is inside this
        // loop, and an index into a collection that grew underneath us is a crash rather than a
        // wrong note.
        var steps = _sequence.Steps.ToArray();
        if (steps.Length == 0)
            return new Dictionary<Port, float[]> { [_hzOutput] = hz, [_gateOutput] = gate };

        // Where each step starts, in steps. Cheaper than asking the list on every sample, and it
        // is what lets the cursor below only ever move forward.
        var starts = new double[steps.Length];
        var total = 0.0;
        for (var s = 0; s < steps.Length; s++)
        {
            starts[s] = total;
            total += steps[s].Length;
        }

        // The list can shrink from the inspector between two blocks, and the cursor is carried
        // across them. Cheaper to pull it back into range than to make every read below defensive.
        _cursor = Math.Clamp(_cursor, 0, steps.Length - 1);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            // Only while recording a batch render; a real-time block publishes once, after the
            // loop. Same split as VuMeterModule's, and for its reason: chunking inside a real-time
            // block would count one voice's block against itself several times in one pass.
            if (VisualizationHub.IsRecording && _totalSamplesRendered % PublishInterval == 0)
                VisualizationHub.Contribute(Id, StepFrame(steps.Length),
                    (double)_totalSamplesRendered / ctx.SampleRate);
            _totalSamplesRendered++;

            if (reset[i] >= TriggerThreshold && _previousReset < TriggerThreshold)
            {
                _position = 0;
                _cursor = 0;
                _awaitingFirstClock = true;
            }
            _previousReset = reset[i];

            if (clocked)
            {
                var rising = clock![i] >= TriggerThreshold && _previousClock < TriggerThreshold;
                _previousClock = clock[i];

                if (rising)
                {
                    if (_awaitingFirstClock)
                    {
                        // Starts the sequence where it stands instead of stepping off it.
                        _awaitingFirstClock = false;
                    }
                    else
                    {
                        // Exactly one step, wherever inside the current one the position had crept
                        // to. This is the whole point: the boundary is where the pulse is, not
                        // where an accumulator happened to arrive.
                        _position = Math.Floor(_position) + 1;
                        _clockInterval = _samplesSinceClock;
                    }

                    _samplesSinceClock = 0;
                }

                // Counted here rather than at the bottom of the loop, which two `continue`s below
                // can skip. Incrementing on the pulse's own sample too is what makes the measured
                // gap come out as the number of samples between two pulses rather than one less.
                _samplesSinceClock++;

                if (_awaitingFirstClock)
                {
                    // Silent, but already holding the pitch of the note that is about to start, so
                    // nothing downstream reads 0 Hz while it waits.
                    var pending = steps[_cursor];
                    if (!pending.Rest)
                        _lastHz = pending.FrequencyHz;

                    hz[i] = (float)(_lastHz * Math.Pow(2, octave[i]));
                    _activeStep = -1;
                    continue;
                }
            }

            if (_position >= total)
            {
                if (loops)
                {
                    _position -= total * Math.Floor(_position / total);
                    _cursor = 0;
                }
                else
                {
                    // Held past the end rather than wrapped: the gate stays shut and the pitch
                    // stays where the last note left it, so whatever is downstream gets to finish
                    // its release instead of being retriggered or yanked to 0 Hz.
                    hz[i] = (float)(_lastHz * Math.Pow(2, octave[i]));
                    _activeStep = -1;
                    continue;
                }
            }

            while (_cursor + 1 < steps.Length && _position >= starts[_cursor + 1])
                _cursor++;

            var step = steps[_cursor];
            var within = _position - starts[_cursor];

            // Včetně pomlky: je to ukazatel pozice v sekvenci, ne toho, jestli zrovna zní tón.
            _activeStep = _cursor;

            if (!step.Rest)
                _lastHz = step.FrequencyHz;

            hz[i] = (float)(_lastHz * Math.Pow(2, octave[i]));
            gate[i] = !step.Rest && within < step.Length * Math.Clamp(articulation[i], 0, 1) ? 1f : 0f;

            var knobRate = 1.0 / (Math.Max(stepLength[i], 0.001) * ctx.SampleRate);

            if (!clocked)
            {
                _position += knobRate;
                continue;
            }

            // Clocked: the position still slides, but only so that Artikulace has somewhere to
            // close the gate. How fast it slides is the whole "Délka kroku podle" switch — the
            // measured gap (so a faster clock shortens the note) or the knob (so every note is the
            // same length however irregular the pulses are). Before a second pulse there is
            // nothing measured, so the knob stands in either way.
            var clockRate = lengthFromClock && _clockInterval > 0
                ? 1.0 / _clockInterval
                : knobRate;

            _position = Math.Min(_position + clockRate, Math.Floor(_position) + StepCeiling);
        }

        if (!VisualizationHub.IsRecording)
            VisualizationHub.Contribute(Id, StepFrame(steps.Length));

        return new Dictionary<Port, float[]> { [_hzOutput] = hz, [_gateOutput] = gate };
    }
}
