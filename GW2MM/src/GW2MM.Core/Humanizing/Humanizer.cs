using GW2MM.Core.Playback;

namespace GW2MM.Core.Humanizing;

/// <summary>
/// Humanizes solo play: one computer driving several game windows while the client is not connected to a host. Host
/// and client playback never use it, because every player there must follow the host's timing exactly.
/// <para>A Guild Wars 2 octave change (9 / 0) takes effect only after a round trip to the server, so the next note in
/// that window must wait OctaveDelay. The plain arrangement starts every chord on its beat and lets the notes that wait
/// come late. When a chord spans two octaves, that is sometimes its melody, so the tune limps. Humanizing plays it the
/// way a player would:</para>
/// <list type="bullet">
/// <item><b>The melody stays on the beat.</b> The note group with the chord's highest note lands on the chord's time.
/// Lower groups the octave route plays first come just before it, like a quick roll into the melody. Groups it plays
/// afterwards follow as soon as the game allows. The order of the groups and the octave changes are exactly the plain
/// arrangement's, so no octave change is ever added.</item>
/// <item><b>Octave changes before a chord</b> go into the gap before it, at least OctaveDelay ahead of the next press.</item>
/// <item><b>No stumble-and-rush.</b> When the previous notes leave too little room, the chord waits just long enough.
/// The song then eases back to its own time at a tempo only 2% faster, instead of jumping back at once. With several
/// windows, a window may trail the others by a few ms on its own before the others wait for it.</item>
/// <item><b>Tiny, correlated timing.</b> Each window drifts by a millisecond or so around the beat: correlated from chord
/// to chord, smaller for closely spaced notes, and the same for every note of a chord. Chords are never rolled.
/// The game already varies note timing by up to a frame, and rolls would split chords across frames.</item>
/// </list>
/// </summary>
public static class Humanizer
{
    /// <summary>One key press of the performance (internal, for tests).</summary>
    internal sealed record Press(double Time, int Window, string Keys, bool IsSwap, bool IsMelody, double ScoreMs);

    /// <summary>Correlation time of a window's timing drift (ms of score time).</summary>
    private const double DriftMs = 300;

    /// <summary>The playback events of a humanized solo performance; the first press is at <paramref name="startMs"/> or later.</summary>
    internal static List<PlaybackEvent> Perform(SoloScore score, double octaveDelayMs, HumanizeLevel level, Random random, double startMs)
    {
        var presses = Render(score, octaveDelayMs, level, random);
        if (presses.Count == 0) return new List<PlaybackEvent>();
        var shift = Math.Max(0, -presses.Min(p => p.Time));
        return presses.Select((p, i) => (p, i)).OrderBy(x => x.p.Time).ThenBy(x => x.i)
            .GroupBy(x => Math.Round(x.p.Time + shift, 1))
            .Select(g => new PlaybackEvent(startMs + g.Key, string.Concat(g.Select(x => x.p.Keys))))
            .ToList();
    }

    /// <summary>Every press of the performance, in score order (times in ms from the score's start, possibly negative).</summary>
    internal static List<Press> Render(SoloScore score, double octaveDelayMs, HumanizeLevel level, Random random)
    {
        var profile = HumanizeProfile.For(level);
        var delay = Math.Max(1, octaveDelayMs);
        var several = score.WindowCount > 1;

        // Each chord's distance (score ms) to its window's previous and next chord, which scales its timing deviation.
        var spacing = new Dictionary<ScoreChord, double>();
        foreach (var window in score.Chords.GroupBy(c => c.Window))
        {
            var chords = window.OrderBy(c => c.Ms).ToList();
            for (var i = 0; i < chords.Count; i++)
                spacing[chords[i]] = Math.Min(i > 0 ? chords[i].Ms - chords[i - 1].Ms : double.PositiveInfinity,
                    i + 1 < chords.Count ? chords[i + 1].Ms - chords[i].Ms : double.PositiveInfinity);
        }
        var windows = score.Chords.Select(c => c.Window).Distinct().OrderBy(w => w).ToList();
        var offset = windows.ToDictionary(w => w, _ => several ? Clamp(profile.PlayerOffsetMs * Gaussian(random), 2.5 * profile.PlayerOffsetMs) : 0);
        var drift = windows.ToDictionary(w => w, _ => Gaussian(random));
        var lastChordMs = new Dictionary<int, double>();
        var lastPress = new Dictionary<int, double>();
        var lag = windows.ToDictionary(w => w, _ => 0.0);
        double late = 0;            // how far the whole performance is behind the score (time octave changes took)
        double? previousMs = null;
        var presses = new List<Press>();

        foreach (var moment in score.Chords.GroupBy(c => c.Tick).OrderBy(g => g.Key))
        {
            var ms = moment.First().Ms;
            if (previousMs is { } p)
            {
                // Ease back towards the score's time: slightly faster than written while behind.
                var dt = ms - p;
                late = Math.Max(0, late - profile.CatchUp * dt);
                foreach (var w in windows) lag[w] = Math.Max(0, lag[w] - 2 * profile.CatchUp * dt);
            }
            previousMs = ms;

            var plans = new List<(ScoreChord Chord, double Target, double Earliest, double Pre)>();
            foreach (var chord in moment.OrderBy(c => c.Window))
            {
                var w = chord.Window;
                // A slow drift around the beat (AR(1) over score time), a little smaller on closely spaced notes.
                var rho = lastChordMs.TryGetValue(w, out var before) ? Math.Exp(-(ms - before) / DriftMs) : 0;
                drift[w] = rho * drift[w] + Math.Sqrt(1 - rho * rho) * Gaussian(random);
                lastChordMs[w] = ms;
                var sigma = Math.Min(profile.TimingCapMs, profile.Timing * spacing[chord]);
                var target = ms + late + lag[w] + offset[w] + Clamp(sigma * drift[w], 2.5 * sigma);

                // The melody group's offset after the chord's first group, and the earliest the melody can then sound:
                // after the window's previous press, its octave changes before the chord, and the groups before it.
                var pre = 0.0;
                for (var i = 0; i < chord.AnchorIndex; i++) pre += Gap(chord.Steps[i]);
                var earliest = lastPress.TryGetValue(w, out var last)
                    ? last + 1 + chord.LeadingSwaps.Count * delay + pre
                    : double.NegativeInfinity;
                plans.Add((chord, target, earliest, pre));
            }

            // Several windows: one that needs more time may trail by up to PlayerLagMs; beyond that everyone waits.
            if (several)
            {
                var wait = plans.Max(x => Math.Max(0, lag[x.Chord.Window] + Math.Max(0, x.Earliest - x.Target) - profile.PlayerLagMs));
                if (wait > 0)
                {
                    late += wait;
                    plans = plans.Select(x => (x.Chord, x.Target + wait, x.Earliest, x.Pre)).ToList();
                }
            }

            foreach (var (chord, target, earliest, pre) in plans)
            {
                var w = chord.Window;
                var melody = Math.Max(target, earliest);
                if (several) lag[w] += melody - target;
                else late += melody - target;

                var first = melody - pre;
                // Octave changes before the chord: in the gap, each at least OctaveDelay before the next press.
                var swaps = chord.LeadingSwaps;
                double? previous = lastPress.TryGetValue(w, out var lp) ? lp + 1 : null;
                for (var j = 0; j < swaps.Count; j++)
                {
                    var latest = first - (swaps.Count - j) * delay;
                    var at = previous is { } e ? (e + latest) / 2 : latest - delay / 2;
                    presses.Add(new Press(at, w, swaps[j], true, false, chord.Ms));
                    previous = at + delay;
                }
                var t = first;
                for (var i = 0; i < chord.Steps.Count; i++)
                {
                    var step = chord.Steps[i];
                    presses.Add(new Press(t, w, step.Keys, step.IsSwap, i == chord.AnchorIndex, chord.Ms));
                    lastPress[w] = t;
                    t += Gap(step);
                }
            }
        }
        return presses;

        double Gap(ChordStep step) => step.IsSwap ? delay : 1;
    }

    private static double Clamp(double value, double limit) => Math.Clamp(value, -limit, limit);

    private static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
