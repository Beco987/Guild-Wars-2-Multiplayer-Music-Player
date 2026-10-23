using System.Net;
using GW2MM.Core.Net;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>The continuous clock estimate (ClockTracker) on simulated connections, and the UDP exchanges over loopback.</summary>
public class ClockTrackerTests
{
    private const double Offset = 1_234_567.25; // host clock minus client clock

    /// <summary>
    /// One exchange a second starting at host time <paramref name="start"/>: <paramref name="up"/> / <paramref name="down"/>
    /// give each direction's delay for exchange i; the host answers in 0.2 ms. <paramref name="skew"/> makes the offset
    /// drift (ms per ms of host time).
    /// </summary>
    private static List<ClockSample> Simulate(int count, Func<int, double> up, Func<int, double> down, double skew = 0, double start = 10_000)
    {
        var samples = new List<ClockSample>();
        for (var i = 0; i < count; i++)
        {
            var hostSend = start + i * 1000.0;                 // host time the client sends at (roughly)
            var offset = Offset + skew * (hostSend - start);
            var t1 = hostSend - offset;                        // client clock
            var t2 = hostSend + up(i);
            var t3 = t2 + 0.2;
            var t4 = t3 + down(i) - (offset + skew * (t3 - hostSend));
            samples.Add(new ClockSample(t1, t2, t3, t4));
        }
        return samples;
    }

    private static ClockEstimate Track(IEnumerable<ClockSample> samples, double now)
    {
        var tracker = new ClockTracker();
        foreach (var s in samples) tracker.Add(s);
        return tracker.Estimate(now) ?? throw new Xunit.Sdk.XunitException("no estimate");
    }

    /// <summary>The previous method: of 8 exchanges, the one with the shortest round trip, read at its midpoint.</summary>
    private static double BestOfEight(List<ClockSample> samples)
    {
        var best = samples.TakeLast(8).MinBy(s => s.RoundTrip);
        return ((best.T2 - best.T1) - (best.T4 - best.T3)) / 2;
    }

    private static readonly Random Rng = new(7);
    private static double Small() => Rng.NextDouble() * 0.5;

    [Fact]
    public void AQuietConnectionGivesTheOffsetToWithinAFractionOfAMillisecond()
    {
        var samples = Simulate(60, _ => 20 + Small(), _ => 20 + Small());
        var e = Track(samples, samples[^1].T2 + 500);
        Assert.InRange(e.Offset - Offset, -0.5, 0.5);
        Assert.Equal(60, e.Samples);
        Assert.InRange(e.JitterMs, 0, 1);
    }

    [Fact]
    public void ABusyUploadOnOneSideNoLongerShiftsThePlayer()
    {
        // The client's upload queues (someone streaming): its packets to the host wait 30-150 ms most of the time.
        var rng = new Random(1);
        var samples = Simulate(60, _ => 20 + (rng.NextDouble() < 0.8 ? 30 + rng.NextDouble() * 120 : Small()), _ => 20 + Small());
        var e = Track(samples, samples[^1].T2 + 500);
        Assert.InRange(e.Offset - Offset, -1, 1);
        // The old method lands tens of ms off whenever all 8 of its exchanges were queued.
        var worst = Enumerable.Range(8, 52).Max(n => Math.Abs(BestOfEight(samples.Take(n).ToList()) - Offset));
        Assert.True(worst > 10, $"best-of-8 was within {worst:0.0} ms");
        Assert.True(e.JitterMs > UnsteadyThreshold, "a queued connection reads as unsteady");
    }

    private const double UnsteadyThreshold = HostSession.UnsteadyJitterMs;

    [Fact]
    public void BothDirectionsBusyAtDifferentMomentsStillWorks()
    {
        var rng = new Random(2);
        var samples = Simulate(60, i => 25 + (i % 3 == 0 ? Small() : 40 + rng.NextDouble() * 80), i => 25 + (i % 4 == 1 ? Small() : 20 + rng.NextDouble() * 60));
        var e = Track(samples, samples[^1].T2 + 200);
        Assert.InRange(e.Offset - Offset, -1, 1);
    }

    [Fact]
    public void ClockDriftIsMeasuredAndAllowedFor()
    {
        // 100 parts per million: the offset moves 6 ms a minute.
        const double skew = 100e-6;
        var samples = Simulate(300, _ => 20 + Small(), _ => 20 + Small(), skew);
        var now = samples[^1].T2 + 500;
        var e = Track(samples, now);
        var truth = Offset + skew * (now - 10_000);
        Assert.InRange(e.Offset - truth, -0.5, 0.5);
        Assert.InRange(e.SkewPpm, 90, 110);
    }

    [Fact]
    public void ThereIsNoEstimateWithTooFewOrNoRecentExchanges()
    {
        var samples = Simulate(4, _ => 20, _ => 20);
        var tracker = new ClockTracker();
        foreach (var s in samples) tracker.Add(s);
        Assert.Null(tracker.Estimate(samples[^1].T2));                         // only 4
        tracker.Add(Simulate(5, _ => 20, _ => 20)[4]);
        Assert.NotNull(tracker.Estimate(samples[^1].T2 + 1000));
        Assert.Null(tracker.Estimate(samples[^1].T2 + 1000 + ClockTracker.MaxAgeMs + 1)); // the exchanges stopped
    }

    [Fact]
    public void ImpossibleExchangesAreIgnored()
    {
        var tracker = new ClockTracker();
        tracker.Add(new ClockSample(100, 50, 60, 90));    // negative round trip
        tracker.Add(new ClockSample(0, 0, 0, double.NaN));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void PacketsRoundTrip()
    {
        var buffer = new byte[256];
        var samples = new List<ClockSample> { new(1, 2, 3, 4), new(5.5, 6.25, 7, 8.125) };
        var n = ClockPackets.WriteRequest(buffer, 0xFEEDBEEFCAFE1234, 42, samples);
        Assert.True(ClockPackets.TryReadRequest(buffer.AsSpan(0, n), out var token, out var seq, out var read));
        Assert.Equal((0xFEEDBEEFCAFE1234UL, 42u), (token, seq));
        Assert.Equal(samples, read);
        ClockPackets.WriteReply(buffer, 9, 123.5, 124.75);
        Assert.True(ClockPackets.TryReadReply(buffer.AsSpan(0, ClockPackets.ReplyLength), out var rs, out var t2, out var t3));
        Assert.Equal((9u, 123.5, 124.75), (rs, t2, t3));
        Assert.False(ClockPackets.TryReadReply(buffer.AsSpan(0, 10), out _, out _, out _));
        Assert.False(ClockPackets.TryReadRequest("nope"u8, out _, out _, out _));
    }

    [Fact]
    public async Task TheUdpExchangesFollowTheClientsClockWithinAMillisecond()
    {
        var hostClock = new OffsetClock(0);
        var clientClock = new OffsetClock(-1_000_000);                  // the client's clock is 1 000 000 ms behind
        using var server = ClockSyncServer.TryStart(0, hostClock) ?? throw new Xunit.Sdk.XunitException("no UDP port");
        var tracker = server.Register(77);
        using var client = new ClockSyncClient(new IPEndPoint(IPAddress.Loopback, server.Port), 77, clientClock);
        client.Start();
        await Wait.Until(() => tracker.Estimate(hostClock.NowMs) is not null, timeoutMs: 5000, what: "estimate");
        var e = tracker.Estimate(hostClock.NowMs)!.Value;
        Assert.InRange(e.Offset - 1_000_000, -1, 1);
        Assert.True(e.Samples >= ClockTracker.MinSamples);

        // A wrong token gets no answer and adds nothing.
        using var stranger = new ClockSyncClient(new IPEndPoint(IPAddress.Loopback, server.Port), 78, clientClock);
        stranger.Start();
        await Task.Delay(300);
        Assert.Equal(0, stranger.Replies);
    }
}
