namespace GW2MM.Core.Humanizing;

/// <summary>
/// The expressive time map: when each onset is played, as a tempo that a player shapes over each phrase. The score's
/// time between two onsets is stretched or shrunk by a local tempo factor (1 + k). The k values from these rules add up:
/// <list type="bullet">
/// <item><b>Phrase arch</b> — a phrase starts a little slower, moves forward towards its middle and relaxes into its end.
/// The phrase's total length is unchanged. This follows the KTH performance rule system's phrase arch (Friberg, Bresin &amp;
/// Sundberg, "Overview of the KTH rule system for musical performance", Advances in Cognitive Psychology 2006) and
/// Todd's model of phrase-level rubato (JASA 1992). The shape is a parabola, fastest at 45% of the phrase, so a
/// phrase relaxes more at its end than at its start. Each phrase's amount varies by about ±20%.</item>
/// <item><b>Phrase-final lengthening</b> — the last note of a phrase is held a little longer (and the note before it
/// slightly), in proportion to how clear the boundary is: the breath between phrases.</item>
/// <item><b>Downbeat stretch</b> — a note that starts a bar is very slightly longer (an agogic accent).</item>
/// <item><b>Final ritardando</b> — the song slows over its last few beats, following Friberg &amp; Sundberg's model of
/// final ritardandi as a runner stopping ("Does music performance allude to locomotion?", JASA 1999): tempo
/// v(x) = (1 + (w^q − 1)·x)^(1/q) over the normalized position x, with end tempo w and q = 3.</item>
/// <item><b>Tempo wander</b> — a slow, smooth random fluctuation (AR(1), correlation time 4 s), so no two phrases
/// are shaped identically.</item>
/// </list>
/// </summary>
internal static class Expression
{
    private const double ArchPeak = 0.45;      // where in the phrase the arch is fastest
    private const double WanderMs = 4000;      // correlation time of the tempo wander
    private const double RitardQ = 3;          // curvature of the final ritardando
    private const double ArchFullMs = 3000;    // phrases shorter than this get a proportionally smaller arch
    private const double EndCapMs = 1500;      // phrase-final lengthening is at most PhraseEnd × this many ms

    /// <summary>The performance time of each onset (ms; the first onset keeps its score time).</summary>
    public static double[] TimeMap(SoloScore score, IReadOnlyList<Onset> onsets, IReadOnlyList<Phrase> phrases, HumanizeProfile profile, Random random)
    {
        var n = onsets.Count;
        var perf = new double[n];
        if (n == 0) return perf;
        perf[0] = onsets[0].Ms;
        if (n == 1) return perf;
        var k = Factors(score, onsets, phrases, profile, random);
        for (var i = 0; i < n - 1; i++) perf[i + 1] = perf[i] + (onsets[i + 1].Ms - onsets[i].Ms) * (1 + k[i]);
        return perf;
    }

    /// <summary>The local tempo factor k of the time from each onset to the next (time × (1 + k)).</summary>
    public static double[] Factors(SoloScore score, IReadOnlyList<Onset> onsets, IReadOnlyList<Phrase> phrases, HumanizeProfile profile, Random random)
    {
        var n = onsets.Count;
        var k = new double[Math.Max(0, n - 1)];
        if (n < 2) return k;
        double Gap(int i) => onsets[i + 1].Ms - onsets[i].Ms;
        double BeatMs(int i) => score.TickToMs(onsets[i].Tick + score.Meter.BeatTicks(onsets[i].Tick)) - onsets[i].Ms;

        // Phrase arch and phrase-final lengthening.
        var m = Math.Max(ArchPeak, 1 - ArchPeak);
        var archMean = (Math.Pow(1 - ArchPeak, 3) + Math.Pow(ArchPeak, 3)) / (3 * m * m);
        foreach (var phrase in phrases)
        {
            var start = onsets[phrase.First].Ms;
            var end = phrase.Last + 1 < n ? onsets[phrase.Last + 1].Ms : Math.Max(onsets[phrase.Last].Ms, score.EndMs);
            var span = end - start;
            if (span <= 0) continue;
            var amount = profile.PhraseArch * Math.Clamp(1 + 0.2 * Gaussian(random), 0.6, 1.4) * Math.Min(1, span / ArchFullMs);
            for (var i = phrase.First; i <= phrase.Last && i < n - 1; i++)
            {
                var u = ((onsets[i].Ms + onsets[i + 1].Ms) / 2 - start) / span;
                var d = (u - ArchPeak) / m;
                k[i] += amount * (d * d - archMean);
            }
            if (phrase.Last + 1 < n && profile.PhraseEnd > 0)
            {
                var extra = Math.Min(profile.PhraseEnd * phrase.Strength, profile.PhraseEnd * EndCapMs / Math.Max(1, Gap(phrase.Last)));
                k[phrase.Last] += extra;
                if (phrase.Last - 1 >= phrase.First) k[phrase.Last - 1] += 0.35 * extra;
            }
        }

        // Downbeat stretch: a note that starts a bar (and lasts at most two beats) is slightly longer.
        if (profile.DownbeatStretch > 0)
            for (var i = 0; i < n - 1; i++)
                if (score.Meter.IsDownbeat(onsets[i].Tick) && Gap(i) <= 2 * BeatMs(i)) k[i] += profile.DownbeatStretch;

        // Final ritardando over the last 2–6 beats (half the last phrase, at most a quarter of the song).
        if (profile.FinalTempo < 1 && phrases.Count > 0)
        {
            var last = onsets[n - 1].Ms;
            var lastPhrase = phrases[^1];
            var beat = BeatMs(Math.Max(0, n - 2));
            var length = Math.Clamp(0.5 * (last - onsets[lastPhrase.First].Ms), 2 * beat, 6 * beat);
            length = Math.Min(length, 0.25 * (last - onsets[0].Ms));
            if (length > 0)
            {
                var wq = Math.Pow(profile.FinalTempo, RitardQ);
                for (var i = 0; i < n - 1; i++)
                {
                    var mid = (onsets[i].Ms + onsets[i + 1].Ms) / 2;
                    if (mid <= last - length) continue;
                    var x = (mid - (last - length)) / length;
                    var v = Math.Pow(1 + (wq - 1) * x, 1 / RitardQ);
                    k[i] += 1 / v - 1;
                }
            }
        }

        // Tempo wander: AR(1) over score time.
        if (profile.TempoWander > 0)
        {
            var d = profile.TempoWander * Gaussian(random);
            for (var i = 0; i < n - 1; i++)
            {
                k[i] += d;
                var rho = Math.Exp(-Gap(i) / WanderMs);
                d = rho * d + Math.Sqrt(1 - rho * rho) * profile.TempoWander * Gaussian(random);
            }
        }

        for (var i = 0; i < k.Length; i++) k[i] = Math.Clamp(k[i], -0.1, 0.6);
        return k;
    }

    internal static double Gaussian(Random random)
    {
        var u1 = 1.0 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
