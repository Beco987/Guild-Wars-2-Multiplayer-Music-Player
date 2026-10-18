namespace GW2MM.Windows.Forms;

/// <summary>Picks songs from the library to add to a playlist: a filter box and a checklist, in library order.</summary>
internal sealed class AddSongsDialog : ThemedDialog
{
    private readonly IReadOnlyList<string> _all;
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private readonly ListBox _list;
    private readonly FieldBox _filter;
    private int _hover = -1;

    private AddSongsDialog(string playlist, IReadOnlyList<string> songs) : base("Add songs", 420, $"Add songs to {playlist}")
    {
        _all = songs;
        _filter = new FieldBox(filled: true) { Glyph = Glyph.Search, ClearButton = true, Bounds = Theme.S(16, 12, 388, 30) };
        _filter.Box.PlaceholderText = "Filter songs";
        Body.Controls.Add(_filter);
        var card = new Card { Bounds = Theme.S(16, 52, 388, 300) };
        _list = new ListBox
        {
            DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.S(28), BorderStyle = BorderStyle.None, IntegralHeight = false,
            BackColor = Theme.Surface, Font = Theme.Body, Bounds = new Rectangle(Theme.S(2), Theme.S(4), card.Width - Theme.S(4), card.Height - Theme.S(8)),
        };
        Theme.ExplorerTheme(_list);
        card.Controls.Add(_list);
        Body.Controls.Add(card);
        SetBodyHeight(366);

        var count = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.FooterBack };
        SetFooterLeft(count);
        var ok = AddButton("Add", ButtonKind.Primary, DialogResult.OK, Glyph.Plus);
        var cancel = AddButton("Cancel", ButtonKind.Secondary, DialogResult.Cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _filter.Box;

        void UpdateCount()
        {
            count.Text = _checked.Count == 1 ? "1 song selected" : $"{_checked.Count} songs selected";
            ok.Enabled = _checked.Count > 0;
        }
        void Fill()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            var f = _filter.Box.Text.Trim();
            foreach (var s in _all.Where(s => f.Length == 0 || s.Contains(f, StringComparison.CurrentCultureIgnoreCase)))
                _list.Items.Add(s);
            _list.EndUpdate();
        }
        void Toggle(int index)
        {
            if (index < 0 || index >= _list.Items.Count) return;
            var name = (string)_list.Items[index];
            if (!_checked.Remove(name)) _checked.Add(name);
            _list.Invalidate(_list.GetItemRectangle(index));
            UpdateCount();
        }
        _filter.Box.TextChanged += (_, _) => Fill();
        _list.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) Toggle(_list.IndexFromPoint(e.Location)); };
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) { Toggle(_list.SelectedIndex); e.Handled = e.SuppressKeyPress = true; } };
        _list.MouseMove += (_, e) =>
        {
            var i = _list.IndexFromPoint(e.Location);
            if (i == _hover) return;
            if (_hover >= 0 && _hover < _list.Items.Count) _list.Invalidate(_list.GetItemRectangle(_hover));
            _hover = i;
            if (i >= 0) _list.Invalidate(_list.GetItemRectangle(i));
        };
        _list.MouseLeave += (_, _) => { _hover = -1; _list.Invalidate(); };
        _list.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            using (var back = new SolidBrush(Theme.Surface)) g.FillRectangle(back, e.Bounds);
            var name = (string)_list.Items[e.Index];
            var on = _checked.Contains(name);
            Theme.Smooth(g);
            if (e.Index == _hover || (e.State & DrawItemState.Selected) != 0)
                Theme.FillRound(g, new RectangleF(e.Bounds.X + Theme.Sf(4), e.Bounds.Y + Theme.Sf(1), e.Bounds.Width - Theme.Sf(8), e.Bounds.Height - Theme.Sf(2)), Theme.Sf(4), Theme.IceHover);
            var s = Theme.Sf(16);
            CheckMark.PaintBox(g, new RectangleF(e.Bounds.X + Theme.Sf(12), e.Bounds.Y + (e.Bounds.Height - s) / 2, s, s), on, e.Index == _hover);
            Theme.Text(g, name, on ? Theme.BodyBold : Theme.Body, new Rectangle(e.Bounds.X + Theme.S(38), e.Bounds.Y, e.Bounds.Width - Theme.S(46), e.Bounds.Height),
                on ? Theme.Title : Theme.Ink);
        };
        Fill();
        UpdateCount();
    }

    /// <summary>The songs chosen (in library order), or an empty list when cancelled.</summary>
    public static IReadOnlyList<string> Ask(IWin32Window owner, string playlist, IReadOnlyList<string> songs)
    {
        using var d = new AddSongsDialog(playlist, songs);
        if ((owner as Control)?.FindForm() is { } o) d.TopMost = o.TopMost;
        return d.ShowDialog(owner) == DialogResult.OK ? songs.Where(d._checked.Contains).ToList() : Array.Empty<string>();
    }
}
