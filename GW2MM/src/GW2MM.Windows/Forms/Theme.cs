using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace GW2MM.Windows.Forms;

/// <summary>
/// The Janthir Wilds look shared by every window: frost surfaces, the navy of the banner lettering, icy azure and
/// lavender accents, Segoe UI, and helpers for painting rounded, DPI-scaled shapes. Layout sizes are written in
/// 96-DPI pixels and passed through <see cref="S"/>, so windows scale the same way on Windows and under Wine.
/// </summary>
internal static class Theme
{
    // Surfaces
    public static Color Frost { get; private set; } = Hex(0xEDF1F7);        // window background
    public static Color Surface { get; private set; } = Color.White;         // cards
    public static Color Raised { get; private set; } = Color.White;          // secondary buttons, selected segment
    public static Color RaisedHover { get; private set; } = Hex(0xEEF5FE);
    public static Color Inset { get; private set; } = Hex(0xF4F7FB);        // search field, chat log
    public static Color Stripe { get; private set; } = Hex(0xF8FAFD);       // alternating grid rows
    public static Color Track { get; private set; } = Hex(0xE1E7F0);        // segmented track, toggle off
    public static Color Border { get; private set; } = Hex(0xD5DDE8);
    public static Color BorderStrong { get; private set; } = Hex(0xB7C3D5);
    public static Color Divider { get; private set; } = Hex(0xE5EAF1);
    public static Color Underline { get; private set; } = Hex(0x808CA3);    // input bottom edge, unchecked box (3:1 on white)
    public static Color StatusBack { get; private set; } = Hex(0xE4E9F1);
    public static Color FooterBack { get; private set; } = Hex(0xF5F7FB);

    // Text
    public static Color Ink { get; private set; } = Hex(0x1A2640);
    public static Color Ink2 { get; private set; } = Hex(0x43506B);
    public static Color Muted { get; private set; } = Hex(0x5A6680);
    public static Color Placeholder { get; private set; } = Hex(0x667187);
    public static Color Disabled { get; private set; } = Hex(0x9AA5B8);
    public static Color Title { get; private set; } = Hex(0x1E3356);        // banner lettering navy

    // Brand
    public static Color Navy { get; private set; } = Hex(0x1F4671);
    public static Color NavyHover { get; private set; } = Hex(0x285A8F);
    public static Color NavyPressed { get; private set; } = Hex(0x183A5E);
    public static Color NavyDeep { get; private set; } = Hex(0x14243F);
    public static Color NavyLight { get; private set; } = Hex(0x3D85CB);    // top of the Play gradient
    public static Color Azure { get; private set; } = Hex(0x3A8FD9);        // focus ring, selection bar, toggles
    public static Color AzureText { get; private set; } = Hex(0x1F6AB3);
    public static Color Ice { get; private set; } = Hex(0xDDEBFB);          // selected row, tonal button
    public static Color IceHover { get; private set; } = Hex(0xEEF5FE);
    public static Color IcePressed { get; private set; } = Hex(0xCFE2F8);
    public static Color Lavender { get; private set; } = Hex(0x7D73C9);
    public static Color LavenderTint { get; private set; } = Hex(0xECE9FA);
    public static Color LavenderText { get; private set; } = Hex(0x4D4398);

    // Meaning
    public static Color Success { get; private set; } = Hex(0x2E9E5B);
    public static Color Warning { get; private set; } = Hex(0xD08A16);
    public static Color WarningText { get; private set; } = Hex(0x8A5A00);
    public static Color WarningTint { get; private set; } = Hex(0xFFF3DE);
    public static Color Danger { get; private set; } = Hex(0xC23B2E);
    public static Color DangerHover { get; private set; } = Hex(0xA83226);
    public static Color DangerPressed { get; private set; } = Hex(0x8E2A20);
    public static Color DangerText { get; private set; } = Hex(0xB03A2E);
    public static Color DangerTint { get; private set; } = Hex(0xFCEBE9);
    public static Color Offline { get; private set; } = Hex(0xA3AEC0);

    // Roles the dusk theme colours differently from the plain navy / white of the light theme.
    public static Color AccentInk { get; private set; } = Hex(0x1F4671);     // accent text and glyphs (sorted header, chat names)
    public static Color TooltipBack { get; private set; } = Hex(0x14243F);
    public static Color ShadowTint { get; private set; } = Hex(0x14243F);
    public static Color BannerBack { get; private set; } = Color.White;      // behind the banner art and the dialog header band

    /// <summary>True when the dusk (dark) theme is in use. Chosen once at start-up (Settings → Dusk theme).</summary>
    public static bool Dusk { get; private set; }

    /// <summary>
    /// Switches to the dusk theme: the Janthir Wilds banner at nightfall, deep navy surfaces, frost-white text, icy
    /// azure accents. Call before any window is created; windows read these colours as they are built.
    /// </summary>
    public static void UseDusk()
    {
        Dusk = true;
        Frost = Hex(0x0C1628);
        Surface = Hex(0x13213A);
        Raised = Hex(0x1C2F4E);
        RaisedHover = Hex(0x243C62);
        Inset = Hex(0x0A1426);
        Stripe = Hex(0x16263F);
        Track = Hex(0x0A1426);
        Border = Hex(0x243A5E);
        BorderStrong = Hex(0x3D5E8E);
        Divider = Hex(0x1C2E4C);
        Underline = Hex(0x4A6694);
        StatusBack = Hex(0x070E1A);
        FooterBack = Hex(0x091222);
        Ink = Hex(0xE8F0FA);
        Ink2 = Hex(0xA7B9D3);
        Muted = Hex(0x8094B3);
        Placeholder = Hex(0x8094B3);
        Disabled = Hex(0x4F6280);
        Title = Hex(0xE8F0FA);
        Navy = Hex(0x1E75C0);          // primary fills: azure at dusk
        NavyHover = Hex(0x2585D2);
        NavyPressed = Hex(0x175F9F);
        NavyDeep = Hex(0x070E1A);
        NavyLight = Hex(0x2B8BD8);
        Azure = Hex(0x5CC8FF);
        AzureText = Hex(0x7FD3FF);
        Ice = Hex(0x1C4170);           // selection
        IceHover = Hex(0x1B2D4A);
        IcePressed = Hex(0x234C80);
        Lavender = Hex(0xB9A8F5);
        LavenderTint = Hex(0x2A2856);
        LavenderText = Hex(0xB9A8F5);
        Success = Hex(0x4CD69B);
        Warning = Hex(0xF2C46B);
        WarningText = Hex(0xF2C46B);
        WarningTint = Hex(0x3A2F14);
        Danger = Hex(0xC9425A);
        DangerHover = Hex(0xD04A63);
        DangerPressed = Hex(0xA93049);
        DangerText = Hex(0xFF8597);
        DangerTint = Hex(0x3A1B24);
        Offline = Hex(0x4F6280);
        AccentInk = Hex(0x7FD3FF);
        TooltipBack = Hex(0x1F3354);
        ShadowTint = Color.Black;
        BannerBack = Hex(0x0E1830);
    }

    /// <summary>The banner art for this theme ("Title.png" / "TitleClient.png", or their dusk versions).</summary>
    public static string Art(string lightName) => Dusk ? lightName.Replace(".png", "Dusk.png").Replace("HeaderFrostDusk", "HeaderDusk") : lightName;

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color Alpha(Color c, int alpha) => Color.FromArgb(alpha, c);

    // ------------------------------------------------------------------ fonts (created once; never dispose these)

    public static readonly Font Body = Make("Segoe UI", 9F);
    public static readonly Font BodyBold = Semibold(9F);
    public static readonly Font Small = Make("Segoe UI", 8.25F);
    public static readonly Font SmallBold = Semibold(8.25F);
    public static readonly Font Overline = Semibold(7.5F);
    public static readonly Font Tiny = Make("Segoe UI", 7.5F);
    public static readonly Font SongTitle = Semibold(11F);
    public static readonly Font Heading = Semibold(12F);

    private static Font Make(string family, float size, FontStyle style = FontStyle.Regular) => new(family, size, style, GraphicsUnit.Point);

    /// <summary>"Segoe UI Semibold" where it exists (every Windows since Vista), else bold Segoe UI.</summary>
    private static Font Semibold(float size)
    {
        var f = Make("Segoe UI Semibold", size);
        if (f.Name == "Segoe UI Semibold") return f;
        f.Dispose();
        return Make("Segoe UI", size, FontStyle.Bold);
    }

    // ------------------------------------------------------------------ DPI

    /// <summary>Device pixels per 96-DPI pixel (the app is system-DPI aware, so this never changes while it runs).</summary>
    public static readonly float Scale = ReadScale();

    private static float ReadScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch (Exception) { return 1f; }
    }

    /// <summary>A 96-DPI length in device pixels.</summary>
    public static int S(float px) => (int)Math.Round(px * Scale);

    public static float Sf(float px) => px * Scale;

    public static Size S(float w, float h) => new(S(w), S(h));

    public static Rectangle S(float x, float y, float w, float h) => new(S(x), S(y), S(w), S(h));

    public static Padding P(float all) => new(S(all));

    public static Padding P(float left, float top, float right, float bottom) => new(S(left), S(top), S(right), S(bottom));

    // ------------------------------------------------------------------ painting

    /// <summary>Anti-aliased shapes with half-pixel offsets, so 1 px lines stay crisp.</summary>
    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
    {
        using var path = Round(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void FillRound(Graphics g, RectangleF r, float radius, Color top, Color bottom)
    {
        using var path = Round(r, radius);
        using var brush = new LinearGradientBrush(new RectangleF(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
    }

    /// <summary>A rounded outline drawn inside <paramref name="r"/>.</summary>
    public static void DrawRound(Graphics g, RectangleF r, float radius, Color color, float width = 1f)
    {
        var w = width * Scale;
        var inner = RectangleF.Inflate(r, -w / 2, -w / 2);
        using var path = Round(inner, Math.Max(0, radius - w / 2));
        using var pen = new Pen(color, w);
        g.DrawPath(pen, path);
    }

    /// <summary>A soft drop shadow under a rounded rectangle (stacked translucent outlines, so no bitmaps are needed).</summary>
    public static void Shadow(Graphics g, RectangleF r, float radius, float blur = 4, float dy = 1, int alpha = 40, Color? tint = null)
    {
        var color = tint ?? ShadowTint;
        var steps = Math.Max(1, (int)Math.Round(blur * Scale));
        var shifted = r;
        shifted.Offset(0, dy * Scale);
        // Rings stack, so the shade fades linearly from `alpha` at the shape's edge to nothing `blur` pixels out.
        var a = Math.Max(1, alpha / steps);
        for (var i = steps; i >= 1; i--)
            FillRound(g, RectangleF.Inflate(shifted, i - 1, i - 1), radius + i - 1, Color.FromArgb(a, color));
    }

    /// <summary>The keyboard focus ring: 2 px azure, 2 px outside the control's shape.</summary>
    public static void FocusRing(Graphics g, RectangleF r, float radius)
    {
        var o = Sf(2);
        DrawRound(g, RectangleF.Inflate(r, o, o), radius + o, Alpha(Azure, 200), 2f);
    }

    public const TextFormatFlags TextLeft = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                                            | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
    public const TextFormatFlags TextCenter = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                                              | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
    public const TextFormatFlags TextRight = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
                                             | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    public static void Text(Graphics g, string text, Font font, Rectangle r, Color color, TextFormatFlags flags = TextLeft) =>
        TextRenderer.DrawText(g, text, font, r, color, flags);

    public static Size Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

    /// <summary>Paints the area of <paramref name="c"/> the way its parent looks behind it (for rounded corners).</summary>
    public static void PaintParent(Control c, Graphics g) => g.Clear(c.Parent?.BackColor ?? Frost);

    /// <summary>Uses the light Explorer scrollbars and list visuals instead of the classic ones.</summary>
    public static void ExplorerTheme(Control c)
    {
        void Apply()
        {
            // Dark scrollbars and list visuals at dusk (Windows 10 1809 and later; older versions ignore the name).
            try { _ = SetWindowTheme(c.Handle, Dusk ? "DarkMode_Explorer" : "Explorer", null); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        if (c.IsHandleCreated) Apply();
        c.HandleCreated += (_, _) => Apply();
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    // ------------------------------------------------------------------ windows

    private static Icon? _appIcon;

    /// <summary>The app icon (the Janthir medallion), shared by every window.</summary>
    public static Icon? AppIcon
    {
        get
        {
            if (_appIcon is not null) return _appIcon;
            try
            {
                using var s = typeof(Theme).Assembly.GetManifestResourceStream("GW2MM.Assets.AppIcon.ico");
                if (s is not null) _appIcon = new Icon(s);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException) { }
            return _appIcon;
        }
    }

    /// <summary>Common window set-up: icon, font, frost background, manual DPI scaling and a frost caption on Windows 11.</summary>
    public static void InitWindow(Form f, string title)
    {
        f.Text = title;
        f.AutoScaleMode = AutoScaleMode.None;
        f.Font = Body;
        f.BackColor = Frost;
        f.ForeColor = Ink;
        if (AppIcon is { } icon) f.Icon = icon;
        f.HandleCreated += (_, _) => TintCaption(f.Handle);
    }

    /// <summary>Windows 11 lets apps colour the title bar: frost background, navy text. Older Windows ignores this.</summary>
    public static void TintCaption(IntPtr hwnd)
    {
        if (Dusk && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            // A dark title bar on Windows 10 1809+ (attribute 20; 19 before build 18985).
            try
            {
                var on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) _ = DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        try
        {
            var caption = ColorRef(Dusk ? StatusBack : Frost);
            var text = ColorRef(Dusk ? Hex(0xCFDCEE) : Title);
            var border = ColorRef(Dusk ? Hex(0x2A4066) : BorderStrong);
            _ = DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));  // DWMWA_CAPTION_COLOR
            _ = DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));     // DWMWA_TEXT_COLOR
            _ = DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));   // DWMWA_BORDER_COLOR
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    /// <summary>Rounded corners for menus and tooltips on Windows 11.</summary>
    public static void RoundCorners(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        try
        {
            var small = 3; // DWMWCP_ROUNDSMALL
            _ = DwmSetWindowAttribute(hwnd, 33, ref small, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Centres a window over its owner (or the screen) and keeps it on the working area.</summary>
    public static void CenterOnOwner(Form f)
    {
        var area = Screen.FromControl(f.Owner ?? f).WorkingArea;
        var anchor = f.Owner is { } o ? o.Bounds : area;
        var x = anchor.Left + (anchor.Width - f.Width) / 2;
        var y = anchor.Top + (anchor.Height - f.Height) / 2;
        f.Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - f.Width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - f.Height)));
    }
}
