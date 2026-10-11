namespace GW2MM.Windows.Forms;

/// <summary>Helpers that place controls at the original AutoHotkey GUI coordinates (96-DPI pixels, scaled by WinForms).</summary>
internal static class Ui
{
    public static readonly Color TextColor = Color.FromArgb(0x1F, 0x46, 0x71);

    public static void InitForm(Form f, string title, int width, int height)
    {
        f.Text = title;
        f.AutoScaleMode = AutoScaleMode.Dpi;
        f.AutoScaleDimensions = new SizeF(96F, 96F);
        f.ClientSize = new Size(width, height);
        f.FormBorderStyle = FormBorderStyle.FixedSingle;
        f.MaximizeBox = false;
        f.MinimizeBox = true;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.Font = new Font("Verdana", 8.25F);
        f.ForeColor = TextColor;
    }

    public static T Add<T>(Control parent, T control, int x, int y, int w, int h) where T : Control
    {
        control.Bounds = new Rectangle(x, y, w, h);
        parent.Controls.Add(control);
        return control;
    }

    public static Label Label(Control parent, string text, int x, int y, int w, int h, ContentAlignment align = ContentAlignment.TopLeft) =>
        Add(parent, new Label { Text = text, TextAlign = align, AutoSize = false }, x, y, w, h);

    public static Button Button(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
    {
        var b = Add(parent, new Button { Text = text, ForeColor = SystemColors.ControlText }, x, y, w, h);
        b.Click += onClick;
        return b;
    }

    public static PictureBox Picture(Control parent, string asset, int x, int y, int w, int h, EventHandler? onClick = null)
    {
        var p = Add(parent, new PictureBox { Image = Assets.Load(asset), SizeMode = PictureBoxSizeMode.StretchImage }, x, y, w, h);
        if (onClick is not null)
        {
            p.Cursor = Cursors.Hand;
            p.Click += onClick;
        }
        return p;
    }

    /// <summary>A small fixed window with one block of text (the original "...Explanation" GUIs).</summary>
    public static void ShowText(IWin32Window? owner, string title, string text, int height = 150)
    {
        var f = new Form();
        InitForm(f, title, 200, height);
        f.ShowInTaskbar = false;
        Label(f, text.Replace("\n", Environment.NewLine), 0, 0, 200, height);
        if (owner is null) f.Show(); else f.Show(owner);
    }
}
