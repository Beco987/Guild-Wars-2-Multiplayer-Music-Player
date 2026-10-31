using System.Diagnostics;

namespace GW2MM.Core.Net;

public enum FirewallStatus
{
    /// <summary>The firewall is off for the current network, or a rule allows this program / port.</summary>
    Allowed,
    /// <summary>A rule blocks this program (Windows adds one when its "allow access" prompt is cancelled).</summary>
    Blocked,
    /// <summary>The firewall is on for the current network and no rule allows this program or port.</summary>
    NotAllowed,
    /// <summary>The firewall settings could not be read.</summary>
    Unknown,
}

/// <summary>
/// Checks whether Windows Firewall lets other computers connect to the host's port on the networks this computer is
/// on now, and adds an allow rule (with administrator permission) when it does not. UPnP only opens the router;
/// Windows Firewall is a separate gate. Its "allow access" prompt ticks Private networks only by default, so on a
/// network Windows classes as Public the host silently receives nothing. Windows only; elsewhere the status is
/// Unknown.
/// </summary>
public static class FirewallCheck
{
    private const int NetFwRuleDirIn = 1;
    private const int NetFwActionBlock = 0;
    private const int NetFwActionAllow = 1;
    private const int ProtocolTcp = 6;
    private const int ProtocolAny = 256;
    public const string RuleName = "GW2MM Host";

    public static (FirewallStatus Status, string Detail) Check(string exePath, int port)
    {
        if (!OperatingSystem.IsWindows()) return (FirewallStatus.Unknown, "Not Windows.");
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return (FirewallStatus.Unknown, "HNetCfg.FwPolicy2 is not available.");
            dynamic policy = Activator.CreateInstance(type)!;
            int current = policy.CurrentProfileTypes;
            var enabledProfiles = 0;
            foreach (var profile in new[] { 1, 2, 4 }) // domain, private, public
                if ((current & profile) != 0 && FirewallOn(policy, profile)) enabledProfiles |= profile;
            var profileNames = ProfileNames(current);
            if (enabledProfiles == 0) return (FirewallStatus.Allowed, $"Firewall is off for the current network ({profileNames}).");

            var allowedProfiles = 0;
            var blocked = false;
            foreach (dynamic rule in policy.Rules)
            {
                if ((int)rule.Direction != NetFwRuleDirIn || !(bool)rule.Enabled) continue;
                int profiles = rule.Profiles;
                if ((profiles & enabledProfiles) == 0) continue;
                int protocol = rule.Protocol;
                if (protocol != ProtocolTcp && protocol != ProtocolAny) continue;
                string? app = rule.ApplicationName;
                string? ports = rule.LocalPorts;
                var forApp = !string.IsNullOrEmpty(app) && SamePath(app, exePath);
                var anyApp = string.IsNullOrEmpty(app) || app == "*";
                if (!forApp && !(anyApp && PortMatches(ports, port))) continue;
                if (forApp && !PortMatches(ports, port)) continue;
                int action = rule.Action;
                if (action == NetFwActionBlock && forApp) blocked = true;
                else if (action == NetFwActionAllow) allowedProfiles |= profiles & enabledProfiles;
            }
            if (blocked) return (FirewallStatus.Blocked, $"A Windows Firewall rule blocks {Path.GetFileName(exePath)} ({profileNames} network).");
            return allowedProfiles == enabledProfiles
                ? (FirewallStatus.Allowed, $"Windows Firewall allows {Path.GetFileName(exePath)} ({profileNames} network).")
                : (FirewallStatus.NotAllowed, $"No Windows Firewall rule allows {Path.GetFileName(exePath)} on the current ({profileNames}) network.");
        }
        catch (Exception ex)
        {
            return (FirewallStatus.Unknown, "Could not read the Windows Firewall settings: " + ex.Message);
        }
    }

    private static bool SamePath(string rulePath, string exePath)
    {
        try { return string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(rulePath)), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static bool FirewallOn(dynamic policy, int profile)
    {
        try { return (bool)policy.FirewallEnabled[profile]; }
        catch (Exception) { return true; }
    }

    private static string ProfileNames(int profiles)
    {
        var names = new List<string>();
        if ((profiles & 1) != 0) names.Add("Domain");
        if ((profiles & 2) != 0) names.Add("Private");
        if ((profiles & 4) != 0) names.Add("Public");
        return names.Count == 0 ? "unknown" : string.Join("/", names);
    }

    /// <summary>A rule's LocalPorts ("*", "27015", "27000-27100,80") covers the port.</summary>
    public static bool PortMatches(string? ports, int port)
    {
        if (string.IsNullOrWhiteSpace(ports) || ports.Trim() == "*") return true;
        foreach (var part in ports.Split(','))
        {
            var p = part.Trim();
            var dash = p.IndexOf('-');
            if (dash > 0 && int.TryParse(p[..dash], out var lo) && int.TryParse(p[(dash + 1)..], out var hi))
            {
                if (port >= lo && port <= hi) return true;
            }
            else if (int.TryParse(p, out var single) && single == port) return true;
        }
        return false;
    }

    /// <summary>
    /// Replaces this program's inbound rules (including the block rules a cancelled prompt leaves) with rules allowing
    /// TCP <paramref name="port"/> (players connect) and UDP <paramref name="port"/> (clock exchanges) on every network.
    /// Shows the Windows administrator prompt; false if it was declined or failed.
    /// </summary>
    public static bool AddAllowRule(string exePath, int port)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var program = $"program=\"{exePath}\"";
        var command = $"/c netsh advfirewall firewall delete rule name=all dir=in {program} & " +
                      $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow {program} protocol=TCP localport={port} profile=any enable=yes && " +
                      $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow {program} protocol=UDP localport={port} profile=any enable=yes";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", command)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null) return false;
            process.WaitForExit(30000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; } // the administrator prompt was declined
    }
}
