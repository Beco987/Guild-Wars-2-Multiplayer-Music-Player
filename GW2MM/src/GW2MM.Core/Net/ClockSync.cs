using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GW2MM.Core.Playback;
using GW2MM.Core.Sessions;

namespace GW2MM.Core.Net;

/// <summary>
/// The clock exchanges between a client and the host, over UDP on the host's port (so a lost packet only loses that
/// exchange instead of holding back the ones behind it, as TCP would). The client asks once a second; the host
/// answers at once. Every time is read right at the socket: just before sending, or just after a packet arrives.
/// <para>Wire format (little-endian):</para>
/// <list type="bullet">
/// <item>request (client to host): "GWT1", token (8 bytes, from the host's Welcome), sequence number (4), the number of
/// finished exchanges that follow (1), then for each its four times T1..T4 (4 × 8, doubles).</item>
/// <item>reply (host to client): "GWT2", the request's sequence number (4), T2 and T3 (2 × 8).</item>
/// </list>
/// The host keeps no state per exchange: the client sends the four times of its recent exchanges with the next
/// request, and the host adds them to that client's <see cref="ClockTracker"/>.
/// </summary>
public static class ClockPackets
{
    public const int MaxSamplesPerRequest = 4;
    private static readonly byte[] RequestMagic = "GWT1"u8.ToArray();
    private static readonly byte[] ReplyMagic = "GWT2"u8.ToArray();
    public const int ReplyLength = 4 + 4 + 16;

    public static int WriteRequest(Span<byte> buffer, ulong token, uint seq, IReadOnlyList<ClockSample> samples)
    {
        var n = Math.Min(samples.Count, MaxSamplesPerRequest);
        RequestMagic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer[4..], token);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..], seq);
        buffer[16] = (byte)n;
        var at = 17;
        for (var i = 0; i < n; i++)
        {
            var s = samples[samples.Count - n + i];
            foreach (var t in new[] { s.T1, s.T2, s.T3, s.T4 })
            {
                BinaryPrimitives.WriteDoubleLittleEndian(buffer[at..], t);
                at += 8;
            }
        }
        return at;
    }

    public static bool TryReadRequest(ReadOnlySpan<byte> packet, out ulong token, out uint seq, out List<ClockSample> samples)
    {
        token = 0; seq = 0; samples = new List<ClockSample>();
        if (packet.Length < 17 || !packet[..4].SequenceEqual(RequestMagic)) return false;
        token = BinaryPrimitives.ReadUInt64LittleEndian(packet[4..]);
        seq = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
        int n = packet[16];
        if (n > MaxSamplesPerRequest || packet.Length < 17 + n * 32) return false;
        for (var i = 0; i < n; i++)
        {
            var at = 17 + i * 32;
            samples.Add(new ClockSample(BinaryPrimitives.ReadDoubleLittleEndian(packet[at..]), BinaryPrimitives.ReadDoubleLittleEndian(packet[(at + 8)..]),
                BinaryPrimitives.ReadDoubleLittleEndian(packet[(at + 16)..]), BinaryPrimitives.ReadDoubleLittleEndian(packet[(at + 24)..])));
        }
        return true;
    }

    public static void WriteReply(Span<byte> buffer, uint seq, double t2, double t3)
    {
        ReplyMagic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer[4..], seq);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer[8..], t2);
        BinaryPrimitives.WriteDoubleLittleEndian(buffer[16..], t3);
    }

    public static bool TryReadReply(ReadOnlySpan<byte> packet, out uint seq, out double t2, out double t3)
    {
        seq = 0; t2 = t3 = 0;
        if (packet.Length < ReplyLength || !packet[..4].SequenceEqual(ReplyMagic)) return false;
        seq = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        t2 = BinaryPrimitives.ReadDoubleLittleEndian(packet[8..]);
        t3 = BinaryPrimitives.ReadDoubleLittleEndian(packet[16..]);
        return true;
    }
}

/// <summary>The host's side: answers every client's clock requests and keeps a <see cref="ClockTracker"/> per client.</summary>
public sealed class ClockSyncServer : IDisposable
{
    private readonly Socket _socket;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<ulong, ClockTracker> _trackers = new();

    private ClockSyncServer(Socket socket, IClock clock)
    {
        _socket = socket;
        _clock = clock;
        var thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "GW2MM clock sync" };
        thread.Start();
    }

    /// <summary>Listens on UDP <paramref name="port"/>; null when that port cannot be used (the clients then fall back to TCP probes).</summary>
    public static ClockSyncServer? TryStart(int port, IClock clock)
    {
        Socket? socket = null;
        try
        {
            // IPv4 and IPv6 on one socket where the system supports it, as for the TCP listener.
            socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
            socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        }
        catch (SocketException)
        {
            socket?.Dispose();
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch (SocketException)
            {
                socket?.Dispose();
                return null;
            }
        }
        IgnoreUnreachableReplies(socket);
        return new ClockSyncServer(socket, clock);
    }

    /// <summary>
    /// On Windows a reply to a client that has gone makes the next receive fail ("connection reset") unless this is
    /// turned off.
    /// </summary>
    internal static void IgnoreUnreachableReplies(Socket socket)
    {
        if (!OperatingSystem.IsWindows()) return;
        const int SioUdpConnReset = unchecked((int)0x9800000C);
        try { socket.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null); }
        catch (SocketException) { }
    }

    /// <summary>The UDP port it listens on.</summary>
    public int Port => ((IPEndPoint)_socket.LocalEndPoint!).Port;

    /// <summary>Starts accepting exchanges for a client (its token goes to it in the Welcome).</summary>
    public ClockTracker Register(ulong token) => _trackers.GetOrAdd(token, _ => new ClockTracker());

    public void Unregister(ulong token) => _trackers.TryRemove(token, out _);

    private void Run()
    {
        var buffer = new byte[512];
        var reply = new byte[ClockPackets.ReplyLength];
        EndPoint from = new IPEndPoint(_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        while (true)
        {
            int n;
            try { n = _socket.ReceiveFrom(buffer, ref from); }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize) { continue; }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { return; }
            var t2 = _clock.NowMs;
            if (!ClockPackets.TryReadRequest(buffer.AsSpan(0, n), out var token, out var seq, out var samples)
                || !_trackers.TryGetValue(token, out var tracker)) continue;
            ClockPackets.WriteReply(reply, seq, t2, _clock.NowMs);
            try { _socket.SendTo(reply, from); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
            foreach (var s in samples) tracker.Add(s);
        }
    }

    public void Dispose() => _socket.Dispose();
}

/// <summary>
/// The client's side: asks the host for its clock once a second (quickly at first, so there is an estimate within
/// a second of connecting) and reports the four times of each finished exchange with a following request.
/// </summary>
public sealed class ClockSyncClient : IDisposable
{
    public const int BurstCount = 10;
    public const int BurstIntervalMs = 60;
    public const int IntervalMs = 1000;

    private readonly Socket _socket;
    private readonly ulong _token;
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<uint, double> _sent = new();
    private readonly List<ClockSample> _finished = new();
    private readonly CancellationTokenSource _cts = new();
    private uint _seq;

    public ClockSyncClient(IPEndPoint host, ulong token, IClock clock)
    {
        _token = token;
        _clock = clock;
        _socket = new Socket(host.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (host.AddressFamily == AddressFamily.InterNetworkV6) _socket.DualMode = true;
        ClockSyncServer.IgnoreUnreachableReplies(_socket);
        _socket.Connect(host);
    }

    /// <summary>Replies received so far (for tests and the log).</summary>
    public int Replies { get; private set; }

    public void Start()
    {
        new Thread(SendLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "GW2MM clock requests" }.Start();
        new Thread(ReceiveLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "GW2MM clock replies" }.Start();
    }

    private void SendLoop()
    {
        var buffer = new byte[256];
        for (var i = 0; !_cts.IsCancellationRequested; i++)
        {
            int length;
            lock (_gate)
            {
                var seq = ++_seq;
                length = ClockPackets.WriteRequest(buffer, _token, seq, _finished);
                _finished.Clear();
                if (_sent.Count > 64) _sent.Clear(); // replies that never came
                _sent[seq] = _clock.NowMs;
            }
            try { _socket.Send(buffer.AsSpan(0, length)); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { if (_cts.IsCancellationRequested) return; }
            if (_cts.Token.WaitHandle.WaitOne(i < BurstCount ? BurstIntervalMs : IntervalMs)) return;
        }
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[256];
        while (!_cts.IsCancellationRequested)
        {
            int n;
            try { n = _socket.Receive(buffer); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { continue; }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { return; }
            var t4 = _clock.NowMs;
            if (!ClockPackets.TryReadReply(buffer.AsSpan(0, n), out var seq, out var t2, out var t3)) continue;
            lock (_gate)
            {
                if (!_sent.Remove(seq, out var t1)) continue;
                _finished.Add(new ClockSample(t1, t2, t3, t4));
                if (_finished.Count > ClockPackets.MaxSamplesPerRequest) _finished.RemoveAt(0);
                Replies++;
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _socket.Dispose();
    }
}
