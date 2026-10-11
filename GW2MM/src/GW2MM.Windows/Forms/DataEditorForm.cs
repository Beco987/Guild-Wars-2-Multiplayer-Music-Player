using GW2MM.Core.Library;

namespace GW2MM.Windows.Forms;

/// <summary>The Data Editor (originally CsvEditor.ahk): double-click a line to edit it; Save writes Data.csv and closes.</summary>
internal sealed class DataEditorForm : Form
{
    private readonly DataFileEditor _editor;
    private readonly ListView _list;

    public DataEditorForm(DataFileEditor editor)
    {
        _editor = editor;
        Ui.InitForm(this, "Csv Editor", 457, 380);
        ForeColor = SystemColors.ControlText;
        _list = Ui.Add(this, new ListView
        {
            View = View.Details, GridLines = true, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        }, 10, 10, 437, 330);
        foreach (var c in new[] { "NAME", "PLAYERS", "TEMPO", "DESCRIPTION" }) _list.Columns.Add(c);
        _list.DoubleClick += (_, _) => EditSelected();
        Ui.Button(this, "Save", 10, 348, 60, 24, (_, _) => Save());
        Fill();
    }

    private void Fill()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var line in _editor.Lines) _list.Items.Add(new ListViewItem(DataFileEditor.Fields(line)));
        foreach (ColumnHeader c in _list.Columns) c.Width = -2;
        _list.EndUpdate();
    }

    private void EditSelected()
    {
        if (_list.SelectedIndices.Count == 0) return;
        var index = _list.SelectedIndices[0];
        var f = DataFileEditor.Fields(_editor.Lines[index]);
        using var dlg = new Form();
        Ui.InitForm(dlg, "NAME-INPUT", 520, 150);
        dlg.ForeColor = SystemColors.ControlText;
        var boxes = new TextBox[4];
        var names = new[] { "NAME", "PLAYERS", "TEMPO", "DESCRIPTION" };
        for (var i = 0; i < 4; i++)
        {
            Ui.Label(dlg, names[i], 10, 10 + i * 28, 90, 20);
            boxes[i] = Ui.Add(dlg, new TextBox { Text = f[i] }, 95, 6 + i * 28, 420, 22);
        }
        var ok = Ui.Button(dlg, "OK", 10, 118, 60, 26, (_, _) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); });
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _editor.EditLine(index, boxes[0].Text, boxes[1].Text, boxes[2].Text, boxes[3].Text);
        Fill();
    }

    private void Save()
    {
        try
        {
            _editor.Save();
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, Texts.AppTitle);
        }
    }
}
