# GW2MM 2.0 — .NET port of the Guild Wars 2 Multiplayer Music Player

A replacement for the AutoHotkey scripts in `Midi Player - Server` and `Midi Player - Client`. It plays MIDI songs
on Guild Wars 2 instruments across several game windows and several computers. The original scripts are the
behavioural specification: song arrangement, key output, timing, hotkeys, files and GUI work the same way. The
deliberate differences are listed in [docs/PORTING_NOTES.md](docs/PORTING_NOTES.md).

| | Original | Port |
|---|---|---|
| Host ("server") | `Midi Player - Server\GW2MMConnect.ahk` | `GW2MM-Host.exe` |
| Client | `Midi Player - Client\GW2MMConnect.ahk` | `GW2MM-Client.exe` |
| MIDI → CSV | `Midicsv.exe` via a temporary `.bat` | built in (byte-identical output) |
| Data Editor | `CsvEditor.ahk` | built in (the table button next to **New**) |

No AutoHotkey, .NET runtime or other installation is required: each exe is self-contained (Windows 10/11, x64).

## Setup

1. Download `GW2MM-Host.exe` / `GW2MM-Client.exe` (from the GitHub Actions artifact, or build them, see below).
2. Put `GW2MM-Host.exe` in your existing **Midi Player - Server** folder and `GW2MM-Client.exe` in your
   **Midi Player - Client** folder. The exe uses the files next to it: `Data.csv`, `Csv\`, `Midi\` and
   `Config.txt`. These are read and written in the original formats (and encoding), so your library carries over
   unchanged, and the folders still work with the old scripts. (`--data <folder>` uses another folder.)
3. Allow `GW2MM-Host.exe` through the Windows firewall (TCP port **27015**, as before). For clients connecting over
   the internet, the host opens that port on the router by itself with UPnP (see *Port opening* below). Forward it
   by hand only if that fails.

Host and clients must all run the new version, because the network protocol changed (see porting notes).

## Using it

The workflow is the one described in the [GW2MM Guide](https://docs.google.com/document/d/1Ek9sHKd0PQw9vmecujCG7tVtNIkKMtPnIPPEBio5ZoU/edit).

1. Start Guild Wars 2 (Gw2Launcher for several accounts). Focus each game window and press **Shift+F1 … Shift+F8**
   to rename it to *Player1…Player8*.
2. Start `GW2MM-Host.exe`. It listens for clients straight away and reports in the chat box whether it opened the
   port and which address players connect to. Each client starts `GW2MM-Client.exe` and presses **Connect…** (in the
   chat card, or Settings → Server), enters a user name and the host's IP address (both remembered in Config.txt and
   filled in next time), and gets the next free player slot (Ip2–Ip5). The same button then reads **Disconnect**.
   A client that isn't connected plays songs on its own windows.
3. On the host, open **Port mapping** (in the chat card). For each slot (Ip1 = the host computer) tick the MIDI tracks it plays, enter
   the input delays (or use **Sync**), then press **Save**.
   The host plays track *n* in its window Player*n*. A client plays its 1st ticked track in its Player1, its 2nd in
   Player2, and so on.
4. Select a song and press **F1** (or the round Play button). Every machine starts together about 2 s later (4 s
   when a player's clock has to be sampled first, see *Timing between computers*). **F3** stops
   everyone and resets the instruments' octaves. **F2** pauses/resumes the host's own playback.
   A client can press **F1** to have the host play the client's selected song, or **F3** (or type `stop` in the chat) to stop.
5. **Sync** needs Guild Wars 2 open on every machine. After you press OK on its prompt, GW2MM brings the game window
   (*Player1*, or else any Guild Wars 2 window) to the front, waits briefly, then plays the 10 test notes.
   A note is only sent while the game is in front. If the game isn't open, Sync says so and keeps the old value.
   The host measures itself first; then each connected player gets the Sync prompt (the chat says who was asked).
   The prompt comes to the front over the game, with a sound, even when GW2MM is minimized. If the host's own test
   fails, the players are still asked.

**Settings → Dusk theme** switches to a dark look (stored as `Theme,Dusk` in Config.txt). It applies the next time
GW2MM starts.

### Adding songs

**New Song** takes a MIDI file (`.mid`) or a MuseScore score (`.mscz`). A score is first exported to MIDI by
MuseScore 4, which must be installed. GW2MM looks for `MuseScore4.exe` in the usual install folders. If it isn't
there, GW2MM asks you to select it once and remembers it (Config.txt `MuseScore`). The exported MIDI is saved
in `Midi\` like any other song.

### Chat

Every message shows the sender's name (`bob > hi`; the host is `Host`). The host passes each client's message on
to all the other clients. Chat is only ever displayed. The one command is `stop` (the whole message, any case),
which stops the song for everyone like **F3**. Text that only contains "stop" ("don't stop") is ordinary chat.
Joins appear as "bob joined as player 2." Internal network messages are written to `GW2MM.log`, not to the chat box.

### One-player songs

A song whose **Players** value is 1 is played on one instrument with the notes of every track. The players with any
track ticked in Port Mapping play it in their Player1 window; if nobody has a track ticked, the host does. Solo play
uses Player1 too. Keys are the same as in multiplayer: 1–8 for naturals, F1–F5 for sharps, 9 / 0 for
octave changes.

A chord whose notes lie in more than one octave is played octave by octave. Its notes in one octave are pressed
together, then the octave changes, then the next group follows OctaveDelay later. The order needs the fewest changes
(from the lowest octave on a tie). After every octave change the next note waits at least OctaveDelay (100 ms
by default). The changes before a chord go into the gap before it when there is room. When there is not, the following
notes are delayed just enough and the song catches up at the next rest. Notes outside C3–C6 are moved by whole
octaves into range.

### Octave changes and OctaveDelay

The game applies an octave change only after a round trip to the server. A note played sooner sounds in the old
octave. Octave changes are placed exactly as the original scripts did (`AddSwaps` / `OpportunisticSwaps`): when a
note needs another octave and the gap since the previous note (in MIDI ticks) is at least *OctaveDelay*, the change is
pressed on its own at the middle of that gap; otherwise it is pressed together with the note. Notes are never moved,
so the rhythm and the sync between players are unchanged. OctaveDelay is 100 ms unless set (Settings → Octave delay).
Set it to about twice your in-game ping.

**Octave reset.** Every computer keeps track of each game window's octave (low, middle or high) through the songs,
counting the octave changes GW2MM adds; the 9 / 0 keys of MIDI notes 96 / 97 are played but not counted. A window
is taken to be in the middle octave unless that says otherwise. When a song finishes or is stopped (and on F4), each
window that is not in the middle octave gets the one key that brings it back (9 from high, 0 from low), OctaveDelay
after the song's last key; a window already in the middle octave gets nothing. If you change an octave by hand in
the game, GW2MM can't know.

**Settings → Live octave timer** (test option, off by default; `LiveOctaveTimer,1` in Config.txt) replaces the
opportunistic swaps above. Each octave change stays in its chord, and while the song plays each game window has a
timer that starts whenever its octave changes and counts in 10 ms steps. Once it reaches OctaveDelay, the window's
next chord (filler skipped) is checked at every step: if it starts with an octave change, that change is pressed
right then, taken out of the chord, and the timer starts again. The host's setting applies to its players too; each
computer times with its own OctaveDelay. One-player songs keep their own timing.

**Settings → Reset octaves between songs** (on by default; `OctaveResetBetweenSongs,0` in Config.txt turns it off)
skips the reset when a song finishes (Stop and F4 still reset). The host's setting applies to its players too. Songs assume the instrument
starts in the middle octave, so with it off a song that ends in another octave starts the next one there.

### Playing solo (client, not connected)

When the client is not connected to a host (it could not connect, or the host went away), Play / F1 plays the song
or playlist on this computer's own game windows instead of asking a host. The song's tracks that contain notes go,
in order, to the windows named Player1…Player8: the first track to Player1, the second to Player2, and so on. This
is the same arrangement a host gives a client assigned all of them. Every part is played whichever windows are open;
keys for a window that isn't open are simply not sent. **F3** stops and resets octaves, **F5** (or **Next**)
skips to the next playlist song.
Once the client is connected, the host takes over again.

**Settings → Offset** (`Offset` in Config.txt, 0 = off) staggers the windows when you play alone (a client that is not
connected, or a host with no players connected) so no two press a key at the same instant. With *n* windows playing, each starts Offset ÷ *n* ms (rounded down) after the one
before: Offset 50 with 3 windows puts Player2 16 ms and Player3 32 ms behind Player1. Each window keeps its own
timing. One window, and playing with others, are never offset.

### Playlists

The **Songs | Playlists** switch above the list chooses what Play (F1) plays. The search box above the songs (Ctrl+F)
shows only the songs whose names contain every word typed (Esc clears it). Double-click or Enter plays a song; Delete
deletes it (after asking).

* **Make a playlist:** Playlists → **⋯ → New playlist…** (or pick **New playlist…** in the playlist list), then
  **Add songs…** (tick songs, filter by name). Or right-click a song on the Songs side → **Add to playlist**.
* **Arrange it:** select songs and use the **↑ / ↓** buttons (or Alt+Up / Alt+Down), drag them, or the bin button
  (Delete key). The **⋯** menu renames or deletes the playlist; **Shuffle** and **Repeat** are the two toggle buttons
  beside the playlist name (also in the ⋯ menu).
* **Play it:** select the song to start from (or none, to start at the top) and press Play, double-click a song, or
  press Enter. With Shuffle, the chosen song plays first and the rest in random order.
* **While it plays:** everyone sees "♪ Now playing: … (2/5 in …)" in the chat and the current song is marked ♪.
  After each song (when every player's last note has rung out) all octaves are reset and the next song starts. The
  host's **Next** button or **F5** skips ahead. **F3**, typing `stop` in the chat, or playing any other song or
  playlist ends it.
* **From a client:** a client plays its own playlists the same way. Its songs are sent to the host, which plays them
  for everyone, exactly like a single song requested with F1.

Playlists are plain text files in the `Playlists` folder next to the exe (one song name per line; **⋯ →
Open Playlists folder**), so they are easy to copy to another computer. Songs that are not in the song list are
shown in grey and skipped. Deleting a song removes it from playlists, and renaming one in the Data Editor renames it
there too.

### Hotkeys

Defaults (change them in **Settings → Keybinds → Edit…**; they are saved in Config.txt as
`Key_<action>,<keys>` lines):

Host and client have the same default keys. The round **Pause** and **Stop** buttons beside the selected song do what F2 and F3 do.

| Keys | Host | Client |
|---|---|---|
| F1 | Play the selected song (on the Playlists tab: the playlist) | Same; when connected, asks the host to play it for everyone |
| F2 | Pause/resume this computer's playback (alone: resumes where it paused; with players: catches up to them) | Pause/resume (not connected): resumes where it paused |
| F3 | Stop everyone, reset octaves | Stop, reset octaves; when connected, stops the song for everyone (like typing `stop`) |
| F4 | Reset octaves, exit | Reset octaves, exit |
| F5 | Skip to the next song of the playlist | Same (not connected) |
| Shift+F1 … Shift+F8 | Rename the active Guild Wars 2 window to Player1 … Player8 | Same |
| F10 / F11 | Tempo −1 % / +1 % for the song playing, while no players are connected | Same, while not connected |
| Esc | Close (main window) | – |

The tempo keys speed up or slow down the rest of the current song (the chat shows the new tempo); each new song
starts at 100%. They never apply while playing with others, whose parts must follow the host's timing. Running
host and client on the same PC, the second one can't use keys the first already has: change them in Keybinds.

Hotkeys are system-wide: while GW2MM runs, other programs (including the game) don't receive those keys.

## Timing between computers

Each machine plays its part from its own clock, so the host needs to know how every player's clock compares with
its own. While connected, each client exchanges a small UDP message with the host once a second, on the host's port
(27015). Each exchange records four times (sent and received at each end, read right at the network socket), so
waiting inside either program doesn't count as network delay. The host keeps the smallest delay seen in each
direction over the last minute: queueing only ever adds delay, so a busy upload, a video call, Wi-Fi bursts or a
resent packet on one side don't shift the result, and how fast the two clocks drift apart is measured and allowed
for. A song then starts without sampling clocks first.

A player whose UDP messages don't get through (a firewall or router blocking UDP, or an older GW2MM) has their clock
sampled over TCP before the song, as before; the song then starts about 4 s after Play instead of 2. When a
player's connection is very unsteady, the chat says so once (their notes may lag in the game too). `GW2MM.log` has
a `Clock,Socket<n>,udp,…` or `…,tcp,…` line for each player and song.

## Port opening (UPnP)

When the host starts, it asks the router to forward TCP port 27015 to this computer using UPnP, so you don't have
to set up port forwarding by hand. It also asks for UDP 27015, for the clock exchanges (without it players' clocks
are sampled over TCP instead). Forwarding by hand? Forward both TCP and UDP 27015. The chat box shows the result:

* *Opened port 27015 on your router (UPnP). Players connect to 203.0.113.7.* Give that address to your players.
* *…already forwarded to this computer.* An existing forward (for example one you made by hand) is used as it is
  and is never removed.
* *…your router already forwards it to another device (192.168.1.50).* Change or delete that forward on the router.
* *…no router answered.* UPnP is off on the router (or not supported). Turn it on, or forward the port by hand.
* *…the router itself is behind another network* or *…the router's internet address is not your public address.*
  Your provider (carrier-grade NAT) or a second router sits in front of yours, so the forward never sees outside
  players. Forward the port on the outer router too, ask your provider for a public IP address, or use a VPN such
  as Tailscale.

The "Players connect to" address comes from asking `api.ipify.org` for this network's public address. (The router's
own answer is wrong behind another NAT.) If that lookup fails, the router's answer is shown instead.

**Windows Firewall** is a separate gate from the router. When the host starts, it checks whether the firewall lets
other computers reach it on the current network. If not, it offers to add rules ("GW2MM Host", TCP and UDP 27015, all
networks) and Windows asks for administrator permission. This matters because the firewall prompt Windows shows the
first time ticks *Private* networks only, and home networks are often classed as *Public*. Pressing *Cancel* on
that prompt makes Windows block the program outright. Third-party firewalls or antivirus suites are not checked.

**Still can't connect?** Players outside your network must use the public address, and port 27015 must reach this
computer. Testing your own public address from inside your network often fails even when everything works (many
routers don't support it). `GW2MM.log` records the router, the forwarded address and the firewall result.

The forward GW2MM added is removed when the host closes. If the host crashes, the forward is cleaned up the next time
it closes normally. Turn this off in Settings (the gear on the banner) → *Open port automatically (UPnP)*, stored as `OpenPort,0` in
Config.txt. Clients never need an open port.

## Updates

**Version numbers** come from CI (shown in the About window, the ⓘ button on the banner): each build is numbered after the newest
release, e.g. `2.0.6-pr.3` for pull request 3 while v2.0.5 is the latest. Merging a pull request into `main`
publishes the next release (v2.0.6, v2.0.7, …) automatically. A release can also be made by hand: Actions →
"GW2MM (.NET port)" → Run workflow, with a version such as `2.1.0` or `next`.

On launch the app checks the repository's latest GitHub release in the background. If it is newer than the
running version, it asks whether to install it. On "Yes" it downloads the executable for its role (checked against
the release's size and SHA-256), replaces itself, and restarts with the same arguments. If the exe's folder is not
writable, or the release has no matching file, it offers to open the release page instead. A failed check (offline,
GitHub unreachable) is silent; the details go to `GW2MM.log`. Turn it off in Settings → *Check for updates
on launch*, stored as `CheckForUpdates,0` in Config.txt. Drafts and pre-releases are never offered.

**Publishing a release:** use any one of these. Each runs the tests, builds `GW2MM-Host.exe` and `GW2MM-Client.exe`
stamped with the version, and attaches them to the GitHub release:

* On GitHub: **Releases → Draft a new release**, type a new tag such as `v2.0.1`, then **Publish release**.
  The executables are added to it a few minutes later.
* On GitHub: **Actions → GW2MM (.NET port) → Run workflow**, enter the version (e.g. `2.0.1`).
* From git: `git tag v2.0.1` then `git push origin v2.0.1`.

Every installed copy older than that version then offers the update on its next launch. Also bump `<Version>` in
`Directory.Build.props` so local builds report the new version.

## Building and testing

Requires the .NET 8 SDK.

```
dotnet test GW2MM.sln                                   # 200+ tests (Linux or Windows)
dotnet publish src/GW2MM.Host   -c Release -o publish   # -> publish/GW2MM-Host.exe
dotnet publish src/GW2MM.Client -c Release -o publish   # -> publish/GW2MM-Client.exe
```

The Windows projects build on Linux as well (`EnableWindowsTargeting`), provided the SDK includes the
WindowsDesktop MSBuild SDK; the Ubuntu-packaged SDK does not, so use Microsoft's SDK build there. The GitHub
workflow `.github/workflows/gw2mm.yml` builds, tests and publishes both executables on Windows.

The golden test data under `tests/GW2MM.Tests/Golden` was produced by running the **original** AutoHotkey code and
the bundled `Midicsv.exe` under Wine; see [tools/oracle/README.md](tools/oracle/README.md) to regenerate it.

## Project layout

```
src/GW2MM.Core      platform-independent logic: arrangement pipeline, playback loop, protocol, sessions, files
src/GW2MM.Windows   WinForms UI and Win32 interop (key posting, hotkeys, window renaming, audio meter)
src/GW2MM.Host      GW2MM-Host.exe entry point
src/GW2MM.Client    GW2MM-Client.exe entry point
tests/GW2MM.Tests   unit, golden (vs. original) and loopback integration tests
tools/oracle        harnesses that run the original AHK code to produce the golden data
docs/               ARCHITECTURE.md, PORTING_NOTES.md
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how playback works.
