using GW2MM.Core.Arrangement;

namespace GW2MM.Core.Playback;

/// <summary>How much solo playback is humanized (Settings → Humanize solo play).</summary>
public enum HumanizeLevel { Off = 0, Subtle = 1, Natural = 2, Loose = 3 }

/// <summary>
/// Humanizes solo playback (one computer driving several game windows; never used for host/client play, where every
/// machine must follow the host's timing exactly). Built around how Guild Wars 2 instruments behave: an octave change
/// (9 / 0) only takes effect after a round trip to the server, so the first note after it must wait OctaveDelay.
/// <list type="number">
/// <item><b>Natural micro-timing.</b> Each window drifts slightly around the beat with correlated (1/f, "pink") noise
/// instead of independent per-note jitter. Listeners hear long-range correlated deviations as human and white-noise
/// jitter as sloppy (Hennig et al., "The Nature and Perception of Fluctuations in Human Musical Rhythms", PLoS ONE
/// 2011). A small white component is added on top. Like a player's, the deviation shrinks with the spacing of the
/// notes (it is stated for notes a quarter second apart), so fast runs stay even: a note never moves by more than
/// 15% of the time to its neighbours.</item>
/// <item><b>Room for octave changes, as rubato.</b> Wherever a window's next note comes sooner than OctaveDelay after
/// an octave change, the whole ensemble slows down over the span between the two presses (with a short lead-in), so
/// every window stays together. The note is not pushed late on its own with an abrupt catch-up. Notes that the
/// arrangement pressed together with an octave change ("9z") get the same room, so they sound in the right octave.</item>
/// <item><b>Chord rolls inside one octave.</b> The notes of a chord in one window are spread over a few milliseconds,
/// low to high, as a player's fingers would (less in fast passages). A roll never crosses an octave change, because
/// chords are only ever rolled within a group the arrangement already plays in one octave. So humanizing never adds
/// an octave change.</item>
/// </list>
/// The micro-timing is decided first and the room for octave changes made around it, so every press stays in order
/// and every octave change at least OctaveDelay ahead of the next press in its window.
/// </summary>
public static class Humanizer
{
    /// <summary>Tuning per level: correlated timing deviation (σ, ms), white jitter (σ, ms), chord roll (max ms) and
    /// how long the slow-down before a needed octave change is allowed to lead in (ms).</summary>
    public sealed record Profile(double DriftMs, double JitterMs, double RollMs, double LeadInMs);

    public static Profile For(HumanizeLevel level) => level switch
    {
        HumanizeLevel.Subtle => new Profile(3, 1, 8, 120),
        HumanizeLevel.Natural => new Profile(6, 2, 15, 250),
        HumanizeLevel.Loose => new Profile(10, 3, 25, 400),
        _ => new Profile(0, 0, 0, 0),
    };

    private const int Down = 13, Up = 14; // KeyMap offsets of keys 9 and 0

    /// <summary>Note spacing (ms) the profile's deviations are stated for; closer notes deviate proportionally less.</summary>
    private const double ReferenceSpacingMs = 250;

    private sealed class Press
    {
        public int Window;
        public string Keys = "";
        public bool IsSwap;
        public double Ideal;      // song time the arrangement gave it (ms)
        public double Planned;    // song time with its micro-timing
        public int Point;         // index of its planned time among the distinct planned times
        public double Offset;     // fixed offset after the point's warped time (presses sharing one planned time)
        public double Time;       // result (ms)
        public int Seq;           // original order
    }

    /// <summary>
    /// Humanizes a solo part. <paramref name="events"/> are (song time in ms, encoded keys) in playing order, as the
    /// arrangement made them; the result is the playback events (times offset by <paramref name="startMs"/>).
    /// </summary>
    public static List<PlaybackEvent> Apply(IReadOnlyList<(double Time, string Keys)> events, double octaveDelayMs,
        HumanizeLevel level, Random random, double startMs = 0)
    {
        var profile = For(level);
        var presses = Split(events);
        if (presses.Count == 0) return new List<PlaybackEvent>();
        var gapAfterSwap = Math.Max(1, octaveDelayMs);
        double Gap(Press p) => p.IsSwap ? gapAfterSwap : 1;

        // How far each press is from its window's presses at the song times before and after (ms).
        var spacing = new Dictionary<Press, double>();
        foreach (var window in presses.GroupBy(p => p.Window))
        {
            var times = window.Select(p => p.Ideal).Distinct().OrderBy(t => t).ToList();
            foreach (var p in window)
            {
                var k = times.BinarySearch(p.Ideal);
                spacing[p] = Math.Min(k > 0 ? times[k] - times[k - 1] : double.PositiveInfinity,
                    k + 1 < times.Count ? times[k + 1] - times[k] : double.PositiveInfinity);
            }
        }
        double Scale(Press p) => Math.Min(1, spacing[p] / ReferenceSpacingMs);

        // 1. Micro-timing: per-window pink noise (Voss–McCartney over the window's song times) plus a little white
        //    noise. Presses at one song time in a window move together.
        foreach (var p in presses) p.Planned = p.Ideal;
        if (profile.DriftMs > 0 || profile.JitterMs > 0)
        {
            foreach (var window in presses.GroupBy(p => p.Window))
            {
                var pink = new PinkNoise(random);
                foreach (var together in window.GroupBy(p => p.Ideal).OrderBy(g => g.Key))
                {
                    var p = together.First();
                    var d = (profile.DriftMs * pink.Next() + profile.JitterMs * Gaussian(random)) * Scale(p);
                    var limit = Math.Min(2.5 * (profile.DriftMs + profile.JitterMs), 0.15 * spacing[p]);
                    foreach (var q in together) q.Planned = p.Ideal + Math.Clamp(d, -limit, limit);
                }
            }
        }

        // Distinct planned times ("points"); presses sharing a point in one window follow each other at fixed offsets.
        var points = presses.Select(p => p.Planned).Distinct().OrderBy(t => t).ToList();
        var pointIndex = points.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        foreach (var p in presses) p.Point = pointIndex[p.Planned];
        foreach (var group in presses.GroupBy(p => (p.Window, p.Point)))
        {
            double offset = 0;
            Press? previous = null;
            foreach (var p in group.OrderBy(p => p.Seq))
            {
                if (previous is not null) offset += Gap(previous);
                p.Offset = offset;
                previous = p;
            }
        }

        // 2. The least global delay (a step function over the points) that gives every window its gaps, and where
        //    each step came from; then the steps spread into gradual slow-downs; then the minimum enforced again.
        var steps = new List<(int From, int To, double Amount)>();
        Warp(points, presses, Gap, lowerBound: null, steps);
        var smooth = new double[points.Count];
        foreach (var (from, to, amount) in steps)
        {
            var begin = points[from] - profile.LeadInMs;
            var end = points[to];
            for (var i = 0; i < points.Count; i++)
            {
                var t = points[i];
                smooth[i] += t >= end ? amount : t <= begin ? 0 : amount * (t - begin) / (end - begin);
            }
        }
        var warp = Warp(points, presses, Gap, smooth, null);
        foreach (var p in presses) p.Time = p.Planned + warp[p.Point] + p.Offset;
        Repair(presses, Gap); // a safety net: the warp already keeps every gap

        // 3. Chord rolls inside one octave group, low to high, never into the next press of the window.
        var output = new List<(double Time, int Window, string Keys)>();
        foreach (var window in presses.GroupBy(p => p.Window))
        {
            var ordered = window.OrderBy(p => p.Time).ThenBy(p => p.Seq).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var p = ordered[i];
                var keys = p.Keys;
                if (!p.IsSwap && keys.Length > 1 && profile.RollMs > 0)
                {
                    var room = i + 1 < ordered.Count ? ordered[i + 1].Time - p.Time - Gap(p) : double.PositiveInfinity;
                    var roll = Math.Min(profile.RollMs * (0.5 + 0.5 * random.NextDouble()) * Scale(p), room / 2);
                    if (roll >= 1)
                    {
                        var sorted = keys.OrderBy(c => c).ToList(); // offsets rise with pitch inside an octave
                        for (var k = 0; k < sorted.Count; k++)
                            output.Add((p.Time + roll * k / (sorted.Count - 1), p.Window, sorted[k].ToString()));
                        continue;
                    }
                }
                output.Add((p.Time, p.Window, keys));
            }
        }

        // Presses at the same moment in different windows go out together, as one event.
        var start = Math.Min(0, output.Min(o => o.Time));
        return output.OrderBy(o => o.Time).ThenBy(o => o.Window)
            .GroupBy(o => Math.Round(o.Time - start, 1))
            .Select(g => new PlaybackEvent(startMs + g.Key, string.Concat(g.Select(o => o.Keys))))
            .ToList();
    }

    /// <summary>
    /// Splits each event into one press per window, and each window's keys at its octave changes (an octave change is
    /// a press of its own), keeping the order.
    /// </summary>
    private static List<Press> Split(IReadOnlyList<(double Time, string Keys)> events)
    {
        var presses = new List<Press>();
        var seq = 0;
        foreach (var (time, keys) in events)
        {
            var byWindow = new Dictionary<int, List<char>>();
            var order = new List<int>();
            foreach (var c in keys)
            {
                if (KeyMap.Decode(c) is not { } k) continue;
                if (!byWindow.TryGetValue(k.Window, out var list)) { byWindow[k.Window] = list = new List<char>(); order.Add(k.Window); }
                list.Add(c);
            }
            foreach (var w in order)
            {
                var current = new List<char>();
                void Flush()
                {
                    if (current.Count > 0) presses.Add(new Press { Window = w, Keys = new string(current.ToArray()), Ideal = time, Seq = seq++ });
                    current.Clear();
                }
                foreach (var c in byWindow[w])
                {
                    var offset = (c - 192) % 16;
                    if (offset is Down or Up)
                    {
                        Flush();
                        presses.Add(new Press { Window = w, Keys = c.ToString(), IsSwap = true, Ideal = time, Seq = seq++ });
                    }
                    else current.Add(c);
                }
                Flush();
            }
        }
        return presses;
    }

    /// <summary>
    /// The global delay at each point: never decreasing, at least <paramref name="lowerBound"/>, and enough for every
    /// window's first press at a point to follow its previous press by the required gap. Records each increase and the
    /// point of the press that required it in <paramref name="steps"/>.
    /// </summary>
    private static double[] Warp(List<double> points, List<Press> presses, Func<Press, double> gap, double[]? lowerBound,
        List<(int From, int To, double Amount)>? steps)
    {
        var delay = new double[points.Count];
        var firstAt = presses.GroupBy(p => (p.Window, p.Point)).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Seq).First());
        var lastAt = presses.GroupBy(p => (p.Window, p.Point)).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Seq).Last());
        var windowsAt = presses.GroupBy(p => p.Point).ToDictionary(g => g.Key, g => g.Select(p => p.Window).Distinct().ToList());
        var lastOfWindow = new Dictionary<int, Press>();
        for (var i = 0; i < points.Count; i++)
        {
            var d = i > 0 ? delay[i - 1] : 0;
            if (lowerBound is not null) d = Math.Max(d, lowerBound[i]);
            var before = d;
            var cause = -1;
            foreach (var w in windowsAt[i])
            {
                if (!lastOfWindow.TryGetValue(w, out var prev)) continue;
                var first = firstAt[(w, i)];
                var need = points[prev.Point] + delay[prev.Point] + prev.Offset + gap(prev) - (points[i] + first.Offset);
                if (need > d) { d = need; cause = prev.Point; }
            }
            delay[i] = d;
            if (steps is not null && d > before && cause >= 0) steps.Add((cause, i, d - before));
            foreach (var w in windowsAt[i]) lastOfWindow[w] = lastAt[(w, i)];
        }
        return delay;
    }

    /// <summary>Keeps each window's presses in order with their gaps (moving a press later only when needed).</summary>
    private static void Repair(List<Press> presses, Func<Press, double> gap)
    {
        foreach (var window in presses.GroupBy(p => p.Window))
        {
            Press? previous = null;
            foreach (var p in window.OrderBy(p => p.Seq))
            {
                if (previous is not null) p.Time = Math.Max(p.Time, previous.Time + gap(previous));
                previous = p;
            }
        }
    }

    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    /// <summary>Voss–McCartney pink noise: rows of random values refreshed every 1, 2, 4, … steps, summed (σ ≈ 1).</summary>
    private sealed class PinkNoise
    {
        private const int Rows = 6;
        private readonly Random _random;
        private readonly double[] _rows = new double[Rows];
        private int _counter;

        public PinkNoise(Random random)
        {
            _random = random;
            for (var r = 0; r < Rows; r++) _rows[r] = Gaussian(random);
        }

        public double Next()
        {
            _counter++;
            // The row to refresh is the number of trailing zeros of the counter (row 0 every step, row 1 every 2, …).
            var row = Math.Min(Rows - 1, System.Numerics.BitOperations.TrailingZeroCount(_counter));
            _rows[row] = Gaussian(_random);
            return _rows.Sum() / Math.Sqrt(Rows);
        }
    }
}
