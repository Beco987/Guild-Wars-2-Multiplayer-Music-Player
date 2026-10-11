using GW2MM.Core.Text;

namespace GW2MM.Core.Library;

/// <summary>
/// The "Data Editor" (originally the separate CsvEditor.ahk): lists Data.csv lines, edits one line at a time and
/// on Save renames the song's CSV/MIDI files when a name changed.
/// </summary>
public sealed class DataFileEditor
{
    private readonly SongLibrary _library;
    private readonly List<string> _beforeNames;

    private DataFileEditor(SongLibrary library, List<string> lines)
    {
        _library = library;
        Lines = lines;
        _beforeNames = lines.Select(l => Fields(l)[0]).ToList();
    }

    public List<string> Lines { get; }

    /// <summary>
    /// CsvEditor.ahk wrote these two template lines into a missing Data.csv. They are not songs and only showed up as
    /// odd rows in the editor, so they are no longer written and are removed when found.
    /// </summary>
    private static readonly string[] TemplateLines = { "col1,col2,col3,col4", "name,players,tempo,description" };

    /// <summary>
    /// Opening the editor creates an empty Data.csv if it is missing and removes lines that are empty, contain only
    /// commas or are the old template lines (rewriting the file), as CsvEditor.ahk did on start.
    /// </summary>
    public static DataFileEditor Open(SongLibrary library)
    {
        var path = library.DataCsvPath;
        if (!File.Exists(path)) LegacyText.Write(path, "");
        var text = LegacyText.ReadText(path);
        var lines = text.Split('\n').Select(l => l.Trim('\r'))
            .Where(l => l.Any(c => c != ',') && !TemplateLines.Contains(l.Trim(), StringComparer.OrdinalIgnoreCase)).ToList();
        LegacyText.Write(path, string.Concat(lines.Select(l => l + "\n")));
        return new DataFileEditor(library, lines);
    }

    /// <summary>NAME, PLAYERS, TEMPO, DESCRIPTION of a line (fields split on every comma; missing ones are blank).</summary>
    public static string[] Fields(string line)
    {
        var parts = line.Split(',');
        return Enumerable.Range(0, 4).Select(i => i < parts.Length ? parts[i] : "").ToArray();
    }

    public void EditLine(int index, string name, string players, string tempo, string description)
    {
        static string Clean(string s) => s.Replace(",", "");
        Lines[index] = string.Join(",", Clean(name), Clean(players), Clean(tempo), Clean(description));
    }

    /// <summary>
    /// Writes Data.csv; for each line whose name changed (case-insensitively) moves Csv\old.csv and Midi\old.mid
    /// and renames the song in every playlist.
    /// </summary>
    public void Save()
    {
        for (var i = 0; i < _beforeNames.Count && i < Lines.Count; i++)
        {
            var before = _beforeNames[i];
            var after = Fields(Lines[i])[0];
            if (string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) continue;
            MoveIfExists(Path.Combine(_library.CsvDir, before + ".csv"), Path.Combine(_library.CsvDir, after + ".csv"));
            MoveIfExists(Path.Combine(_library.MidiDir, before + ".mid"), Path.Combine(_library.MidiDir, after + ".mid"));
            _library.Playlists.RenameSong(before, after);
        }
        LegacyText.Write(_library.DataCsvPath, string.Concat(Lines.Select(l => l + "\n")));
    }

    private static void MoveIfExists(string from, string to)
    {
        if (File.Exists(from)) File.Move(from, to, overwrite: true);
    }
}
