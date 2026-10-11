namespace GW2MM.Windows.Forms;

/// <summary>The Controls window: the role's hotkeys as key caps with their actions.</summary>
internal sealed class ControlsForm : Form
{
    public ControlsForm(AppRole role)
    {
        Modern.InitDialog(this, "Controls");
        var root = Modern.Stack(this);
        root.Controls.Add(Modern.Heading("Controls"));
        root.Controls.Add(Modern.Caption("These keys work anywhere, even while Guild Wars 2 has focus.", 380));

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Modern.Surface, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var text = role == AppRole.Host ? Texts.HostControls : Texts.ClientControls;
        foreach (var line in text.Split('\n'))
        {
            var sep = line.IndexOf(" - ", StringComparison.Ordinal);
            if (sep < 0) continue;
            var keys = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 14, 0) };
            foreach (var k in line[..sep].Split('+')) keys.Controls.Add(Modern.KeyCap(k));
            table.Controls.Add(keys);
            table.Controls.Add(new Label { Text = line[(sep + 3)..], AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) });
        }
        root.Controls.Add(table);

        var close = Modern.Primary("Close");
        close.Click += (_, _) => Close();
        root.Controls.Add(Modern.ButtonRow(close));
        AcceptButton = close;
        CancelButton = close;
    }
}
