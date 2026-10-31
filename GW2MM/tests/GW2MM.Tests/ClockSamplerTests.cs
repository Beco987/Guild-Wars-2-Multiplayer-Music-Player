using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

public sealed class ClockSamplerTests
{
    [Fact]
    public void OneProbeAtT1IsTheOriginalFormula()
    {
        var c = new ClockSampler(5) { T1 = 1000 };
        c.Sent(2, 1, 1000);
        c.Reply(2, 1, tick: 50_030, received: 1040);
        Assert.Equal(50_030 - 40.0 / 2, c.Ticks[2]); // tick - RTT/2
    }

    /// <summary>
    /// The client's clock is 49 000 ms ahead of the host's. The first probe's reply was held up by a lag spike on the
    /// way back (one-way delays 10 ms out, 110 ms back), which alone would misplace that player by 50 ms; the later,
    /// clean probes give the exact answer.
    /// </summary>
    [Fact]
    public void TheShortestRoundTripWins()
    {
        const double offset = 49_000;
        var c = new ClockSampler(5) { T1 = 1000 };
        void Probe(int id, double sent, double outMs, double backMs)
        {
            c.Sent(2, id, sent);
            c.Reply(2, id, tick: sent + outMs + offset, received: sent + outMs + backMs);
        }
        Probe(1, 1000, 10, 110);
        Assert.Equal(1000 + offset - 50, c.Ticks[2]); // the spike skews the single-probe estimate by 50 ms
        Probe(2, 1050, 10, 10);
        Probe(3, 1100, 12, 30);
        Assert.Equal(1000 + offset, c.Ticks[2]);
        Assert.Equal(20, c.RoundTrip(2));
    }

    [Fact]
    public void IgnoresUnknownProbesAndOtherSlots()
    {
        var c = new ClockSampler(5) { T1 = 0 };
        c.Sent(2, 7, 0);
        c.Reply(3, 7, 100, 10);   // wrong slot
        c.Reply(2, 8, 100, 10);   // unknown probe
        c.Reply(0, 7, 100, 10);   // no slot
        Assert.Null(c.Ticks[2]);
        Assert.Null(c.Ticks[3]);
    }
}
