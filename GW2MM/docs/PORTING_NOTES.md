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
* A client can stop the song by typing a chat message containing "Stop" (it has no stop hotkey).
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
| 2 | Any received text containing `Stop`, `Play`, `StartSong`, `Sync`, `Socket`, `Note_1`… triggered that command, even in chat | Commands are typed messages; only the chat "Stop" convention is kept | Ordinary chat could start songs or corrupt state. |
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
| 13 | Sync waited forever for sound after each press and froze the client's network handling | Each press times out after 3 s. If nothing is heard the host keeps its old value (the client sends blank). Networking keeps running | The app could hang forever. |
| 14 | MIDI conversion via a generated `.bat` running Midicsv.exe plus a blind 2 s wait | In-process converter with byte-identical output | Removes the race and the temp file. The `Csv`/`Midi` folders are created if missing. |
| 15 | New Song accepted an empty title or no file, leaving a broken Data.csv entry. A file outside `Midi\` whose name matched one inside was *moved* | Title and file are required. Files outside `Midi\` are copied, files inside are renamed | Prevents broken entries and surprising file moves. |
| 16 | Delete with no song selected offered to delete "" | Does nothing | Guard. |
| 17 | The chat box echoed every raw protocol message (including whole songs) | It shows one short line per message (`Peer > Tick_Count`, `Peer > Note_1 (523 notes)`) | Readability. |
| 18 | The client's Controls window said F1 plays (the hotkey is F11). The host's omitted F2 | Texts corrected | Documentation bug. |
| 19 | `ConnectConfig.txt` "Name" prompt (value never used) | Dropped | Dead code. Neither shipped folder contains the file. |
| 20 | Realtime process priority; AHK keyboard-hook hotkeys | High priority; `RegisterHotKey` (no auto-repeat). A key already taken by another program is reported in the chat box | Realtime priority can starve the system. |
| 21 | Files relative to the working directory | Relative to the exe folder (or `--data`) | Fragile paths. |
| 22 | Line breaks in descriptions were only partly removed | All line breaks are replaced by spaces | Data.csv consistency. |
| 23 | Midicsv read past the end of malformed tracks (no End_track or truncated), printing uninitialised memory | Conversion stops at the end of the chunk | Undefined behaviour cannot be reproduced. Well-formed files are byte-identical. |

Changes 5–8 were found by reading the original code (the oracle runs each case in a fresh process, so it cannot
show cross-song or concurrency effects). The corrected behaviour is covered by the integration tests.

Dead code that was not ported: the client's legacy single-player engine (`OnePlayer`, `TrackPlayer`,
`DupeSeparation`, …); the server's `SongPlayer2–5` / `SongPlayerHalf3` and the `Note_2`/`Rest_2`/`*_Half` /
`Return_Start_*` messages; and `WinSet Region/TransColor`, which had no window to act on. None of these was
reachable from any hotkey, button or message.

## 3. Verification

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
machines over the internet. The code for these follows the original's Win32 calls one-to-one (`PostMessage`
WM_KEYDOWN/UP with the same lParam, `SetWindowText`, `IAudioMeterInformation::GetPeakValue`, `SendInput "1"`).
