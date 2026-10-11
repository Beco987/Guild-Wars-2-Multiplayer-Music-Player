using GW2MM.Core.Arrangement;
using GW2MM.Core.Sessions;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Host "Port" window: which tracks each player slot (Ip1 = this computer, Ip2..Ip5 = clients) plays, each slot's
/// input delay and the measured client ping. Edits only take effect on Save (or when a client disconnects), as before.
/// </summary>
internal sealed class PortMappingForm : Form
{
    private readonly HostSession _session;
    private readonly CheckBox[,] _checks = new CheckBox[PortMapping.Slots + 1, PortMapping.Tracks + 1];
    private readonly TextBox[] _delays = new TextBox[PortMapping.Slots + 1];
    private readonly TextBox[] _pings = new TextBox[PortMapping.Slots + 1];

    public PortMappingForm(HostSession session)
    {
        _session = session;
        Ui.InitForm(this, "Port Mapping", 320, 350);
        Font = new Font("Segoe UI", 10F);
        ShowInTaskbar = false;
        var bold = new Font("Segoe UI", 10F, FontStyle.Bold);
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            var x = slot * 50 + 30;
            Ui.Label(this, "Ip" + slot, x, 0, 45, 20).Font = bold;
            for (var track = 1; track <= PortMapping.Tracks; track++)
            {
                var y = (track - 1) * 20 + 40;
                if (slot == 1) Ui.Label(this, $"Track {track} :", 20, y, 60, 20);
                _checks[slot, track] = Ui.Add(this, new CheckBox(), x, y, 20, 20);
            }
        }
        Ui.Label(this, "Input Delay", 0, 200, 100, 20).Font = bold;
        Ui.Label(this, "Client Ping", 0, 250, 100, 20).Font = bold;
        var small = new Font("Segoe UI", 9F);
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
        {
            var x = slot * 50 + 30;
            _delays[slot] = Ui.Add(this, new TextBox { Text = "0", Font = small }, x, 220, 40, 22);
            _pings[slot] = Ui.Add(this, new TextBox { Text = "0", Font = small }, x, 270, 40, 22);
        }
        Ui.Button(this, "Save", 135, 300, 50, 40, (_, _) => CommitAndHide());
    }

    /// <summary>Applies the window's values to the session and hides it (the original "Gui, PortMappingGui:Submit").</summary>
    public void CommitAndHide()
    {
        var mapping = new PortMapping();
        for (var slot = 1; slot <= PortMapping.Slots; slot++)
            for (var track = 1; track <= PortMapping.Tracks; track++)
                mapping[slot, track] = _checks[slot, track].Checked;
        _session.CommitPortMapping(mapping, Enumerable.Range(1, PortMapping.Slots).Select(s => _delays[s].Text.Trim()).ToArray());
        Hide();
    }

    public void SetInputDelay(int slot, string value) => _delays[slot].Text = value;
    public void SetClientPing(int slot, string value) => _pings[slot].Text = value;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing only hides the window; unsaved edits stay in it but are not applied.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
