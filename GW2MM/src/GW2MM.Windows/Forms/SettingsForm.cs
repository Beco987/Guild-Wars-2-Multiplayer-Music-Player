using GW2MM.Core.Ahk;
using GW2MM.Core.Config;

namespace GW2MM.Windows.Forms;

/// <summary>
/// Octave Delay / Always On Top / Transparency. Saving writes Config.txt and applies the values immediately
/// (the original restarted the whole script, which also dropped every network connection).
/// </summary>
internal sealed class SettingsForm : Form
{
    public SettingsForm(AppSettings settings, string dataDir, Action applied)
    {
        Ui.InitForm(this, "Settings", 200, 200);
        ShowInTaskbar = false;
        Ui.Label(this, "Octave Delay :", 0, 3, 95, 20);
        Ui.Button(this, "?", 170, 0, 20, 20, (_, _) => Ui.ShowText(this, "Octave Delay Explanation", Texts.OctaveDelay));
        var delay = Ui.Add(this, new TextBox { Text = settings.OctaveDelay }, 100, 0, 50, 20);
        var onTop = Ui.Add(this, new CheckBox { Text = "Enable Always On Top?", Checked = settings.AlwaysOnTop }, 0, 30, 170, 20);
        Ui.Button(this, "?", 170, 30, 20, 20, (_, _) => Ui.ShowText(this, "AlwaysOnTop Explanation", Texts.AlwaysOnTop));
        var transparent = Ui.Add(this, new CheckBox { Text = "Enable Transparency?", Checked = settings.IsTransparent }, 0, 50, 170, 20);
        Ui.Button(this, "?", 170, 50, 20, 20, (_, _) => Ui.ShowText(this, "Transparency Explanation", Texts.Transparency));
        Ui.Button(this, "Save", 0, 160, 200, 40, (_, _) =>
        {
            if (delay.Text.Length > 0 && !AhkValue.TryNumber(delay.Text, out _))
            {
                MessageBox.Show(this, "Octave Delay must be a number.", Texts.AppTitle);
                return;
            }
            settings.OctaveDelay = delay.Text;
            settings.AlwaysOnTop = onTop.Checked;
            settings.IsTransparent = transparent.Checked;
            try { settings.SaveDisplaySettings(dataDir); }
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
