using System.Text;

namespace GW2MM.Tests;

/// <summary>Access to the golden data produced by running the original AutoHotkey code (tools/oracle/make_golden.py).</summary>
internal static class Golden
{
    public static string Dir(params string[] parts) => Path.Combine(new[] { AppContext.BaseDirectory, "Golden" }.Concat(parts).ToArray());

    public static string[] ReadLines(string path) =>
        File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿').Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

    /// <summary>Decodes the oracle's "count:item|item" array encoding.</summary>
    public static List<string> DecodeArray(string s)
    {
        var p = s.IndexOf(':');
        var n = int.Parse(s[..p]);
        var rest = s[(p + 1)..];
        // Mirrors the harness's AHK decoder: "Loop, Parse" over an empty string yields no items, so "1:" (one
        // blank item) reaches the original code as an empty array.
        if (n == 0 || rest.Length == 0) return new List<string>();
        var items = rest.Split('|').ToList();
        Assert.Equal(n, items.Count);
        return items;
    }

    /// <summary>Decodes note strings written as dot-separated code points.</summary>
    public static List<string> DecodeNotes(string s) =>
        DecodeArray(s).Select(e => e.Length == 0 ? "" : new string(e.Split('.').Select(c => (char)int.Parse(c)).ToArray())).ToList();

    public static string Encode(IReadOnlyList<string> a) => a.Count + ":" + string.Join("|", a);
}
