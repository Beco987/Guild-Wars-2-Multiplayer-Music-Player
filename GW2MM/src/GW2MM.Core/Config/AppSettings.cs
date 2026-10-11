namespace GW2MM.Core.Config;

/// <summary>The settings stored in Config.txt (host and client share the file format).</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Raw OctaveDelay text. The arranger compares it with the gap in MIDI ticks before splitting an octave swap
    /// into its own key press; the client also sleeps this many ms between the octave-reset presses after a stop.
    /// A missing value behaves like the original's blank value (every swap is split, no sleep).
    /// </summary>
    public string OctaveDelay { get; set; } = "";
    public bool AlwaysOnTop { get; set; }
    public bool IsTransparent { get; set; }
    /// <summary>Look for a newer GitHub release on launch (not in the original; on unless Config.txt says 0).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Host: open the port on the router with UPnP (not in the original; on unless Config.txt says 0).</summary>
    public bool OpenPortAutomatically { get; set; } = true;
    /// <summary>Hotkey overrides from Config.txt ("Key_&lt;action&gt;,Shift+1"), by action id.</summary>
    public Dictionary<string, string> Hotkeys { get; private set; } = new(StringComparer.Ordinal);
    private const string HotkeyPrefix = "Key_";

    /// <summary>The hotkey for an action: the stored one if it is valid, otherwise <paramref name="defaultText"/>.</summary>
    public Hotkey HotkeyFor(string actionId, string defaultText) =>
        Hotkeys.TryGetValue(actionId, out var text) && Hotkey.TryParse(text, out var h) ? h : Hotkey.Parse(defaultText);

    /// <summary>Stores the hotkeys (one "Key_&lt;action&gt;,&lt;keys&gt;" line each) and keeps them in memory.</summary>
    public void SaveHotkeys(string dataDir, IReadOnlyDictionary<string, Hotkey> bindings)
    {
        LegacyConfig.Save(Path.Combine(dataDir, FileName), bindings.Select(b => (HotkeyPrefix + b.Key, b.Value.ToString())).ToList());
        foreach (var (id, h) in bindings) Hotkeys[id] = h.ToString();
    }

    /// <summary>Client only: the user name announced to the host.</summary>
    public string Name { get; set; } = "";
    /// <summary>Client only: the last host address entered, offered again on the next start (not in the original).</summary>
    public string HostAddress { get; set; } = "";

    public const string FileName = "Config.txt";

    public static AppSettings Load(string dataDir)
    {
        var c = LegacyConfig.Load(Path.Combine(dataDir, FileName));
        return new AppSettings
        {
            OctaveDelay = c.Get("OctaveDelay") ?? "",
            AlwaysOnTop = LegacyConfig.IsTrue(c.Get("AlwaysOnTop")),
            IsTransparent = LegacyConfig.IsTrue(c.Get("IsTransparent")),
            Name = c.Get("Name") ?? "",
            HostAddress = (c.Get("HostAddress") ?? "").Trim(),
            CheckForUpdates = c.Get("CheckForUpdates") is not { } u || LegacyConfig.IsTrue(u),
            OpenPortAutomatically = c.Get("OpenPort") is not { } o || LegacyConfig.IsTrue(o),
            Hotkeys = c.WithPrefix(HotkeyPrefix).ToDictionary(kv => kv.Key[HotkeyPrefix.Length..], kv => kv.Value, StringComparer.Ordinal),
        };
    }

    /// <summary>Settings window "Save": OctaveDelay, AlwaysOnTop, IsTransparent (and the added CheckForUpdates, OpenPort).</summary>
    public void SaveDisplaySettings(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("OctaveDelay", OctaveDelay), ("AlwaysOnTop", AlwaysOnTop ? "1" : "0"), ("IsTransparent", IsTransparent ? "1" : "0"),
            ("CheckForUpdates", CheckForUpdates ? "1" : "0"), ("OpenPort", OpenPortAutomatically ? "1" : "0"),
        });

    /// <summary>Client start-up: the original rewrote all four values after asking for the user name.</summary>
    public void SaveWithName(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("OctaveDelay", OctaveDelay), ("AlwaysOnTop", AlwaysOnTop ? "1" : "0"), ("IsTransparent", IsTransparent ? "1" : "0"), ("Name", Name),
        });

    /// <summary>Client start-up: remembers the host address that was just entered.</summary>
    public void SaveHostAddress(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[] { ("HostAddress", HostAddress) });

    /// <summary>The client's sleep between octave-reset presses (the original's <c>Sleep, %Delay%</c>).</summary>
    public int OctaveDelayMs => Ahk.AhkValue.TryNumber(OctaveDelay, out var v) && v > 0 ? (int)Math.Min(v, 60000) : 0;
}
