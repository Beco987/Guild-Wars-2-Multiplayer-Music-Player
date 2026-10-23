using System.Diagnostics;
using GW2MM.Core.Updates;
using GW2MM.Windows.Forms;

namespace GW2MM.Windows;

/// <summary>
/// Checks GitHub for a newer release on launch and, if the user agrees, downloads this role's executable, swaps it
/// in place and restarts with the same arguments. Every failure is logged and otherwise silent at launch.
/// </summary>
internal static class AppUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task CheckOnLaunchAsync(Form owner, string exeFileName, string[] args, Action<string> status)
    {
        ReleaseInfo? latest;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            latest = await new UpdateChecker(Http).GetLatestAsync(cts.Token);
        }
        catch (Exception ex)
        {
            AppLog.Write("Update check failed: " + ex.Message);
            return;
        }
        var current = UpdateChecker.CurrentVersion;
        if (latest is null || !UpdateChecker.IsNewer(latest.Version, current) || owner.IsDisposed) return;

        var exePath = Environment.ProcessPath;
        var asset = latest.Asset(exeFileName);
        var canReplace = asset is not null && exePath is not null
                         && string.Equals(Path.GetFileName(exePath), exeFileName, StringComparison.OrdinalIgnoreCase)
                         && IsWritable(Path.GetDirectoryName(exePath)!);

        var question = canReplace
            ? $"GW2MM {latest.Version} is available (you have {current}).\n\nDownload and install it now? GW2MM will restart."
            : $"GW2MM {latest.Version} is available (you have {current}).\n\nOpen the download page?";
        if (Dialogs.Show(owner, question, "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information, heading: "A new version is available",
                yes: "Update", no: "Not now") != DialogResult.Yes) return;

        if (!canReplace)
        {
            OpenPage(owner, latest.PageUrl);
            return;
        }

        try
        {
            status($"Downloading GW2MM {latest.Version}...");
            var download = SelfUpdater.DownloadPath(exePath!);
            await new UpdateChecker(Http).DownloadAsync(asset!, download, CancellationToken.None);
            SelfUpdater.SwapExecutable(exePath!, download);
            AppLog.Write($"Updated {exeFileName} from {current} to {latest.Version}.");
            Process.Start(new ProcessStartInfo(exePath!) { UseShellExecute = false }.WithArguments(args));
            owner.Close();
        }
        catch (Exception ex)
        {
            AppLog.Write("Update failed: " + ex);
            status("Update failed.");
            Dialogs.Show(owner, "The update could not be installed: " + ex.Message + "\n\nYou can download it from the release page instead.", Texts.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            OpenPage(owner, latest.PageUrl);
        }
    }

    private static ProcessStartInfo WithArguments(this ProcessStartInfo psi, string[] args)
    {
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".gw2mm-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void OpenPage(IWin32Window owner, string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Dialogs.Show(owner, ex.Message, Texts.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
