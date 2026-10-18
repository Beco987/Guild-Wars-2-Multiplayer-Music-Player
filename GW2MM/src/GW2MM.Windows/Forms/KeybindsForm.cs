using GW2MM.Core.Config;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Lets the user change every hotkey: click a box and press the new combination; Backspace/Delete clears it.
/// Saving stores the bindings in Config.txt; the main window re-registers them when this window closes.
/// </summary>
internal sealed class KeybindsForm : ThemedDialog
{
    private readonly IReadOnlyList<HotkeyAction> _actions;
    private readonly Dictionary<string, Hotkey> _keys;
    private readonly Dictionary<string, KeyCapBox> _boxes = new();
    private readonly Label _problem;
    private readonly Action<IReadOnlyDictionary<string, Hotkey>> _save;

    public KeybindsForm(AppRole role, IReadOnlyList<(HotkeyAction Action, Hotkey Keys)> current, Action<IReadOnlyDictionary<string, Hotkey>> save)
        : base("Keybinds", 420, "Keybinds", "Click a box and press a key. Delete clears it.")
    {
        _save = save;
        _actions = current.Select(c => c.Action).ToList();
        _keys = current.ToDictionary(c => c.Action.Id, c => c.Keys);
        KeyPreview = false;

        // Grouped for reading: playback keys, the window-naming keys, then Exit.
        var groups = new[]
        {
            ("Playback", _actions.Where(a => !a.Id.StartsWith("Window", StringComparison.Ordinal) && a.Id != "Exit")),
            ("Game windows", _actions.Where(a => a.Id.StartsWith("Window", StringComparison.Ordinal))),
            ("App", _actions.Where(a => a.Id == "Exit")),
        };
        var card = new Card { AutoScroll = true, Location = Theme.S(16, 12, 0, 0).Location, Width = Theme.S(388) };
        var y = Theme.S(6);
        foreach (var (title, actions) in groups)
        {
            var list = actions.ToList();
            if (list.Count == 0) continue;
            card.Controls.Add(new Overline(title) { BackColor = Theme.Surface, Location = new Point(Theme.S(16), y + Theme.S(8)) });
            y += Theme.S(28);
            foreach (var action in list)
            {
                var box = new KeyCapBox { Size = Theme.S(170, 28), AccessibleName = action.Label };
                box.KeyDown += (_, e) => CaptureKey(action.Id, e);
                _boxes[action.Id] = box;
                card.Controls.Add(new Label
                {
                    Text = action.Label == "Exit" ? "Exit (resets octaves)" : action.Label, Font = Theme.Body, ForeColor = Theme.Ink, BackColor = Theme.Surface,
                    AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Bounds = new Rectangle(Theme.S(16), y, Theme.S(180), Theme.S(34)),
                });
                box.Location = new Point(card.Width - Theme.S(28) - box.Width, y + Theme.S(3));
                card.Controls.Add(box);
                y += Theme.S(34);
            }
        }
        // Tall enough for every row, unless the screen is short; then the card scrolls.
        var room = Screen.PrimaryScreen is { } screen ? screen.WorkingArea.Height - Theme.S(66 + 60 + 50 + 80) : int.MaxValue;
        card.Height = Math.Min(y + Theme.S(8), Math.Max(Theme.S(200), room));
        Body.Controls.Add(card);
        ThemedScrollBar.Attach(card);

        _problem = new Label { AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.DangerText, Location = new Point(Theme.S(18), card.Bottom + Theme.S(8)) };
        Body.Controls.Add(_problem);
        SetBodyHeight((int)Math.Ceiling(card.Bottom / Theme.Scale) + 30);

        var saveButton = AddButton("Save", ButtonKind.Primary);
        saveButton.Click += (_, _) => Save();
        var cancel = AddButton("Cancel", ButtonKind.Secondary, DialogResult.Cancel);
        CancelButton = cancel;
        var reset = new ThemedButton("Defaults", ButtonKind.Ghost, Glyph.Reload) { FixedHeight = Theme.S(32), BackColor = Theme.FooterBack };
        reset.Size = reset.GetPreferredSize(Size.Empty);
        reset.Click += (_, _) =>
        {
            foreach (var a in _actions) _keys[a.Id] = Hotkey.Parse(a.Default);
            UpdateView();
        };
        SetFooterLeft(reset);
        UpdateView();
    }

    private void CaptureKey(string id, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.Back or Keys.Delete && e.Modifiers == Keys.None)
        {
            _keys[id] = Hotkey.None;
        }
        else
        {
            var vk = (uint)e.KeyCode;
            if (!Hotkey.IsBindable(vk)) return; // a modifier on its own, Tab, Enter, ...
            var mods = HotkeyModifiers.None;
            if (e.Control) mods |= HotkeyModifiers.Ctrl;
            if (e.Alt) mods |= HotkeyModifiers.Alt;
            if (e.Shift) mods |= HotkeyModifiers.Shift;
            _keys[id] = new Hotkey(mods, vk);
        }
        UpdateView();
    }

    // Keys like F10 and Alt+key arrive as system keys; route them to the focused box too.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is KeyCapBox box && _boxes.ContainsValue(box) && (keyData & Keys.KeyCode) is not (Keys.Tab or Keys.Escape))
        {
            var id = _boxes.First(b => b.Value == box).Key;
            CaptureKey(id, new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateView()
    {
        var duplicates = _keys.Where(k => !k.Value.IsNone).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var (id, box) in _boxes)
        {
            box.Keys = _keys[id];
            box.Clash = duplicates.Contains(_keys[id]);
        }
        _problem.Text = duplicates.Count > 0 ? "Used twice: " + string.Join(", ", duplicates.Select(d => d.ToString())) : "";
    }

    private void Save()
    {
        if (_problem.Text.Length > 0) return;
        _save(_keys);
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>A key combination drawn as key caps ("Shift + F1"); focus it and press keys to change it.</summary>
internal sealed class KeyCapBox : Control
{
    private Hotkey _keys = Hotkey.None;
    private bool _clash, _hover;

    public KeyCapBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    public Hotkey Keys
    {
        get => _keys;
        set { _keys = value; AccessibleDescription = value.IsNone ? "none" : value.ToString(); Invalidate(); }
    }

    public bool Clash
    {
        get => _clash;
        set { _clash = value; Invalidate(); }
    }

    protected override bool IsInputKey(Keys keyData) => (keyData & System.Windows.Forms.Keys.KeyCode) is not (System.Windows.Forms.Keys.Tab or System.Windows.Forms.Keys.Escape);

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, r, Theme.Sf(6), Focused ? Theme.Surface : _hover ? Theme.IceHover : Theme.Inset);
        Theme.DrawRound(g, r, Theme.Sf(6), _clash ? Theme.Danger : Focused ? Theme.Azure : Theme.Divider, _clash || Focused ? 1.5f : 1f);
        if (_keys.IsNone)
        {
            Theme.Text(g, Focused ? "Press a key…" : "—", Theme.Body, Rectangle.Round(r), Theme.Placeholder, Theme.TextCenter);
            return;
        }
        // Each part ("Ctrl", "Shift", "F1") as a small key cap, joined by "+".
        var parts = _keys.ToString().Split('+');
        var font = Theme.SmallBold;
        var plus = Theme.Measure("+", Theme.Small).Width + Theme.S(6);
        var widths = parts.Select(p => Theme.Measure(p, font).Width + Theme.S(12)).ToList();
        var total = widths.Sum() + plus * (parts.Length - 1);
        var x = Math.Max(Theme.S(4), (Width - total) / 2f);
        var capH = Theme.Sf(20);
        for (var i = 0; i < parts.Length; i++)
        {
            var cap = new RectangleF(x, (Height - capH) / 2, widths[i], capH);
            Theme.FillRound(g, cap, Theme.Sf(4), Theme.Surface);
            Theme.DrawRound(g, cap, Theme.Sf(4), Theme.BorderStrong);
            Theme.Text(g, parts[i], font, Rectangle.Round(cap), Theme.Title, Theme.TextCenter);
            x += widths[i];
            if (i < parts.Length - 1)
            {
                Theme.Text(g, "+", Theme.Small, new Rectangle((int)x, 0, plus, Height), Theme.Muted, Theme.TextCenter);
                x += plus;
            }
        }
    }
}
