using GW2MM.Core.Ahk;
using GW2MM.Core.Config;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Octave Delay / Always On Top / Transparency (plus update checks, UPnP or the client's launch prompts and solo humanizing, and keybinds). Saving writes Config.txt and applies the values immediately
/// (the original restarted the whole script, which also dropped every network connection).
/// </summary>
internal sealed class SettingsForm : Form
{
    public SettingsForm(AppSettings settings, string dataDir, bool isHost, Action applied, Action<IWin32Window> showKeybinds)
    {
        // The client has four more rows: the launch prompts option, the name and host address it uses, and how solo
        // play is humanized (the host never humanizes: players must all follow its timing).
        var extra = isHost ? 0 : 75;
        Ui.InitForm(this, "Settings", 200, 200 + extra);
        ShowInTaskbar = false;
        Ui.Label(this, "Octave Delay :", 0, 3, 95, 20);
        Ui.Button(this, "?", 170, 0, 20, 20, (_, _) => Ui.ShowText(this, "Octave Delay Explanation", Texts.OctaveDelay));
        var delay = Ui.Add(this, new TextBox { Text = settings.OctaveDelay }, 100, 0, 50, 20);
        var onTop = Ui.Add(this, new CheckBox { Text = "Enable Always On Top?", Checked = settings.AlwaysOnTop }, 0, 30, 170, 20);
        Ui.Button(this, "?", 170, 30, 20, 20, (_, _) => Ui.ShowText(this, "AlwaysOnTop Explanation", Texts.AlwaysOnTop));
        var transparent = Ui.Add(this, new CheckBox { Text = "Enable Transparency?", Checked = settings.IsTransparent }, 0, 50, 170, 20);
        Ui.Button(this, "?", 170, 50, 20, 20, (_, _) => Ui.ShowText(this, "Transparency Explanation", Texts.Transparency));
        var updates = Ui.Add(this, new CheckBox { Text = "Check for updates on launch?", Checked = settings.CheckForUpdates }, 0, 70, 200, 20);
        CheckBox? openPort = null, askOnLaunch = null;
        TextBox? name = null, hostAddress = null;
        ComboBox? humanize = null;
        if (isHost)
        {
            openPort = Ui.Add(this, new CheckBox { Text = "Auto-open port (UPnP)?", Checked = settings.OpenPortAutomatically }, 0, 90, 170, 20);
            Ui.Button(this, "?", 170, 90, 20, 20, (_, _) => Ui.ShowText(this, "UPnP Explanation", Texts.OpenPort));
        }
        else
        {
            askOnLaunch = Ui.Add(this, new CheckBox { Text = "Ask name && IP on launch?", Checked = settings.AskOnLaunch }, 0, 90, 170, 20);
            Ui.Button(this, "?", 170, 90, 20, 20, (_, _) => Ui.ShowText(this, "Launch Prompts Explanation", Texts.AskOnLaunch));
            Ui.Label(this, "Name :", 0, 118, 70, 20);
            name = Ui.Add(this, new TextBox { Text = settings.Name }, 70, 115, 130, 20);
            Ui.Label(this, "Host IP :", 0, 143, 70, 20);
            hostAddress = Ui.Add(this, new TextBox { Text = settings.HostAddress }, 70, 140, 130, 20);
            Ui.Label(this, "Humanize :", 0, 168, 70, 20);
            humanize = Ui.Add(this, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 70, 165, 95, 21);
            humanize.Items.AddRange(new object[] { "Off", "Subtle", "Natural", "Loose", "Phrased", "Expressive", "Rubato" });
            humanize.SelectedIndex = Math.Clamp((int)settings.Humanize, 0, humanize.Items.Count - 1);
            Ui.Button(this, "?", 170, 165, 20, 20, (_, _) => Ui.ShowText(this, "Humanize Explanation", Texts.Humanize, 420));
        }
        Ui.Button(this, "Keybinds…", 0, 120 + extra, 200, 28, (_, _) => showKeybinds(this));
        Ui.Button(this, "Save", 0, 160 + extra, 200, 40, (_, _) =>
        {
            if (delay.Text.Length > 0 && !AhkValue.TryNumber(delay.Text, out _))
            {
                MessageBox.Show(this, "Octave Delay must be a number.", Texts.AppTitle);
                return;
            }
            settings.OctaveDelay = delay.Text;
            settings.AlwaysOnTop = onTop.Checked;
            settings.IsTransparent = transparent.Checked;
            settings.CheckForUpdates = updates.Checked;
            if (openPort is not null) settings.OpenPortAutomatically = openPort.Checked;
            if (askOnLaunch is not null)
            {
                settings.AskOnLaunch = askOnLaunch.Checked;
                settings.Name = name!.Text.Replace(",", "").Trim();          // Config.txt is comma-separated
                settings.HostAddress = hostAddress!.Text.Replace(",", "").Trim();
                settings.Humanize = (Core.Humanizing.HumanizeLevel)Math.Max(0, humanize!.SelectedIndex);
            }
            try
            {
                settings.SaveDisplaySettings(dataDir);
                if (askOnLaunch is not null) settings.SaveClientSettings(dataDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Could not save Config.txt: " + ex.Message, Texts.AppTitle);
                return;
            }
            applied();
            Close();
        });
    }
}
