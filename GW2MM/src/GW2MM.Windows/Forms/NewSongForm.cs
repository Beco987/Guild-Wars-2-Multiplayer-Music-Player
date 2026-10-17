using GW2MM.Core.Config;
using GW2MM.Core.Library;
using Microsoft.Win32;

namespace GW2MM.Windows.Forms;

/// <summary>
/// "New Song": imports a MIDI into the library (Midi\, Data.csv entry, Csv\ conversion). A MuseScore score (.mscz) is
/// first exported to MIDI by MuseScore 4, which must be installed.
/// </summary>
internal sealed class NewSongForm : Form
{
    private readonly SongLibrary _library;
    private readonly AppSettings _settings;
    private readonly string _dataDir;
    private readonly Action _added;
    private readonly Button _add;
    private readonly TextBox _file, _title, _description;
    private readonly ToolTip _fileTip = new();
    private string _path = ""; // the chosen file; the box shows only its name
    private readonly NumericUpDown _players, _tempo;
    private readonly Label _tempoHint;

    public NewSongForm(SongLibrary library, AppSettings settings, string dataDir, Action added)
    {
        _library = library;
        _settings = settings;
        _dataDir = dataDir;
        _added = added;
        Modern.InitDialog(this, "New Song");
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = DroppedFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        DragDrop += (_, e) => { if (DroppedFile(e) is { } f) UseFile(f); };

        var root = Modern.Stack(this);

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        root.Controls.Add(grid);

        _file = new TextBox { ReadOnly = true, Width = 226, PlaceholderText = "Choose or drop a .mid or .mscz file", Margin = new Padding(0, 4, 0, 0), Cursor = Cursors.Hand };
        _file.Click += (_, _) => SelectFile();
        // A file dropped on the box (not just around it) is taken too.
        _file.AllowDrop = true;
        _file.DragEnter += (_, e) => e.Effect = DroppedFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        _file.DragDrop += (_, e) => { if (DroppedFile(e) is { } f) UseFile(f); };
        var browse = Modern.Secondary("Browse…");
        browse.Click += (_, _) => SelectFile();
        var filePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        filePanel.Controls.Add(_file);
        filePanel.Controls.Add(browse);
        AddRow(grid, "File", filePanel);

        _title = new TextBox { Width = 334 };
        AddRow(grid, "Title", _title);

        _players = new NumericUpDown { Minimum = 1, Maximum = 8, Value = 1, Width = 70 };
        AddRow(grid, "Players", _players);

        _tempo = new NumericUpDown { Minimum = -10, Maximum = 10, Value = 0, Width = 70, Margin = new Padding(0) };
        _tempoHint = new Label { AutoSize = true, ForeColor = Modern.Accent, Margin = new Padding(10, 3, 0, 0) };
        _tempo.ValueChanged += (_, _) => UpdateTempoHint();
        var tempoPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        tempoPanel.Controls.Add(_tempo);
        tempoPanel.Controls.Add(_tempoHint);
        AddRow(grid, "Tempo", tempoPanel);
        UpdateTempoHint();

        _description = new TextBox { Multiline = true, Height = 64, Width = 334, ScrollBars = ScrollBars.Vertical };
        AddRow(grid, "Description", _description);

        var add = _add = Modern.Primary("Add song");
        add.Click += async (_, _) => await AddSong();
        var cancel = Modern.Secondary("Cancel");
        cancel.Click += (_, _) => Hide();
        root.Controls.Add(Modern.ButtonRow(add, cancel));
        AcceptButton = add;
        CancelButton = cancel;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control input)
    {
        grid.Controls.Add(Modern.FieldLabel(label));
        input.Margin = new Padding(0, 3, 0, 8);
        grid.Controls.Add(input);
    }

    private void UpdateTempoHint()
    {
        var m = (int)_tempo.Value;
        _tempoHint.Text = m == 0 ? "Normal speed" : $"{Math.Abs(m) * 5}% {(m > 0 ? "faster" : "slower")}";
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && (MuseScore.IsMidi(files[0]) || MuseScore.IsScore(files[0]))
            ? files[0] : null;

    /// <summary>Shows the chosen file's name; the title follows the file unless it was typed in.</summary>
    private void UseFile(string path)
    {
        var title = _title.Text.Trim();
        if (title.Length == 0 || (_path.Length > 0 && title == Path.GetFileNameWithoutExtension(_path)))
            _title.Text = Path.GetFileNameWithoutExtension(path);
        _path = path;
        _file.Text = Path.GetFileName(path);
        _fileTip.SetToolTip(_file, path);
    }

    private void SelectFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import a song",
            Filter = "MIDI or MuseScore files (*.mid; *.mscz)|*.mid;*.midi;*.mscz;*.mscx|MIDI files (*.mid)|*.mid;*.midi|MuseScore files (*.mscz)|*.mscz;*.mscx",
            InitialDirectory = Path.GetDirectoryName(_path) is { Length: > 0 } dir && Directory.Exists(dir) ? dir
                : Directory.Exists(_library.MidiDir) ? _library.MidiDir : _library.Root,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) UseFile(dlg.FileName);
    }

    private void Reset()
    {
        _path = "";
        _file.Text = "";
        _fileTip.SetToolTip(_file, null);
        _title.Text = "";
        _description.Text = "";
        _players.Value = 1;
        _tempo.Value = 0;
    }

    private async Task AddSong()
    {
        if (_path.Length == 0)
        {
            MessageBox.Show(this, "Choose a .mid or .mscz file first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string? exported = null;
        try
        {
            var midi = _path;
            if (MuseScore.IsScore(midi))
            {
                if (FindMuseScore() is not { } exe) return;
                exported = Path.Combine(Path.GetTempPath(), $"GW2MM-{Guid.NewGuid():N}.mid");
                _add.Enabled = false;
                _add.Text = "Converting…";
                UseWaitCursor = true;
                try { await MuseScore.ExportMidiAsync(exe, midi, exported); }
                finally { EndConverting(); }
                midi = exported;
            }
            var r = _library.AddSong(new NewSongRequest(_title.Text, ((int)_players.Value).ToString(), ((int)_tempo.Value).ToString(), _description.Text, midi));
            if (r.ConversionError is not null)
                MessageBox.Show(this, "The MIDI file could not be fully converted: " + r.ConversionError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            MessageBox.Show(this, $"\"{r.Title}\" was added to your library.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Reset();
            Hide();
            _added();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (exported is not null) try { File.Delete(exported); } catch (IOException) { }
        }
    }

    private void EndConverting()
    {
        _add.Enabled = true;
        _add.Text = "Add song";
        UseWaitCursor = false;
    }

    /// <summary>MuseScore4.exe: the saved path, the usual install folders, or what opens .mscz files; else the user picks it (and it is saved).</summary>
    private string? FindMuseScore()
    {
        if (MuseScore.Find(_settings.MuseScorePath, MuseScore.DefaultLocations().Append(ScoreOpener() ?? "")) is { } found) return found;
        MessageBox.Show(this, "MuseScore 4 is needed to import .mscz files, but it wasn't found. Please select MuseScore4.exe.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        using var dlg = new OpenFileDialog { Title = "Select MuseScore4.exe", Filter = "MuseScore (MuseScore4.exe)|MuseScore*.exe|Programs (*.exe)|*.exe" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return null;
        _settings.MuseScorePath = dlg.FileName;
        try { _settings.SaveMuseScorePath(_dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } // still used this time
        return dlg.FileName;
    }

    /// <summary>The program Windows opens .mscz files with, if any (from the file association).</summary>
    private static string? ScoreOpener()
    {
        try
        {
            if (Registry.ClassesRoot.OpenSubKey(".mscz")?.GetValue("") is not string progId || progId.Length == 0) return null;
            if (Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command")?.GetValue("") is not string command) return null;
            command = command.Trim();
            var exe = command.StartsWith('"') ? command[1..].Split('"')[0] : command.Split(' ')[0];
            return Path.GetFileName(exe).StartsWith("MuseScore", StringComparison.OrdinalIgnoreCase) ? exe : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The window is reused: closing hides it and keeps what was typed.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
