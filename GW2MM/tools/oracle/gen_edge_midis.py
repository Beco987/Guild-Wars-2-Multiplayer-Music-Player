"""Hand-crafted malformed / edge-case MIDI files for checking midicsv-compatible error handling."""
import os, struct, sys
from gen_midis import vlq, chunk, meta

def hdr(fmt, ntr, div, extra=b""):
    return chunk(b"MThd", struct.pack(">HHH", fmt, ntr, div) + extra)

CASES = {
    # running status continues across a meta event
    "edge_rs_meta": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00\x90\x3c\x40" + b"\x00" + meta(0x01, b"x") + b"\x10\x3e\x40" + b"\x00\xff\x2f\x00"),
    # running status continues across sysex
    "edge_rs_sysex": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00\x90\x3c\x40\x00\xf0\x02\x01\xf7\x10\x3e\x40\x00\xff\x2f\x00"),
    # events after End_track inside the chunk
    "edge_after_eot": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00\x90\x3c\x40\x00\xff\x2f\x00\x05\x90\x3e\x40"),
    # no End_track at all
    "edge_no_eot": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00\x90\x3c\x40\x10\x80\x3c\x00"),
    # header length 8 (extra bytes)
    "edge_hdr8": hdr(1, 1, 480, b"\x01\x02") + chunk(b"MTrk", b"\x00\x90\x3c\x40\x00\xff\x2f\x00"),
    # fewer track chunks than declared
    "edge_missing_track": hdr(1, 3, 480) + chunk(b"MTrk", b"\x00\x90\x3c\x40\x00\xff\x2f\x00"),
    # track chunk declared longer than file
    "edge_truncated": hdr(0, 1, 96) + b"MTrk" + struct.pack(">I", 100) + b"\x00\x90\x3c\x40",
    # not a MIDI file
    "edge_not_midi": b"RIFF....junkjunkjunk",
    # empty file
    "edge_empty": b"",
    # big delta and long meta
    "edge_bigdelta": hdr(0, 1, 96) + chunk(b"MTrk", vlq(0x0FFFFFFF) + b"\x90\x3c\x40" + b"\x00" + meta(0x03, b"T" * 300) + b"\x00\xff\x2f\x00"),
    # data byte without any status (running status undefined)
    "edge_no_status": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00\x3c\x40\x00\xff\x2f\x00"),
    # system common / realtime status bytes in a track
    "edge_sys_common": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00\xf2\x01\x02\x00\x90\x3c\x40\x00\xff\x2f\x00"),
    # key signature with odd values, time sig with 5 bytes, tempo with 2 bytes
    "edge_odd_meta": hdr(0, 1, 96) + chunk(b"MTrk", b"\x00" + meta(0x59, b"\x05\x02") + b"\x00" + meta(0x58, b"\x04\x02\x18\x08\x01") + b"\x00" + meta(0x51, b"\x07\xa1") + b"\x00" + meta(0x00, b"") + b"\x00" + meta(0x20, b"") + b"\x00\xff\x2f\x00"),
    # zero-track file
    "edge_zero_tracks": hdr(1, 0, 480),
}

def main(outdir):
    os.makedirs(outdir, exist_ok=True)
    for name, data in CASES.items():
        with open(os.path.join(outdir, name + ".mid"), "wb") as f:
            f.write(data)

if __name__ == "__main__":
    main(sys.argv[1])
