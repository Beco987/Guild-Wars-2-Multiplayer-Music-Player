using GW2MM.Core.Arrangement;

namespace GW2MM.Tests;

/// <summary>The improved octave handling (OctaveSwapMode.Improved) against the original's behaviour.</summary>
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
    }
}
