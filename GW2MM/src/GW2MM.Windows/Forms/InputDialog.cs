namespace GW2MM.Windows.Forms;

/// <summary>AutoHotkey InputBox replacement. Returns null when cancelled.</summary>
internal sealed class InputDialog : Form
{
    private readonly TextBox _text;

    private InputDialog(string title, string prompt, string defaultValue)
    {
        Ui.InitForm(this, title, 300, 110);
        ForeColor = SystemColors.ControlText;
        Ui.Label(this, prompt, 10, 10, 280, 20);
        _text = Ui.Add(this, new TextBox { Text = defaultValue }, 10, 35, 280, 22);
        var ok = Ui.Button(this, "OK", 130, 70, 75, 26, (_, _) => { DialogResult = DialogResult.OK; Close(); });
        var cancel = Ui.Button(this, "Cancel", 215, 70, 75, 26, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        AcceptButton = ok;
        CancelButton = cancel;
        MinimizeBox = false;
    }

    public static (bool Ok, string Text) Ask(string title, string prompt, string defaultValue)
    {
        using var d = new InputDialog(title, prompt, defaultValue);
        var ok = d.ShowDialog() == DialogResult.OK;
        return (ok, d._text.Text);
    }
}
