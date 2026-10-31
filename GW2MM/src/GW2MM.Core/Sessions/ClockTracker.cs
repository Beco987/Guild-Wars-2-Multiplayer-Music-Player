namespace GW2MM.Core.Sessions;

/// <summary>
/// One clock exchange between a client and the host: <see cref="T1"/> and <see cref="T4"/> on the client's clock
/// (request sent, reply received), <see cref="T2"/> and <see cref="T3"/> on the host's (request received, reply sent).
/// </summary>
public readonly record struct ClockSample(double T1, double T2, double T3, double T4)
{
    /// <summary>Client-to-host delay plus the offset (host clock minus client clock).</summary>
    public double Up => T2 - T1;
    /// <summary>Host-to-client delay minus the offset.</summary>
    public double Down => T4 - T3;
    /// <summary>Time on the network, both ways (the time the host held the request is left out).</summary>
    public double RoundTrip => T4 - T1 - (T3 - T2);
}

/// <summary>A client's clock as the host currently sees it.</summary>
/// <param name="Offset">The host's clock minus the client's (ms).</param>
/// <param name="Samples">Exchanges the estimate is based on.</param>
/// <param name="JitterMs">How much the round trip varies (median minus minimum): a busy or unsteady connection.</param>
/// <param name="SkewPpm">How fast the two clocks drift apart (parts per million; already allowed for in the offset).</param>
/// <param name="AgeMs">How long ago the newest exchange was.</param>
public readonly record struct ClockEstimate(double Offset, int Samples, double JitterMs, double SkewPpm, double AgeMs)
{
    /// <summary>The client's clock at host time <paramref name="hostTime"/>.</summary>
    public double ClientClockAt(double hostTime) => hostTime - Offset;
}

/// <summary>
/// Follows one client's clock from a steady stream of exchanges (one a second), so that a song can start without
/// sampling clocks first, and so that a busy connection does not throw the estimate off.
/// <list type="bullet">
/// <item>Each exchange has four times, so time the request waited inside either program does not count as network
/// delay.</item>
/// <item>The two directions are filtered separately: over the last <see cref="WindowMs"/>, the smallest client-to-host
/// and the smallest host-to-client delay. Queueing (an upload or a video call on one side, Wi-Fi bursts, a resent
/// packet) only ever adds delay, so each minimum comes from a moment that direction was clear, even if the other
/// direction was busy then. The offset is half the difference of the two minima. What is left is the fixed difference
/// between the two routes, which no exchange between two computers can see (usually a few ms at most).</item>
/// <item>Clocks drift apart (often 10-50 parts per million). The drift is measured over <see cref="HistoryMs"/> and
/// allowed for, so the estimate holds for the moment it is asked about, not for the middle of the window.</item>
/// </list>
/// Thread-safe.
/// </summary>
public sealed class ClockTracker
{
    /// <summary>The exchanges the offset is taken from.</summary>
    public const double WindowMs = 60_000;
    /// <summary>The exchanges the drift is measured over.</summary>
    public const double HistoryMs = 300_000;
    /// <summary>Drift is measured between offsets taken over periods this long.</summary>
    public const double BucketMs = 15_000;
    /// <summary>Exchanges needed before there is an estimate.</summary>
    public const int MinSamples = 5;
    /// <summary>With no exchange for this long, there is no estimate (the client's probes stopped getting through).</summary>
    public const double MaxAgeMs = 5_000;
    /// <summary>Exchanges slower than this (round trip) are ignored.</summary>
    public const double MaxRoundTripMs = 5_000;
    /// <summary>Drift beyond this is taken to be noise (500 parts per million).</summary>
    private const double MaxSkew = 0.0005;

    private readonly object _gate = new();
    private readonly List<ClockSample> _samples = new();

    public int Count { get { lock (_gate) return _samples.Count; } }

    public void Add(ClockSample sample)
    {
        var rt = sample.RoundTrip;
        if (double.IsNaN(rt) || rt < 0 || rt > MaxRoundTripMs || double.IsNaN(sample.Up) || double.IsNaN(sample.Down)) return;
        lock (_gate)
        {
            // Kept in host-time order; a late sample goes where it belongs.
            var i = _samples.Count;
            while (i > 0 && _samples[i - 1].T2 > sample.T2) i--;
            _samples.Insert(i, sample);
            var newest = _samples[^1].T2;
            var old = 0;
            while (old < _samples.Count && _samples[old].T2 < newest - HistoryMs) old++;
            if (old > 0) _samples.RemoveRange(0, old);
        }
    }

    /// <summary>The client's clock as of host time <paramref name="hostNow"/>; null when there are too few recent exchanges.</summary>
    public ClockEstimate? Estimate(double hostNow)
    {
        lock (_gate)
        {
            if (_samples.Count == 0 || hostNow - _samples[^1].T2 > MaxAgeMs) return null;
            var window = _samples.Where(s => s.T2 >= hostNow - WindowMs).ToList();
            if (window.Count < MinSamples) return null;
            var skew = Skew();
            double minUp = double.PositiveInfinity, minDown = double.PositiveInfinity;
            foreach (var s in window)
            {
                // Each exchange as if it had happened now: the offset has drifted by skew × (now − then) since.
                var drift = skew * (hostNow - s.T2);
                minUp = Math.Min(minUp, s.Up + drift);
                minDown = Math.Min(minDown, s.Down - drift);
            }
            var trips = window.Select(s => s.RoundTrip).OrderBy(t => t).ToList();
            var jitter = trips[trips.Count / 2] - trips[0];
            return new ClockEstimate((minUp - minDown) / 2, window.Count, jitter, skew * 1e6, hostNow - _samples[^1].T2);
        }
    }

    /// <summary>
    /// How fast the offset changes (ms per ms of host time): the median slope between offsets taken over
    /// <see cref="BucketMs"/> periods (each from that period's two minima). Zero until there are four such periods.
    /// Call under the lock.
    /// </summary>
    private double Skew()
    {
        var points = _samples.GroupBy(s => Math.Floor(s.T2 / BucketMs)).Where(g => g.Count() >= 3)
            .Select(g => (Time: g.Average(s => s.T2), Offset: (g.Min(s => s.Up) - g.Min(s => s.Down)) / 2)).ToList();
        if (points.Count < 4) return 0;
        var slopes = new List<double>();
        for (var i = 0; i < points.Count; i++)
            for (var j = i + 1; j < points.Count; j++)
                if (points[j].Time - points[i].Time >= BucketMs / 2)
                    slopes.Add((points[j].Offset - points[i].Offset) / (points[j].Time - points[i].Time));
        if (slopes.Count == 0) return 0;
        slopes.Sort();
        return Math.Clamp(slopes[slopes.Count / 2], -MaxSkew, MaxSkew);
    }
}
