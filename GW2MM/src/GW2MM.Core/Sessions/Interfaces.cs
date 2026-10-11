namespace GW2MM.Core.Sessions;

/// <summary>What a session needs from the window. Implementations marshal to the UI thread themselves.</summary>
public interface ISessionView
{
    /// <summary>The status line under the chat box ("Waiting for a peer...", "Connected to x!", ...).</summary>
    void SetStatus(string text);
    /// <summary>Appends a line to the chat box ("name > text", or a notice such as "bob joined as player 2.").</summary>
    void AppendDialog(string line);
    /// <summary>
    /// A protocol event (song part received, clock sync, ping, ...). The original echoed these into the chat box as
    /// "Peer > Tick_Count,..."; they now go to the log only, so the chat box holds only chat.
    /// </summary>
    void LogEvent(string line);
    /// <summary>Enables or disables the chat input and Send button.</summary>
    void SetChatEnabled(bool enabled);
    /// <summary>Shows a message box without blocking the caller.</summary>
    void ShowMessage(string text);
    /// <summary>Shows a message box and completes when the user closes it.</summary>
    Task ShowMessageAndWaitAsync(string text);
    /// <summary>Something went wrong that the user should know about (also logged).</summary>
    void ReportProblem(string text);
    /// <summary>The song now playing changed (null: nothing from a playlist or song request is playing).</summary>
    void OnNowPlaying(NowPlayingInfo? info);
}

/// <summary>A song that is playing, and where it is in its playlist (Index is 1-based; 0 for a single song).</summary>
public sealed record NowPlayingInfo(string Song, string? Playlist, int Index, int Count)
{
    public string Notice => Playlist is null ? $"♪ Now playing: {Song}" : $"♪ Now playing: {Song} ({Index}/{Count} in {Playlist})";
}

public interface IHostView : ISessionView
{
    void SetInputDelay(int slot, string value);
    void SetClientPing(int slot, string value);
    /// <summary>A client disconnected: commit the Port Mapping window's values and hide it (the original's Gui Submit).</summary>
    void OnClientDisconnected();
    /// <summary>Stop was triggered: show/activate the main window (the original's <c>Gui, Show</c>).</summary>
    void ShowMainWindow();
    /// <summary>A client joined, left or announced its name (see <see cref="HostSession.PlayerOf"/>).</summary>
    void OnPlayersChanged();
}

/// <summary>Measures how long the game takes to sound a note: presses "1" in the foreground window and watches the
/// default playback device's peak meter (the original Sync feature, VA.ahk).</summary>
public interface IInputDelayMeter
{
    /// <summary>Ten presses, 4 s apart; returns the delays of the presses that produced sound within the timeout.</summary>
    Task<IReadOnlyList<double>> MeasureAsync(CancellationToken ct);
}

/// <summary>The input-delay test could not run (for example, Guild Wars 2 is not open). The message is for the user.</summary>
public sealed class InputDelayException(string message) : Exception(message);

/// <summary>Chat rules shared by host and client.</summary>
public static class ChatRules
{
    /// <summary>The name shown for the host's own chat lines.</summary>
    public const string HostName = "Host";

    /// <summary>
    /// Typing exactly "stop" (any case, surrounding spaces ignored) stops the song for everyone. It is the only chat
    /// command; any other text, including text that merely contains "stop", is just chat.
    /// </summary>
    public static bool IsStopCommand(string text) => text.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase);

    public static string Line(string name, string text) => $"{name} > {text}";
}

public static class InputDelay
{
    /// <summary>Round(mean) of the samples, or null when there are none (the original then reported a blank value).</summary>
    public static long? Mean(IReadOnlyList<double> samples) =>
        samples.Count == 0 ? null : Ahk.AhkValue.Round(samples.Sum() / samples.Count);
}
