using GW2MM.Core.Arrangement;
using GW2MM.Core.Config;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>One-player songs: every track merged into one instrument.</summary>
public sealed class SingleInstrumentTests
{
    [Theory]
    [InlineData(new[] { 60, 64, 67 }, new[] { 60, 64, 67 })]          // already in one octave: unchanged
    [InlineData(new[] { 48, 64, 67 }, new[] { 60, 64, 67 })]          // bass C3 folded up into the melody's octave
    [InlineData(new[] { 36, 76 }, new[] { 72, 76 })]                  // C2 (below range) into E5's octave
    [InlineData(new[] { 55, 72 }, new[] { 67, 72 })]                  // top note C5 = the middle octave's top C (key 8)
    [InlineData(new[] { 60, 84 }, new[] { 72, 84 })]                  // C6, the high octave's top C
    [InlineData(new[] { 98 }, new[] { 74 })]                          // D7 (above range) folded down
    [InlineData(new[] { 50, 62, 74 }, new[] { 74 })]                  // the same note in three octaves becomes one
    public void ChordsAreFoldedIntoTheOctaveOfTheirHighestNote(int[] chord, int[] expected) =>
        Assert.Equal(expected, SingleInstrument.Fold(chord));

    private const string TwoTracks = "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        "2, 0, Start_track\n2, 0, Note_on_c, 0, 72, 90\n2, 200, Note_on_c, 0, 74, 90\n2, 400, Note_on_c, 0, 76, 90\n2, 600, Note_on_c, 0, 77, 90\n2, 600, End_track\n" +
        "3, 0, Start_track\n3, 0, Note_on_c, 0, 36, 90\n3, 400, Note_on_c, 0, 43, 90\n3, 400, End_track\n0, 0, End_of_file\n";

    [Fact]
    public void MergeCombinesAllTracksIntoTrackOneChordByChord()
    {
        var merged = SingleInstrument.Merge(CsvSong.Parse(TwoTracks.Split('\n')));
        Assert.Empty(merged.TrackNotes[2]);
        Assert.Empty(merged.TrackNotes[3]);
        Assert.Equal(new[] { "0", "0", "200", "400", "400", "600" }, merged.TrackTimes[1]);
        // C5 + C2 -> C5 is the middle octave's top C, C2 -> C4; E5 + G2 -> E5 + G5.
        Assert.Equal(new int?[] { 60, 72, 74, 76, 79, 77 }, merged.TrackPitches[1]);
        Assert.Equal(new[] { "B500000" }, merged.TempoMarkers);
    }

    [Fact]
    public void TheMergedPartGoesToEverySlotWithATrackTickedOrElseTheHost()
    {
        var m = SingleInstrument.Mapping(PortMapping.Parse("23,35"));
        Assert.True(m[2, 1] && m[3, 1]);
        Assert.False(m[1, 1] || m[2, 3] || m[3, 5]);
        Assert.True(SingleInstrument.Mapping(new PortMapping())[1, 1]);
    }

    [Fact]
    public void ArrangedForOneWindowWithEveryTracksNotes()
    {
        var song = CsvSong.Parse(TwoTracks.Split('\n'));
        var plan = SongArranger.Arrange(song, PortMapping.Parse("12,13"), "130", 1.0, singleInstrument: true);
        var keys = plan[1].Notes.SelectMany(n => n).Select(c => KeyMap.Decode(c)).Where(k => k is not null).ToList();
        Assert.All(keys, k => Assert.Equal(1, k!.Value.Window));   // all in Player1
        Assert.Equal(6, keys.Count(k => k!.Value.Key is not "9" and not "0")); // 6 notes from both tracks
    }

    [Fact]
    public void LibraryKnowsOnePlayerSongs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw2mm-si-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Data.csv"), "Solo Piece, 1, 0, x\nBand Piece, 4, 0, y\nOdd, , 0, z\n");
            var lib = new SongLibrary(dir);
            Assert.True(lib.IsSingleInstrument("Solo Piece"));
            Assert.False(lib.IsSingleInstrument("Band Piece"));
            Assert.False(lib.IsSingleInstrument("Odd"));
            Assert.False(lib.IsSingleInstrument("Unknown"));
        }
        finally { Directory.Delete(dir, true); }
    }
}

[Collection(TimingCollection.Name)]
public sealed class OctaveResetTests
{
    [Theory]
    [InlineData("130", 130)]
    [InlineData("", 150)]
    [InlineData("0", 150)]
    [InlineData("abc", 150)]
    public void ResetDelayIsOctaveDelayOr150(string setting, int expected) => Assert.Equal(expected, AppSettings.OctaveResetDelay(setting));

    /// <summary>The host's octave reset waits OctaveDelay between rounds (the original always waited 150 ms).</summary>
    [Fact]
    public async Task HostResetWaitsOctaveDelayBetweenOctaveChanges()
    {
        var clock = new OffsetClock(0);
        var keys = new RecordingKeys(clock);
        using var host = new HostSession(new FakeView(), new SongLibrary(Path.GetTempPath()), () => "60", clock, keys, new FakeMeter());
        await host.ResetOctavesAsync();
        var rounds = keys.All.Where(p => p.Window == 1).ToList();
        Assert.Equal(new[] { "9", "9", "0" }, rounds.Select(p => p.Key));
        Assert.InRange(rounds[1].Time - rounds[0].Time, 60 - 5, 60 + 40);
        Assert.InRange(rounds[2].Time - rounds[1].Time, 60 - 5, 60 + 40);
    }
}
