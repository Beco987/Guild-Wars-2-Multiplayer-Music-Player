using System.Text.RegularExpressions;

namespace GW2MM.Windows.Forms;

/// <summary>How one list row looks: bold, coloured, with a leading glyph (the now-playing equalizer) or dimmed.</summary>
internal readonly record struct RowLook(bool Bold = false, Color? Color = null, Glyph Glyph = Glyph.None, bool Dim = false);

/// <summary>
/// The Details-view list used for songs and playlists, painted in the theme: uppercase headers with a sort caret,
/// 24 px rows, rounded ice-blue selection with an azure marker, hover highlight. Behaviour is the stock ListView.
/// </summary>
internal sealed class ThemedListView : ListView
{
    private int _hover = -1;

    public ThemedListView()
    {
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        DoubleBuffered = true;
        BorderStyle = BorderStyle.None;
        Font = Theme.Body;
        ForeColor = Theme.Ink;
        BackColor = Theme.Surface;
        HideSelection = false;
        // The row height follows the (invisible) image list.
        SmallImageList = new ImageList { ImageSize = new Size(1, Theme.S(24)) };
        // No Explorer theme: its hover effect repaints part of a row behind the owner drawing's back.
        ThemedScrollBar.Attach(this);
    }

    /// <summary>Per-row look (null = plain).</summary>
    public Func<ListViewItem, RowLook>? Look { get; set; }

    /// <summary>The column showing the sort caret (-1 for none).</summary>
    public int SortColumn { get; set; } = -1;
    public bool SortDescending { get; set; }

    /// <summary>The column that takes up the remaining width.</summary>
    public int FillColumn { get; set; } = 0;

    public void FitColumns()
    {
        if (Columns.Count == 0 || FillColumn >= Columns.Count) return;
        var others = 0;
        for (var i = 0; i < Columns.Count; i++) if (i != FillColumn) others += Columns[i].Width;
        var w = Math.Max(Theme.S(60), ClientSize.Width - others);
        if (Columns[FillColumn].Width != w) Columns[FillColumn].Width = w;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitColumns();
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        using (var back = new SolidBrush(Theme.Surface)) g.FillRectangle(back, e.Bounds);
        var col = e.Header!;
        var sorted = col.Index == SortColumn;
        var text = col.Text.ToUpperInvariant();
        var pad = Theme.S(col.Index == 0 ? 14 : 8);
        var r = new Rectangle(e.Bounds.X + pad, e.Bounds.Y, e.Bounds.Width - 2 * pad, e.Bounds.Height);
        var flags = col.TextAlign switch
        {
            HorizontalAlignment.Right => Theme.TextRight,
            HorizontalAlignment.Center => Theme.TextCenter,
            _ => Theme.TextLeft,
        };
        var color = sorted ? Theme.AccentInk : Theme.Muted;
        var caret = sorted ? Theme.S(10) : 0;
        var tw = Theme.Measure(text, Theme.Overline).Width;
        if (sorted)
        {
            // The caret sits after the label, so shift right-aligned labels left to make room.
            var tx = col.TextAlign switch
            {
                HorizontalAlignment.Right => r.Right - caret - Theme.S(2) - tw,
                HorizontalAlignment.Center => r.X + (r.Width - tw - caret) / 2,
                _ => r.X,
            };
            Theme.Text(g, text, Theme.Overline, new Rectangle(tx, r.Y, tw + 1, r.Height), color);
            Glyphs.Draw(g, SortDescending ? Glyph.CaretDown : Glyph.CaretUp,
                new RectangleF(tx + tw + Theme.S(2), r.Y + (r.Height - caret) / 2f, caret, caret), Theme.Azure);
        }
        else Theme.Text(g, text, Theme.Overline, r, color, flags);
        using var line = new SolidBrush(Theme.Divider);
        g.FillRectangle(line, e.Bounds.X, e.Bounds.Bottom - Math.Max(1, Theme.S(1)), e.Bounds.Width, Math.Max(1, Theme.S(1)));
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        var g = e.Graphics;
        var row = new Rectangle(0, e.Bounds.Y, ClientSize.Width, e.Bounds.Height);
        using (var back = new SolidBrush(Theme.Surface)) g.FillRectangle(back, row);
        Theme.Smooth(g);
        var pill = new RectangleF(Theme.Sf(4), row.Y + Theme.Sf(1), row.Width - Theme.Sf(8), row.Height - Theme.Sf(2));
        if (e.Item.Selected)
        {
            Theme.FillRound(g, pill, Theme.Sf(4), Theme.Ice);
            Theme.FillRound(g, new RectangleF(pill.X + Theme.Sf(1), row.Y + (row.Height - Theme.Sf(12)) / 2f, Theme.Sf(3), Theme.Sf(12)), Theme.Sf(1.5f),
                Focused ? Theme.Azure : Theme.BorderStrong);
        }
        else if (e.ItemIndex == _hover) Theme.FillRound(g, pill, Theme.Sf(4), Theme.IceHover);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;

        // The whole row is drawn here, cells included: Windows sometimes repaints a row without asking for its
        // cells (hovering, for one), which used to leave the row without text.
        var look = Look?.Invoke(e.Item) ?? default;
        var x = e.Bounds.X;
        foreach (var col in Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex))
        {
            if (col.Index < e.Item.SubItems.Count)
                DrawCell(g, e.Item, look, col, e.Item.SubItems[col.Index].Text, new Rectangle(x, e.Bounds.Y, col.Width, e.Bounds.Height));
            x += col.Width;
        }
    }

    // Cells are drawn with their row (see OnDrawItem).
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) { }

    private void DrawCell(Graphics g, ListViewItem item, RowLook look, ColumnHeader col, string text, Rectangle bounds)
    {
        var pad = Theme.S(col.Index == 0 ? 14 : 8);
        var r = new Rectangle(bounds.X + pad, bounds.Y, bounds.Width - 2 * pad, bounds.Height);
        var nameColumn = col.Index == FillColumn;
        if (nameColumn && look.Glyph != Glyph.None)
        {
            var s = Theme.S(12);
            Glyphs.Draw(g, look.Glyph, new RectangleF(r.X, r.Y + (r.Height - s) / 2f, s, s), Theme.Azure);
            r.X += s + Theme.S(6);
            r.Width -= s + Theme.S(6);
        }
        var font = nameColumn && (look.Bold || item.Selected) ? Theme.BodyBold : Theme.Body;
        var color = look.Dim ? Theme.Disabled
            : nameColumn && look.Color is { } c ? c
            : nameColumn ? (item.Selected ? Theme.Title : Theme.Ink)
            : Theme.Ink2;
        var flags = col.TextAlign switch
        {
            HorizontalAlignment.Right => Theme.TextRight,
            HorizontalAlignment.Center => Theme.TextCenter,
            _ => Theme.TextLeft,
        };
        Theme.Text(g, text, font, r, color, flags);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = GetItemAt(Theme.S(8), e.Y)?.Index ?? -1;
        if (hit == _hover) return;
        InvalidateRow(_hover);
        _hover = hit;
        InvalidateRow(_hover);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateRow(_hover);
        _hover = -1;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        var b = GetItemRect(index);
        Invalidate(new Rectangle(0, b.Y, ClientSize.Width, b.Height));
    }
}

/// <summary>
/// The chat and event log: chat lines ("name &gt; text") show the name in navy bold, problems ("*** …") in red,
/// "♪" notices in lavender, everything else muted. Keeps the last 100 000 characters once it passes 200 000.
/// </summary>
internal sealed class ChatLog : RichTextBox
{
    private static readonly Regex ChatLine = new(@"^(?<name>[^\r\n>]{1,40}) > (?<text>.*)$", RegexOptions.Compiled);

    public ChatLog()
    {
        ReadOnly = true;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Inset;
        ForeColor = Theme.Ink;
        Font = Theme.Small;
        DetectUrls = false;
        ScrollBars = RichTextBoxScrollBars.Vertical;
        Cursor = Cursors.Arrow;
        TabStop = false;
        ThemedScrollBar.Attach(this);
    }

    public void AppendLine(string line)
    {
        if (TextLength > 200_000)
        {
            Select(0, TextLength - 100_000);
            SelectedText = "";
        }
        var m = ChatLine.Match(line);
        if (TextLength > 0) Append("\n", Theme.Ink, Theme.Small);
        if (line.StartsWith("*** ", StringComparison.Ordinal)) Append(line[4..], Theme.DangerText, Theme.Small);
        else if (line.StartsWith('♪')) Append(line, Theme.LavenderText, Theme.Small);
        else if (m.Success)
        {
            Append(m.Groups["name"].Value + "  ", Theme.AccentInk, Theme.SmallBold);
            Append(m.Groups["text"].Value, Theme.Ink, Theme.Small);
        }
        else Append(line, Theme.Muted, Theme.Small);
        SelectionStart = TextLength;
        ScrollToCaret();
    }

    private void Append(string text, Color color, Font font)
    {
        SelectionStart = TextLength;
        SelectionLength = 0;
        SelectionColor = color;
        SelectionFont = font;
        AppendText(text);
    }
}

/// <summary>
/// The main window's banner: the Janthir Wilds art (kept at its own aspect ratio), a frost line beneath it and a
/// frosted capsule with the Settings and About buttons.
/// </summary>
internal sealed class Banner : Control
{
    private readonly Image _art;
    private readonly ThemedToolTip _tips = new();
    private int _hover = -1; // 0 = settings, 1 = about

    public Banner(Image art)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _art = art;
        BackColor = Theme.BannerBack;
    }

    public event EventHandler? SettingsClicked;
    public event EventHandler? AboutClicked;

    private RectangleF Capsule => new(Width - Theme.Sf(37), Theme.Sf(5), Theme.Sf(26), Theme.Sf(51));
    private RectangleF Button(int i) => new(Capsule.X, Capsule.Y + i * Capsule.Height / 2, Capsule.Width, Capsule.Height / 2);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = Button(0).Contains(e.Location) ? 0 : Button(1).Contains(e.Location) ? 1 : -1;
        if (hit == _hover) return;
        _hover = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        _tips.SetToolTip(this, hit switch { 0 => "Settings", 1 => "About GW2MM", _ => "" });
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;
        if (Button(0).Contains(e.Location)) SettingsClicked?.Invoke(this, EventArgs.Empty);
        else if (Button(1).Contains(e.Location)) AboutClicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.BannerBack);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        var h = (float)Width * _art.Height / _art.Width;
        g.DrawImage(_art, new RectangleF(0, Height - Theme.Sf(2) - h, Width, h));

        // Frost line: the banner sky's lavender running into ice and azure.
        var line = new RectangleF(0, Height - Theme.Sf(2), Width, Theme.Sf(2));
        using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(-1, line.Y, Width + 2, line.Height),
                   Color.FromArgb(0xD9, 0xD3, 0xF0), Color.FromArgb(0x5E, 0x9B, 0xD8), 0f))
        {
            brush.InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
            {
                Colors = new[] { Color.FromArgb(0xD9, 0xD3, 0xF0), Color.FromArgb(0xB9, 0xC9, 0xEE), Color.FromArgb(0x5E, 0x9B, 0xD8) },
                Positions = new[] { 0f, 0.45f, 1f },
            };
            g.FillRectangle(brush, line);
        }

        Theme.Smooth(g);
        var cap = Capsule;
        Theme.Shadow(g, cap, cap.Width / 2, blur: 3, dy: 1, alpha: 50);
        // Frosted glass: white by day, deep navy at dusk.
        Theme.FillRound(g, cap, cap.Width / 2, Theme.Alpha(Theme.Surface, 225));
        Theme.DrawRound(g, cap, cap.Width / 2, Theme.Dusk ? Theme.BorderStrong : Color.White);
        for (var i = 0; i < 2; i++)
        {
            var b = Button(i);
            if (_hover == i)
            {
                using var hover = new SolidBrush(Theme.Ice);
                g.FillEllipse(hover, RectangleF.Inflate(new RectangleF(b.X, b.Y + (b.Height - b.Width) / 2, b.Width, b.Width), -Theme.Sf(2), -Theme.Sf(2)));
            }
            var s = Theme.Sf(16);
            Glyphs.Draw(g, i == 0 ? Glyph.Gear : Glyph.Info, new RectangleF(b.X + (b.Width - s) / 2, b.Y + (b.Height - s) / 2, s, s), Theme.Title);
        }
        using var sep = new SolidBrush(Theme.Border);
        g.FillRectangle(sep, cap.X + Theme.Sf(6), cap.Y + cap.Height / 2 - Theme.Sf(0.5f), cap.Width - Theme.Sf(12), Math.Max(1, Theme.Sf(1)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Menus in the theme: white, rounded hover rows, glyph icons, red text for destructive items (Tag = "danger").</summary>
internal sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = new RectangleF(Theme.Sf(4), 0, e.Item.Width - Theme.Sf(8), e.Item.Height);
        Theme.FillRound(g, r, Theme.Sf(4), Theme.Ice);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? Theme.Disabled : Equals(e.Item.Tag, "danger") ? Theme.DangerText : Theme.Ink;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var s = Theme.S(12);
        var r = e.ArrowRectangle;
        Glyphs.Draw(e.Graphics, Glyph.ChevronRight, new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), Theme.Muted);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var s = Theme.S(16);
        var r = e.ImageRectangle;
        Glyphs.Draw(e.Graphics, Glyph.Check, new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), Theme.Azure, 1.8f);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Divider);
        e.Graphics.FillRectangle(b, Theme.S(10), e.Item.Height / 2, e.Item.Width - Theme.S(20), Math.Max(1, Theme.S(1)));
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(Theme.Surface);
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Theme.Ice;
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color SeparatorDark => Theme.Divider;
        public override Color SeparatorLight => Theme.Surface;
        public override Color CheckBackground => Theme.Surface;
        public override Color CheckSelectedBackground => Theme.Ice;
        public override Color CheckPressedBackground => Theme.Ice;
    }
}

internal static class Menus
{
    /// <summary>A context menu in the theme.</summary>
    public static ContextMenuStrip Create()
    {
        var m = new ContextMenuStrip
        {
            Renderer = new ThemedMenuRenderer(), Font = Forms.Theme.Body, ShowImageMargin = true, ImageScalingSize = Forms.Theme.S(16, 16),
            Padding = new Padding(0, Forms.Theme.S(4), 0, Forms.Theme.S(4)),
        };
        m.HandleCreated += (_, _) => Forms.Theme.RoundCorners(m.Handle);
        return m;
    }

    /// <summary>Adds an item with an optional glyph; "danger" items get red text.</summary>
    public static ToolStripMenuItem Item(this ToolStrip menu, string text, Glyph glyph, EventHandler? onClick, bool danger = false, string? keys = null)
    {
        var item = Item(text, glyph, onClick, danger, keys);
        menu.Items.Add(item);
        return item;
    }

    public static ToolStripMenuItem Item(string text, Glyph glyph, EventHandler? onClick, bool danger = false, string? keys = null)
    {
        var item = new ToolStripMenuItem(text) { Padding = new Padding(0, Forms.Theme.S(3), 0, Forms.Theme.S(3)) };
        if (glyph != Glyph.None) item.Image = Glyphs.Image(glyph, danger ? Forms.Theme.DangerText : Forms.Theme.Ink2);
        if (onClick is not null) item.Click += onClick;
        if (danger) item.Tag = "danger";
        if (keys is not null) item.ShortcutKeyDisplayString = keys;
        return item;
    }

    /// <summary>Gives a submenu the same look as its parent menu.</summary>
    public static void Style(ToolStripMenuItem item)
    {
        item.DropDown.Renderer = new ThemedMenuRenderer();
        item.DropDown.Font = Forms.Theme.Body;
        item.DropDown.ImageScalingSize = Forms.Theme.S(16, 16);
        item.DropDown.Padding = new Padding(0, Forms.Theme.S(4), 0, Forms.Theme.S(4));
        item.DropDown.HandleCreated += (_, _) => Forms.Theme.RoundCorners(item.DropDown.Handle);
    }
}
