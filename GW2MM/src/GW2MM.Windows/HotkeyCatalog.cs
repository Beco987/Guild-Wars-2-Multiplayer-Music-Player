using GW2MM.Core.Config;
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
            new HotkeyAction("TempoDown", "Tempo down", "F10"),
            new HotkeyAction("TempoUp", "Tempo up", "F11"),
        }).ToList();

    /// <summary>
    /// Earlier defaults. Config.txt keeps every binding once the Keybinds window is saved, so bindings saved before
    /// KeysVersion 2 that still equal a whole set of old defaults are treated as "default" and follow the new ones.
    /// A binding chosen on purpose (any set that differs, or anything saved since) is kept as it is.
    /// </summary>
    private static readonly (AppRole? Role, Dictionary<string, string> Keys)[] OldDefaults =
    {
        (AppRole.Client, new() { ["RequestSong"] = "F11", ["StopSong"] = "Ctrl+F3", ["NextSong"] = "Ctrl+F5" }),
        (null, new() { ["TempoUp"] = "F10", ["TempoDown"] = "F11" }),
    };

    /// <summary>The keys for an action: the stored binding, else its default.</summary>
    public static Hotkey KeysFor(AppRole role, AppSettings settings, HotkeyAction action) =>
        settings.KeysVersion < AppSettings.CurrentKeysVersion
            && OldDefaults.Any(old => (old.Role is null || old.Role == role) && old.Keys.ContainsKey(action.Id) && old.Keys.All(k => Stored(settings, k.Key, k.Value)))
            ? Hotkey.Parse(action.Default)
            : settings.HotkeyFor(action.Id, action.Default);

    private static bool Stored(AppSettings settings, string id, string keys) =>
        settings.Hotkeys.TryGetValue(id, out var stored) && Hotkey.TryParse(stored, out var h) && h.ToString() == Hotkey.Parse(keys).ToString();
}
