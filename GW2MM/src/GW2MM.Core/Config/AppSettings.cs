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
    /// Client only: ask for the user name and host address every launch (the original always did). When off, the
    /// saved Name and HostAddress are used, and only a missing host address is still asked for.
    /// </summary>
    public bool AskOnLaunch { get; set; } = true;

    /// <summary>
    /// Client only: how solo play (not connected to a host) is humanized (not in the original; Config.txt "Humanize",
    /// 0 = off, 1 = subtle, 2 = natural, 3 = loose, 4 = phrased, 5 = expressive, 6 = rubato). Host playback is never humanized.
    /// </summary>
    public Humanizing.HumanizeLevel Humanize { get; set; } = Humanizing.HumanizeLevel.Off;

    /// <summary>Client Settings: the launch prompts option, the name and host address it uses, and solo humanizing.</summary>
    public void SaveClientSettings(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("AskOnLaunch", AskOnLaunch ? "1" : "0"), ("Name", Name), ("HostAddress", HostAddress),
            ("Humanize", ((int)Humanize).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        });

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
            AskOnLaunch = c.Get("AskOnLaunch") is not { } ask || LegacyConfig.IsTrue(ask),
            Humanize = Ahk.AhkValue.Number(c.Get("Humanize") ?? "") is { } h && h == Math.Floor(h) && Enum.IsDefined((Humanizing.HumanizeLevel)(int)h)
                ? (Humanizing.HumanizeLevel)(int)h : Humanizing.HumanizeLevel.Off,
            PlaylistShuffle = LegacyConfig.IsTrue(c.Get("PlaylistShuffle")),
            PlaylistRepeat = LegacyConfig.IsTrue(c.Get("PlaylistRepeat")),
            LastPlaylist = c.Get("LastPlaylist") ?? "",
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
