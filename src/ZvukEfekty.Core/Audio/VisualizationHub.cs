using System.Collections.Concurrent;

namespace ZvukEfekty.Core.Audio;

/// <summary>
/// One published window from a module's live display. Usually raw samples from a Scope (roughly
/// -1..1) — always samples, never a spectrum: which mode a Scope node displays is a per-node UI
/// choice (see ScopeModule.Mode), not something baked into what gets summed here; see
/// <see cref="VisualizationHub"/>'s remarks for why mixing that into the shared slot caused the
/// display to flip between voices.
///
/// <para>A module whose display is not a signal publishes here too, rather than growing a second
/// hub that would need its own copy of the pass/recording wiring in VoiceManager and MainViewModel:
/// <see cref="Modules.WaveMorphModule"/> publishes a single value, how far its crossfade has got.
/// Such a slot is contributed to with <c>average: true</c>, because a control value is averaged
/// across the sounding voices, not summed the way their signals are.</para>
/// </summary>
public sealed record VisualizationSnapshot(float[] Values);

/// <summary>
/// A published level reading from a VU meter module — smoothed peak and RMS, both roughly 0..1.
/// </summary>
public readonly record struct LevelSnapshot(float Peak, float Rms);

/// <summary>
/// Bridges a Scope module's <see cref="Process"/> (which may run on the real-time audio callback
/// thread, and on a per-voice clone rather than the UI's own module instance — see Audio.Voice, one
/// clone per currently-held note) to the node UI polling for something to draw. Keyed by
/// <see cref="Graph.ModuleBase.Id"/>, which survives the clone (see ModuleBase.RestoreId), so the
/// UI's instance and the voices actually rendering audio agree on a slot even though they're
/// different objects.
///
/// Two very different situations feed this hub:
///
/// - Real-time (keyboard) play: <see cref="Process"/> runs at the actual pace the audio is heard —
///   one VoiceManager.Read call per audio callback, all voices rendering their share of it before
///   the mixed result reaches the speakers. <see cref="BeginPass"/>/<see cref="EndPass"/> bracket
///   one such call: every voice's window is summed the same way VoiceManager itself mixes voices
///   down to one signal (simply overwriting the slot on every publish, the original approach, only
///   ever showed whichever voice happened to run last — a held note's release tail flickering over
///   a freshly struck note, or the reverse, depending on unpredictable dictionary iteration order).
///
/// - Batch play/export: the whole clip's <see cref="Process"/> calls run instantly, well before
///   NAudio actually streams the rendered buffer out over the next several seconds — so by the time
///   the listener hears anything, every window has already come and gone, and a live pass-summed
///   display would just freeze on whatever it last saw (a release tail never actually shows, since
///   it was computed and overwritten in the same instant as everything before it). MainViewModel
///   instead brackets a batch render with <see cref="BeginRecording"/>/<see cref="EndRecording"/>,
///   which records a timestamped timeline per slot rather than summing, and drives
///   <see cref="AdvancePlayback"/> off a timer during actual playback so the display advances
///   through the recording in step with what's actually audible.
/// </summary>
public static class VisualizationHub
{
    private sealed class Slot
    {
        public float[] Sum = Array.Empty<float>();
        public int Contributions;
        // Whether this slot's voices are averaged rather than summed — see VisualizationSnapshot.
        // Held on the slot rather than passed to EndPass because only the publisher knows which
        // its payload is, and it is the same answer every time for a given module.
        public bool Average;
        public VisualizationSnapshot? Published;
        public List<(double TimeSeconds, float[] Window)>? Timeline;
    }

    private static readonly ConcurrentDictionary<Guid, Slot> Slots = new();

    /// <summary>True while a batch render is being recorded as a timeline (see
    /// <see cref="BeginRecording"/>) rather than live-summed pass by pass.</summary>
    public static bool IsRecording { get; private set; }

    /// <summary>
    /// Counts the plays that have been started. Displays that ACCUMULATE what they are shown — the
    /// spectrogram's scrolling history, as opposed to the Scope, which only ever draws the newest
    /// window — watch this for a change and throw their picture away when it moves. Without it a
    /// second Play appends its columns to the first one's, and the tail of the previous sound sits
    /// to the left of the new one looking exactly like part of it.
    /// </summary>
    public static int Generation { get; private set; }

    /// <summary>Announces that a new play is starting, whatever kind — a batch render (called for
    /// you by <see cref="BeginRecording"/>) or switching the keyboard instrument on, which has no
    /// bracketing call of its own because its passes just start arriving.
    ///
    /// <para>Every slot is dropped here, which is the one moment that is safe and the one moment it
    /// matters: nothing is sounding yet, so no slot holds anything a display still needs, and from
    /// here on <see cref="BeginPass"/>/<see cref="EndPass"/> walk only what this play actually
    /// publishes. A slot is re-created by its first <see cref="Contribute"/>, so a monitor in the
    /// patch loses nothing by it.</para></summary>
    public static void BeginSession()
    {
        Generation++;
        Slots.Clear();
    }

    /// <summary>Drops one module's slot, for a module that has just been taken out of the patch.
    /// <see cref="BeginSession"/> would get to it at the next play anyway; this is what keeps a
    /// node deleted mid-session from being walked by every pass until then.</summary>
    public static void RemoveSlot(Guid moduleId) => Slots.TryRemove(moduleId, out _);

    /// <summary>Call once before rendering every voice for a real-time pass, so this pass's
    /// contributions start from a clean sum instead of piling onto the previous pass.</summary>
    public static void BeginPass()
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                Array.Clear(slot.Sum);
                slot.Contributions = 0;
            }
        }
    }

    /// <summary>Adds one voice's completed window either to this real-time pass's running sum, or
    /// (while <see cref="IsRecording"/>) to the timeline at <paramref name="elapsedSeconds"/> into
    /// the render.</summary>
    /// <param name="average">Divide the pass by how many voices contributed to it instead of
    /// leaving them summed. For a slot carrying a control value rather than a signal — sixteen
    /// voices at the same crossfade position are still at that position, not at sixteen times
    /// it.</param>
    public static void Contribute(Guid moduleId, float[] window, double elapsedSeconds = 0, bool average = false)
    {
        var slot = Slots.GetOrAdd(moduleId, _ => new Slot());
        lock (slot)
        {
            if (IsRecording)
            {
                (slot.Timeline ??= new List<(double, float[])>()).Add((elapsedSeconds, (float[])window.Clone()));
                return;
            }

            if (slot.Sum.Length != window.Length)
                slot.Sum = new float[window.Length];

            for (var i = 0; i < window.Length; i++)
                slot.Sum[i] += window[i];

            slot.Contributions++;
            slot.Average = average;
        }
    }

    /// <summary>
    /// Call once after every voice has rendered for a real-time pass: publishes the pass's combined
    /// window as what <see cref="TryGet"/> returns.
    ///
    /// <para>A slot nothing contributed to is emptied rather than left alone. Nothing contributed
    /// means nothing is sounding, and a Scope going on showing the last note's waveform is a
    /// reading of a signal that stopped — the display would sit there claiming the patch was still
    /// making that sound. Empty is what a display of a silent signal should say.</para>
    ///
    /// <para>Emptied, not zeroed: <see cref="TryGet"/> then returns null, which the Scope draws as
    /// a bare grid and the spectrogram ignores — so folding the release tail into silence does not
    /// cost the spectrogram a column of history it is meant to keep.</para>
    /// </summary>
    public static void EndPass()
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                if (slot.Contributions == 0)
                {
                    slot.Published = null;
                    continue;
                }

                var window = (float[])slot.Sum.Clone();
                if (slot.Average && slot.Contributions > 1)
                    for (var i = 0; i < window.Length; i++)
                        window[i] /= slot.Contributions;

                slot.Published = new VisualizationSnapshot(window);
            }
        }
    }

    public static VisualizationSnapshot? TryGet(Guid moduleId) =>
        Slots.TryGetValue(moduleId, out var slot) ? slot.Published : null;

    /// <summary>
    /// Empties every live readout — what a batch play's end has to do, since its display is driven
    /// by <see cref="AdvancePlayback"/> off a timer that simply stops, leaving the last frame of the
    /// clip on screen as if it were still playing.
    ///
    /// <para>Recorded timelines are left where they are: this says "nothing is sounding now", not
    /// "forget what was rendered". Accumulating displays are left alone too — the slot going empty
    /// is a value they never draw.</para>
    /// </summary>
    public static void ClearDisplays()
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                slot.Published = null;
                Array.Clear(slot.Sum);
                slot.Contributions = 0;
            }
        }
    }

    /// <summary>Runs the FFT on a raw window for display — kept here (rather than making
    /// SpectrumAnalyzer itself public) so callers go through one obvious entry point.</summary>
    public static float[] ComputeSpectrum(float[] window) => SpectrumAnalyzer.ComputeMagnitudes(window);

    /// <summary>Starts recording every slot's published windows into a timestamped timeline
    /// instead of live-summing them — call before a batch Play/Export render. Follow with
    /// <see cref="EndRecording"/> once the render finishes, then drive <see cref="AdvancePlayback"/>
    /// off a timer while the recorded buffer is actually playing.</summary>
    public static void BeginRecording()
    {
        // BeginSession drops the slots, so there are none left to hand a fresh timeline to: the
        // first Contribute of this render both re-creates the slot and starts its timeline.
        BeginSession();
        IsRecording = true;
    }

    public static void EndRecording() => IsRecording = false;

    /// <summary>Publishes, for every slot with a recorded timeline, whichever window covers
    /// <paramref name="elapsedSeconds"/> into playback. Call periodically while the recorded
    /// render is actually playing, so the display advances through the recording (including any
    /// decay/release tail) instead of freezing on its first entry.</summary>
    public static void AdvancePlayback(double elapsedSeconds)
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                if (slot.Timeline is not { Count: > 0 } timeline)
                    continue;

                var index = timeline.FindLastIndex(w => w.TimeSeconds <= elapsedSeconds);
                if (index < 0) index = 0;
                slot.Published = new VisualizationSnapshot(timeline[index].Window);
            }
        }
    }
}

/// <summary>Same bridge, same real-time pass summing, and the same batch-render timeline recording
/// as <see cref="VisualizationHub"/> (see its remarks) — for VU meter level readings. The
/// fast-attack/slow-release smoothing ("VU ballistics") lives here rather than on the module
/// instance, since a voice clone is destroyed when its note ends but the meter itself (one per
/// node Id) should keep smoothing continuously across notes — and, for a recording, across the
/// whole timeline.</summary>
public static class LevelMeterHub
{
    private sealed class Slot
    {
        public float[] Sum = Array.Empty<float>();
        public bool HasContribution;
        public float SmoothedPeak;

        // Nullable, unlike the value this used to hold: a meter with nothing to show has to be able
        // to say so, rather than reporting a silence indistinguishable from a genuine 0 reading.
        public LevelSnapshot? Published;
        public List<(double TimeSeconds, LevelSnapshot Level)>? Timeline;
    }

    private static readonly ConcurrentDictionary<Guid, Slot> Slots = new();

    /// <summary>How much of the peak survives one pass with nothing louder in it. At the ~10 ms
    /// blocks real-time play renders in, that is roughly 20 dB of fall-off in 140 ms.</summary>
    private const float ReleaseFactor = 0.85f;

    /// <summary>Below this the needle is at rest and the slot is emptied — about -66 dBFS, well
    /// under the bottom of the drawn scale.</summary>
    private const float RestThreshold = 0.0005f;

    public static bool IsRecording { get; private set; }

    /// <summary>Drops every meter's slot at the start of a play — see
    /// <see cref="VisualizationHub.BeginSession"/>, which this mirrors and which the same callers
    /// call alongside it. There is no generation counter here: no VU display accumulates.</summary>
    public static void BeginSession() => Slots.Clear();

    /// <summary>Drops one module's slot — see <see cref="VisualizationHub.RemoveSlot"/>.</summary>
    public static void RemoveSlot(Guid moduleId) => Slots.TryRemove(moduleId, out _);

    public static void BeginPass()
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                Array.Clear(slot.Sum);
                slot.HasContribution = false;
            }
        }
    }

    public static void Contribute(Guid moduleId, float[] block, double elapsedSeconds = 0)
    {
        var slot = Slots.GetOrAdd(moduleId, _ => new Slot());
        lock (slot)
        {
            if (IsRecording)
            {
                var (peak, rms) = ComputeLevels(block);
                slot.SmoothedPeak = peak > slot.SmoothedPeak ? peak : slot.SmoothedPeak * ReleaseFactor;
                (slot.Timeline ??= new List<(double, LevelSnapshot)>()).Add((elapsedSeconds, new LevelSnapshot(slot.SmoothedPeak, rms)));
                return;
            }

            if (slot.Sum.Length != block.Length)
                slot.Sum = new float[block.Length];

            for (var i = 0; i < block.Length; i++)
                slot.Sum[i] += block[i];

            slot.HasContribution = true;
        }
    }

    public static void EndPass()
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                // Nothing sounding: go on releasing rather than freezing on the last reading, and
                // empty the slot once the needle is back at rest. Freezing left the meter parked at
                // whatever the last note peaked at, which reads as a signal that is still there.
                if (!slot.HasContribution)
                {
                    if (slot.Published is null)
                        continue;

                    slot.SmoothedPeak *= ReleaseFactor;
                    if (slot.SmoothedPeak < RestThreshold)
                    {
                        slot.SmoothedPeak = 0f;
                        slot.Published = null;
                    }
                    else
                    {
                        slot.Published = new LevelSnapshot(slot.SmoothedPeak, 0f);
                    }

                    continue;
                }

                var (peak, rms) = ComputeLevels(slot.Sum);

                // Fast attack / slow release: jumps to a new peak immediately but decays
                // gradually, so short transients stay visible instead of flickering past in one block.
                slot.SmoothedPeak = peak > slot.SmoothedPeak ? peak : slot.SmoothedPeak * ReleaseFactor;
                slot.Published = new LevelSnapshot(slot.SmoothedPeak, rms);
            }
        }
    }

    public static LevelSnapshot? TryGet(Guid moduleId) =>
        Slots.TryGetValue(moduleId, out var slot) ? slot.Published : null;

    /// <summary>Sends every meter back to rest — see
    /// <see cref="VisualizationHub.ClearDisplays"/>, which this mirrors.</summary>
    public static void ClearDisplays()
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                slot.Published = null;
                slot.SmoothedPeak = 0f;
                Array.Clear(slot.Sum);
                slot.HasContribution = false;
            }
        }
    }

    public static void BeginRecording()
    {
        // No slots survive BeginSession — see VisualizationHub.BeginRecording.
        BeginSession();
        IsRecording = true;
    }

    public static void EndRecording() => IsRecording = false;

    public static void AdvancePlayback(double elapsedSeconds)
    {
        foreach (var slot in Slots.Values)
        {
            lock (slot)
            {
                if (slot.Timeline is not { Count: > 0 } timeline)
                    continue;

                var index = timeline.FindLastIndex(w => w.TimeSeconds <= elapsedSeconds);
                if (index < 0) index = 0;
                slot.Published = timeline[index].Level;
            }
        }
    }

    private static (float Peak, float Rms) ComputeLevels(float[] block)
    {
        var peak = 0f;
        var sumSquares = 0.0;
        foreach (var sample in block)
        {
            var abs = Math.Abs(sample);
            if (abs > peak) peak = abs;
            sumSquares += (double)sample * sample;
        }
        var rms = block.Length > 0 ? (float)Math.Sqrt(sumSquares / block.Length) : 0f;
        return (peak, rms);
    }
}
