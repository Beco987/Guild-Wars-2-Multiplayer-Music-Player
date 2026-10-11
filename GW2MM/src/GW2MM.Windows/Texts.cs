namespace GW2MM.Windows;

/// <summary>User-facing texts from the original GUI scripts (the Controls lists are corrected to match the real hotkeys).</summary>
internal static class Texts
{
    public const string AppTitle = "GW2MM";
    public const string Version = "Version 1.6";
    public const string GuideUrl = "https://docs.google.com/document/d/1Ek9sHKd0PQw9vmecujCG7tVtNIkKMtPnIPPEBio5ZoU/edit?usp=sharing";
    public const string DefaultHostAddress = "97.118.35.47";

    public const string OctaveDelay = "This Delay is used in Singleplayer scripts to prevent the octave bug. Usually you'll want to set this to roughly 2x your average in-game ping.";
    public const string AlwaysOnTop = "Sets the application to always be visible. This is useful if you want to play Guild Wars 2 in Fullscreen.";
    public const string Transparency = "Sets the application to be transparent. You may like this if you play Guild Wars 2 in Fullscreen.";

    public const string HostControls =
        "F1 - Play the selected song for everyone\n" +
        "F2 - Pause / resume this computer's playback\n" +
        "F3 - Stop everyone and reset octaves\n" +
        "F4 - Reset octaves and exit\n" +
        "F5 - Name the active Guild Wars 2 window Player 1\n" +
        "F6 - Name it Player 2\n" +
        "F7 - Name it Player 3\n" +
        "F8 - Name it Player 4\n" +
        "F9 - Name it Player 5\n" +
        "Shift+F6 - Name it Player 6\n" +
        "Shift+F7 - Name it Player 7\n" +
        "Shift+F8 - Name it Player 8";
    public const string ClientControls =
        "F11 - Ask the host to play the selected song\n" +
        "F4 - Reset octaves and exit\n" +
        "F5 - Name the active Guild Wars 2 window Player 1\n" +
        "F6 - Name it Player 2\n" +
        "F7 - Name it Player 3\n" +
        "F8 - Name it Player 4\n" +
        "F9 - Name it Player 5";
}
