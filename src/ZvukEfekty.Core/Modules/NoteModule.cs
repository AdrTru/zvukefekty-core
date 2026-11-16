using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Source of "which note is sounding, and is its key still down" — the bridge between a played
/// (or sampled) pitch and the module graph. Doesn't know about the keyboard itself.
///
/// Outputs "Hz" (the note's frequency, 0 when no note is sounding) — patch straight into an
/// Oscillator's Frequency input (with that Frequency's base Value at 0, ModulationAmount at 1,
/// the same "base 0 + full modulation" convention used elsewhere) to make the oscillator track
/// the played pitch. Wire several oscillators to the same Hz output with ModulationAmount
/// 2, 3, 4... for a harmonic stack (an integer multiple of a frequency is that harmonic).
///
/// Outputs "Gate" (1 while the note is held, 0 once released) — patch into an Envelope's Gate
/// input so its Attack/Release react to the actual key press/release instead of a fixed buffer length.
///
/// Držení reaches the rest of the patch even with no Gate wire, through
/// <c>ModuleGraph.BatchNoteHoldSeconds</c> and <c>RenderContext.NoteHoldSeconds</c>: an ungated
/// envelope holds for it, and a patch with no envelope at all gets it applied at the Output.
/// Without that the knob set the length of nothing at all unless the Gate wire happened to be
/// patched — see <see cref="BatchNoteHoldSeconds"/>.
///
/// Has two modes, chosen by whether a <see cref="Audio.Voice"/> is driving it — the same shape of
/// split <see cref="EnvelopeModule"/> makes on its Gate input:
/// - Live (<see cref="IsDrivenLive"/>): the voice pushes <see cref="CurrentFrequencyHz"/> /
///   <see cref="IsGateOpen"/> in before each of the small blocks it renders, so the gate follows
///   the real key.
/// - Batch (Play/Export): nothing pushes anything in between blocks, because the whole clip is one
///   Process call. The gate therefore has to close WITHIN that one buffer, which is what
///   <see cref="HoldSecondsAt"/> is for — hold it that long, then release and let the tail ring out
///   for the rest of the clip. Without it the note would still be in sustain when the buffer ends
///   and the export would stop mid-tone. Point the module at a pitch with <see cref="SetBatchNote"/>.
///
/// The "Klávesnice" parameter restricts which keys this instance reacts to — "Dolní řada"/"Horní
/// řada" match the two rows <c>KeyboardNoteMap</c> (App project) maps to MIDI notes 48-59 and
/// 60-72 respectively. A patch with two Nota modules (see <see cref="Audio.Voice"/>, which pushes
/// every held note into *every* Nota module in the graph) — one per range, each with its own
/// instrument chain — plays a different instrument per keyboard row instead of layering both on
/// every key.
/// </summary>
public sealed class NoteModule : ModuleBase
{
    /// <summary>First MIDI note of the "upper row" — must match <c>KeyboardNoteMap</c>.</summary>
    public const int UpperRowFirstMidiNote = 60;

    private static readonly string[] KeyRangeNames = { "Celá", "Dolní řada", "Horní řada" };

    private readonly Port _hzOutput;
    private readonly Port _gateOutput;
    private readonly ModuleParameter _octave;
    private readonly ModuleParameter _keyRange;
    private readonly ModuleParameter _hold;

    public NoteModule() : base("Nota")
    {
        _hzOutput = AddOutput("Hz");
        _gateOutput = AddOutput("Gate");

        _octave = AddParameter("Octave", 0, -3, 3);
        _keyRange = AddParameter("Klávesnice", 0, 0, KeyRangeNames.Length - 1, options: KeyRangeNames);

        // Not modulatable: a per-sample signal saying "how long is the key held" is meaningless,
        // and the value is read once for the whole buffer anyway.
        _hold = AddParameter("Držení", 0.5, 0, 10, "s", modulatable: false, tempoSync: TempoSyncKind.Seconds);
    }

    public override string Description =>
        "Zdroj výšky tónu a gate signálu. Výstup Hz napojte na Frequency oscilátoru, " +
        "výstup Gate do vstupu Gate na Envelope. Parametr Klávesnice omezuje, na které klávesy modul reaguje. " +
        "Držení platí jen pro Přehrát/Export — říká, jak dlouho zůstane klávesa pomyslně stisknutá, " +
        "než se pustí a nechá dozvuk doběhnout; při hraní na klávesnici ho určuje skutečný stisk. " +
        "Platí i pro obálku bez zapojeného Gate a i pro sestavu, která obálku vůbec nemá — " +
        "tón se v obou případech ukončí po Držení, ne až na konci klipu. " +
        "Vlastní Držení na obálce má přednost.";

    /// <summary>Set by the owning <see cref="Audio.Voice"/> before each block; 0 while no note is held.</summary>
    public double CurrentFrequencyHz { get; set; }

    /// <summary>Set by the owning <see cref="Audio.Voice"/> before each block, alongside <see cref="CurrentFrequencyHz"/> — needed to test against "Klávesnice".</summary>
    public int CurrentMidiNote { get; set; }

    /// <summary>Set by the owning <see cref="Audio.Voice"/> before each block.</summary>
    public bool IsGateOpen { get; set; }

    /// <summary>Set by the owning <see cref="Audio.Voice"/> before each block, and by nothing else —
    /// its being false is how a batch render is recognised. See the two modes in the class remarks.</summary>
    public bool IsDrivenLive { get; set; }

    /// <summary>How long the gate stays open in a batch render, at <paramref name="bpm"/>. Exposed
    /// so the UI can warn when it runs past the clip length, which would export a tone cut off
    /// mid-sustain. Takes a tempo because Držení can be set as a note value, and then the answer
    /// is only meaningful against one.</summary>
    public double HoldSecondsAt(double bpm) => _hold.ResolveValue(bpm);

    /// <summary>Publishes <see cref="HoldSecondsAt"/> to the render, so an envelope with nothing
    /// wired into its Gate can hold for it too. Without this the knob reached only the envelopes
    /// the user had also run a Gate wire to, and did nothing at all on the rest — which looks
    /// exactly like a broken knob, because from the outside that is what it is.</summary>
    public override double BatchNoteHoldSeconds(double bpm)
        => IsDrivenLive ? 0 : Math.Max(0, HoldSecondsAt(bpm));

    /// <summary>The "Klávesnice" knob. Exposed so the UI can watch it and show which keys this
    /// patch will actually answer to, instead of letting a mismatch render silence.</summary>
    public ModuleParameter KeyRangeParameter => _keyRange;

    /// <summary>Points this module at one pitch for a batch Play/Export. The gate is left closed —
    /// <see cref="Process"/> opens it itself for <see cref="HoldSecondsAt"/>, since a batch render is
    /// a single call with nobody to release the key partway through.</summary>
    public void SetBatchNote(int midiNote)
    {
        CurrentMidiNote = midiNote;
        CurrentFrequencyHz = MidiNotes.ToFrequencyHz(midiNote);
        IsGateOpen = false;
        IsDrivenLive = false;
    }

    /// <summary>Whether this instance's "Klávesnice" range covers <paramref name="midiNote"/>.</summary>
    public bool AcceptsMidiNote(int midiNote) => (int)_keyRange.Value switch
    {
        1 => midiNote < UpperRowFirstMidiNote,
        2 => midiNote >= UpperRowFirstMidiNote,
        _ => true,
    };

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var octave = GetModulatedValues(_octave, ctx, inputBuffers);
        var hz = NewBuffer(ctx);
        var gate = NewBuffer(ctx);

        var inRange = AcceptsMidiNote(CurrentMidiNote);
        var frequencyHz = inRange ? CurrentFrequencyHz : 0.0;
        for (var i = 0; i < ctx.SampleCount; i++)
            hz[i] = (float)(frequencyHz * Math.Pow(2, octave[i]));

        if (IsDrivenLive)
        {
            // One block is short enough that the key cannot change state inside it — the voice
            // already told us which way it is.
            var gateValue = inRange && IsGateOpen ? 1f : 0f;
            for (var i = 0; i < ctx.SampleCount; i++)
                gate[i] = gateValue;
        }
        else
        {
            // Clamped to the clip: a Držení longer than the clip would never let go, and the
            // release the envelope is waiting for would never come.
            var heldSeconds = inRange ? Math.Clamp(_hold.ResolveValue(ctx.Bpm), 0, ctx.DurationSeconds) : 0;
            var heldSamples = Math.Min((int)Math.Round(heldSeconds * ctx.SampleRate), ctx.SampleCount);
            for (var i = 0; i < heldSamples; i++)
                gate[i] = 1f;
        }

        return new Dictionary<Port, float[]> { [_hzOutput] = hz, [_gateOutput] = gate };
    }
}
