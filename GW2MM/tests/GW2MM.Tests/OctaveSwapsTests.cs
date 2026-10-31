using GW2MM.Core.Arrangement;
using GW2MM.Core.Playback;

namespace GW2MM.Tests;

/// <summary>
/// The octave handling beyond the original's: the player's two changes for a jump between the low and high octave
/// (OctaveSwapMode.Player), and the test-only OctaveSwapMode.Improved, against the original's behaviour.
/// </summary>
public sealed class OctaveSwapsTests
{
    private static readonly Func<double, double> OneMsPerTick = _ => 1;

    [Fact]
    public void AJumpBetweenTheLowAndHighOctaveGetsTwoChangesInsteadOfLosingTheNote()
    {
        var chords = new[] { "z", "w", "e", "x" }; // low C... then high D, high E, then low D
        Assert.Equal(new[] { "9z", "", "", "x" }, SongFunctions.AddSwaps(chords));        // original: high notes dropped
        Assert.Equal(new[] { "9z", "00w", "e", "99x" }, OctaveSwaps.AddSwaps(chords));
    }

    [Fact]
    public void EverythingElseMatchesTheOriginal()
    {
        var chords = new[] { "a", "z", "a", "q", "w", "q", "a", "s", "zd", "", "aq" };
        Assert.Equal(SongFunctions.AddSwaps(chords), OctaveSwaps.AddSwaps(chords));
    }

    private static (List<string> Chords, List<string> Times) Place(string[] chords, string[] times, double delayMs, Func<double, double>? msPerTick = null) =>
        OctaveSwaps.Place(chords, times, delayMs, msPerTick ?? OneMsPerTick);

    [Fact]
    public void WithPlentyOfRoomTheChangeStaysAtTheMidpointLikeTheOriginal()
    {
        var (chords, times) = Place(new[] { "a", "0w" }, new[] { "0", "1000" }, 130);
        Assert.Equal(new[] { "a", "0", "w" }, chords);
        Assert.Equal(new[] { "0", "500", "1000" }, times);
    }

    [Fact]
    public void WithLessRoomTheChangeGetsTheFullOctaveDelayRatherThanHalfTheGap()
    {
        var (chords, times) = Place(new[] { "a", "0w" }, new[] { "0", "200" }, 130);
        Assert.Equal(new[] { "a", "0", "w" }, chords);
        Assert.Equal(new[] { "0", "70", "200" }, times); // 130 ms ahead; the original's midpoint (100) gave only 100 ms
    }

    [Fact]
    public void WithoutEnoughRoomTheChangeGoesRightAfterThePreviousChord()
    {
        // The original keeps the change glued to the note (pressed together), so the note sounds in the old octave.
        Assert.Equal(new[] { "a", "0w" }, SongFunctions.OpportunisticSwaps(new[] { "a", "0w" }, new[] { "0", "50" }, "130").Num);
        var (chords, times) = Place(new[] { "a", "0w" }, new[] { "0", "50" }, 130);
        Assert.Equal(new[] { "a", "0", "w" }, chords);
        Assert.Equal(new[] { "0", "1", "50" }, times);
    }

    [Fact]
    public void NoRoomAtAllKeepsTheChangeWithItsNote()
    {
        var (chords, times) = Place(new[] { "a", "0w" }, new[] { "0", "1" }, 130);
        Assert.Equal(new[] { "a", "0w" }, chords);
        Assert.Equal(new[] { "0", "1" }, times);
    }

    [Fact]
    public void TwoChangesAreSpacedByOctaveDelay()
    {
        var (roomy, roomyTimes) = Place(new[] { "z", "00w" }, new[] { "0", "1000" }, 130);
        Assert.Equal(new[] { "z", "0", "0", "w" }, roomy);
        Assert.Equal(new[] { "0", "333", "666", "1000" }, roomyTimes);

        var (_, tightTimes) = Place(new[] { "z", "00w" }, new[] { "0", "300" }, 130);
        Assert.Equal(new[] { "0", "40", "170", "300" }, tightTimes); // 130 ms apart, the last 130 ms before the note
    }

    [Fact]
    public void OctaveDelayIsMillisecondsAtTheSongsTempo()
    {
        // 60 bpm (tempo 1 000 000): 2 ms per tick. 100 ticks = 200 ms, enough for 130 ms; the original compared
        // 100 (ticks) with 130 and kept the change glued to the note.
        Assert.Equal(new[] { "a", "0w" }, SongFunctions.OpportunisticSwaps(new[] { "a", "0w" }, new[] { "0", "100" }, "130").Num);
        var (chords, times) = Place(new[] { "a", "0w" }, new[] { "0", "100" }, 130, _ => 2);
        Assert.Equal(new[] { "a", "0", "w" }, chords);
        Assert.Equal(new[] { "0", "35", "100" }, times); // 65 ticks = 130 ms ahead
    }

    [Fact]
    public void MsPerTickFollowsTempoChangesAndTheTempoModifier()
    {
        var song = CsvSong.Parse(new[]
        {
            // (Like the original parser, only tempo changes after the first note in the file are kept.)
            "0, 0, Header, 1, 2, 480", "1, 0, Start_track", "1, 0, Tempo, 500000", "1, 0, End_track",
            "2, 0, Start_track", "2, 0, Note_on_c, 0, 60, 90", "2, 1000, Tempo, 250000", "2, 2000, Note_on_c, 0, 62, 90", "2, 2000, End_track", "0, 0, End_of_file",
        });
        var ms = OctaveSwaps.MsPerTick(song, 1.0);
        Assert.Equal(1.0, ms(500));
        Assert.Equal(0.5, ms(1500));
        Assert.Equal(1.1, OctaveSwaps.MsPerTick(song, 1.1)(0), 6);
    }

    [Fact]
    public void ABlankOctaveDelaySplitsAtTheMidpointLikeTheOriginal()
    {
        var (chords, times) = Place(new[] { "a", "0w" }, new[] { "0", "200" }, 0);
        Assert.Equal(new[] { "a", "0", "w" }, chords);
        Assert.Equal(new[] { "0", "100", "200" }, times);
    }

    [Fact]
    public void TheArrangementKeepsNotesAcrossALowToHighJump()
    {
        // One track: low C (48), then high D (74) 500 ms later.
        var song = CsvSong.Parse(new[]
        {
            "0, 0, Header, 1, 2, 480", "1, 0, Start_track", "1, 0, Tempo, 500000", "1, 0, End_track",
            "2, 0, Start_track", "2, 0, Note_on_c, 0, 48, 90", "2, 500, Note_on_c, 0, 74, 90", "2, 1000, Note_on_c, 0, 74, 90", "2, 1000, End_track", "0, 0, End_of_file",
        });
        var mapping = PortMapping.Parse("22");
        string Keys(OctaveSwapMode mode) => string.Concat(SongArranger.Arrange(song, mapping, "130", 1.0, mode)[2].Notes
            .SelectMany(n => n).Select(c => KeyMap.Decode(c)?.Key ?? ""));
        Assert.Equal("91", Keys(OctaveSwapMode.Original));        // the two high D's are lost
        Assert.Equal("910022", Keys(OctaveSwapMode.Improved));   // low C, up, up, D, D
        Assert.Equal("910022", Keys(OctaveSwapMode.Player));     // the same keys in the player's mode
    }

    [Fact]
    public void TheLiveTimerModeLeavesEachOctaveChangeInItsChord()
    {
        // One track: middle C, then high D 1000 ticks later (room for the original's opportunistic swap).
        var song = CsvSong.Parse(new[]
        {
            "0, 0, Header, 1, 2, 480", "1, 0, Start_track", "1, 0, Tempo, 500000", "1, 0, End_track",
            "2, 0, Start_track", "2, 0, Note_on_c, 0, 60, 90", "2, 1000, Note_on_c, 0, 74, 90", "2, 1000, End_track", "0, 0, End_of_file",
        });
        var mapping = PortMapping.Parse("22");
        List<string> Chords(OctaveSwapMode mode) => SongArranger.Arrange(song, mapping, "130", 1.0, mode)[2].Notes
            .Where(n => n.Length > 0).Select(n => string.Concat(n.Select(c => KeyMap.Decode(c)?.Key ?? ""))).ToList();
        Assert.Equal(new[] { "1", "0", "2" }, Chords(OctaveSwapMode.Original));   // the change moved to its own element
        Assert.Equal(new[] { "1", "02" }, Chords(OctaveSwapMode.LiveTimer));      // pressed early by playback instead
    }

    // ------------------------------------------------------------------ the player's two changes (OctaveSwapMode.Player)

    [Fact]
    public void TwoChangesWithPlentyOfRoomGoEvenlyThroughTheGap() =>
        Assert.Equal((333L, 666L), OctaveSwaps.PlaceTwo(0, 1000, null, 100));

    [Fact]
    public void TwoChangesWithLessRoomGoOctaveDelayApartAndOctaveDelayBeforeTheNote() =>
        Assert.Equal((50L, 150L), OctaveSwaps.PlaceTwo(0, 250, null, 100));

    [Fact]
    public void TwoChangesWithoutEnoughRoomShareItEvenlyAndNeverMoveTheNote()
    {
        Assert.Equal((1L, 60L), OctaveSwaps.PlaceTwo(0, 120, null, 100));   // 59 apart and 60 before the note, not 100
        Assert.Null(OctaveSwaps.PlaceTwo(0, 2, null, 100));                 // no room for two presses: both go with the note
    }

    [Fact]
    public void ARecentOctaveChangePushesTheFirstOfTwoLater()
    {
        // A change was pressed with the previous chord (at 0): the steps from it to the note are shared evenly.
        Assert.Equal((83L, 166L), OctaveSwaps.PlaceTwo(0, 250, 0, 100));
        // One long before makes no difference.
        Assert.Equal((50L, 150L), OctaveSwaps.PlaceTwo(0, 250, -500, 100));
    }

    [Fact]
    public void HighEToLowEAndBackGetsTwoChangesBeforeEachNote()
    {
        var chords = OctaveSwaps.AddSwaps(new[] { "e", "c", "e" });   // high E, low E, high E
        Assert.Equal(new[] { "0e", "", "e" }, SongFunctions.AddSwaps(new[] { "e", "c", "e" }));   // the original drops the low E
        Assert.Equal(new[] { "0e", "99c", "00e" }, chords);
        var placed = OctaveSwaps.PlaceForPlayer(chords, new[] { "0", "1000", "1300", "" }, "100", _ => 1);
        Assert.Equal(new[] { "0e", "9", "9", "c", "0", "0", "e" }, placed.Num);
        Assert.Equal(new[] { "0", "333", "666", "1000", "1100", "1200", "1300" }, placed.Alpha);
    }

    [Fact]
    public void TwoChangesUseOctaveDelayInMillisecondsAtTheSongsTempo()
    {
        var chords = new[] { "e", "99c" };
        var times = new[] { "0", "120", "" };
        Assert.Equal(new[] { "0", "1", "60", "120" }, OctaveSwaps.PlaceForPlayer(chords, times, "100", _ => 1).Alpha);  // 100 ticks: no room
        Assert.Equal(new[] { "0", "20", "70", "120" }, OctaveSwaps.PlaceForPlayer(chords, times, "100", _ => 2).Alpha);  // 2 ms a tick: 50 ticks
    }

    [Fact]
    public void OneChangeIsPlacedExactlyAsTheOriginal()
    {
        // No jump between the low and high octave: the player's placement is the original's, change for change
        // (including a blank or zero OctaveDelay, and a 9 key from MIDI note 96).
        var letters = new[] { "a", "w", "e", "s", "z", "a", "s", "q", "w", "9", "s" };
        var times = new[] { "0", "40", "400", "420", "900", "901", "1300", "1350", "2000", "2100", "2400", "" };
        var chords = SongFunctions.AddSwaps(letters);
        Assert.Equal(chords, OctaveSwaps.AddSwaps(letters));
        foreach (var delay in new[] { "130", "0", "", "1000", "40" })
        {
            var original = SongFunctions.OpportunisticSwaps(chords, times, delay);
            var player = OctaveSwaps.PlaceForPlayer(chords, times, delay, _ => 1);
            Assert.Equal(original.Num, player.Num);
            Assert.Equal(original.Alpha, player.Alpha);
        }
    }

    [Fact]
    public void HighEToLowEPlaysInTheModesThePlayerUses()
    {
        // High E, low E 1000 ms later, high E 1000 ms after that.
        var song = CsvSong.Parse(new[]
        {
            "0, 0, Header, 1, 2, 480", "1, 0, Start_track", "1, 0, Tempo, 500000", "1, 0, End_track",
            "2, 0, Start_track", "2, 0, Note_on_c, 0, 76, 90", "2, 1000, Note_on_c, 0, 52, 90", "2, 2000, Note_on_c, 0, 76, 90", "2, 2000, End_track", "0, 0, End_of_file",
        });
        List<(string Keys, double At)> Played(OctaveSwapMode mode)
        {
            var part = SongArranger.Arrange(song, PortMapping.Parse("22"), "130", 1.0, mode)[2];
            var times = Schedule.AbsoluteTimes(part, 0, 0);   // element k + 1 is pressed at times[k]
            return Enumerable.Range(0, times.Count).Where(k => k + 1 < part.Notes.Count && part.Notes[k + 1].Length > 0)
                .Select(k => (string.Concat(part.Notes[k + 1].Select(c => KeyMap.Decode(c)!.Value.Key)), times[k] - Schedule.StartDelayMs)).ToList();
        }
        Assert.Equal(new[] { ("03", 0.0), ("3", 2000.0) }, Played(OctaveSwapMode.Original));                     // the low E is lost
        Assert.Equal(new[] { ("03", 0.0), ("9", 333.0), ("9", 666.0), ("3", 1000.0), ("0", 1333.0), ("0", 1666.0), ("3", 2000.0) },
            Played(OctaveSwapMode.Player));                                                                       // what the player uses
        Assert.Equal(new[] { ("03", 0.0), ("993", 1000.0), ("003", 2000.0) }, Played(OctaveSwapMode.LiveTimer)); // pressed early by playback
    }
}
