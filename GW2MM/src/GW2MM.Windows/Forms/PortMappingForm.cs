using GW2MM.Core.Ahk;
using GW2MM.Core.Arrangement;
using GW2MM.Core.Sessions;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Host "Port Mapping": which MIDI tracks each player slot plays, shown with the player's name, plus each slot's input
/// delay and measured ping. Save applies the changes; Cancel (or closing) reverts to the applied values. As before,
/// a client disconnecting applies the window's current values and closes it.
/// </summary>
internal sealed class PortMappingForm : ThemedDialog
{
    private const int LabelColumn = 120, Column = 112, Margin_ = 16;
    private readonly HostSession _session;
    private readonly CheckMark[,] _checks = new CheckMark[PortMapping.Slots + 1, PortMapping.Tracks + 1];
    private readonly FieldBox[] _delays = new FieldBox[PortMapping.Slots + 1];
    private readonly Label[] _pings = new Label[PortMapping.Slots + 1];
    private readonly PlayerCard[] _players = new PlayerCard[PortMapping.Slots + 1];
    private readonly ThemedToolTip _tips = new();

    public PortMappingForm(HostSession session)
        : base("Port Mapping", Margin_ * 2 + LabelColumn + Column * PortMapping.Slots, "Who plays which track",
            "Tick the MIDI tracks each player plays. Changes apply when you press Save.")
    {
        _session = session;
        int X(int slot) => Theme.S(LabelColumn + Column * (slot - 1));

        // One card per player slot, above its column.
        for (var s = 1; s <= PortMapping.Slots; s++)
        {
            _players[s] = new PlayerCard(s) { Bounds = new Rectangle(Theme.S(Margin_) + X(s) + Theme.S(4), Theme.S(14), Theme.S(Column - 8), Theme.S(58)) };
            Body.Controls.Add(_players[s]);
        }

        const int rowH = 30, tracksTop = 6;
        var grid = new GridCard(PortMapping.Tracks, rowH, tracksTop)
        {
            Bounds = new Rectangle(Theme.S(Margin_), Theme.S(84), Theme.S(LabelColumn + Column * PortMapping.Slots), Theme.S(tracksTop + rowH * PortMapping.Tracks + 92)),
        };
        Body.Controls.Add(grid);
        for (var t = 1; t <= PortMapping.Tracks; t++)
            for (var s = 1; s <= PortMapping.Slots; s++)
            {
                var check = new CheckMark { AccessibleName = $"Track {t}, player {s}" };
                check.Location = new Point(X(s) + (Theme.S(Column) - check.Width) / 2, Theme.S(tracksTop + rowH * (t - 1)) + (Theme.S(rowH) - check.Height) / 2);
                _checks[s, t] = check;
                grid.Controls.Add(check);
            }

        var delayTop = tracksTop + rowH * PortMapping.Tracks + 10;
        for (var s = 1; s <= PortMapping.Slots; s++)
        {
            var slot = s;
            var delay = new FieldBox { Suffix = "ms", Size = Theme.S(80, 30) };
            delay.Box.TextAlign = HorizontalAlignment.Right;
            delay.Box.PlaceholderText = "–";
            delay.Location = new Point(X(s) + (Theme.S(Column) - delay.Width) / 2, Theme.S(delayTop + 3));
            delay.Box.Validating += (_, e) =>
            {
                var text = _delays[slot].Box.Text.Trim();
                _delays[slot].Invalid = text.Length > 0 && !AhkValue.TryNumber(text, out double _);
                if (_delays[slot].Invalid)
                {
                    Dialogs.Show(this, "Input delay must be a number of milliseconds (or empty).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
            _tips.SetToolTip(delay.Box, "How long the game takes to sound a note on this player's computer (Sync measures it).");
            _delays[s] = delay;
            grid.Controls.Add(delay);
            _pings[s] = new Label
            {
                Text = "–", Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Stripe, TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(X(s), Theme.S(delayTop + 40), Theme.S(Column), Theme.S(30)),
            };
            _tips.SetToolTip(_pings[s], "One-way network delay, measured when this window opens (for information).");
            grid.Controls.Add(_pings[s]);
        }
        grid.ExtraRows = new[] { ("Input delay", delayTop, 36), ("Ping", delayTop + 40, 30) };

        var note = new Label
        {
            Text = "The host plays track n in its window Player n. A client plays its first ticked track in its Player1 window, its second in Player2, and so on.",
            Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(grid.Width, 0), Location = new Point(Theme.S(Margin_), grid.Bottom + Theme.S(10)),
        };
        Body.Controls.Add(note);
        note.Size = note.GetPreferredSize(new Size(grid.Width, 0));
        SetBodyHeight((int)Math.Ceiling(note.Bottom / Theme.Scale) + 14);

        var save = AddButton("Save", ButtonKind.Primary);
        save.Click += (_, _) => { if (ValidateChildren()) CommitAndHide(); };
        var cancel = AddButton("Cancel", ButtonKind.Secondary);
        cancel.CausesValidation = false;
        cancel.Click += (_, _) => Hide();
        AcceptButton = save;
        CancelButton = cancel;

        LoadFromSession();
        RefreshPlayers();
    }

    /// <summary>Updates the column headers from the session (names arrive when a client connects).</summary>
    public void RefreshPlayers()
    {
        for (var s = 1; s <= PortMapping.Slots; s++)
        {
            var p = _session.PlayerOf(s);
            string name, detail;
            if (p.IsHost) { name = "You (host)"; detail = "Ip1 · this computer"; }
            else if (!p.Connected) { name = "Open slot"; detail = $"Ip{s} · waiting"; }
            else { name = string.IsNullOrWhiteSpace(p.Name) ? "Connecting…" : p.Name!; detail = $"Ip{s} · {p.Address}"; }
            _players[s].Set(name, detail, p.Connected, p.IsHost);
            _tips.SetToolTip(_players[s], p.Connected ? $"{name} ({detail})" : "No player in this slot yet. Tracks ticked here apply to the next player who takes it.");
        }
    }

    /// <summary>Shows the values currently in effect (discarding unsaved edits).</summary>
    private void LoadFromSession()
    {
        var mapping = _session.Mapping;
        for (var s = 1; s <= PortMapping.Slots; s++)
        {
            for (var t = 1; t <= PortMapping.Tracks; t++) _checks[s, t].Checked = mapping[s, t];
            _delays[s].Box.Text = _session.InputDelayOf(s);
            _delays[s].Invalid = false;
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        if (Visible)
        {
            LoadFromSession();
            RefreshPlayers();
        }
        base.OnVisibleChanged(e);
    }

    /// <summary>Applies the window's values to the session and hides it.</summary>
    public void CommitAndHide()
    {
        var mapping = new PortMapping();
        for (var s = 1; s <= PortMapping.Slots; s++)
            for (var t = 1; t <= PortMapping.Tracks; t++)
                mapping[s, t] = _checks[s, t].Checked;
        _session.CommitPortMapping(mapping, Enumerable.Range(1, PortMapping.Slots).Select(s => _delays[s].Box.Text.Trim()).ToArray());
        Hide();
    }

    public void SetInputDelay(int slot, string value) => _delays[slot].Box.Text = value;

    public void SetClientPing(int slot, string value)
    {
        _pings[slot].Text = value.Length > 0 && value != "–" ? value + " ms" : value;
        _pings[slot].ForeColor = Theme.Ink2;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}

/// <summary>A player slot's card: numbered avatar with a presence dot, the player's name and where they connect from.</summary>
internal sealed class PlayerCard : Control
{
    private readonly int _slot;
    private string _name = "", _detail = "";
    private bool _connected, _host;

    public PlayerCard(int slot)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _slot = slot;
    }

    public void Set(string name, string detail, bool connected, bool host)
    {
        (_name, _detail, _connected, _host) = (name, detail, connected, host);
        AccessibleName = $"Player {_slot}: {name}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.PaintParent(this, g);
        Theme.Smooth(g);
        var r = new RectangleF(0, 0, Width, Height);
        if (_connected)
        {
            Theme.FillRound(g, r, Theme.Sf(8), Theme.Surface);
            Theme.DrawRound(g, r, Theme.Sf(8), Theme.Border);
        }
        else
        {
            Theme.FillRound(g, r, Theme.Sf(8), Theme.Alpha(Theme.Surface, 140));
            using var dash = new Pen(Theme.BorderStrong, Theme.Sf(1)) { DashPattern = new[] { 3f, 2f } };
            using var path = Theme.Round(RectangleF.Inflate(r, -Theme.Sf(0.5f), -Theme.Sf(0.5f)), Theme.Sf(8));
            g.DrawPath(dash, path);
        }
        var avatar = new RectangleF(Theme.Sf(9), Theme.Sf(9), Theme.Sf(24), Theme.Sf(24));
        if (_host)
        {
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(RectangleF.Inflate(avatar, 0, 1), Theme.NavyLight, Theme.Navy, 90f);
            g.FillEllipse(brush, avatar);
        }
        else
        {
            using var brush = new SolidBrush(_connected ? Theme.LavenderTint : Theme.Track);
            g.FillEllipse(brush, avatar);
        }
        Theme.Text(g, _slot.ToString(), Theme.SmallBold, Rectangle.Round(avatar), _host ? Color.White : _connected ? Theme.LavenderText : Theme.Muted, Theme.TextCenter);
        var dot = new RectangleF(avatar.Right - Theme.Sf(7), avatar.Bottom - Theme.Sf(7), Theme.Sf(9), Theme.Sf(9));
        using (var ring = new SolidBrush(Theme.Surface)) g.FillEllipse(ring, dot);
        using (var b = new SolidBrush(_connected ? Theme.Success : Theme.Offline)) g.FillEllipse(b, RectangleF.Inflate(dot, -Theme.Sf(1.5f), -Theme.Sf(1.5f)));
        var x = Theme.S(40);
        Theme.Text(g, _name, Theme.BodyBold, new Rectangle(x, Theme.S(8), Width - x - Theme.S(6), Theme.S(18)), _connected ? Theme.Title : Theme.Muted);
        Theme.Text(g, _detail, Theme.Tiny, new Rectangle(Theme.S(9), Theme.S(36), Width - Theme.S(15), Theme.S(16)), Theme.Muted);
    }
}

/// <summary>The track grid's card: row labels, hairlines between tracks and a tinted band for the delay and ping rows.</summary>
internal sealed class GridCard : Card
{
    private readonly int _rows, _rowH, _top;

    public GridCard(int rows, int rowH, int top) => (_rows, _rowH, _top) = (rows, rowH, top);

    public (string Label, int Top, int Height)[] ExtraRows { get; set; } = Array.Empty<(string, int, int)>();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using var line = new SolidBrush(Theme.Divider);
        var px = Math.Max(1, Theme.S(1));
        for (var t = 1; t <= _rows; t++)
        {
            var y = Theme.S(_top + _rowH * (t - 1));
            Theme.Text(g, $"Track {t}", Theme.Body, new Rectangle(Theme.S(16), y, Theme.S(100), Theme.S(_rowH)), Theme.Ink2);
            if (t > 1) g.FillRectangle(line, Theme.S(14), y, Width - Theme.S(28), px);
        }
        if (ExtraRows.Length == 0) return;
        var bandTop = Theme.S(ExtraRows[0].Top - 6);
        using (var band = new SolidBrush(Theme.Stripe)) g.FillRectangle(band, px, bandTop, Width - 2 * px, Height - bandTop - Theme.S(8));
        using (var border = new SolidBrush(Theme.Border)) g.FillRectangle(border, px, bandTop, Width - 2 * px, px);
        foreach (var (label, top, height) in ExtraRows)
            Theme.Text(g, label, Theme.BodyBold, new Rectangle(Theme.S(16), Theme.S(top), Theme.S(110), Theme.S(height)), Theme.Ink);
    }
}
