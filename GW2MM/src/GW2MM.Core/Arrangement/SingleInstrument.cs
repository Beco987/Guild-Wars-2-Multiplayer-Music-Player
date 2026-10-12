using GW2MM.Core.Ahk;

namespace GW2MM.Core.Arrangement;

/// <summary>
/// One-player songs (Players = 1 in Data.csv): every track's notes are played on one instrument. This is what the
/// original client's unfinished single-player engine (<c>OnePlayer</c> / <c>DupeSeparation</c>) set out to do.
/// <para>A Guild Wars 2 instrument plays one octave at a time: keys 1–7 and F1–F5 for its 12 notes, 8 for the C above.
/// It has three octaves (C3–C6), changed with 9 (down) and 0 (up), and the game applies a change only after a round
/// trip to the server. So:</para>
/// <list type="bullet">
/// <item>A chord whose notes lie in more than one octave is played octave by octave. Its notes in one octave are
/// pressed together, then the octave changes, then the next group follows OctaveDelay later. The order is the one
/// needing the fewest octave changes (from the lowest octave on a tie).</item>
/// <item>After every octave change the next press waits at least OctaveDelay (150 ms if it is blank). The changes
/// leading into a chord are moved into the gap before it where possible. When the song leaves no room, the
/// following notes are delayed just enough, and the song catches up again at the next rest.</item>
/// <item>Pitches outside C3–C6 are moved by whole octaves into range.</item>
/// </list>
/// </summary>
public static class SingleInstrument
{
    private const int Lowest = 48;   // C3, bottom of the low octave
    private const int Highest = 84;  // C6, the top C of the high octave
    private const int Down = 13, Up = 14; // KeyMap offsets of keys 9 and 0

    /// <summary>OctaveDelay in ms for the one-player schedule (150 ms when blank or zero).</summary>
    public static double DelayMs(string octaveDelay) => AhkValue.Number(octaveDelay) is { } v && v > 0 ? Math.Min(v, 60000) : 150;

    /// <summary>
    /// The one-player part for a slot whose output window is 1 (the first window: Player1, or the first open window
    /// in solo play), in the same form as the multiplayer arrangement: element 0 is empty and element k+1 follows
    /// element k after <c>DelaysMs[k]</c>.
    /// </summary>
    public static SlotPlan Arrange(CsvSong song, int slot, string octaveDelay, double? tempoFactor)
    {
        var delay = DelayMs(octaveDelay);
        var toMs = TickToMs(song, tempoFactor);

        var chords = new SortedDictionary<double, SortedSet<int>>();
        for (var t = 1; t <= CsvSong.MaxTrack; t++)
            for (var k = 0; k < song.TrackTimes[t].Count && k < song.TrackPitches[t].Count; k++)
                if (song.TrackPitches[t][k] is { } pitch && AhkValue.Number(song.TrackTimes[t][k]) is { } tick)
                {
                    if (!chords.TryGetValue(tick, out var set)) chords[tick] = set = new SortedSet<int>();
                    set.Add(IntoRange(pitch));
                }

        var events = new List<(double Time, string Keys)>();
        var octave = 1;                                   // octaves are reset to the middle one before a song
        double? lastPress = null, lastSwap = null;
        foreach (var (tick, pitches) in chords)
        {
            var time = toMs(tick);
            var steps = Route(pitches.ToList(), ref octave);
            var leading = steps.TakeWhile(s => s.Swap != 0).Count();
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                double at;
                if (step.Swap != 0)
                {
                    var earliest = Math.Max(lastPress is { } p ? p + 1 : double.NegativeInfinity, lastSwap is { } s ? s + delay : double.NegativeInfinity);
                    if (i < leading)
                    {
                        // Into the gap before the chord: evenly spread (the midpoint for one change) but never closer
                        // than OctaveDelay to the chord, and as early as possible when the gap is too short.
                        var latest = time - (leading - i) * delay;
                        var even = lastPress is { } prev && prev < time ? prev + (i + 1) * (time - prev) / (leading + 1) : latest;
                        at = Math.Max(earliest, Math.Min(even, latest));
                    }
                    else at = double.IsNegativeInfinity(earliest) ? time : earliest; // mid-chord: straight after the notes
                    lastSwap = at;
                    events.Add((at, SwapKey(step.Swap)));
                }
                else
                {
                    at = Math.Max(time, Math.Max(lastPress is { } p ? p + 1 : double.NegativeInfinity, lastSwap is { } s ? s + delay : double.NegativeInfinity));
                    events.Add((at, NotesKey(step.Notes)));
                }
                lastPress = at;
            }
        }

        // Element 0 is empty (the host never presses it); it sits at the song's start, or earlier when the first
        // chord's octave change was moved in front of it.
        var start = Math.Min(0, events.Count > 0 ? events[0].Time : 0);
        var notes = new List<string> { "" };
        var delays = new List<long>();
        var previous = (long)Math.Round(start);
        foreach (var (t, keys) in events)
        {
            var now = (long)Math.Round(t);
            delays.Add(Math.Max(0, now - previous));
            notes.Add(keys);
            previous = Math.Max(previous, now);
        }
        delays.Add(0);
        return new SlotPlan(slot, notes, delays, notes.Count > 3);
    }

    /// <summary>One step of a chord: an octave change (+1 up / -1 down) or the notes to press together (KeyMap offsets).</summary>
    internal readonly record struct Step(int Swap, IReadOnlyList<int> Notes);

    /// <summary>
    /// The octave groups of a chord in playing order, with the octave changes between them, starting from
    /// <paramref name="octave"/> (updated to where the chord ends).
    /// </summary>
    internal static List<Step> Route(IReadOnlyList<int> pitches, ref int octave)
    {
        // Notes other than C belong to one octave. A C can be the bottom of one octave or the top (key 8) of the one
        // below, so it joins an octave that is needed anyway, else the one nearest the current octave.
        var groups = new SortedDictionary<int, SortedSet<int>>();
        void Add(int o, int pitch)
        {
            if (!groups.TryGetValue(o, out var g)) groups[o] = g = new SortedSet<int>();
            g.Add(pitch - (Lowest + 12 * o));
        }
        foreach (var p in pitches.Where(p => p % 12 != 0)) Add((p - Lowest) / 12, p);
        foreach (var c in pitches.Where(p => p % 12 == 0).OrderByDescending(p => p))
        {
            var options = new[] { (c - Lowest) / 12, (c - Lowest) / 12 - 1 }.Where(o => o is >= 0 and <= 2).ToList();
            var current = octave;
            var o = options.FirstOrDefault(groups.ContainsKey, -1);
            if (o < 0) o = options.OrderBy(x => Math.Abs(x - current)).ThenBy(x => x).First();
            Add(o, c);
        }
        if (groups.Count == 0) return new List<Step>();

        var lo = groups.Keys.First();
        var hi = groups.Keys.Last();
        // Visit every needed octave with the fewest changes; on a tie go down first (bass before melody).
        var upFirst = octave <= lo || (octave < hi && (hi - octave) + (hi - lo) < (octave - lo) + (hi - lo));
        var route = new List<int>();
        if (octave <= lo) for (var o = octave; o <= hi; o++) route.Add(o);
        else if (octave >= hi) for (var o = octave; o >= lo; o--) route.Add(o);
        else if (upFirst) { for (var o = octave; o <= hi; o++) route.Add(o); for (var o = hi - 1; o >= lo; o--) route.Add(o); }
        else { for (var o = octave; o >= lo; o--) route.Add(o); for (var o = lo + 1; o <= hi; o++) route.Add(o); }

        var steps = new List<Step>();
        var played = new HashSet<int>();
        for (var i = 0; i < route.Count; i++)
        {
            if (i > 0) steps.Add(new Step(route[i] > route[i - 1] ? 1 : -1, Array.Empty<int>()));
            if (groups.TryGetValue(route[i], out var g) && played.Add(route[i])) steps.Add(new Step(0, g.ToList()));
            if (played.Count == groups.Count) { octave = route[i]; break; }
        }
        return steps;
    }

    // Window 1's key characters (see KeyMap): offset 0..12 = the notes C..C (1, F1, 2, …, 7, 8), 13 = 9, 14 = 0.
    private static string SwapKey(int swap) => ((char)(192 + (swap > 0 ? Up : Down))).ToString();
    private static string NotesKey(IEnumerable<int> offsets) => string.Concat(offsets.Select(o => (char)(192 + o)));

    private static int IntoRange(int pitch)
    {
        while (pitch < Lowest) pitch += 12;
        while (pitch > Highest) pitch -= 12;
        return pitch;
    }

    /// <summary>Song time in ms at a tick: tempo / 500000 ms per tick (as playback converts), times the tempo factor.</summary>
    internal static Func<double, double> TickToMs(CsvSong song, double? tempoFactor)
    {
        var changes = new List<(double Tick, double Tempo)>();
        for (var k = 0; k < song.TempoTimes.Count && k < song.TempoMarkers.Count; k++)
        {
            var marker = song.TempoMarkers[k];
            if (AhkValue.Number(song.TempoTimes[k]) is { } tick && marker.Length > 1 && AhkValue.Number(marker[1..]) is { } tempo && tempo > 0)
                changes.Add((tick, tempo));
        }
        changes = changes.Select((c, k) => (c, k)).OrderBy(x => x.c.Tick).ThenBy(x => x.k).Select(x => x.c).ToList(); // stable
        var factor = tempoFactor is { } f && f > 0 ? f : 1;
        return tick =>
        {
            double ms = 0, at = 0, tempo = changes.Count > 0 ? changes[0].Tempo : 500000;
            foreach (var c in changes)
            {
                if (c.Tick >= tick) break;
                if (c.Tick > at) { ms += (c.Tick - at) * tempo; at = c.Tick; }
                tempo = c.Tempo;
            }
            ms += (tick - at) * tempo;
            return ms / 500000 * factor;
        };
    }

    /// <summary>
    /// Who plays a one-player song: every slot that has any track ticked in Port Mapping, or the host when nobody has.
    /// </summary>
    public static IReadOnlyList<int> Players(PortMapping mapping)
    {
        var slots = Enumerable.Range(1, PortMapping.Slots).Where(s => Enumerable.Range(1, PortMapping.Tracks).Any(t => mapping[s, t])).ToList();
        return slots.Count > 0 ? slots : new List<int> { 1 };
    }
}
