using System.Text;
using GW2MM.Core.Config;
using GW2MM.Core.Library;
using GW2MM.Core.Text;

namespace GW2MM.Tests;

public sealed class LibraryAndConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-lib-" + Guid.NewGuid().ToString("N"));
    public LibraryAndConfigTests() { Directory.CreateDirectory(Path.Combine(_dir, "Csv")); Directory.CreateDirectory(Path.Combine(_dir, "Midi")); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string P(params string[] parts) => Path.Combine(new[] { _dir }.Concat(parts).ToArray());
    private SongLibrary Lib() => new(_dir);

    [Fact]
    public void SongListMatchesCsvFilesAgainstDataCsvFields()
    {
        File.WriteAllText(P("Data.csv"), "Alpha, 3, -1, Lute Bass\r\nBeta, 2, 0, \r\nGamma, 1, 0, not on disk\r\n");
        File.WriteAllText(P("Csv", "Alpha.csv"), "");
        File.WriteAllText(P("Csv", "Beta.csv"), "");
        File.WriteAllText(P("Csv", "Orphan.csv"), "");
        File.WriteAllText(P("Csv", "Upper.CSV"), ""); // ".csv" is removed case-sensitively, so this never matches
        var songs = Lib().ListSongs();
        Assert.Equal(new[] { "Alpha", "Beta" }, songs.Select(s => s.Name));
        Assert.Equal(" 3", songs[0].Players);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", songs[0].Date);
    }

    [Fact]
    public void DescriptionAndTempoFactorFollowTheOriginalLookups()
    {
        File.WriteAllText(P("Data.csv"), "Alpha, 3, -2, Lute Bass\nBeta, 2, 4, Horn");
        var lib = Lib();
        Assert.Equal(" Lute Bass", lib.Description("Alpha"));
        Assert.Equal(1.1, lib.TempoFactor("Alpha"), 10);
        Assert.Equal(0.8, lib.TempoFactor("Beta"), 10);
        Assert.Equal(1.0, lib.TempoFactor("Missing"));
        // Duplicate entries add up, as in the original.
        File.WriteAllText(P("Data.csv"), "Alpha, 3, -2, a\nAlpha, 3, -2, b");
        Assert.Equal(1.2, Lib().TempoFactor("Alpha"), 10);
        Assert.Equal(" b", Lib().Description("Alpha"));
    }

    [Fact]
    public void DeleteRemovesFilesAndEveryMatchingDataLine()
    {
        File.WriteAllText(P("Data.csv"), "Alpha, 3, -1, x\r\nBeta, 2, 0, y\r\nAlpha, 1, 0, z\r\n");
        File.WriteAllText(P("Csv", "Alpha.csv"), "");
        File.WriteAllText(P("Midi", "Alpha.mid"), "");
        var recycled = new List<string>();
        new SongLibrary(_dir, new ListRecycler(recycled)).Delete("Alpha");
        Assert.Equal(2, recycled.Count);
        Assert.Equal("Beta, 2, 0, y", File.ReadAllText(P("Data.csv")));
    }

    private sealed class ListRecycler(List<string> list) : IFileRecycler
    {
        public void Recycle(string path) { list.Add(path); File.Delete(path); }
    }

    [Fact]
    public void AddSongCopiesMidiAppendsEntryAndConvertsToCsv()
    {
        File.WriteAllText(P("Data.csv"), "Alpha, 3, -1, x\r\n");
        var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mid");
        File.Copy(Golden.Dir("midi", "yeah_right.mid"), src);
        try
        {
            var r = Lib().AddSong(new NewSongRequest("New, Song", "2", "-3", "line1\r\nline2", src));
            Assert.Equal("New  Song", r.Title);
            Assert.Null(r.ConversionError);
            Assert.True(File.Exists(src), "a file outside the Midi folder is copied, not moved");
            Assert.True(File.Exists(P("Midi", "New  Song.mid")));
            Assert.Equal(File.ReadAllBytes(Golden.Dir("midi", "yeah_right.csv")), File.ReadAllBytes(P("Csv", "New  Song.csv")));
            Assert.Equal("Alpha, 3, -1, x\r\n\nNew  Song, 2, -3, line1 line2", File.ReadAllText(P("Data.csv")));
            Assert.Contains(Lib().ListSongs(), s => s.Name == "New  Song");
        }
        finally { File.Delete(src); }
    }

    [Fact]
    public void AddSongRenamesAFileAlreadyInTheMidiFolder()
    {
        File.Copy(Golden.Dir("midi", "gen001.mid"), P("Midi", "raw.mid"));
        Lib().AddSong(new NewSongRequest("Tidy", "1", "0", "", P("Midi", "raw.mid")));
        Assert.False(File.Exists(P("Midi", "raw.mid")));
        Assert.True(File.Exists(P("Midi", "Tidy.mid")));
    }

    [Fact]
    public void AddSongValidatesInput()
    {
        Assert.Throws<ArgumentException>(() => Lib().AddSong(new NewSongRequest(" ", "1", "0", "", "x.mid")));
        Assert.Throws<FileNotFoundException>(() => Lib().AddSong(new NewSongRequest("T", "1", "0", "", P("nope.mid"))));
        Assert.False(File.Exists(P("Data.csv")));
    }

    [Fact]
    public void DataEditorCleansEditsAndRenamesFiles()
    {
        File.WriteAllText(P("Data.csv"), "Alpha, 3, -1, x\r\n,,,\r\n\r\nBeta, 2, 0, y, extra\r\n");
        File.WriteAllText(P("Csv", "Beta.csv"), "b");
        File.WriteAllText(P("Midi", "Beta.mid"), "m");
        var ed = DataFileEditor.Open(Lib());
        Assert.Equal("Alpha, 3, -1, x\nBeta, 2, 0, y, extra\n", File.ReadAllText(P("Data.csv")));
        Assert.Equal(new[] { "Beta", " 2", " 0", " y" }, DataFileEditor.Fields(ed.Lines[1]));
        ed.EditLine(1, "Gamma,", "2", "1", "new, desc");
        ed.Save();
        Assert.Equal("Alpha, 3, -1, x\nGamma,2,1,new desc\n", File.ReadAllText(P("Data.csv")));
        Assert.Equal("b", File.ReadAllText(P("Csv", "Gamma.csv")));
        Assert.True(File.Exists(P("Midi", "Gamma.mid")));
        Assert.False(File.Exists(P("Csv", "Beta.csv")));
    }

    [Fact]
    public void DataEditorCreatesAMissingFile()
    {
        var ed = DataFileEditor.Open(Lib());
        Assert.Equal(new[] { "col1,col2,col3,col4", "name,players,tempo,description" }, ed.Lines);
    }

    [Fact]
    public void ConfigReadsFlatFieldsAndRewritesOnlySettingLines()
    {
        File.WriteAllText(P("Config.txt"), ";Comment line, with a comma\r\nOctaveDelay,130\r\nAlwaysOnTop,0\r\n;Another\r\nIsTransparent,1");
        var s = AppSettings.Load(_dir);
        Assert.Equal("130", s.OctaveDelay);
        Assert.False(s.AlwaysOnTop);
        Assert.True(s.IsTransparent);
        s.OctaveDelay = "200";
        s.AlwaysOnTop = true;
        s.SaveDisplaySettings(_dir);
        Assert.Equal(";Comment line, with a comma\nOctaveDelay,200\nAlwaysOnTop,1\n;Another\nIsTransparent,1\n", File.ReadAllText(P("Config.txt")));
        s.Name = "Bob";
        s.SaveWithName(_dir);
        Assert.EndsWith("IsTransparent,1\nName,Bob\n", File.ReadAllText(P("Config.txt")));
        Assert.Equal("Bob", AppSettings.Load(_dir).Name);
        Assert.Equal(200, AppSettings.Load(_dir).OctaveDelayMs);
    }

    [Fact]
    public void MissingConfigGivesBlankOctaveDelay()
    {
        var s = AppSettings.Load(_dir);
        Assert.Equal("", s.OctaveDelay);
        Assert.Equal(0, s.OctaveDelayMs);
    }

    [Fact]
    public void AnsiFilesKeepTheirEncoding()
    {
        var ansi = LegacyText.Ansi;
        File.WriteAllBytes(P("Data.csv"), ansi.GetBytes("Beyoncé, 1, 0, café\r\n"));
        File.WriteAllText(P("Csv", "Beyoncé.csv"), "");
        Assert.Equal("Beyoncé", Lib().ListSongs().Single().Name);
        Lib().Delete("Nothing");
        Assert.Equal(ansi.GetBytes("Beyoncé, 1, 0, café"), File.ReadAllBytes(P("Data.csv")));
        File.WriteAllText(P("Data.csv"), "Zoë, 1, 0, x", new UTF8Encoding(true));
        Lib().Delete("Nothing");
        Assert.Equal(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("Zoë, 1, 0, x")), File.ReadAllBytes(P("Data.csv")));
    }
}
