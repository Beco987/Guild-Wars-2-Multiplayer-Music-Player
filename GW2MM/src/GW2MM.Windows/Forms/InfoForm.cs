namespace GW2MM.Windows.Forms;

internal sealed class InfoForm : Form
{
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

    public InfoForm()
    {
        Ui.InitForm(this, "Info", 200, 220);
        ShowInTaskbar = false;
        const string text = "Please check out this doc for guidance on using this application.";
        var link = Ui.Add(this, new LinkLabel { Text = text }, 0, 0, 200, 40);
        link.Links.Add(text.IndexOf("doc", StringComparison.Ordinal), 3, Texts.GuideUrl);
        link.LinkClicked += (_, e) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo((string)e.Link!.LinkData!) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Texts.AppTitle); }
        };
        Ui.Picture(this, "logo.png", 20, 30, 160, 160);
        Ui.Label(this, "Version " + AppVersion, 0, 200, 200, 20, ContentAlignment.TopCenter);
    }
}
