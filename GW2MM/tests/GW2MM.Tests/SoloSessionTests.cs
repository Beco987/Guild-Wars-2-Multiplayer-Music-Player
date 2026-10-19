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
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 10, 5000);
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, KeysIn(1));       // track 2
        Assert.Equal(new[] { "8", "0", "2", "3", "4" }, KeysIn(2));       // track 3
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
        SpinWait.SpinUntil(() => _keys.Presses.Count >= 10, 5000);
        var w1 = _keys.All.Where(p => p.Window == 1).ToList();
        var w3 = _keys.All.Where(p => p.Window == 2).ToList();
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, w1.Select(p => p.Key));
        Assert.Equal(new[] { "8", "0", "2", "3", "4" }, w3.Select(p => p.Key));
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
