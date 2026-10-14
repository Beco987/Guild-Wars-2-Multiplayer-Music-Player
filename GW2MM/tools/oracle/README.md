# Oracle: running the original AutoHotkey code

These harnesses `#Include` the **unmodified** original scripts from `Midi Player - Server/scripts` and run them under
Wine to produce the golden data in `tests/GW2MM.Tests/Golden`. Only socket sends and the audio library are stubbed
(`stubs.ahk`).

| File | Purpose |
|---|---|
| `funcs.ahk` | Calls `Combine`, `CombineFlags`, `AddSwaps`, `OpportunisticSwaps`, `RemoveOutliers` on vectors from `gen_func_vectors.py` |
| `pipeline.ahk` | Runs the host's `SongPlayer` label for one case (song, port mapping, delays, ticks) and dumps `NewNotes`, `DelayToPlay`, `TimeToPlay`, latencies and the messages sent to clients |
| `gen_midis.py`, `gen_edge_midis.py` | MIDI corpus, converted with the bundled `Midicsv.exe` |
| `gen_song_csvs.py` | Synthetic songs that exercise the parser's quirks |
| `make_golden.py` | Regenerates everything |

Requirements (Ubuntu): `wine64`, `wine32:i386` (Midicsv.exe is 32-bit), `xvfb`, `x11-utils`, Python 3, and
AutoHotkey 1.1.37 (`AutoHotkey_1.1.37.02.zip` from the AutoHotkey GitHub releases).

```
AHK=/path/to/AutoHotkeyU64.exe python3 make_golden.py            # all steps
AHK=/path/to/AutoHotkeyU64.exe python3 make_golden.py pipeline   # one step
```

Notes: `Functions.ahk` declares `#SingleInstance Force`, so every parallel case runs its own copy of
`pipeline.ahk`. All cases share one Xvfb display, because parallel `xvfb-run -a` calls race for display numbers.
