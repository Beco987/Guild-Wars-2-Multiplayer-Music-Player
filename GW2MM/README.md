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
3. Allow `GW2MM-Host.exe` through the Windows firewall (TCP port **27015**, as before) and forward that port
   on your router if clients connect over the internet.

Host and clients must all run the new version, because the network protocol changed (see porting notes).

## Using it

The workflow is the one described in the [GW2MM Guide](https://docs.google.com/document/d/1Ek9sHKd0PQw9vmecujCG7tVtNIkKMtPnIPPEBio5ZoU/edit).

1. Start Guild Wars 2 (Gw2Launcher for several accounts). Focus each game window and press **F5–F9**
   (host also **Shift+F6–F8**) to rename it to *Player1…Player8*.
2. Start `GW2MM-Host.exe`. It listens for clients straight away. Each client starts `GW2MM-Client.exe`, enters a
   user name and the host's IP address, and gets the next free player slot (Ip2–Ip5).
3. On the host, open **Port**. For each slot (Ip1 = the host computer) tick the MIDI tracks it plays, enter
   the input delays (or use **Sync**), then press **Save**.
   The host plays track *n* in its window Player*n*. A client plays its 1st ticked track in its Player1, its 2nd in
   Player2, and so on.
4. Select a song and press **F1** (or the Play icon). Every machine starts together about 4 s later. **F3** stops
   everyone and resets the instruments' octaves. **F2** pauses/resumes the host's own playback.
   A client can press **F11** to have the host play the client's selected song, or type "Stop" in the chat to stop.

### Hotkeys

| Key | Host | Client |
|---|---|---|
| F1 | Play the selected song | – |
| F2 | Pause/resume this computer's playback | – |
| F3 | Stop everyone, reset octaves | – |
| F4 | Reset octaves, exit | Reset octaves, exit |
| F5–F9 | Rename the active Guild Wars 2 window to Player1–Player5 | same |
| Shift+F6–F8 | Rename to Player6–Player8 | – |
| F10 / F11 | Tempo ±1 % (no audible effect, as in the original) | F11: ask the host to play the selected song |
| Esc | Close (main window) | – |

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
