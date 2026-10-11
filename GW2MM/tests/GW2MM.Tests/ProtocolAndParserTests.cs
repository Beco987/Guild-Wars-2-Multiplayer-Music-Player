using System.Net;
using System.Net.Sockets;
using System.Text;
using GW2MM.Core.Arrangement;
using GW2MM.Core.Net;

namespace GW2MM.Tests;

public class ProtocolAndParserTests
{
    [Fact]
    public void MessagesRoundTrip()
    {
        Message[] all =
        {
            new Welcome(3), new Welcome(null), new Chat("hi \"there\"\n"), new ClockRequest(7),
            new SongPart(new[] { "", "Àľ", "x" }, new double?[] { 1.5, null, 1e12 }),
            new StopPlayback(), new SyncInstrument(), new Ping(1), new Hello("Bob"), new Typing(true),
            new ClockReply(7, 123.25), new Pong(1), new InputDelayReport(null), new InputDelayReport(80),
            new PlayRequest("1, 0, Note_on_c, 0, 60, 90\n", "1.050000"),
        };
        foreach (var m in all)
        {
            var back = MessageCodec.Deserialize(MessageCodec.Serialize(m));
            Assert.NotNull(back);
            Assert.Equal(MessageCodec.Serialize(m), MessageCodec.Serialize(back!));
        }
        Assert.Null(MessageCodec.Deserialize("{\"type\":\"unknown\"}"));
        Assert.Null(MessageCodec.Deserialize("not json"));
    }

    [Fact]
    public async Task LargeAndFragmentedMessagesArriveIntact()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync();
        using var raw = new TcpClient();
        await raw.ConnectAsync(IPAddress.Loopback, port);
        using var conn = new PeerConnection(await acceptTask);
        var received = new List<Message>();
        conn.Received += (_, m) => { lock (received) received.Add(m); };
        conn.Start();

        // A song far larger than the original's 64 KB frame limit, written in small pieces.
        var notes = Enumerable.Range(0, 200_000).Select(i => ((char)(192 + i % 120)).ToString()).ToArray();
        var big = MessageCodec.Serialize(new SongPart(notes, notes.Select((_, i) => (double?)i).ToArray()));
        var bytes = Encoding.UTF8.GetBytes(MessageCodec.Serialize(new Chat("a")) + "\n" + big + "\n" + "garbage\n" + MessageCodec.Serialize(new Ping(5)) + "\n");
        var stream = raw.GetStream();
        for (var i = 0; i < bytes.Length; i += 7777) await stream.WriteAsync(bytes.AsMemory(i, Math.Min(7777, bytes.Length - i)));
        await Wait.Until(() => { lock (received) return received.Count == 3; });
        Assert.IsType<Chat>(received[0]);
        Assert.Equal(200_000, Assert.IsType<SongPart>(received[1]).Notes.Length);
        Assert.Equal(5, Assert.IsType<Ping>(received[2]).Id);
        listener.Stop();
    }

    [Fact]
    public void KeyMapDecodesEveryWindowAndKey()
    {
        for (var w = 1; w <= 8; w++)
            foreach (var (from, to) in KeyMap.Replacements(w))
            {
                var d = KeyMap.Decode(to[0])!.Value;
                Assert.Equal(w, d.Window);
            }
        Assert.Equal((1, "1"), KeyMap.Decode((char)192));
        Assert.Equal((2, "F1"), KeyMap.Decode((char)209));
        Assert.Equal((8, "0"), KeyMap.Decode((char)318));
        Assert.Null(KeyMap.Decode((char)207)); // 16th code of a window is unused
        Assert.Null(KeyMap.Decode('a'));
        Assert.Null(KeyMap.Decode((char)320));
    }

    [Fact]
    public void ParserQuirksAreKept()
    {
        var song = CsvSong.Parse(new[]
        {
            "1, 0, Tempo, 500000",
            "1, 0, Tempo, 400000",                 // before the first note: ignored
            "2, 0, Note_on_c, 0, 30, 90",          // out of range before any mapped field: blank letter
            "2, 10, Control_c, 0, 64, 127",        // controller number 64 in field 5 sets the letter to "d"
            "2, 20, Note_on_c, 0, 100, 90",        // out of range: repeats "d"
            "2, 30, Note_on_c, 0, 60, 0",          // velocity 0: not recorded (but sets the letter)
            "2, 40, note_on_c, 0, 61, 1",          // the type comparison is case-insensitive
            "2, 50, Note_off_c, 0, 62, 64",        // note-off: not recorded
            "2, 60, Tempo, 600000",                // after the first note: kept
            "26, 70, Note_on_c, 0, 60, 90",        // track > 25: dropped
            "2, 80, Text_t, \"a, 72, b\"",         // a comma inside quotes still splits: field 5 = 72 -> "q"
            "2, 90, Note_on_c, 0, 5, 90",
        });
        Assert.Equal(new[] { "", "d", "A", "q" }, song.TrackNotes[2]);
        Assert.Equal(new[] { "0", "20", "40", "90" }, song.TrackTimes[2]);
        Assert.Equal(new[] { "B500000", "B600000" }, song.TempoMarkers);
        Assert.Equal(new[] { "0", "60" }, song.TempoTimes);
    }
}
