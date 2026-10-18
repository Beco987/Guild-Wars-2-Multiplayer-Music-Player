using System.Text.Json;
using System.Text.Json.Serialization;

namespace GW2MM.Core.Net;

/// <summary>
/// Messages exchanged between host and clients (one JSON object per line over TCP port 27015). They replace the
/// original AHKsock text frames ("Note_1,...", "Tick_Count", "Stop", ...), whose 16-bit length field overflowed on
/// long songs and whose keyword matching let ordinary chat text trigger commands.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Welcome), "welcome")]
[JsonDerivedType(typeof(Chat), "chat")]
[JsonDerivedType(typeof(ClockRequest), "clock")]
[JsonDerivedType(typeof(SongPart), "song")]
[JsonDerivedType(typeof(StopPlayback), "stop")]
[JsonDerivedType(typeof(SyncInstrument), "sync")]
[JsonDerivedType(typeof(Ping), "ping")]
[JsonDerivedType(typeof(Hello), "hello")]
[JsonDerivedType(typeof(Typing), "typing")]
[JsonDerivedType(typeof(ClockReply), "clockReply")]
[JsonDerivedType(typeof(Pong), "pong")]
[JsonDerivedType(typeof(InputDelayReport), "inputDelay")]
[JsonDerivedType(typeof(PlayRequest), "play")]
[JsonDerivedType(typeof(PlaylistRequest), "playlist")]
[JsonDerivedType(typeof(NowPlaying), "nowPlaying")]
public abstract record Message;

// ---- host -> client ----
/// <summary>Player slot assigned to the client (2..5), or null when all slots are taken ("Socket,Socket&lt;n&gt;").</summary>
public sealed record Welcome(int? Slot) : Message;
/// <summary>
/// Chat text (both directions). From is the sender's name, set by the host when it passes a message on to the
/// other clients ("Host" for the host's own messages); a client leaves it empty, as the host knows who sent it.
/// Chat is only ever displayed: the only chat command is "stop" (see ChatRules), which only the host acts on.
/// </summary>
public sealed record Chat(string Text, string? From = null) : Message;
/// <summary>Asks for the client's clock ("Tick_Count").</summary>
public sealed record ClockRequest(int Id) : Message;
/// <summary>
/// The client's part: key strings and the client-clock times ("Note_1"/"Rest_1"). <c>OctaveTimer</c>: the host
/// uses the live octave timer, so the client presses octave changes early with its own (see PlaybackLoop).
/// </summary>
public sealed record SongPart(string[] Notes, double?[] Rests,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool OctaveTimer = false) : Message;
/// <summary>Stop playing and reset octaves ("Stop"); between playlist songs the host may ask to keep the octaves.</summary>
public sealed record StopPlayback(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool KeepOctaves = false) : Message;
/// <summary>Measure the input delay ("Sync_Instrument").</summary>
public sealed record SyncInstrument : Message;
/// <summary>Round-trip probe ("Ping_Server").</summary>
public sealed record Ping(int Id) : Message;
/// <summary>
/// What is playing (not in the original). Song is null when nothing is; Finished marks the natural end of a playlist.
/// Index is 1-based within Count songs of Playlist (0 for a single song).
/// </summary>
public sealed record NowPlaying(string? Song, string? Playlist, int Index, int Count, bool Finished = false) : Message;

// ---- client -> host ----
/// <summary>The client's user name, sent once connected.</summary>
public sealed record Hello(string Name) : Message;
/// <summary>The client started/stopped typing in the chat box.</summary>
public sealed record Typing(bool Active) : Message;
/// <summary>The client's clock at the time it answered ("Tick_Count,SocketN,tick").</summary>
public sealed record ClockReply(int Id, double Tick) : Message;
/// <summary>Answer to <see cref="Ping"/> ("Return_Ping").</summary>
public sealed record Pong(int Id) : Message;
/// <summary>Measured input delay in ms, null if nothing was measured ("Socket_Ping").</summary>
public sealed record InputDelayReport(long? Ms) : Message;
/// <summary>
/// Play this song for everyone ("Play;..." chunks + "StartSong,tempo"). Tempo is the 6-decimal text the original
/// sent. Name (not in the original) is the song's name, for "now playing" notices; SingleInstrument marks a
/// one-player song in the client's library (all tracks merged into one instrument).
/// </summary>
public sealed record PlayRequest(string Csv, string Tempo, string? Name = null, bool SingleInstrument = false) : Message;
/// <summary>Play these songs one after another, starting at StartIndex (not in the original).</summary>
public sealed record PlaylistRequest(string Name, PlayRequest[] Songs, int StartIndex, bool Repeat) : Message;

public static class MessageCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(Message m) => JsonSerializer.Serialize(m, Options);

    /// <summary>Returns null for malformed or unknown messages.</summary>
    public static Message? Deserialize(string line)
    {
        try { return JsonSerializer.Deserialize<Message>(line, Options); }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
