# Porting notes

The original AHK scripts are the specification. This file records the original behaviour, what was preserved,
what was deliberately changed (and why), and how each part was verified.

## 1. Behaviour preserved exactly (including quirks)

These were kept even where they look odd, because they decide which keys are pressed and when. Unless noted,
they are covered by the golden tests against the original code.

**Song parsing**
* Only `Note_on_c` lines with velocity > 0 become notes. Note-offs (including velocity-0 note-ons) do not.
* The note letter comes from field 5 of the *most recent line of any type* whose field 5 is a mapped MIDI number
  (48–84, 96, 97). A note outside that range therefore repeats the previous letter, and a controller number in a
  `Control_c` line also counts. Fields are split on every comma, even inside quoted text.
* Before the first recorded note only the first `Tempo` event is used; after it, every one is.
* The file's PPQ is ignored: one tick lasts µs-per-quarter / 500 000 ms (1 ms at 120 bpm), scaled by the
  Tempo Modifier (−0.05 per step). Tracks above 25 are dropped; only tracks 1–8 can be mapped.

**Arrangement**
* `AddSwaps` / `OpportunisticSwaps` rules, the per-octave key sets and the fixed note order inside a chord.
  Notes outside the instrument's current three-octave window are dropped.
* *OctaveDelay* is compared with the gap in **MIDI ticks** (despite its "ms" description). When it is missing
  from Config.txt every swap is split, as the original's blank value did.
* The filler timeline, the trailing elements left by AHK `Sort`/`StrSplit`, and the order in which tempo
  markers land (`Object.Insert` shifting). For example, a marker goes in front of a note group one tick earlier.
* The running tempo carries over from one player slot to the next while computing delays.
* A part with three or fewer elements is not played or sent.
* The host never presses its first element (normally the tempo marker); a client presses its first element on
  arrival.
* The host plays track *n* in window Player*n*; a client plays its *k*-th ticked track in Player*k*.

**Session, controls and files**
* 4 s lead time, 2 s clock-collection wait, start-time formula, and latency = slot delay − host delay.
* Stop: the host resets octaves (9, 9, 0 to Player1–5, 150 ms apart) and shows its window; a client resets with
  *OctaveDelay* ms pauses. F4 resets octaves and exits. A song ending naturally does not reset octaves.
* F2 pauses only the host; on resume it catches up overdue notes in a burst, as the paused AHK timer did.
* F10/F11 on the host change a tempo value that is recalculated at the next Play, so they have no audible effect.
* A client can stop the song by typing "stop" in the chat (it has no stop hotkey). The whole message must be "stop"
  (any case); see change 2.
* Client slots are assigned in order (2–5) and reused after a disconnect. Extra clients can chat but get no slot.
* A disconnect shows "The client disconnected!" and commits and hides the Port Mapping window.
* Edits in the Port Mapping window take effect only on Save (or on a disconnect). Sync and client reports
  update input delays immediately.
* `Data.csv` lookups use the original flat field list. Duplicate entries are listed twice and their tempo
  modifiers add up. The ".csv" suffix is matched case-sensitively. Delete removes every line containing the name.
  The Data Editor's rename check is case-insensitive.
* `Config.txt` is read as a flat field list and rewritten line by line, keeping comments. Files keep their
  encoding (ANSI unless they have a BOM). Window titles, texts, layout, artwork and `Version 1.6` are unchanged.
* The host closes on Esc; the client does not.

## 2. Deliberate changes

| # | Original behaviour | New behaviour | Reason |
|---|---|---|---|
| 1 | AHKsock frames with a 16-bit length | JSON lines (`Net/Messages.cs`) | Songs over ~32 000 characters overflowed the length field and corrupted the stream. The new protocol is **not compatible** with the AHK scripts. |
| 2 | Any received text containing `Stop`, `Play`, `StartSong`, `Sync`, `Socket`, `Note_1`… triggered that command, even in chat. Clients never saw each other's messages | Commands are typed messages. Chat is only displayed. The host passes each message on to every other client, labelled with the sender's name. The one chat command is a message that is exactly "stop" (any case), which stops the song like F3 | Ordinary chat could start songs, stop them, or corrupt state. |
| 3 | Client songs were sent as `Play;` chunks and cut at the first `;` | The whole CSV is sent in one message | Lines containing `;` (e.g. in titles) were truncated. |
| 4 | A blank input delay or `Pings[1]` (e.g. before Sync, or after Port Mapping Save) made every note fire at once | Blank counts as 0. Latency = slot delay − host (Ip1) delay | Demonstrable bug. Relative timing between players is unchanged from the working case (Sync then play). |
| 5 | A client that did not report its clock within 2 s got times based on a stale value, so its notes fired at once or never | That client's part is not sent and a warning is shown | Demonstrable bug. |
| 6 | Stop during the 2 s preparation still sent the song to clients. Play during a song made the old timer run on the new data | Stop cancels preparation. Play replaces the current song cleanly | Race conditions producing garbled playback. |
| 7 | After a client-requested song, the host's F1 played nothing until F3 or the Play button (stale `FromClient`) | F1 and the Play button always play the host's selected song | Demonstrable bug. |
| 8 | The second song in a session could include two stale notes from a longer previous song, and the carried-over note letter persisted across songs | Every song is parsed from fresh state | Demonstrable bug (state leakage). |
| 9 | Timing came from `A_TickCount` (10–16 ms steps), one element per timer tick | Sub-millisecond clock. Each event fires at its due time, at least 1 ms after the previous key press | Less jitter. Order and catch-up behaviour are the same (tested against an emulation of the AHK timer). |
| 10 | Settings → Save rewrote Config.txt and **restarted the script**, dropping all connections. A missing line could never be added | Values are applied live. Missing lines are appended | Avoids disconnecting everyone. |
| 11 | The host asked for a "peer address" at start-up and tried to connect to it. The client also listened on 27015 | The host just listens. Clients only connect | Those connections were never used for playback. |
| 12 | Ping pooled samples across clients and button presses | Per-client samples, matched by probe id | The displayed value was wrong for every client after the first. |
| 13 | Sync waited forever for sound after each press and froze the client's network handling. It pressed "1" in whatever window had focus (usually GW2MM itself, right after its "press OK" prompt) | Sync requires Guild Wars 2 to be open and brings it to the front before the first press; a press is only sent while the game is in front. Each press times out after 3 s. If nothing is heard, or the game isn't open, the host keeps its old value (the client sends blank). Networking keeps running | The app could hang forever, and the test measured nothing unless the user clicked into the game in time. |
| 14 | MIDI conversion via a generated `.bat` running Midicsv.exe plus a blind 2 s wait | In-process converter with byte-identical output | Removes the race and the temp file. The `Csv`/`Midi` folders are created if missing. |
| 15 | New Song accepted an empty title or no file, leaving a broken Data.csv entry. A file outside `Midi\` whose name matched one inside was *moved* | Title and file are required. Files outside `Midi\` are copied, files inside are renamed | Prevents broken entries and surprising file moves. |
| 16 | Delete with no song selected offered to delete "" | Does nothing | Guard. |
| 17 | The chat box echoed every raw protocol message (including whole songs), and every line was labelled "Peer" | The chat box shows only chat, labelled with the sender's name (`bob > hi`, `Host > hi`), plus join notices. Protocol messages go to `GW2MM.log` (`Event: Tick_Count`, `Event: Note_1 (523 notes)`) | Readability. |
| 18 | The client's Controls window said F1 plays (the hotkey is F11). The host's omitted F2 | Texts corrected | Documentation bug. |
| 19 | `ConnectConfig.txt` "Name" prompt (value never used) | Dropped | Dead code. Neither shipped folder contains the file. |
| 20 | Realtime process priority; AHK keyboard-hook hotkeys | High priority; `RegisterHotKey` (no auto-repeat). A key already taken by another program is reported in the chat box | Realtime priority can starve the system. |
| 21 | Files relative to the working directory | Relative to the exe folder (or `--data`) | Fragile paths. |
| 22 | Line breaks in descriptions were only partly removed | All line breaks are replaced by spaces | Data.csv consistency. |
| 23 | Midicsv read past the end of malformed tracks (no End_track or truncated), printing uninitialised memory | Conversion stops at the end of the chunk | Undefined behaviour cannot be reproduced. Well-formed files are byte-identical. |
| 24 | The Data Editor wrote `col1,col2,col3,col4` and `name,players,tempo,description` into a missing Data.csv, and showed them as rows | A missing Data.csv is created empty. Those two lines are removed when found | They are not songs. |
| 25 | Each client's clock was sampled once per song, so a lag spike at that moment put that player early or late for the whole song | 8 probes, 50 ms apart, within the same 2 s wait; the reply with the shortest round trip is used | A single unlucky sample could offset a player by tens of ms. |
| 26 | Octave changes (`AddSwaps` / `OpportunisticSwaps`): a jump straight between the low and high octave got no change and its notes were dropped; OctaveDelay was compared with MIDI ticks, so it meant different times at different tempos; a change with room was put at the gap's midpoint (only half the gap ahead of its note), and one without room was pressed together with its note | Two changes for a low ↔ high jump; OctaveDelay is milliseconds at the song's tempo and tempo modifier; each change is at least OctaveDelay ahead of the next press when the gap allows (still the midpoint when there is plenty of room), otherwise as early as possible after the previous chord | Notes sounded in the wrong octave or went missing. The original behaviour is kept as `OctaveSwapMode.Original` and still matches the oracle byte for byte. |
| 27 | The host's octave reset always waited 150 ms between 9, 9, 0, whatever OctaveDelay said; a client with a blank OctaveDelay pressed all three at once | Every octave reset (stop, exit, between playlist songs, solo) waits OctaveDelay between presses, 150 ms when it is blank | A reset faster than the game applies octave changes leaves instruments in the wrong octave. |
| 28 | The CSV parser kept only the first `Tempo` event found before the first note in the file. Most MIDI files keep their tempo changes in a first track without notes, which comes first in the file, so every tempo change but the opening one was ignored and the song played at its opening tempo throughout. This affected every song | All tempo changes are kept, in time order (`CsvSong.Parse`); both arrangements follow them | Example: Ave Maria (Bach/Gounod) has 187 tempo changes and played in 224 s instead of 165 s. The original rule is kept for the oracle tests (`allTempoChanges: false`). |

Changes 5–8 were found by reading the original code (the oracle runs each case in a fresh process, so it cannot
show cross-song or concurrency effects). The corrected behaviour is covered by the integration tests.

Dead code that was not ported: the client's legacy single-player engine (`OnePlayer`, `TrackPlayer`,
`DupeSeparation`, …); the server's `SongPlayer2–5` / `SongPlayerHalf3` and the `Note_2`/`Rest_2`/`*_Half` /
`Return_Start_*` messages; and `WinSet Region/TransColor`, which had no window to act on. None of these was
reachable from any hotkey, button or message.

## 3. Added features

* **Update check on launch** (not in the original). It looks up the latest GitHub release and offers to install a
  newer one; see the README. It is on by default and can be switched off in Settings (`CheckForUpdates` in
  Config.txt). The only network request it makes is to `api.github.com` and the release download.

* **Automatic port opening (UPnP)** (not in the original, which needed a manual port forward). The host asks the
  router to forward TCP 27015 to itself on start-up and removes that forward on exit. It never touches a forward
  it did not create. On by default; switch it off in Settings (`OpenPort` in Config.txt). Code: `Net/Upnp.cs`
  (SSDP discovery and the IGD SOAP calls) and `Net/PortOpener.cs`. Tests use a fake router (`UpnpTests`).

* **No preset host address.** The original client's IP prompt was pre-filled with the author's address
  (`97.118.35.47`). The prompt now offers the address last entered (saved as `HostAddress` in Config.txt), starts
  empty the first time, and asks again if left blank.

* **Playlists** (not in the original). A Playlists tab next to the song list creates, edits and plays playlists
  on host and client (a client's playlist is played by the host for everyone, like F11). The host plays the songs in
  order (optionally shuffled or repeated), resets octaves between songs, and announces each song in the chat. Next
  is F5 on the host. Stored as `Playlists\<name>.txt`. Single songs requested by a client are now announced by name too
  (`PlayRequest` carries the name).

* **Solo play on a client** (not in the original). While the client is not connected, Play / F11 plays songs and
  playlists on its own Player windows, each track with notes on the next open window. Ctrl+F3 stops, Ctrl+F5 skips.

* **One-player songs on one instrument.** A song with Players = 1 plays all its tracks on one instrument
  (`Arrangement/SingleInstrument`). A chord spanning octaves is played octave by octave, with OctaveDelay after each
  octave change; notes are delayed only when the song leaves no room for that. The original client had an
  unfinished engine for this (`OnePlayer`, never called); in the original every song went through the
  multiplayer arrangement and Players only labelled the song list.

* **Offset for solo play** (not in the original). Client Settings → *Offset (ms)* staggers the windows in solo play:
  with n windows playing, each starts Offset ÷ n ms (rounded down) after the one before, so no two windows press at
  the same instant. Never applied to host or client playback.

* **Optional launch prompts (client).** Settings → *Ask name & IP on launch?* (`AskOnLaunch`) skips the name and
  host address prompts and uses the saved values.

* **Configurable hotkeys.** Every hotkey can be changed in a Keybinds window (Controls → Change keybinds…, or
  Settings → Keybinds…). Changes take effect immediately and are stored in Config.txt as `Key_<action>,<keys>`.
  The window-naming defaults changed on request from F5–F9 (host also Shift+F6–F8) to **Shift+F1–Shift+F8 for
  Player1–Player8**, now on the client too (it could only name Player1–5 before). The other defaults are unchanged.
* **Redesigned dialogs** (New Song, Data Editor, Controls, Port Mapping). Same fields and the same effects on
  files and playback, with a cleaner layout:
  * *Port Mapping* shows each slot's player: "You (host)", the name a client announced (live as players join and
    leave) or "Open slot". Save applies; **Cancel or closing now reverts** unsaved ticks/delays (the original kept
    them in the hidden window without applying them). A client disconnecting still applies the window's values.
    Ping is shown read-only.
  * *New Song* shows the help inline instead of "?" pop-ups, accepts a dragged .mid file and pre-fills the title
    from the file name. Players and Tempo are number boxes, with a live "10% faster"-style hint.
  * *Data Editor* is an editable grid with Save/Cancel and a "discard changes?" prompt. Only edited lines are
    rewritten (as before, in "Name,Players,Tempo,Description" form), and renames still move the song's files.
  * *Controls* lists the hotkeys as key caps.

## 4. Verification

| Area | How | Result |
|---|---|---|
| `Combine`, `CombineFlags`, `AddSwaps`, `OpportunisticSwaps`, `RemoveOutliers` | 4 000 random vectors through the original `Functions.ahk`/`ConnectButtons.ahk` (AutoHotkey 1.1.37 under Wine) vs. the port | identical |
| Whole host pipeline (parse → arrange → delays → absolute times → latencies → which clients receive a part) | 70 cases (real songs, plus synthetic ones with tempo changes, out-of-range notes, quoted commas, 1–10 tracks, random port mappings, OctaveDelay, tempo modifiers, client-originated songs) through the original `SongPlayer` | identical |
| MIDI → CSV | 81 generated files (every event type, running status, SysEx, odd text bytes, SMPTE divisions) + edge cases + `Yeah Right.mid` vs. the bundled `Midicsv.exe` under Wine | byte-identical (except the 3 undefined cases in change 23) |
| Playback loop index semantics and catch-up | Compared with a literal emulation of the AHK `SongPlayer1` timer at 1 / 10 / 15.6 ms ticks | same keys, same order, never early, never later than the original |
| Host ↔ client sync, latency compensation, Stop, pause, replace, stop-during-prep, missing clock reply, slots, chat, typing, ping, sync, client play request | Loopback TCP integration tests with each "machine" on its own clock | pass (notes line up within a few ms) |
| Files (Data.csv, Csv, Midi, Config.txt, Data Editor, encodings) | Unit tests | pass |
| Real executable | `GW2MM-Host.exe` (self-contained build) run under Wine: window renders, library loads, a TCP client gets slot 2, chat and a play request reach the host | works |

**Not verified here** (needs Windows with Guild Wars 2): key delivery into the real game windows, the F-key
hotkeys, renaming game windows, the Core Audio peak meter used by Sync, and actual synchronisation across real
machines over the internet. The updater's release parsing, version comparison, download checks and file swap are
unit-tested. Replacing the running exe on real Windows and a live GitHub release have not been exercised yet;
the first tagged release will do that. The code for these follows the original's Win32 calls one-to-one (`PostMessage`
WM_KEYDOWN/UP with the same lParam, `SetWindowText`, `IAudioMeterInformation::GetPeakValue`, `SendInput "1"`).
