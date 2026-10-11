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
    /// <summary>Client only: the user name announced to the host.</summary>
    public string Name { get; set; } = "";

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
        };
    }

    /// <summary>Settings window "Save": OctaveDelay, AlwaysOnTop, IsTransparent.</summary>
    public void SaveDisplaySettings(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("OctaveDelay", OctaveDelay), ("AlwaysOnTop", AlwaysOnTop ? "1" : "0"), ("IsTransparent", IsTransparent ? "1" : "0"),
        });

    /// <summary>Client start-up: the original rewrote all four values after asking for the user name.</summary>
    public void SaveWithName(string dataDir) =>
        LegacyConfig.Save(Path.Combine(dataDir, FileName), new[]
        {
            ("OctaveDelay", OctaveDelay), ("AlwaysOnTop", AlwaysOnTop ? "1" : "0"), ("IsTransparent", IsTransparent ? "1" : "0"), ("Name", Name),
        });

    /// <summary>The client's sleep between octave-reset presses (the original's <c>Sleep, %Delay%</c>).</summary>
    public int OctaveDelayMs => Ahk.AhkValue.TryNumber(OctaveDelay, out var v) && v > 0 ? (int)Math.Min(v, 60000) : 0;
}
