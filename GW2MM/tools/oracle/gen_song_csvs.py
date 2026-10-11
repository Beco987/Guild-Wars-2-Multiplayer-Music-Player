"""Synthetic midicsv-format songs for the pipeline oracle. They deliberately include the quirks the
original parser reacts to: out-of-range notes, notes 96/97, velocity-0 note-ons, controller numbers in
field 5, quoted text containing commas, tempo events before/after the first note, multiple tracks."""
import random

def song(rng, name):
    lines = []
    ntracks = rng.randint(1, 10)
    division = rng.choice([96, 192, 480])
    lines.append(f"0, 0, Header, 1, {ntracks}, {division}")
    tempo_mode = rng.choice(["first", "first", "first", "multi", "late", "none", "multi_collide"])
    note_times = set()
    for tr in range(1, ntracks + 1):
        lines.append(f"{tr}, 0, Start_track")
        t = 0
        if tr == 1 and tempo_mode in ("first", "multi", "multi_collide"):
            lines.append(f"1, 0, Tempo, {rng.choice([500000, 705881, 1000000, 428571])}")
        if rng.random() < 0.3:
            lines.append(f'{tr}, 0, Title_t, "Track, {rng.randint(48, 84)}, text"')
        if rng.random() < 0.4:
            lines.append(f"{tr}, 0, Control_c, 0, {rng.randint(40, 90)}, {rng.randint(0, 127)}")
        for _ in range(rng.randint(0, 45)):
            t += rng.choice([0, 0, 1, 2, rng.randint(1, 400), rng.choice([96, 120, 240, 480])])
            if rng.random() < 0.08:
                lines.append(f"{tr}, {t}, Control_c, 0, {rng.randint(0, 127)}, {rng.randint(0, 127)}")
                continue
            if tempo_mode in ("multi", "multi_collide", "late") and rng.random() < 0.06:
                lines.append(f"{tr}, {t}, Tempo, {rng.choice([500000, 600000, 400000])}")
            chord = rng.choice([1, 1, 1, 2, 3])
            for _ in range(chord):
                n = rng.choice([rng.randint(48, 84), rng.randint(48, 84), rng.randint(30, 110), 96, 97])
                v = rng.choice([0, rng.randint(1, 127), rng.randint(1, 127)])
                kind = "Note_on_c" if rng.random() < 0.9 else "Note_off_c"
                lines.append(f"{tr}, {t}, {kind}, {rng.randint(0, 15)}, {n}, {v}")
                note_times.add(t)
        if tempo_mode == "multi_collide" and note_times and rng.random() < 0.5:
            lines.append(f"{tr}, {rng.choice(sorted(note_times))}, Tempo, 450000")
        lines.append(f"{tr}, {t}, End_track")
    lines.append("0, 0, End_of_file")
    return "\r\n".join(lines) + "\r\n"
