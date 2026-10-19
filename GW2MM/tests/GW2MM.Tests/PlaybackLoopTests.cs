using GW2MM.Core.Arrangement;
using GW2MM.Core.Playback;

namespace GW2MM.Tests;

[Collection(TimingCollection.Name)]
public class PlaybackLoopTests
{
    private static string K(int window, int offset) => ((char)(192 + 16 * (window - 1) + offset)).ToString();

    private static List<Press> RunToEnd(IReadOnlyList<PlaybackEvent> events, VirtualClock clock, out PlaybackOutcome outcome)
    {
        var keys = new RecordingKeys(clock);
        var loop = new PlaybackLoop(events, clock, keys);
        loop.Start();
        outcome = loop.Completion.Wait(5000) ? loop.Completion.Result : throw new TimeoutException();
        return keys.All;
    }

    [Fact]
    public void HostLoopPressesElementKPlusOneAtTimeK_AndNeverTheFirstElement()
    {
        var notes = new[] { K(1, 0), K(1, 2), "", K(2, 4) + K(1, 13), "" };
        var times = new double[] { 100, 250, 400, 400, 400 };
        var presses = RunToEnd(Schedule.HostEvents(notes, times), new VirtualClock(), out var outcome);
        Assert.Equal(PlaybackOutcome.Completed, outcome);
        Assert.Equal(new[]
        {
            new Press(100, 1, "2"),
            new Press(400, 2, "3"), new Press(400, 1, "9"),
        }, presses);
    }

    [Fact]
    public void ClientLoopPressesTheFirstElementImmediately()
    {
        var notes = new[] { K(1, 0), K(1, 2), K(1, 4) };
        var rests = new double?[] { 1000, 1200, 1200 };
        var presses = RunToEnd(Schedule.ClientEvents(notes, rests), new VirtualClock(50), out _);
        Assert.Equal(new[] { new Press(50, 1, "1"), new Press(1000, 1, "2"), new Press(1200, 1, "3") }, presses);
    }

    [Fact]
    public void OverdueEventsCatchUpInOrderOneMillisecondApart()
    {
        var events = new[] { new PlaybackEvent(10, K(1, 0)), new PlaybackEvent(10, ""), new PlaybackEvent(11, K(1, 2)), new PlaybackEvent(12, K(1, 4)) };
        var presses = RunToEnd(events, new VirtualClock(500), out _);
        Assert.Equal(new[] { "1", "2", "3" }, presses.Select(p => p.Key));
        Assert.Equal(new double[] { 500, 501, 502 }, presses.Select(p => p.Time));
    }

    [Fact]
    public void StopPreventsAnyFurtherPress()
    {
        var clock = PrecisionClock.Instance;
        var keys = new RecordingKeys(clock);
        var now = clock.NowMs;
        var events = Enumerable.Range(0, 50).Select(i => new PlaybackEvent(now + 20 + i * 20, K(1, 0))).ToList();
        var loop = new PlaybackLoop(events, clock, keys);
        loop.Start();
        Thread.Sleep(100);
        loop.Stop();
        Assert.Equal(PlaybackOutcome.Stopped, loop.Completion.Result);
        var count = keys.Presses.Count;
        Thread.Sleep(100);
        Assert.Equal(count, keys.Presses.Count);
        Assert.InRange(count, 2, 10);
    }

    [Fact]
    public void ResumeCarriesOnFromWherePlaybackWasPaused()
    {
        var clock = new VirtualClock();
        var keys = new RecordingKeys(clock);
        var events = Enumerable.Range(0, 5).Select(i => new PlaybackEvent(10 + i * 10, K(1, 0))).ToList();
        var loop = new PlaybackLoop(events, clock, keys, startPaused: true);   // paused at 0
        loop.Start();
        Thread.Sleep(100);
        clock.Advance(500);
        Assert.Empty(keys.Presses);
        var resumed = clock.NowMs;
        loop.SetPaused(false);
        Assert.Equal(PlaybackOutcome.Completed, loop.Completion.Result);
        // Every note keeps its place in the song: no burst of missed notes.
        Assert.Equal(new[] { 10, 20, 30, 40, 50 }.Select(t => resumed + t), keys.All.Select(p => p.Time));
    }

    [Fact]
    public void WithCatchUpResumeCatchesUpTheMissedEvents()
    {
        var clock = PrecisionClock.Instance;
        var keys = new RecordingKeys(clock);
        var now = clock.NowMs;
        var events = Enumerable.Range(0, 5).Select(i => new PlaybackEvent(now + 10 + i * 10, K(1, 0))).ToList();
        var loop = new PlaybackLoop(events, clock, keys, startPaused: true) { CatchUp = true };
        loop.Start();
        Thread.Sleep(150);
        Assert.Empty(keys.Presses);
        var resumed = clock.NowMs;
        loop.SetPaused(false);
        Assert.Equal(PlaybackOutcome.Completed, loop.Completion.Result);
        var presses = keys.All;
        Assert.Equal(5, presses.Count);
        Assert.All(presses, p => Assert.True(p.Time >= resumed));
        Assert.True(presses[^1].Time - resumed < 50, "caught up quickly");
    }

    [Fact]
    public void PausingMidSongMovesTheRestOfItForEveryWindow()
    {
        // Several accounts playing alone: two windows, paused at 150 ms (between notes) and resumed at 500.
        var clock = new VirtualClock();
        var keys = new RecordingKeys(clock);
        var events = new[]
        {
            new PlaybackEvent(100, K(1, 0)), new PlaybackEvent(100, K(2, 4)), new PlaybackEvent(200, K(1, 2)),
            new PlaybackEvent(200, K(2, 5)), new PlaybackEvent(300, K(1, 4)), new PlaybackEvent(300, K(2, 7)),
        };
        var pausedAt150 = new PauseAt(clock, 150);
        var loop = new PlaybackLoop(events, pausedAt150, keys);
        pausedAt150.Loop = loop;
        loop.Start();
        Assert.True(pausedAt150.Paused.Wait(5000));
        Thread.Sleep(100);
        Assert.Equal(2, keys.All.Count);
        clock.Advance(500 - clock.NowMs);
        loop.SetPaused(false);
        Assert.True(loop.Completion.Wait(5000));
        // The rest of the song keeps its spacing, 350 ms (the pause) later: no burst of missed notes.
        Assert.Equal(new double[] { 100, 101, 550, 551, 650, 651 }, keys.All.Select(p => p.Time));
        Assert.Equal(new[] { 1, 2, 1, 2, 1, 2 }, keys.All.Select(p => p.Window));
    }

    /// <summary>A virtual clock that pauses the loop the first time it waits past <c>at</c> ms.</summary>
    private sealed class PauseAt(VirtualClock inner, double at) : IClock
    {
        public PlaybackLoop? Loop;
        public readonly ManualResetEventSlim Paused = new();
        public double NowMs => inner.NowMs;
        public bool WaitUntil(double dueMs, CancellationToken ct)
        {
            if (dueMs > at && !Paused.IsSet)
            {
                inner.WaitUntil(at, ct);
                Loop!.SetPaused(true);
                Paused.Set();
            }
            return inner.WaitUntil(dueMs, ct);
        }
    }

    [Fact]
    public void KeyOutputFailureDoesNotStopTheSong()
    {
        var clock = new VirtualClock();
        var failing = new ThrowingKeys();
        var loop = new PlaybackLoop(new[] { new PlaybackEvent(1, K(1, 0)), new PlaybackEvent(2, K(1, 2)) }, clock, failing);
        var errors = 0;
        loop.KeyError += _ => errors++;
        loop.Start();
        Assert.Equal(PlaybackOutcome.Completed, loop.Completion.Result);
        Assert.Equal(2, errors);
    }

    private sealed class ThrowingKeys : IKeyOutput
    {
        public void Press(int window, string key) => throw new InvalidOperationException("boom");
    }

    /// <summary>
    /// Literal emulation of the original host label SongPlayer1 driven by an AutoHotkey-style timer (fires every
    /// <paramref name="tickMs"/>, reads a clock that only advances in <paramref name="tickMs"/> steps). The new loop
    /// must press the same keys in the same order, never earlier than scheduled and never later than the original.
    /// </summary>
    private static List<(double Time, string Keys)> EmulateOriginalHostTimer(IReadOnlyList<string> notes, IReadOnlyList<double> times, double tickMs)
    {
        var pressed = new List<(double, string)>();
        var songIndex = 1; // SongIndex1 := 1
        var songLength = notes.Count;
        for (double t = 0; songIndex <= songLength; t += tickMs)
        {
            var aTickCount = Math.Floor(t / tickMs) * tickMs;
            var rest = songIndex - 1 < times.Count ? times[songIndex - 1] : double.NegativeInfinity;
            if (aTickCount >= rest)
            {
                var next = songIndex < notes.Count ? notes[songIndex] : "";
                if (next.Length > 0) pressed.Add((t, next));
                songIndex++;
            }
        }
        return pressed;
    }

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 15.625)]
    [InlineData(3, 10.0)]
    public void MatchesTheOriginalTimerLoop(int seed, double tickMs)
    {
        var rng = new Random(seed);
        var notes = new List<string> { "" };
        var times = new List<double>();
        double t = 1000;
        for (var i = 0; i < 300; i++)
        {
            t += rng.Next(0, 4) == 0 ? 0 : rng.Next(1, 300);
            times.Add(t);
            notes.Add(rng.Next(0, 3) == 0 ? "" : K(rng.Next(1, 9), rng.Next(0, 15)));
        }
        times.Add(t); times.Add(t);
        notes.Add(""); notes.Add("");

        var original = EmulateOriginalHostTimer(notes, times, tickMs);
        var keys = new RecordingKeys(new VirtualClock());
        var clock = new VirtualClock();
        keys = new RecordingKeys(clock);
        var loop = new PlaybackLoop(Schedule.HostEvents(notes, times), clock, keys);
        loop.Start();
        loop.Completion.Wait();
        var mine = keys.All.Select(p => p).ToList();

        var originalKeys = original.SelectMany(o => o.Keys.Select(c => KeyMap.Decode(c)!.Value)).ToList();
        Assert.Equal(originalKeys, mine.Select(p => (p.Window, p.Key)).ToList());

        // Per press: never before its scheduled time, never after the original's press.
        var scheduled = new List<double>();
        for (var k = 0; k < times.Count; k++)
            if (k + 1 < notes.Count && notes[k + 1].Length > 0) scheduled.Add(times[k]);
        var originalTimes = original.Select(o => o.Time).ToList();
        for (var i = 0; i < scheduled.Count; i++)
        {
            Assert.True(mine[i].Time >= scheduled[i], $"press {i} early");
            Assert.True(mine[i].Time <= originalTimes[i] + 0.001, $"press {i} later than the original ({mine[i].Time} > {originalTimes[i]})");
        }
    }

    [Fact]
    public void PrecisionClockWaitsAccurately() => AssertPunctual(PrecisionClock.Instance);

    /// <summary>
    /// Windows without 1 ms timer resolution rounds sleeps up to 15.6 ms ticks (seen on the CI runner): the clock
    /// must learn that and spin the rest instead of oversleeping.
    /// </summary>
    [Fact]
    public void PrecisionClockStaysPunctualWithACoarseSleep()
    {
        var reference = System.Diagnostics.Stopwatch.StartNew();
        var clock = new PrecisionClock((t, ct) =>
        {
            var tick = 15.625;
            var wake = Math.Ceiling((reference.Elapsed.TotalMilliseconds + t.TotalMilliseconds) / tick) * tick;
            while (reference.Elapsed.TotalMilliseconds < wake)
                if (ct.WaitHandle.WaitOne(1)) return true;
            return false;
        });
        AssertPunctual(clock);
    }

    private static void AssertPunctual(PrecisionClock clock)
    {
        var errors = new List<double>();
        for (var i = 0; i < 40; i++)
        {
            var due = clock.NowMs + 7 + i % 5;
            clock.WaitUntil(due, CancellationToken.None);
            errors.Add(clock.NowMs - due);
        }
        errors.Sort();
        Assert.True(errors[0] >= 0);
        Assert.True(errors[(int)(errors.Count * 0.9)] < 2.0, "90th percentile lateness " + errors[(int)(errors.Count * 0.9)]);
    }


    [Fact]
    public void StaggerSpreadsWindowsEvenlyOverTheOffset()
    {
        var events = new List<PlaybackEvent> { new(null, K(1, 0) + K(2, 0) + K(3, 0)), new(100, K(1, 2) + K(3, 13) + K(3, 4)), new(100.5, K(2, 2)) };
        var staggered = Schedule.Stagger(events, 50, 10);
        // Three windows: Floor(50 / 3) = 16 ms apart.
        Assert.Equal(new (double?, string)[]
        {
            (10, K(1, 0)), (26, K(2, 0)), (42, K(3, 0)), (100, K(1, 2)), (116.5, K(2, 2)), (132, K(3, 13) + K(3, 4)),
        }, staggered.Select(e => (e.DueMs, e.Keys)));
    }

    [Fact]
    public void StaggerLeavesOneWindowOrNoOffsetAlone()
    {
        var one = new List<PlaybackEvent> { new(null, K(1, 0)), new(100, K(1, 2)) };
        Assert.Equal(one, Schedule.Stagger(one, 50, 0));
        var two = new List<PlaybackEvent> { new(5, K(1, 0) + K(2, 0)) };
        Assert.Equal(two, Schedule.Stagger(two, 0, 0));
        Assert.Equal(two, Schedule.Stagger(two, 1, 0));   // Floor(1 / 2) = 0, as the original script's 1 ms tick
    }

    [Fact]
    public void SpeedRetimesTheRestOfTheSong()
    {
        var clock = new VirtualClock();
        var events = new List<PlaybackEvent> { new(100, K(1, 0)), new(200, K(1, 2)), new(300, K(1, 4)), new(400, K(1, 5)) };
        var keys = new RecordingKeys(clock);
        var loop = new PlaybackLoop(events, clock, keys);
        loop.SetSpeed(2);                                // twice as fast from now (0)
        Assert.Equal(200, loop.LastDueMs);
        loop.Start();
        Assert.True(loop.Completion.Wait(5000));
        Assert.Equal(new[] { 50.0, 100, 150, 200 }, keys.All.Select(p => p.Time));
        Assert.Equal(2, loop.Speed);
    }

    [Fact]
    public void ASpeedChangeWakesAWaitingLoop()
    {
        var clock = new OffsetClock(0);
        var keys = new RecordingKeys(clock);
        var start = clock.NowMs;
        var loop = new PlaybackLoop(new List<PlaybackEvent> { new(start + 800, K(1, 0)) }, clock, keys);
        loop.Start();
        Thread.Sleep(100);
        loop.SetSpeed(4);                                // 700 ms left becomes 175 ms
        Assert.True(loop.Completion.Wait(2000));
        Assert.InRange(keys.All.Single().Time - start, 200, 400);
    }

    // ------------------------------------------------------------------ live octave timer

    private static List<Press> RunWithOctaveTimer(IReadOnlyList<PlaybackEvent> events, double delayMs)
    {
        var clock = new VirtualClock();
        var keys = new RecordingKeys(clock);
        var loop = new PlaybackLoop(events, clock, keys, octaveTimerMs: delayMs);
        loop.Start();
        Assert.True(loop.Completion.Wait(5000));
        return keys.All;
    }

    private const int Down = 13, Up = 14;

    [Fact]
    public void TheOctaveTimerPressesAChordsOctaveChangeAtTheFirstStepAfterThePreviousChord()
    {
        // No change yet, so the timer has run out: the change goes at the first 10 ms step after the previous chord.
        var presses = RunWithOctaveTimer(new[]
        {
            new PlaybackEvent(0, K(1, 0)), new PlaybackEvent(50, K(1, 2)), new PlaybackEvent(500, K(1, Down) + K(1, 4)),
        }, 100);
        Assert.Equal(new[] { new Press(0, 1, "1"), new Press(50, 1, "2"), new Press(60, 1, "9"), new Press(500, 1, "3") }, presses);
    }

    [Fact]
    public void TheOctaveTimerWaitsOctaveDelayAfterEachChange()
    {
        var presses = RunWithOctaveTimer(new[]
        {
            new PlaybackEvent(0, K(1, Down) + K(1, 0)),    // a change at 0 starts the timer
            new PlaybackEvent(30, K(1, 2)),
            new PlaybackEvent(80, K(1, Up) + K(1, 4)),     // the timer is at 80 < 100: stays with its chord, restarts at 80
            new PlaybackEvent(400, K(1, Down) + K(1, 5)),  // pressed early once the timer reaches 100 (at 180)
        }, 100);
        Assert.Equal(new[]
        {
            new Press(0, 1, "9"), new Press(0, 1, "1"), new Press(30, 1, "2"), new Press(80, 1, "0"), new Press(80, 1, "3"),
            new Press(180, 1, "9"), new Press(400, 1, "4"),
        }, presses);
    }

    [Fact]
    public void TheOctaveTimerCountsInTenMillisecondSteps()
    {
        // OctaveDelay 95 is reached at the step of 100 ms after the change at 0.
        var presses = RunWithOctaveTimer(new[] { new PlaybackEvent(0, K(1, Up) + K(1, 0)), new PlaybackEvent(400, K(1, Down) + K(1, 2)) }, 95);
        Assert.Equal(new[] { new Press(0, 1, "0"), new Press(0, 1, "1"), new Press(100, 1, "9"), new Press(400, 1, "2") }, presses);
    }

    [Fact]
    public void TwoLeadingChangesAreBothPressedEarlyOctaveDelayApart()
    {
        var presses = RunWithOctaveTimer(new[] { new PlaybackEvent(0, K(1, 0)), new PlaybackEvent(1000, K(1, Down) + K(1, Down) + K(1, 0)) }, 100);
        Assert.Equal(new[] { new Press(0, 1, "1"), new Press(10, 1, "9"), new Press(110, 1, "9"), new Press(1000, 1, "1") }, presses);
    }

    [Fact]
    public void EachWindowHasItsOwnTimerAndFillerIsSkipped()
    {
        var presses = RunWithOctaveTimer(new[]
        {
            new PlaybackEvent(0, K(1, 0)),
            new PlaybackEvent(100, ""),                                // filler
            new PlaybackEvent(200, K(1, 2)),                           // window 1 only: not window 2's next chord
            new PlaybackEvent(600, K(1, 4) + K(2, Up) + K(2, 0)),      // window 2's chord starts with its change
        }, 100);
        Assert.Equal(new[]
        {
            new Press(0, 1, "1"), new Press(10, 2, "0"), new Press(200, 1, "2"), new Press(600, 1, "3"), new Press(600, 2, "1"),
        }, presses);
    }

    [Fact]
    public void AChangeWithNoTimeToSpareStaysWithItsChord()
    {
        var presses = RunWithOctaveTimer(new[]
        {
            new PlaybackEvent(0, K(1, Up) + K(1, 0)), new PlaybackEvent(60, K(1, Down) + K(1, 2)),   // timer at 60 < 100
        }, 100);
        Assert.Equal(new[] { new Press(0, 1, "0"), new Press(0, 1, "1"), new Press(60, 1, "9"), new Press(60, 1, "2") }, presses);
    }

    [Fact]
    public void WithoutTheOctaveTimerChangesArePressedWithTheirChords()
    {
        var presses = RunToEnd(new[] { new PlaybackEvent(0, K(1, 0)), new PlaybackEvent(500, K(1, Down) + K(1, 4)) }, new VirtualClock(), out _);
        Assert.Equal(new[] { new Press(0, 1, "1"), new Press(500, 1, "9"), new Press(500, 1, "3") }, presses);
    }
}
