namespace GW2MM.Core.Sessions;

/// <summary>
/// Estimates each client's clock at the host's moment T1 from clock probes ("Tick_Count"). A reply's clock reading is
/// taken to be from halfway through its round trip, so the estimate is <c>tick - ((sent + received) / 2 - T1)</c>.
/// With several probes, the one with the shortest round trip is kept: its midpoint guess can be off by at most half
/// that round trip, so a probe delayed by a lag spike cannot skew the result. With one probe sent at T1 this is
/// exactly the original <c>tick - RTT/2</c>.
/// </summary>
public sealed class ClockSampler
{
    private readonly Dictionary<int, (int Slot, double Sent)> _probes = new();
    private readonly double[] _bestRoundTrip;

    public ClockSampler(int slots)
    {
        Ticks = new double?[slots + 1];
        _bestRoundTrip = Enumerable.Repeat(double.PositiveInfinity, slots + 1).ToArray();
    }

    /// <summary>The host's clock when the song's clocks are sampled.</summary>
    public double T1 { get; set; }

    /// <summary>Each slot's clock at host time T1 (null until a reply arrives). Slot 1 is the host itself.</summary>
    public double?[] Ticks { get; }

    /// <summary>The best round trip seen for a slot (infinity until a reply arrives).</summary>
    public double RoundTrip(int slot) => _bestRoundTrip[slot];

    public void Sent(int slot, int id, double sent) => _probes[id] = (slot, sent);

    public void Reply(int slot, int id, double tick, double received)
    {
        if (slot <= 0 || slot >= Ticks.Length || !_probes.TryGetValue(id, out var probe) || probe.Slot != slot) return;
        var roundTrip = received - probe.Sent;
        if (roundTrip < 0 || roundTrip >= _bestRoundTrip[slot]) return;
        _bestRoundTrip[slot] = roundTrip;
        Ticks[slot] = tick - ((probe.Sent + received) / 2 - T1);
    }
}
