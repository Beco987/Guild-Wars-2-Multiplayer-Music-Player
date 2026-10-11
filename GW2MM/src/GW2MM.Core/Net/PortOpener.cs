using System.Net;
using System.Net.Sockets;

namespace GW2MM.Core.Net;

/// <summary>What happened when the host tried to open its port on the router.</summary>
/// <param name="Open">The router now forwards the port to this computer.</param>
/// <param name="ExternalAddress">The router's internet address (what players type in), when the router reported it.</param>
/// <param name="Message">One line for the status bar / log.</param>
public sealed record PortOpenResult(bool Open, string? ExternalAddress, string Message);

/// <summary>
/// Opens the host's TCP port on the router with UPnP and closes it again on exit. A forward that was already
/// there (set up by hand, or by another program) is used as it is and never removed.
/// </summary>
public sealed class PortOpener
{
    public const string MappingDescription = "GW2MM";
    /// <summary>IGDv2 routers may refuse a permanent (0) lease; 7 days is the longest they must accept.</summary>
    public const int FallbackLeaseSeconds = 604800;
    private const string Protocol = "TCP";

    private readonly UpnpClient _upnp;
    private readonly Func<CancellationToken, Task<UpnpGateway?>> _discover;
    private UpnpGateway? _gateway;
    private int _port;

    public PortOpener(HttpClient http, Func<UpnpClient, CancellationToken, Task<UpnpGateway?>>? discover = null)
    {
        _upnp = new UpnpClient(http);
        _discover = discover is null
            ? ct => _upnp.DiscoverAsync(TimeSpan.FromSeconds(3), ct)
            : ct => discover(_upnp, ct);
    }

    /// <summary>True while this program owns a forward that <see cref="CloseAsync"/> will remove.</summary>
    public bool OwnsMapping => _gateway is not null;

    public async Task<PortOpenResult> OpenAsync(int port, CancellationToken ct)
    {
        UpnpGateway? gateway;
        try { gateway = await _discover(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is SocketException or HttpRequestException) { gateway = null; }
        if (gateway is null)
            return new PortOpenResult(false, null,
                $"Could not open port {port} automatically: no router answered (UPnP may be turned off on it). Forward the port by hand so players can connect from the internet.");

        try
        {
            var existing = await _upnp.GetMappingAsync(gateway, port, Protocol, ct).ConfigureAwait(false);
            var mine = gateway.LocalAddress.ToString();
            var ownedByUs = false;
            if (existing is not null && existing.InternalClient != mine)
                return new PortOpenResult(false, null,
                    $"Could not open port {port} automatically: your router already forwards it to another device ({existing.InternalClient}).");
            if (existing is null)
            {
                await AddAsync(gateway, port, ct).ConfigureAwait(false);
                ownedByUs = true;
            }
            else
            {
                // Left over from an earlier run that did not exit cleanly: still ours to remove.
                ownedByUs = existing.Description == MappingDescription;
            }
            if (ownedByUs) { _gateway = gateway; _port = port; }

            var external = await TryGetExternalAddressAsync(gateway, ct).ConfigureAwait(false);
            if (external is not null && IsPrivate(external))
                return new PortOpenResult(true, external,
                    $"Port {port} is open on your router, but the router itself is behind another network ({external}) - " +
                    "usually your internet provider or a second router - so players outside your network may still not reach you.");
            var opened = ownedByUs && existing is null ? $"Opened port {port} on your router (UPnP)." : $"Port {port} is already forwarded to this computer.";
            return new PortOpenResult(true, external, external is null ? opened : $"{opened} Players connect to {external}");
        }
        catch (Exception ex) when (ex is UpnpException or HttpRequestException or System.Xml.XmlException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new PortOpenResult(false, null, $"Could not open port {port} automatically: {ex.Message}");
        }
    }

    private async Task AddAsync(UpnpGateway gateway, int port, CancellationToken ct)
    {
        try
        {
            await _upnp.AddMappingAsync(gateway, port, Protocol, MappingDescription, 0, ct).ConfigureAwait(false);
        }
        catch (UpnpException ex) when (ex.Code != 718) // 718 = ConflictInMappingEntry: a time limit would not help
        {
            await _upnp.AddMappingAsync(gateway, port, Protocol, MappingDescription, FallbackLeaseSeconds, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> TryGetExternalAddressAsync(UpnpGateway gateway, CancellationToken ct)
    {
        try
        {
            var text = await _upnp.GetExternalAddressAsync(gateway, ct).ConfigureAwait(false);
            return IPAddress.TryParse(text, out var ip) && !ip.Equals(IPAddress.Any) ? ip.ToString() : null;
        }
        catch (Exception ex) when (ex is UpnpException or HttpRequestException or System.Xml.XmlException) { return null; }
    }

    /// <summary>Removes the forward this program added (nothing when it was already there).</summary>
    public async Task CloseAsync(CancellationToken ct)
    {
        var gateway = _gateway;
        if (gateway is null) return;
        _gateway = null;
        try { await _upnp.DeleteMappingAsync(gateway, _port, Protocol, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is UpnpException or HttpRequestException or TaskCanceledException) { }
    }

    /// <summary>Addresses that cannot be reached from the internet (private ranges and carrier-grade NAT).</summary>
    public static bool IsPrivate(string address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            || (b[0] == 169 && b[1] == 254);
    }
}
