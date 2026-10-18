using System.Globalization;
using GW2MM.Core.Ahk;
using GW2MM.Core.Config;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The Settings window, the same on host and client except that only the host has the UPnP option and only the
/// client has the server connection. Saving writes Config.txt and applies the values immediately (the original
/// restarted the whole script, which also dropped every network connection). Cancel, Esc or closing discard edits.
/// </summary>
internal sealed class SettingsForm : ThemedDialog
{
    private const int Width96 = 340;

    /// <summary>The client's link to a server: whether it is connected, and connecting (asks for name and IP) or disconnecting.</summary>
    public sealed record Connection(Func<bool> IsConnected, Action<IWin32Window> Connect, Action Disconnect);

    /// <param name="connection">The client's server connection; null on the host (no server section).</param>
    public SettingsForm(AppSettings settings, string dataDir, bool isHost, Action applied, Action<IWin32Window> showKeybinds,
        Connection? connection = null) : base("Settings", Width96, "Settings")
    {
        var cardWidth = Theme.S(Width96 - 32);
        var y = Theme.S(12);
        RowCard? previous = null;

        var playback = Section("Playback");
        var delay = Number(settings.OctaveDelay);
        var offset = Number(settings.OffsetMs.ToString(CultureInfo.InvariantCulture));
        playback.AddRow("Octave delay", delay);
        playback.AddRow("Offset", offset);

        var window = Section("Window");
        var onTop = new ToggleSwitch("Always on top") { Checked = settings.AlwaysOnTop };
        var transparent = new ToggleSwitch("Transparent") { Checked = settings.IsTransparent };
        window.AddRow("Always on top", onTop);
        window.AddRow("Transparent", transparent);

        var other = Section(isHost ? "Startup & network" : "Startup");
        var updates = new ToggleSwitch("Check for updates on launch") { Checked = settings.CheckForUpdates };
        other.AddRow("Check for updates on launch", updates);
        ToggleSwitch? openPort = null;
        if (isHost)
        {
            openPort = new ToggleSwitch("Open port automatically (UPnP)") { Checked = settings.OpenPortAutomatically };
            other.AddRow("Open port automatically (UPnP)", openPort);
        }

        // Client only: connect to a server (or leave it). The row follows the connection while the window is open.
        if (connection is not null)
        {
            var server = Section("Server");
            var button = new ThemedButton("Connect…", ButtonKind.Tonal, Glyph.Plug) { FixedHeight = Theme.S(30) };
            var label = server.AddRow("Not connected", button);
            void Refresh()
            {
                var on = connection.IsConnected();
                label.Text = on ? "Connected" : "Not connected";
                button.Text = on ? "Disconnect" : "Connect…";
                button.Kind = on ? ButtonKind.Secondary : ButtonKind.Tonal;
                button.Size = button.GetPreferredSize(Size.Empty);
                button.Left = server.Width - Theme.S(16) - button.Width;
            }
            button.Click += (_, _) =>
            {
                if (connection.IsConnected()) connection.Disconnect();
                else connection.Connect(this);
                Refresh();
            };
            Refresh();
            var refresh = new System.Windows.Forms.Timer { Interval = 300 };
            refresh.Tick += (_, _) => Refresh();
            refresh.Start();
            FormClosed += (_, _) => refresh.Dispose();
        }

        var keys = Section(null);
        var edit = new ThemedButton("Edit…", ButtonKind.Secondary, Glyph.Keyboard) { FixedHeight = Theme.S(30) };
        edit.Size = edit.GetPreferredSize(Size.Empty);
        edit.Click += (_, _) => showKeybinds(this);
        keys.AddRow("Keybinds", edit);

        SetBodyHeight((int)Math.Ceiling((keys.Bottom + Theme.S(16)) / Theme.Scale));
        var save = AddButton("Save", ButtonKind.Primary);
        var cancel = AddButton("Cancel", ButtonKind.Secondary);
        cancel.Click += (_, _) => Close();     // a modeless window: Cancel (and Esc) just close it
        AcceptButton = save;
        CancelButton = cancel;
        save.Click += (_, _) =>
        {
            var delayText = delay.Box.Text.Trim();
            delay.Invalid = delayText.Length > 0 && (!AhkValue.TryNumber(delayText, out var d) || d < 0);
            if (delay.Invalid)
            {
                Dialogs.Show(this, "Octave delay must be a number of ms.", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            double offsetValue = 0;
            offset.Invalid = offset.Box.Text.Trim().Length > 0
                && (!AhkValue.TryNumber(offset.Box.Text.Trim(), out offsetValue) || offsetValue < 0 || offsetValue > AppSettings.MaxOffsetMs);
            if (offset.Invalid)
            {
                Dialogs.Show(this, $"Offset must be a number of ms from 0 to {AppSettings.MaxOffsetMs}.", Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                Dialogs.Show(this, "Could not save Config.txt: " + ex.Message, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            applied();
            Close();
        };

        RowCard Section(string? title)
        {
            // Each section starts below the previous card, which has grown by the rows added to it.
            if (previous is not null) y = previous.Bottom + Theme.S(14);
            if (title is not null)
            {
                Body.Controls.Add(new Overline(title) { Location = new Point(Theme.S(20), y) });
                y += Theme.S(18);
            }
            var card = new RowCard(cardWidth) { Location = new Point(Theme.S(16), y) };
            Body.Controls.Add(card);
            previous = card;
            return card;
        }

        FieldBox Number(string value)
        {
            var f = new FieldBox { Suffix = "ms", Size = Theme.S(100, 30) };
            f.Box.Text = value;
            f.Box.TextAlign = HorizontalAlignment.Right;
            f.Box.TextChanged += (_, _) => f.Invalid = false;
            return f;
        }
    }
}
