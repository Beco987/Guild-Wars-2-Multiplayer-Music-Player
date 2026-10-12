using GW2MM.Core.Ahk;

namespace GW2MM.Core.Arrangement;

/// <summary>Note and tempo events extracted from a midicsv file, exactly as the original <c>SongPlayer</c> extracts them.</summary>
public sealed class CsvSong
{
    public const int MaxTrack = 25;

    /// <summary>Per track (index 1..25): the times (as text) and note letters of each recorded note.</summary>
    public List<string>[] TrackTimes { get; } = Enumerable.Range(0, MaxTrack + 1).Select(_ => new List<string>()).ToArray();
    public List<string>[] TrackNotes { get; } = Enumerable.Range(0, MaxTrack + 1).Select(_ => new List<string>()).ToArray();
    /// <summary>
    /// The MIDI note number of each recorded note, parallel to <see cref="TrackNotes"/> (null when not a number).
    /// Not used by the original arrangement, whose letters repeat the previous note for pitches outside C3..C6.
    /// The single-instrument merge needs the real pitches to fold them into range.
    /// </summary>
    public List<int?>[] TrackPitches { get; } = Enumerable.Range(0, MaxTrack + 1).Select(_ => new List<int?>()).ToArray();
    public List<string> TempoTimes { get; } = new();
    public List<string> TempoMarkers { get; } = new();

    public int NoteCount => TrackNotes.Sum(t => t.Count);

    /// <summary>
    /// Parses midicsv text. Behaviour carried over from the original parser:
    /// <list type="bullet">
    /// <item>A note is recorded for every <c>Note_on_c</c> with velocity &gt; 0, in the track named by field 1.</item>
    /// <item>The note letter comes from field 5 of the <em>most recent line of any type</em> whose field 5 is a mapped
    /// MIDI number (48-84, 96, 97). A note outside that range therefore repeats the previous mapped letter
    /// (blank at the start of a song), and e.g. a controller number in a <c>Control_c</c> line also counts.</item>
    /// <item>Fields are split on every comma (quoted text is not special) and trimmed of spaces/tabs.</item>
    /// <item>Before the first recorded note only the first <c>Tempo</c> event is kept; after it, all are kept.</item>
    /// <item>Only tracks 1..25 are kept (only 1..8 can be mapped to players).</item>
    /// </list>
    /// </summary>
    public static CsvSong Parse(IEnumerable<string> lines)
    {
        var song = new CsvSong();
        string note = "", track = "", duration = "";
        int? pitch = null;
        var postNotes = false;
        var pzero = false;
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            var record = false;
            var tempoer = false;
            var fields = line.Split(',');
            for (var f = 0; f < fields.Length; f++)
            {
                var subIndex = f + 1;
                var v = fields[f].Trim(' ', '\t');
                switch (subIndex)
                {
                    case 1: track = v; break;
                    case 2: duration = v; break;
                    case 3:
                        if (AhkValue.Equal(v, "Note_on_c")) record = true;
                        if (AhkValue.Equal(v, "Tempo")) tempoer = true;
                        break;
                    case 4:
                        if (tempoer && (!postNotes && !pzero || postNotes))
                        {
                            song.TempoMarkers.Add("B" + v);
                            song.TempoTimes.Add(duration);
                            pzero = true;
                        }
                        break;
                    case 5:
                        pitch = AhkValue.TryNumber(v, out var raw) && raw == Math.Floor(raw) && raw is >= 0 and <= 127 ? (int)raw : null;
                        if (AhkValue.TryNumber(v, out var n) && n == Math.Floor(n) && n is >= int.MinValue and <= int.MaxValue
                            && KeyMap.MidiNoteLetters.TryGetValue((int)n, out var letter))
                            note = letter;
                        break;
                    case 6:
                        if (record && AhkValue.Compare(v, "0") > 0)
                        {
                            postNotes = true;
                            if (TrackNumber(track) is { } t)
                            {
                                song.TrackTimes[t].Add(duration);
                                song.TrackNotes[t].Add(note);
                                song.TrackPitches[t].Add(pitch);
                            }
                        }
                        break;
                }
            }
        }
        return song;
    }

    // The original pushes onto the variable "Track<field1>", which only exists for "1".."25".
    private static int? TrackNumber(string s)
    {
        if (s.Length is < 1 or > 2 || s[0] == '0' || !s.All(char.IsAsciiDigit)) return null;
        var n = int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return n is >= 1 and <= MaxTrack ? n : null;
    }
}
