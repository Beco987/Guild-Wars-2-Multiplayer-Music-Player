using GW2MM.Core.Arrangement;
using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>Humanized solo playback: natural timing without extra octave changes or notes in the wrong octave.</summary>
public sealed class HumanizerTests
{
    private const double Delay = 130;
    private const int Down = 13, Up = 14;

    /// <summary>Window <paramref name="w"/>'s key characters for the given KeyMap offsets.</summary>
    private static string K(int w, params int[] offsets) => string.Concat(offsets.Select(o => (char)(192 + 16 * (w - 1) + o)));

    /// <summary>Each window's presses in time order (keys pressed together keep their order): (time, key name).</summary>
    private static Dictionary<int, List<(double Time, string Key)>> ByWindow(IEnumerable<(double Time, string Keys)> events)
    {
        var result = new Dictionary<int, List<(double, string)>>();
        foreach (var (time, keys) in events)
            foreach (var c in keys)
                if (KeyMap.Decode(c) is { } k)
                {
                    if (!result.TryGetValue(k.Window, out var list)) result[k.Window] = list = new List<(double, string)>();
                    list.Add((time, k.Key));
                }
        return result.ToDictionary(r => r.Key, r => r.Value.OrderBy(p => p.Item1).ToList());
    }

    private static IEnumerable<(double, string)> Out(IEnumerable<PlaybackEvent> events) => events.Select(e => (e.DueMs!.Value, e.Keys));

    /// <summary>A window's keys as its octave changes and, between them, the notes played in each octave (sorted).</summary>
    private static List<string> Segments(IEnumerable<string> keys)
    {
        var result = new List<string>();
        var notes = new List<string>();
        void Flush()
        {
            if (notes.Count > 0) result.Add(string.Join("+", notes.OrderBy(n => n, StringComparer.Ordinal)));
            notes.Clear();
        }
        foreach (var key in keys)
        {
            if (key is "9" or "0") { Flush(); result.Add(key); }
            else notes.Add(key);
        }
        Flush();
        return result;
    }

    /// <summary>
    /// A two-window song with octave changes in the gaps, a change pressed together with its note (as the arrangement
    /// leaves it when there is no room), a fast run, chords and a two-octave jump.
    /// </summary>
    private static List<(double Time, string Keys)> Song()
    {
        var song = new List<(double, string)> { (0, K(1, 0, 4, 7) + K(2, 0)) };
        for (var i = 1; i <= 40; i++)
        {
            var t = i * 120.0;
            var keys = K(1, i % 12);
            if (i % 7 == 0) keys = K(1, Up) + K(1, 2, 5);                    // octave change glued to a chord
            if (i % 11 == 0) song.Add((t - 60, K(1, Down)));                   // change in the gap, 60 ms ahead
            if (i % 2 == 0) keys += K(2, i % 5 == 0 ? Down : 4) + K(2, 7);    // window 2: notes and changes
            if (i == 30) keys += K(2, Up, Up, 0);                              // a two-octave jump with a note
            song.Add((t, keys));
        }
        return song;
    }

    public static IEnumerable<object[]> LevelsAndSeeds() =>
        from level in new[] { HumanizeLevel.Off, HumanizeLevel.Subtle, HumanizeLevel.Natural, HumanizeLevel.Loose }
        from seed in new[] { 1, 2, 3, 42, 1234 }
        select new object[] { level, seed };

    [Theory]
    [MemberData(nameof(LevelsAndSeeds))]
    public void EveryOctaveChangeHasOctaveDelayBeforeTheNextPressInItsWindow(HumanizeLevel level, int seed)
    {
        var result = ByWindow(Out(Humanizer.Apply(Song(), Delay, level, new Random(seed))));
        foreach (var presses in result.Values)
            for (var k = 0; k + 1 < presses.Count; k++)
            {
                var gap = presses[k + 1].Time - presses[k].Time;
                if (presses[k + 1].Key is "9" or "0")
                    Assert.True(gap >= 0.5, $"an octave change only {gap} ms after the press before it");
                if (presses[k].Key is "9" or "0")
                    Assert.True(gap >= Delay - 0.2, $"press {k + 1} only {gap} ms after an octave change");
            }
    }

    [Theory]
    [MemberData(nameof(LevelsAndSeeds))]
    public void HumanizingNeverAddsOctaveChangesOrMovesANoteIntoAnotherOctave(HumanizeLevel level, int seed)
    {
        var song = Song();
        var before = ByWindow(song);
        var after = ByWindow(Out(Humanizer.Apply(song, Delay, level, new Random(seed))));
        Assert.Equal(before.Keys.OrderBy(w => w), after.Keys.OrderBy(w => w));
        foreach (var w in before.Keys)
            Assert.Equal(Segments(before[w].Select(p => p.Key)), Segments(after[w].Select(p => p.Key)));
    }

    [Fact]
    public void AnOctaveChangePressedWithItsNoteGetsOctaveDelayBeforeTheNote()
    {
        var song = new List<(double, string)> { (0, K(1, 4)), (500, K(1, Up) + K(1, 4, 7)), (1000, K(1, 7)) };
        var p = ByWindow(Out(Humanizer.Apply(song, Delay, HumanizeLevel.Off, new Random(1))))[1];
        Assert.Equal(new[] { "3", "0", "3", "5", "5" }, p.Select(x => x.Key));
        Assert.Equal(500, p[1].Time, 1);
        Assert.Equal(500 + Delay, p[2].Time, 1);
        Assert.Equal(p[2].Time, p[3].Time, 1);                 // no roll at Off
        Assert.Equal(1000, p[4].Time, 1);                      // the next note was far enough away to stay in time
    }

    [Fact]
    public void WithoutRoomTheEnsembleSlowsDownInsteadOfRushingAfterwards()
    {
        // E4 E4 E4 then E5 (an octave up) every 100 ms; OctaveDelay 130. The arrangement without forced delays puts
        // the change straight after the third note, which leaves 99 ms.
        var csv = "0, 0, Header, 1, 2, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
            string.Concat(new[] { 64, 64, 64, 76, 77, 79, 77, 76 }.Select((p, i) => $"2, {i * 100}, Note_on_c, 0, {p}, 90\n")) + "0, 0, End_of_file\n";
        var song = CsvSong.Parse(csv.Split('\n'));
        var forced = Times(SingleInstrument.Arrange(song, 1, "130", 1.0));
        var free = Times(SingleInstrument.Arrange(song, 1, "130", 1.0, forceDelays: false));
        Assert.Equal(new[] { 0.0, 100, 200, 331, 400, 500, 600, 700 }, Notes(ByWindow(forced)[1]));  // 131 then 69: a stutter
        var even = Notes(ByWindow(Out(Humanizer.Apply(free, Delay, HumanizeLevel.Off, new Random(1))))[1]);
        Assert.Equal(new[] { 0.0, 100, 200, 331, 431, 531, 631, 731 }, even.Select(t => Math.Round(t)));

        // With humanizing the slow-down starts ahead of the change, and the notes after it keep the song's spacing.
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var human = Notes(ByWindow(Out(Humanizer.Apply(free, Delay, HumanizeLevel.Subtle, new Random(seed))))[1]);
            var gaps = human.Zip(human.Skip(1), (a, b) => b - a).ToList();
            Assert.True(gaps[2] >= 131 - 0.2, $"seed {seed}: {gaps[2]}");
            Assert.All(gaps.Skip(3), g => Assert.InRange(g, 100 - 20.5, 100 + 20.5));   // ±2.5·(3+1) ms each side
        }

        static List<(double Time, string Keys)> Times(SlotPlan plan)
        {
            var result = new List<(double, string)>();
            double t = 0;
            for (var k = 1; k < plan.Notes.Count; k++) { t += plan.DelaysMs[k - 1]; result.Add((t, plan.Notes[k])); }
            return result;
        }
        static List<double> Notes(IEnumerable<(double Time, string Key)> presses) => presses.Where(p => p.Key is not "9" and not "0").Select(p => p.Time).ToList();
    }

    [Theory]
    [InlineData(HumanizeLevel.Subtle)]
    [InlineData(HumanizeLevel.Natural)]
    [InlineData(HumanizeLevel.Loose)]
    public void TimingVariesAroundTheBeatWithinTheLevelsBounds(HumanizeLevel level)
    {
        var profile = Humanizer.For(level);
        var song = Enumerable.Range(0, 400).Select(i => (i * 250.0, K(1, i % 12) + K(2, (i + 4) % 12))).ToList();
        var result = ByWindow(Out(Humanizer.Apply(song, Delay, level, new Random(7))));
        var limit = 2.5 * (profile.DriftMs + profile.JitterMs);
        foreach (var presses in result.Values)
        {
            var deviation = presses.Select((p, i) => p.Time - i * 250.0).ToList();
            Assert.True(deviation.Max() - deviation.Min() <= 2 * limit + 0.2);
            var mean = deviation.Average();
            var sd = Math.Sqrt(deviation.Average(d => (d - mean) * (d - mean)));
            Assert.InRange(sd, 0.5 * profile.DriftMs, 2 * (profile.DriftMs + profile.JitterMs));
            // Correlated (1/f) drift, not independent jitter: neighbouring notes lean the same way.
            var lag1 = deviation.Zip(deviation.Skip(1), (a, b) => (a - mean) * (b - mean)).Average() / (sd * sd);
            Assert.True(lag1 > 0.4, $"lag-1 correlation {lag1}");
        }
        // The two windows do not drift in lockstep.
        Assert.NotEqual(result[1].Select(p => Math.Round(p.Time)), result[2].Select(p => Math.Round(p.Time)));
    }

    [Theory]
    [InlineData(HumanizeLevel.Subtle)]
    [InlineData(HumanizeLevel.Natural)]
    [InlineData(HumanizeLevel.Loose)]
    public void FastRunsStayEven(HumanizeLevel level)
    {
        // Sixteenths 60 ms apart with an octave change every 8 notes (in the gap, as the arrangement places it).
        var song = new List<(double, string)>();
        for (var i = 0; i < 400; i++)
        {
            if (i % 8 == 7) song.Add((i * 60.0 + 30, K(1, i % 16 == 7 ? Up : Down)));
            song.Add((i * 60.0, K(1, i % 12)));
        }
        song.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        foreach (var seed in Enumerable.Range(1, 10))
        {
            var notes = ByWindow(Out(Humanizer.Apply(song, Delay, level, new Random(seed))))[1].Where(p => p.Key is not "9" and not "0").ToList();
            Assert.All(notes.Zip(notes.Skip(1), (a, b) => b.Time - a.Time), gap => Assert.True(gap >= 0.7 * 60 - 0.2, $"{gap} ms"));
        }
    }

    [Fact]
    public void ChordsRollLowToHighWithinTheirOctaveOnly()
    {
        // C E G in the middle octave, then the high octave's C E G after an octave change.
        var song = new List<(double, string)> { (0, K(1, 7, 0, 4)), (300, K(1, Up)), (600, K(1, 0, 4, 7)), (1200, K(1, 0)) };
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var p = ByWindow(Out(Humanizer.Apply(song, Delay, HumanizeLevel.Loose, new Random(seed))))[1];
            Assert.Equal(new[] { "1", "3", "5", "0", "1", "3", "5", "1" }, p.Select(x => x.Key));
            Assert.InRange(p[2].Time - p[0].Time, 1, 25);
            Assert.True(p[3].Time > p[2].Time);                           // the roll is over before the change
            Assert.InRange(p[6].Time - p[4].Time, 1, 25);
        }
    }

    [Fact]
    public void TheResultStartsAtTheGivenTimeOrLater()
    {
        // An octave change ahead of the first note (as a one-player song's first chord has it).
        var song = new List<(double, string)> { (-130, K(1, Down)), (0, K(1, 4)), (200, K(1, 5)) };
        var events = Humanizer.Apply(song, Delay, HumanizeLevel.Natural, new Random(3), startMs: 1000);
        Assert.True(events.Min(e => e.DueMs!.Value) >= 1000);
        Assert.Equal(new[] { "9", "3", "4" }, events.SelectMany(e => e.Keys).Select(c => KeyMap.Decode(c)!.Value.Key));
        Assert.Empty(Humanizer.Apply(new List<(double, string)> { (0, "") }, Delay, HumanizeLevel.Natural, new Random(3)));
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

    private List<string> Play(HumanizeLevel level)
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
        var plain = Play(HumanizeLevel.Off);
        var human = Play(HumanizeLevel.Natural);
        Assert.Equal(8, plain.Count(k => k is not "9" and not "0"));
        Assert.Equal(Segments(plain), Segments(human));
    }

    private static List<string> Segments(IEnumerable<string> keys)
    {
        var result = new List<string>();
        var notes = new List<string>();
        foreach (var key in keys.Append("|"))
        {
            if (key is "9" or "0" or "|")
            {
                if (notes.Count > 0) result.Add(string.Join("+", notes.OrderBy(n => n, StringComparer.Ordinal)));
                notes.Clear();
                if (key != "|") result.Add(key);
            }
            else notes.Add(key);
        }
        return result;
    }
}

/// <summary>The host humanizes its part only when it plays alone (no player gets a part).</summary>
[Collection(TimingCollection.Name)]
public sealed class HumanizedHostTests : IDisposable
{
    private static readonly HostTimings Timings = new() { ClockWaitMs = 100, ClockProbeIntervalMs = 10, OctaveResetDelayMs = 5, SongTailMs = 50, PlaylistGapMs = 50 };
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-hhost-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = new();

    public HumanizedHostTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // Track 2 and track 3 with chords across octaves and quick octave changes; tempo 500000 => 1 tick == 1 ms.
    private static readonly string Song = "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        string.Concat(new[] { (0, 64), (0, 52), (150, 67), (300, 76), (400, 55), (400, 79), (550, 60), (700, 72), (900, 64), (1100, 52) }
            .Select((n, i) => $"{2 + i % 2}, {n.Item1}, Note_on_c, 0, {n.Item2}, 90\n")) + "0, 0, End_of_file\n";

    /// <summary>Plays the song on a host whose clock jumps to each press (exact times) and returns its key presses.</summary>
    private async Task<List<Press>> PlayOnHost(HumanizeLevel level, PortMapping mapping, bool singleInstrument, bool withClient = false)
    {
        var clock = new VirtualClock(1000);
        var keys = new RecordingKeys(clock);
        var view = new FakeView();
        var host = new HostSession(view, new SongLibrary(Path.Combine(_dir, "host")), () => "130", clock, keys, new FakeMeter(), Timings,
            humanize: () => level, random: new Random(5));
        _disposables.Add(host);
        host.CommitPortMapping(mapping, new[] { "0", "0", "0", "0", "0" });
        host.Start(0);
        if (withClient)
        {
            var c = new OffsetClock(0);
            var client = new ClientSession(new FakeView(), new SongLibrary(Path.Combine(_dir, "client")), () => 5, c, new RecordingKeys(c), new FakeMeter());
            _disposables.Add(client);
            Assert.True(await client.ConnectAsync("127.0.0.1", "bob", host.Port));
            await Wait.Until(() => client.Slot is not null, what: "welcome");
        }
        await host.PlayAsync(Song, 1.0, singleInstrument: singleInstrument);
        await Wait.Until(() => keys.Presses.Count > 0 && !host.IsPlaying, what: "the song to end");
        Assert.Empty(view.Problems);
        return keys.All;
    }

    private static List<string> Segments(IEnumerable<string> keys)
    {
        var result = new List<string>();
        var notes = new List<string>();
        foreach (var key in keys.Append("|"))
        {
            if (key is "9" or "0" or "|")
            {
                if (notes.Count > 0) result.Add(string.Join("+", notes.OrderBy(n => n, StringComparer.Ordinal)));
                notes.Clear();
                if (key != "|") result.Add(key);
            }
            else notes.Add(key);
        }
        return result;
    }

    private static List<double> Relative(List<Press> presses) => presses.Select(p => Math.Round(p.Time - presses[0].Time, 1)).ToList();

    [Fact]
    public async Task AHostPlayingAloneHumanizesWithTheSameNotesAndOctaveChanges()
    {
        var plain = await PlayOnHost(HumanizeLevel.Off, new PortMapping(), singleInstrument: true);
        var human = await PlayOnHost(HumanizeLevel.Natural, new PortMapping(), singleInstrument: true);
        Assert.Equal(10, plain.Count(p => p.Key is not "9" and not "0"));
        Assert.Equal(Segments(plain.Select(p => p.Key)), Segments(human.Select(p => p.Key)));
        Assert.NotEqual(Relative(plain), Relative(human));
        for (var k = 0; k + 1 < human.Count; k++)
            if (human[k].Key is "9" or "0") Assert.True(human[k + 1].Time - human[k].Time >= 130 - 0.2);
    }

    [Fact]
    public async Task WithAPlayerTheHostKeepsTheExactTiming()
    {
        var mapping = PortMapping.Parse("12,23");
        var expected = SongArranger.Arrange(CsvSong.Parse(Song.Split('\n')), mapping, "130", 1.0)[1];
        var times = Schedule.AbsoluteTimes(expected, 0, 0);
        var due = Schedule.HostEvents(expected.Notes, times).Where(e => e.Keys.Length > 0).SelectMany(e => e.Keys.Select(_ => e.DueMs!.Value)).ToList();
        var played = await PlayOnHost(HumanizeLevel.Loose, mapping, singleInstrument: false, withClient: true);
        Assert.Equal(due.Select(t => Math.Round(t - due[0], 1)), Relative(played));
    }
}
