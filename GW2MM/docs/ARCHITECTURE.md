# Architecture

## Original application (what was ported)

Two AutoHotkey v1 scripts that share most of their code:

* **Server/host** (`GW2MMConnect.ahk` + `scripts/*`): the GUI and song library. It accepts clients over AHKsock
  (TCP 27015), and on Play arranges the song for up to 5 player slots, sends each client its part, and plays its
  own part by posting key messages to windows titled `Player1…Player8`.
* **Client**: the same GUI and library. It connects to the host, answers clock/ping probes, plays the parts it
  receives, and can send one of its songs to the host to play.

The only external dependencies were Windows itself, `Midicsv.exe` (MIDI → CSV), Core Audio (via VA.ahk, for
Sync) and the Guild Wars 2 windows.

## New structure

```
            ┌──────────────────────── GW2MM.Windows (WinForms) ──────────────────────────┐
 hotkeys ──►│ MainForm (IHostView / ISessionView)  Port/Settings/NewSong/DataEditor forms│
            │ Win32KeyOutput  WindowRenamer  HotkeyWindow  AudioInputDelayMeter          │
            └──────────────┬──────────────────────────────────────────────▲─────────────┘
                           │ commands                          view calls │ (BeginInvoke)
            ┌──────────────▼──────────────── GW2MM.Core ───────────────────┴─────────────┐
            │ HostSession / ClientSession  ── one lock guards slots, mapping, current run│
            │    │            │                                                          │
            │    │            └─ PeerConnection (JSON lines over TCP, ordered send queue) │
            │    ├─ SongLibrary / DataFileEditor / AppSettings (original file formats)    │
            │    ├─ CsvSong.Parse → SongArranger (TrackWriter, AddSwaps, …) → SongPlan     │
            │    └─ Schedule → PlaybackLoop (dedicated thread, IClock, IKeyOutput)        │
            └────────────────────────────────────────────────────────────────────────────┘
```

Separate processes are kept only where needed: one host per computer and one client per remote computer,
connected by TCP. Everything else that the original did with timers, `Gosub`, `.bat` files and a second script
(CsvEditor) is now ordinary in-process code.

## The song-playing loop

### 1. Arrangement (host, `SongArranger`)

A literal port of the original `SongPlayer` label, verified against it (see *Verification* below):

1. `CsvSong.Parse` extracts, per MIDI track (1–25), every `Note_on_c` with velocity > 0 as *(tick, note letter)*,
   plus the tempo events (only the first one before the first note).
2. For each slot 1–5 (`TrackWriter`) and each track ticked in the port mapping:
   simultaneous notes are merged (`Combine`); octave changes are added as key `9`/`0`, and Cs as `8` (`AddSwaps`);
   a swap that leads a chord moves to the middle of the preceding gap when that gap is at least *OctaveDelay*
   ticks (`OpportunisticSwaps`); letters are encoded as one character per *(window, instrument key)* (`KeyMap`).
   The slot's tracks are then merged.
3. `AddFiller` gives every active slot an element (blank if silent) at every time any slot plays. All parts
   therefore walk the same timeline.
4. `CombineFlags` inserts the tempo markers. `DelayToClock` turns tick gaps into ms:
   `floor(ticks / 500000 × µs-per-quarter × tempoFactor)`, where tempoFactor = 1 − 0.05 × the song's Tempo Modifier.
   Result: `SlotPlan` = key strings + gaps (the original `NewNotes` / `DelayToPlay`).

### 2. Synchronised start (host, `HostSession.PlayAsync`)

1. Cancel whatever song is preparing or playing (no duplicate loops).
2. T1 = host clock; send `ClockRequest` to every connected client. Each client answers immediately with its
   own clock; the host records `clientClock − RTT/2` (the client's clock at host time T1).
3. Wait 2 s (as the original did). This step is cancellable: Stop or a new Play during the wait abandons the run
   before anything is sent.
4. For each active slot: `time[k] = clock_at_T1 + 4000 − latency + Σ gaps[0..k]` in **that machine's clock**,
   where latency = slot input delay − host input delay. A slow-reacting game client therefore starts early.
   Clients get their `SongPart`; the host starts its own loop.

### 3. Playing (`PlaybackLoop`, host and clients)

* Event list (`Schedule`): on the host, element *k+1* is pressed when the clock reaches `time[k]`. The first
  element (the tempo marker) is never pressed. On a client the first element is pressed on arrival, then the same
  rule applies. These index semantics come straight from the original timer labels.
* A dedicated `Highest`-priority thread waits for each due time using a Stopwatch clock: it sleeps (with
  `timeBeginPeriod(1)`) until about 1.5 ms before, then spins. The original polled `A_TickCount`, which only
  advances in 10–16 ms steps.
* Overdue events (a late start, or after a pause) fire in order, at least 1 ms apart. That is the same catch-up
  the original timer did, one element per tick.
* `Stop` cancels before the next press. Pause holds the loop until it is resumed. A key-output exception is
  reported and playback continues. The loop ends after the last element; nothing repeats or restarts on its own.
* Each key press is `PostMessage(WM_KEYDOWN/WM_KEYUP)` to the visible top-level window whose title starts with
  `Player<n>`, exactly like the original `PlayNotes`, so the game does not need focus.

### 4. Stop

The host cancels the current run, sends `StopPlayback` to every slot, brings its window forward and resets octaves
(9, 9, 0 to Player1–5, 150 ms apart). A client stops its loop and resets octaves with *OctaveDelay* ms pauses.

### Concurrency rules

* Every session mutation (slots, port mapping, input delays, current run, pause) happens under one lock, and no
  I/O is awaited while holding it. View callbacks never block (they marshal with `BeginInvoke`).
* Network reads run on one task per connection, in arrival order. Sends are queued, so callers never block.
  Clock and ping probes are answered on the network thread for accurate timing.
* A run is identified by a `SongRun` object. Clock replies and loop-completion callbacks for an old run are
  ignored, so a stale event cannot affect a newer song.

## Port opening (UPnP)

`Net/UpnpClient` finds the router with an SSDP `M-SEARCH`. The search is sent to the multicast group from every
active IPv4 adapter, and as unicast to each default gateway. The client then reads the router's device
description and picks its `WANIPConnection:2`/`:1` or `WANPPPConnection:1` service. `Net/PortOpener` runs the
host's sequence:

1. `GetSpecificPortMappingEntry`. A forward to another device fails with a message. A forward to this computer
   is reused, and is only removed on exit when its description is `GW2MM` (left by an earlier run).
2. `AddPortMapping`, with a permanent lease. If the router refuses it, a 7-day lease is used instead (IGDv2's
   maximum).
3. `GetExternalIPAddress`. A private or carrier-grade NAT address means the router is behind another NAT.

`MainForm` runs this in the background after the listener starts and writes the result to the chat box and
`GW2MM.log`. On exit it calls `DeletePortMapping`, waiting at most 2 seconds.

## Protocol

Newline-delimited JSON over TCP port 27015, defined in `Net/Messages.cs`. Each message documents the original
frame it replaces (`Tick_Count`, `Rest_1`/`Note_1`, `Stop`, `Sync_Instrument`, `Ping_Server`, `Socket_Ping`,
`Play;…`/`StartSong`). There is no size limit short of 64 MB per message.
