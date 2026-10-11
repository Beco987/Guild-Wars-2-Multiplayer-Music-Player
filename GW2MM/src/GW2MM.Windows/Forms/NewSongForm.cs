using GW2MM.Core.Library;

namespace GW2MM.Windows.Forms;

/// <summary>"New Song": imports a MIDI into the library (Midi\, Data.csv entry, Csv\ conversion).</summary>
internal sealed class NewSongForm : Form
{
    private readonly SongLibrary _library;
    private readonly Action _added;
    private readonly TextBox _title, _description;
    private readonly ComboBox _players, _tempo;
    private readonly Label _fileName;
    private string? _selected;

    public NewSongForm(SongLibrary library, Action added)
    {
        _library = library;
        _added = added;
        Ui.InitForm(this, "New Song", 200, 200);
        ShowInTaskbar = false;
        Ui.Label(this, "Title :", 0, 3, 30, 20);
        _title = Ui.Add(this, new TextBox(), 30, 0, 170, 20);
        Ui.Label(this, "Players :", 0, 22, 80, 20);
        Ui.Button(this, "?", 80, 20, 20, 20, (_, _) => Ui.ShowText(this, "Players Explanation", Texts.Players, 100));
        _players = Ui.Add(this, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 100, 20, 100, 20);
        _players.Items.AddRange(Enumerable.Range(1, 8).Select(i => (object)i.ToString()).ToArray());
        Ui.Label(this, "Tempo Modifier :", 0, 42, 80, 20);
        Ui.Button(this, "?", 80, 40, 20, 20, (_, _) => Ui.ShowText(this, "Tempo Explanation", Texts.Tempo));
        _tempo = Ui.Add(this, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 100, 40, 100, 20);
        _tempo.Items.AddRange(Enumerable.Range(-10, 21).Select(i => (object)i.ToString()).ToArray());
        Ui.Label(this, "Description :", 0, 77, 80, 20);
        Ui.Button(this, "?", 80, 75, 20, 20, (_, _) => Ui.ShowText(this, "Description Explanation", Texts.Description, 100));
        _description = Ui.Add(this, new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical }, 100, 60, 100, 50);
        Ui.Button(this, "Import Midi", 0, 110, 55, 50, (_, _) => SelectFile());
        _fileName = Ui.Label(this, "", 60, 130, 140, 20);
        Ui.Button(this, "Add song", 70, 160, 75, 40, (_, _) => AddSong());
        Reset();
    }

    private void Reset()
    {
        _title.Text = "";
        _description.Text = "";
        _players.SelectedIndex = 0;
        _tempo.SelectedIndex = 10;
        _selected = null;
        _fileName.Text = "";
    }

    private void SelectFile()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Open a file",
            Filter = "MIDI files (*.mid; *.midi)|*.mid;*.midi",
            InitialDirectory = Directory.Exists(_library.MidiDir) ? _library.MidiDir : _library.Root,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _selected = dlg.FileName;
        _fileName.Text = Path.GetFileName(dlg.FileName);
    }

    private void AddSong()
    {
        if (_selected is null)
        {
            MessageBox.Show(this, "Please import a MIDI file first.", Texts.AppTitle);
            return;
        }
        try
        {
            var r = _library.AddSong(new NewSongRequest(_title.Text, (string)_players.SelectedItem!, (string)_tempo.SelectedItem!, _description.Text, _selected));
            if (r.ConversionError is not null)
                MessageBox.Show(this, "The MIDI file could not be fully converted: " + r.ConversionError, Texts.AppTitle);
            MessageBox.Show(this, "Done!", Texts.AppTitle);
            Reset();
            Hide();
            _added();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Texts.AppTitle);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Like the original GUI, the window is hidden and keeps its contents until the next song is added.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
