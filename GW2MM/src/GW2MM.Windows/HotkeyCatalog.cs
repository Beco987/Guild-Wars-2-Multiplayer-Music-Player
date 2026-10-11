using GW2MM.Windows.Forms;

namespace GW2MM.Windows;

/// <summary>A hotkey-driven action: its id in Config.txt ("Key_&lt;Id&gt;"), what it does and its default keys.</summary>
internal sealed record HotkeyAction(string Id, string Label, string Default);

internal static class HotkeyCatalog
{
    private static IEnumerable<HotkeyAction> Windows() =>
        Enumerable.Range(1, 8).Select(n => new HotkeyAction($"Window{n}",
            n == 1 ? "Name the active Guild Wars 2 window Player 1" : $"Name it Player {n}", $"Shift+F{n}"));

    public static IReadOnlyList<HotkeyAction> For(AppRole role) => role == AppRole.Host
        ? new[]
        {
            new HotkeyAction("PlaySong", "Play the selected song for everyone", "F1"),
            new HotkeyAction("PauseResume", "Pause / resume this computer's playback", "F2"),
            new HotkeyAction("StopSong", "Stop everyone and reset octaves", "F3"),
            new HotkeyAction("Exit", "Reset octaves and exit", "F4"),
            new HotkeyAction("NextSong", "Skip to the next song of the playlist", "F5"),
        }.Concat(Windows()).Concat(new[]
        {
            new HotkeyAction("TempoUp", "Tempo +1% (no audible effect, as in the original)", "F10"),
            new HotkeyAction("TempoDown", "Tempo -1% (no audible effect, as in the original)", "F11"),
        }).ToList()
        : new[]
        {
            new HotkeyAction("RequestSong", "Ask the host to play the selected song", "F11"),
            new HotkeyAction("Exit", "Reset octaves and exit", "F4"),
        }.Concat(Windows()).ToList();
}
