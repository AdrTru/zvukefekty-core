using ZvukEfekty.Core.Audio;
using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Plays a sound file as an oscillator would play a waveform: a recording becomes a source you can
/// pitch, gate and patch into the rest of the chain.
///
/// Two modes, chosen the same way <see cref="EnvelopeModule"/> chooses its two — by whether "Gate"
/// is wired:
/// <list type="bullet">
/// <item>Unconnected: the sample starts at the top of the render and plays through, which is what a
/// one-shot effect patch wants.</item>
/// <item>Connected: every rising edge restarts it, so a live voice retriggers on each key press.</item>
/// </list>
///
/// The file is decoded once by <see cref="AudioSampleCache"/> and shared by reference, because a
/// live voice clones the whole patch per note — without that, a chord would decode the same file
/// once per key. Decoding happens in <see cref="Prepare"/>, off the audio thread.
///
/// Mono, like the rest of the engine: a stereo file is averaged down at decode time. That is the
/// one place to revisit when the engine grows sides (todo.md section 5).
///
/// <para><b>Nemusí to být soubor na disku.</b> Cizí program může vzorek vložit do paměti
/// (<see cref="AudioSampleCache.VlozVzorek(string, DecodedSample)"/>) a do „Soubor" zapsat
/// <c>pamet:jméno</c> — hra si tak zvuk rozbalí po svém a nemusí ho kvůli Sampleru ukládat na disk.
/// Odkazuje se jménem, ne objektem, protože živý hlas klonuje sestavu přes serializér; proč to tak
/// je, stojí u <see cref="AudioSampleCache"/>.</para>
/// </summary>
public sealed class SamplerModule : ModuleBase
{
    private static readonly string[] LoopNames = { "Ne", "Ano" };

    /// <summary>Every MIDI note by name, so "Základní nota" is picked as C4 rather than as 60. The
    /// stored value is still the note number, which is what the arithmetic wants.</summary>
    private static readonly string[] NoteNames =
        Enumerable.Range(0, 128).Select(MidiNotes.Name).ToArray();

    private readonly Port _gate;
    private readonly Port _pitch;
    private readonly Port _output;
    private readonly ModuleParameter _speed;
    private readonly ModuleParameter _start;
    private readonly ModuleParameter _amplitude;
    private readonly ModuleParameter _loop;
    private readonly ModuleParameter _baseNote;
    private readonly CurveInput _readPath;
    private readonly ModuleFile _file;

    private DecodedSample? _sample;
    private string _loadedPath = "";

    // Fractional, because the file's rate almost never matches the render rate and the speed knob
    // moves it further. Carried across blocks so a live voice plays continuously.
    /// <summary>Read position in samples, used directly when no read path is patched in.</summary>
    private double _position;

    /// <summary>The same motion as <see cref="_position"/> but as a fraction of the file, which is
    /// what a drawn read path is a function of. Kept alongside rather than derived, so the plain
    /// path stays bit-for-bit what it always was.</summary>
    private double _progress;
    private bool _isPlaying;
    private bool _gateWasHigh;

    public SamplerModule() : base("Sampler")
    {
        _gate = AddInput("Gate");
        _pitch = AddInput("Hz");
        _output = AddOutput("Out");

        // Negative reads backwards and zero stops the playhead dead. Both were shut out by the old
        // 0.25 floor, and both are the whole mechanism behind a family of DJ effects: a vinyl brake
        // is Speed driven to 0, a backspin is Speed driven below it, and a scratch is Speed swinging
        // either side of zero. See dj_zvuky_zdroje/recepty/08 and 09.
        //
        // "Průběh čtení" below could already send the head backwards or hold it still, so this is
        // not the first way to do either. It is the first MODULATABLE one: a drawn path is a fixed
        // shape over the whole render, while Speed is read per sample and so can be driven by an
        // LFO or an envelope.
        _speed = AddParameter("Speed", 1, -4, 4);
        _start = AddParameter("Start", 0, 0, 1);
        _amplitude = AddParameter("Amplitude", 1, 0, 1);
        _loop = AddParameter("Loop", 0, 0, LoopNames.Length - 1, options: LoopNames);

        // What pitch the recording itself is at. Only means anything once something is wired into
        // "Hz"; on its own it changes nothing, which is why it sits after Loop rather than among
        // the knobs that always do something.
        _baseNote = AddParameter("Základní nota", MidiNotes.MiddleC, 0, NoteNames.Length - 1, options: NoteNames);

        // Where in the file to read, as a function of how far through playback we are. The default
        // is the diagonal, which IS ordinary linear playback — so an unwired sampler behaves
        // exactly as it did before this existed, down to the sample.
        //
        // Unipolar and untied: Y is a position in the file (0 = start, 1 = end) and the two ends
        // have no reason to meet — a shape that starts and finishes in different places is the
        // normal case, not a discontinuity to hide.
        _readPath = AddCurveInput("Průběh čtení", Curve.Line(0, 1), CurveOutput.Unipolar);

        _file = AddFile("Soubor", "Zvukové soubory|*.wav;*.mp3;*.aiff;*.flac|Všechny soubory|*.*");
    }

    public override string Description =>
        "Přehraje zvukový soubor jako zdroj zvuku. Speed mění rychlost i výšku zároveň (jako " +
        "u pásku), Start posune začátek přehrávání. Bez zapojeného Gate hraje od začátku " +
        "vykreslení, se zapojeným se spustí znovu při každém stisku klávesy. Stereo soubor se " +
        "smíchá do mona. Vstup Hz z modulu Nota rozehraje vzorek hudebně: řekni v Základní notě, " +
        "na jaké výšce nahrávka je, a modul dopočítá rychlost sám. Speed pak zůstává jako " +
        "doladění, 1 = přesná výška. Záporná rychlost čte soubor pozpátku, 0 zastaví " +
        "čtení na místě (pozor, to je konstantní hodnota = stejnosměrná složka — utlum ji obálkou). " +
        "Pozpátku se hraje od místa daného Startem, takže pro přehrání celého souboru odzadu dej " +
        "Start na 1; ve smyčce se přetečení přes začátek obtočí na konec souboru samo. " +
        "Místo cesty k souboru může stát pamet:jméno — to je zvuk, který do paměti vložil program, " +
        "co sestavu pouští; v aplikaci samotné takový vzorek nevzniká.";

    /// <summary>Decodes the file, or picks it up from the cache when something already has. Called
    /// off the audio thread — see <see cref="ModuleBase.Prepare"/>.</summary>
    public override void Prepare()
    {
        var cesta = _file.Path;
        var zPameti = AudioSampleCache.JeVPameti(cesta);

        // Paměťový vzorek se vyzvedává pokaždé, i když se cesta nezměnila: pod stejným jménem může
        // program mezitím vložit jiný zvuk (to je celý smysl toho, že se na něj ukazuje jménem)
        // a zapamatovaný odkaz by hrál ten starý. Je to hledání ve slovníku, ne čtení z disku.
        if (_sample is not null && _loadedPath == cesta && !zPameti)
            return;

        _loadedPath = cesta;

        if (string.IsNullOrWhiteSpace(cesta))
        {
            _sample = null;
            _file.Status = "Není vybraný soubor – modul hraje ticho.";
            return;
        }

        _sample = AudioSampleCache.Get(cesta, out var error);

        if (error is not null)
        {
            // Vzorek, který se nepodařilo sehnat, se nesmí nechat viset z minulé přípravy — zvuk by
            // hrál dál a hláška by tvrdila opak.
            _sample = null;
            _file.Status = error;
            return;
        }

        _file.Status = (zPameti ? "z paměti: " : "") + $"{_sample!.DurationSeconds:0.00} s" +
            (_sample.Channels > 1 ? $", {_sample.Channels} kanály smíchané do mona" : ", mono") +
            $", {_sample.SampleRate} Hz";
    }

    public override void ResetState()
    {
        _position = 0;
        _progress = 0;
        _isPlaying = false;
        _gateWasHigh = false;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var output = NewBuffer(ctx);

        // Never decodes here. If Prepare has not run, or the file could not be read, the module is
        // silent rather than stalling the audio thread on a file read.
        var sample = _sample;
        if (sample is null || sample.Samples.Length == 0)
            return new Dictionary<Port, float[]> { [_output] = output };

        var speed = GetModulatedValues(_speed, ctx, inputBuffers);

        // With "Hz" wired the sampler plays musically: speed becomes the ratio of the note being
        // asked for to the note the recording is at. Without it, nothing changes — Speed is the
        // plain tape-speed knob it has always been.
        //
        // This is what made connecting NoteModule.Hz straight to Speed useless: Speed reads -4 to 4,
        // and a frequency in Hz lands nowhere near that scale. The base note is the missing half of
        // the sum.
        var hasPitch = inputBuffers.TryGetValue(_pitch, out var pitch);
        var baseFrequency = MidiNotes.ToFrequencyHz((int)Math.Clamp(_baseNote.Value, 0, NoteNames.Length - 1));
        var start = GetModulatedValues(_start, ctx, inputBuffers);
        var amplitude = GetModulatedValues(_amplitude, ctx, inputBuffers);
        var loops = (int)_loop.Value == 1;

        var isGated = inputBuffers.TryGetValue(_gate, out var gate);
        if (!isGated && !_isPlaying)
        {
            // Free-running: begin at the top of the render, matching how an unwired Envelope
            // shapes the whole buffer rather than waiting for an edge that never comes.
            _isPlaying = true;
            _position = StartPosition(sample, start[0]);
            _progress = Math.Clamp(start[0], 0, 1);
        }

        // The file's own rate against the render's: a 22 kHz sample played on a 44.1 kHz engine has
        // to advance half a sample per output sample, or it plays an octave low and twice as long.
        var rateRatio = sample.SampleRate / (double)ctx.SampleRate;

        // Null when nothing is patched in, and then the playhead is stepped exactly as it always
        // was. That is not an optimisation, it is correctness: Curve.Sample reads a 4096-entry
        // FLOAT table, so even a perfect diagonal quantises the read position by about 0.01 of a
        // sample. Measured, that put the output 38-50 dB below the signal away from the original
        // on five playback cases — inaudible in isolation, but a sampler exists to reproduce a
        // file, and paying that on every patch that never draws a curve would be a poor trade.
        //
        // Resolved once per block, never per sample: an edit swaps the curve object atomically, so
        // taking it here also keeps one block reading one shape.
        var readPath = _readPath.IsConnected ? _readPath.Curve : null;
        var lastIndex = Math.Max(1, sample.Samples.Length - 1);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            if (isGated)
            {
                var isHigh = gate![i] > 0.5f;
                if (isHigh && !_gateWasHigh)
                {
                    _isPlaying = true;
                    _position = StartPosition(sample, start[i]);
                    _progress = Math.Clamp(start[i], 0, 1);
                }

                _gateWasHigh = isHigh;
            }

            if (!_isPlaying)
                continue;

            // Strictly past the end, not at it: landing exactly on the last position still has one
            // sample left to play, and stopping there clipped it off — measured as a ramp that
            // dropped from 0.99998 straight to zero on its very last sample.
            //
            // With a read path drawn in, the end is tested on PROGRESS instead: the curve can
            // legitimately send the playhead backwards or hold it still, so what finishes playback
            // is the time being up, not the head reaching the end of the file.
            // Both ends, now that the playhead can travel either way: running off the front is just
            // as much "finished" as running off the back, and testing only the back left a reversed
            // sampler reading at a negative index.
            var pastEnd = readPath is null ? _position > lastIndex : _progress > 1;
            var beforeStart = readPath is null ? _position < 0 : _progress < 0;
            if (pastEnd || beforeStart)
            {
                if (!loops)
                {
                    _isPlaying = false;
                    continue;
                }

                // Wrap to the far end from the one we left by. Forward playback keeps returning to
                // Start exactly as it always did; backward playback returns to the end of the file,
                // which is what makes Speed -1 with Loop on play the file backwards over and over
                // instead of stalling against its first sample.
                if (pastEnd)
                {
                    _position = StartPosition(sample, start[i]);
                    _progress = Math.Clamp(start[i], 0, 1);
                }
                else
                {
                    _position = lastIndex;
                    _progress = 1;
                }
            }

            var position = readPath is null ? _position : readPath.Sample(_progress) * lastIndex;
            output[i] = (float)(amplitude[i] * ReadInterpolated(sample.Samples, position));

            // A Hz of zero means nothing is being held; leaving the knob speed alone then keeps a
            // released note ringing out at pitch instead of freezing the playhead mid-tail.
            var step = speed[i];
            if (hasPitch && pitch![i] > 0)
                step *= (float)(pitch[i] / baseFrequency);

            // Both playheads advance; which one is read depends on whether a curve is patched in.
            // Progress is the same motion expressed as a fraction of the file, so a drawn diagonal
            // traverses in exactly the time plain playback would.
            var advance = rateRatio * step;
            _position += advance;
            _progress += advance / lastIndex;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    /// <summary>Start offset as a fraction of the file, so the knob means the same thing whatever
    /// the file's length — a fixed number of seconds would run off the end of a short one.</summary>
    private static double StartPosition(DecodedSample sample, double start)
        => Math.Clamp(start, 0, 1) * (sample.Samples.Length - 1);

    /// <summary>
    /// Reads at a fractional position, interpolating between neighbours. Rounding to the nearest
    /// whole sample instead would add a broadband rasp that gets worse the further Speed moves from
    /// 1 — audible on anything tonal.
    /// </summary>
    private static double ReadInterpolated(float[] samples, double position)
    {
        // Before the first sample as well as past the last, because the playhead runs backwards now.
        // Without this the cast truncates towards zero and a position of -0.5 would extrapolate off
        // the front of the file rather than reading its first sample.
        if (position <= 0)
            return samples[0];

        var index = (int)position;
        if (index >= samples.Length - 1)
            return samples[^1];

        var fraction = position - index;
        return samples[index] + (samples[index + 1] - samples[index]) * fraction;
    }
}
