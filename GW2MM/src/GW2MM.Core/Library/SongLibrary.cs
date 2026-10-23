using System.Globalization;
using GW2MM.Core.Ahk;
using GW2MM.Core.Midi;
using GW2MM.Core.Text;

namespace GW2MM.Core.Library;

/// <summary>Sends files to the Recycle Bin (the original used FileRecycle); implemented by the Windows layer.</summary>
public interface IFileRecycler
{
    void Recycle(string path);
}

public sealed class DeleteFileRecycler : IFileRecycler
{
    public void Recycle(string path) => File.Delete(path);
}

/// <summary>A row of the main song list.</summary>
/// <param name="Added">When the song was added: the full timestamp behind <paramref name="Date"/>, for sorting.</param>
public sealed record SongEntry(string Name, string Players, string Date, DateTime Added = default)
{
    /// <summary>
    /// The song list's search: every word of <paramref name="query"/> appears in the name (any order, any case,
    /// ignoring accents); an empty query matches everything.
    /// </summary>
    public bool Matches(string query)
    {
        var name = Fold(Name);
        return query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(w => name.Contains(Fold(w), StringComparison.Ordinal));
    }

    private static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var b = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                b.Append(char.ToLowerInvariant(c));
        return b.ToString();
    }
}

public sealed record NewSongRequest(string Title, string Players, string TempoModifier, string Description, string SelectedMidiPath);

public sealed record AddSongResult(string Title, string CsvPath, string? ConversionError);

/// <summary>
/// The song library in a data folder: Data.csv (Title, Players, TempoModifier, Description), Csv\*.csv
/// (midicsv output, one per song) and Midi\*.mid. Files keep the original layout and encoding so the same folder
/// works with the original scripts.
/// </summary>
public sealed class SongLibrary
{
    private readonly IFileRecycler _recycler;

    public SongLibrary(string root, IFileRecycler? recycler = null)
    {
        Root = root;
        _recycler = recycler ?? new DeleteFileRecycler();
        Playlists = new PlaylistStore(root);
    }

    public string Root { get; }
    public PlaylistStore Playlists { get; }
    public string DataCsvPath => Path.Combine(Root, "Data.csv");
    public string CsvDir => Path.Combine(Root, "Csv");
    public string MidiDir => Path.Combine(Root, "Midi");

    public string SongCsvPath(string name) => Path.Combine(CsvDir, name + ".csv");

    /// <summary>
    /// Data.csv as the original read it: all lines split on commas into one flat list. Lookups match a song name
    /// against any field and take the following fields as Players/Tempo/Description.
    /// </summary>
    public List<string> DataFields()
    {
        if (!File.Exists(DataCsvPath)) return new List<string>();
        return LegacyText.Lines(LegacyText.ReadText(DataCsvPath)).SelectMany(l => l.Length == 0 ? Array.Empty<string>() : l.Split(',')).ToList();
    }

    /// <summary>
    /// The main list (ReloadSongs): every file in Csv\ whose name (with ".csv" removed, case-sensitively) appears
    /// in Data.csv, once per matching field, showing the next field as Players and the date the song was added
    /// (see <see cref="AddedTime"/>).
    /// </summary>
    /// <summary>
    /// When a song file was added: its creation time, or its last-modified time when that is earlier. Copying a file
    /// (to another PC, or a backup restored by copy-paste) gives the copy a new creation time but keeps the
    /// modified time, so the earlier of the two is still the original date. (The original showed the creation time.)
    /// </summary>
    public static DateTime AddedTime(string file)
    {
        var created = File.GetCreationTime(file);
        var modified = File.GetLastWriteTime(file);
        return modified < created ? modified : created;
    }

    public List<SongEntry> ListSongs()
    {
        var result = new List<SongEntry>();
        if (!Directory.Exists(CsvDir)) return result;
        var fields = DataFields();
        foreach (var file in Directory.EnumerateFiles(CsvDir).OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file).Replace(".csv", "", StringComparison.Ordinal);
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i] != name) continue;
                var added = AddedTime(file);
                result.Add(new SongEntry(name, i + 1 < fields.Count ? fields[i + 1] : "", added.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), added));
            }
        }
        return result;
    }

    /// <summary>The description shown when a song is selected (the field three after the last matching name).</summary>
    public string Description(string name)
    {
        var fields = DataFields();
        var result = "";
        for (var i = 0; i < fields.Count; i++)
            if (fields[i] == name) result = i + 3 < fields.Count ? fields[i + 3] : "";
        return result;
    }

    /// <summary>
    /// A one-player song (Players = 1, the field after the last matching name): it is played with all tracks merged
    /// into one instrument (see <see cref="Arrangement.SingleInstrument"/>).
    /// </summary>
    public bool IsSingleInstrument(string name)
    {
        var fields = DataFields();
        string? players = null;
        for (var i = 0; i < fields.Count; i++)
            if (fields[i] == name) players = i + 1 < fields.Count ? fields[i + 1] : "";
        return players is not null && AhkValue.Number(players.Trim()) == 1;
    }

    /// <summary>The song's tempo factor ("DefaultTempo"): 1 - 0.05 * modifier, summed over every matching entry.</summary>
    public double TempoFactor(string name) => TempoFactorFromFields(DataFields(), name);

    public static double TempoFactorFromFields(IReadOnlyList<string> fields, string name)
    {
        double tempo = 1;
        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i] != name) continue;
            var m = AhkValue.Number(i + 2 < fields.Count ? fields[i + 2] : "");
            if (m is not null) tempo += m.Value * 0.05 * -1;
        }
        return tempo;
    }

    /// <summary>The song's CSV text, or null when Csv\&lt;name&gt;.csv does not exist (the original then played nothing).</summary>
    public string? ReadSongCsv(string name)
    {
        var path = SongCsvPath(name);
        return File.Exists(path) ? LegacyText.ReadText(path) : null;
    }

    /// <summary>
    /// "Delete Song": recycles the CSV and MIDI, removes every Data.csv line with a field equal to the name, and
    /// removes the song from every playlist.
    /// </summary>
    public void Delete(string name)
    {
        foreach (var path in new[] { SongCsvPath(name), Path.Combine(MidiDir, name + ".mid") })
            if (File.Exists(path)) _recycler.Recycle(path);
        Playlists.RemoveSong(name);
        if (!File.Exists(DataCsvPath)) return;
        var kept = LegacyText.Lines(LegacyText.ReadText(DataCsvPath)).Where(l => l.Length == 0 || !l.Split(',').Contains(name)).ToList();
        LegacyText.Write(DataCsvPath, string.Join("\n", kept));
    }

    /// <summary>
    /// "Add song": places the MIDI at Midi\&lt;Title&gt;.mid, appends the Data.csv entry and converts the MIDI to
    /// Csv\&lt;Title&gt;.csv (in-process, byte-compatible with the bundled Midicsv.exe).
    /// </summary>
    public AddSongResult AddSong(NewSongRequest request)
    {
        var title = Sanitize(request.Title);
        var description = Sanitize(request.Description);
        if (title.Trim().Length == 0) throw new ArgumentException("Please enter a title.");
        if (!File.Exists(request.SelectedMidiPath)) throw new FileNotFoundException("Please import a MIDI file.", request.SelectedMidiPath);
        if (title.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The title contains characters that cannot be used in a file name.");

        Directory.CreateDirectory(MidiDir);
        Directory.CreateDirectory(CsvDir);
        var target = Path.Combine(MidiDir, title + ".mid");
        var selected = Path.GetFullPath(request.SelectedMidiPath);
        var inMidiFolder = string.Equals(Path.GetDirectoryName(selected), Path.GetFullPath(MidiDir), StringComparison.OrdinalIgnoreCase);
        if (inMidiFolder)
        {
            // A file already in the Midi folder is renamed to the title (overwriting), as before.
            if (!string.Equals(selected, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Move(selected, target, overwrite: true);
        }
        else if (!File.Exists(target))
        {
            // Otherwise it is copied; like the original FileCopy, an existing Midi\<Title>.mid is kept.
            File.Copy(selected, target);
        }

        LegacyText.Append(DataCsvPath, $"\n{title}, {request.Players}, {request.TempoModifier}, {description}");

        var csvPath = SongCsvPath(title);
        var result = MidiCsvConverter.ConvertFile(target, csvPath);
        return new AddSongResult(title, csvPath, result.Error);
    }

    // Commas and line breaks would corrupt Data.csv.
    private static string Sanitize(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace(',', ' ');
}
