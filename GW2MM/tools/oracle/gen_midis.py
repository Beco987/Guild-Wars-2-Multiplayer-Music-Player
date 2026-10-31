"""Writes a corpus of Standard MIDI Files exercising every event type midicsv knows about.
Raw bytes are written by hand so running status, SysEx packets and odd meta events are covered."""
import os, random, struct, sys

def vlq(n):
    out = [n & 0x7F]
    n >>= 7
    while n:
        out.append((n & 0x7F) | 0x80)
        n >>= 7
    return bytes(reversed(out))

def chunk(tag, data):
    return tag + struct.pack(">I", len(data)) + data

def track(events, running=False):
    """events: list of (delta, bytes) where bytes is a full status-bearing event."""
    data = b""; last = None
    for delta, ev in events:
        st = ev[0]
        if running and 0x80 <= st < 0xF0 and st == last:
            data += vlq(delta) + ev[1:]
        else:
            data += vlq(delta) + ev
        last = st if 0x80 <= st < 0xF0 else (None if st in (0xF0, 0xF7) else last)
    return chunk(b"MTrk", data)

def meta(t, payload):
    return bytes([0xFF, t]) + vlq(len(payload)) + payload

def eot():
    return (0, meta(0x2F, b""))

def rand_text(rng):
    pool = [b"Hello", b"a, b, c", b'quote " here', b"back\\slash", b"\x00\x01\x7f\x80\xe9\xff",
            b"tab\tnew\nline", b"", b"Piano, Piano, Piano\x00", bytes(rng.randrange(256) for _ in range(rng.randint(0, 12)))]
    return rng.choice(pool)

def channel_event(rng, ch):
    kind = rng.choice(["on", "on", "on", "off", "on0", "cc", "pc", "pb", "pat", "cat"])
    note = rng.choice([rng.randint(0, 127), rng.randint(46, 99), 96, 97])
    if kind == "on": return bytes([0x90 | ch, note, rng.randint(1, 127)])
    if kind == "on0": return bytes([0x90 | ch, note, 0])
    if kind == "off": return bytes([0x80 | ch, note, rng.randint(0, 127)])
    if kind == "cc": return bytes([0xB0 | ch, rng.randint(0, 127), rng.randint(0, 127)])
    if kind == "pc": return bytes([0xC0 | ch, rng.randint(0, 127)])
    if kind == "pb": return bytes([0xE0 | ch, rng.randint(0, 127), rng.randint(0, 127)])
    if kind == "pat": return bytes([0xA0 | ch, note, rng.randint(0, 127)])
    return bytes([0xD0 | ch, rng.randint(0, 127)])

def meta_event(rng):
    k = rng.randint(0, 17)
    if k == 0: return meta(0x00, struct.pack(">H", rng.randint(0, 65535)))
    if 1 <= k <= 9: return meta(k, rand_text(rng))
    if k == 10: return meta(0x20, bytes([rng.randint(0, 15)]))
    if k == 11: return meta(0x21, bytes([rng.randint(0, 15)]))
    if k == 12: return meta(0x51, rng.randint(1, 0xFFFFFF).to_bytes(3, "big"))
    if k == 13: return meta(0x54, bytes([rng.randint(0, 23), rng.randint(0, 59), rng.randint(0, 59), rng.randint(0, 29), rng.randint(0, 99)]))
    if k == 14: return meta(0x58, bytes([rng.randint(1, 12), rng.randint(0, 5), rng.randint(1, 96), 8]))
    if k == 15: return meta(0x59, bytes([rng.randint(-7, 7) & 0xFF, rng.randint(0, 1)]))
    if k == 16: return meta(0x7F, bytes(rng.randrange(256) for _ in range(rng.randint(0, 6))))
    return meta(rng.choice([0x60, 0x0A, 0x4B, 0x7E]), bytes(rng.randrange(256) for _ in range(rng.randint(0, 5))))

def sysex(rng):
    body = bytes(rng.randrange(128) for _ in range(rng.randint(0, 8)))
    if rng.random() < 0.5:
        return bytes([0xF0]) + vlq(len(body) + 1) + body + b"\xF7"
    return bytes([0xF7]) + vlq(len(body)) + body

def random_file(rng):
    fmt = rng.choice([0, 1, 1, 1, 2])
    ntr = 1 if fmt == 0 else rng.randint(1, 6)
    div = rng.choice([480, 96, 192, 120, 1, 0x7FFF, 0xE728, 0xE250])  # last two are SMPTE divisions
    tracks = []
    for ti in range(ntr):
        evs = []
        if ti == 0:
            evs.append((0, meta(0x51, rng.choice([500000, 705881, 1000000, 400000]).to_bytes(3, "big"))))
        for _ in range(rng.randint(0, 60)):
            delta = rng.choice([0, 0, 1, rng.randint(0, 500), rng.randint(0, 300000)])
            r = rng.random()
            if r < 0.75: ev = channel_event(rng, rng.randint(0, 15))
            elif r < 0.93: ev = meta_event(rng)
            else: ev = sysex(rng)
            evs.append((delta, ev))
        evs.append(eot())
        tracks.append(track(evs, running=rng.random() < 0.5))
    hdr = chunk(b"MThd", struct.pack(">HHH", fmt, ntr, div))
    extra = chunk(b"XFIH", b"junk") if rng.random() < 0.1 else b""
    return hdr + extra + b"".join(tracks)

def main(outdir, count, seed):
    os.makedirs(outdir, exist_ok=True)
    rng = random.Random(seed)
    for i in range(count):
        with open(os.path.join(outdir, f"gen{i:03d}.mid"), "wb") as f:
            f.write(random_file(rng))

if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]))
