using GW2MM.Core.Config;
using GW2MM.Core.Library;
using Microsoft.Win32;

namespace GW2MM.Windows.Forms;

/// <summary>
/// "New Song": imports a MIDI into the library (Midi\, Data.csv entry, Csv\ conversion). A MuseScore score (.mscz) is
/// first exported to MIDI by MuseScore 4, which must be installed.
/// </summary>
internal sealed class NewSongForm : ThemedDialog
{
    private readonly SongLibrary _library;
    private readonly AppSettings _settings;
    private readonly string _dataDir;
    private readonly Action _added;
    private readonly ThemedButton _add;
    private readonly FileDropZone _file;
    private readonly FieldBox _title, _description;
    private string _path = ""; // the chosen file
    private readonly Stepper _players, _tempo;
    private readonly Label _tempoHint;

    public NewSongForm(SongLibrary library, AppSettings settings, string dataDir, Action added) : base("New Song", 440, "New song")
    {
        _library = library;
        _settings = settings;
        _dataDir = dataDir;
        _added = added;
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = DroppedFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        DragDrop += (_, e) => { if (DroppedFile(e) is { } f) UseFile(f); };

        // The file: click to browse, or drop a .mid / .mscz on it (or anywhere on the window).
        _file = new FileDropZone { Bounds = Theme.S(16, 14, 408, 64) };
        _file.Click += (_, _) => SelectFile();
        _file.DragEnter += (_, e) => { e.Effect = DroppedFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy; _file.DragOver_ = e.Effect != DragDropEffects.None; };
        _file.DragLeave += (_, _) => _file.DragOver_ = false;
        _file.DragDrop += (_, e) => { _file.DragOver_ = false; if (DroppedFile(e) is { } f) UseFile(f); };
        Body.Controls.Add(_file);

        _title = new FieldBox { Size = Theme.S(312, 30) };
        Row("Title", _title, 92);
        _players = new Stepper(1, 8, 1);
        Row("Players", _players, 132);
        _tempo = new Stepper(-10, 10, 0);
        Row("Tempo", _tempo, 172);
        _tempoHint = new Label { AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.Navy, BackColor = Theme.Ice, Padding = Theme.P(8, 3, 8, 3) };
        _tempoHint.Location = new Point(_tempo.Right + Theme.S(10), _tempo.Top + (_tempo.Height - _tempoHint.PreferredHeight) / 2);
        Body.Controls.Add(_tempoHint);
        _tempo.ValueChanged += (_, _) => UpdateTempoHint();
        UpdateTempoHint();
        _description = new FieldBox(multiline: true) { Size = Theme.S(312, 72) };
        Row("Description", _description, 212);
        SetBodyHeight(300);

        var add = _add = AddButton("Add song", ButtonKind.Primary);
        add.Click += async (_, _) => await AddSong();
        var cancel = AddButton("Cancel", ButtonKind.Secondary);
        cancel.Click += (_, _) => Hide();
        AcceptButton = add;
        CancelButton = cancel;

        void Row(string label, Control input, int y)
        {
            Body.Controls.Add(new Label
            {
                Text = label, Font = Theme.BodyBold, ForeColor = Theme.Ink, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                Bounds = Theme.S(16, y, 96, 30),
            });
            input.Location = Theme.S(112, y, 0, 0).Location;
            Body.Controls.Add(input);
        }
    }

    private void UpdateTempoHint()
    {
        var m = _tempo.Value;
        _tempoHint.Text = m == 0 ? "Normal speed" : $"{Math.Abs(m) * 5}% {(m > 0 ? "faster" : "slower")}";
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && (MuseScore.IsMidi(files[0]) || MuseScore.IsScore(files[0]))
            ? files[0] : null;

    /// <summary>Shows the chosen file's name; the title follows the file unless it was typed in.</summary>
    private void UseFile(string path)
    {
        var title = _title.Box.Text.Trim();
        if (title.Length == 0 || (_path.Length > 0 && title == Path.GetFileNameWithoutExtension(_path)))
            _title.Box.Text = Path.GetFileNameWithoutExtension(path);
        _path = path;
        _file.SetFile(path);
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
        _file.SetFile(null);
        _title.Box.Text = "";
        _description.Box.Text = "";
        _players.Value = 1;
        _tempo.Value = 0;
    }

    private async Task AddSong()
    {
        if (_path.Length == 0)
        {
            Dialogs.Show(this, "Choose a .mid or .mscz file first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            var r = _library.AddSong(new NewSongRequest(_title.Box.Text, _players.Value.ToString(), _tempo.Value.ToString(), _description.Box.Text, midi));
            if (r.ConversionError is not null)
                Dialogs.Show(this, "The MIDI file could not be fully converted: " + r.ConversionError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Dialogs.Show(this, $"\u201C{r.Title}\u201D was added to your library.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Reset();
            Hide();
            _added();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Dialogs.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        Dialogs.Show(this, "MuseScore 4 is needed to import .mscz files, but it wasn't found. Please select MuseScore4.exe.",
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

/// <summary>New Song's file area: a dashed drop target when empty, the chosen file (with Change…) once picked.</summary>
internal sealed class FileDropZone : Control
{
    private readonly ThemedToolTip _tip = new();
    private string? _path;
    private bool _hover, _dragOver;

    public FileDropZone()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AllowDrop = true;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Choose a .mid or .mscz file";
    }

    public bool DragOver_
    {
        get => _dragOver;
        set { _dragOver = value; Invalidate(); }
    }

    public void SetFile(string? path)
    {
        _path = path;
        AccessibleDescription = path;
        _tip.SetToolTip(this, path ?? "");
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space) OnClick(EventArgs.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(Theme.Sf(1), Theme.Sf(1), Width - Theme.Sf(2), Height - Theme.Sf(2));
        var radius = Theme.Sf(8);
        var empty = _path is null;
        if (empty && !_dragOver)
        {
            Theme.FillRound(g, r, radius, _hover ? Theme.Ice : Theme.IceHover);
            using var dash = new Pen(Theme.Azure, Theme.Sf(1.5f)) { DashPattern = new[] { 4f, 3f } };
            using var path = Theme.Round(r, radius);
            g.DrawPath(dash, path);
        }
        else
        {
            Theme.FillRound(g, r, radius, _dragOver ? Theme.Ice : _hover ? Theme.IceHover : Theme.Surface);
            Theme.DrawRound(g, r, radius, _dragOver || Focused ? Theme.Azure : Theme.Border, _dragOver || Focused ? 1.5f : 1f);
        }
        if (Focused && empty && !_dragOver) Theme.DrawRound(g, r, radius, Theme.Azure, 1.5f);

        var badge = new RectangleF(Theme.Sf(14), (Height - Theme.Sf(36)) / 2, Theme.Sf(36), Theme.Sf(36));
        Theme.FillRound(g, badge, Theme.Sf(9), empty ? Theme.Surface : Theme.Navy);
        if (empty) Theme.DrawRound(g, badge, Theme.Sf(9), Theme.Border);
        var s = Theme.Sf(18);
        Glyphs.Draw(g, Glyph.FileMusic, new RectangleF(badge.X + (badge.Width - s) / 2, badge.Y + (badge.Height - s) / 2, s, s), empty ? Theme.AzureText : Color.White);

        var x = (int)badge.Right + Theme.S(12);
        var change = empty ? "Browse…" : "Change…";
        var changeW = Theme.Measure(change, Theme.BodyBold).Width;
        var textW = Width - x - changeW - Theme.S(20);
        if (empty)
        {
            Theme.Text(g, "Drop a .mid or .mscz file here", Theme.BodyBold, new Rectangle(x, Theme.S(13), textW, Theme.S(18)), Theme.Title);
            Theme.Text(g, "or click to choose one", Theme.Small, new Rectangle(x, Theme.S(32), textW, Theme.S(16)), Theme.Muted);
        }
        else
        {
            Theme.Text(g, Path.GetFileName(_path!), Theme.BodyBold, new Rectangle(x, Theme.S(13), textW, Theme.S(18)), Theme.Title);
            var kind = MuseScore.IsScore(_path!) ? "MuseScore score · exported when added" : "MIDI file";
            var folder = Path.GetFileName(Path.GetDirectoryName(_path!) ?? "");
            Theme.Text(g, folder.Length > 0 ? $"{kind} · in {folder}" : kind, Theme.Small, new Rectangle(x, Theme.S(32), textW, Theme.S(16)), Theme.Muted);
        }
        Theme.Text(g, change, Theme.BodyBold, new Rectangle(Width - Theme.S(14) - changeW, 0, changeW + Theme.S(2), Height), Theme.AzureText);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
