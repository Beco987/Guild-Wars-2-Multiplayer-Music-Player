using GW2MM.Core.Config;
using GW2MM.Core.Library;

namespace GW2MM.Tests;

public sealed class MuseScoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-mscz-" + Guid.NewGuid().ToString("N"));
    public MuseScoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string P(string name) => Path.Combine(_dir, name);

    /// <summary>A stand-in for MuseScore4.exe: logs its arguments, then runs <paramref name="windows"/> or <paramref name="unix"/>.</summary>
    private string FakeMuseScore(string windows, string unix)
    {
        if (OperatingSystem.IsWindows())
        {
            var cmd = P("musescore.cmd");
            File.WriteAllText(cmd, "@echo off\r\necho %*> \"%~dp0args.txt\"\r\n" + windows + "\r\n");
            return cmd;
        }
        var sh = P("musescore.sh");
        File.WriteAllText(sh, "#!/bin/sh\necho \"$@\" > \"$(dirname \"$0\")/args.txt\"\n" + unix + "\n");
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Theory]
    [InlineData("song.mscz", true, false)]
    [InlineData("Song.MSCX", true, false)]
    [InlineData("song.mid", false, true)]
    [InlineData("song.MIDI", false, true)]
    [InlineData("song.mp3", false, false)]
    public void ScoresAndMidisAreRecognised(string name, bool score, bool midi)
    {
        Assert.Equal(score, MuseScore.IsScore(name));
        Assert.Equal(midi, MuseScore.IsMidi(name));
    }

    [Fact]
    public void FindTakesTheSavedPathFirstThenTheFirstThatExists()
    {
        File.WriteAllText(P("a.exe"), "");
        File.WriteAllText(P("b.exe"), "");
        Assert.Equal(P("b.exe"), MuseScore.Find(P("b.exe"), new[] { P("a.exe") }));
        Assert.Equal(P("a.exe"), MuseScore.Find(P("gone.exe"), new[] { P("missing.exe"), "", P("a.exe") }));
        Assert.Equal(P("a.exe"), MuseScore.Find("", new[] { P("a.exe") }));
        Assert.Null(MuseScore.Find(null, new[] { P("missing.exe") }));
    }

    [Fact]
    public async Task ExportRunsMuseScoreWithTheOutputThenTheScore()
    {
        var exe = FakeMuseScore("echo MThd> \"%~2\"", "echo MThd > \"$2\"");
        await MuseScore.ExportMidiAsync(exe, P("song.mscz"), P("song.mid"));
        Assert.True(File.Exists(P("song.mid")));
        var args = File.ReadAllText(P("args.txt")).Replace("\"", "").Trim();
        Assert.Equal($"-o {P("song.mid")} {P("song.mscz")}", args);
    }

    [Fact]
    public async Task AFailedExportIsReported()
    {
        var exe = FakeMuseScore("echo MThd> \"%~2\"\r\nexit /b 1", "echo MThd > \"$2\"\nexit 1");
        var ex = await Assert.ThrowsAsync<IOException>(() => MuseScore.ExportMidiAsync(exe, P("song.mscz"), P("song.mid")));
        Assert.Equal(MuseScore.ExportFailed, ex.Message);
    }

    [Fact]
    public async Task AnExportThatWritesNothingFails()
    {
        File.WriteAllText(P("song.mid"), "left over from before");
        var exe = FakeMuseScore("exit /b 0", "exit 0");
        var ex = await Assert.ThrowsAsync<IOException>(() => MuseScore.ExportMidiAsync(exe, P("song.mscz"), P("song.mid")));
        Assert.Equal(MuseScore.ExportFailed, ex.Message);
    }

    [Fact]
    public void TheMuseScorePathIsRemembered()
    {
        var s = AppSettings.Load(_dir);
        Assert.Equal("", s.MuseScorePath);
        s.MuseScorePath = @"D:\Apps\MuseScore 4\bin\MuseScore4.exe";
        s.SaveMuseScorePath(_dir);
        Assert.Equal(@"D:\Apps\MuseScore 4\bin\MuseScore4.exe", AppSettings.Load(_dir).MuseScorePath);
    }
}
