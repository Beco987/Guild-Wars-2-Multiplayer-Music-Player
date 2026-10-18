namespace GW2MM.Windows.Forms;

/// <summary>
/// The shell shared by the dialogs: an optional header band (title, short caption and the Janthir pine-and-sky art
/// fading in on the right), a frost body that subclasses fill with cards and fields, and a footer band with the
/// buttons right-aligned (the first button added is the right-most, normally the primary one).
/// </summary>
internal class ThemedDialog : Form
{
    private static Image? _headerArt;
    private readonly string? _heading, _caption;
    private readonly List<Control> _footerRight = new();
    private Control? _footerLeft;

    public ThemedDialog(string title, int width96, string? heading = null, string? caption = null)
    {
        Theme.InitWindow(this, title);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Load += (_, _) => Theme.CenterOnOwner(this);
        _heading = heading;
        _caption = caption;
        HeaderHeight = heading is null ? 0 : Theme.S(caption is null ? 54 : 66);
        Body = new DialogBody { BackColor = Theme.Frost, Location = new Point(0, HeaderHeight), Width = Theme.S(width96) };
        Footer = new DialogFooter { BackColor = Theme.FooterBack, Height = Theme.S(60), Width = Theme.S(width96) };
        Controls.Add(Body);
        Controls.Add(Footer);
        ClientSize = new Size(Theme.S(width96), HeaderHeight + Theme.S(100) + Footer.Height);
    }

    public int HeaderHeight { get; }
    public Panel Body { get; }
    public Panel Footer { get; }

    /// <summary>Adds a footer button; the first one added sits at the right.</summary>
    public ThemedButton AddButton(string text, ButtonKind kind, DialogResult result = DialogResult.None, Glyph glyph = Glyph.None)
    {
        var b = new ThemedButton(text, kind, glyph) { DialogResult = result, MinimumWidth = Theme.S(84) };
        b.Size = b.GetPreferredSize(Size.Empty);
        Footer.Controls.Add(b);
        _footerRight.Add(b);
        UpdateSize();
        return b;
    }

    /// <summary>A control at the left of the footer (Defaults, a count, a tip).</summary>
    public void SetFooterLeft(Control c)
    {
        _footerLeft = c;
        Footer.Controls.Add(c);
        UpdateSize();
    }

    /// <summary>Sets the body height (96-DPI px) and sizes the window around header, body and footer.</summary>
    public void SetBodyHeight(int height96)
    {
        Body.Height = Theme.S(height96);
        UpdateSize();
    }

    // (Footer.Visible reads false until the window is shown, so whether there is a footer is worked out here.)
    private bool HasFooter => _footerRight.Count > 0 || _footerLeft is not null;

    /// <summary>Sizes the window around header, body and footer (buttons may be added before or after the body height is set).</summary>
    private void UpdateSize()
    {
        Footer.Location = new Point(0, HeaderHeight + Body.Height);
        Footer.Visible = HasFooter;
        if (FormBorderStyle != FormBorderStyle.Sizable)
            ClientSize = new Size(Body.Width, HeaderHeight + Body.Height + (HasFooter ? Footer.Height : 0));
        LayoutFooter();
    }

    /// <summary>For resizable dialogs: the body takes the space between header and footer.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Body is null || Footer is null || FormBorderStyle != FormBorderStyle.Sizable) return;
        var footer = HasFooter ? Footer.Height : 0;
        Body.Bounds = new Rectangle(0, HeaderHeight, ClientSize.Width, Math.Max(0, ClientSize.Height - HeaderHeight - footer));
        Footer.Bounds = new Rectangle(0, ClientSize.Height - footer, ClientSize.Width, Footer.Height);
        LayoutFooter();
        Invalidate();
    }

    private void LayoutFooter()
    {
        var x = Footer.Width - Theme.S(16);
        foreach (var b in _footerRight)
        {
            x -= b.Width;
            b.Location = new Point(x, (Footer.Height - b.Height) / 2);
            x -= Theme.S(8);
        }
        if (_footerLeft is { } left) left.Location = new Point(Theme.S(16), (Footer.Height - left.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_heading is null) return;
        var g = e.Graphics;
        var band = new Rectangle(0, 0, ClientSize.Width, HeaderHeight);
        using (var back = new SolidBrush(Theme.BannerBack)) g.FillRectangle(back, band);
        _headerArt ??= LoadArt();
        if (_headerArt is { } art)
        {
            var w = art.Width * (float)HeaderHeight / art.Height;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(art, new RectangleF(band.Right - w, 0, w, HeaderHeight));
        }
        var textTop = _caption is null ? (HeaderHeight - Theme.S(22)) / 2 : Theme.S(13);
        Theme.Text(g, _heading, Theme.Heading, new Rectangle(Theme.S(20), textTop, band.Width - Theme.S(40), Theme.S(22)), Theme.Title);
        if (_caption is not null)
            Theme.Text(g, _caption, Theme.Small, new Rectangle(Theme.S(20), textTop + Theme.S(24), band.Width - Theme.S(40), Theme.S(16)), Theme.Muted);
        using var line = new SolidBrush(Theme.Divider);
        g.FillRectangle(line, 0, HeaderHeight - Math.Max(1, Theme.S(1)), band.Width, Math.Max(1, Theme.S(1)));
    }

    private static Image? LoadArt()
    {
        try { return Assets.Load(Theme.Art("HeaderFrost.png")); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>The body: paints the shadows under its cards.</summary>
    private sealed class DialogBody : Panel
    {
        public DialogBody()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Card.PaintShadows(this, e.Graphics);
        }
    }

    private sealed class DialogFooter : Panel
    {
        public DialogFooter()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var line = new SolidBrush(Theme.Border);
            e.Graphics.FillRectangle(line, 0, 0, Width, Math.Max(1, Theme.S(1)));
        }
    }
}

/// <summary>Themed replacements for <see cref="MessageBox"/>: same modality and owner, same results.</summary>
internal static class Dialogs
{
    /// <summary>
    /// Shows a message. <paramref name="yes"/> / <paramref name="no"/> relabel the Yes and No buttons (e.g. "Delete",
    /// "Cancel"); <paramref name="danger"/> makes the Yes button red and gives the No button the initial focus.
    /// </summary>
    /// <param name="urgent">
    /// For prompts the user must see even while playing (Sync): the box stays on top, takes the foreground from the
    /// game, flashes and plays the system sound. If the owner is minimized it is shown on its own (an owned window is
    /// hidden with its minimized owner).
    /// </param>
    public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None, string? heading = null, string? yes = null, string? no = null, bool danger = false,
        bool urgent = false)
    {
        using var dlg = new MessageDialog(text, caption, buttons, icon, heading, yes, no, danger);
        var ownerForm = (owner as Control)?.FindForm();
        if (urgent)
        {
            dlg.TopMost = true;
            dlg.Shown += (_, _) =>
            {
                System.Media.SystemSounds.Asterisk.Play();
                Interop.GameWindow.BringToFront(dlg.Handle);
                var flash = new Interop.NativeMethods.FLASHWINFO
                {
                    cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Interop.NativeMethods.FLASHWINFO>(), hwnd = dlg.Handle,
                    dwFlags = Interop.NativeMethods.FLASHW_ALL | Interop.NativeMethods.FLASHW_TIMERNOFG,
                };
                Interop.NativeMethods.FlashWindowEx(ref flash);
            };
            if (ownerForm is { WindowState: FormWindowState.Minimized } or { Visible: false }) ownerForm = null;
        }
        if (ownerForm is not null && !ownerForm.IsDisposed)
        {
            dlg.TopMost |= ownerForm.TopMost;
            return dlg.ShowDialog(ownerForm);
        }
        dlg.ShowInTaskbar = true;
        dlg.StartPosition = FormStartPosition.CenterScreen;
        return dlg.ShowDialog();
    }

    /// <summary>Asks to confirm something destructive. True when the user chose <paramref name="action"/>.</summary>
    public static bool Confirm(IWin32Window owner, string heading, string text, string action, string caption = "Confirm") =>
        Show(owner, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, heading, action, "Cancel", danger: true) == DialogResult.Yes;

    private sealed class MessageDialog : ThemedDialog
    {
        private readonly MessageBoxIcon _icon;

        public MessageDialog(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, string? heading, string? yes, string? no, bool danger)
            : base(caption, 420)
        {
            _icon = icon;
            Body.BackColor = Theme.Surface;
            Body.Paint += PaintBadge;
            var left = icon == MessageBoxIcon.None ? 24 : 72;
            var width = 420 - left - 24;
            var y = 22;
            if (heading is not null)
            {
                var h = new Label
                {
                    Text = heading, Font = Theme.Heading, ForeColor = Theme.Title, AutoSize = true, MaximumSize = new Size(Theme.S(width), 0),
                    Location = Theme.S(left, y, 0, 0).Location, UseMnemonic = false,
                };
                Body.Controls.Add(h);
                y += (int)Math.Ceiling(h.GetPreferredSize(new Size(Theme.S(width), 0)).Height / Theme.Scale) + 6;
            }
            var label = new Label
            {
                Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine), Font = Theme.Body, ForeColor = heading is null ? Theme.Ink : Theme.Ink2,
                AutoSize = true, MaximumSize = new Size(Theme.S(width), 0), Location = Theme.S(left, y, 0, 0).Location, UseMnemonic = false,
            };
            Body.Controls.Add(label);
            var textBottom = y + (int)Math.Ceiling(label.GetPreferredSize(new Size(Theme.S(width), 0)).Height / Theme.Scale);
            SetBodyHeight(Math.Max(textBottom, icon == MessageBoxIcon.None ? 0 : 58) + 22);

            if (buttons is MessageBoxButtons.YesNo or MessageBoxButtons.OKCancel or MessageBoxButtons.YesNoCancel)
            {
                var okResult = buttons == MessageBoxButtons.OKCancel ? DialogResult.OK : DialogResult.Yes;
                var noResult = buttons == MessageBoxButtons.OKCancel ? DialogResult.Cancel : DialogResult.No;
                var primary = AddButton(yes ?? (okResult == DialogResult.OK ? "OK" : "Yes"), danger ? ButtonKind.Danger : ButtonKind.Primary, okResult);
                var secondary = AddButton(no ?? (noResult == DialogResult.Cancel ? "Cancel" : "No"), ButtonKind.Secondary, noResult);
                AcceptButton = primary;
                CancelButton = secondary;
                if (danger) ActiveControl = secondary;
                else ActiveControl = primary;
            }
            else
            {
                var ok = AddButton("OK", ButtonKind.Primary, DialogResult.OK);
                AcceptButton = ok;
                CancelButton = ok;
                ActiveControl = ok;
            }
        }

        private void PaintBadge(object? sender, PaintEventArgs e)
        {
            if (_icon == MessageBoxIcon.None) return;
            var (glyph, fore, back) = _icon switch
            {
                MessageBoxIcon.Warning => (Glyph.Warning, Theme.Warning, Theme.WarningTint),
                MessageBoxIcon.Error => (Glyph.Warning, Theme.Danger, Theme.DangerTint),
                MessageBoxIcon.Question => (Glyph.Question, Theme.AzureText, Theme.Ice),
                _ => (Glyph.Info, Theme.AzureText, Theme.Ice),
            };
            var g = e.Graphics;
            Theme.Smooth(g);
            var badge = new RectangleF(Theme.Sf(24), Theme.Sf(20), Theme.Sf(36), Theme.Sf(36));
            Theme.FillRound(g, badge, badge.Width / 2, back);
            var s = Theme.Sf(20);
            Glyphs.Draw(g, glyph, new RectangleF(badge.X + (badge.Width - s) / 2, badge.Y + (badge.Height - s) / 2, s, s), fore, 1.7f);
        }
    }
}
