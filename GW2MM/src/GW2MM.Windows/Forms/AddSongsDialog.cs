namespace GW2MM.Windows.Forms;

/// <summary>Picks songs from the library to add to a playlist: a filter box and a checklist, in library order.</summary>
internal sealed class AddSongsDialog : Form
{
    private readonly IReadOnlyList<string> _all;
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private readonly CheckedListBox _list;
    private readonly TextBox _filter;
    private bool _filling;

    private AddSongsDialog(string playlist, IReadOnlyList<string> songs)
    {
        _all = songs;
        Modern.InitDialog(this, "Add songs");
        var stack = Modern.Stack(this);
        stack.Controls.Add(Modern.Heading($"Add songs to {playlist}"));
        stack.Controls.Add(Modern.Caption("Tick the songs to add. They are added in the order shown; you can reorder them afterwards.", 380));
        _filter = new TextBox { Width = 380, PlaceholderText = "Filter songs…", Margin = new Padding(0, 0, 0, 6) };
        stack.Controls.Add(_filter);
        _list = new CheckedListBox { Width = 380, Height = 300, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.FixedSingle };
        stack.Controls.Add(_list);
        var count = new Label { AutoSize = true, Font = Modern.Small, ForeColor = Modern.Muted, Margin = new Padding(0, 6, 0, 0) };
        stack.Controls.Add(count);
        var ok = Modern.Primary("Add");
        var cancel = Modern.Secondary("Cancel");
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        stack.Controls.Add(Modern.ButtonRow(ok, cancel));
        AcceptButton = ok;
        CancelButton = cancel;

        void UpdateCount()
        {
            count.Text = _checked.Count == 1 ? "1 song selected" : $"{_checked.Count} songs selected";
            ok.Enabled = _checked.Count > 0;
        }
        void Fill()
        {
            _filling = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            var f = _filter.Text.Trim();
            foreach (var s in _all.Where(s => f.Length == 0 || s.Contains(f, StringComparison.CurrentCultureIgnoreCase)))
                _list.Items.Add(s, _checked.Contains(s));
            _list.EndUpdate();
            _filling = false;
        }
        _filter.TextChanged += (_, _) => Fill();
        _list.ItemCheck += (_, e) =>
        {
            if (_filling) return;
            var name = (string)_list.Items[e.Index];
            if (e.NewValue == CheckState.Checked) _checked.Add(name); else _checked.Remove(name);
            UpdateCount();
        };
        Fill();
        UpdateCount();
    }

    /// <summary>The songs chosen (in library order), or an empty list when cancelled.</summary>
    public static IReadOnlyList<string> Ask(IWin32Window owner, string playlist, IReadOnlyList<string> songs)
    {
        using var d = new AddSongsDialog(playlist, songs);
        return d.ShowDialog(owner) == DialogResult.OK ? songs.Where(d._checked.Contains).ToList() : Array.Empty<string>();
    }
}
