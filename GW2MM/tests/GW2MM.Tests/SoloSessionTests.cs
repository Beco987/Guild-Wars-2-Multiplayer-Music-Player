using GW2MM.Core.Library;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Tests;

/// <summary>A client playing on its own game windows while it is not connected.</summary>
[Collection(TimingCollection.Name)]
public sealed class SoloSessionTests : IDisposable
{
    // Track 2: a rising scale; track 3: a few notes; tempo 500000 => 1 tick == 1 ms.
    private const string Song = "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        "2, 0, Start_track\n2, 0, Note_on_c, 0, 60, 90\n2, 200, Note_on_c, 0, 62, 90\n2, 400, Note_on_c, 0, 64, 90\n2, 600, Note_on_c, 0, 65, 90\n2, 800, Note_on_c, 0, 67, 90\n2, 800, End_track\n" +
        "3, 0, Start_track\n3, 100, Note_on_c, 0, 72, 90\n3, 300, Note_on_c, 0, 74, 90\n3, 500, Note_on_c, 0, 76, 90\n3, 700, Note_on_c, 0, 77, 90\n3, 700, End_track\n0, 0, End_of_file\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-solo-" + Guid.NewGuid().ToString("N"));
    private readonly OffsetClock _clock = new(0);
    private readonly RecordingKeys _keys;
    private readonly FakeView _view = new();
    private readonly SoloSession _solo;

    public SoloSessionTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Csv"));
        File.WriteAllText(Path.Combine(_dir, "Csv", "Duet.csv"), Song);
        File.WriteAllText(Path.Combine(_dir, "Csv", "Other.csv"), Song);
        File.WriteAllText(Path.Combine(_dir, "Data.csv"), "Duet, 2, 0, x\nOther, 2, 0, y\n");
        _keys = new RecordingKeys(_clock);
        _solo = new SoloSession(_view, new SongLibrary(_dir), () => "130", () => 1, _clock, _keys, tailMs: 50, gapMs: 50);
    }

    public void Dispose()
    {
        _solo.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private List<string> KeysIn(int window) => _keys.All.Where(p => p.Window == window).Select(p => p.Key).ToList();

    [Fact]
    public void EachTrackPlaysOnItsOwnWindowWithTheSongsTiming()
    {
        // Which game windows are open plays no part: every part is played, track by track on Player1, Player2, …
        _solo.PlaySong("Duet");
        Assert.Contains("♪ Now playing: Duet", _view.Dialog);
        Assert.Contains("Not connected: playing on Player1, Player2.", _view.Dialog);
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 11, 5000);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, KeysIn(1));       // track 2
        Assert.Equal(new[] { "8", "0", "2", "3", "4", "9" }, KeysIn(2));  // track 3, then the reset from the high octave
        var p1 = _keys.All.Where(p => p.Window == 1).ToList();
        for (var k = 1; k < p1.Count; k++) Assert.InRange(p1[k].Time - p1[k - 1].Time, 200 - 15, 200 + 15);
        Assert.True(_keys.All.Min(p => p.Time) > 0);
        Assert.DoesNotContain(_view.Dialog, l => l.StartsWith("This song has"));
        Assert.Empty(_view.Problems);
    }

    [Fact]
    public async Task PlaylistsPlaySoloWithOctaveResetsBetweenSongs()
    {
        await _solo.PlayPlaylistAsync(new Playlist("Set", new[] { "Duet", "Missing", "Other" }), 0, shuffle: false, repeat: false)
            .WaitAsync(TimeSpan.FromSeconds(10));
        var notes = _keys.All.Where(p => p.Window == 1 && p.Key != "9" && p.Key != "0").Select(p => p.Key).ToList();
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "1", "2", "3", "4", "5" }, notes);
        // Each song ends with Player2 (track 3) in the high octave: one 9 after each, and nothing for the others.
        Assert.Equal(new[] { "8", "0", "2", "3", "4", "9", "8", "0", "2", "3", "4", "9" }, KeysIn(2));
        Assert.DoesNotContain(_keys.All, p => p.Window >= 3);
        Assert.Contains(_view.Problems, p => p.Contains("Missing"));
        Assert.Contains("♪ Now playing: Other (2/2 in Set)", _view.Dialog);
        Assert.Contains("♪ Playlist Set finished.", _view.Dialog);
        Assert.Equal(1, _view.Dialog.Count(l => l.StartsWith("Not connected: playing on")));
    }

    [Fact]
    public async Task SoloPlaylistsCanKeepTheOctavesBetweenSongs()
    {
        using var solo = new SoloSession(_view, new SongLibrary(_dir), () => "130", () => 1, _clock, _keys, tailMs: 50, gapMs: 50)
        {
            ResetOctavesBetweenSongs = () => false,
        };
        await solo.PlayPlaylistAsync(new Playlist("Set", new[] { "Duet", "Other" }), 0, shuffle: false, repeat: false)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "1", "2", "3", "4", "5" }, KeysIn(1));
        Assert.DoesNotContain(_keys.All, p => p.Window >= 3);   // a reset is the only thing that presses Player3..5
    }

    [Fact]
    public async Task StopEndsSoloPlaybackAndResetsOctaves()
    {
        File.WriteAllText(Path.Combine(_dir, "Csv", "Slow.csv"), Song.Replace(", 200,", ", 5000,").Replace("3, 300,", "3, 3000,"));
        File.AppendAllText(Path.Combine(_dir, "Data.csv"), "Slow, 2, 0, z\n");
        var playing = _solo.PlayPlaylistAsync(new Playlist("Set", new[] { "Slow", "Other" }), 0, false, false);
        SpinWait.SpinUntil(() => KeysIn(2).Contains("0"), 5000);   // Player2 has gone up to the high octave
        await _solo.StopAsync();
        await playing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(_solo.IsPlaying);
        Assert.False(_solo.IsPlaylistRunning);
        // The reset presses only what each window needs: one 9 for Player2, nothing for Player1 (still middle).
        Assert.Equal(new[] { "8", "0", "9" }, KeysIn(2));
        Assert.Equal(new[] { "1" }, KeysIn(1));
        Assert.DoesNotContain(_view.Dialog, l => l.Contains("(2/2 in Set)"));
    }

    [Fact]
    public void AOnePlayerSongMergesEveryTrackIntoTheFirstWindow()
    {
        File.WriteAllText(Path.Combine(_dir, "Csv", "Solo.csv"), Song);
        File.AppendAllText(Path.Combine(_dir, "Data.csv"), "Solo, 1, 0, merged\n");
        _solo.PlaySong("Solo");
        SpinWait.SpinUntil(() => _keys.Presses.Count(p => p.Key is not "9" and not "0") >= 9, 5000);
        Thread.Sleep(200);
        Assert.All(_keys.All, p => Assert.Equal(1, p.Window));                      // one instrument, on Player1
        Assert.Equal(9, _keys.All.Count(p => p.Key is not "9" and not "0"));      // 5 + 4 notes from both tracks
        Assert.DoesNotContain(_view.Dialog, l => l.StartsWith("This song has"));
    }

    [Fact]
    public void OffsetStaggersTheWindowsWhilePlayingSolo()
    {
        using var solo = new SoloSession(_view, _solo.Library, () => "130", () => 1, _clock, _keys, tailMs: 50, gapMs: 50, offsetMs: () => 60);
        solo.PlaySong("Duet");
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 11, 5000);
        var w1 = _keys.All.Where(p => p.Window == 1).ToList();
        var w3 = _keys.All.Where(p => p.Window == 2).ToList();
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, w1.Select(p => p.Key));
        Assert.Equal(new[] { "8", "0", "2", "3", "4", "9" }, w3.Select(p => p.Key));   // ends with the reset
        // Track 3 starts 100 ms after track 2 in the song; Offset 60 over 2 windows adds 30 ms to the second window.
        Assert.InRange(w3[0].Time - w1[0].Time, 130 - 15, 130 + 15);
        for (var k = 1; k < w1.Count; k++) Assert.InRange(w1[k].Time - w1[k - 1].Time, 200 - 15, 200 + 15);
    }

    [Fact]
    public void PausingSoloCarriesOnWhereItWasOnEveryWindow()
    {
        _solo.PlaySong("Duet");
        SpinWait.SpinUntil(() => KeysIn(1).Count >= 2, 5000);
        _solo.TogglePause();
        Thread.Sleep(400);
        _solo.TogglePause();
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 10, 5000);
        Thread.Sleep(100);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, KeysIn(1));
        Assert.Equal(new[] { "8", "0", "2", "3", "4", "9" }, KeysIn(2));      // ends with the reset to the middle octave
        foreach (var w in new[] { 1, 2 })
        {
            var p = _keys.All.Where(x => x.Window == w).Take(5).ToList();
            var gaps = Enumerable.Range(1, p.Count - 1).Select(k => p[k].Time - p[k - 1].Time).ToList();
            Assert.All(gaps, g => Assert.True(g > 85, $"no burst after resuming (gap {g:0} ms on Player{w})"));
        }
        Assert.Contains(Enumerable.Range(1, 4).Select(k => _keys.All.Where(x => x.Window == 1).ElementAt(k).Time - _keys.All.Where(x => x.Window == 1).ElementAt(k - 1).Time), g => g > 450);
    }

    // Track 2: high E, low E 1000 ms later, high E again; track 3: low C at 500, the top C (C6) at 1500.
    private const string Jumps = "0, 0, Header, 1, 3, 480\n1, 0, Start_track\n1, 0, Tempo, 500000\n1, 0, End_track\n" +
        "2, 0, Start_track\n2, 0, Note_on_c, 0, 76, 90\n2, 1000, Note_on_c, 0, 52, 90\n2, 2000, Note_on_c, 0, 76, 90\n2, 2000, End_track\n" +
        "3, 0, Start_track\n3, 500, Note_on_c, 0, 48, 90\n3, 1500, Note_on_c, 0, 84, 90\n3, 1500, End_track\n0, 0, End_of_file\n";

    /// <summary>A window's first presses (as many as <paramref name="due"/>): keys, and times from the song's start.</summary>
    private (List<string> Keys, List<double> Times) Window(int window, double[] due, double start)
    {
        var presses = _keys.All.Where(p => p.Window == window).Take(due.Length).ToList();
        return (presses.Select(p => p.Key).ToList(), presses.Select(p => p.Time - start).ToList());
    }

    /// <summary>When the song started, from a window's least-late press: a press can be late but never early.</summary>
    private double SongStart(int window, double[] due)
    {
        var presses = _keys.All.Where(p => p.Window == window).Take(due.Length).ToList();
        return Enumerable.Range(0, presses.Count).Min(k => presses[k].Time - due[k]);
    }

    private static void AssertTimes(double[] expected, List<double> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var k = 0; k < expected.Length; k++) Assert.InRange(actual[k], expected[k], expected[k] + 20);
    }

    private static readonly double[] TrackTwoDue = { 0, 0, 333, 666, 1000, 1333, 1666, 2000 };

    [Fact]
    public void ADoubleOctaveJumpPlaysSoloOnOneWindow()
    {
        // One part (track 2 only): high E, then low E (two octaves down), then high E (two up).
        var oneTrack = Jumps[..Jumps.IndexOf("3, 0, Start_track", StringComparison.Ordinal)] + "0, 0, End_of_file\n";
        File.WriteAllText(Path.Combine(_dir, "Csv", "Jump.csv"), oneTrack);
        File.AppendAllText(Path.Combine(_dir, "Data.csv"), "Jump, 2, 0, x\n");
        _solo.PlaySong("Jump");
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 8, 5000);
        var (keys, times) = Window(1, TrackTwoDue, SongStart(1, TrackTwoDue));
        // Both changes go ahead of each note, evenly through the 1000 ms gap; the notes keep their time.
        Assert.Equal(new[] { "0", "3", "9", "9", "3", "0", "0", "3" }, keys);
        AssertTimes(TrackTwoDue, times);
    }

    [Fact]
    public void DoubleOctaveJumpsPlaySoloOnSeveralWindows()
    {
        File.WriteAllText(Path.Combine(_dir, "Csv", "Jumps.csv"), Jumps);
        File.AppendAllText(Path.Combine(_dir, "Data.csv"), "Jumps, 2, 0, x\n");
        _solo.PlaySong("Jumps");
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 13, 5000);
        var start = SongStart(1, TrackTwoDue);
        var (w1, t1) = Window(1, TrackTwoDue, start);
        Assert.Equal(new[] { "0", "3", "9", "9", "3", "0", "0", "3" }, w1);
        AssertTimes(TrackTwoDue, t1);
        // Player2: low C (one change down, with the note), then the top C two octaves up. Both windows share the
        // song's timing (Offset is off).
        var due2 = new double[] { 500, 500, 833, 1166, 1500 };
        var (w2, t2) = Window(2, due2, start);
        Assert.Equal(new[] { "9", "1", "0", "0", "8" }, w2);
        Assert.InRange(SongStart(2, due2) - start, -10, 10);
        AssertTimes(due2.Select(d => d + (SongStart(2, due2) - start)).ToArray(), t2);
    }

    [Fact]
    public void TheDefaultTempoSlowsEverySongAndTheTempoKeysCountFromIt()
    {
        using var solo = new SoloSession(_view, _solo.Library, () => "130", () => 1, _clock, _keys, tailMs: 50, gapMs: 50)
        {
            DefaultSpeed = () => 0.95,
        };
        solo.PlaySong("Duet");
        SpinWait.SpinUntil(() => KeysIn(1).Count >= 3, 5000);
        var p = _keys.All.Where(x => x.Window == 1).ToList();
        Assert.InRange(p[2].Time - p[1].Time, 200 / 0.95 - 10, 200 / 0.95 + 15);   // 200 ms apart at 95%: about 211
        solo.ChangeTempo(1);
        Assert.Contains("Tempo 96%", _view.Dialog);
    }

    [Fact]
    public void TempoKeysChangeTheSongPlayingSolo()
    {
        _solo.PlaySong("Duet");
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 1, 5000);
        for (var i = 0; i < 25; i++) _solo.ChangeTempo(-1);   // 75%: 200 ms apart becomes about 267
        Assert.Contains("Tempo 75%", _view.Dialog);
        SpinWait.SpinUntil(() => KeysIn(1).Count >= 5, 5000);
        var p = _keys.All.Where(x => x.Window == 1).ToList();
        Assert.InRange(p[4].Time - p[3].Time, 267 - 20, 267 + 20);
    }
}
