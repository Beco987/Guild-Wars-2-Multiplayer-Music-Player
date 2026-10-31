using System.Net;
using System.Net.Sockets;

namespace GW2MM.Core.Net;

/// <summary>What happened when the host tried to open its port on the router.</summary>
/// <param name="Open">The router now forwards the port to this computer.</param>
/// <param name="ExternalAddress">The router's internet address (what players type in), when the router reported it.</param>
/// <param name="Message">One line for the chat box / log.</param>
/// <param name="Details">Technical details for GW2MM.log (router, addresses).</param>
public sealed record PortOpenResult(bool Open, string? ExternalAddress, string Message, string Details = "");

/// <summary>
/// Opens the host's TCP port on the router with UPnP and closes it again on exit. A forward that was already
/// there (set up by hand, or by another program) is used as it is and never removed. The same port is also opened
/// for UDP, for the clock exchanges (<see cref="ClockSyncServer"/>); that part is best effort, since players fall
/// back to clock probes over TCP without it.
/// </summary>
public sealed class PortOpener
{
    public const string MappingDescription = "GW2MM";
    /// <summary>IGDv2 routers may refuse a permanent (0) lease; 7 days is the longest they must accept.</summary>
    public const int FallbackLeaseSeconds = 604800;
    private const string Protocol = "TCP";
    private const string ClockProtocol = "UDP";

    /// <summary>Returns this network's public IPv4 address as seen from the internet (plain text).</summary>
    public const string PublicAddressService = "https://api.ipify.org";

    private readonly UpnpClient _upnp;
    private readonly Func<CancellationToken, Task<UpnpGateway?>> _discover;
    private readonly Func<CancellationToken, Task<string?>> _publicAddress;
    private UpnpGateway? _gateway, _udpGateway;
    private int _port;

    /// <param name="publicAddress">
    /// Looks up the real public address (default: <see cref="PublicAddressService"/>). The router's own idea of its
    /// internet address is wrong when it sits behind another NAT, which the router cannot see.
    /// </param>
    public PortOpener(HttpClient http, Func<UpnpClient, CancellationToken, Task<UpnpGateway?>>? discover = null,
        Func<CancellationToken, Task<string?>>? publicAddress = null)
    {
        _upnp = new UpnpClient(http);
        _discover = discover is null
            ? ct => _upnp.DiscoverAsync(TimeSpan.FromSeconds(3), ct)
            : ct => discover(_upnp, ct);
        _publicAddress = publicAddress ?? (ct => LookUpPublicAddressAsync(http, ct));
    }

    private static async Task<string?> LookUpPublicAddressAsync(HttpClient http, CancellationToken ct)
    {
        try
        {
            var text = (await http.GetStringAsync(PublicAddressService, ct).ConfigureAwait(false)).Trim();
            return IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? ip.ToString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    private async Task<string?> TryGetPublicAddressAsync(CancellationToken ct)
    {
        try { return await _publicAddress(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    /// <summary>True while this program owns a forward that <see cref="CloseAsync"/> will remove.</summary>
    public bool OwnsMapping => _gateway is not null;

    public async Task<PortOpenResult> OpenAsync(int port, CancellationToken ct)
    {
        UpnpGateway? gateway;
        try { gateway = await _discover(ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is SocketException or HttpRequestException) { gateway = null; }
        if (gateway is null)
        {
            var publicIp = await TryGetPublicAddressAsync(ct).ConfigureAwait(false);
            return new PortOpenResult(false, publicIp,
                $"Could not open port {port} automatically: no router answered (UPnP may be turned off on it). Forward the port by hand so players can connect from the internet."
                + (publicIp is null ? "" : $" Your public address is {publicIp}"), "No UPnP router found.");
        }
        var details = $"Router control URL {gateway.ControlUrl} ({gateway.ServiceType}); forwarding to {gateway.LocalAddress}:{port}.";

        try
        {
            var existing = await _upnp.GetMappingAsync(gateway, port, Protocol, ct).ConfigureAwait(false);
            var mine = gateway.LocalAddress.ToString();
            var ownedByUs = false;
            if (existing is not null && existing.InternalClient != mine)
                return new PortOpenResult(false, null,
                    $"Could not open port {port} automatically: your router already forwards it to another device ({existing.InternalClient}).", details);
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
            await OpenClockPortAsync(gateway, port, ct).ConfigureAwait(false);

            var external = await TryGetExternalAddressAsync(gateway, ct).ConfigureAwait(false);
            var publicIp = await TryGetPublicAddressAsync(ct).ConfigureAwait(false);
            details += $" Router's internet address: {external ?? "unknown"}; public address: {publicIp ?? "unknown"}.";
            const string behind = "usually your internet provider (carrier-grade NAT) or a second router - so players outside your network cannot reach you through it. " +
                                  "Ask your provider for a public IP address, forward the port on the outer router too, or use a VPN such as Tailscale.";
            if (external is not null && IsPrivate(external))
                return new PortOpenResult(true, publicIp ?? external,
                    $"Port {port} is open on your router, but the router itself is behind another network ({external}) - " + behind, details);
            if (external is not null && publicIp is not null && external != publicIp)
                return new PortOpenResult(true, publicIp,
                    $"Port {port} is open on your router, but the router's internet address ({external}) is not your public address ({publicIp}) - " + behind, details);
            var opened = ownedByUs && existing is null ? $"Opened port {port} on your router (UPnP)." : $"Port {port} is already forwarded to this computer.";
            var address = publicIp ?? external;
            return new PortOpenResult(true, address, address is null ? opened : $"{opened} Players connect to {address}", details);
        }
        catch (Exception ex) when (ex is UpnpException or HttpRequestException or System.Xml.XmlException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new PortOpenResult(false, null, $"Could not open port {port} automatically: {ex.Message}", details);
        }
    }

    private async Task AddAsync(UpnpGateway gateway, int port, CancellationToken ct, string protocol = Protocol)
    {
        try
        {
            await _upnp.AddMappingAsync(gateway, port, protocol, MappingDescription, 0, ct).ConfigureAwait(false);
        }
        catch (UpnpException ex) when (ex.Code != 718) // 718 = ConflictInMappingEntry: a time limit would not help
        {
            await _upnp.AddMappingAsync(gateway, port, protocol, MappingDescription, FallbackLeaseSeconds, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The UDP forward for the clock exchanges, if the router allows it (failures are only logged by the players' fallback).</summary>
    private async Task OpenClockPortAsync(UpnpGateway gateway, int port, CancellationToken ct)
    {
        try
        {
            var existing = await _upnp.GetMappingAsync(gateway, port, ClockProtocol, ct).ConfigureAwait(false);
            if (existing is null)
            {
                await AddAsync(gateway, port, ct, ClockProtocol).ConfigureAwait(false);
                _udpGateway = gateway;
            }
            else if (existing.InternalClient == gateway.LocalAddress.ToString() && existing.Description == MappingDescription)
                _udpGateway = gateway; // left over from an earlier run
        }
        catch (Exception ex) when (ex is UpnpException or HttpRequestException or System.Xml.XmlException or TaskCanceledException && !ct.IsCancellationRequested) { }
        if (_udpGateway is not null) _port = port;
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
        var (gateway, udpGateway) = (_gateway, _udpGateway);
        _gateway = _udpGateway = null;
        if (gateway is not null)
            try { await _upnp.DeleteMappingAsync(gateway, _port, Protocol, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is UpnpException or HttpRequestException or TaskCanceledException) { }
        if (udpGateway is not null)
            try { await _upnp.DeleteMappingAsync(udpGateway, _port, ClockProtocol, ct).ConfigureAwait(false); }
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
