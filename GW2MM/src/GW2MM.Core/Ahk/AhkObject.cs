namespace GW2MM.Core.Ahk;

/// <summary>
/// Emulates the AutoHotkey v1 associative object as used by the original <c>Combine</c>/<c>CombineFlags</c>:
/// keys that are integer-looking strings become integer keys and are enumerated in ascending numeric order,
/// followed by the remaining (string) keys. Missing keys read as blank.
/// </summary>
internal sealed class AhkObject
{
    private readonly SortedDictionary<long, string> _ints = new();
    private readonly SortedDictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string key) =>
        AhkValue.IsInteger(key, out var i) ? Get(i) : _strings.GetValueOrDefault(key, "");

    public string Get(long key) => _ints.GetValueOrDefault(key, "");

    public void Set(string key, string value)
    {
        if (AhkValue.IsInteger(key, out var i)) _ints[i] = value;
        else _strings[key] = value;
    }

    public void Set(long key, string value) => _ints[key] = value;

    /// <summary>AHK v1 <c>Object.Insert(Index, Value)</c>: integer keys &gt;= Index shift up by one.</summary>
    public void InsertAt(long index, string value)
    {
        var shifted = _ints.Where(kv => kv.Key >= index).OrderByDescending(kv => kv.Key).ToList();
        foreach (var kv in shifted) _ints.Remove(kv.Key);
        foreach (var kv in shifted) _ints[kv.Key + 1] = kv.Value;
        _ints[index] = value;
    }

    public List<string> Values() => _ints.Values.Concat(_strings.Values).ToList();
}
