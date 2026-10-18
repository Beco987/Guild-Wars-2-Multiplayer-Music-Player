namespace GW2MM.Core.Config;

/// <summary>The settings stored in Config.txt (host and client share the file format).</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Raw OctaveDelay text. The arranger compares it with the gap in MIDI ticks before splitting an octave swap
    /// into its own key press; the client also sleeps this many ms between the octave-reset presses after a stop.
    /// A missing or blank value is <see cref="DefaultOctaveDelay"/>.
    /// </summary>
    public string OctaveDelay { get; set; } = DefaultOctaveDelay;

    /// <summary>OctaveDelay when Config.txt has none (ms).</summary>
    public const string DefaultOctaveDelay = "100";
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
    /// <summary>Playlists tab: shuffle and repeat, and the playlist last shown (not in the original).</summary>
    public bool PlaylistShuffle { get; set; }
    public bool PlaylistRepeat { get; set; }
    public string LastPlaylist { get; set; } = "";

    /// <summary>Stores the Playlists tab's options.</summary>
    public void SavePlaylistOptions(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("PlaylistShuffle", PlaylistShuffle ? "1" : "0"), ("PlaylistRepeat", PlaylistRepeat ? "1" : "0"), ("LastPlaylist", LastPlaylist),
        });


    /// <summary>
    /// Offset in ms when playing alone (not in the original; Config.txt "Offset", 0 = off): the windows' starts are
    /// spread evenly over it so no two press at the same instant. Used by a client that is not connected and by a host
    /// with no players connected; never when players on other computers take part.
    /// </summary>
    public int OffsetMs { get; set; }

    /// <summary>Client: the name and server address last used to connect (offered again next time).</summary>
    public void SaveClientSettings(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[] { ("Name", Name), ("HostAddress", HostAddress) });

    /// <summary>The largest Offset accepted (ms).</summary>
    public const int MaxOffsetMs = 1000;

    /// <summary>Client only: the last host address entered, offered again on the next start (not in the original).</summary>
    public string HostAddress { get; set; } = "";

    /// <summary>Where MuseScore4.exe is, when the user had to point to it (Config.txt "MuseScore"; blank = look in the usual places).</summary>
    public string MuseScorePath { get; set; } = "";

    /// <summary>The dusk (dark) look instead of the light one (Config.txt "Theme,Dusk"); used from the next start.</summary>
    public bool DuskTheme { get; set; }

    public void SaveMuseScorePath(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[] { ("MuseScore", MuseScorePath) });

    public const string FileName = "Config.txt";

    public static AppSettings Load(string dataDir)
    {
        var c = LegacyConfig.Load(Path.Combine(dataDir, FileName));
        return new AppSettings
        {
            OctaveDelay = c.Get("OctaveDelay") is { } od && od.Trim().Length > 0 ? od : DefaultOctaveDelay,
            AlwaysOnTop = LegacyConfig.IsTrue(c.Get("AlwaysOnTop")),
            IsTransparent = LegacyConfig.IsTrue(c.Get("IsTransparent")),
            Name = c.Get("Name") ?? "",
            HostAddress = (c.Get("HostAddress") ?? "").Trim(),
            MuseScorePath = (c.Get("MuseScore") ?? "").Trim(),
            DuskTheme = string.Equals((c.Get("Theme") ?? "").Trim(), "Dusk", StringComparison.OrdinalIgnoreCase),
            OffsetMs = Ahk.AhkValue.Number(c.Get("Offset") ?? "") is { } offset && offset > 0 ? (int)Math.Min(offset, MaxOffsetMs) : 0,
            PlaylistShuffle = LegacyConfig.IsTrue(c.Get("PlaylistShuffle")),
            PlaylistRepeat = LegacyConfig.IsTrue(c.Get("PlaylistRepeat")),
            LastPlaylist = c.Get("LastPlaylist") ?? "",
            CheckForUpdates = c.Get("CheckForUpdates") is not { } u || LegacyConfig.IsTrue(u),
            OpenPortAutomatically = c.Get("OpenPort") is not { } o || LegacyConfig.IsTrue(o),
            Hotkeys = c.WithPrefix(HotkeyPrefix).ToDictionary(kv => kv.Key[HotkeyPrefix.Length..], kv => kv.Value, StringComparer.Ordinal),
        };
    }

    /// <summary>Settings window "Save": OctaveDelay, AlwaysOnTop, IsTransparent (and the added CheckForUpdates, OpenPort, Offset).</summary>
    public void SaveDisplaySettings(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("OctaveDelay", OctaveDelay), ("AlwaysOnTop", AlwaysOnTop ? "1" : "0"), ("IsTransparent", IsTransparent ? "1" : "0"),
            ("CheckForUpdates", CheckForUpdates ? "1" : "0"), ("OpenPort", OpenPortAutomatically ? "1" : "0"),
            ("Offset", OffsetMs.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("Theme", DuskTheme ? "Dusk" : "Light"),
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

    /// <summary>The pause between octave-reset rounds on this computer (see <see cref="OctaveResetDelay"/>).</summary>
    public int OctaveDelayMs => OctaveResetDelay(OctaveDelay);

    /// <summary>Pause between octave-reset presses when OctaveDelay is blank (the original host's fixed Sleep, 150).</summary>
    public const int DefaultOctaveResetDelayMs = 150;

    /// <summary>
    /// The pause before each round of an octave reset (9, 9, 0): OctaveDelay ms, so the game has time to apply each
    /// octave change. A blank or zero OctaveDelay uses 150 ms. The original client pressed all three at once in that
    /// case, and the original host always used 150 ms whatever OctaveDelay said.
    /// </summary>
    public static int OctaveResetDelay(string octaveDelay) =>
        Ahk.AhkValue.TryNumber(octaveDelay, out var v) && v > 0 ? (int)Math.Min(v, 60000) : DefaultOctaveResetDelayMs;
}
