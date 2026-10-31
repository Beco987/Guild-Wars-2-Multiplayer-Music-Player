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
/// Stopwatch-based clock. A wait sleeps until shortly before the deadline and spins for the rest, giving
/// sub-millisecond accuracy (the original's A_TickCount-based checks were limited to ~10-16 ms steps).
/// <para>
/// On Windows the clock requests 1 ms timer resolution itself (timeBeginPeriod(1), held for the life of the
/// process); otherwise sleeps are rounded up to ~15.6 ms. It also measures how far sleeps overshoot and keeps that
/// much spin margin, so a coarse timer only costs CPU time, never punctuality.
/// </para>
/// </summary>
public sealed class PrecisionClock : IClock
{
    private const double MinGuardMs = 1.5;
    private const double MaxGuardMs = 20;
    private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;
    private readonly long _origin = Stopwatch.GetTimestamp();
    private double _guardMs = MinGuardMs;
    private readonly Func<TimeSpan, CancellationToken, bool> _sleep;

    public static readonly PrecisionClock Instance = new();

    public PrecisionClock() : this((t, ct) => ct.WaitHandle.WaitOne(t)) { }

    /// <summary>Tests: substitute the sleep primitive (returns true when cancelled).</summary>
    internal PrecisionClock(Func<TimeSpan, CancellationToken, bool> sleep) => _sleep = sleep;

    static PrecisionClock()
    {
        if (OperatingSystem.IsWindows())
        {
            try { _ = TimeBeginPeriod(1); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    public double NowMs => (Stopwatch.GetTimestamp() - _origin) * TicksToMs;

    public bool WaitUntil(double dueMs, CancellationToken ct)
    {
        while (true)
        {
            if (ct.IsCancellationRequested) return false;
            var remaining = dueMs - NowMs;
            if (remaining <= 0) return true;
            var guard = Volatile.Read(ref _guardMs);
            if (remaining > guard + 0.5)
            {
                var sleepMs = Math.Min(remaining - guard, 50);
                var wakeTarget = NowMs + sleepMs;
                if (_sleep(TimeSpan.FromMilliseconds(sleepMs), ct)) return false;
                Learn(NowMs - wakeTarget);
            }
            else
            {
                Thread.SpinWait(50);
            }
        }
    }

    // Grow the margin at once when a sleep overshoots; let it shrink slowly when sleeps are accurate.
    private void Learn(double overshootMs)
    {
        var guard = Volatile.Read(ref _guardMs);
        var next = overshootMs + 0.5 > guard ? overshootMs + 0.5 : guard * 0.98;
        Volatile.Write(ref _guardMs, Math.Clamp(next, MinGuardMs, MaxGuardMs));
    }
}
