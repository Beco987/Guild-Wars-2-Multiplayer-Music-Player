using GW2MM.Core.Arrangement;
using GW2MM.Core.Config;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>One-player songs: every track on one instrument, octave by octave.</summary>
public sealed class SingleInstrumentTests
{
    private static string Describe(IReadOnlyList<SingleInstrument.Step> steps) => string.Join(" ", steps.Select(s =>
        s.Swap > 0 ? "up" : s.Swap < 0 ? "down" : "[" + string.Join(",", s.Notes) + "]"));

    private static string Route(int octave, params int[] pitches)
    {
        var o = octave;
        return Describe(SingleInstrument.Route(pitches, ref o)) + " @" + o;
    }

    [Fact]
    public void AChordInOneOctaveNeedsNoChange() => Assert.Equal("[4,7] @1", Route(1, 64, 67));        // E4 G4

    [Fact]
    public void AChordAcrossTwoOctavesChangesOctaveMidChord() =>
        Assert.Equal("[4] down [4] @0", Route(1, 52, 64));                                            // E4, then E3

    [Fact]
    public void AChordAcrossThreeOctavesVisitsAllOfThemWithTheFewestChanges()
    {
        Assert.Equal("[4] down [4] up up [4] @2", Route(1, 52, 64, 76));     // tie: down first (bass before melody)
        Assert.Equal("[4] up [4] up [4] @2", Route(0, 52, 64, 76));           // from the low octave: straight up
        Assert.Equal("[4] down [4] down [4] @0", Route(2, 52, 64, 76));       // from the high octave: straight down
    }

    [Fact]
    public void CsUseTheOctaveThatIsNeededAnyway()
    {
        Assert.Equal("[0,4] @1", Route(1, 60, 64));     // C4 as key 1 of the middle octave
        Assert.Equal("[4,12] @1", Route(1, 64, 72));    // C5 as key 8 of the middle octave
        Assert.Equal("[12] @0", Route(0, 60));          // a lone C4 in the low octave: key 8, no change
        Assert.Equal("up [12] @2", Route(1, 84));        // C6 only exists as the high octave's key 8
    }

    private static string Csv(params (int Track, int Tick, int Pitch)[] notes) =>
        "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        string.Concat(notes.Select(n => $"{n.Track}, {n.Tick}, Note_on_c, 0, {n.Pitch}, 90\n")) + "0, 0, End_of_file\n";

    /// <summary>(time from the part's start in ms, keys pressed) for each element after the empty first one.</summary>
    private static List<(long Time, string Keys)> Presses(SlotPlan plan)
    {
        var result = new List<(long, string)>();
        long t = 0;
        for (var k = 1; k < plan.Notes.Count; k++)
        {
            t += plan.DelaysMs[k - 1];
            result.Add((t, string.Join("+", plan.Notes[k].Select(c => KeyMap.Decode(c)!.Value.Key))));
        }
        return result;
    }

    private static List<(long Time, string Keys)> Play(string octaveDelay, params (int Track, int Tick, int Pitch)[] notes) =>
        Presses(SingleInstrument.Arrange(CsvSong.Parse(Csv(notes).Split('\n')), 1, octaveDelay, 1.0));

    [Fact]
    public void TheOctaveChangeInsideAChordIsFollowedByOctaveDelay()
    {
        // E4 + E3 together at 0, then E3 alone at 1000 (already in the low octave), then E4 at 2000.
        var p = Play("130", (2, 0, 64), (3, 0, 52), (3, 1000, 52), (2, 2000, 64));
        Assert.Equal(new (long, string)[] { (0, "3"), (1, "9"), (131, "3"), (1000, "3"), (1500, "0"), (2000, "3") }, p);
    }

    [Fact]
    public void AnOctaveChangeBeforeAChordGoesIntoTheGapWithOctaveDelayToSpare()
    {
        var roomy = Play("130", (2, 0, 64), (2, 1000, 76));      // E4, then E5: change at the midpoint
        Assert.Equal(new (long, string)[] { (0, "3"), (500, "0"), (1000, "3") }, roomy);
        var medium = Play("130", (2, 0, 64), (2, 200, 76));      // midpoint (100 ms ahead) too late: OctaveDelay ahead
        Assert.Equal(new (long, string)[] { (0, "3"), (70, "0"), (200, "3") }, medium);
    }

    [Fact]
    public void WithoutRoomTheNoteIsDelayedAndTheSongCatchesUp()
    {
        // E4 at 0, E5 at 100 (needs a change: 130 ms), F5 at 200, G5 at 300.
        var p = Play("130", (2, 0, 64), (2, 100, 76), (2, 200, 77), (2, 300, 79));
        Assert.Equal(new (long, string)[] { (0, "3"), (1, "0"), (131, "3"), (200, "4"), (300, "5") }, p);
    }

    [Fact]
    public void TheFirstChordsOctaveChangeComesBeforeIt()
    {
        var p = Play("130", (2, 0, 52), (2, 500, 53), (2, 1000, 55), (2, 1500, 57));
        Assert.Equal((0L, "9"), p[0]);
        Assert.Equal((130L, "3"), p[1]); // E3, OctaveDelay after the change
    }

    [Fact]
    public void SharpsUseTheFKeysLikeTheMultiplayerLoop()
    {
        var p = Play("130", (2, 0, 61), (2, 200, 63), (2, 400, 66), (2, 600, 68), (2, 800, 70)); // C# D# F# G# A#
        Assert.Equal(new[] { "F1", "F2", "F3", "F4", "F5" }, p.Select(x => x.Keys));
    }

    [Fact]
    public void ABlankOctaveDelayWaits150Ms()
    {
        var p = Play("", (2, 0, 64), (3, 0, 52), (2, 1000, 64), (2, 2000, 64));
        Assert.Equal((151L, "3"), p[2]);
    }

    [Fact]
    public void TheHostOrTheTickedPlayersPlayIt()
    {
        Assert.Equal(new[] { 2, 3 }, SingleInstrument.Players(PortMapping.Parse("23,35")));
        Assert.Equal(new[] { 1 }, SingleInstrument.Players(new PortMapping()));
        var song = CsvSong.Parse(Csv((2, 0, 64), (3, 0, 52), (2, 500, 65), (2, 1000, 67)).Split('\n'));
        var plan = SongArranger.Arrange(song, PortMapping.Parse("23,35"), "130", 1.0, singleInstrument: true);
        Assert.True(plan[2].IsActive && plan[3].IsActive);
        Assert.False(plan[1].IsActive || plan[4].IsActive || plan[5].IsActive);
        Assert.All(plan[2].Notes.SelectMany(n => n), c => Assert.Equal(1, KeyMap.Decode(c)!.Value.Window));
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

    /// <summary>A reset waits OctaveDelay, then presses only what each window needs to get back to the middle octave.</summary>
    [Fact]
    public async Task HostResetWaitsOctaveDelayThenReturnsEachWindowToTheMiddleOctave()
    {
        var clock = new OffsetClock(0);
        var keys = new RecordingKeys(clock);
        var tracker = new OctaveTracker(keys);
        using var host = new HostSession(new FakeView(), new SongLibrary(Path.GetTempPath()), () => "60", clock, tracker, new FakeMeter());
        tracker.PressKeys(Key(1, 14) + Key(2, 13) + Key(2, 13) + Key(2, 13) + KeyMap.NoteSwap(3, up: false));
        Assert.Equal((2, 0, 1), (tracker.OctaveOf(1), tracker.OctaveOf(2), tracker.OctaveOf(3)));  // kept within 0..2; note 96 not counted
        var before = clock.NowMs;
        keys.Presses.Clear();
        await host.ResetOctavesAsync();
        Assert.Equal(new[] { (1, "9"), (2, "0") }, keys.All.Select(p => (p.Window, p.Key)));
        Assert.InRange(keys.All[0].Time - before, 60 - 5, 60 + 40);
        Assert.All(Enumerable.Range(1, 8), w => Assert.Equal(1, tracker.OctaveOf(w)));

        // In the middle octave already: nothing to press and no wait.
        keys.Presses.Clear();
        await host.ResetOctavesAsync().WaitAsync(TimeSpan.FromMilliseconds(30));
        Assert.Empty(keys.All);
    }

    private static string Key(int window, int offset) => ((char)(192 + 16 * (window - 1) + offset)).ToString();
}
