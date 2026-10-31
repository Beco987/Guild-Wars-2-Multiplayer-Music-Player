using GW2MM.Core.Library;

namespace GW2MM.Tests;

public sealed class PlaylistStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gw2mm-pl-" + Guid.NewGuid().ToString("N"));
    public PlaylistStoreTests() { Directory.CreateDirectory(Path.Combine(_dir, "Csv")); Directory.CreateDirectory(Path.Combine(_dir, "Midi")); }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private string P(params string[] parts) => Path.Combine(new[] { _dir }.Concat(parts).ToArray());

    [Fact]
    public void CreateSaveLoadRenameDelete()
    {
        var store = new PlaylistStore(_dir);
        Assert.Empty(store.Names());
        store.Create("  Evening set ");
        store.Save(new Playlist("Evening set", new[] { "Coffee", "Yeah Right", "Coffee" }));
        store.Create("another");
        Assert.Equal(new[] { "another", "Evening set" }, store.Names());
        Assert.Equal(new[] { "Coffee", "Yeah Right", "Coffee" }, store.Load("Evening set")!.Songs);

        store.Rename("Evening set", "Night set");
        Assert.Null(store.Load("Evening set"));
        Assert.Equal(3, store.Load("Night set")!.Songs.Count);
        store.Rename("Night set", "NIGHT SET"); // case-only rename is allowed
        Assert.Contains("NIGHT SET", store.Names());

        store.Delete("another");
        Assert.Equal(new[] { "NIGHT SET" }, store.Names());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("what?")]
    [InlineData("ends with dot.")]
    public void RejectsUnusableNames(string name) => Assert.Throws<ArgumentException>(() => new PlaylistStore(_dir).Create(name));

    [Fact]
    public void RejectsDuplicates()
    {
        var store = new PlaylistStore(_dir);
        store.Create("Set");
        Assert.Throws<ArgumentException>(() => store.Create("Set"));
        store.Create("Other");
        Assert.Throws<ArgumentException>(() => store.Rename("Other", "Set"));
    }

    [Fact]
    public void HandEditedFilesIgnoreBlankAndCommentLines()
    {
        Directory.CreateDirectory(P("Playlists"));
        File.WriteAllText(P("Playlists", "Mine.txt"), "# my favourites\n\nCoffee\n  Beyoncé – Halo  \n");
        Assert.Equal(new[] { "Coffee", "Beyoncé – Halo" }, new PlaylistStore(_dir).Load("Mine")!.Songs);
    }

    [Fact]
    public void DeletingASongRemovesItFromPlaylists()
    {
        var lib = new SongLibrary(_dir);
        File.WriteAllText(P("Data.csv"), "Alpha, 1, 0, x\nBeta, 1, 0, y\n");
        File.WriteAllText(P("Csv", "Alpha.csv"), "");
        lib.Playlists.Save(new Playlist("Set", new[] { "Alpha", "Beta", "Alpha" }));
        lib.Delete("Alpha");
        Assert.Equal(new[] { "Beta" }, lib.Playlists.Load("Set")!.Songs);
    }

    [Fact]
    public void RenamingASongInTheDataEditorRenamesItInPlaylists()
    {
        var lib = new SongLibrary(_dir);
        File.WriteAllText(P("Data.csv"), "Alpha, 1, 0, x\n");
        File.WriteAllText(P("Csv", "Alpha.csv"), "a");
        lib.Playlists.Save(new Playlist("Set", new[] { "Alpha", "Other" }));
        var ed = DataFileEditor.Open(lib);
        ed.EditLine(0, "Gamma", "1", "0", "x");
        ed.Save();
        Assert.Equal(new[] { "Gamma", "Other" }, lib.Playlists.Load("Set")!.Songs);
    }

    [Fact]
    public void OrderStartsAtTheChosenSong()
    {
        var songs = new[] { "a", "b", "c", "d" };
        var (same, start) = PlaylistOrder.Arrange(songs, 2, shuffle: false);
        Assert.Equal(songs, same);
        Assert.Equal(2, start);

        var (shuffled, first) = PlaylistOrder.Arrange(songs, 2, shuffle: true, new Random(1));
        Assert.Equal(0, first);
        Assert.Equal("c", shuffled[0]);
        Assert.Equal(songs.OrderBy(s => s), shuffled.OrderBy(s => s));

        Assert.Equal(0, PlaylistOrder.Arrange(Array.Empty<string>(), 3, true).Start);
    }
}
