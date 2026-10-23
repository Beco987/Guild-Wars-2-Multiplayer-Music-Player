namespace GW2MM.Windows.Forms;

/// <summary>Connect to server: asks for the user name and the server's IP address.</summary>
internal sealed class ConnectForm : ThemedDialog
{
    private readonly FieldBox _name, _address;

    private ConnectForm(string name, string address) : base("Connect to server", 340, "Connect to server")
    {
        _name = Field("Your name", name, 14);
        _address = Field("Server IP", address, 74);
        var problem = new Label { Font = Theme.Small, ForeColor = Theme.DangerText, AutoSize = true, Location = Theme.S(20, 132, 0, 0).Location };
        Body.Controls.Add(problem);
        SetBodyHeight(150);
        var connect = AddButton("Connect", ButtonKind.Primary, DialogResult.None, Glyph.Plug);
        CancelButton = AddButton("Cancel", ButtonKind.Secondary, DialogResult.Cancel);
        AcceptButton = connect;
        connect.Click += (_, _) =>
        {
            _name.Invalid = Name_.Length == 0;
            _address.Invalid = Address.Length == 0;
            if (_name.Invalid || _address.Invalid)
            {
                problem.Text = "Enter your name and the server's IP address.";
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        ActiveControl = name.Length == 0 ? _name.Box : _address.Box;

        FieldBox Field(string label, string value, int y)
        {
            Body.Controls.Add(new Label { Text = label, Font = Theme.BodyBold, ForeColor = Theme.Ink, AutoSize = true, Location = Theme.S(20, y, 0, 0).Location });
            var f = new FieldBox { Bounds = Theme.S(20, y + 22, 300, 30) };
            f.Box.Text = value;
            f.Box.TextChanged += (_, _) => f.Invalid = false;
            Body.Controls.Add(f);
            return f;
        }
    }

    private string Name_ => _name.Box.Text.Replace(",", "").Trim();          // Config.txt is comma-separated
    private string Address => _address.Box.Text.Replace(",", "").Trim();

    /// <summary>Shows the dialog filled with the last name and address; null when cancelled.</summary>
    public static (string Name, string Address)? Ask(IWin32Window owner, string name, string address)
    {
        using var f = new ConnectForm(name, address);
        if ((owner as Control)?.FindForm() is { } o) f.TopMost = o.TopMost;
        return f.ShowDialog(owner) == DialogResult.OK ? (f.Name_, f.Address) : null;
    }
}
