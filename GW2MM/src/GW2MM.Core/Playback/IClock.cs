using System.Diagnostics;

namespace GW2MM.Core.Playback;

/// <summary>A monotonic millisecond clock (the original used A_TickCount) that can also wait precisely.</summary>
public interface IClock
{
    double NowMs { get; }

    /// <summary>Blocks the calling thread until <see cref="NowMs"/> &gt;= <paramref name="dueMs"/>; false if cancelled first.</summary>
    bool WaitUntil(double dueMs, CancellationToken ct);
}

/// <summary>
/// Stopwatch-based clock. Waits sleep until about 1.5 ms before the deadline and spin for the rest, giving
/// sub-millisecond accuracy (the original's A_TickCount-based checks were limited to ~10-16 ms steps).
/// </summary>
public sealed class PrecisionClock : IClock
{
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    private readonly long _origin = Stopwatch.GetTimestamp();

    public static readonly PrecisionClock Instance = new();

    public double NowMs => (Stopwatch.GetTimestamp() - _origin) * TicksToMs;

    public bool WaitUntil(double dueMs, CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested) return false;
            var remaining = dueMs - NowMs;
            if (remaining <= 0) return true;
            if (remaining > 2.0)
            {
                if (ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(remaining - 1.5, 50)))) return false;
            }
            else
            {
                Thread.SpinWait(50);
            }
        }
    }
}
