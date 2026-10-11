using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace GW2MM.Core.Net;

/// <summary>
/// One TCP connection carrying newline-delimited JSON messages. Received messages are raised on the reader task in
/// arrival order; <see cref="Send"/> never blocks (messages are queued and written in order by a writer task).
/// </summary>
public sealed class PeerConnection : IDisposable
{
    public const int DefaultPort = 27015;
    private const int MaxLineBytes = 64 * 1024 * 1024;

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private int _closed;
    private Task _writeTask = Task.CompletedTask;

    public PeerConnection(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;
        _stream = client.GetStream();
        RemoteAddress = (client.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString() ?? "?";
        if (RemoteAddress.StartsWith("::ffff:", StringComparison.Ordinal)) RemoteAddress = RemoteAddress[7..];
    }

    public string RemoteAddress { get; }

    public event Action<PeerConnection, Message>? Received;
    public event Action<PeerConnection>? Closed;

    public static async Task<PeerConnection> ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        var client = new TcpClient(); // resolves the host and tries each of its IPv4/IPv6 addresses
        try
        {
            await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
            return new PeerConnection(client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Start()
    {
        _ = Task.Run(ReadLoopAsync);
        _writeTask = Task.Run(WriteLoopAsync);
    }

    public void Send(Message message)
    {
        if (Volatile.Read(ref _closed) == 0) _outbox.Writer.TryWrite(MessageCodec.Serialize(message));
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var line in _outbox.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                var bytes = Encoding.UTF8.GetBytes(line + "\n");
                await _stream.WriteAsync(bytes, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception) { }
        finally { Close(); }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        var pending = new MemoryStream();
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var n = await _stream.ReadAsync(buffer, _cts.Token).ConfigureAwait(false);
                if (n == 0) break;
                var start = 0;
                for (var i = 0; i < n; i++)
                {
                    if (buffer[i] != (byte)'\n') continue;
                    pending.Write(buffer, start, i - start);
                    Dispatch(pending);
                    pending.SetLength(0);
                    start = i + 1;
                }
                pending.Write(buffer, start, n - start);
                if (pending.Length > MaxLineBytes) break;
            }
        }
        catch (Exception) { }
        finally { Close(); }
    }

    private void Dispatch(MemoryStream line)
    {
        var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
        if (text.Length == 0) return;
        if (MessageCodec.Deserialize(text) is { } message) Received?.Invoke(this, message);
    }

    public void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _outbox.Writer.TryComplete();
        _ = Task.Run(async () =>
        {
            // Let queued messages (e.g. a final Stop) go out before the socket closes.
            try { await _writeTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }
            _cts.Cancel();
            _client.Dispose();
            Closed?.Invoke(this);
        });
    }

    public void Dispose() => Close();
}
