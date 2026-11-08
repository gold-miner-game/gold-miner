#!/usr/bin/env python3
"""Draw the four Beta metrics from the analytics rows.

    python3 tools/analytics/beta_metrics.py responses.csv        # the Google Form sheet, as CSV
    python3 tools/analytics/beta_metrics.py events.json          # rows copied from the game
    python3 tools/analytics/beta_metrics.py --mock [out_dir]     # simulated players, for the mockups

Writes metric1_tutorial.png, metric2_dropoff.png, metric3_hooks.png and
metric4_choices.png next to the input (or into out_dir with --mock, default
tools/analytics/mock, along with mock_rows.csv).

Needs matplotlib: pip3 install matplotlib

Sheet columns: player, session, level, character, event, value, player_money,
miner_money, won, end_reason, tutorial, source, seq, t, attempt, build.
"""

import csv
import json
import random
import sys
from collections import Counter, defaultdict
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

LEVELS = [("level_03", "Level 1"), ("level_01", "Level 2"), ("level_02", "Level 3")]
BATTLES = [("battle_03", "Level 1"), ("battle_06", "Level 2"), ("battle_02", "Level 3")]
STEPS = [("eat_gold", "Eat gold"), ("go_here", "Go to the rope"), ("cut", "Cut the rope"), ("eat_drop", "Eat what fell")]
NAMES = {"0": "Mr. Hyde", "1": "JK. Rolling", "2": "Scrat", "3": "Rero Rero"}
ITEMS = ["swift", "ration", "shield", "brace"]
BUILD = "beta-1110"  # PlayAnalytics.Build
UNDERSTOOD = 8.0     # seconds; the same line PlayAnalytics uses
STANDING = 3.0       # units moved in a 15 s stuck window; below this the player stood still

GREEN, YELLOW, RED, GREY, BLUE, PURPLE = "#59bf66", "#e8b931", "#d9594d", "#9a948c", "#4f8fd9", "#9b59d0"


# ---------- loading ----------

def load_rows(path):
    text = Path(path).read_text(encoding="utf-8-sig")
    if text.lstrip()[:1] in "[{":
        data = json.loads(text)
        raw = data.get("events", data) if isinstance(data, dict) else data
    else:
        raw = list(csv.DictReader(text.splitlines()))
    rows = [normalize(r) for r in raw]
    rows = [r for r in rows if r["event"] and r["source"] != "check" and r["event"] != "pipeline_check"]
    # The sheet also holds the Alpha playtests, which have no build. Keep only this build's rows.
    if any(r["build"] for r in rows):
        rows = [r for r in rows if r["build"] == BUILD]
    return rows


def normalize(row):
    clean = {(k or "").strip().lower().replace(" ", "_"): (v.strip() if isinstance(v, str) else v) for k, v in row.items()}

    def num(*names):
        for name in names:
            raw = clean.get(name)
            if raw not in (None, ""):
                try:
                    return float(raw)
                except ValueError:
                    return 0.0
        return 0.0

    return {
        "player": str(clean.get("player", "")),
        "session": str(clean.get("session", "")),
        "level": str(clean.get("level", "")),
        "character": str(clean.get("character", "")),
        "event": str(clean.get("event") or clean.get("eventname") or ""),
        "value": num("value"),
        "player_money": num("player_money", "playermoney"),
        "miner_money": num("miner_money", "minermoney"),
        "won": str(clean.get("won", "")),
        "reason": str(clean.get("end_reason") or clean.get("endreason") or ""),
        "tutorial": str(clean.get("tutorial", "")),
        "source": str(clean.get("source", "")),
        "attempt": str(clean.get("attempt", "")),
        "build": str(clean.get("build", "")),
    }


def of(rows, event, tutorial=None):
    return [r for r in rows if r["event"] == event and (tutorial is None or r["tutorial"] == tutorial)]


# ---------- metric 1: tutorial, prompt to action ----------

def metric1(rows, out):
    actions = of(rows, "tutorial_action")
    labels, fast, slow, missed, medians = [], [], [], [], []
    for step, label in STEPS:
        mine = [r for r in actions if r["reason"] == step]
        n = len(mine) or 1
        done_fast = sum(1 for r in mine if r["won"] == "1")
        never = sum(1 for r in mine if r["won"] != "1" and r["value"] < UNDERSTOOD)   # gave up early
        labels.append(label)
        fast.append(100 * done_fast / n)
        missed.append(100 * never / n)
        slow.append(100 - fast[-1] - missed[-1] if mine else 0)
        times = sorted(r["value"] for r in mine)
        medians.append(times[len(times) // 2] if times else 0)

    fig, ax = plt.subplots(figsize=(8, 4.2))
    ax.bar(labels, fast, color=GREEN, label=f"Done within {UNDERSTOOD:.0f} s")
    ax.bar(labels, slow, bottom=fast, color=YELLOW, label=f"Took longer than {UNDERSTOOD:.0f} s")
    ax.bar(labels, missed, bottom=[a + b for a, b in zip(fast, slow)], color=RED, label="Never did it")
    for i, m in enumerate(medians):
        ax.text(i, 102, f"median {m:.1f} s", ha="center", fontsize=9)
    ax.set_ylim(0, 112)
    ax.set_ylabel("Share of players (%)")
    ax.set_title("Metric 1: time from each tutorial prompt to the player's action")
    ax.legend(loc="lower right", fontsize=8)
    save(fig, out / "metric1_tutorial.png")


# ---------- metric 2: where players stop, and stuck or wandering ----------

def metric2(rows, out):
    reached = defaultdict(set)
    for r in rows:
        p = r["player"]
        if r["event"] == "tutorial_action":
            reached["Tutorial"].add(p)
        for level, label in LEVELS:
            if r["level"] != level or r["tutorial"] == "1":
                continue
            if r["event"] == "level_start":
                reached[label + " start"].add(p)
            if r["event"] == "round_end" and r["won"] == "1":
                reached[label + " win"].add(p)
    stages = ["Tutorial"] + [f"{label} {s}" for _, label in LEVELS for s in ("start", "win")]
    counts = [len(reached[s]) for s in stages]

    standing, wandering = [], []
    for level, label in LEVELS:
        stuck = [r for r in of(rows, "stuck") if r["level"] == level]
        rounds = len([r for r in of(rows, "round_end", tutorial="0") if r["level"] == level]) or 1
        standing.append(sum(1 for r in stuck if r["value"] < STANDING) / rounds)
        wandering.append(sum(1 for r in stuck if r["value"] >= STANDING) / rounds)

    fig, (a, b) = plt.subplots(1, 2, figsize=(11, 4.2), gridspec_kw={"width_ratios": [3, 2]})
    a.bar(stages, counts, color=[GREY] + [BLUE, GREEN] * 3)
    for i, c in enumerate(counts):
        a.text(i, c + 0.5, str(c), ha="center", fontsize=9)
    a.set_ylabel("Players who got here")
    a.set_title("Where players stop")
    a.tick_params(axis="x", rotation=35, labelsize=8)
    names = [label for _, label in LEVELS]
    b.bar(names, standing, color=RED, label=f"Standing still (< {STANDING:.0f} units)")
    b.bar(names, wandering, bottom=standing, color=YELLOW, label="Walking around")
    b.set_ylabel("Stuck windows per round")
    b.set_title("Stuck or wandering")
    b.legend(fontsize=8)
    fig.suptitle("Metric 2: where we lose players")
    save(fig, out / "metric2_dropoff.png")


# ---------- metric 3: hooks and each level's plan ----------

OUTCOMES = {
    "red": [("hit", RED), ("blocked", BLUE), ("cut", PURPLE), ("dodge", GREEN)],
    "yellow": [("banked", RED), ("cut", PURPLE), ("blocked", BLUE), ("hit", "#7a2a22"), ("empty", GREY)],
}


def metric3(rows, out):
    totals = defaultdict(Counter)
    for r in of(rows, "hook_result"):
        color, _, outcome = r["reason"].partition(":")
        name = dict(LEVELS).get(r["level"], r["level"])
        totals[(name, color)][outcome] += r["value"]

    fig, (a, b) = plt.subplots(1, 2, figsize=(12, 4.4), gridspec_kw={"width_ratios": [3, 2]})
    bars = [(label, color) for _, label in LEVELS for color in ("yellow", "red")]
    x_labels = [f"{label}\n{color}" for label, color in bars]
    seen = set()
    for i, key in enumerate(bars):
        count = totals.get(key, Counter())
        n = sum(count.values()) or 1
        bottom = 0
        for outcome, shade in OUTCOMES[key[1]]:
            share = 100 * count[outcome] / n
            tag = f"{key[1]} {outcome}"
            a.bar(i, share, bottom=bottom, color=shade, edgecolor="white", linewidth=0.5,
                  label=outcome if outcome not in seen else None)
            seen.add(outcome)
            bottom += share
    a.set_xticks(range(len(bars)), x_labels, fontsize=8)
    a.set_ylabel("Share of shots (%)")
    a.set_title("How each hook shot ended")
    a.legend(fontsize=8, ncol=3, loc="upper center", bbox_to_anchor=(0.5, -0.18))

    plan = [r for r in of(rows, "level_plan") if r["level"] == "level_01"]
    rounds = len(plan) or 1
    diamond = sum(1 for r in plan if "Diamond" in r["reason"])
    large = sum(1 for r in plan if "LargeGold" in r["reason"])
    both = sum(1 for r in plan if "Diamond" in r["reason"] and "LargeGold" in r["reason"])
    lost = sum(1 for r in of(rows, "item_banked") if r["level"] == "level_01" and r["reason"] == "Diamond")
    labels = ["Cut, then ate\nthe diamond", "Cut, then ate\nthe large gold", "Both", "Miner took\nthe diamond"]
    values = [100 * v / rounds for v in (diamond, large, both, lost)]
    b.bar(labels, values, color=[GREEN, GREEN, BLUE, RED])
    for i, v in enumerate(values):
        b.text(i, v + 1, f"{v:.0f}%", ha="center", fontsize=9)
    b.set_ylim(0, 105)
    b.set_ylabel("Share of Level 2 rounds (%)")
    b.set_title("Level 2: the caged treasure plan")
    b.tick_params(axis="x", labelsize=8)
    fig.suptitle("Metric 3: do players read the hooks and use them?")
    save(fig, out / "metric3_hooks.png")


# ---------- metric 4: store and character choices ----------

def metric4(rows, out):
    fig, axes = plt.subplots(2, 2, figsize=(11, 7.6))
    a, b, c, d = axes.flat

    kinds = [("bought", GREEN, "Bought"), ("looked_only", YELLOW, "Went, bought nothing"),
             ("skipped", RED, "Could pay, never went"), ("cant_afford", GREY, "Could not pay")]
    chances = of(rows, "store_chance")
    for i, (node, label) in enumerate(BATTLES):
        mine = [r for r in chances if r["level"] == node]
        n = len(mine) or 1
        bottom = 0
        for kind, shade, text in kinds:
            share = 100 * sum(1 for r in mine if r["reason"] == kind) / n
            a.bar(label, share, bottom=bottom, color=shade, label=text if i == 0 else None)
            bottom += share
    a.set_ylabel("Share of level entries (%)")
    a.set_title("Before each level: did they use the store?")
    a.legend(fontsize=7, loc="lower right")

    fit = Counter()
    for r in of(rows, "store_visit"):
        for part in r["reason"].split(","):
            words = part.split()
            if len(words) >= 2 and r["won"] != "none":
                fit[(words[1], words[2] if len(words) > 2 else "")] += 1
    groups = ["read", "unread"]
    fits = [fit[("fit", g)] for g in groups]
    misses = [fit[("miss", g)] for g in groups]
    totals = [f + m or 1 for f, m in zip(fits, misses)]
    shares = [100 * f / t for f, t in zip(fits, totals)]
    b.bar(["Read the item first", "Did not read it"], shares, color=[GREEN, YELLOW])
    b.axhline(50, color=GREY, linestyle="--", linewidth=1)
    b.text(1.45, 51, "random pick", fontsize=8, ha="right", color=GREY)
    for i, (s, t) in enumerate(zip(shares, totals)):
        b.text(i, s + 1, f"{s:.0f}% of {t}", ha="center", fontsize=9)
    b.set_ylim(0, 105)
    b.set_ylabel("Buys that fix the last loss (%)")
    b.set_title("After a loss: did they buy the right item?")

    with_items, without = defaultdict(list), defaultdict(list)
    for r in of(rows, "round_items"):
        name = dict(LEVELS).get(r["level"])
        if name:
            (without if r["reason"] == "none" else with_items)[name].append(r["won"] == "1")
    names = [label for _, label in LEVELS]
    rate = lambda xs: 100 * sum(xs) / len(xs) if xs else 0
    xs = range(len(names))
    c.bar([x - 0.2 for x in xs], [rate(without[n]) for n in names], 0.4, color=GREY, label="No item")
    c.bar([x + 0.2 for x in xs], [rate(with_items[n]) for n in names], 0.4, color=GREEN, label="With an item")
    c.set_xticks(list(xs), names)
    c.set_ylim(0, 100)
    c.set_ylabel("Win rate (%)")
    c.set_title("Does an item help them win?")
    c.legend(fontsize=8)

    picks = Counter()
    latest = {}
    for r in of(rows, "character_pick"):
        latest[r["player"]] = r["character"]
    picks.update(latest.values())
    played, won = Counter(), Counter()
    for r in of(rows, "round_end", tutorial="0"):
        if r["level"] in ("level_01", "level_02"):   # Level 1 is played before the pick
            played[r["character"]] += 1
            won[r["character"]] += r["won"] == "1"
    for key in ("0", "1", "2", "3"):
        if not played[key]:
            continue
        x, y = picks[key], 100 * won[key] / played[key]
        d.scatter(x, y, s=80 + 6 * played[key], color=[BLUE, YELLOW, GREEN, PURPLE][int(key)], alpha=0.8)
        d.annotate(NAMES[key], (x, y), textcoords="offset points", xytext=(8, 4), fontsize=9)
    d.set_xlabel("Players who picked it")
    d.set_ylabel("Win rate, Levels 2 and 3 (%)")
    d.set_ylim(0, 100)
    d.set_title("Characters: popularity and win rate")

    fig.suptitle("Metric 4: between-level choices (store and character)")
    save(fig, out / "metric4_choices.png")


def save(fig, path):
    fig.tight_layout()
    fig.savefig(path, dpi=130)
    plt.close(fig)
    print(path)


# ---------- simulated players for the mockups ----------

def mock_rows(players=40, seed=526):
    rng = random.Random(seed)
    rows = []

    def add(p, s, event, level="", value=0, won="", reason="", tutorial="0", character="0", attempt=0):
        rows.append({"player": p, "session": s, "level": level, "character": character, "event": event,
                     "value": round(value, 2), "player_money": 0, "miner_money": 0, "won": won,
                     "end_reason": reason, "tutorial": tutorial, "source": "mock", "attempt": attempt,
                     "build": BUILD})

    slow_odds = {"eat_gold": 0.1, "go_here": 0.08, "cut": 0.38, "eat_drop": 0.15}
    for i in range(players):
        p, s = f"p{i:02d}", f"s{i:02d}"
        for step, _ in STEPS:
            slow = rng.random() < slow_odds[step]
            gave_up = step == "eat_drop" and slow and rng.random() < 0.5   # the only step that times out
            seconds = rng.uniform(8.5, 20) if slow and not gave_up else rng.uniform(1.2, 6.5)
            if gave_up:
                seconds = 6.0
            add(p, s, "tutorial_action", "level_03", seconds, "0" if slow else "1", step, tutorial="1")
        if rng.random() < 0.05:
            continue

        character = "0"
        wallet = 0
        last_loss = "none"
        reach = [0.95, 0.72, 0.6]           # chance to keep going into each level
        win_odds = {"level_03": 0.7, "level_01": 0.35, "level_02": 0.3}
        for (level, _), (node, _), go in zip(LEVELS, BATTLES, reach):
            if rng.random() > go:
                break
            if level == "level_01" and rng.random() < 0.85:
                character = rng.choices("0123", weights=[5, 3, 2, 2])[0]
                add(p, s, "character_pick", "select", rng.uniform(3, 20), character=character)
            affordable = wallet >= 200
            if affordable and rng.random() < 0.55:
                fits = {"killed": ["shield", "brace"], "outearned": ["swift", "ration"]}.get(last_loss, ITEMS)
                read = rng.random() < 0.7
                item = rng.choice(fits) if (read and rng.random() < 0.75) else rng.choice(ITEMS)
                kind = "fit" if item in fits and last_loss != "none" else ("no_loss" if last_loss == "none" else "miss")
                add(p, s, "store_visit", "store", 2, last_loss, f"{item} {kind} {'read' if read else 'unread'}", character=character)
                add(p, s, "store_chance", node, 1, last_loss, "bought", character=character)
                items = item
            else:
                outcome = "skipped" if affordable and rng.random() < 0.6 else ("looked_only" if affordable else "cant_afford")
                add(p, s, "store_chance", node, int(affordable), last_loss, outcome, character=character)
                items = "none"

            for attempt in range(1, 4):
                add(p, s, "level_start", level, attempt=attempt, character=character)
                odds = win_odds[level] + (0.15 if items != "none" else 0)
                if level == "level_01" and character in "12":
                    odds -= 0.15
                won = rng.random() < odds
                for color, outcomes, weights in (
                    ("yellow", ["banked", "cut", "blocked", "hit", "empty"], [55, 18, 8, 4, 15]),
                    ("red", ["hit", "blocked", "cut", "dodge"],
                     [30 - 8 * LEVELS.index((level, dict(LEVELS)[level])), 15, 10, 45])):
                    for outcome, n in Counter(rng.choices(outcomes, weights=weights, k=rng.randint(8, 16))).items():
                        add(p, s, "hook_result", level, n, "1" if won else "0", f"{color}:{outcome}", character=character, attempt=attempt)
                if level == "level_01":
                    freed = [t for t, odds_t in (("Diamond", 0.45 if character in "03" else 0.1),
                                                 ("LargeGold", 0.35 if character in "03" else 0.08)) if rng.random() < odds_t]
                    add(p, s, "level_plan", level, len(freed), "1" if won else "0", ",".join(freed) or "none", character=character, attempt=attempt)
                    if "Diamond" not in freed and rng.random() < 0.6:
                        add(p, s, "item_banked", level, 500, "", "Diamond", character=character, attempt=attempt)
                for _ in range(rng.choice([0, 0, 1, 2] if level != "level_01" else [0, 1, 2, 3])):
                    wander = rng.random() < (0.7 if level == "level_01" else 0.35)
                    add(p, s, "stuck", level, rng.uniform(4, 30) if wander else rng.uniform(0, 2.5), character=character, attempt=attempt)
                add(p, s, "round_items", level, 0, "1" if won else "0", items, character=character, attempt=attempt)
                add(p, s, "round_end", level, rng.randint(-300, 600), "1" if won else "0", "", character=character, attempt=attempt)
                last_loss = "none" if won else rng.choice(["killed", "outearned"])
                if won:
                    wallet += rng.randint(150, 500)
                    break
                if wallet >= 200 and rng.random() < 0.6:   # back to the store before the retry
                    fits = {"killed": ["shield", "brace"], "outearned": ["swift", "ration"]}[last_loss]
                    read = rng.random() < 0.65
                    items = rng.choice(fits) if read and rng.random() < 0.8 else rng.choice(ITEMS)
                    kind = "fit" if items in fits else "miss"
                    add(p, s, "store_visit", "store", 2, last_loss, f"{items} {kind} {'read' if read else 'unread'}", character=character)
                    wallet -= 250
                else:
                    items = "none"
            else:
                break
    return rows


def main():
    args = sys.argv[1:]
    if args and args[0] == "--mock":
        out = Path(args[1] if len(args) > 1 else Path(__file__).parent / "mock")
        out.mkdir(parents=True, exist_ok=True)
        raw = mock_rows()
        with open(out / "mock_rows.csv", "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=list(raw[0].keys()))
            writer.writeheader()
            writer.writerows(raw)
        rows = load_rows(out / "mock_rows.csv")
    else:
        src = Path(args[0] if args else "responses.csv")
        rows = load_rows(src)
        out = src.parent
    for draw in (metric1, metric2, metric3, metric4):
        draw(rows, out)


if __name__ == "__main__":
    main()
