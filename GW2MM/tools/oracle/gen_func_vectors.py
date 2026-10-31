"""Generates random test vectors for the function-level oracle (funcs.ahk)."""
import random, sys

LETTERS = "zZxXcvVbBnNmaAsSdfFgGhHjqQwWerRtTyYui90"
LOW = "zZxXcvVbBnNm"; MID = "aAsSdfFgGhHj"; HIGH = "qQwWerRtTyYui"

def enc(a):
    return f"{len(a)}:" + "|".join(a)

def chord(rng):
    pool = rng.choice([LETTERS, LOW, MID, HIGH, LOW + MID, MID + HIGH, "aq", "aqi", "9a0", LOW + HIGH])
    n = rng.choice([0, 1, 1, 1, 2, 2, 3, 4, 6])
    return "".join(rng.choice(pool) for _ in range(n))

def times(rng, n, unique=False, sort=False):
    t = [rng.choice([0, 0, rng.randint(0, 50), rng.randint(0, 5000)]) for _ in range(n)]
    if unique: t = list(dict.fromkeys(t))
    if sort: t.sort()
    return [str(x) for x in t]

def main(path, count, seed):
    rng = random.Random(seed)
    lines = []
    for i in range(count):
        kind = i % 5
        if kind == 0:  # Combine
            n1, n2 = rng.randint(0, 12), rng.randint(0, 12)
            a1 = times(rng, n1); a2 = [chord(rng) for _ in a1]
            a3 = times(rng, n2); a4 = [chord(rng) for _ in a3]
            if rng.random() < 0.3: a1.append("")  # trailing blank like Combine output
            if rng.random() < 0.3: a3.append("")
            if rng.random() < 0.2 and a2: a2.pop()  # shorter key array
            lines.append(f"c{i}\tCombine\t{enc(a1)}\t{enc(a2)}\t{enc(a3)}\t{enc(a4)}")
        elif kind == 1:  # CombineFlags
            a1 = times(rng, rng.randint(0, 12), unique=True, sort=True)
            a2 = [chord(rng) for _ in a1]
            if rng.random() < 0.7: a1.append("")
            if rng.random() < 0.5 and a1: a2.append("")
            a3 = times(rng, rng.choice([0, 1, 1, 1, 2, 3, 5]), sort=rng.random() < 0.8)
            if rng.random() < 0.5 and a1 and a1[0] != "":
                a3 = [rng.choice([x for x in a1 if x != ""]) for _ in range(rng.randint(1, 4))]
                a3.sort(key=int)
            a4 = ["B" + str(rng.choice([500000, 400000, 600000, 250000])) for _ in a3]
            lines.append(f"f{i}\tCombineFlags\t{enc(a1)}\t{enc(a2)}\t{enc(a3)}\t{enc(a4)}")
        elif kind == 2:  # AddSwaps
            a = [chord(rng) for _ in range(rng.randint(0, 25))]
            lines.append(f"s{i}\tAddSwaps\t{enc(a)}")
        elif kind == 3:  # OpportunisticSwaps
            n = rng.randint(0, 15)
            t = sorted(set(rng.randint(0, 3000) for _ in range(n)))
            notes = []
            for _ in t:
                c = chord(rng)
                if rng.random() < 0.4: c = rng.choice("90") + c
                notes.append(c)
            ts = [str(x) for x in t]
            if rng.random() < 0.5: ts.append("")
            delay = rng.choice(["0", "1", "50", "130", "240", "300", "1000"])
            lines.append(f"o{i}\tOpportunisticSwaps\t{enc(notes)}\t{enc(ts)}\t{delay}")
        else:  # RemoveOutliers
            n = rng.randint(0, 20)
            a = [str(rng.choice([rng.randint(10, 40), rng.randint(0, 400)])) for _ in range(n)]
            lines.append(f"r{i}\tRemoveOutliers\t{enc(a)}")
    with open(path, "w", newline="\n") as f:
        f.write("\n".join(lines) + "\n")

if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]), int(sys.argv[3]))
