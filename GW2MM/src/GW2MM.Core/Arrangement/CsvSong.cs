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

    // Not used by the original or by the arrangement; solo humanizing reads the song's meter and phrasing from them.

    /// <summary>Ticks per quarter note from the <c>Header</c> line (0 when missing or not a positive number).</summary>
    public int Division { get; private set; }
    /// <summary>The <c>Time_signature</c> events (numerator and denominator, e.g. 6 and 8), in file order.</summary>
    public List<(double Tick, int Numerator, int Denominator)> TimeSignatures { get; } = new();
    /// <summary>Every recorded note with the tick it ends at (its <c>Note_off_c</c>, or <c>Note_on_c</c> with velocity 0).</summary>
    public List<ScoreNote> Notes { get; } = new();

    public int NoteCount => TrackNotes.Sum(t => t.Count);

    /// <summary>
    /// Parses midicsv text. Behaviour carried over from the original parser:
    /// <list type="bullet">
    /// <item>A note is recorded for every <c>Note_on_c</c> with velocity &gt; 0, in the track named by field 1.</item>
    /// <item>The note letter comes from field 5 of the <em>most recent line of any type</em> whose field 5 is a mapped
    /// MIDI number (48-84, 96, 97). A note outside that range therefore repeats the previous mapped letter
    /// (blank at the start of a song), and e.g. a controller number in a <c>Control_c</c> line also counts.</item>
    /// <item>Fields are split on every comma (quoted text is not special) and trimmed of spaces/tabs.</item>
    /// <item>Every <c>Tempo</c> event is kept, sorted by time (<paramref name="allTempoChanges"/>). The original kept
    /// only the first one before the first recorded note in the file, and all after it. Most MIDI files keep their
    /// tempo changes in a first track without notes, which comes before every note in the file, so all of a song's
    /// tempo changes but the first were lost. With <paramref name="allTempoChanges"/> false the original rule
    /// applies (used by the oracle tests).</item>
    /// <item>Only tracks 1..25 are kept (only 1..8 can be mapped to players).</item>
    /// </list>
    /// </summary>
    public static CsvSong Parse(IEnumerable<string> lines, bool allTempoChanges = true)
    {
        var song = new CsvSong();
        string note = "", track = "", duration = "";
        int? pitch = null;
        var postNotes = false;
        var pzero = false;
        var sounding = new Dictionary<(int, double, int), Queue<ScoreNote>>();
        foreach (var line in lines)
        {
            if (line.Length == 0) continue;
            var record = false;
            var tempoer = false;
            var fields = line.Split(',');
            ReadScoreEvent(song, fields, sounding);
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
                        if (tempoer && (allTempoChanges || !postNotes && !pzero || postNotes))
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
        if (allTempoChanges && song.TempoTimes.Count > 1)
        {
            // Tempo changes from several tracks, in time order (stable: at equal times the later one in the file wins).
            var ordered = song.TempoTimes.Zip(song.TempoMarkers)
                .Select((p, k) => (Time: p.First, Marker: p.Second, Key: AhkValue.Number(p.First) ?? double.MaxValue, Index: k))
                .OrderBy(x => x.Key).ThenBy(x => x.Index).ToList();
            song.TempoTimes.Clear();
            song.TempoMarkers.Clear();
            foreach (var x in ordered) { song.TempoTimes.Add(x.Time); song.TempoMarkers.Add(x.Marker); }
        }
        return song;
    }

    /// <summary>Header division, time signatures and note starts/ends (none of which the original parser kept).</summary>
    private static void ReadScoreEvent(CsvSong song, string[] fields, Dictionary<(int, double, int), Queue<ScoreNote>> sounding)
    {
        if (fields.Length < 3) return;
        string F(int i) => i < fields.Length ? fields[i].Trim(' ', '\t') : "";
        var type = F(2);
        if (type.Equals("Header", StringComparison.OrdinalIgnoreCase))
        {
            if (AhkValue.Number(F(5)) is { } division && division >= 1 && division < 32768) song.Division = (int)division;
        }
        else if (type.Equals("Time_signature", StringComparison.OrdinalIgnoreCase))
        {
            if (AhkValue.Number(F(1)) is { } tick && AhkValue.Number(F(3)) is { } num && num is >= 1 and <= 64
                && AhkValue.Number(F(4)) is { } power && power is >= 0 and <= 6)
                song.TimeSignatures.Add((tick, (int)num, 1 << (int)power));
        }
        else if (type.Equals("Note_on_c", StringComparison.OrdinalIgnoreCase) || type.Equals("Note_off_c", StringComparison.OrdinalIgnoreCase))
        {
            if (TrackNumber(F(0)) is not { } track || AhkValue.Number(F(1)) is not { } at || AhkValue.Number(F(3)) is not { } channel
                || AhkValue.Number(F(4)) is not { } p || p != Math.Floor(p) || p is < 0 or > 127 || AhkValue.Number(F(5)) is not { } velocity)
                return;
            var key = (track, channel, (int)p);
            if (type.Equals("Note_on_c", StringComparison.OrdinalIgnoreCase) && velocity > 0)
            {
                var note = new ScoreNote(track, at, (int)p);
                song.Notes.Add(note);
                if (!sounding.TryGetValue(key, out var open)) sounding[key] = open = new Queue<ScoreNote>();
                open.Enqueue(note);
            }
            else if (sounding.TryGetValue(key, out var open) && open.Count > 0)
                open.Dequeue().EndTick = at;
        }
    }

    // The original pushes onto the variable "Track<field1>", which only exists for "1".."25".
    private static int? TrackNumber(string s)
    {
        if (s.Length is < 1 or > 2 || s[0] == '0' || !s.All(char.IsAsciiDigit)) return null;
        var n = int.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        return n is >= 1 and <= MaxTrack ? n : null;
    }
}

/// <summary>A note of the song: its track, start tick, MIDI pitch and end tick (null when it never ends).</summary>
public sealed class ScoreNote(int track, double tick, int pitch)
{
    public int Track { get; } = track;
    public double Tick { get; } = tick;
    public int Pitch { get; } = pitch;
    public double? EndTick { get; internal set; }
}
