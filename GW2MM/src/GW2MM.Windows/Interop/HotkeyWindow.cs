namespace GW2MM.Windows.Interop;

/// <summary>System-wide hotkeys (RegisterHotKey), raised on the UI thread.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyWindow() => CreateHandle(new CreateParams());

    /// <summary>Registers a hotkey; returns false if another program already owns it.</summary>
    public bool Register(GW2MM.Core.Config.Hotkey hotkey, Action action)
    {
        if (hotkey.IsNone) return true;
        var id = _nextId++;
        var mods = NativeMethods.MOD_NOREPEAT | (uint)hotkey.Modifiers;
        if (!NativeMethods.RegisterHotKey(Handle, id, mods, hotkey.VirtualKey)) return false;
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) NativeMethods.UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action)) action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
