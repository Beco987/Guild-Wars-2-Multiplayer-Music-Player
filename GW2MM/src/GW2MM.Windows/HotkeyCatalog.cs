using GW2MM.Windows.Forms;

namespace GW2MM.Windows;

/// <summary>A hotkey-driven action: its id in Config.txt ("Key_&lt;Id&gt;"), what it does and its default keys.</summary>
internal sealed record HotkeyAction(string Id, string Label, string Default);

internal static class HotkeyCatalog
{
    private static IEnumerable<HotkeyAction> Windows() =>
        Enumerable.Range(1, 8).Select(n => new HotkeyAction($"Window{n}", $"Name window Player{n}", $"Shift+F{n}"));

    // Host and client have the same keys; the client's Play action keeps its own id ("RequestSong") in Config.txt.
    public static IReadOnlyList<HotkeyAction> For(AppRole role) => new[]
        {
            new HotkeyAction(role == AppRole.Host ? "PlaySong" : "RequestSong", "Play", "F1"),
            new HotkeyAction("PauseResume", "Pause / resume", "F2"),
            new HotkeyAction("StopSong", "Stop", "F3"),
            new HotkeyAction("Exit", "Exit", "F4"),
            new HotkeyAction("NextSong", "Next song", "F5"),
        }.Concat(Windows()).Concat(new[]
        {
            new HotkeyAction("TempoUp", "Tempo up", "F10"),
            new HotkeyAction("TempoDown", "Tempo down", "F11"),
        }).ToList();

    /// <summary>
    /// The client's earlier defaults. Config.txt keeps every binding once the Keybinds window is saved, so a stored
    /// binding equal to its old default is treated as "default" and follows the new one.
    /// </summary>
    private static readonly Dictionary<string, string> OldClientDefaults = new()
    {
        ["RequestSong"] = "F11", ["StopSong"] = "Ctrl+F3", ["NextSong"] = "Ctrl+F5",
    };

    /// <summary>The keys for an action: the stored binding, else its default.</summary>
    public static GW2MM.Core.Config.Hotkey KeysFor(AppRole role, GW2MM.Core.Config.AppSettings settings, HotkeyAction action) =>
        role != AppRole.Host && settings.Hotkeys.TryGetValue(action.Id, out var stored)
            && OldClientDefaults.TryGetValue(action.Id, out var old)
            && GW2MM.Core.Config.Hotkey.TryParse(stored, out var keys) && keys.ToString() == GW2MM.Core.Config.Hotkey.Parse(old).ToString()
            ? GW2MM.Core.Config.Hotkey.Parse(action.Default)
            : settings.HotkeyFor(action.Id, action.Default);
}
