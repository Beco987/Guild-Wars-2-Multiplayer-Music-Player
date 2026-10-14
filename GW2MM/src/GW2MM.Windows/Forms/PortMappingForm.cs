using GW2MM.Core.Ahk;
using GW2MM.Core.Arrangement;
using GW2MM.Core.Sessions;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Host "Port Mapping": which MIDI tracks each player slot plays, shown with the player's name, plus each slot's input
/// delay and measured ping. Save applies the changes; Cancel (or closing) reverts to the applied values. As before,
/// a client disconnecting applies the window's current values and closes it.
/// </summary>
internal sealed class PortMappingForm : Form
{
    private const int ColumnWidth = 124;
    private readonly HostSession _session;
    private readonly CheckBox[,] _checks = new CheckBox[PortMapping.Slots + 1, PortMapping.Tracks + 1];
    private readonly TextBox[] _delays = new TextBox[PortMapping.Slots + 1];
    private readonly Label[] _pings = new Label[PortMapping.Slots + 1];
    private readonly Label[] _names = new Label[PortMapping.Slots + 1];
    private readonly Label[] _details = new Label[PortMapping.Slots + 1];
    private readonly Panel[] _dots = new Panel[PortMapping.Slots + 1];
    private readonly ToolTip _tips = new();

    public PortMappingForm(HostSession session)
    {
        _session = session;
        Modern.InitDialog(this, "Port Mapping");
        var root = Modern.Stack(this);
        root.Controls.Add(Modern.Heading("Who plays which track"));
        root.Controls.Add(Modern.Caption(
            "Tick the MIDI tracks each player plays. The host plays track n in its window Player n; a client plays its first " +
            "ticked track in its Player1 window, its second in Player2, and so on. Changes apply when you press Save.", 660));

        var grid = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = PortMapping.Slots + 1, BackColor = Modern.Surface, Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 4), CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        for (var s = 1; s <= PortMapping.Slots; s++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ColumnWidth));
        root.Controls.Add(grid);

        // Header: one card per player slot.
        grid.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 0);
        for (var s = 1; s <= PortMapping.Slots; s++) grid.Controls.Add(PlayerHeader(s), s, 0);

        for (var t = 1; t <= PortMapping.Tracks; t++)
        {
            var stripe = t % 2 == 1 ? Modern.Stripe : Modern.Surface;
            grid.Controls.Add(RowLabel($"Track {t}", stripe), 0, t);
            for (var s = 1; s <= PortMapping.Slots; s++)
            {
                var cell = new Panel { BackColor = stripe, Dock = DockStyle.Fill, Height = 28, Margin = new Padding(0) };
                var check = new CheckBox { AutoSize = true, BackColor = stripe, Cursor = Cursors.Hand };
                cell.Controls.Add(check);
                cell.Resize += (_, _) => check.Location = new Point((cell.Width - check.Width) / 2, (cell.Height - check.Height) / 2);
                _checks[s, t] = check;
                grid.Controls.Add(cell, s, t);
            }
        }

        var delayRow = PortMapping.Tracks + 1;
        var pingRow = delayRow + 1;
        grid.Controls.Add(RowLabel("Input delay (ms)", Modern.Surface, top: 10), 0, delayRow);
        grid.Controls.Add(RowLabel("Ping (ms)", Modern.Surface), 0, pingRow);
        for (var s = 1; s <= PortMapping.Slots; s++)
        {
            var slot = s;
            _delays[s] = new TextBox { Width = 64, TextAlign = HorizontalAlignment.Center, Anchor = AnchorStyles.None, Margin = new Padding(0, 10, 0, 2) };
            _delays[s].Validating += (_, e) =>
            {
                var text = _delays[slot].Text.Trim();
                if (text.Length > 0 && !AhkValue.TryNumber(text, out double _))
                {
                    MessageBox.Show(this, "Input delay must be a number of milliseconds (or empty).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
            _tips.SetToolTip(_delays[s], "How long the game takes to sound a note on this player's computer (Sync measures it).");
            grid.Controls.Add(_delays[s], s, delayRow);
            _pings[s] = new Label { Text = "–", AutoSize = true, ForeColor = Modern.Muted, Anchor = AnchorStyles.None, Margin = new Padding(0, 6, 0, 6) };
            _tips.SetToolTip(_pings[s], "One-way network delay measured with the Ping button (for information).");
            grid.Controls.Add(_pings[s], s, pingRow);
        }

        var save = Modern.Primary("Save");
        save.Click += (_, _) => { if (ValidateChildren()) CommitAndHide(); };
        var cancel = Modern.Secondary("Cancel");
        cancel.CausesValidation = false;
        cancel.Click += (_, _) => Hide();
        root.Controls.Add(Modern.ButtonRow(save, cancel));
        AcceptButton = save;
        CancelButton = cancel;

        LoadFromSession();
        RefreshPlayers();
    }

    private Control PlayerHeader(int slot)
    {
        var card = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 8) };
        _dots[slot] = new Panel { Size = new Size(8, 8), Margin = new Padding(0, 6, 5, 0) };
        _dots[slot].Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var b = new SolidBrush(_dots[slot].ForeColor);
            e.Graphics.FillEllipse(b, 0, 0, 7, 7);
        };
        _names[slot] = new Label { AutoSize = false, Width = ColumnWidth - 18, Height = 20, Font = Modern.Bold, AutoEllipsis = true, Margin = new Padding(0) };
        _details[slot] = new Label { AutoSize = false, Width = ColumnWidth - 6, Height = 16, Font = Modern.Small, ForeColor = Modern.Muted, AutoEllipsis = true, Margin = new Padding(0) };
        card.Controls.Add(_dots[slot], 0, 0);
        card.Controls.Add(_names[slot], 1, 0);
        card.Controls.Add(_details[slot], 0, 1);
        card.SetColumnSpan(_details[slot], 2);
        return card;
    }

    private static Label RowLabel(string text, Color back, int top = 0) => new()
    {
        Text = text, AutoSize = false, Dock = DockStyle.Fill, Height = 28, BackColor = back, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, top, 0, 0),
    };

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
            _names[s].Text = name;
            _names[s].ForeColor = p.Connected ? Modern.Accent : Modern.Muted;
            _details[s].Text = detail;
            _dots[s].ForeColor = p.Connected ? Modern.Online : Modern.Offline;
            _dots[s].Invalidate();
            _tips.SetToolTip(_names[s], p.Connected ? $"{name} ({detail})" : "No player in this slot yet. Tracks ticked here apply to the next player who takes it.");
        }
    }

    /// <summary>Shows the values currently in effect (discarding unsaved edits).</summary>
    private void LoadFromSession()
    {
        var mapping = _session.Mapping;
        for (var s = 1; s <= PortMapping.Slots; s++)
        {
            for (var t = 1; t <= PortMapping.Tracks; t++) _checks[s, t].Checked = mapping[s, t];
            _delays[s].Text = _session.InputDelayOf(s);
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
        _session.CommitPortMapping(mapping, Enumerable.Range(1, PortMapping.Slots).Select(s => _delays[s].Text.Trim()).ToArray());
        Hide();
    }

    public void SetInputDelay(int slot, string value) => _delays[slot].Text = value;
    public void SetClientPing(int slot, string value) => _pings[slot].Text = value;

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
