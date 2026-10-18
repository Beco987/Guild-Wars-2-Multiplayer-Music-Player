namespace GW2MM.Windows.Forms;

/// <summary>About: the medallion, which program this is, its version and a link to the user guide.</summary>
internal sealed class InfoForm : ThemedDialog
{
    private readonly Image _logo = Assets.Load("logo.png");

    /// <summary>This build's version as CI numbered it (e.g. "2.0.6", or "2.0.6-pr.3" for a pull request build).</summary>
    public static string AppVersion
    {
        get
        {
            var assembly = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(InfoForm).Assembly;
            var informational = System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion;
            return (informational ?? Core.Updates.UpdateChecker.CurrentVersion.ToString()).Split('+')[0];
        }
    }

    public InfoForm(AppRole role) : base("About GW2MM", 320)
    {
        Body.BackColor = Theme.Surface;
        Body.Paint += (_, e) =>
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var r = new RectangleF((Body.Width - Theme.Sf(96)) / 2, Theme.Sf(22), Theme.Sf(96), Theme.Sf(96));
            Theme.Shadow(g, r, Theme.Sf(18), blur: 5, dy: 2, alpha: 60);
            using var clip = Theme.Round(r, Theme.Sf(18));
            var state = g.Save();
            g.SetClip(clip);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_logo, r);
            g.Restore(state);
        };
        var w = 320;
        Body.Controls.Add(Centered(new Label { Text = "GW2MM Multi Manager", Font = Theme.Heading, ForeColor = Theme.Title }, 132));
        Body.Controls.Add(Centered(new Label
        {
            Text = role == AppRole.Host ? "Server · plays songs together in Guild Wars 2" : "Client · plays songs together in Guild Wars 2",
            Font = Theme.Small, ForeColor = Theme.Muted,
        }, 158));
        Body.Controls.Add(Centered(new Label { Text = "Version " + AppVersion, Font = Theme.SmallBold, ForeColor = Theme.Navy, BackColor = Theme.Ice, Padding = Theme.P(8, 2, 8, 2) }, 180));
        var link = new LinkLabel
        {
            Text = "Open the user guide", Font = Theme.BodyBold, LinkColor = Theme.AzureText, ActiveLinkColor = Theme.Navy, VisitedLinkColor = Theme.AzureText,
            LinkBehavior = LinkBehavior.HoverUnderline,
        };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Texts.GuideUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Dialogs.Show(this, ex.Message, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        Body.Controls.Add(Centered(link, 210));
        SetBodyHeight(244);
        var close = AddButton("Close", ButtonKind.Secondary);
        close.Click += (_, _) => Close();
        CancelButton = close;
        ActiveControl = close;

        Control Centered(Control c, int y)
        {
            c.AutoSize = true;
            c.BackColor = c.BackColor == SystemColors.Control ? Theme.Surface : c.BackColor;
            var size = c.GetPreferredSize(Size.Empty);
            c.Location = new Point((Theme.S(w) - size.Width) / 2, Theme.S(y));
            return c;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logo.Dispose();
        base.Dispose(disposing);
    }
}
