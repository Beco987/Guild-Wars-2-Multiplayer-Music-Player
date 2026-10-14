namespace GW2MM.Windows;

/// <summary>User-facing texts from the original GUI scripts .</summary>
internal static class Texts
{
    public const string AppTitle = "GW2MM";
    public const string Version = "Version 1.6";
    public const string GuideUrl = "https://docs.google.com/document/d/1Ek9sHKd0PQw9vmecujCG7tVtNIkKMtPnIPPEBio5ZoU/edit?usp=sharing";

    public const string OctaveDelay = "Guild Wars 2 only applies an octave change after a round trip to the server, so a note played too soon after one sounds in the wrong octave. Every song presses each octave change at least this many milliseconds before its note whenever the gap between notes allows (and as early as possible when it does not). It is also the pause between the octave-reset presses after a stop. Usually you'll want to set this to roughly 2x your average in-game ping.";
    public const string AlwaysOnTop = "Sets the application to always be visible. This is useful if you want to play Guild Wars 2 in Fullscreen.";
    public const string Transparency = "Sets the application to be transparent. You may like this if you play Guild Wars 2 in Fullscreen.";
    public const string AskOnLaunch = "When ticked, the client asks for your name and the host's IP address every time it starts. Untick it to skip both prompts and use the name and host IP below instead (changes apply the next time the client starts). If no host IP is saved, it is still asked for.";
    public const string Offset = "Solo play only (when the client is not connected to a host). Spreads your game windows' parts over this many milliseconds so no two windows press a key at the same instant: with n windows playing, each starts Offset / n ms after the one before. Set it to the game server's tick interval if you know it (50 ms = 20 ticks a second). 0 turns it off. One window, and playing with a host, are never offset.";
    public const string OpenPort = "Asks your router (with UPnP) to forward port 27015 to this computer while the host is open, so players can connect from the internet without setting up port forwarding by hand. The forward is removed again when you close the host. If your router has UPnP turned off, forward TCP port 27015 by hand instead.";

}
