using GW2MM.Core.Library;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The Data Editor (originally CsvEditor.ahk): edit Data.csv entries in place. Save writes the file and renames the
/// song's CSV/MIDI files when a name changed; Cancel (or closing) discards the edits.
/// </summary>
internal sealed class DataEditorForm : Form
{
    private readonly DataFileEditor _editor;
    private readonly DataGridView _grid;
    private readonly Label _count;
    private bool _saved;

    public DataEditorForm(DataFileEditor editor)
    {
        _editor = editor;
        Modern.InitDialog(this, "Data Editor");
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        AutoSize = false;
        ClientSize = new Size(720, 460);
        MinimumSize = new Size(520, 320);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(Modern.Heading("Song data"));
        root.Controls.Add(Modern.Caption("Click a cell to edit it. Renaming a song also renames its files in the Csv and Midi folders. " +
                                         "Commas are removed when saving. Changes are only written when you press Save.", 680));

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, BackgroundColor = Modern.Surface, BorderStyle = BorderStyle.FixedSingle,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            EnableHeadersVisualStyles = false, GridColor = Modern.Border, Margin = new Padding(0, 0, 0, 4),
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Modern.Background;
        _grid.ColumnHeadersDefaultCellStyle.Font = Modern.Bold;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Modern.Accent;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Modern.Stripe;
        _grid.DefaultCellStyle.SelectionBackColor = Modern.AccentHover;
        AddColumn("Name", 38);
        AddColumn("Players", 10);
        AddColumn("Tempo", 10);
        AddColumn("Description", 42);
        _grid.CellValueChanged += (_, _) => UpdateCount();
        root.Controls.Add(_grid);

        var save = Modern.Primary("Save");
        save.Click += (_, _) => Save();
        var cancel = Modern.Secondary("Cancel");
        cancel.Click += (_, _) => Close();
        _count = new Label { AutoSize = true, ForeColor = Modern.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 22, 0, 0) };
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_count);
        bottom.Controls.Add(Modern.ButtonRow(save, cancel));
        root.Controls.Add(bottom);
        CancelButton = cancel;

        foreach (var line in _editor.Lines)
        {
            var f = DataFileEditor.Fields(line).Select(x => x.Trim()).ToArray();
            var row = _grid.Rows[_grid.Rows.Add(f.Cast<object>().ToArray())];
            row.Tag = f;
        }
        UpdateCount();
    }

    private void AddColumn(string header, float weight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

    private static string Cell(DataGridViewRow row, int i) => (row.Cells[i].Value as string ?? "").Trim();

    private IEnumerable<int> ChangedRows() =>
        _grid.Rows.Cast<DataGridViewRow>().Where(r => Enumerable.Range(0, 4).Any(i => Cell(r, i) != ((string[])r.Tag!)[i])).Select(r => r.Index);

    private void UpdateCount()
    {
        var changed = ChangedRows().Count();
        _count.Text = $"{_grid.Rows.Count} entries" + (changed > 0 ? $" · {changed} changed" : "");
    }

    private void Save()
    {
        _grid.EndEdit();
        foreach (var i in ChangedRows().ToList())
        {
            var r = _grid.Rows[i];
            if (Cell(r, 0).Replace(",", "").Length == 0)
            {
                MessageBox.Show(this, $"Row {i + 1} has no name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _editor.EditLine(i, Cell(r, 0), Cell(r, 1), Cell(r, 2), Cell(r, 3));
        }
        try
        {
            _editor.Save();
            _saved = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Could not save: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _grid.EndEdit();
        if (!_saved && e.CloseReason == CloseReason.UserClosing && ChangedRows().Any()
            && MessageBox.Show(this, "Discard your changes?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }
}
