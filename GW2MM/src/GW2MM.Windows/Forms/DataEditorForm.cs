using GW2MM.Core.Library;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The Data Editor (originally CsvEditor.ahk): edit Data.csv entries in place. Save writes the file and renames the
/// song's CSV/MIDI files when a name changed; Cancel (or closing) discards the edits.
/// </summary>
internal sealed class DataEditorForm : ThemedDialog
{
    private readonly DataFileEditor _editor;
    private readonly Action _saved_;
    private readonly ThemedGrid _grid;
    private readonly Label _count;
    private readonly FieldBox _search;
    private bool _saved;

    /// <param name="saved">Called after a successful save (the main window reloads its song list).</param>
    public DataEditorForm(DataFileEditor editor, Action saved)
        : base("Data Editor", 720, "Song data", "Click a cell to edit it. Renaming a song also renames its files. Changes are written when you press Save.")
    {
        _editor = editor;
        _saved_ = saved;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;

        // Footer and size first: the grid card below is anchored to the body's final size.
        _count = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.FooterBack };
        SetFooterLeft(_count);
        var save = AddButton("Save", ButtonKind.Primary);
        save.Click += (_, _) => Save();
        var cancel = AddButton("Cancel", ButtonKind.Secondary);
        cancel.Click += (_, _) => Close();
        CancelButton = cancel;
        SetBodyHeight(368);
        ClientSize = new Size(Theme.S(720), HeaderHeight + Theme.S(368) + Footer.Height);
        MinimumSize = new Size(Theme.S(520), Theme.S(340));

        // Search: shows only the songs whose names contain every word typed (Esc clears it).
        var search = _search = new FieldBox(filled: true) { Glyph = Glyph.Search, ClearButton = true, Bounds = Theme.S(16, 12, 280, 30) };
        search.Box.PlaceholderText = "Search songs";
        search.Box.TextChanged += (_, _) => Filter(search.Box.Text);
        // Without this the form's Cancel button takes the Esc key before the box sees it.
        search.Box.PreviewKeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && search.Box.TextLength > 0) e.IsInputKey = true; };
        search.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && search.Box.TextLength > 0) { search.Box.Clear(); e.SuppressKeyPress = true; } };
        Body.Controls.Add(search);

        var card = new Card { Bounds = Theme.S(16, 54, 688, 300), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        _grid = new ThemedGrid
        {
            Bounds = new Rectangle(Theme.S(2), Theme.S(4), card.Width - Theme.S(4), card.Height - Theme.S(8)),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
            BackgroundColor = Theme.Surface, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = Theme.S(32),
            EnableHeadersVisualStyles = false, GridColor = Theme.Divider, Font = Theme.Body,
        };
        _grid.RowTemplate.Height = Theme.S(28);
        var header = _grid.ColumnHeadersDefaultCellStyle;
        header.BackColor = header.SelectionBackColor = Theme.Surface;
        header.ForeColor = header.SelectionForeColor = Theme.Muted;
        header.Font = Theme.Overline;
        header.Padding = Theme.P(6, 0, 6, 0);
        var cells = _grid.DefaultCellStyle;
        cells.BackColor = Theme.Surface;
        cells.ForeColor = Theme.Ink;
        cells.SelectionBackColor = Theme.Ice;
        cells.SelectionForeColor = Theme.Title;
        cells.Padding = Theme.P(6, 0, 6, 0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Stripe;
        ThemedScrollBar.Attach(_grid);
        AddColumn("Name", 38);
        AddColumn("Players", 10);
        AddColumn("Tempo", 10);
        AddColumn("Description", 42);
        _grid.CellValueChanged += (_, _) => UpdateCount();
        card.Controls.Add(_grid);
        Body.Controls.Add(card);


        foreach (var line in _editor.Lines)
        {
            var f = DataFileEditor.Fields(line).Select(x => x.Trim()).ToArray();
            var row = _grid.Rows[_grid.Rows.Add(f.Cast<object>().ToArray())];
            row.Tag = f;
        }
        UpdateCount();
        ActiveControl = search.Box;
    }

    private void Filter(string query)
    {
        _grid.EndEdit();
        _grid.CurrentCell = null; // a hidden row cannot hold the current cell
        foreach (DataGridViewRow row in _grid.Rows)
            row.Visible = new Core.Library.SongEntry(Cell(row, 0), "", "").Matches(query);
    }

    // Not sortable: Save maps grid row i to Data.csv line i.
    private void AddColumn(string header, float weight) =>
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header.ToUpperInvariant(), FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

    private static string Cell(DataGridViewRow row, int i) => (row.Cells[i].Value as string ?? "").Trim();

    private IEnumerable<int> ChangedRows() =>
        _grid.Rows.Cast<DataGridViewRow>().Where(r => Enumerable.Range(0, 4).Any(i => Cell(r, i) != ((string[])r.Tag!)[i])).Select(r => r.Index);

    private void UpdateCount()
    {
        var changed = ChangedRows().Count();
        _count.Text = $"{_grid.Rows.Count} entries" + (changed > 0 ? $" · {changed} changed" : "");
        _count.ForeColor = changed > 0 ? Theme.WarningText : Theme.Muted;
    }

    private void Save()
    {
        _grid.EndEdit();
        foreach (var i in ChangedRows().ToList())
        {
            var r = _grid.Rows[i];
            if (Cell(r, 0).Replace(",", "").Length == 0)
            {
                Dialogs.Show(this, $"Row {i + 1} has no name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _editor.EditLine(i, Cell(r, 0), Cell(r, 1), Cell(r, 2), Cell(r, 3));
        }
        try
        {
            _editor.Save();
            _saved = true;
            _saved_();
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Show(this, "Could not save: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // EditOnEnter would start editing the first cell (taking the focus) as soon as the window opens.
        _grid.CurrentCell = null;
        _search.Box.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _grid.EndEdit();
        if (!_saved && e.CloseReason == CloseReason.UserClosing && ChangedRows().Any()
            && Dialogs.Show(this, "Your edits have not been saved.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                heading: "Discard your changes?", yes: "Discard", no: "Keep editing", danger: true) != DialogResult.Yes)
            e.Cancel = true;
        base.OnFormClosing(e);
    }
}
