namespace GW2MM.Core.Sessions;

/// <summary>Ports of <c>RemoveOutliers</c> / <c>Percentile</c> from the original ConnectButtons.ahk (used by the Ping button).</summary>
public static class Statistics
{
    /// <summary>Removes values outside [Q1 - k*IQR, Q3 + k*IQR]; lists with fewer than 4 values are returned unchanged.</summary>
    public static List<double> RemoveOutliers(IReadOnlyList<double> values, double multiplier = 1.5)
    {
        if (values.Count < 4) return values.ToList();
        var sorted = values.OrderBy(v => v).ToList();
        var q1 = Percentile(sorted, 25);
        var q3 = Percentile(sorted, 75);
        var iqr = q3 - q1;
        var lower = q1 - multiplier * iqr;
        var upper = q3 + multiplier * iqr;
        return values.Where(v => v >= lower && v <= upper).ToList();
    }

    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        var n = sorted.Count;
        var pos = p / 100 * (n + 1);
        if (pos <= 1) return sorted[0];
        if (pos >= n) return sorted[n - 1];
        var lower = (int)Math.Floor(pos);
        var upper = (int)Math.Ceiling(pos);
        if (lower == upper) return sorted[lower - 1];
        var fraction = pos - lower;
        return sorted[lower - 1] + fraction * (sorted[upper - 1] - sorted[lower - 1]);
    }

    /// <summary>The Ping button's result: Floor(mean of the outlier-filtered one-way times), 0 when there are none.</summary>
    public static long PingEstimate(IReadOnlyList<double> oneWayTimes)
    {
        var cleaned = RemoveOutliers(oneWayTimes);
        if (cleaned.Count == 0) return 0;
        return (long)Math.Floor(cleaned.Sum() / cleaned.Count);
    }
}
