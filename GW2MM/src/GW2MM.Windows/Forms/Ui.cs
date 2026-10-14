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
            AddPressEffect(p);
        }
        return p;
    }

    /// <summary>
    /// Hover and click feedback for an image button: brighter while the mouse is over it, darker and pushed in
    /// (2 px smaller on each side) while the button is held down.
    /// </summary>
    private static void AddPressEffect(PictureBox p)
    {
        var normal = p.Image!;
        var hover = Tint(normal, scale: 0.85f, add: 0.15f);   // 15 % toward white
        var pressed = Tint(normal, scale: 0.78f, add: 0f);    // 22 % darker
        Rectangle? unpushed = null; // the real bounds while pushed in (read at press time: DPI scaling may have moved it)
        var down = false;
        void Show(Image image, bool pushed)
        {
            p.Image = image;
            if (pushed && unpushed is null)
            {
                unpushed = p.Bounds;
                p.Bounds = Rectangle.Inflate(p.Bounds, -2, -2);
            }
            else if (!pushed && unpushed is { } r)
            {
                p.Bounds = r;
                unpushed = null;
            }
        }
        p.MouseEnter += (_, _) => Show(down ? pressed : hover, down);
        p.MouseLeave += (_, _) => Show(normal, false);
        p.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            down = true;
            Show(pressed, true);
        };
        p.MouseUp += (_, e) =>
        {
            down = false;
            var area = unpushed ?? p.Bounds;
            Show(area.Contains(p.Left + e.X, p.Top + e.Y) ? hover : normal, false);
        };
    }

    /// <summary>A copy of the image with each colour channel mapped to <c>c * scale + add</c> (alpha unchanged).</summary>
    private static Bitmap Tint(Image source, float scale, float add)
    {
        var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { scale, 0f, 0f, 0f, 0f },
            new[] { 0f, scale, 0f, 0f, 0f },
            new[] { 0f, 0f, scale, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { add, add, add, 0f, 1f },
        });
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(matrix);
        using var g = Graphics.FromImage(result);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
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
