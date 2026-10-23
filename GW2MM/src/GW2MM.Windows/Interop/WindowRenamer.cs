namespace GW2MM.Windows.Interop;

/// <summary>
/// F5-F9 / Shift+F6-F8: waits until a "Guild Wars 2" window is active, then renames it to "Player&lt;n&gt;"
/// (the original WinWaitActive + WinSetTitle; there is no time limit, as before).
/// </summary>
internal static class WindowRenamer
{
    public const string GameTitle = "Guild Wars 2";

    public static async Task RenameWhenActiveAsync(string newTitle, CancellationToken ct)
    {
        while (true)
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero && NativeMethods.GetWindowTitle(fg).StartsWith(GameTitle, StringComparison.Ordinal))
            {
                NativeMethods.SendMessageTimeoutW(fg, NativeMethods.WM_SETTEXT, IntPtr.Zero, newTitle, NativeMethods.SMTO_ABORTIFHUNG, 2000, out _);
                return;
            }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }
}
