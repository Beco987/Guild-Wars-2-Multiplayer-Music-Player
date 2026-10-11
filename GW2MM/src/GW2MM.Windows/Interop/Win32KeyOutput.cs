using GW2MM.Core.Arrangement;
using GW2MM.Core.Playback;

namespace GW2MM.Windows.Interop;

/// <summary>
/// Posts WM_KEYDOWN/WM_KEYUP for an instrument key directly to the "Player&lt;n&gt;" game window, as the original
/// PlayNotes did (the game does not need focus). Windows that do not exist are skipped silently.
/// </summary>
internal sealed class Win32KeyOutput : IKeyOutput
{
    private readonly WindowFinder _finder = new();

    public void Press(int window, string key)
    {
        var hwnd = _finder.Find(KeyMap.WindowTitle(window));
        if (hwnd == IntPtr.Zero) return;
        var vk = NativeMethods.VirtualKey(key);
        if (vk == 0) return;
        var sc = NativeMethods.MapVirtualKeyW(vk, NativeMethods.MAPVK_VK_TO_VSC);
        var down = (IntPtr)(1 | (sc << 16));
        var up = (IntPtr)unchecked((int)(1u | (sc << 16) | (1u << 30) | (1u << 31)));
        NativeMethods.PostMessageW(hwnd, NativeMethods.WM_KEYDOWN, (IntPtr)vk, down);
        NativeMethods.PostMessageW(hwnd, NativeMethods.WM_KEYUP, (IntPtr)vk, up);
    }
}
