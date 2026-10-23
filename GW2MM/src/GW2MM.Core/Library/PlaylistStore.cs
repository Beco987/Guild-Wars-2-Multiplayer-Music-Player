using System.Text;

namespace GW2MM.Core.Library;

/// <summary>A named, ordered list of song names (as in the song list, i.e. Csv\&lt;name&gt;.csv).</summary>
public sealed record Playlist(string Name, IReadOnlyList<string> Songs);

/// <summary>
/// Playlists in a data folder: Playlists\&lt;name&gt;.txt, one song name per line (blank lines and lines starting
/// with '#' are ignored), UTF-8. Plain text so playlists are easy to back up, share or edit by hand. Not in the
/// original.
/// </summary>
public sealed class PlaylistStore
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public PlaylistStore(string root) => Dir = Path.Combine(root, "Playlists");

    public string Dir { get; }

    private string PathOf(string name) => Path.Combine(Dir, name + ".txt");

    /// <summary>Playlist names, alphabetically.</summary>
    public IReadOnlyList<string> Names() =>
        Directory.Exists(Dir)
            ? Directory.EnumerateFiles(Dir, "*.txt").Select(f => Path.GetFileNameWithoutExtension(f)!)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList()
            : new List<string>();

    public bool Exists(string name) => File.Exists(PathOf(name));

    /// <summary>The playlist, or null when it does not exist.</summary>
    public Playlist? Load(string name)
    {
        var path = PathOf(name);
        if (!File.Exists(path)) return null;
        var songs = File.ReadAllLines(path, Utf8).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        return new Playlist(name, songs);
    }

    public void Save(Playlist playlist)
    {
        CheckName(playlist.Name);
        Directory.CreateDirectory(Dir);
        File.WriteAllLines(PathOf(playlist.Name), playlist.Songs, Utf8);
    }

    /// <summary>Creates an empty playlist. Throws <see cref="ArgumentException"/> (with a message for the user) when the name is unusable or taken.</summary>
    public Playlist Create(string name)
    {
        name = name.Trim();
        CheckName(name);
        if (Exists(name)) throw new ArgumentException($"A playlist called \"{name}\" already exists.");
        var playlist = new Playlist(name, Array.Empty<string>());
        Save(playlist);
        return playlist;
    }

    public void Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        CheckName(newName);
        if (oldName == newName) return;
        var differsOnlyInCase = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);
        if (!differsOnlyInCase && Exists(newName)) throw new ArgumentException($"A playlist called \"{newName}\" already exists.");
        var playlist = Load(oldName) ?? throw new ArgumentException($"The playlist \"{oldName}\" no longer exists.");
        File.Delete(PathOf(oldName));
        Save(playlist with { Name = newName });
    }

    public void Delete(string name)
    {
        var path = PathOf(name);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>A song was renamed (Data Editor): every playlist follows it.</summary>
    public void RenameSong(string oldName, string newName)
    {
        foreach (var name in Names())
        {
            var p = Load(name);
            if (p is null || !p.Songs.Contains(oldName)) continue;
            Save(p with { Songs = p.Songs.Select(s => s == oldName ? newName : s).ToList() });
        }
    }

    /// <summary>A song was deleted: it is removed from every playlist.</summary>
    public void RemoveSong(string songName)
    {
        foreach (var name in Names())
        {
            var p = Load(name);
            if (p is null || !p.Songs.Contains(songName)) continue;
            Save(p with { Songs = p.Songs.Where(s => s != songName).ToList() });
        }
    }

    public static void CheckName(string name)
    {
        if (name.Trim().Length == 0) throw new ArgumentException("Please enter a playlist name.");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            throw new ArgumentException("A playlist name cannot contain any of these characters: \\ / : * ? \" < > |");
        if (name.EndsWith('.') || name.Length > 100) throw new ArgumentException("Please choose a shorter name that does not end with a dot.");
    }
}

/// <summary>Playing order of a playlist.</summary>
public static class PlaylistOrder
{
    /// <summary>
    /// The songs in the order they will play and the index to start at. Without shuffle that is the playlist as it
    /// is, starting at <paramref name="startIndex"/>. With shuffle the chosen song plays first, followed by the
    /// others in random order.
    /// </summary>
    public static (IReadOnlyList<T> Songs, int Start) Arrange<T>(IReadOnlyList<T> songs, int startIndex, bool shuffle, Random? random = null)
    {
        if (songs.Count == 0) return (songs, 0);
        startIndex = Math.Clamp(startIndex, 0, songs.Count - 1);
        if (!shuffle) return (songs, startIndex);
        random ??= Random.Shared;
        var rest = songs.Where((_, i) => i != startIndex).ToArray();
        random.Shuffle(rest);
        return (new[] { songs[startIndex] }.Concat(rest).ToList(), 0);
    }
}
