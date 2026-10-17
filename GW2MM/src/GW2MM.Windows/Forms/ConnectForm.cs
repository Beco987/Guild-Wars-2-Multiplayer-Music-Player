namespace GW2MM.Windows.Forms;

/// <summary>Settings → Connect to server: asks for the user name and the server's IP address.</summary>
internal sealed class ConnectForm : Form
{
    private readonly TextBox _name;
    private readonly TextBox _address;

    private ConnectForm(string name, string address)
    {
        Ui.InitForm(this, "Connect to server", 260, 120);
        ShowInTaskbar = false;
        MinimizeBox = false;
        Ui.Label(this, "Name", 10, 13, 70, 20);
        _name = Ui.Add(this, new TextBox { Text = name }, 85, 10, 165, 20);
        Ui.Label(this, "Server IP", 10, 43, 70, 20);
        _address = Ui.Add(this, new TextBox { Text = address }, 85, 40, 165, 20);
        var connect = Ui.Button(this, "Connect", 85, 80, 80, 28, (_, _) =>
        {
            if (Name_.Length == 0 || Address.Length == 0)
            {
                MessageBox.Show(this, "Enter your name and the server's IP address.", Texts.AppTitle);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        });
        var cancel = Ui.Button(this, "Cancel", 170, 80, 80, 28, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        AcceptButton = connect;
        CancelButton = cancel;
    }

    private string Name_ => _name.Text.Replace(",", "").Trim();          // Config.txt is comma-separated
    private string Address => _address.Text.Replace(",", "").Trim();

    /// <summary>Shows the dialog filled with the last name and address; null when cancelled.</summary>
    public static (string Name, string Address)? Ask(IWin32Window owner, string name, string address)
    {
        using var f = new ConnectForm(name, address);
        return f.ShowDialog(owner) == DialogResult.OK ? (f.Name_, f.Address) : null;
    }
}
