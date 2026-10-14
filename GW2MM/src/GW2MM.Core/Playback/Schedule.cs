using GW2MM.Core.Arrangement;

namespace GW2MM.Core.Playback;

/// <summary>One step of a playback loop: when <see cref="DueMs"/> (null = immediately) is reached, press <see cref="Keys"/>.</summary>
public readonly record struct PlaybackEvent(double? DueMs, string Keys);

/// <summary>
/// Anchors a <see cref="SlotPlan"/> to a machine's clock and builds the event lists the original playback
/// timers walked through.
/// </summary>
public static class Schedule
{
    /// <summary>Lead time from the host's clock sample (T1) to the first note, before latency compensation.</summary>
    public const double StartDelayMs = 4000;

    /// <summary>
    /// The original <c>TimeToPlay</c>: element k's time is <c>tick + 4000 - latency + sum(delays[0..k])</c>, in the
    /// clock of the machine that will play the part.
    /// </summary>
    public static List<double> AbsoluteTimes(SlotPlan plan, double tick, double latency)
    {
        var times = new List<double>(plan.DelaysMs.Count);
        double prev = 0;
        for (var k = 0; k < plan.DelaysMs.Count; k++)
        {
            prev = k == 0 ? plan.DelaysMs[0] + StartDelayMs - latency + tick : prev + plan.DelaysMs[k];
            times.Add(prev);
        }
        return times;
    }

    /// <summary>
    /// Host loop (<c>SongPlayer1</c>): starting at index 1, when the clock reaches <c>TimeToPlay[k]</c> it presses
    /// <c>NewNotes[k+1]</c>. The first element (normally the tempo marker) is therefore never pressed.
    /// </summary>
    public static List<PlaybackEvent> HostEvents(IReadOnlyList<string> notes, IReadOnlyList<double> times)
    {
        var events = new List<PlaybackEvent>(times.Count);
        for (var k = 0; k < times.Count; k++)
            events.Add(new PlaybackEvent(times[k], k + 1 < notes.Count ? notes[k + 1] : ""));
        return events;
    }

    /// <summary>
    /// Solo play's Offset setting (not in the original): staggers the windows so no two press at the same instant.
    /// With n windows that have notes, the k-th (by window number) plays everything (k − 1) × Floor(spread / n) ms
    /// later; one window is unchanged. Each window keeps its own timing and order; an event that is due at once
    /// (<c>null</c>) counts as due at <paramref name="nowMs"/>.
    /// </summary>
    public static List<PlaybackEvent> Stagger(IReadOnlyList<PlaybackEvent> events, double spreadMs, double nowMs)
    {
        var windows = events.SelectMany(e => e.Keys).Select(KeyMap.Decode).Where(d => d is not null)
            .Select(d => d!.Value.Window).Distinct().OrderBy(w => w).ToList();
        var step = windows.Count > 1 && spreadMs > 0 ? Math.Floor(spreadMs / windows.Count) : 0;
        if (step <= 0) return events.ToList();
        var rank = windows.Select((w, i) => (w, i)).ToDictionary(x => x.w, x => x.i);
        var parts = new List<(double Due, int Order, string Keys)>();
        foreach (var e in events)
        {
            var due = e.DueMs ?? nowMs;
            foreach (var keys in e.Keys.GroupBy(c => KeyMap.Decode(c)?.Window ?? windows[0]))
                parts.Add((due + rank[keys.Key] * step, parts.Count, new string(keys.ToArray())));
        }
        return parts.OrderBy(p => p.Due).ThenBy(p => p.Order).Select(p => new PlaybackEvent(p.Due, p.Keys)).ToList();
    }

    /// <summary>
    /// Client loop (client <c>SongPlayer1</c>): its index starts at 0, so the first element is pressed as soon as the
    /// song arrives; afterwards element k+1 is pressed at <c>Rest[k]</c>. A missing rest means "immediately".
    /// </summary>
    public static List<PlaybackEvent> ClientEvents(IReadOnlyList<string> notes, IReadOnlyList<double?> rests)
    {
        var events = new List<PlaybackEvent>(rests.Count + 1) { new(null, notes.Count > 0 ? notes[0] : "") };
        for (var k = 0; k < rests.Count; k++)
            events.Add(new PlaybackEvent(rests[k], k + 1 < notes.Count ? notes[k + 1] : ""));
        return events;
    }
}
