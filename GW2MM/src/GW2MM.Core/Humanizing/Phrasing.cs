namespace GW2MM.Core.Humanizing;

/// <summary>A moment where any window starts notes: its score position, highest pitch and the silence after it.</summary>
internal sealed record Onset(double Tick, double Ms, int TopPitch, double RestAfterMs);

/// <summary>A phrase: onsets <see cref="First"/>..<see cref="Last"/>, and how strong the boundary after it is (0..1).</summary>
internal sealed record Phrase(int First, int Last, double Strength);

/// <summary>
/// Splits the performance into phrases, the units a player shapes with tempo. Boundaries come from the Local Boundary
/// Detection Model (Cambouropoulos, "The Local Boundary Detection Model (LBDM) and its application in the study of
/// expressive timing", ICMC 2001). A boundary is likelier where the time between onsets, the melodic interval or the
/// silence changes sharply compared with its neighbours. Weights are 0.5 for time between onsets, 0.25 for pitch
/// and 0.25 for silence. Phrases are kept between about 2.4 and 12 s. A longer stretch without a clear boundary is
/// split at the bar line nearest its middle, so steady accompaniment figures still get bar-group phrasing.
/// </summary>
internal static class Phrasing
{
    public const double MinPhraseMs = 2400;
    public const double MaxPhraseMs = 12000;
    private const int MinOnsets = 4;

    /// <summary>Every distinct onset of the score in time order.</summary>
    public static List<Onset> Onsets(SoloScore score)
    {
        var groups = score.Chords.GroupBy(c => c.Tick).OrderBy(g => g.Key).ToList();
        var spans = score.NoteSpans.OrderBy(s => s.Start).ToList();
        var onsets = new List<Onset>(groups.Count);
        var latestEnd = double.NegativeInfinity;
        var n = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            var ms = groups[i].First().Ms;
            // Notes that started at or before this onset: when is the last of them released?
            while (n < spans.Count && spans[n].Start <= ms + 0.5)
            {
                if (spans[n].End is { } e) latestEnd = Math.Max(latestEnd, e);
                n++;
            }
            var next = i + 1 < groups.Count ? groups[i + 1].First().Ms : ms;
            var rest = double.IsNegativeInfinity(latestEnd) ? 0 : Math.Max(0, next - Math.Max(latestEnd, ms));
            onsets.Add(new Onset(groups[i].Key, ms, groups[i].Max(c => c.TopPitch), rest));
        }
        return onsets;
    }

    /// <summary>LBDM boundary strength of the interval after each onset (0..1; the last onset has none).</summary>
    public static double[] BoundaryStrengths(IReadOnlyList<Onset> onsets)
    {
        var n = onsets.Count;
        var result = new double[n];
        if (n < 3) return result;
        var ioi = new double[n - 1];
        var pitch = new double[n - 1];
        var rest = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            ioi[i] = onsets[i + 1].Ms - onsets[i].Ms;
            pitch[i] = Math.Abs(onsets[i + 1].TopPitch - onsets[i].TopPitch);
            rest[i] = onsets[i].RestAfterMs;
        }
        var a = Profile(ioi);
        var b = Profile(pitch);
        var c = Profile(rest);
        for (var i = 0; i < n - 1; i++) result[i] = 0.5 * a[i] + 0.25 * b[i] + 0.25 * c[i];
        return result;
    }

    /// <summary>LBDM strength of one parameter: its value times the degree of change to each neighbour, normalized to 0..1.</summary>
    private static double[] Profile(double[] x)
    {
        static double Change(double p, double q) => p + q > 0 ? Math.Abs(p - q) / (p + q) : 0;
        var s = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
            s[i] = x[i] * ((i > 0 ? Change(x[i - 1], x[i]) : 0) + (i + 1 < x.Length ? Change(x[i], x[i + 1]) : 0));
        var max = s.Length > 0 ? s.Max() : 0;
        if (max > 0) for (var i = 0; i < s.Length; i++) s[i] /= max;
        return s;
    }

    /// <summary>The phrases: strong LBDM peaks first, then long stretches split at bar lines.</summary>
    public static List<Phrase> Phrases(SoloScore score, IReadOnlyList<Onset> onsets)
    {
        var n = onsets.Count;
        if (n == 0) return new List<Phrase>();
        var meter = score.Meter;
        var songEnd = Math.Max(onsets[^1].Ms, score.EndMs);
        double EndAfter(int last) => last + 1 < n ? onsets[last + 1].Ms : songEnd;
        var strength = BoundaryStrengths(onsets);
        var mean = n > 1 ? strength.Take(n - 1).Average() : 0;
        var sd = n > 1 ? Math.Sqrt(strength.Take(n - 1).Average(s => (s - mean) * (s - mean))) : 0;
        var threshold = Math.Max(0.3, mean + 0.75 * sd);

        // Peaks of the strength profile above the threshold, strongest first, each kept if both phrases it creates
        // stay long enough.
        var cuts = new SortedSet<int>(); // a cut after onset i
        var peaks = Enumerable.Range(0, Math.Max(0, n - 1))
            .Where(i => strength[i] >= threshold && (i == 0 || strength[i] >= strength[i - 1]) && (i + 1 >= n - 1 || strength[i] > strength[i + 1]))
            .OrderByDescending(i => strength[i]);
        foreach (var i in peaks)
            if (Fits(i)) cuts.Add(i);

        // Too long without a boundary: cut at the bar line nearest the middle (else the strongest point).
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (first, last) in Bounds())
            {
                var end = EndAfter(last);
                if (end - onsets[first].Ms <= MaxPhraseMs) continue;
                var middle = (onsets[first].Ms + end) / 2;
                var options = Enumerable.Range(first, last - first).Where(Fits).ToList();
                if (options.Count == 0) continue;
                var bar = options.Where(i => meter.IsDownbeat(onsets[i + 1].Tick)).ToList();
                var pick = (bar.Count > 0 ? bar : options)
                    .OrderBy(i => Math.Abs(onsets[i + 1].Ms - middle) - 200 * strength[i]).First();
                cuts.Add(pick);
                changed = true;
                break;
            }
        }

        var phrases = new List<Phrase>();
        foreach (var (first, last) in Bounds())
            phrases.Add(new Phrase(first, last, last + 1 < n ? Math.Clamp(0.4 + 0.6 * strength[last], 0.4, 1) : 1));
        return phrases;

        List<(int First, int Last)> Bounds()
        {
            var list = new List<(int, int)>();
            var start = 0;
            foreach (var c in cuts) { list.Add((start, c)); start = c + 1; }
            list.Add((start, n - 1));
            return list;
        }

        // Would a cut after onset i leave phrases of at least MinOnsets onsets and MinPhraseMs on both sides?
        bool Fits(int i)
        {
            var before = cuts.GetViewBetween(int.MinValue, i - 1);
            var after = cuts.GetViewBetween(i + 1, int.MaxValue);
            var first = before.Count > 0 ? before.Max + 1 : 0;
            var last = after.Count > 0 ? after.Min : n - 1;
            var leftEnd = onsets[i + 1].Ms;
            var rightEnd = EndAfter(last);
            return !cuts.Contains(i) && i - first + 1 >= MinOnsets && last - i >= MinOnsets
                && leftEnd - onsets[first].Ms >= MinPhraseMs && rightEnd - onsets[i + 1].Ms >= MinPhraseMs;
        }
    }
}
