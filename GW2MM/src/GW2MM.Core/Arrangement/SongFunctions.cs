using GW2MM.Core.Ahk;

namespace GW2MM.Core.Arrangement;

/// <summary>
/// Ports of the list-processing functions in the original <c>scripts/Functions.ahk</c>. They are
/// deliberately literal: their exact output (including artefacts such as the trailing blank time that
/// <c>Sort</c> + <c>StrSplit</c> leave behind) determines which keys are pressed and when, and every
/// function is checked against the original AutoHotkey code by the golden tests.
/// Arrays are 0-based here; the AHK originals are 1-based.
/// </summary>
public static class SongFunctions
{
    public readonly record struct Pair(List<string> Num, List<string> Alpha);

    private static string At(IReadOnlyList<string> a, int i) => i >= 0 && i < a.Count ? a[i] : "";

    /// <summary>
    /// <c>Combine(a1, a2, a3, a4)</c>: merges two (time, keys) lists. Num = the numerically sorted unique
    /// times followed by one blank entry (an artefact of the original <c>Sort</c>/<c>StrSplit</c>); Alpha =
    /// the key strings grouped per time (later entries prepended), in time order.
    /// </summary>
    public static Pair Combine(IReadOnlyList<string> a1, IReadOnlyList<string> a2, IReadOnlyList<string> a3, IReadOnlyList<string> a4)
    {
        var num = SortedTimes(a1, a3, unique: true);
        var t = new AhkObject();
        for (var k = 0; k < a1.Count; k++) t.Set(a1[k], At(a2, k) + t.Get(a1[k]));
        for (var k = 0; k < a3.Count; k++) t.Set(a3[k], At(a4, k) + t.Get(a3[k]));
        return new Pair(num, t.Values());
    }

    /// <summary>
    /// <c>CombineFlags(times, keys, tempoTimes, tempoMarkers)</c>: inserts tempo markers ("B&lt;usPerQuarter&gt;")
    /// into the note timeline. Num keeps duplicate times.
    /// <para>
    /// The original guards with <c>if(checknull =)</c>, which AutoHotkey never treats as true (verified with the
    /// oracle), so every marker goes through <c>Object.Insert(time + adjust - 1)</c>: integer keys at or above that
    /// position shift up by one and <c>adjust</c> grows by one. The visible effect is that a marker lands in front of
    /// the note group at its own time, or in front of the group exactly one tick earlier if there is one.
    /// That behaviour is reproduced as-is.
    /// </para>
    /// </summary>
    public static Pair CombineFlags(IReadOnlyList<string> a1, IReadOnlyList<string> a2, IReadOnlyList<string> a3, IReadOnlyList<string> a4)
    {
        var num = SortedTimes(a1, a3, unique: false);
        var t = new AhkObject();
        for (var k = 0; k < a1.Count; k++) t.Set(a1[k], At(a2, k) + t.Get(a1[k]));

        long adjust = 0;
        for (var k = 0; k < a3.Count; k++)
        {
            var marker = At(a4, k);
            if (marker == "") continue;
            if (AhkValue.IsInteger(a3[k], out var v)) t.InsertAt(v + adjust - 1, marker);
            else t.Set("", marker); // a blank/non-integer time: Insert with a non-integer key is a plain assignment
            adjust++;
        }
        return new Pair(num, t.Values());
    }

    private static List<string> SortedTimes(IReadOnlyList<string> a1, IReadOnlyList<string> a3, bool unique)
    {
        var list = a1.Where(v => v != "").Concat(a3.Where(v => v != "")).ToList();
        if (list.Count == 0) return new List<string>();
        IEnumerable<string> sorted = list.OrderBy(NumericSortKey);
        if (unique) sorted = sorted.GroupBy(NumericSortKey).Select(g => g.First()).OrderBy(NumericSortKey);
        var result = sorted.ToList();
        result.Add("");
        return result;
    }

    // AHK's numeric Sort compares the leading numeric part of each item (atof semantics).
    private static double NumericSortKey(string s)
    {
        var t = s.TrimStart(' ', '\t');
        var len = 0;
        while (len < t.Length && (char.IsAsciiDigit(t[len]) || (len == 0 && t[len] is '+' or '-') || t[len] == '.')) len++;
        return AhkValue.TryNumber(t[..len], out var v) ? v : 0;
    }

    private const string Set1 = "aAsSdfFgGhHjqQwWerRtTyYuizZxXcvVbBnNm90";
    private const string Set2 = "zZxXcvVbBnNmaAsSdfFgGhHjkq";
    private const string Set3 = "qQwWerRtTyYuiaAsSdfFgGhHj";
    private const string LowRow = "zZxXcvVbBnNm";
    private const string MidRowNoC = "AsSdfFgGhHj";
    private const string MidRow = "aAsSdfFgGhHj";
    private const string HighRowNoC = "QwWerRtTyYui";

    private static bool ContainsAny(string s, string chars) => s.IndexOfAny(chars.ToCharArray()) >= 0;

    /// <summary>
    /// <c>AddSwaps</c>: walks the chords in order while tracking the instrument octave (0 low, 1 middle,
    /// 2 high), inserting "9" (octave down) / "0" (octave up) and turning C notes into "8" where needed.
    /// Each output chord lists its notes in the fixed order of the octave's key set.
    /// </summary>
    public static List<string> AddSwaps(IReadOnlyList<string> a1)
    {
        var octave = 1;
        var a3 = new List<string>(a1.Count);
        foreach (var notes in a1) a3.Add(AddSwapsStep(notes, ref octave));
        return a3;
    }

    /// <summary>One chord of <see cref="AddSwaps"/>: its keys with octave changes, updating the current octave (0..2).</summary>
    internal static string AddSwapsStep(string notes, ref int octave)
    {
        var s = new System.Text.StringBuilder();
        if (octave == 1)
        {
            foreach (var i in Set1)
            {
                if (notes.IndexOf(i) < 0) continue;
                if (i == 'a')
                {
                    if (ContainsAny(notes, LowRow) && !ContainsAny(notes, MidRowNoC))
                    {
                        if (octave == 1) { octave--; s.Append('9'); }
                        s.Append('8');
                    }
                    else s.Append(i);
                }
                else if (HighRowNoC.IndexOf(i) >= 0)
                {
                    if (octave == 1) { octave++; s.Append('0'); }
                    s.Append(i);
                }
                else if (LowRow.IndexOf(i) >= 0)
                {
                    if (octave == 1) { octave--; s.Append('9'); }
                    s.Append(i);
                }
                else if (i == 'q')
                {
                    if (ContainsAny(notes, HighRowNoC))
                    {
                        if (octave == 1) { octave++; s.Append('0'); }
                        s.Append(i);
                    }
                    else s.Append('8');
                }
                else s.Append(i);
            }
        }
        else if (octave == 0)
        {
            foreach (var i in Set2)
            {
                if (notes.IndexOf(i) < 0) continue;
                if (i == 'a')
                {
                    if (ContainsAny(notes, MidRowNoC))
                    {
                        if (octave == 0) { octave++; s.Append('0'); }
                        s.Append(i);
                    }
                    else s.Append('8');
                }
                else if (i == 'q')
                {
                    if (octave == 0) { octave++; s.Append('0'); }
                    s.Append('8');
                }
                else if ((MidRowNoC + "k").IndexOf(i) >= 0)
                {
                    if (octave == 0) { octave++; s.Append('0'); }
                    s.Append(i);
                }
                else s.Append(i);
            }
        }
        else if (octave == 2)
        {
            foreach (var i in Set3)
            {
                if (notes.IndexOf(i) < 0) continue;
                if (i == 'q')
                {
                    if (ContainsAny(notes, MidRow) && !ContainsAny(notes, HighRowNoC))
                    {
                        if (octave == 2) { octave--; s.Append('9'); }
                        s.Append('8');
                    }
                    else s.Append(i);
                }
                else if (MidRow.IndexOf(i) >= 0)
                {
                    if (octave == 2) { octave--; s.Append('9'); }
                    s.Append(i);
                }
                else s.Append(i);
            }
        }
        return s.ToString();
    }

    /// <summary>
    /// <c>OpportunisticSwaps(notes, times)</c>: when a chord starts with an octave swap and the gap since the
    /// previous chord is at least the OctaveDelay (compared in MIDI ticks, as the original does), the swap is
    /// moved to its own event halfway through the gap. Returns (Num = notes, Alpha = times) like the original.
    /// </summary>
    public static Pair OpportunisticSwaps(IReadOnlyList<string> a1, IReadOnlyList<string> a2, string octaveDelay)
    {
        var a3 = new List<string>();
        var a4 = new List<string>();
        for (var idx = 0; idx < a1.Count; idx++)
        {
            var note = a1[idx];
            if (idx != 0 && note.Length > 0 && note[0] is '9' or '0')
            {
                var dif = Sub(At(a2, idx), At(a2, idx - 1));
                if (AhkValue.Compare(dif is null ? "" : FormatNumber(dif.Value), octaveDelay) >= 0)
                {
                    a3.Add(note[0].ToString());
                    var prev = AhkValue.Number(At(a2, idx - 1));
                    double? mid = prev is null || dif is null ? null : prev.Value + dif.Value / 2;
                    a4.Add(AhkValue.Format(AhkValue.Floor(mid)));
                    a3.Add(note[1..]);
                    a4.Add(At(a2, idx));
                    continue;
                }
            }
            a3.Add(note);
            a4.Add(At(a2, idx));
        }
        return new Pair(a3, a4);
    }

    /// <summary><c>replaceChars</c>: applies each (from, to) replacement in order to every item.</summary>
    public static List<string> ReplaceChars(IReadOnlyList<string> a1, IReadOnlyList<(string From, string To)> pairs)
    {
        var result = new List<string>(a1.Count);
        foreach (var item in a1)
        {
            var v = item;
            foreach (var (from, to) in pairs) v = v.Replace(from, to, StringComparison.Ordinal);
            result.Add(v);
        }
        return result;
    }

    /// <summary>AHK subtraction of two values (blank when either side is not numeric).</summary>
    internal static double? Sub(string a, string b)
    {
        var x = AhkValue.Number(a);
        var y = AhkValue.Number(b);
        return x is null || y is null ? null : x.Value - y.Value;
    }

    internal static string FormatNumber(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e15
            ? ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : v.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
}
