using System.Globalization;
using GW2MM.Core.Arrangement;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Text;

namespace GW2MM.Tests;

/// <summary>
/// End-to-end comparison with the original host pipeline (SongPlayer -> TrackWriter -> AddFiller -> CombineFlags ->
/// DelayToClock -> TrackSharer) run under AutoHotkey: per-slot key strings, delays, absolute times, latencies and
/// which clients are sent a part.
/// </summary>
public class PipelineGoldenTests
{
    public static IEnumerable<object[]> Cases() =>
        Directory.GetFiles(Golden.Dir("pipeline"), "*.case.txt").Select(f => new object[] { Path.GetFileName(f)[..^".case.txt".Length] }).OrderBy(o => (string)o[0]);

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchesOriginal(string name)
    {
        var spec = Golden.ReadLines(Golden.Dir("pipeline", name + ".case.txt")).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var outLines = Golden.ReadLines(Golden.Dir("pipeline", name + ".out.txt"));
        var expected = outLines.Where(l => l.Contains('=') && !l.StartsWith("SEND")).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var sends = outLines.Where(l => l.StartsWith("SEND")).Select(l => l.Split('\t')).ToList();

        var songPath = Golden.Dir("songs", spec["song"] + ".csv");
        var lines = File.Exists(songPath) ? LegacyText.Lines(LegacyText.ReadText(songPath)) : new List<string>();
        double? tempo = spec.ContainsKey("fromclient")
            ? GW2MM.Core.Ahk.AhkValue.Number(spec["clienttempo"])
            : SongLibrary.TempoFactorFromFields(new[] { "Some Other Song", " 2", " 3", " desc", spec["song"], " 3", " " + spec["tempomod"], " generated" }, spec["song"]);

        // The original octave handling, change for change (the player's adds the jumps between the low and high
        // octave, which the original dropped: see PlayerMatchesTheOriginalWhereNothingIsDropped).
        var plan = SongArranger.Arrange(CsvSong.Parse(lines, allTempoChanges: false), PortMapping.Parse(spec["ip"]), spec["octavedelay"], tempo, OctaveSwapMode.Original);
        // The player also keeps every tempo change (a deliberate fix to timing), which only adds empty tempo elements.
        // Octave swaps are worked out in MIDI ticks, so the keys pressed, swaps included, must come out the same.
        var played = SongArranger.Arrange(CsvSong.Parse(lines), PortMapping.Parse(spec["ip"]), spec["octavedelay"], tempo, OctaveSwapMode.Original);
        for (var slot = 1; slot <= 5; slot++)
            Assert.Equal(plan[slot].Notes.Where(n => n.Length > 0), played[slot].Notes.Where(n => n.Length > 0));

        var gw2 = spec["gw2"].Split(',').Select(double.Parse).ToArray();
        var ticks = spec["ticks"].Split(',').Select(double.Parse).ToArray();
        ticks[0] = double.Parse(expected["tick1"], CultureInfo.InvariantCulture);
        var sentSlots = new List<int>();
        for (var slot = 1; slot <= 5; slot++)
        {
            var p = plan[slot];
            // MIDI notes 96 / 97 press 9 / 0 under their own characters (so the octave tracker leaves them out);
            // the keys pressed are the original's.
            Assert.Equal(Golden.DecodeNotes(expected["notes" + slot]), p.Notes.Select(AsOriginal));
            // A slot with 1-3 keyed elements keeps unused raw (unscaled) gaps in the original's DelayToPlay; it is never
            // scheduled, so only the delays of slots that reach DelayToClock are compared.
            if (p.HasKeys)
                Assert.Equal(Golden.DecodeArray(expected["delays" + slot]), p.DelaysMs.Select(d => d.ToString(CultureInfo.InvariantCulture)).ToList());
            var latency = HostLatency(gw2, slot);
            if (p.HasKeys)
                Assert.Equal(double.Parse(expected["latency" + slot], CultureInfo.InvariantCulture), latency);
            var times = p.IsActive ? Schedule.AbsoluteTimes(p, ticks[slot - 1], latency) : new List<double>();
            Assert.Equal(Golden.DecodeArray(expected["times" + slot]), times.Select(t => t.ToString(CultureInfo.InvariantCulture)).ToList());
            if (slot != 1 && p.IsActive) sentSlots.Add(slot);
        }
        var expectedSlots = sends.Where(s => s[2].StartsWith("Note_1")).Select(s => int.Parse(s[1]) - 99).ToList();
        Assert.Equal(expectedSlots, sentSlots);
    }

    private static (CsvSong Song, PortMapping Mapping, string OctaveDelay, double? Tempo) Load(string name, bool allTempoChanges = true)
    {
        var spec = Golden.ReadLines(Golden.Dir("pipeline", name + ".case.txt")).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var songPath = Golden.Dir("songs", spec["song"] + ".csv");
        var lines = File.Exists(songPath) ? LegacyText.Lines(LegacyText.ReadText(songPath)) : new List<string>();
        double? tempo = spec.ContainsKey("fromclient")
            ? GW2MM.Core.Ahk.AhkValue.Number(spec["clienttempo"])
            : SongLibrary.TempoFactorFromFields(new[] { "Some Other Song", " 2", " 3", " desc", spec["song"], " 3", " " + spec["tempomod"], " generated" }, spec["song"]);
        return (CsvSong.Parse(lines, allTempoChanges), PortMapping.Parse(spec["ip"]), spec["octavedelay"], tempo);
    }

    /// <summary>The (slot, track, window) of every mapped track: the host plays track n on Player n, a client its k-th on Player k.</summary>
    private static IEnumerable<(int Slot, int Track, int Window)> Windows(PortMapping mapping)
    {
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            var window = 1;
            for (var track = 1; track <= PortMapping.Tracks; track++)
            {
                if (mapping[slot, track])
                {
                    yield return (slot, track, window);
                    if (slot != 1) window++;
                }
                if (slot == 1) window++;
            }
        }
    }

    /// <summary>
    /// Where the song has no jump straight between the low and high octave, the player's octave handling is the
    /// original's exactly (every key, every delay).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void PlayerMatchesTheOriginalWhereNothingIsDropped(string name)
    {
        var (song, mapping, octaveDelay, tempo) = Load(name, allTempoChanges: false);
        var jumps = Windows(mapping).Any(w =>
        {
            var chords = SongFunctions.Combine(song.TrackTimes[w.Track], song.TrackNotes[w.Track], new List<string>(), new List<string>()).Alpha;
            return !OctaveSwaps.AddSwaps(chords).SequenceEqual(SongFunctions.AddSwaps(chords));
        });
        if (jumps) return; // covered by ThePlayerPlaysEveryNoteAtItsPitch
        var original = SongArranger.Arrange(song, mapping, octaveDelay, tempo, OctaveSwapMode.Original);
        var player = SongArranger.Arrange(song, mapping, octaveDelay, tempo);
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            Assert.Equal(original[slot].Notes, player[slot].Notes);
            Assert.Equal(original[slot].DelaysMs, player[slot].DelaysMs);
        }
    }

    /// <summary>
    /// Follows each instrument's octave through the keys the player presses and checks that every note sounds at its
    /// pitch, in order, chord by chord, including the jumps between the low and high octave the original dropped.
    /// Chords that span the low and the high octave at once cannot be played in one octave and are only counted.
    /// (The 9 / 0 keys of MIDI notes 96 / 97 are left out, as the arrangement leaves them out of its octave.)
    /// <para>Cases with OctaveDelay under 2 are skipped: there the original rule for one change (kept as it was) splits
    /// a change from a chord 1 tick after the previous one, and its midpoint falls on the previous chord, which is
    /// then played after it. No real setting is that small.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void ThePlayerPlaysEveryNoteAtItsPitch(string name)
    {
        var (song, mapping, octaveDelay, tempo) = Load(name);
        if (GW2MM.Core.Ahk.AhkValue.Number(octaveDelay) is not >= 2) return;
        var plan = SongArranger.Arrange(song, mapping, octaveDelay, tempo);
        foreach (var (slot, track, window) in Windows(mapping))
        {
            if (!plan[slot].HasKeys) continue;
            var expected = ExpectedChords(song, track);
            var played = PlayedChords(plan[slot].Notes, window);
            Assert.Equal(expected.Count, played.Count);
            for (var k = 0; k < expected.Count; k++)
                if (!(expected[k].Min() < 60 && expected[k].Max() > 72))
                    Assert.True(expected[k].SequenceEqual(played[k]), $"{name}, track {track}, chord {k}: expected {string.Join(" ", expected[k])}, played {string.Join(" ", played[k])}");
        }
    }

    /// <summary>
    /// The track's chords in time order, as sorted pitches: the pitch of each note's letter (C3 to C6). The letter is
    /// what the arrangement plays; the original parser keeps the previous letter for a pitch an instrument does not
    /// have, so it can differ from the note's own pitch.
    /// </summary>
    private static List<int[]> ExpectedChords(CsvSong song, int track)
    {
        var pitchOf = KeyMap.MidiNoteLetters.Where(kv => kv.Value is not ("9" or "0")).ToDictionary(kv => kv.Value, kv => kv.Key);
        return song.TrackTimes[track].Select((time, k) => (Time: GW2MM.Core.Ahk.AhkValue.Number(time), Letter: song.TrackNotes[track][k]))
            .Where(n => n.Time is not null && pitchOf.ContainsKey(n.Letter))
            .GroupBy(n => n.Time!.Value).OrderBy(g => g.Key)
            .Select(g => g.Select(n => pitchOf[n.Letter]).Distinct().OrderBy(p => p).ToArray()).ToList();
    }

    /// <summary>The pitches a window's instrument plays, chord by chord, starting in the middle octave.</summary>
    private static List<int[]> PlayedChords(IReadOnlyList<string> notes, int window)
    {
        var octave = 1;
        var chords = new List<int[]>();
        foreach (var element in notes)
        {
            var pitches = new List<int>();
            foreach (var c in element)
            {
                if (KeyMap.Decode(c) is not { } k || k.Window != window) continue;
                if (KeyMap.ArrangedSwap(c) is { } swap) { octave = Math.Clamp(octave + swap.Step, 0, 2); continue; }
                if (k.Key is "9" or "0") continue; // MIDI note 96 / 97
                pitches.Add(48 + 12 * octave + KeyMap.Keys.ToList().IndexOf(k.Key));
            }
            if (pitches.Count > 0) chords.Add(pitches.Distinct().OrderBy(p => p).ToArray());
        }
        return chords;
    }

    /// <summary>The original encoding of a key string: a 9 / 0 from MIDI note 96 / 97 as the window's 9 / 0 key.</summary>
    private static string AsOriginal(string keys) => string.Concat(keys.Select(c =>
        c >= 320 && KeyMap.Decode(c) is { } k ? KeyMap.Replacements(k.Window).First(r => r.From == k.Key).To[0] : c));

    [Fact]
    public void OnlyTheArrangedOctaveChangesAreTracked()
    {
        // Middle C, then MIDI 96 (a "9" key) with it, then high D: one arranged change up, and the 9 key of note 96.
        var song = CsvSong.Parse(LegacyText.Lines("0, 0, Header, 1, 2, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
            "2, 0, Start_track\n2, 0, Note_on_c, 0, 60, 90\n2, 500, Note_on_c, 0, 62, 90\n2, 500, Note_on_c, 0, 96, 90\n2, 1000, Note_on_c, 0, 74, 90\n2, 1000, End_track\n0, 0, End_of_file\n"));
        var keys = string.Concat(SongArranger.Arrange(song, PortMapping.Parse("22"), "130", 1.0)[2].Notes);
        Assert.Equal("1", Decoded(KeyMap.NoteSwap(1, up: false)));
        Assert.Contains(KeyMap.NoteSwap(1, up: false), keys);                                         // note 96: pressed, not tracked
        Assert.Single(keys.Where(c => KeyMap.ArrangedSwap(c) is { Step: 1 }));                         // the change up to high D
        Assert.DoesNotContain(keys, c => KeyMap.ArrangedSwap(c) is { Step: -1 });
        static string Decoded(char c) => KeyMap.Decode(c)!.Value.Window.ToString(CultureInfo.InvariantCulture);
    }

    // latency = client's input delay minus the host's (the original's Ip<n>GW2 - Pings[1], with Pings[1] = host delay after Sync).
    private static double HostLatency(double[] gw2, int slot) => gw2[slot - 1] - gw2[0];
}
