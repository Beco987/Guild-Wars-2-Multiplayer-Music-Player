using System.Globalization;
using GW2MM.Core.Ahk;
using GW2MM.Core.Config;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The Settings window, the same on host and client except that only the host has the UPnP option; the client's
/// Connect to server button is greyed out on the host. Saving writes Config.txt and applies the values immediately (the original restarted the whole script,
/// which also dropped every network connection).
/// </summary>
internal sealed class SettingsForm : Form
{
    private const int Width_ = 220;

    /// <summary>The client's link to a server: whether it is connected, and connecting (asks for name and IP) or disconnecting.</summary>
    public sealed record Connection(Func<bool> IsConnected, Action<IWin32Window> Connect, Action Disconnect);

    /// <param name="connection">The client's server connection; null on the host, where the button is greyed out.</param>
    public SettingsForm(AppSettings settings, string dataDir, bool isHost, Action applied, Action<IWin32Window> showKeybinds,
        Connection? connection = null)
    {
        Ui.InitForm(this, "Settings", Width_, 330);
        ShowInTaskbar = false;

        var y = 0;
        TextBox Field(string label, string value, int width, bool enabled = true)
        {
            Ui.Label(this, label, 0, y + 3, Width_ - width - 5, 20).Enabled = enabled;
            var box = Ui.Add(this, new TextBox { Text = value, Enabled = enabled }, Width_ - width, y, width, 20);
            y += 26;
            return box;
        }
        CheckBox Check(string label, bool value, bool enabled = true)
        {
            var box = Ui.Add(this, new CheckBox { Text = label, Checked = value, Enabled = enabled }, 0, y, Width_, 20);
            y += 22;
            return box;
        }

        var delay = Field("Octave delay (ms)", settings.OctaveDelay, 60);
        var offset = Field("Offset (ms)", settings.OffsetMs.ToString(CultureInfo.InvariantCulture), 60);
        y += 4;
        var onTop = Check("Always on top", settings.AlwaysOnTop);
        var transparent = Check("Transparent", settings.IsTransparent);
        var updates = Check("Check for updates on launch", settings.CheckForUpdates);
        var openPort = isHost ? Check("Open port automatically (UPnP)", settings.OpenPortAutomatically) : null;
        y += 8;
        // Client: connect to a server (or leave it). The label follows the connection while the window is open.
        string ConnectText() => connection?.IsConnected() == true ? "Disconnect from server" : "Connect to server…";
        var connect = Ui.Button(this, ConnectText(), 0, y, Width_, 28, (_, _) =>
        {
            if (connection is null) return;
            if (connection.IsConnected()) connection.Disconnect();
            else connection.Connect(this);
        });
        connect.Enabled = connection is not null;
        if (connection is not null)
        {
            var refresh = new System.Windows.Forms.Timer { Interval = 300 };
            refresh.Tick += (_, _) => connect.Text = ConnectText();
            refresh.Start();
            FormClosed += (_, _) => refresh.Dispose();
        }
        y += 34;
        Ui.Button(this, "Keybinds…", 0, y, Width_, 28, (_, _) => showKeybinds(this));
        y += 34;
        Ui.Button(this, "Save", 0, y, Width_, 36, (_, _) =>
        {
            var delayText = delay.Text.Trim();
            if (delayText.Length > 0 && (!AhkValue.TryNumber(delayText, out var d) || d < 0))
            {
                MessageBox.Show(this, "Octave delay must be a number of ms.", Texts.AppTitle);
                return;
            }
            double offsetValue = 0;
            if (offset.Text.Trim().Length > 0
                && (!AhkValue.TryNumber(offset.Text.Trim(), out offsetValue) || offsetValue < 0 || offsetValue > AppSettings.MaxOffsetMs))
            {
                MessageBox.Show(this, $"Offset must be a number of ms from 0 to {AppSettings.MaxOffsetMs}.", Texts.AppTitle);
                return;
            }
            settings.OctaveDelay = delayText.Length > 0 ? delayText : AppSettings.DefaultOctaveDelay;
            settings.OffsetMs = (int)Math.Floor(offsetValue);
            settings.AlwaysOnTop = onTop.Checked;
            settings.IsTransparent = transparent.Checked;
            settings.CheckForUpdates = updates.Checked;
            if (openPort is not null) settings.OpenPortAutomatically = openPort.Checked;
            try
            {
                settings.SaveDisplaySettings(dataDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Could not save Config.txt: " + ex.Message, Texts.AppTitle);
                return;
            }
            applied();
            Close();
        });
        ClientSize = new Size(Width_, y + 36);
    }
}
