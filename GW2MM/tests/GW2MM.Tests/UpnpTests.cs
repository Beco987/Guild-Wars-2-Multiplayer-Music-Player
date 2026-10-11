using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using GW2MM.Core.Net;

namespace GW2MM.Tests;

/// <summary>UPnP port opening against a fake router (SSDP responder + IGD description + SOAP control endpoint).</summary>
public sealed class UpnpTests : IDisposable
{
    private const string IpService = "urn:schemas-upnp-org:service:WANIPConnection:1";

    /// <summary>A minimal Internet Gateway Device on 127.0.0.1 that keeps a port forward table.</summary>
    private sealed class FakeRouter : IDisposable
    {
        private readonly HttpListener _http = new();
        private readonly UdpClient _ssdp = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _stop = new();
        public readonly Dictionary<int, (string Client, string Description, int Lease)> Mappings = new();
        public readonly List<string> Actions = new();
        public string ExternalAddress = "203.0.113.7";
        public bool RejectPermanentLease;
        public string ServiceType = IpService;
        public string Prefix { get; }
        public IPEndPoint SsdpEndPoint => (IPEndPoint)_ssdp.Client.LocalEndPoint!;

        public FakeRouter()
        {
            var port = FreePort();
            Prefix = $"http://127.0.0.1:{port}/";
            _http.Prefixes.Add(Prefix);
            _http.Start();
            _ = Task.Run(ServeHttpAsync);
            _ = Task.Run(ServeSsdpAsync);
        }

        private static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private async Task ServeSsdpAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await _ssdp.ReceiveAsync(_stop.Token); }
                catch (Exception) { return; }
                var request = Encoding.ASCII.GetString(r.Buffer);
                if (!request.StartsWith("M-SEARCH", StringComparison.Ordinal) || !request.Contains("ssdp:discover")) continue;
                var reply = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nST: upnp:rootdevice\r\nLocation: {Prefix}rootDesc.xml\r\n\r\n");
                await _ssdp.SendAsync(reply, r.RemoteEndPoint);
            }
        }

        private async Task ServeHttpAsync()
        {
            while (_http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _http.GetContextAsync(); }
                catch (Exception) { return; }
                var (status, body) = ctx.Request.HttpMethod == "GET" ? (200, Description()) : Control(ctx.Request);
                var bytes = Encoding.UTF8.GetBytes(body);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "text/xml";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        private string Description() => $"""
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
                <serviceList><service><serviceType>urn:schemas-upnp-org:service:Layer3Forwarding:1</serviceType><controlURL>/l3f</controlURL></service></serviceList>
                <deviceList><device>
                  <deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>
                  <deviceList><device>
                    <deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>
                    <serviceList><service><serviceType>{ServiceType}</serviceType><controlURL>/ctl/IPConn</controlURL></service></serviceList>
                  </device></deviceList>
                </device></deviceList>
              </device>
            </root>
            """;

        private (int, string) Control(HttpListenerRequest request)
        {
            var soapAction = request.Headers["SOAPAction"] ?? "";
            var action = soapAction.Trim('"').Split('#').Last();
            Actions.Add(action);
            if (request.Url!.AbsolutePath != "/ctl/IPConn" || !soapAction.StartsWith($"\"{ServiceType}#", StringComparison.Ordinal))
                return Fault(401, "Invalid Action");
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var args = XDocument.Parse(reader.ReadToEnd()).Descendants().First(e => e.Name.LocalName == action)
                .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
            switch (action)
            {
                case "GetExternalIPAddress":
                    return Ok(action, ("NewExternalIPAddress", ExternalAddress));
                case "GetSpecificPortMappingEntry":
                    return Mappings.TryGetValue(int.Parse(args["NewExternalPort"]), out var m)
                        ? Ok(action, ("NewInternalPort", args["NewExternalPort"]), ("NewInternalClient", m.Client), ("NewEnabled", "1"),
                            ("NewPortMappingDescription", m.Description), ("NewLeaseDuration", m.Lease.ToString()))
                        : Fault(714, "NoSuchEntryInArray");
                case "AddPortMapping":
                    var port = int.Parse(args["NewExternalPort"]);
                    var lease = int.Parse(args["NewLeaseDuration"]);
                    if (args["NewProtocol"] != "TCP" || args["NewInternalPort"] != args["NewExternalPort"]) return Fault(402, "Invalid Args");
                    if (RejectPermanentLease && lease == 0) return Fault(725, "OnlyPermanentLeasesSupported"); // wrong code on purpose: routers vary
                    if (Mappings.TryGetValue(port, out var cur) && cur.Client != args["NewInternalClient"]) return Fault(718, "ConflictInMappingEntry");
                    Mappings[port] = (args["NewInternalClient"], args["NewPortMappingDescription"], lease);
                    return Ok(action);
                case "DeletePortMapping":
                    return Mappings.Remove(int.Parse(args["NewExternalPort"])) ? Ok(action) : Fault(714, "NoSuchEntryInArray");
                default:
                    return Fault(401, "Invalid Action");
            }
        }

        private (int, string) Ok(string action, params (string Name, string Value)[] outs) => (200,
            $"""<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><u:{action}Response xmlns:u="{ServiceType}">{string.Concat(outs.Select(o => $"<{o.Name}>{o.Value}</{o.Name}>"))}</u:{action}Response></s:Body></s:Envelope>""");

        private static (int, string) Fault(int code, string text) => (500,
            $"""<?xml version="1.0"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail><UPnPError xmlns="urn:schemas-upnp-org:control-1-0"><errorCode>{code}</errorCode><errorDescription>{text}</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>""");

        public void Dispose()
        {
            _stop.Cancel();
            _http.Close();
            _ssdp.Dispose();
        }
    }

    private readonly FakeRouter _router = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public void Dispose() { _router.Dispose(); _http.Dispose(); }

    /// <summary>Discovery aimed at the fake router's SSDP socket instead of the multicast group.</summary>
    private PortOpener Opener() => new(_http, (upnp, ct) =>
        upnp.DiscoverAsync(TimeSpan.FromSeconds(3), ct, new[] { (IPAddress.Loopback, _router.SsdpEndPoint) }));

    [Fact]
    public async Task DiscoversTheRouterOverSsdpAndFindsItsPortForwardingService()
    {
        var gateway = await new UpnpClient(_http).DiscoverAsync(TimeSpan.FromSeconds(3), CancellationToken.None,
            new[] { (IPAddress.Loopback, _router.SsdpEndPoint) });
        Assert.NotNull(gateway);
        Assert.Equal(new Uri(_router.Prefix + "ctl/IPConn"), gateway!.ControlUrl);
        Assert.Equal(IpService, gateway.ServiceType);
        Assert.Equal(IPAddress.Loopback, gateway.LocalAddress);
    }

    [Fact]
    public async Task NoRouterMeansNoGatewayWithinTheTimeout()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); // receives the search, never answers
        var gateway = await new UpnpClient(_http).DiscoverAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None,
            new[] { (IPAddress.Loopback, (IPEndPoint)silent.Client.LocalEndPoint!) });
        Assert.Null(gateway);
    }

    [Fact]
    public async Task OpensThePortAndRemovesItAgainOnClose()
    {
        var opener = Opener();
        var result = await opener.OpenAsync(27015, CancellationToken.None);
        Assert.True(result.Open, result.Message);
        Assert.Equal("203.0.113.7", result.ExternalAddress);
        Assert.Contains("Players connect to 203.0.113.7", result.Message);
        Assert.Equal(("127.0.0.1", PortOpener.MappingDescription, 0), _router.Mappings[27015]);
        Assert.True(opener.OwnsMapping);

        await opener.CloseAsync(CancellationToken.None);
        Assert.Empty(_router.Mappings);
        Assert.False(opener.OwnsMapping);
    }

    [Fact]
    public async Task AManualForwardToThisComputerIsUsedButNeverRemoved()
    {
        _router.Mappings[27015] = ("127.0.0.1", "my manual forward", 0);
        var opener = Opener();
        var result = await opener.OpenAsync(27015, CancellationToken.None);
        Assert.True(result.Open);
        Assert.Contains("already forwarded to this computer", result.Message);
        Assert.DoesNotContain("AddPortMapping", _router.Actions);

        await opener.CloseAsync(CancellationToken.None);
        Assert.True(_router.Mappings.ContainsKey(27015));
        Assert.DoesNotContain("DeletePortMapping", _router.Actions);
    }

    [Fact]
    public async Task AForwardLeftByACrashedRunIsAdoptedAndRemoved()
    {
        _router.Mappings[27015] = ("127.0.0.1", PortOpener.MappingDescription, 0);
        var opener = Opener();
        Assert.True((await opener.OpenAsync(27015, CancellationToken.None)).Open);
        await opener.CloseAsync(CancellationToken.None);
        Assert.Empty(_router.Mappings);
    }

    [Fact]
    public async Task APortForwardedToAnotherDeviceIsReportedAndLeftAlone()
    {
        _router.Mappings[27015] = ("192.168.1.50", "someone else", 0);
        var opener = Opener();
        var result = await opener.OpenAsync(27015, CancellationToken.None);
        Assert.False(result.Open);
        Assert.Contains("192.168.1.50", result.Message);
        Assert.Equal("192.168.1.50", _router.Mappings[27015].Client);
        Assert.False(opener.OwnsMapping);
    }

    [Fact]
    public async Task FallsBackToATimedLeaseWhenTheRouterRefusesAPermanentOne()
    {
        _router.RejectPermanentLease = true;
        var result = await Opener().OpenAsync(27015, CancellationToken.None);
        Assert.True(result.Open, result.Message);
        Assert.Equal(PortOpener.FallbackLeaseSeconds, _router.Mappings[27015].Lease);
    }

    [Fact]
    public async Task WarnsWhenTheRouterIsItselfBehindAnotherNat()
    {
        _router.ExternalAddress = "100.72.4.9"; // carrier-grade NAT
        var result = await Opener().OpenAsync(27015, CancellationToken.None);
        Assert.True(result.Open);
        Assert.Contains("behind another network", result.Message);
    }

    [Fact]
    public async Task WorksWithPppRouters()
    {
        _router.ServiceType = "urn:schemas-upnp-org:service:WANPPPConnection:1";
        var result = await Opener().OpenAsync(27015, CancellationToken.None);
        Assert.True(result.Open, result.Message);
    }

    [Fact]
    public async Task NoRouterGivesAHelpfulMessage()
    {
        var result = await new PortOpener(_http, (_, _) => Task.FromResult<UpnpGateway?>(null)).OpenAsync(27015, CancellationToken.None);
        Assert.False(result.Open);
        Assert.Contains("Forward the port by hand", result.Message);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nLOCATION: http://192.168.1.1:5000/rootDesc.xml\r\nST: upnp:rootdevice\r\n\r\n", "http://192.168.1.1:5000/rootDesc.xml")]
    [InlineData("HTTP/1.1 200 OK\r\nlocation:http://10.0.0.1/igd.xml\r\n\r\n", "http://10.0.0.1/igd.xml")]
    [InlineData("NOTIFY * HTTP/1.1\r\nLOCATION: http://10.0.0.1/igd.xml\r\n\r\n", null)]
    [InlineData("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n", null)]
    public void ParsesSsdpAnswers(string response, string? location) =>
        Assert.Equal(location, UpnpClient.ParseSsdpLocation(response)?.AbsoluteUri);

    [Fact]
    public void ResolvesTheControlUrlAgainstUrlBase()
    {
        const string xml = """
            <root xmlns="urn:schemas-upnp-org:device-1-0"><URLBase>http://192.168.0.1:49000/</URLBase>
            <device><serviceList><service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>upnp/control/WANIPConn1</controlURL></service></serviceList></device></root>
            """;
        var (control, type) = UpnpClient.FindControlUrl(xml, new Uri("http://192.168.0.1:49000/igddesc.xml"))!.Value;
        Assert.Equal("http://192.168.0.1:49000/upnp/control/WANIPConn1", control.AbsoluteUri);
        Assert.Equal(IpService, type);
        Assert.Null(UpnpClient.FindControlUrl("<root><device/></root>", new Uri("http://x/")));
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("203.0.113.7", false)]
    public void RecognisesPrivateAddresses(string address, bool isPrivate) => Assert.Equal(isPrivate, PortOpener.IsPrivate(address));

    [Fact]
    public void SettingDefaultsOnAndIsSaved()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw2mm-upnp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var s = Core.Config.AppSettings.Load(dir);
            Assert.True(s.OpenPortAutomatically);
            s.OpenPortAutomatically = false;
            s.SaveDisplaySettings(dir);
            Assert.False(Core.Config.AppSettings.Load(dir).OpenPortAutomatically);
            Assert.Contains("OpenPort,0", File.ReadAllText(Path.Combine(dir, "Config.txt")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
