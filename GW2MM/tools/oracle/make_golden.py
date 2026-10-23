"""Regenerates the golden test data in tests/GW2MM.Tests/Golden by running the ORIGINAL AutoHotkey code
(and the bundled Midicsv.exe) under Wine. Requirements: wine (32+64 bit), xvfb-run, AutoHotkey v1.1
(AutoHotkeyU64.exe; pass its path as AHK=...), Xvfb.

  AHK=/path/AutoHotkeyU64.exe python3 make_golden.py
"""
import concurrent.futures, os, random, shutil, subprocess, sys, tempfile
import gen_func_vectors, gen_midis, gen_edge_midis, gen_song_csvs

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
GOLDEN = os.path.join(REPO, "GW2MM", "tests", "GW2MM.Tests", "Golden")
AHK = os.environ.get("AHK")
MIDICSV = os.path.join(REPO, "Midi Player - Server", "Midicsv.exe")
ENV = dict(os.environ, WINEDEBUG="-all", DISPLAY=os.environ.get("ORACLE_DISPLAY", ":77"))

def ensure_display():
    """One shared virtual X display (parallel xvfb-run -a invocations race for display numbers)."""
    if subprocess.run(["xdpyinfo"], env=ENV, capture_output=True).returncode != 0:
        subprocess.Popen(["Xvfb", ENV["DISPLAY"], "-screen", "0", "1024x768x24", "-nolisten", "tcp"],
                         stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        import time; time.sleep(2)

def winpath(p):
    return "Z:" + os.path.abspath(p).replace("/", "\\")

def run_ahk(script, *args):
    cmd = ["wine", AHK, "/ErrorStdOut", os.path.join(HERE, script)] + [winpath(a) for a in args]
    for attempt in range(3):  # Wine occasionally stalls when many instances start at once; retry.
        try:
            r = subprocess.run(cmd, cwd=HERE, env=ENV, capture_output=True, text=True, timeout=90)
            break
        except subprocess.TimeoutExpired:
            if attempt == 2: raise
    err = (r.stdout + r.stderr).strip()
    if err.strip():
        raise RuntimeError(f"AHK error running {script}: {err}")

def functions():
    d = os.path.join(GOLDEN, "functions"); os.makedirs(d, exist_ok=True)
    gen_func_vectors.main(os.path.join(d, "vectors.in.txt"), 4000, 20240101)
    run_ahk("funcs.ahk", os.path.join(d, "vectors.in.txt"), os.path.join(d, "vectors.out.txt"))

def midis():
    d = os.path.join(GOLDEN, "midi")
    shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
    gen_midis.main(d, 80, 777)
    gen_edge_midis.main(d)
    shutil.copy(os.path.join(REPO, "Midi Player - Client", "Midi", "Yeah Right.mid"), os.path.join(d, "yeah_right.mid"))
    for f in sorted(os.listdir(d)):
        if not f.endswith(".mid"): continue
        base = os.path.join(d, f[:-4])
        r = subprocess.run(["wine", MIDICSV, f, f[:-4] + ".csv"], cwd=d, env=ENV, capture_output=True, timeout=60)
        with open(base + ".rc", "w") as fh:
            fh.write(str(r.returncode))

SONGS = os.path.join(GOLDEN, "songs")

def songs():
    shutil.rmtree(SONGS, ignore_errors=True); os.makedirs(SONGS)
    shutil.copy(os.path.join(REPO, "Midi Player - Client", "Csv", "Beabadoobee - Coffee Song.csv"), os.path.join(SONGS, "coffee.csv"))
    shutil.copy(os.path.join(GOLDEN, "midi", "yeah_right.csv"), os.path.join(SONGS, "yeah_right.csv"))
    rng = random.Random(4242)
    for i in range(40):
        with open(os.path.join(SONGS, f"syn{i:02d}.csv"), "w", newline="") as fh:
            fh.write(gen_song_csvs.song(rng, f"syn{i:02d}"))

def case_specs():
    rng = random.Random(99)
    names = sorted(f[:-4] for f in os.listdir(SONGS))
    specs = []
    for i in range(70):
        song = names[i % len(names)] if i < 2 * len(names) else rng.choice(names)
        if i == 68: song = "does_not_exist"
        ips = []
        for slot in range(1, 6):
            if slot == 1 or rng.random() < 0.6:
                for trk in range(1, 9):
                    if rng.random() < (0.5 if slot == 1 else 0.3):
                        ips.append(f"{slot}{trk}")
        gw2 = [rng.choice([0, 0, 35, 80, 150]) for _ in range(5)]
        spec = {
            "song": song,
            "octavedelay": str(rng.choice([0, 50, 130, 240, 300, 1000])),
            "ip": ",".join(ips),
            "gw2": ",".join(map(str, gw2)),
            "pings": ",".join([str(gw2[0])] + ["0"] * 4),
            "ticks": "0,1000000,2000000,3000000,4000000",
            "sockets": "1,101,102,103,104",
            "tempomod": str(rng.choice([0, 0, 0, -1, 2, -3, 5, -10, 10])),
        }
        if i % 7 == 3:
            spec["fromclient"] = "1"
            spec["clienttempo"] = rng.choice(["1.000000", "1.050000", "0.850000", "1.150000"])
        specs.append((f"case{i:02d}", spec))
    return specs

def run_case(name, spec):
    d = os.path.join(GOLDEN, "pipeline")
    work = tempfile.mkdtemp(prefix=name)
    os.makedirs(os.path.join(work, "Csv"))
    song = spec["song"]
    src = os.path.join(SONGS, song + ".csv")
    if os.path.exists(src):
        shutil.copy(src, os.path.join(work, "Csv", song + ".csv"))
    with open(os.path.join(work, "Data.csv"), "w", newline="") as fh:
        fh.write(f"Some Other Song, 2, 3, desc\r\n{song}, 3, {spec['tempomod']}, generated\r\n")
    with open(os.path.join(work, "case.txt"), "w", newline="\r\n") as fh:
        for k, v in spec.items():
            if k != "tempomod": fh.write(f"{k}={v}\n")
    # The original Functions.ahk declares #SingleInstance Force, so concurrent runs of the same script file would
    # terminate each other: give every case its own copy of the harness.
    script = f"pipeline_{name}.ahk"
    shutil.copy(os.path.join(HERE, "pipeline.ahk"), os.path.join(HERE, script))
    try:
        run_ahk(script, work, os.path.join(work, "out.txt"))
    finally:
        os.remove(os.path.join(HERE, script))
    with open(os.path.join(d, name + ".case.txt"), "w", newline="\n") as fh:
        for k, v in spec.items(): fh.write(f"{k}={v}\n")
    shutil.copy(os.path.join(work, "out.txt"), os.path.join(d, name + ".out.txt"))
    shutil.rmtree(work)
    return name

def pipeline():
    d = os.path.join(GOLDEN, "pipeline")
    shutil.rmtree(d, ignore_errors=True); os.makedirs(d)
    with concurrent.futures.ThreadPoolExecutor(4) as ex:
        for n in ex.map(lambda s: run_case(*s), case_specs()):
            print("done", n, flush=True)

if __name__ == "__main__":
    if not AHK: sys.exit("set AHK=/path/to/AutoHotkeyU64.exe")
    ensure_display()
    steps = sys.argv[1:] or ["functions", "midis", "songs", "pipeline"]
    for s in steps:
        print("==", s, flush=True); globals()[s]()
