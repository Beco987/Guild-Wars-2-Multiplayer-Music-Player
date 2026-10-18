namespace GW2MM.Windows.Forms;

/// <summary>Asks for one line of text (a playlist name). The OK button is labelled with the action ("Create", "Rename").</summary>
internal sealed class InputDialog : ThemedDialog
{
    private readonly FieldBox _text;

    private InputDialog(string title, string prompt, string defaultValue, string verb) : base(title, 360)
    {
        var label = new Label { Text = prompt, Font = Theme.BodyBold, ForeColor = Theme.Ink, AutoSize = true, Location = Theme.S(20, 16, 0, 0).Location };
        _text = new FieldBox { Bounds = Theme.S(20, 40, 320, 30) };
        _text.Box.Text = defaultValue;
        Body.Controls.Add(label);
        Body.Controls.Add(_text);
        SetBodyHeight(90);
        AcceptButton = AddButton(verb, ButtonKind.Primary, DialogResult.OK);
        CancelButton = AddButton("Cancel", ButtonKind.Secondary, DialogResult.Cancel);
        ActiveControl = _text.Box;
        _text.Box.SelectAll();
    }

    public static (bool Ok, string Text) Ask(IWin32Window? owner, string title, string prompt, string defaultValue, string verb = "OK")
    {
        using var d = new InputDialog(title, prompt, defaultValue, verb);
        var ownerForm = (owner as Control)?.FindForm();
        if (ownerForm is not null) d.TopMost = ownerForm.TopMost;
        var ok = (ownerForm is null ? d.ShowDialog() : d.ShowDialog(ownerForm)) == DialogResult.OK;
        return (ok, d._text.Box.Text);
    }
}
