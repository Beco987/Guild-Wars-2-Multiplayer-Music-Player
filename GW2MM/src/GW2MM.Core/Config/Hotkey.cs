using System.Globalization;

namespace GW2MM.Core.Config;

/// <summary>Modifier flags with the same values as RegisterHotKey's MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>
/// A global hotkey: modifiers plus a Windows virtual-key code. Stored in Config.txt as text such as "Shift+1",
/// "Ctrl+Alt+F5" or "None" (no comma is ever produced, as Config.txt is comma-separated).
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    public static readonly Hotkey None = default;
    public bool IsNone => VirtualKey == 0;

    private static readonly Dictionary<uint, string> Names = BuildNames();
    private static readonly Dictionary<string, uint> Codes =
        Names.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<uint, string> BuildNames()
    {
        var n = new Dictionary<uint, string>();
        for (uint c = 0x30; c <= 0x39; c++) n[c] = ((char)c).ToString();
        for (uint c = 0x41; c <= 0x5A; c++) n[c] = ((char)c).ToString();
        for (uint i = 1; i <= 24; i++) n[0x6F + i] = "F" + i.ToString(CultureInfo.InvariantCulture);
        for (uint i = 0; i <= 9; i++) n[0x60 + i] = "NumPad" + i.ToString(CultureInfo.InvariantCulture);
        foreach (var (code, name) in new (uint, string)[]
        {
            (0x20, "Space"), (0x2D, "Insert"), (0x2E, "Delete"), (0x24, "Home"), (0x23, "End"), (0x21, "PageUp"),
            (0x22, "PageDown"), (0x25, "Left"), (0x26, "Up"), (0x27, "Right"), (0x28, "Down"), (0x13, "Pause"),
            (0x91, "ScrollLock"), (0x6A, "NumPadMultiply"), (0x6B, "NumPadAdd"), (0x6D, "NumPadSubtract"),
            (0x6E, "NumPadDecimal"), (0x6F, "NumPadDivide"), (0xC0, "Backtick"), (0xBD, "Minus"), (0xBB, "Equals"),
            (0xDB, "LeftBracket"), (0xDD, "RightBracket"), (0xDC, "Backslash"), (0xBA, "Semicolon"), (0xDE, "Quote"),
            (0xBC, "Comma"), (0xBE, "Period"), (0xBF, "Slash"),
        })
            n[code] = name;
        return n;
    }

    /// <summary>Whether a key code can be used (modifier keys alone cannot).</summary>
    public static bool IsBindable(uint virtualKey) => Names.ContainsKey(virtualKey);

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = None;
        var t = (text ?? "").Trim();
        if (t.Length == 0 || t.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
        var mods = HotkeyModifiers.None;
        var parts = t.Split('+', StringSplitOptions.TrimEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "windows": mods |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }
        if (!Codes.TryGetValue(parts[^1], out var vk)) return false;
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    public static Hotkey Parse(string text) =>
        TryParse(text, out var h) ? h : throw new FormatException($"Not a hotkey: '{text}'");

    public override string ToString()
    {
        if (IsNone) return "None";
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Names.TryGetValue(VirtualKey, out var n) ? n : "Key" + VirtualKey.ToString(CultureInfo.InvariantCulture));
        return string.Join("+", parts);
    }
}
