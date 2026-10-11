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
| Data Editor | `CsvEditor.ahk` | built in (Tool Kit button) |

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
   port and which address players connect to. Each client starts `GW2MM-Client.exe`, enters a user name and the
   host's IP address (remembered in Config.txt as `HostAddress` and filled in next time), and gets the next free
   player slot (Ip2–Ip5).
3. On the host, open **Port**. For each slot (Ip1 = the host computer) tick the MIDI tracks it plays, enter
   the input delays (or use **Sync**), then press **Save**.
   The host plays track *n* in its window Player*n*. A client plays its 1st ticked track in its Player1, its 2nd in
   Player2, and so on.
4. Select a song and press **F1** (or the Play icon). Every machine starts together about 4 s later. **F3** stops
   everyone and resets the instruments' octaves. **F2** pauses/resumes the host's own playback.
   A client can press **F11** to have the host play the client's selected song, or type `stop` in the chat to stop.
5. **Sync** needs Guild Wars 2 open on every machine. After you press OK on its prompt, GW2MM brings the game window
   (*Player1*, or else any Guild Wars 2 window) to the front, waits briefly, then plays the 10 test notes.
   A note is only sent while the game is in front. If the game isn't open, Sync says so and keeps the old value.

### Chat

Every message shows the sender's name (`bob > hi`; the host is `Host`). The host passes each client's message on
to all the other clients. Chat is only ever displayed. The one command is `stop` (the whole message, any case),
which stops the song for everyone like **F3**. Text that only contains "stop" ("don't stop") is ordinary chat.
Joins appear as "bob joined as player 2." Internal network messages are written to `GW2MM.log`, not to the chat box.

### Hotkeys

Defaults (change them in **Controls → Change keybinds…** or **⚙ → Keybinds…**; they are saved in Config.txt as
`Key_<action>,<keys>` lines):

| Keys | Host | Client |
|---|---|---|
| F1 | Play the selected song | – |
| F2 | Pause/resume this computer's playback | – |
| F3 | Stop everyone, reset octaves | – |
| F4 | Reset octaves, exit | Reset octaves, exit |
| Shift+F1 … Shift+F8 | Rename the active Guild Wars 2 window to Player1 … Player8 | same |
| F10 / F11 | Tempo ±1 % (no audible effect, as in the original) | F11: ask the host to play the selected song |
| Esc | Close (main window) | – |

Hotkeys are system-wide: while GW2MM runs, other programs (including the game) don't receive those keys.

## Port opening (UPnP)

When the host starts, it asks the router to forward TCP port 27015 to this computer using UPnP, so you don't have
to set up port forwarding by hand. The chat box shows the result:

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
other computers reach it on the current network. If not, it offers to add a rule ("GW2MM Host", TCP 27015, all
networks) and Windows asks for administrator permission. This matters because the firewall prompt Windows shows the
first time ticks *Private* networks only, and home networks are often classed as *Public*. Pressing *Cancel* on
that prompt makes Windows block the program outright. Third-party firewalls or antivirus suites are not checked.

**Still can't connect?** Players outside your network must use the public address, and port 27015 must reach this
computer. Testing your own public address from inside your network often fails even when everything works (many
routers don't support it). `GW2MM.log` records the router, the forwarded address and the firewall result.

The forward GW2MM added is removed when the host closes. If the host crashes, the forward is cleaned up the next time
it closes normally. Turn this off in Settings (⚙) → *Auto-open port (UPnP)?*, stored as `OpenPort,0` in
Config.txt. Clients never need an open port.

## Updates

On launch the app checks the repository's latest GitHub release in the background. If it is newer than the
running version, it asks whether to install it. On "Yes" it downloads the executable for its role (checked against
the release's size and SHA-256), replaces itself, and restarts with the same arguments. If the exe's folder is not
writable, or the release has no matching file, it offers to open the release page instead. A failed check (offline,
GitHub unreachable) is silent; the details go to `GW2MM.log`. Turn it off in Settings (⚙) → *Check for updates
on launch?*, stored as `CheckForUpdates,0` in Config.txt. Drafts and pre-releases are never offered.

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
