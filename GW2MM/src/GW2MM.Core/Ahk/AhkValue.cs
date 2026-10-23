using System.Globalization;

namespace GW2MM.Core.Ahk;

/// <summary>
/// The small subset of AutoHotkey v1 value semantics the original song pipeline relies on.
/// Every rule here was confirmed by running AutoHotkey 1.1.37 (see tools/oracle).
/// <list type="bullet">
/// <item>Strings that look like numbers (optionally surrounded by spaces/tabs, decimal, float or 0x hex) are numbers.</item>
/// <item>Arithmetic with a blank or non-numeric operand yields blank (represented as <c>null</c>).</item>
/// <item><c>Floor</c>/<c>Round</c> of blank yield 0.</item>
/// <item><c>=</c> is case-insensitive (even with StringCaseSense On); ordering comparisons between a number
/// and a non-numeric string fall back to case-sensitive string comparison.</item>
/// </list>
/// </summary>
public static class AhkValue
{
    public static bool TryNumber(string? s, out double value)
    {
        value = 0;
        if (s is null) return false;
        var t = s.Trim(' ', '\t');
        if (t.Length == 0) return false;
        var body = t;
        var negative = false;
        if (body[0] is '+' or '-')
        {
            negative = body[0] == '-';
            body = body[1..];
        }
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (body.Length > 2 && long.TryParse(body.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex))
            {
                value = negative ? -hex : hex;
                return true;
            }
            return false;
        }
        foreach (var c in body)
            if (!(char.IsAsciiDigit(c) || c == '.' || c is 'e' or 'E' or '+' or '-'))
                return false;
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;
        return true;
    }

    public static double? Number(string? s) => TryNumber(s, out var v) ? v : null;

    public static bool IsInteger(string? s, out long value)
    {
        value = 0;
        if (s is null) return false;
        return long.TryParse(s.Trim(' ', '\t'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>AHK <c>Floor()</c>: blank -> 0.</summary>
    public static long Floor(double? v) => v is null ? 0 : (long)Math.Floor(v.Value);

    /// <summary>AHK <c>Round()</c> with no precision argument: blank -> 0, implemented as AutoHotkey does (floor(x+0.5) / ceil(x-0.5)).</summary>
    public static long Round(double? v)
    {
        if (v is null) return 0;
        var x = v.Value;
        return (long)(x >= 0.0 ? Math.Floor(x + 0.5) : Math.Ceiling(x - 0.5));
    }

    /// <summary>AHK case-insensitive <c>=</c>.</summary>
    public static bool Equal(string? a, string? b)
    {
        if (TryNumber(a, out var x) && TryNumber(b, out var y)) return x == y;
        return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AHK <c>a &gt;= b</c> / <c>a &gt; b</c> with StringCaseSense On.</summary>
    public static int Compare(string? a, string? b)
    {
        if (TryNumber(a, out var x) && TryNumber(b, out var y)) return x.CompareTo(y);
        return string.CompareOrdinal(a ?? "", b ?? "");
    }

    /// <summary>Formats a number the way AHK v1 prints integers (used when a computed integer is stored back as text).</summary>
    public static string Format(long v) => v.ToString(CultureInfo.InvariantCulture);
}
