namespace GW2MM.Core.Sessions;

/// <summary>What a session needs from the window. Implementations marshal to the UI thread themselves.</summary>
public interface ISessionView
{
    /// <summary>The status line under the chat box ("Waiting for a peer...", "Connected to x!", ...).</summary>
    void SetStatus(string text);
    /// <summary>Appends a line to the chat/dialog box ("You > ..." / "Peer > ...").</summary>
    void AppendDialog(string line);
    /// <summary>Enables or disables the chat input and Send button.</summary>
    void SetChatEnabled(bool enabled);
    /// <summary>Shows a message box without blocking the caller.</summary>
    void ShowMessage(string text);
    /// <summary>Shows a message box and completes when the user closes it.</summary>
    Task ShowMessageAndWaitAsync(string text);
    /// <summary>Something went wrong that the user should know about (also logged).</summary>
    void ReportProblem(string text);
}

public interface IHostView : ISessionView
{
    void SetInputDelay(int slot, string value);
    void SetClientPing(int slot, string value);
    /// <summary>A client disconnected: commit the Port Mapping window's values and hide it (the original's Gui Submit).</summary>
    void OnClientDisconnected();
    /// <summary>Stop was triggered: show/activate the main window (the original's <c>Gui, Show</c>).</summary>
    void ShowMainWindow();
}

/// <summary>Measures how long the game takes to sound a note: presses "1" in the foreground window and watches the
/// default playback device's peak meter (the original Sync feature, VA.ahk).</summary>
public interface IInputDelayMeter
{
    /// <summary>Ten presses, 4 s apart; returns the delays of the presses that produced sound within the timeout.</summary>
    Task<IReadOnlyList<double>> MeasureAsync(CancellationToken ct);
}

public static class InputDelay
{
    /// <summary>Round(mean) of the samples, or null when there are none (the original then reported a blank value).</summary>
    public static long? Mean(IReadOnlyList<double> samples) =>
        samples.Count == 0 ? null : Ahk.AhkValue.Round(samples.Sum() / samples.Count);
}
