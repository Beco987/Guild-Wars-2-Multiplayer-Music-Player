using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace GW2MM.Core.Net;

/// <summary>An error a router returned for a UPnP request (for example 718 = the port is forwarded elsewhere).</summary>
public sealed class UpnpException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>A router (Internet Gateway Device) found with SSDP, and the service that manages its port forwards.</summary>
public sealed record UpnpGateway(Uri ControlUrl, string ServiceType, IPAddress LocalAddress);

/// <summary>An existing port forward on the router.</summary>
public sealed record UpnpMapping(string InternalClient, int InternalPort, string Description);

/// <summary>
/// Minimal UPnP Internet Gateway Device client: finds the router with SSDP and asks it to forward a port to this
/// computer, so the host does not have to set up port forwarding by hand. Not in the original (which needed a
/// manual port forward).
/// </summary>
public sealed class UpnpClient
{
    public static readonly IPEndPoint SsdpMulticast = new(IPAddress.Parse("239.255.255.250"), 1900);

    private static readonly string[] SearchTargets =
    {
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
    };

    // Preferred first: IGDv2 IP connection, IGDv1 IP connection, then PPP (DSL modems).
    private static readonly string[] ServiceTypes =
    {
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    };

    private readonly HttpClient _http;

    public UpnpClient(HttpClient http) => _http = http;

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// Where to send the SSDP search: the multicast group from every active IPv4 interface, and each interface's
    /// default gateway directly (some routers only answer a unicast search, and multicast from the wrong adapter -
    /// a VPN, Hyper-V - never reaches the router).
    /// </summary>
    public static IReadOnlyList<(IPAddress Local, IPEndPoint Target)> DefaultProbes()
    {
        var probes = new List<(IPAddress, IPEndPoint)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = nic.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    probes.Add((addr.Address, SsdpMulticast));
                    foreach (var gw in props.GatewayAddresses)
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any))
                            probes.Add((addr.Address, new IPEndPoint(gw.Address, 1900)));
                }
            }
        }
        catch (NetworkInformationException) { }
        if (probes.Count == 0) probes.Add((IPAddress.Any, SsdpMulticast));
        return probes;
    }

    /// <summary>Finds a router that supports port forwarding over UPnP, or null when none answers in time.</summary>
    public async Task<UpnpGateway?> DiscoverAsync(TimeSpan timeout, CancellationToken ct,
        IReadOnlyList<(IPAddress Local, IPEndPoint Target)>? probes = null)
    {
        probes ??= DefaultProbes();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var found = new System.Collections.Concurrent.ConcurrentQueue<Uri>();
        var tried = new HashSet<string>();
        var listeners = probes.Select(p => SearchAsync(p.Local, p.Target, found, cts.Token)).ToList();
        try
        {
            while (!cts.IsCancellationRequested)
            {
                while (found.TryDequeue(out var location))
                {
                    if (!tried.Add(location.AbsoluteUri)) continue;
                    try
                    {
                        if (await DescribeAsync(location, cts.Token).ConfigureAwait(false) is { } gateway) return gateway;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or TaskCanceledException or SocketException) { }
                }
                await Task.Delay(50, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        finally
        {
            cts.Cancel();
            try { await Task.WhenAll(listeners).ConfigureAwait(false); } catch (Exception) { }
        }
        ct.ThrowIfCancellationRequested();
        return null;
    }

    private static async Task SearchAsync(IPAddress local, IPEndPoint target, System.Collections.Concurrent.ConcurrentQueue<Uri> found, CancellationToken ct)
    {
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        try
        {
            udp.Client.Bind(new IPEndPoint(local, 0));
            // Binding alone does not choose the adapter a multicast leaves from; the routing table would.
            if (target.Address.Equals(SsdpMulticast.Address) && !local.Equals(IPAddress.Any))
                udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            foreach (var st in SearchTargets)
            {
                var request = Encoding.ASCII.GetBytes(
                    "M-SEARCH * HTTP/1.1\r\n" +
                    $"HOST: {SsdpMulticast}\r\n" +
                    "MAN: \"ssdp:discover\"\r\n" +
                    "MX: 2\r\n" +
                    $"ST: {st}\r\n\r\n");
                await udp.SendAsync(request, target, ct).ConfigureAwait(false);
            }
            while (true)
            {
                var result = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                if (ParseSsdpLocation(Encoding.ASCII.GetString(result.Buffer)) is { } location) found.Enqueue(location);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }

    /// <summary>The LOCATION of a successful SSDP answer (the router's device description URL).</summary>
    public static Uri? ParseSsdpLocation(string response)
    {
        var lines = response.Split('\n');
        if (lines.Length == 0 || !lines[0].Contains(" 200", StringComparison.Ordinal)) return null;
        foreach (var raw in lines.Skip(1))
        {
            var line = raw.TrimEnd('\r');
            var colon = line.IndexOf(':');
            if (colon <= 0 || !line[..colon].Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase)) continue;
            return Uri.TryCreate(line[(colon + 1)..].Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp ? uri : null;
        }
        return null;
    }

    private async Task<UpnpGateway?> DescribeAsync(Uri location, CancellationToken ct)
    {
        var xml = await _http.GetStringAsync(location, ct).ConfigureAwait(false);
        if (FindControlUrl(xml, location) is not var (control, serviceType)) return null;
        return new UpnpGateway(control, serviceType, LocalAddressFor(control));
    }

    /// <summary>The port-forwarding service in a device description: its control URL and service type.</summary>
    public static (Uri ControlUrl, string ServiceType)? FindControlUrl(string descriptionXml, Uri location)
    {
        var doc = XDocument.Parse(descriptionXml);
        var baseText = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
        var baseUri = !string.IsNullOrEmpty(baseText) && Uri.TryCreate(baseText, UriKind.Absolute, out var b) ? b : location;
        var services = doc.Descendants().Where(e => e.Name.LocalName == "service")
            .Select(s => (Type: Child(s, "serviceType"), Control: Child(s, "controlURL")))
            .Where(s => s.Type.Length > 0 && s.Control.Length > 0)
            .ToList();
        foreach (var wanted in ServiceTypes)
            foreach (var s in services)
                if (s.Type.Equals(wanted, StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(baseUri, s.Control, out var control))
                    return (control, s.Type);
        return null;

        static string Child(XElement e, string name) => e.Elements().FirstOrDefault(c => c.Name.LocalName == name)?.Value.Trim() ?? "";
    }

    /// <summary>This computer's address on the network the router is on (the forward's destination).</summary>
    private static IPAddress LocalAddressFor(Uri control)
    {
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var router = IPAddress.TryParse(control.Host, out var ip) ? ip : Dns.GetHostAddresses(control.Host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        probe.Connect(router, control.Port); // UDP "connect" sends nothing; it only picks the route.
        return ((IPEndPoint)probe.LocalEndPoint!).Address;
    }

    // ------------------------------------------------------------------ port forwarding (SOAP)

    public async Task<string> GetExternalAddressAsync(UpnpGateway gateway, CancellationToken ct)
    {
        var result = await InvokeAsync(gateway, "GetExternalIPAddress", Array.Empty<(string, string)>(), ct).ConfigureAwait(false);
        return result.GetValueOrDefault("NewExternalIPAddress", "");
    }

    /// <summary>The router's TCP/UDP forward for an external port, or null when there is none.</summary>
    public async Task<UpnpMapping?> GetMappingAsync(UpnpGateway gateway, int externalPort, string protocol, CancellationToken ct)
    {
        try
        {
            var r = await InvokeAsync(gateway, "GetSpecificPortMappingEntry", new[]
            {
                ("NewRemoteHost", ""), ("NewExternalPort", externalPort.ToString(CultureInfo.InvariantCulture)), ("NewProtocol", protocol),
            }, ct).ConfigureAwait(false);
            return new UpnpMapping(r.GetValueOrDefault("NewInternalClient", ""),
                int.TryParse(r.GetValueOrDefault("NewInternalPort", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 0,
                r.GetValueOrDefault("NewPortMappingDescription", ""));
        }
        catch (UpnpException ex) when (ex.Code is 714 or 713 or 402) // NoSuchEntryInArray / SpecifiedArrayIndexInvalid / some routers
        {
            return null;
        }
    }

    public Task AddMappingAsync(UpnpGateway gateway, int port, string protocol, string description, int leaseSeconds, CancellationToken ct) =>
        InvokeAsync(gateway, "AddPortMapping", new[]
        {
            ("NewRemoteHost", ""),
            ("NewExternalPort", port.ToString(CultureInfo.InvariantCulture)),
            ("NewProtocol", protocol),
            ("NewInternalPort", port.ToString(CultureInfo.InvariantCulture)),
            ("NewInternalClient", gateway.LocalAddress.ToString()),
            ("NewEnabled", "1"),
            ("NewPortMappingDescription", description),
            ("NewLeaseDuration", leaseSeconds.ToString(CultureInfo.InvariantCulture)),
        }, ct);

    public Task DeleteMappingAsync(UpnpGateway gateway, int port, string protocol, CancellationToken ct) =>
        InvokeAsync(gateway, "DeletePortMapping", new[]
        {
            ("NewRemoteHost", ""), ("NewExternalPort", port.ToString(CultureInfo.InvariantCulture)), ("NewProtocol", protocol),
        }, ct);

    private async Task<Dictionary<string, string>> InvokeAsync(UpnpGateway gateway, string action, IReadOnlyList<(string Name, string Value)> args, CancellationToken ct)
    {
        var body = new StringBuilder()
            .Append("<?xml version=\"1.0\"?>")
            .Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">")
            .Append("<s:Body><u:").Append(action).Append(" xmlns:u=\"").Append(gateway.ServiceType).Append("\">");
        foreach (var (name, value) in args)
            body.Append('<').Append(name).Append('>').Append(SecurityElement.Escape(value)).Append("</").Append(name).Append('>');
        body.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");

        using var request = new HttpRequestMessage(HttpMethod.Post, gateway.ControlUrl)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{gateway.ServiceType}#{action}\"");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (ParseError(text) is { } error) throw error;
            throw new HttpRequestException($"{action}: HTTP {(int)response.StatusCode}");
        }
        return ParseResponse(text, action + "Response");
    }

    /// <summary>The output arguments of a SOAP response.</summary>
    public static Dictionary<string, string> ParseResponse(string xml, string responseElement)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(xml)) return values;
        var element = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == responseElement);
        if (element is null) return values;
        foreach (var arg in element.Elements()) values[arg.Name.LocalName] = arg.Value.Trim();
        return values;
    }

    /// <summary>The UPnPError of a SOAP fault, or null when the text is not one.</summary>
    public static UpnpException? ParseError(string xml)
    {
        try
        {
            var error = XDocument.Parse(xml).Descendants().FirstOrDefault(e => e.Name.LocalName == "UPnPError");
            if (error is null) return null;
            var code = error.Elements().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value.Trim();
            var description = error.Elements().FirstOrDefault(e => e.Name.LocalName == "errorDescription")?.Value.Trim() ?? "";
            return new UpnpException(int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0,
                $"UPnP error {code}{(description.Length > 0 ? " (" + description + ")" : "")}");
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }
}
