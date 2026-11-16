using ZvukEfekty.Core.Graph;

namespace ZvukEfekty.Core.Modules;

/// <summary>
/// Chops whatever arrives at "In" into short overlapping grains and replays them, each with its own
/// read position, its own pitch and its own window.
///
/// <para><b>What it is actually for.</b> Every other reader in this engine has ONE read head:
/// <see cref="SamplerModule"/> tracks a single position, and its Speed moves rate and pitch
/// together, exactly like a tape. That is why the engine cannot stretch a sound without transposing
/// it, and why "hold this sound" has no answer — a frozen head reads one sample over and over,
/// which is DC, not a tone. A granulator has many heads at once, and the speed the heads are
/// LAUNCHED at is independent of the speed each one READS at. Those are the two knobs Pozice and
/// Výška, and their independence is the whole point of the module.</para>
///
/// <para><b>Why it granulates the input and not a file.</b> The recipe this was built for is
/// <c>Noise → Filter → Granulator → Reverb</c> (<c>poznamky_syntezy.md</c>), which has no file in
/// it at all — the material is filtered noise. The other gap it closes is the beat repeat missing
/// from <c>dj_zvuky_zdroje/recepty/13_glitch_stutter.md</c>, which needs the LAST bar of a live
/// signal, not a recording on disk. A file is not lost by this choice: wire a
/// <see cref="SamplerModule"/> into "In" and the file is granulated too. What IS given up is reach
/// — grains only come from as far back as the ring buffer holds (<see cref="MaxBufferSeconds"/>),
/// so scattering grains across a whole thirty-second recording is out.</para>
///
/// <para><b>The output is grains only</b>, with no dry signal mixed back in, for the same reason
/// <see cref="DelayLineModule"/> exists next to <see cref="DelayModule"/>: a fixed dry path cannot
/// be turned off, and a stutter with the un-stuttered signal underneath it is not a stutter. Mix
/// the dry back in with a <see cref="MixerModule"/> when that is what is wanted.</para>
/// </summary>
public sealed class GranulatorModule : ModuleBase
{
    /// <summary>How far back grains can reach. Two seconds is one bar at 128 BPM, which is what
    /// makes a bar-long beat repeat possible at club tempos; below 120 BPM a bar no longer fits and
    /// Pozice clamps. It is a big buffer — 768 kB at 96 kHz, cloned into every voice
    /// (<c>Audio.Voice.ClonePatch</c>), so 16 held voices is 12 MB — but it is the same two seconds
    /// <see cref="DelayModule"/> already allocates per voice, so it sets no new high-water mark for
    /// the engine.</summary>
    private const double MaxBufferSeconds = 2.0;

    private const int MaxBufferSamples = (int)(MaxBufferSeconds * 96000) + 4;

    /// <summary>Ceiling on grains sounding at once. Reached only at the extremes (200 grains/s of
    /// 0,5 s each would want 100), where individual grains have long since stopped being audible as
    /// events. Past it new grains are skipped rather than cutting a sounding one off mid-window,
    /// which would click; the level stays right because <see cref="OverlapGain"/> normalises by the
    /// density that can actually be delivered, not by the one that was asked for.</summary>
    private const int MaxGrains = 64;

    /// <summary>One sounding grain. A struct in a fixed array, not an object in a list, because the
    /// engine already allocates 363 MB over a 60 s render (todo.md 7e) and a granulator spawning
    /// tens of these a second is exactly the wrong place to add to that.</summary>
    private struct Grain
    {
        public bool Active;

        /// <summary>Fractional read index into <see cref="_line"/>.</summary>
        public double ReadPos;

        /// <summary>How far the read index moves per output sample — the grain's own playback
        /// speed, so 1 is the recorded pitch, 2 an octave up, negative backwards.</summary>
        public double Step;

        public int Age;
        public int Life;
    }

    private readonly Port _input;
    private readonly Port _output;
    private readonly ModuleParameter _rate;
    private readonly ModuleParameter _length;
    private readonly ModuleParameter _position;
    private readonly ModuleParameter _spread;
    private readonly ModuleParameter _pitch;
    private readonly ModuleParameter _detune;
    private readonly ModuleParameter _jitter;
    private readonly ModuleParameter _freeze;
    private readonly CurveInput _shape;
    private readonly SeededRandomSource _randomSource;

    private readonly float[] _line = new float[MaxBufferSamples];
    private readonly Grain[] _grains = new Grain[MaxGrains];
    private int _writePos;

    /// <summary>Progress towards the next grain, in mean intervals — the same accounting
    /// <see cref="PoissonTriggerModule"/> uses, so that modulating Četnost shortens the gap already
    /// running rather than only the ones after it.</summary>
    private double _progress;

    private double _interval;

    public GranulatorModule() : base("Granulátor")
    {
        _input = AddInput("In");
        _output = AddOutput("Out");

        _rate = AddParameter("Četnost", 20, 0.5, 200, "1/s", tempoSync: TempoSyncKind.Rate);
        _length = AddParameter("Délka", 0.05, 0.002, 0.5, "s", tempoSync: TempoSyncKind.Seconds);

        // How far back in the recorded past a grain starts. This is the knob that makes the module
        // a granulator rather than a delay: it says WHERE to read, while Výška says how fast to
        // read once there, and nothing ties the two together.
        _position = AddParameter("Pozice", 0.2, 0, MaxBufferSeconds, "s", tempoSync: TempoSyncKind.Seconds);
        _spread = AddParameter("Rozptyl", 0.02, 0, MaxBufferSeconds, "s", tempoSync: TempoSyncKind.Seconds);

        // Not tempo-syncable: this is a pitch, and the same reasoning that keeps an Oscillator's Hz
        // off the note grid keeps this off it. Negative plays each grain backwards, which reverses
        // the sound without reversing the order the grains arrive in — something the Sampler's own
        // negative Speed cannot produce.
        _pitch = AddParameter("Výška", 1, -4, 4);
        _detune = AddParameter("Rozladění", 0, 0, 1200, "c");

        _jitter = AddParameter("Nepravidelnost", 0, 0, 1);

        // Stops the recording head without stopping the grains. Needed because Pozice is an offset
        // from that head, so while the head runs, a fixed Pozice is a fixed DELAY and the material
        // slides past underneath it — useful as a smear, but it is not a freeze and it is not a
        // beat repeat. With the head parked, Pozice addresses a fixed point in the captured audio
        // instead, so the same slice comes back grain after grain.
        //
        // A plain 0-1 knob rather than a two-item enum because an enum cannot be modulated
        // (see AddParameter), and throwing this from an Envelope, an LFO or the irregular trigger
        // is most of what it is for. Anything from 0.5 up is frozen.
        _freeze = AddParameter("Zmrazit", 0, 0, 1);

        // The window each grain is multiplied by. The default is a Hann window, which is what keeps
        // a grain from clicking at both ends; drawing something else here is how a grain becomes
        // percussive (sharp attack, long tail) instead of symmetric.
        _shape = AddCurveInput("Obálka zrna", HannWindow(), CurveOutput.Unipolar);

        _randomSource = AddSeed();

        ResetState();
    }

    public override string Description =>
        "Rozřeže signál na vstupu na krátká zrna a přehraje je překrytá přes sebe. Pozice říká, " +
        "jak daleko do minulosti zrno sahá (až 2 s), Výška jak rychle se zrno čte — a na sobě " +
        "nezávisí, takže jde zvuk natáhnout beze změny výšky nebo zmrazit na místě. " +
        "Malá Délka a velký Rozptyl dělají mrak/texturu. Zmrazit (od 0,5) zastaví nahrávání, " +
        "takže se pořád dokola vrací týž úsek — to je zmrazení i beat repeat; bez něj Pozice " +
        "znamená jen zpoždění a materiál pod zrny protéká. Rozladění rozhází výšku po zrnech " +
        "(mrak, chorus). Na výstupu jsou jen " +
        "zrna, suchý signál ne — přimíchej ho Mixerem. Soubor granuluješ tak, že do In zapojíš " +
        "Sampler.";

    public override void ResetState()
    {
        Array.Clear(_line);
        Array.Clear(_grains);
        _writePos = 0;

        _randomSource.Reset();
        _interval = IrregularTiming.DrawInterval(_randomSource.GetGenerator(out _), _jitter.Value);

        // Unlike PoissonTriggerModule, do NOT start at the end of the first gap. That module fires
        // on sample zero so that a slow rate does not render an empty buffer; here sample zero has
        // an empty ring behind it, so a grain launched there would be a window over silence.
        // Waiting one gap means the first grain has something to read.
        _progress = 0;
    }

    public override IReadOnlyDictionary<Port, float[]> Process(RenderContext ctx, IReadOnlyDictionary<Port, float[]> inputBuffers)
    {
        var input = GetInputOrSilence(inputBuffers, _input, ctx);
        var rate = GetModulatedValues(_rate, ctx, inputBuffers);
        var length = GetModulatedValues(_length, ctx, inputBuffers);
        var position = GetModulatedValues(_position, ctx, inputBuffers);
        var spread = GetModulatedValues(_spread, ctx, inputBuffers);
        var pitch = GetModulatedValues(_pitch, ctx, inputBuffers);
        var detune = GetModulatedValues(_detune, ctx, inputBuffers);
        var jitter = GetModulatedValues(_jitter, ctx, inputBuffers);
        var freeze = GetModulatedValues(_freeze, ctx, inputBuffers);

        // Same reasoning as the trigger's: a seed moved in the inspector invalidates the gap
        // currently counting down, because it came out of the sequence that was just replaced.
        var random = _randomSource.GetGenerator(out var wasReseeded);
        if (wasReseeded)
            _interval = IrregularTiming.DrawInterval(random, _jitter.Value);

        var shape = _shape.Curve;
        var output = NewBuffer(ctx);
        var sampleDuration = 1.0 / ctx.SampleRate;

        // Never let a read reach the write head itself: the newest sample is being written this
        // instant, and one behind it is the newest that is safely readable.
        var maxOffset = Math.Min(MaxBufferSamples - 2, MaxBufferSeconds * ctx.SampleRate);

        for (var i = 0; i < ctx.SampleCount; i++)
        {
            var frozen = freeze[i] >= 0.5f;
            if (!frozen)
                _line[_writePos] = input[i];

            if (_progress >= _interval)
            {
                _progress -= _interval;
                _interval = IrregularTiming.DrawInterval(random, jitter[i]);
                Spawn(random, ctx.SampleRate, maxOffset, frozen,
                    length[i], position[i], spread[i], pitch[i], detune[i]);
            }

            var sum = 0.0;

            for (var g = 0; g < MaxGrains; g++)
            {
                ref var grain = ref _grains[g];
                if (!grain.Active)
                    continue;

                sum += ReadInterpolated(grain.ReadPos) * shape.Sample((double)grain.Age / grain.Life);

                grain.ReadPos += grain.Step;
                if (grain.ReadPos >= MaxBufferSamples)
                    grain.ReadPos -= MaxBufferSamples;
                else if (grain.ReadPos < 0)
                    grain.ReadPos += MaxBufferSamples;

                if (++grain.Age >= grain.Life)
                    grain.Active = false;
            }

            output[i] = (float)(sum / OverlapGain(rate[i], length[i]));

            if (!frozen)
                _writePos = (_writePos + 1) % MaxBufferSamples;

            _progress += Math.Max(0, rate[i]) * sampleDuration;
        }

        return new Dictionary<Port, float[]> { [_output] = output };
    }

    /// <summary>
    /// What to divide the summed grains by, so that density does not double as a volume knob.
    ///
    /// <para>Grains taken from scattered positions are mutually incoherent, so N of them sum to
    /// sqrt(N) times the level of one — turning Četnost up would otherwise make a patch louder as
    /// well as thicker, and this engine has a documented history of patches clipping (todo.md 7d).
    /// Dividing by that same sqrt keeps a cloud at roughly the level of its material however dense
    /// it is. Coherent grains (Rozptyl 0, Výška 1) still add up faster than that and DO get louder
    /// with density; that is honest — they really are the same signal laid on itself.</para>
    ///
    /// <para>Never below 1: under one grain at a time there is nothing to cancel, and dividing by
    /// sqrt(0.1) would amplify a sparse stutter by three.</para>
    /// </summary>
    private static double OverlapGain(double rate, double length)
        => Math.Sqrt(Math.Clamp(rate * length, 1, MaxGrains));

    private void Spawn(Random random, int sampleRate, double maxOffset, bool frozen,
        double length, double position, double spread, double pitch, double detune)
    {
        var slot = -1;
        for (var g = 0; g < MaxGrains; g++)
        {
            if (!_grains[g].Active)
            {
                slot = g;
                break;
            }
        }

        if (slot < 0)
            return;

        var cents = detune <= 0 ? 0 : Math.Clamp(detune, 0, 1200) * (random.NextDouble() * 2 - 1);
        var step = pitch * Math.Pow(2, cents / 1200.0);

        // Two samples is the floor: a grain shorter than that has no window left to speak of, and
        // Age/Life would be dividing by something meaningless.
        var life = Math.Max(2, (int)Math.Round(Math.Max(0, length) * sampleRate));

        // While a grain sounds, the write head closes on it at (headStep - step) samples per
        // sample: a grain slower than the head sinks deeper into the past, a faster one catches the
        // head up. Over its whole life it therefore travels this far relative to the head, and both
        // ends of that journey have to stay inside the ring. A frozen head does not move at all,
        // so every grain drifts towards it at its own full speed.
        var headStep = frozen ? 0 : 1;
        var travel = (headStep - step) * life;

        // A grain long enough that its own travel exceeds the buffer cannot be placed anywhere at
        // all. Shortening it is the only option that stays inside the ring, and it is far less
        // audible than the alternative of reading across the write head, which would splice the
        // buffer's seam into the middle of every grain.
        if (Math.Abs(travel) > maxOffset - 1)
        {
            life = (int)((maxOffset - 1) / Math.Max(1e-9, Math.Abs(headStep - step)));
            if (life < 2)
                return;

            travel = (headStep - step) * life;
        }

        var offset = (position + spread * (random.NextDouble() * 2 - 1)) * sampleRate;
        offset = Math.Clamp(offset, Math.Max(1, 1 - travel), Math.Min(maxOffset, maxOffset - travel));

        var start = (_writePos - offset) % MaxBufferSamples;
        if (start < 0)
            start += MaxBufferSamples;
        if (start >= MaxBufferSamples)
            start -= MaxBufferSamples;

        _grains[slot] = new Grain { Active = true, ReadPos = start, Step = step, Age = 0, Life = life };
    }

    /// <summary>Reads the ring at a fractional index, interpolating between neighbours — for the
    /// same reason <see cref="SamplerModule"/> does: rounding to a whole sample adds a broadband
    /// rasp that gets worse the further Výška moves from 1.</summary>
    private float ReadInterpolated(double position)
    {
        var index = (int)position;
        var fraction = position - index;
        var next = index + 1 == MaxBufferSamples ? 0 : index + 1;

        return (float)(_line[index] * (1 - fraction) + _line[next] * fraction);
    }

    /// <summary>
    /// A Hann window as breakpoints: zero at both ends, one in the middle, and — the part that
    /// matters — FLAT at both ends, so a grain fades in and out rather than starting with a step.
    ///
    /// <para>Five points, one per quarter of the raised cosine, each handle laid along the true
    /// tangent (the slope of <c>0.5 - 0.5·cos(2πx)</c> is <c>π·sin(2πx)</c>, so it is π at the
    /// quarter points and zero at the ends and the peak). The handle length is the 0,3519 that
    /// <see cref="Curve.Sine"/>'s sweep found for a quarter of a sinusoid, which is exactly what
    /// each of these segments is.</para>
    /// </summary>
    private static Curve HannWindow()
    {
        const double handle = 0.3519;
        const double quarter = 0.25;
        var reach = handle * quarter;

        var rising = new CurveHandle(reach, reach * Math.PI);
        var falling = new CurveHandle(reach, -reach * Math.PI);
        var flat = new CurveHandle(reach, 0);

        return new Curve(new[]
        {
            new CurvePoint(0, 0, Out: flat),
            new CurvePoint(0.25, 0.5, Out: rising, In: new CurveHandle(-rising.Dx, -rising.Dy)),
            new CurvePoint(0.5, 1, Out: flat, In: new CurveHandle(-flat.Dx, -flat.Dy)),
            new CurvePoint(0.75, 0.5, Out: falling, In: new CurveHandle(-falling.Dx, -falling.Dy)),
            new CurvePoint(1, 0, In: new CurveHandle(-flat.Dx, -flat.Dy)),
        });
    }
}
