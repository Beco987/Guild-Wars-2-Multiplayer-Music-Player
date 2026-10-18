using System.Drawing.Drawing2D;

namespace GW2MM.Windows.Forms;

internal enum ButtonKind { Primary, Secondary, Tonal, Ghost, Danger }

/// <summary>
/// A painted button (a real <see cref="Button"/> underneath, so Accept/Cancel buttons, DialogResult, keyboard and
/// accessibility keep working). Primary is navy, Secondary white with an outline, Tonal ice blue, Ghost has no
/// background until hovered, Danger red. A glyph can sit before the text, or alone for icon buttons.
/// </summary>
internal class ThemedButton : Button
{
    private bool _hover, _pressed, _toggled;

    public ThemedButton(string text, ButtonKind kind = ButtonKind.Secondary, Glyph glyph = Glyph.None)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Kind = kind;
        Glyph = glyph;
        Font = Theme.BodyBold;
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        if (text.Length == 0 && glyph != Glyph.None) AccessibleName = glyph.ToString();
        Size = GetPreferredSize(Size.Empty);
    }

    public ButtonKind Kind { get; set; }
    public Glyph Glyph { get; set; }
    public float Radius { get; set; } = 6;
    public int MinimumWidth { get; set; }
    public int FixedHeight { get; set; } = Theme.S(32);

    /// <summary>For on/off icon buttons (Shuffle, Repeat): shown with a tonal background while on.</summary>
    public bool Toggled
    {
        get => _toggled;
        set { _toggled = value; Invalidate(); }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var glyph = Glyph == Glyph.None ? 0 : Theme.S(16);
        if (Text.Length == 0) return new Size(FixedHeight, FixedHeight);
        var text = Theme.Measure(Text, Font).Width;
        var gap = glyph > 0 ? Theme.S(6) : 0;
        var w = glyph + gap + text + Theme.S(Kind == ButtonKind.Ghost ? 20 : 28);
        return new Size(Math.Max(w, MinimumWidth), FixedHeight);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        var radius = Theme.Sf(Radius);
        var (fill, border, ink) = Colors();
        if (fill.A > 0) Theme.FillRound(g, r, radius, fill);
        if (border.A > 0) Theme.DrawRound(g, r, radius, border);
        if (Focused && ShowFocusCues && Enabled) Theme.DrawRound(g, RectangleF.Inflate(r, -1, -1), radius, Theme.Azure, 2f);

        var glyph = Glyph == Glyph.None ? 0 : Theme.S(16);
        if (Text.Length == 0)
        {
            var size = Math.Min(Theme.S(16), Math.Min(Width, Height) - Theme.S(8));
            Glyphs.Draw(g, Glyph, new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size), ink);
            return;
        }
        var textW = Theme.Measure(Text, Font).Width;
        var gap = glyph > 0 ? Theme.S(6) : 0;
        var x = (Width - (glyph + gap + textW)) / 2;
        if (glyph > 0) Glyphs.Draw(g, Glyph, new RectangleF(x, (Height - glyph) / 2f, glyph, glyph), ink);
        Theme.Text(g, Text, Font, new Rectangle(x + glyph + gap, 0, Width - (x + glyph + gap), Height), ink);
    }

    private (Color Fill, Color Border, Color Ink) Colors()
    {
        if (!Enabled)
            return Kind is ButtonKind.Ghost ? (Color.Transparent, Color.Transparent, Theme.Disabled)
                : Kind is ButtonKind.Secondary ? (Theme.Surface, Theme.Divider, Theme.Disabled) : (Theme.Track, Color.Transparent, Theme.Disabled);
        return Kind switch
        {
            ButtonKind.Primary => (_pressed ? Theme.NavyPressed : _hover ? Theme.NavyHover : Theme.Navy, Color.Transparent, Color.White),
            ButtonKind.Danger => (_pressed ? Theme.DangerPressed : _hover ? Theme.DangerHover : Theme.Danger, Color.Transparent, Color.White),
            ButtonKind.Tonal => (_pressed ? Theme.IcePressed : _hover ? Theme.Alpha(Theme.Ice, 255) : Theme.Ice, _hover ? Theme.Alpha(Theme.Azure, 90) : Color.Transparent, Theme.Navy),
            ButtonKind.Ghost => (_pressed ? Theme.IcePressed : _hover ? Theme.IceHover : _toggled ? Theme.Ice : Color.Transparent,
                Color.Transparent, _toggled ? Theme.AzureText : Theme.Ink2),
            _ => (_pressed ? Theme.IcePressed : _hover ? Theme.IceHover : Theme.Surface, _hover ? Theme.BorderStrong : Theme.Border, Theme.Ink),
        };
    }
}

/// <summary>A round transport button: the large navy Play button, or a white outlined Pause / Stop.</summary>
internal sealed class RoundButton : Button
{
    private bool _hover, _pressed;

    public RoundButton(Glyph glyph, bool primary, string name)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Glyph = glyph;
        Primary = primary;
        AccessibleName = name;
        Text = "";
        FlatStyle = FlatStyle.Flat;
        Cursor = Cursors.Hand;
    }

    public Glyph Glyph { get; }
    public bool Primary { get; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var pad = Theme.Sf(Primary ? 3 : 1);
        var d = Math.Min(Width, Height) - 2 * pad;
        var circle = new RectangleF((Width - d) / 2f, (Height - d) / 2f - (Primary ? Theme.Sf(1) : 0), d, d);
        if (_pressed) circle.Offset(0, Theme.Sf(1));
        using var path = new GraphicsPath();
        path.AddEllipse(circle);
        if (Primary)
        {
            if (!_pressed) Theme.Shadow(g, circle, d / 2, blur: 3, dy: 2, alpha: 60);
            var top = _pressed ? Theme.NavyPressed : _hover ? Color.FromArgb(0x4A, 0x93, 0xD8) : Theme.NavyLight;
            var bottom = _pressed ? Theme.NavyPressed : _hover ? Theme.NavyHover : Theme.Navy;
            using var brush = new LinearGradientBrush(RectangleF.Inflate(circle, 0, 1), top, bottom, LinearGradientMode.Vertical);
            g.FillPath(brush, path);
            using var shine = new Pen(Color.FromArgb(70, 255, 255, 255), Theme.Sf(1));
            g.DrawArc(shine, RectangleF.Inflate(circle, -Theme.Sf(1), -Theme.Sf(1)), 200, 140);
            var size = d * 0.46f;
            Glyphs.Draw(g, Glyph, new RectangleF(circle.X + (d - size) / 2 + d * 0.035f, circle.Y + (d - size) / 2, size, size), Color.White);
        }
        else
        {
            using var brush = new SolidBrush(_pressed ? Theme.IcePressed : _hover ? Theme.IceHover : Theme.Surface);
            g.FillPath(brush, path);
            using var pen = new Pen(_hover ? Theme.Azure : Theme.BorderStrong, Theme.Sf(1));
            g.DrawEllipse(pen, RectangleF.Inflate(circle, -Theme.Sf(0.5f), -Theme.Sf(0.5f)));
            var size = d * 0.44f;
            Glyphs.Draw(g, Glyph, new RectangleF(circle.X + (d - size) / 2, circle.Y + (d - size) / 2, size, size), Theme.Navy);
        }
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Theme.Azure, Theme.Sf(2));
            g.DrawEllipse(ring, RectangleF.Inflate(circle, -Theme.Sf(1), -Theme.Sf(1)));
        }
    }
}

/// <summary>A white rounded card on the frost background. Its parent draws the soft shadow (see <see cref="Theme"/>).</summary>
internal class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Surface;
    }

    public float Radius { get; set; } = 8;

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, r, Theme.Sf(Radius), BackColor);
        Theme.DrawRound(g, r, Theme.Sf(Radius), Theme.Border);
    }

    /// <summary>Paints the shadows of every card inside <paramref name="parent"/> (call from the parent's OnPaint).</summary>
    public static void PaintShadows(Control parent, Graphics g)
    {
        Theme.Smooth(g);
        foreach (Control c in parent.Controls)
            if (c is Card { Visible: true } card)
                Theme.Shadow(g, card.Bounds, Theme.Sf(card.Radius), blur: 3, dy: 1, alpha: 22);
    }
}

/// <summary>
/// A text field: a borderless <see cref="TextBox"/> inside a painted frame (white with an outline, or the filled
/// "inset" style for search). Optional leading glyph, trailing unit ("ms") and a clear (×) button.
/// </summary>
internal sealed class FieldBox : Panel
{
    private bool _hover, _invalid;
    private readonly bool _multiline;

    public FieldBox(bool multiline = false, bool filled = false)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        _multiline = multiline;
        Filled = filled;
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None, Font = Theme.Body, ForeColor = Theme.Ink, Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
        };
        if (multiline) Theme.ExplorerTheme(Box);
        Controls.Add(Box);
        Box.GotFocus += (_, _) => { UpdateColors(); Invalidate(); };
        Box.LostFocus += (_, _) => { UpdateColors(); Invalidate(); };
        Box.TextChanged += (_, _) => { if (ClearButton) Invalidate(); };
        Box.MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        Box.MouseLeave += (_, _) => { _hover = false; Invalidate(); };
        Height = Theme.S(multiline ? 72 : 30);
        UpdateColors();
    }

    public TextBox Box { get; }
    public bool Filled { get; }
    public Glyph Glyph { get; set; }
    public string? Suffix { get; set; }
    public bool ClearButton { get; set; }

    /// <summary>Red outline, e.g. while a value is not a number.</summary>
    public bool Invalid
    {
        get => _invalid;
        set { _invalid = value; Invalidate(); }
    }

    public string Value
    {
        get => Box.Text;
        set => Box.Text = value;
    }

    private Color Fill => !Enabled ? Theme.FooterBack : Filled && !Box.Focused ? Theme.Inset : Theme.Surface;

    private void UpdateColors()
    {
        Box.BackColor = Fill;
        BackColor = Fill;
    }

    private Rectangle ClearArea => new(Width - Theme.S(26), (Height - Theme.S(20)) / 2, Theme.S(20), Theme.S(20));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        var left = Theme.S(Glyph == Glyph.None ? 9 : 30);
        var right = Theme.S(8);
        if (Suffix is not null) right += Theme.Measure(Suffix, Theme.Small).Width + Theme.S(6);
        if (ClearButton) right += Theme.S(22);
        if (_multiline)
            Box.Bounds = new Rectangle(left, Theme.S(6), Math.Max(1, Width - left - Theme.S(3)), Math.Max(1, Height - Theme.S(10)));
        else
            Box.Bounds = new Rectangle(left, (Height - Box.PreferredHeight) / 2 + 1, Math.Max(1, Width - left - right), Box.PreferredHeight);
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); UpdateColors(); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = ClearButton && Box.TextLength > 0 && ClearArea.Contains(e.Location) ? Cursors.Hand : Cursors.IBeam;
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (ClearButton && Box.TextLength > 0 && ClearArea.Contains(e.Location)) Box.Clear();
        Box.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        var radius = Theme.Sf(6);
        Theme.FillRound(g, r, radius, Fill);
        var focused = Box.Focused;
        if (_invalid) Theme.DrawRound(g, r, radius, Theme.Danger, focused ? 1.5f : 1f);
        else if (focused) Theme.DrawRound(g, r, radius, Theme.Azure, 1.5f);
        else if (Filled) Theme.DrawRound(g, r, radius, _hover ? Theme.BorderStrong : Theme.Divider);
        else
        {
            Theme.DrawRound(g, r, radius, _hover ? Theme.BorderStrong : Theme.Border);
            if (Enabled)
            {
                // A stronger bottom edge, so the field reads as an input (3:1 contrast) without a heavy outline.
                using var pen = new Pen(Theme.Underline, Theme.Sf(1));
                var y = Height - Theme.Sf(0.5f);
                g.DrawLine(pen, radius * 0.7f, y, Width - radius * 0.7f, y);
            }
        }
        if (Glyph != Glyph.None)
        {
            var s = Theme.S(14);
            Glyphs.Draw(g, Glyph, new RectangleF(Theme.S(10), (Height - s) / 2f, s, s), focused ? Theme.Ink2 : Theme.Placeholder);
        }
        var right = Width - Theme.S(8);
        if (ClearButton && Box.TextLength > 0)
        {
            var c = ClearArea;
            Theme.FillRound(g, c, c.Height / 2f, Theme.Track);
            var s = Theme.S(10);
            Glyphs.Draw(g, Glyph.Close, new RectangleF(c.X + (c.Width - s) / 2f, c.Y + (c.Height - s) / 2f, s, s), Theme.Ink2, 1.8f);
            right = c.X - Theme.S(4);
        }
        if (Suffix is not null)
        {
            var w = Theme.Measure(Suffix, Theme.Small).Width;
            Theme.Text(g, Suffix, Theme.Small, new Rectangle(right - w, 0, w, Height), Theme.Placeholder);
        }
    }
}

/// <summary>Two or more segments (Songs | Playlists) with an optional count pill each; one tab stop, arrow keys switch.</summary>
internal sealed class SegmentedControl : Control
{
    private readonly List<(string Text, int? Count)> _items = new();
    private int _selected;

    public SegmentedControl(params string[] items)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        foreach (var i in items) _items.Add((i, null));
        Font = Theme.BodyBold;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
        Height = Theme.S(30);
    }

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value == _selected || value < 0 || value >= _items.Count) return;
            _selected = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetCount(int index, int? count)
    {
        if (_items[index].Count == count) return;
        _items[index] = (_items[index].Text, count);
        Width = PreferredWidth;
        Invalidate();
    }

    private int SegmentWidth(int i)
    {
        var (text, count) = _items[i];
        var w = Theme.Measure(text, Font).Width + Theme.S(28);
        if (count is { } c) w += Theme.Measure(c.ToString(), Theme.Tiny).Width + Theme.S(14);
        return w;
    }

    public int PreferredWidth => Enumerable.Range(0, _items.Count).Sum(SegmentWidth) + Theme.S(6);

    private Rectangle Segment(int i)
    {
        var x = Theme.S(3);
        for (var j = 0; j < i; j++) x += SegmentWidth(j);
        return new Rectangle(x, Theme.S(3), SegmentWidth(i), Height - Theme.S(6));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        for (var i = 0; i < _items.Count; i++)
            if (Segment(i).Contains(e.Location)) SelectedIndex = i;
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) SelectedIndex = Math.Max(0, _selected - 1);
        else if (e.KeyCode == Keys.Right) SelectedIndex = Math.Min(_items.Count - 1, _selected + 1);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var track = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, track, Theme.Sf(8), Theme.Track);
        for (var i = 0; i < _items.Count; i++)
        {
            var seg = Segment(i);
            var on = i == _selected;
            if (on)
            {
                Theme.Shadow(g, seg, Theme.Sf(6), blur: 2, dy: 1, alpha: 40);
                Theme.FillRound(g, seg, Theme.Sf(6), Theme.Surface);
                if (Focused && ShowFocusCues) Theme.DrawRound(g, seg, Theme.Sf(6), Theme.Azure, 1.5f);
            }
            var (text, count) = _items[i];
            var tw = Theme.Measure(text, Font).Width;
            var pill = count is { } c ? Theme.Measure(c.ToString(), Theme.Tiny).Width + Theme.S(10) : 0;
            var x = seg.X + (seg.Width - tw - (pill > 0 ? pill + Theme.S(6) : 0)) / 2;
            Theme.Text(g, text, Font, new Rectangle(x, seg.Y, tw + Theme.S(2), seg.Height), on ? Theme.Title : Theme.Muted);
            if (count is { } n)
            {
                var pr = new RectangleF(x + tw + Theme.S(6), seg.Y + (seg.Height - Theme.S(16)) / 2f, pill, Theme.S(16));
                Theme.FillRound(g, pr, pr.Height / 2, on ? Theme.Ice : Theme.Alpha(Theme.Title, 18));
                Theme.Text(g, n.ToString(), Theme.Tiny, Rectangle.Round(pr), on ? Theme.Navy : Theme.Muted, Theme.TextCenter);
            }
        }
    }
}

/// <summary>An on/off switch (a <see cref="CheckBox"/> underneath, so Checked, events and accessibility are unchanged).</summary>
internal sealed class ToggleSwitch : CheckBox
{
    private bool _hover;

    public ToggleSwitch(string accessibleName)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleName = accessibleName;
        Text = "";
        AutoSize = false;
        Size = Theme.S(38, 22);
        Cursor = Cursors.Hand;
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var h = Theme.Sf(20);
        var track = new RectangleF(Theme.Sf(1), (Height - h) / 2, Width - Theme.Sf(2), h);
        if (Checked)
        {
            Theme.FillRound(g, track, h / 2, !Enabled ? Theme.Disabled : _hover ? Theme.AzureText : Theme.Azure);
            var d = h - Theme.Sf(6);
            g.FillEllipse(Brushes.White, track.Right - Theme.Sf(3) - d, track.Y + Theme.Sf(3), d, d);
        }
        else
        {
            Theme.FillRound(g, track, h / 2, _hover ? Theme.IceHover : Theme.Surface);
            Theme.DrawRound(g, track, h / 2, Enabled ? Theme.Underline : Theme.Disabled);
            var d = h - Theme.Sf(8);
            using var knob = new SolidBrush(Enabled ? Theme.Muted : Theme.Disabled);
            g.FillEllipse(knob, track.X + Theme.Sf(4), track.Y + Theme.Sf(4), d, d);
        }
        if (Focused && ShowFocusCues) Theme.DrawRound(g, RectangleF.Inflate(track, Theme.Sf(1), Theme.Sf(1)), h / 2 + Theme.Sf(1), Theme.Azure, 1.5f);
    }
}

/// <summary>A painted check box (16 px box, optional label after it).</summary>
internal sealed class CheckMark : CheckBox
{
    private bool _hover;

    public CheckMark(string text = "")
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        Font = Theme.Body;
        AutoSize = false;
        Cursor = Cursors.Hand;
        Size = text.Length == 0 ? Theme.S(20, 20) : new Size(Theme.S(26) + Theme.Measure(text, Font).Width, Theme.S(22));
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var s = Theme.Sf(16);
        var x = Text.Length == 0 ? (Width - s) / 2 : Theme.Sf(2);
        PaintBox(g, new RectangleF(x, (Height - s) / 2, s, s), Checked, _hover, Enabled);
        if (Focused && ShowFocusCues) Theme.DrawRound(g, new RectangleF(x - Theme.Sf(2), (Height - s) / 2 - Theme.Sf(2), s + Theme.Sf(4), s + Theme.Sf(4)), Theme.Sf(5), Theme.Azure, 1.5f);
        if (Text.Length > 0)
            Theme.Text(g, Text, Font, new Rectangle(Theme.S(26), 0, Width - Theme.S(26), Height), Enabled ? Theme.Ink : Theme.Disabled);
    }

    public static void PaintBox(Graphics g, RectangleF box, bool on, bool hover, bool enabled = true)
    {
        var radius = Theme.Sf(4);
        if (on)
        {
            Theme.FillRound(g, box, radius, !enabled ? Theme.Disabled : hover ? Theme.AzureText : Theme.Azure);
            Glyphs.Draw(g, Glyph.Check, RectangleF.Inflate(box, -Theme.Sf(1.5f), -Theme.Sf(1.5f)), Color.White, 2f);
        }
        else
        {
            Theme.FillRound(g, box, radius, hover ? Theme.IceHover : Theme.Surface);
            Theme.DrawRound(g, box, radius, !enabled ? Theme.Disabled : hover ? Theme.Azure : Theme.Underline);
        }
    }
}

/// <summary>Small uppercase section label ("CHAT", "PLAYBACK").</summary>
internal sealed class Overline : Label
{
    public Overline(string text)
    {
        Text = text.ToUpperInvariant();
        UseMnemonic = false;
        Font = Theme.Overline;
        ForeColor = Theme.Muted;
        AutoSize = true;
        BackColor = Color.Transparent;
    }
}

/// <summary>The main window's status line: a coloured state dot, the status text and, on the right, a short context.</summary>
internal sealed class StatusBar : Control
{
    private string _right = "";

    public StatusBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.StatusBack;
        Font = Theme.Small;
        Height = Theme.S(26);
    }

    public string RightText
    {
        get => _right;
        set { _right = value; Invalidate(); }
    }

    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

    /// <summary>The dot colour follows the session's status messages.</summary>
    public static Color DotFor(string status)
    {
        var s = status.ToLowerInvariant();
        if (s.Contains("failed") || s.Contains("could not")) return Theme.Danger;
        if (s.StartsWith("not connected") || s.StartsWith("disconnected")) return Theme.Offline;
        if (s.Contains("trying to connect") || s.Contains("downloading")) return Theme.Azure;
        if (s.Contains("waiting")) return Theme.Warning;
        if (s.Contains("connected") || s.Contains("typing")) return Theme.Success;
        return Theme.Offline;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using (var line = new SolidBrush(Theme.Border)) g.FillRectangle(line, 0, 0, Width, Math.Max(1, Theme.S(1)));
        Theme.Smooth(g);
        var dot = DotFor(Text);
        var cy = Height / 2f + Theme.Sf(0.5f);
        var halo = Theme.Sf(12);
        using (var b = new SolidBrush(Theme.Alpha(dot, 50))) g.FillEllipse(b, Theme.Sf(16) - halo / 2, cy - halo / 2, halo, halo);
        var d = Theme.Sf(7);
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, Theme.Sf(16) - d / 2, cy - d / 2, d, d);
        var rightW = _right.Length > 0 ? Theme.Measure(_right, Font).Width : 0;
        Theme.Text(g, Text, Theme.SmallBold, new Rectangle(Theme.S(28), 0, Width - Theme.S(40) - rightW, Height), Theme.Ink2);
        if (rightW > 0) Theme.Text(g, _right, Font, new Rectangle(Width - Theme.S(12) - rightW, 0, rightW, Height), Theme.Muted, Theme.TextRight);
    }
}

/// <summary>A themed tooltip: deep navy with white text.</summary>
internal sealed class ThemedToolTip : ToolTip
{
    public ThemedToolTip()
    {
        OwnerDraw = true;
        InitialDelay = 500;
        ReshowDelay = 100;
        AutoPopDelay = 12000;
        Popup += (_, e) =>
        {
            var text = GetToolTip(e.AssociatedControl);
            var size = TextRenderer.MeasureText(text, Theme.Small, new Size(Theme.S(320), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            e.ToolTipSize = new Size(size.Width + Theme.S(16), size.Height + Theme.S(10));
        };
        Draw += (_, e) =>
        {
            using var back = new SolidBrush(Theme.NavyDeep);
            e.Graphics.FillRectangle(back, e.Bounds);
            var r = Rectangle.Inflate(e.Bounds, -Theme.S(8), -Theme.S(5));
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Small, r, Color.White, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
        };
    }
}

/// <summary>A card of rows: a label on the left, a control on the right, hairlines between rows.</summary>
internal sealed class RowCard : Card
{
    private readonly List<int> _dividers = new();
    private int _y = Theme.S(4);

    public RowCard(int width) => Width = width;

    /// <summary>Adds a row; clicking the label of a check box or switch toggles it.</summary>
    public Label AddRow(string label, Control control, int height96 = 44)
    {
        if (_y > Theme.S(4)) _dividers.Add(_y);
        var h = Theme.S(height96);
        var l = new Label
        {
            Text = label, Font = Theme.Body, ForeColor = Theme.Ink, BackColor = Theme.Surface, AutoSize = false, AutoEllipsis = true,
            // Starts a pixel below the row's top so it does not cover the hairline above it.
            TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(Theme.S(16), _y + Math.Max(1, Theme.S(1)), Width - Theme.S(32) - control.Width - Theme.S(8), h - Math.Max(1, Theme.S(1))),
        };
        if (control is CheckBox box)
        {
            l.Cursor = Cursors.Hand;
            l.Click += (_, _) => { if (box.Enabled) { box.Checked = !box.Checked; box.Focus(); } };
        }
        control.Location = new Point(Width - Theme.S(16) - control.Width, _y + (h - control.Height) / 2);
        Controls.Add(l);
        Controls.Add(control);
        _y += h;
        Height = _y + Theme.S(4);
        return l;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var line = new SolidBrush(Theme.Divider);
        foreach (var y in _dividers) e.Graphics.FillRectangle(line, Theme.S(16), y, Width - Theme.S(32), Math.Max(1, Theme.S(1)));
    }
}

/// <summary>A number with − and + buttons (Up/Down, PageUp/PageDown and the mouse wheel work too).</summary>
internal sealed class Stepper : Control
{
    private int _value, _hover = -1;

    public Stepper(int minimum, int maximum, int value)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Minimum = minimum;
        Maximum = maximum;
        _value = Math.Clamp(value, minimum, maximum);
        TabStop = true;
        Size = Theme.S(108, 30);
        AccessibleRole = AccessibleRole.SpinButton;
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public event EventHandler? ValueChanged;

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            AccessibleDescription = v.ToString();
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Rectangle Minus => new(0, 0, Theme.S(32), Height);
    private Rectangle Plus => new(Width - Theme.S(32), 0, Theme.S(32), Height);

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Up or Keys.Add or Keys.Oemplus) Value++;
        else if (e.KeyCode is Keys.Down or Keys.Subtract or Keys.OemMinus) Value--;
        else if (e.KeyCode == Keys.PageUp) Value += 5;
        else if (e.KeyCode == Keys.PageDown) Value -= 5;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (Minus.Contains(e.Location)) Value--;
        else if (Plus.Contains(e.Location)) Value++;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = Minus.Contains(e.Location) ? 0 : Plus.Contains(e.Location) ? 1 : -1;
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        if (h != _hover) { _hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, r, Theme.Sf(6), Theme.Surface);
        for (var i = 0; i < 2; i++)
        {
            var b = i == 0 ? Minus : Plus;
            var enabled = i == 0 ? _value > Minimum : _value < Maximum;
            if (_hover == i && enabled)
            {
                using var clip = Theme.Round(r, Theme.Sf(6));
                var state = g.Save();
                g.SetClip(clip);
                using var hover = new SolidBrush(Theme.IceHover);
                g.FillRectangle(hover, b);
                g.Restore(state);
            }
            var s = Theme.Sf(12);
            using var pen = new Pen(enabled ? Theme.Navy : Theme.Disabled, Theme.Sf(1.6f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            var cx = b.X + b.Width / 2f;
            var cy = Height / 2f;
            g.DrawLine(pen, cx - s / 2.4f, cy, cx + s / 2.4f, cy);
            if (i == 1) g.DrawLine(pen, cx, cy - s / 2.4f, cx, cy + s / 2.4f);
        }
        using (var line = new SolidBrush(Theme.Divider))
        {
            g.FillRectangle(line, Minus.Right, Theme.S(6), Math.Max(1, Theme.S(1)), Height - Theme.S(12));
            g.FillRectangle(line, Plus.X, Theme.S(6), Math.Max(1, Theme.S(1)), Height - Theme.S(12));
        }
        Theme.DrawRound(g, r, Theme.Sf(6), Focused ? Theme.Azure : Theme.Border, Focused ? 1.5f : 1f);
        Theme.Text(g, _value.ToString(), Theme.BodyBold, new Rectangle(Minus.Right, 0, Plus.X - Minus.Right, Height), Theme.Ink, Theme.TextCenter);
    }
}
