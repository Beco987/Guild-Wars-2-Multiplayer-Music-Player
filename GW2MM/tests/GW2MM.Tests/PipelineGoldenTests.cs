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

        // No swap mode given: the default is what the player uses, so this checks the player's octave swaps.
        var plan = SongArranger.Arrange(CsvSong.Parse(lines, allTempoChanges: false), PortMapping.Parse(spec["ip"]), spec["octavedelay"], tempo);
        // The player also keeps every tempo change (a deliberate fix to timing), which only adds empty tempo elements.
        // Octave swaps are worked out in MIDI ticks, so the keys pressed, swaps included, must come out the same.
        var played = SongArranger.Arrange(CsvSong.Parse(lines), PortMapping.Parse(spec["ip"]), spec["octavedelay"], tempo);
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
