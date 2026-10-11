namespace GW2MM.Core.Arrangement;

/// <summary>
/// Which MIDI tracks (1..8) each player slot (1..5) plays. Slot 1 is the host machine; slots 2..5 are the
/// connected clients in the order the host assigned them ("Ip1".."Ip5" in the original Port Mapping window).
/// </summary>
public sealed class PortMapping
{
    public const int Slots = 5;
    public const int Tracks = 8;
    private readonly bool[,] _map = new bool[Slots + 1, Tracks + 1];

    public bool this[int slot, int track]
    {
        get => _map[slot, track];
        set => _map[slot, track] = value;
    }

    public PortMapping Clone()
    {
        var c = new PortMapping();
        Array.Copy(_map, c._map, _map.Length);
        return c;
    }

    /// <summary>Parses "11,12,23" style lists (slot digit followed by track digit), as used by the golden tests.</summary>
    public static PortMapping Parse(string list)
    {
        var m = new PortMapping();
        foreach (var item in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
            m[item[0] - '0', int.Parse(item[1..], System.Globalization.CultureInfo.InvariantCulture)] = true;
        return m;
    }
}
