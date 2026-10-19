namespace GW2MM.Core.Arrangement;

/// <summary>
/// The key encoding shared by host and clients. After arrangement every key press is a single character:
/// track-window <c>n</c> (1..8) owns the 15 characters <c>Chr(192 + 16*(n-1) + offset)</c>, offset 0..14,
/// which stand for the Guild Wars 2 instrument keys below, sent to the window titled "Player<i>n</i>".
/// <para>The 9 / 0 keys written by MIDI notes 96 / 97 (notes of the song, not octave changes the arrangement added)
/// use their own characters after the window ranges, <c>Chr(320 + 2*(n-1))</c> for 9 and the next one for 0, so the
/// octave tracker (<see cref="Playback.OctaveTracker"/>) can leave them out. They press the same keys.</para>
/// </summary>
public static class KeyMap
{
    public const int WindowCount = 8;
    private const int FirstChar = 192;
    private const int CharsPerWindow = 16;
    private const int FirstNoteSwapChar = FirstChar + CharsPerWindow * WindowCount;
    private const int Down = 13, Up = 14; // offsets of keys 9 and 0

    /// <summary>Instrument keys in offset order (1..8 = notes, F1..F5 = sharps, 9 = octave down, 0 = octave up).</summary>
    public static readonly IReadOnlyList<string> Keys = new[] { "1", "F1", "2", "F2", "3", "4", "F3", "5", "F4", "6", "F5", "7", "8", "9", "0" };

    /// <summary>MIDI note number -> note letter used by the original parser (lower case = natural, upper case = sharp).</summary>
    public static readonly IReadOnlyDictionary<int, string> MidiNoteLetters = new Dictionary<int, string>
    {
        [48] = "z", [49] = "Z", [50] = "x", [51] = "X", [52] = "c", [53] = "v", [54] = "V", [55] = "b", [56] = "B", [57] = "n", [58] = "N", [59] = "m",
        [60] = "a", [61] = "A", [62] = "s", [63] = "S", [64] = "d", [65] = "f", [66] = "F", [67] = "g", [68] = "G", [69] = "h", [70] = "H", [71] = "j",
        [72] = "q", [73] = "Q", [74] = "w", [75] = "W", [76] = "e", [77] = "r", [78] = "R", [79] = "t", [80] = "T", [81] = "y", [82] = "Y", [83] = "u",
        [84] = "i", [96] = "9", [97] = "0",
    };

    private static readonly (string Letter, int Offset)[] LetterOffsets =
    {
        ("z", 0), ("Z", 1), ("x", 2), ("X", 3), ("c", 4), ("v", 5), ("V", 6), ("b", 7), ("B", 8), ("n", 9), ("N", 10), ("m", 11),
        ("a", 0), ("A", 1), ("s", 2), ("S", 3), ("d", 4), ("f", 5), ("F", 6), ("g", 7), ("G", 8), ("h", 9), ("H", 10), ("j", 11),
        ("q", 0), ("Q", 1), ("w", 2), ("W", 3), ("e", 4), ("r", 5), ("R", 6), ("t", 7), ("T", 8), ("y", 9), ("Y", 10), ("u", 11),
        ("i", 12), ("8", 12), ("9", 13), ("0", 14),
    };

    /// <summary>The ordered replacement list the original <c>TrackWriter</c> applies for output window <paramref name="window"/>.</summary>
    public static IReadOnlyList<(string From, string To)> Replacements(int window)
    {
        var baseChar = FirstChar + CharsPerWindow * (window - 1);
        return LetterOffsets.Select(p => (p.Letter, ((char)(baseChar + p.Offset)).ToString())).ToArray();
    }

    /// <summary>The character for a 9 / 0 key from MIDI note 96 / 97 in <paramref name="window"/> (see the class remarks).</summary>
    public static char NoteSwap(int window, bool up) => (char)(FirstNoteSwapChar + 2 * (window - 1) + (up ? 1 : 0));

    /// <summary>
    /// The window and direction (+1 up, -1 down) of an octave change the arrangement added; null for anything else,
    /// including the 9 / 0 keys of MIDI notes 96 / 97.
    /// </summary>
    public static (int Window, int Step)? ArrangedSwap(char c)
    {
        var rel = c - FirstChar;
        if (rel < 0 || rel >= CharsPerWindow * WindowCount) return null;
        return (rel % CharsPerWindow) switch { Down => (rel / CharsPerWindow + 1, -1), Up => (rel / CharsPerWindow + 1, 1), _ => null };
    }

    /// <summary>Decodes one key character into (window number 1..8, instrument key name); null for anything else.</summary>
    public static (int Window, string Key)? Decode(char c)
    {
        var note = c - FirstNoteSwapChar;
        if (note >= 0 && note < 2 * WindowCount) return (note / 2 + 1, note % 2 == 0 ? "9" : "0");
        var rel = c - FirstChar;
        if (rel < 0) return null;
        var window = rel / CharsPerWindow + 1;
        var offset = rel % CharsPerWindow;
        if (window > WindowCount || offset >= Keys.Count) return null;
        return (window, Keys[offset]);
    }

    public static string WindowTitle(int window) => "Player" + window;
}
