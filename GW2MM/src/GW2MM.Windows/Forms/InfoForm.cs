namespace GW2MM.Windows.Forms;

internal sealed class InfoForm : Form
{
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
        Ui.Label(this, Texts.Version, 0, 200, 200, 20, ContentAlignment.TopCenter);
    }
}
