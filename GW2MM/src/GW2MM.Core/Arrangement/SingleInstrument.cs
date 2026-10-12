namespace GW2MM.Core.Arrangement;

/// <summary>
/// One-player songs (Players = 1 in Data.csv): every track's notes are merged into one part for one instrument (what
/// the original client's unfinished single-player engine set out to do).
/// <para>A Guild Wars 2 instrument plays one octave at a time: C to C, 8 keys. When several tracks sound at once,
/// their notes can span more than that, so each chord is folded into one octave. That octave is the one holding the
/// chord's highest note (normally the melody), and the other notes are moved by whole octaves into it, keeping
/// harmony and rhythm. Pitches outside the instrument's C3..C6 range are folded in the same way.</para>
/// </summary>
public static class SingleInstrument
{
    private const int Lowest = 48;   // C3, bottom of the low octave
    private const int Highest = 84;  // C6, the top C of the high octave

    /// <summary>
    /// A copy of the song with all notes in track 1 (folded chord by chord, see the class remarks) and the same tempo
    /// changes. Notes whose pitch is not a number are dropped.
    /// </summary>
    public static CsvSong Merge(CsvSong song)
    {
        var notes = new List<(double Time, string TimeText, int Pitch)>();
        for (var t = 1; t <= CsvSong.MaxTrack; t++)
            for (var k = 0; k < song.TrackTimes[t].Count && k < song.TrackPitches[t].Count; k++)
                if (song.TrackPitches[t][k] is { } p && Ahk.AhkValue.Number(song.TrackTimes[t][k]) is { } time)
                    notes.Add((time, song.TrackTimes[t][k], p));

        var merged = new CsvSong();
        merged.TempoTimes.AddRange(song.TempoTimes);
        merged.TempoMarkers.AddRange(song.TempoMarkers);
        foreach (var chord in notes.GroupBy(n => n.Time).OrderBy(g => g.Key))
        {
            var timeText = chord.First().TimeText;
            foreach (var pitch in Fold(chord.Select(n => n.Pitch).ToList()))
            {
                merged.TrackTimes[1].Add(timeText);
                merged.TrackNotes[1].Add(KeyMap.MidiNoteLetters[pitch]);
                merged.TrackPitches[1].Add(pitch);
            }
        }
        return merged;
    }

    /// <summary>The chord's pitches moved into the octave of its highest note (C..C, so its top C may stay).</summary>
    public static IReadOnlyList<int> Fold(IReadOnlyList<int> chord)
    {
        if (chord.Count == 0) return chord;
        var top = chord.Select(IntoRange).Max();
        // The octave whose C..C span holds the top note. A top note that is a C is the top C of the octave below
        // (each octave plays its upper C as key 8), so a melody touching C needs no octave change for it.
        var bottom = top % 12 == 0 && top > Lowest ? top - 12 : Lowest + 12 * ((top - Lowest) / 12);
        var result = new SortedSet<int>();
        foreach (var p in chord)
        {
            var q = IntoRange(p);
            if (q == bottom + 12 && q == top) { result.Add(q); continue; } // the top C itself
            result.Add(bottom + (((q - bottom) % 12) + 12) % 12);
        }
        return result.ToList();
    }

    private static int IntoRange(int pitch)
    {
        while (pitch < Lowest) pitch += 12;
        while (pitch > Highest) pitch -= 12;
        return pitch;
    }

    /// <summary>
    /// The port mapping for a merged song: every slot that has any track ticked plays the merged part (track 1, in its
    /// Player1 window). When nobody has a track ticked, the host does.
    /// </summary>
    public static PortMapping Mapping(PortMapping mapping)
    {
        var result = new PortMapping();
        var any = false;
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            var ticked = Enumerable.Range(1, PortMapping.Tracks).Any(t => mapping[slot, t]);
            result[slot, 1] = ticked;
            any |= ticked;
        }
        if (!any) result[1, 1] = true;
        return result;
    }
}
