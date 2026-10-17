using GW2MM.Core.Library;

namespace GW2MM.Windows.Forms;

/// <summary>"New Song": imports a MIDI into the library (Midi\, Data.csv entry, Csv\ conversion).</summary>
internal sealed class NewSongForm : Form
{
    private readonly SongLibrary _library;
    private readonly Action _added;
    private readonly TextBox _file, _title, _description;
    private readonly NumericUpDown _players, _tempo;
    private readonly Label _tempoHint;

    public NewSongForm(SongLibrary library, Action added)
    {
        _library = library;
        _added = added;
        Modern.InitDialog(this, "New Song");
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = DroppedMidi(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        DragDrop += (_, e) => { if (DroppedMidi(e) is { } f) UseFile(f); };

        var root = Modern.Stack(this);

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        root.Controls.Add(grid);

        _file = new TextBox { ReadOnly = true, Width = 226, PlaceholderText = "Choose or drop a .mid file", Margin = new Padding(0, 4, 0, 0) };
        var browse = Modern.Secondary("Browse…");
        browse.Click += (_, _) => SelectFile();
        var filePanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        filePanel.Controls.Add(_file);
        filePanel.Controls.Add(browse);
        AddRow(grid, "MIDI file", filePanel);

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

        var add = Modern.Primary("Add song");
        add.Click += (_, _) => AddSong();
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

    private static string? DroppedMidi(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files
        && (files[0].EndsWith(".mid", StringComparison.OrdinalIgnoreCase) || files[0].EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            ? files[0] : null;

    private void UseFile(string path)
    {
        _file.Text = path;
        if (_title.Text.Trim().Length == 0) _title.Text = Path.GetFileNameWithoutExtension(path);
    }

    private void SelectFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import a MIDI file",
            Filter = "MIDI files (*.mid; *.midi)|*.mid;*.midi",
            InitialDirectory = Directory.Exists(_library.MidiDir) ? _library.MidiDir : _library.Root,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) UseFile(dlg.FileName);
    }

    private void Reset()
    {
        _file.Text = "";
        _title.Text = "";
        _description.Text = "";
        _players.Value = 1;
        _tempo.Value = 0;
    }

    private void AddSong()
    {
        if (_file.Text.Length == 0)
        {
            MessageBox.Show(this, "Choose a MIDI file first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            var r = _library.AddSong(new NewSongRequest(_title.Text, ((int)_players.Value).ToString(), ((int)_tempo.Value).ToString(), _description.Text, _file.Text));
            if (r.ConversionError is not null)
                MessageBox.Show(this, "The MIDI file could not be fully converted: " + r.ConversionError, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            MessageBox.Show(this, $"\"{r.Title}\" was added to your library.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Reset();
            Hide();
            _added();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
