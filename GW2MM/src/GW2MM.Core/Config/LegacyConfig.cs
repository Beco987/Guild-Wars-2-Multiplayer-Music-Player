using GW2MM.Core.Text;

namespace GW2MM.Core.Config;

/// <summary>
/// Reads and rewrites Config.txt exactly like the original: every line is split on commas into one flat list of
/// fields and a setting's value is the field after its name (the last occurrence wins). Saving rewrites each line
/// whose first field is a setting's name (case-insensitive) as "Name,Value" and keeps every other line (comments)
/// as-is. (The original rewrote any line merely containing the name, so a value such as the host address
/// "mynameserver.net" would have been overwritten by the "Name" setting.)
/// </summary>
public sealed class LegacyConfig
{
    private readonly List<string> _fields;

    private LegacyConfig(List<string> fields) => _fields = fields;

    public static LegacyConfig Load(string path)
    {
        if (!File.Exists(path)) return new LegacyConfig(new List<string>());
        var fields = LegacyText.Lines(LegacyText.ReadText(path)).SelectMany(l => l.Length == 0 ? Array.Empty<string>() : l.Split(',')).ToList();
        return new LegacyConfig(fields);
    }

    /// <summary>All (name, value) pairs whose name starts with <paramref name="prefix"/> (last occurrence wins).</summary>
    public Dictionary<string, string> WithPrefix(string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < _fields.Count; i++)
            if (_fields[i].StartsWith(prefix, StringComparison.Ordinal))
                result[_fields[i]] = i + 1 < _fields.Count ? _fields[i + 1] : "";
        return result;
    }

    /// <summary>The value of <paramref name="key"/> (case-sensitive name), "" when the name is the last field, null when absent.</summary>
    public string? Get(string key)
    {
        string? value = null;
        for (var i = 0; i < _fields.Count; i++)
            if (_fields[i] == key) value = i + 1 < _fields.Count ? _fields[i + 1] : "";
        return value;
    }

    /// <summary>AutoHotkey truthiness of a setting ("", "0" and other zero values are false).</summary>
    public static bool IsTrue(string? value) =>
        !string.IsNullOrEmpty(value) && !(Ahk.AhkValue.TryNumber(value, out var n) && n == 0);

    /// <summary>
    /// Rewrites the file with new values. Keys are checked in the given order. Unlike the original, a setting whose
    /// line is missing is appended (the original could never add a missing line, so such a setting could not be saved).
    /// </summary>
    public static void Save(string path, IReadOnlyList<(string Key, string Value)> updates)
    {
        var lines = File.Exists(path) ? LegacyText.Lines(LegacyText.ReadText(path)) : new List<string>();
        var sb = new System.Text.StringBuilder();
        var written = new HashSet<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            var entry = lines[i];
            var name = entry.Split(',')[0].Trim();
            var match = updates.FirstOrDefault(u => name.Equals(u.Key, StringComparison.OrdinalIgnoreCase));
            if (match.Key is not null)
            {
                sb.Append(match.Key).Append(',').Append(match.Value).Append('\n');
                written.Add(match.Key);
            }
            else
            {
                sb.Append(entry);
                if (i != lines.Count - 1) sb.Append('\n');
            }
        }
        foreach (var (key, value) in updates.Where(u => !written.Contains(u.Key)))
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
            sb.Append(key).Append(',').Append(value).Append('\n');
        }
        LegacyText.Write(path, sb.ToString());
    }
}
