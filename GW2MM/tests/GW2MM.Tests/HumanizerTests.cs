using GW2MM.Core.Arrangement;
using GW2MM.Core.Humanizing;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>Humanized solo play: the melody on the beat, the same octave changes, and only a few ms of timing.</summary>
public sealed class HumanizerTests
{
    private const double Delay = 130;

    /// <summary>A song with tempo 500000, so one tick is one ms.</summary>
    private static CsvSong Song(params (int Track, int Tick, int Pitch)[] notes) => CsvSong.Parse((
        "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        string.Concat(notes.OrderBy(n => n.Tick).Select(n => $"{n.Track}, {n.Tick}, Note_on_c, 0, {n.Pitch}, 90\n")) + "0, 0, End_of_file\n").Split('\n'));

    private static string Name(string keys) => string.Join("+", keys.Select(c => KeyMap.Decode(c)!.Value.Key));

    /// <summary>The presses of a one-player song as (time relative to the first note's score time, key names).</summary>
    private static List<(double Time, string Keys)> Play(CsvSong song, HumanizeLevel level = HumanizeLevel.Off, int seed = 1) =>
        Humanizer.Render(SoloScore.OnePlayer(song, 1.0), Delay, level, new Random(seed)).Select(p => (p.Time, Name(p.Keys))).ToList();

    /// <summary>The plain one-player arrangement's presses as key strings, in order.</summary>
    private static List<string> Plain(CsvSong song) =>
        SingleInstrument.Arrange(song, 1, "130", 1.0).Notes.Where(n => n.Length > 0).ToList();

    [Fact]
    public void AChordThatStartsLowRollsIntoItsMelodyOnTheBeat()
    {
        // C3 alone (the instrument goes down to the low octave), then E3 + E4 at 1000: the route plays E3, goes up,
        // then E4. The plain arrangement starts E3 on the beat, so the melody E4 comes 131 ms late.
        var song = Song((2, 0, 48), (2, 1000, 52), (3, 1000, 64));
        Assert.Equal(new[] { "9", "1", "3", "0", "3" }, Plain(song).Select(Name));
        var p = Play(song);
        Assert.Equal(new[] { "9", "1", "3", "0", "3" }, p.Select(x => x.Keys));
        Assert.Equal(0, p[1].Time, 3);                       // C3 on its beat
        Assert.Equal(1000, p[4].Time, 3);                    // the melody on its beat
        Assert.Equal(1000 - Delay - 1, p[2].Time, 3);        // E3 just before it, as early as the game requires
        Assert.Equal(1000 - Delay, p[3].Time, 3);            // the octave change right after E3
        Assert.True(p[1].Time - p[0].Time >= Delay);         // the first change well ahead of C3
    }

    [Fact]
    public void AChordThatStartsWithItsMelodyKeepsItOnTheBeat()
    {
        // From the middle octave, E4 + E3: the route plays E4, goes down, then E3 (as early as the game allows).
        var p = Play(Song((2, 0, 64), (3, 0, 52), (2, 1000, 52)));
        Assert.Equal(new[] { "3", "9", "3", "3" }, p.Select(x => x.Keys));
        Assert.Equal(new[] { 0, 1, 1 + Delay, 1000 }, p.Select(x => x.Time));
    }

    [Fact]
    public void WithoutRoomTheChordWaitsAndTheSongEasesBackInsteadOfRushing()
    {
        // E4 every 100 ms, then E5 (an octave up) and on up in 100 ms steps. The change needs 130 ms.
        var pitches = new[] { 64, 64, 64, 76, 77, 79, 77, 76, 77, 79, 77, 76 };
        var song = Song(pitches.Select((x, i) => (2, i * 100, x)).ToArray());
        var notes = Play(song).Where(x => x.Keys is not "9" and not "0").Select(x => x.Time).ToList();
        var gaps = notes.Zip(notes.Skip(1), (a, b) => b - a).ToList();
        Assert.Equal(new[] { 100.0, 100 }, gaps.Take(2));
        Assert.Equal(1 + Delay, gaps[2], 3);                                 // waits just long enough
        Assert.All(gaps.Skip(3), g => Assert.InRange(g, 98 - 0.001, 100)); // then 2% faster until back in time
        // The plain arrangement instead squeezes the next note to 69 ms to catch up at once.
        var plain = SingleInstrument.Arrange(song, 1, "130", 1.0);
        Assert.Equal(new long[] { 130, 69 }, plain.DelaysMs.Skip(4).Take(2)); // E4, change, E5 after 130 ms, F5 69 ms later
    }

    [Fact]
    public void TheSameNotesAndOctaveChangesAsThePlainArrangementInTheSameOrder()
    {
        var random = new Random(7);
        for (var round = 0; round < 30; round++)
        {
            var notes = new List<(int, int, int)>();
            var tick = 0;
            for (var i = 0; i < 80; i++)
            {
                tick += new[] { 0, 60, 120, 120, 250, 400 }[random.Next(6)];
                notes.Add((2 + random.Next(3), tick, 36 + random.Next(60)));
            }
            var song = Song(notes.ToArray());
            foreach (var level in Enum.GetValues<HumanizeLevel>())
            {
                var presses = Humanizer.Render(SoloScore.OnePlayer(song, 1.0), Delay, level, new Random(round));
                var inTime = presses.OrderBy(p => p.Time).ToList();
                Assert.Equal(Plain(song), inTime.Select(p => p.Keys));
                for (var k = 0; k + 1 < inTime.Count; k++)
                    Assert.True(inTime[k + 1].Time - inTime[k].Time >= (inTime[k].IsSwap ? Delay : 1) - 1e-9,
                        $"{level}: press {k + 1} only {inTime[k + 1].Time - inTime[k].Time} ms after {Name(inTime[k].Keys)}");
            }
        }
    }

    [Theory]
    [InlineData(HumanizeLevel.Subtle, 1.0)]
    [InlineData(HumanizeLevel.Natural, 2.0)]
    [InlineData(HumanizeLevel.Loose, 4.0)]
    public void TimingVariesByOnlyAFewCorrelatedMilliseconds(HumanizeLevel level, double capMs)
    {
        // Two windows, a note each every 250 ms, no octave changes.
        var song = Song(Enumerable.Range(0, 400).SelectMany(i => new[] { (2, i * 250, 60 + i % 7), (3, i * 250, 64 + i % 5) }).ToArray());
        var mapping = new PortMapping();
        mapping[2, 2] = mapping[2, 3] = true;
        var part = SongArranger.Arrange(song, mapping, "130", 1.0)[2];
        var presses = Humanizer.Render(SoloScore.FromPart(song, part, 1.0), Delay, level, new Random(3));
        var profile = HumanizeProfile.For(level);
        foreach (var window in presses.GroupBy(p => p.Window))
        {
            var deviation = window.OrderBy(p => p.ScoreMs).Select(p => p.Time - p.ScoreMs).ToList();
            Assert.True(deviation.Max() - deviation.Min() <= 2 * 2.5 * capMs + 1e-9);
            Assert.True(deviation.Select(Math.Abs).Max() <= 2.5 * capMs + 2.5 * profile.PlayerOffsetMs + 1e-9);
            var mean = deviation.Average();
            var lag1 = deviation.Zip(deviation.Skip(1), (a, b) => (a - mean) * (b - mean)).Average() / deviation.Average(d => (d - mean) * (d - mean));
            Assert.True(lag1 > 0.2, $"lag-1 correlation {lag1}");       // a drift, not independent jitter
        }
        Assert.NotEqual(presses.Where(p => p.Window == 1).Select(p => p.Time - p.ScoreMs),
            presses.Where(p => p.Window == 2).Select(p => p.Time - p.ScoreMs));
    }

    [Fact]
    public void ANotePressedTogetherWithItsOctaveChangeGetsOctaveDelay()
    {
        // A two-window part as the arrangement can leave it: window 1 presses "0" and "1" in the same element.
        var song = Song((2, 0, 64), (2, 500, 76), (2, 1000, 77), (3, 0, 60), (3, 500, 62), (3, 1000, 64));
        string K(int window, params int[] offsets) => string.Concat(offsets.Select(o => (char)(192 + 16 * (window - 1) + o)));
        var part = new SlotPlan(2, new[] { "", K(1, 4) + K(2, 0), K(1, 14, 4) + K(2, 2), K(1, 5) + K(2, 4) }, new long[] { 0, 500, 500, 0 }, true)
        {
            Ticks = new double?[] { 0, 0, 500, 1000 },
        };
        var presses = Humanizer.Render(SoloScore.FromPart(song, part, 1.0), Delay, HumanizeLevel.Off, new Random(1));
        var w1 = presses.Where(p => p.Window == 1).OrderBy(p => p.Time).ToList();
        Assert.Equal(new[] { "3", "0", "3", "4" }, w1.Select(p => Name(p.Keys)));
        Assert.Equal(500, w1[2].Time, 3);                     // the note keeps its time: the change moved into the gap
        Assert.True(w1[2].Time - w1[1].Time >= Delay);
        Assert.Equal(new[] { 0.0, 500, 1000 }, presses.Where(p => p.Window == 2).Select(p => p.Time));
    }

    [Fact]
    public void TheSameSeedPlaysTheSameWay()
    {
        var song = Song(Enumerable.Range(0, 50).Select(i => (2, i * 150, 50 + i % 30)).ToArray());
        var a = Humanizer.Perform(SoloScore.OnePlayer(song, 1.0), Delay, HumanizeLevel.Natural, new Random(9), 1000);
        var b = Humanizer.Perform(SoloScore.OnePlayer(song, 1.0), Delay, HumanizeLevel.Natural, new Random(9), 1000);
        Assert.Equal(a, b);
        Assert.True(a.Min(e => e.DueMs!.Value) >= 1000);
    }
}

/// <summary>The phrased levels: meter, phrases and the expressive time map.</summary>
public sealed class ExpressionTests
{
    /// <summary>Notes as (track, tick, pitch, length in ticks); tempo 500000 and PPQ 480, so a tick is a ms and a beat 480 ms.</summary>
    private static CsvSong Song(IEnumerable<(int Track, int Tick, int Pitch, int Length)> notes, string extra = "") => CsvSong.Parse((
        "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n" + extra + "1, 0, End_track\n" +
        string.Concat(notes.SelectMany(n => new[] { (n.Track, n.Tick, $"Note_on_c, 0, {n.Pitch}, 90"), (n.Track, n.Tick + n.Length, $"Note_off_c, 0, {n.Pitch}, 0") })
            .OrderBy(e => e.Item2).Select(e => $"{e.Item1}, {e.Item2}, {e.Item3}\n")) + "0, 0, End_of_file\n").Split('\n'));

    [Fact]
    public void TheParserReadsTheMeterAndWhenNotesEnd()
    {
        var song = Song(new[] { (2, 0, 60, 240), (2, 480, 62, 480) }, "1, 0, Time_signature, 6, 3, 24, 8\n1, 2880, Time_signature, 3, 2, 24, 8\n");
        Assert.Equal(480, song.Division);
        Assert.Equal(new[] { (0.0, 6, 8), (2880.0, 3, 4) }, song.TimeSignatures);
        Assert.Equal(new double?[] { 240, 960 }, song.Notes.Select(n => n.EndTick));
        // Velocity 0 also ends a note; the original fields are unchanged.
        var v0 = CsvSong.Parse("0, 0, Header, 1, 2, 96\n2, 0, Note_on_c, 0, 60, 90\n2, 48, Note_on_c, 0, 60, 0\n".Split('\n'));
        Assert.Equal(96, v0.Division);
        Assert.Equal(48, v0.Notes.Single().EndTick);
        Assert.Equal(new[] { "0" }, v0.TrackTimes[2]);
    }

    [Fact]
    public void MetricWeightFollowsTheBarAndBeat()
    {
        var common = new Meter(480, Array.Empty<(double, int, int)>());       // 4/4
        Assert.Equal(new[] { 1, 0.15, 0.25, 0.4, 0.6, 0.75, 0.6, 1 }, new double[] { 0, 30, 120, 240, 480, 960, 1440, 1920 }.Select(common.Weight));
        var compound = new Meter(480, new[] { (0.0, 6, 8), (2880.0, 3, 4) }); // 6/8 (two dotted-quarter beats), then 3/4
        Assert.Equal(new[] { 1, 0.4, 0.6, 1, 1, 0.6, 1 }, new double[] { 0, 240, 720, 1440, 2880, 3360, 4320 }.Select(compound.Weight));
        Assert.Equal(720, compound.BeatTicks(100));
        Assert.Equal(480, compound.BeatTicks(3000));
    }

    private static (SoloScore Score, List<Onset> Onsets, List<Phrase> Phrases) Analyse(CsvSong song)
    {
        var score = SoloScore.OnePlayer(song, 1.0);
        var onsets = Phrasing.Onsets(score);
        return (score, onsets, Phrasing.Phrases(score, onsets));
    }

    [Fact]
    public void PhrasesEndAtLongNotesAndRests()
    {
        // Four phrases: seven eighths (240 ms apart) leading to a long note, then a rest.
        var notes = new List<(int, int, int, int)>();
        var tick = 0;
        for (var phrase = 0; phrase < 4; phrase++)
        {
            for (var i = 0; i < 7; i++) { notes.Add((2, tick, 60 + i, 200)); tick += 240; }
            notes.Add((2, tick, 67, 900)); tick += 1920;
        }
        var (_, onsets, phrases) = Analyse(Song(notes));
        Assert.Equal(new[] { 0, 8, 16, 24 }, phrases.Select(p => p.First));
        Assert.All(phrases.SkipLast(1), p => Assert.True(p.Strength > 0.8));
        Assert.Equal(1, phrases[^1].Strength);
        Assert.Equal(32, onsets.Count);
        Assert.Equal(1920 - 900, onsets[7].RestAfterMs);
    }

    [Fact]
    public void ASteadyFigureIsSplitAtBarLines()
    {
        // 50 s of even sixteenths (120 ms): no boundary from the music itself.
        var (score, onsets, phrases) = Analyse(Song(Enumerable.Range(0, 420).Select(i => (2, i * 120, 60 + i % 4 * 4, 120))));
        Assert.True(phrases.Count >= 5);
        foreach (var p in phrases)
        {
            var end = p.Last + 1 < onsets.Count ? onsets[p.Last + 1].Ms : score.EndMs;
            Assert.InRange(end - onsets[p.First].Ms, Phrasing.MinPhraseMs, Phrasing.MaxPhraseMs);
            Assert.True(score.Meter.IsDownbeat(onsets[p.First].Tick));
        }
    }

    private static readonly HumanizeProfile Plain = HumanizeProfile.For(HumanizeLevel.Off);

    [Fact]
    public void ThePhraseArchShapesTempoWithoutChangingThePhrasesLength()
    {
        var (score, onsets, phrases) = Analyse(Song(Enumerable.Range(0, 240).Select(i => (2, i * 120, 60 + i % 4 * 4, 120))));
        var k = Expression.Factors(score, onsets, phrases, Plain with { PhraseArch = 0.06 }, new Random(1));
        foreach (var p in phrases.SkipLast(1))
        {
            var span = Enumerable.Range(p.First, p.Last - p.First + 1).ToList();
            var stretch = span.Sum(i => 120 * k[i]) / (span.Count * 120);
            Assert.InRange(stretch, -0.003, 0.003);               // same length
            Assert.True(k[p.First] > 0 && k[p.Last] > 0);       // slower at the edges
            Assert.True(k[p.Last] > k[p.First]);                  // and most at the end
            Assert.True(span.Min(i => k[i]) < 0);                 // moving forward in between
        }
    }

    [Fact]
    public void PhraseEndsBreatheAndTheSongSlowsAtTheEnd()
    {
        var notes = new List<(int, int, int, int)>();
        var tick = 0;
        for (var phrase = 0; phrase < 4; phrase++)
        {
            for (var i = 0; i < 11; i++) { notes.Add((2, tick, 60 + i, 200)); tick += 240; }
            notes.Add((2, tick, 72, 900)); tick += 1440;
        }
        var (score, onsets, phrases) = Analyse(Song(notes));
        var end = Expression.Factors(score, onsets, phrases, Plain with { PhraseEnd = 0.1 }, new Random(1));
        foreach (var p in phrases.SkipLast(1))
        {
            Assert.InRange(end[p.Last], 0.08, 0.1);
            Assert.Equal(0.35 * end[p.Last], end[p.Last - 1], 9);
        }
        var ritard = Expression.Factors(score, onsets, phrases, Plain with { FinalTempo = 0.8 }, new Random(1));
        var slowing = ritard.Select((x, i) => (x, i)).Where(x => x.x > 0).Select(x => x.x).ToList();
        Assert.True(slowing.Count >= 3);
        Assert.Equal(slowing.OrderBy(x => x), slowing);           // ever slower
        Assert.InRange(slowing[^1], 0.15, 1 / 0.8 - 1);           // towards 80% of the tempo
        Assert.All(ritard.Take(onsets.Count - 1 - slowing.Count), x => Assert.Equal(0, x));
    }

    [Fact]
    public void TheExpressiveLevelsStayGentle()
    {
        var (score, onsets, phrases) = Analyse(Song(Enumerable.Range(0, 400).Select(i => (2, i * 240, 55 + (i * 7) % 24, 200))));
        foreach (var (level, typical) in new[] { (HumanizeLevel.Phrased, 0.03), (HumanizeLevel.Expressive, 0.06), (HumanizeLevel.Rubato, 0.1) })
        {
            var k = Expression.Factors(score, onsets, phrases, HumanizeProfile.For(level), new Random(4));
            var body = k.Take(k.Length - 12).OrderBy(Math.Abs).ToList();   // all but the closing ritardando
            Assert.True(Math.Abs(body[(int)(0.9 * body.Count)]) <= typical, $"{level}: 90% within {body[(int)(0.9 * body.Count)]:P1}");
            var map = Expression.TimeMap(score, onsets, phrases, HumanizeProfile.For(level), new Random(4));
            Assert.InRange((map[^1] - map[0]) / (onsets[^1].Ms - onsets[0].Ms), 1, 1.03);
        }
    }
}

/// <summary>A humanized one-player song played solo.</summary>
[Collection(TimingCollection.Name)]
public sealed class HumanizedSoloTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-human-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private List<string> PlaySolo(HumanizeLevel level)
    {
        // Chords across two octaves and quick octave changes, on one instrument.
        var notes = new[] { (0, 64), (0, 52), (150, 67), (300, 76), (400, 55), (400, 79), (550, 60), (700, 72) };
        var csv = "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
            string.Concat(notes.Select((n, i) => $"{2 + i % 2}, {n.Item1}, Note_on_c, 0, {n.Item2}, 90\n")) + "0, 0, End_of_file\n";
        Directory.CreateDirectory(Path.Combine(_dir, "Csv"));
        File.WriteAllText(Path.Combine(_dir, "Csv", "Piano.csv"), csv);
        File.WriteAllText(Path.Combine(_dir, "Data.csv"), "Piano, 1, 0, x\n");
        var clock = new OffsetClock(0);
        var keys = new RecordingKeys(clock);
        var view = new FakeView();
        using var solo = new SoloSession(view, new SongLibrary(_dir), () => new[] { 1 }, () => "130", () => 1, clock, keys,
            tailMs: 50, gapMs: 50, humanize: () => level, random: new Random(5));
        solo.PlaySong("Piano");
        SpinWait.SpinUntil(() => keys.Presses.Count(p => p.Key is not "9" and not "0") >= notes.Length, 5000);
        Thread.Sleep(300);
        Assert.Empty(view.Problems);
        return keys.All.Select(p => p.Key).ToList();
    }

    [Fact]
    public void HumanizedOnePlayerSongsPlayTheSameNotesAndOctaveChanges()
    {
        var plain = PlaySolo(HumanizeLevel.Off);
        Assert.Equal(8, plain.Count(k => k is not "9" and not "0"));
        Assert.Equal(plain, PlaySolo(HumanizeLevel.Natural));
    }
}
