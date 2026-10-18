using System.Diagnostics;

namespace GW2MM.Windows.Interop;

/// <summary>Finds the Guild Wars 2 window and brings it to the front (for the Sync input-delay test).</summary>
internal static class GameWindow
{
    /// <summary>
    /// The game window: the one renamed "Player1" if there is one (the window this computer's first part plays in),
    /// otherwise any Guild Wars 2 window (window class ArenaNet_*, or the Gw2-64.exe / Gw2.exe process).
    /// Zero when the game is not open.
    /// </summary>
    public static IntPtr Find()
    {
        var player1 = new WindowFinder().Find("Player1");
        if (player1 != IntPtr.Zero && IsGame(player1)) return player1;
        var found = IntPtr.Zero;
        NativeMethods.EnumWindows((h, _) =>
        {
            if (!NativeMethods.IsWindowVisible(h) || !IsGame(h)) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsGame(IntPtr hWnd)
    {
        if (NativeMethods.GetClassName(hWnd).StartsWith("ArenaNet_", StringComparison.Ordinal)) return true;
        NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            return name.Equals("Gw2-64", StringComparison.OrdinalIgnoreCase) || name.Equals("Gw2", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Makes a window the foreground window even when another program (the game) has the focus. Windows only lets the
    /// foreground process hand focus to another window; sharing the foreground window's input state makes this
    /// process count as foreground for the call.
    /// </summary>
    public static void BringToFront(IntPtr hWnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var thisThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread && NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(hWnd);
            NativeMethods.SetForegroundWindow(hWnd);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    /// <summary>Restores and activates the window; true once it is the foreground window (waits up to 2 s).</summary>
    public static bool Focus(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return false;
        if (NativeMethods.GetForegroundWindow() == hWnd) return true;
        if (NativeMethods.IsIconic(hWnd)) NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        BringToFront(hWnd);
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2000)
        {
            if (NativeMethods.GetForegroundWindow() == hWnd) return true;
            Thread.Sleep(20);
        }
        return false;
    }
}
