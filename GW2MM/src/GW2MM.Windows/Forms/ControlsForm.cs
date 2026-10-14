namespace GW2MM.Windows.Forms;

/// <summary>The Controls window: the current hotkeys as key caps with their actions, and a way to change them.</summary>
internal sealed class ControlsForm : Form
{
    public ControlsForm(MainForm main)
    {
        Modern.InitDialog(this, "Controls");
        var root = Modern.Stack(this);
        root.Controls.Add(Modern.Heading("Controls"));
        root.Controls.Add(Modern.Caption("These keys work anywhere, even while Guild Wars 2 has focus.", 380));

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, BackColor = Modern.Surface, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.Controls.Add(table);

        void Fill()
        {
            table.SuspendLayout();
            table.Controls.Clear();
            foreach (var (action, keys) in main.CurrentBindings())
            {
                var caps = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 14, 0) };
                if (keys.IsNone) caps.Controls.Add(new Label { Text = "not set", AutoSize = true, ForeColor = Modern.Muted, Margin = new Padding(0, 6, 0, 6) });
                else foreach (var k in keys.ToString().Split('+')) caps.Controls.Add(Modern.KeyCap(k));
                table.Controls.Add(caps);
                table.Controls.Add(new Label { Text = action.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) });
            }
            table.ResumeLayout();
        }
        Fill();

        var close = Modern.Primary("Close");
        close.Click += (_, _) => Close();
        var change = Modern.Secondary("Change keybinds…");
        change.Click += (_, _) => { main.ShowKeybinds(this); Fill(); };
        root.Controls.Add(Modern.ButtonRow(close, change));
        AcceptButton = close;
        CancelButton = close;
    }
}
