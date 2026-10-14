namespace GW2MM.Windows.Forms;

/// <summary>Shared look for the redesigned dialogs: Segoe UI, a light background, flat buttons and muted captions.</summary>
internal static class Modern
{
    public static readonly Color Accent = Color.FromArgb(0x1F, 0x46, 0x71);
    public static readonly Color AccentHover = Color.FromArgb(0x2A, 0x5C, 0x93);
    public static readonly Color Background = Color.FromArgb(0xF4, 0xF6, 0xF9);
    public static readonly Color Surface = Color.White;
    public static readonly Color Border = Color.FromArgb(0xD5, 0xDB, 0xE3);
    public static readonly Color Muted = Color.FromArgb(0x5F, 0x6B, 0x7A);
    public static readonly Color Stripe = Color.FromArgb(0xEE, 0xF2, 0xF7);
    public static readonly Color Online = Color.FromArgb(0x2E, 0x9E, 0x5B);
    public static readonly Color Offline = Color.FromArgb(0xB0, 0xB8, 0xC2);

    public static Font Body => new("Segoe UI", 9F);
    public static Font Bold => new("Segoe UI Semibold", 9F);
    public static Font Small => new("Segoe UI", 8.25F);
    public static Font Title => new("Segoe UI Semibold", 12F);

    public static void InitDialog(Form f, string title)
    {
        f.Text = title;
        f.Font = Body;
        f.AutoScaleMode = AutoScaleMode.Font;
        f.BackColor = Background;
        f.ForeColor = Color.FromArgb(0x1C, 0x24, 0x30);
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.ShowInTaskbar = false;
        f.AutoSize = true;
        f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        f.Padding = new Padding(18, 14, 18, 14);
        f.StartPosition = FormStartPosition.Manual;
        f.Load += (_, _) => CenterOnOwner(f);
    }

    /// <summary>Centres a modeless window over its owner the first time it opens (CenterParent only works for modal dialogs).</summary>
    public static void CenterOnOwner(Form f)
    {
        var area = Screen.FromControl(f.Owner ?? f).WorkingArea;
        var anchor = f.Owner is { } o ? o.Bounds : area;
        var x = anchor.Left + (anchor.Width - f.Width) / 2;
        var y = anchor.Top + (anchor.Height - f.Height) / 2;
        f.Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - f.Width)), Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - f.Height)));
    }

    /// <summary>A single-column layout that stacks its children and sizes the dialog to fit.</summary>
    public static TableLayoutPanel Stack(Form f)
    {
        var t = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        f.Controls.Add(t);
        return t;
    }

    public static Label Heading(string text) =>
        new() { Text = text, Font = Title, ForeColor = Accent, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };

    public static Label Caption(string text, int maxWidth = 460) =>
        new() { Text = text, Font = Small, ForeColor = Muted, AutoSize = true, MaximumSize = new Size(maxWidth, 0), Margin = new Padding(0, 0, 0, 10) };

    public static Label FieldLabel(string text) =>
        new() { Text = text, Font = Bold, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(0, 6, 14, 2) };

    public static Button Primary(string text) => Style(new Button { Text = text }, primary: true);
    public static Button Secondary(string text) => Style(new Button { Text = text }, primary: false);

    private static Button Style(Button b, bool primary)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.AutoSize = true;
        b.MinimumSize = new Size(92, 32);
        b.Padding = new Padding(10, 2, 10, 2);
        b.Margin = new Padding(8, 0, 0, 0);
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
        b.BackColor = primary ? Accent : Surface;
        b.ForeColor = primary ? Color.White : Accent;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Stripe;
        if (primary) b.Font = Bold;
        return b;
    }

    /// <summary>Right-aligned row of dialog buttons.</summary>
    public static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0), WrapContents = false,
        };
        foreach (var b in buttons) p.Controls.Add(b);
        return p;
    }

    /// <summary>A keyboard key drawn as a key cap.</summary>
    public static Label KeyCap(string key) => new()
    {
        Text = key, Font = Bold, AutoSize = true, BackColor = Surface, ForeColor = Accent, BorderStyle = BorderStyle.FixedSingle,
        Padding = new Padding(6, 2, 6, 2), Margin = new Padding(0, 3, 4, 3),
    };
}
