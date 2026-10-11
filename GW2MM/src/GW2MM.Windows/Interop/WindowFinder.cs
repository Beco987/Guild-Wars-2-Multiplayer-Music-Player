namespace GW2MM.Windows.Interop;

/// <summary>
/// Finds a visible top-level window whose title starts with a given text, case-sensitively, topmost first - the
/// matching rules AutoHotkey's WinExist used by default. Handles are cached and re-validated on every use.
/// </summary>
internal sealed class WindowFinder
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IntPtr> _cache = new();

    public IntPtr Find(string titlePrefix)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(titlePrefix, out var cached) && Matches(cached, titlePrefix)) return cached;
            var found = IntPtr.Zero;
            NativeMethods.EnumWindows((h, _) =>
            {
                if (!Matches(h, titlePrefix)) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            _cache[titlePrefix] = found;
            return found;
        }
    }

    public static bool Matches(IntPtr hWnd, string titlePrefix) =>
        hWnd != IntPtr.Zero && NativeMethods.IsWindow(hWnd) && NativeMethods.IsWindowVisible(hWnd)
        && NativeMethods.GetWindowTitle(hWnd).StartsWith(titlePrefix, StringComparison.Ordinal);
}
