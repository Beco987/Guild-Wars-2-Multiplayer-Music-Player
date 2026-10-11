namespace GW2MM.Windows;

/// <summary>User-facing texts from the original GUI scripts (the Controls lists are corrected to match the real hotkeys).</summary>
internal static class Texts
{
    public const string AppTitle = "GW2MM";
    public const string Version = "Version 1.6";
    public const string GuideUrl = "https://docs.google.com/document/d/1Ek9sHKd0PQw9vmecujCG7tVtNIkKMtPnIPPEBio5ZoU/edit?usp=sharing";
    public const string DefaultHostAddress = "97.118.35.47";

    public const string Tempo = "Midis don't always import with the correct tempo, so with this you can change the tempo that the song will start with. Each -1/-+1 will decrease/increase the tempo by 5 percent. For example, -4 will slow the song down by 20 percent, while +4 will speed up the song by 20 percent. You can always change this value with the data editor.";
    public const string Description = "This is where you can type out a helpful description of the song. For example, you might want to write down which instruments are used in it as well as their order.";
    public const string Players = "Add the number of players that are used in this song. If this is set to 1 player, it will merge all the tracks in the midi and use a different method while playing it.";
    public const string OctaveDelay = "This Delay is used in Singleplayer scripts to prevent the octave bug. Usually you'll want to set this to roughly 2x your average in-game ping.";
    public const string AlwaysOnTop = "Sets the application to always be visible. This is useful if you want to play Guild Wars 2 in Fullscreen.";
    public const string Transparency = "Sets the application to be transparent. You may like this if you play Guild Wars 2 in Fullscreen.";

    public const string HostControls = "F1 - Plays the selected song\nF2 - Pauses/resumes this computer's playback\nF3 - Stops current song\nF4 - Exits the application\nF5 - Sets window as Player 1\nF6 - Sets window as Player 2\nF7 - Sets window as Player 3\nF8 - Sets window as Player 4\nF9 - Sets window as Player 5\nShift+F6 - Sets window as Player 6\nShift+F7 - Sets window as Player 7\nShift+F8 - Sets window as Player 8";
    public const string ClientControls = "F11 - Asks the host to play the selected song\nF4 - Exits the application\nF5 - Sets window as Player 1\nF6 - Sets window as Player 2\nF7 - Sets window as Player 3\nF8 - Sets window as Player 4\nF9 - Sets window as Player 5";
}
