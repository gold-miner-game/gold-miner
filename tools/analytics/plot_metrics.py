#!/usr/bin/env python3
"""Turn analytics rows into the four Alpha charts.

Reads either the JSON copied from the in-game Events panel, or a CSV
exported from the Google Form response sheet. Writes four SVGs next to
the input. events.example.json is sample data, not a playtest.

    python3 tools/analytics/plot_metrics.py tools/analytics/events.example.json
    python3 tools/analytics/plot_metrics.py responses.csv

Sheet columns: player, session, level, character, event, value,
player_money, miner_money, won, end_reason, tutorial, source.
The event column is eventName in the JSON.
"""

import csv
import json
import sys
from collections import defaultdict
from pathlib import Path


SKIP_LEVELS = {"store", "select"}
NAMES = {"0": "Mr. Hyde", "1": "JK. Rolling", "2": "Scrat", "3": "Rero Rero"}
ITEMS = ["swift", "ration", "shield", "brace"]


def load_rows(path):
    text = Path(path).read_text(encoding="utf-8-sig")
    if path.suffix.lower() == ".json" or text.lstrip().startswith("{") or text.lstrip().startswith("["):
        data = json.loads(text)
        rows = data.get("events", data) if isinstance(data, dict) else data
    else:
        rows = list(csv.DictReader(text.splitlines()))
    return [row for row in (normalize(raw) for raw in rows) if keep(row)]


def keep(row):
    # The one-time check row posted while linking the form. It is not a play.
    if row["event"] == "pipeline_check":
        return False
    if row["item"] == "delete_this_row":
        return False
    if row["player"] == "pipeline_check":
        return False
    if row["source"] == "check":
        return False
    return True


def normalize(row):
    clean = {(key or "").strip().lower(): (value if not isinstance(value, str) else value.strip()) for key, value in row.items()}
    event = clean.get("event") or clean.get("eventname") or ""
    def num(name, default=0):
        raw = clean.get(name, "")
        if raw is None or raw == "":
            return default
        return float(raw)
    return {
        "player": str(clean.get("player", "")),
        "level": str(clean.get("level", "?")),
        "event": str(event),
        "value": num("value"),
        "character": str(clean.get("character", "")),
        "won": str(clean.get("won", "")),
        "tutorial": str(clean.get("tutorial", "")),
        "item": str(clean.get("end_reason") or clean.get("endreason") or ""),
        "source": str(clean.get("source", "")),
    }


def scored(row):
    # Tutorials are taught and often ended by the script, so they are not a difficulty result.
    return row["tutorial"] != "1"


def players_on(rows, level):
    found = set()
    for row in rows:
        if not scored(row) or row["level"] != level:
            continue
        if row["event"] in ("level_start", "round_end", "first_gold"):
            found.add(row["player"])
    return found or {row["player"] for row in rows if scored(row) and row["level"] == level}


def levels_of(rows):
    seen = []
    for row in rows:
        if not scored(row) or row["level"] in SKIP_LEVELS or row["level"] in seen:
            continue
        seen.append(row["level"])
    return seen


def mean_first_gold(rows):
    buckets = defaultdict(list)
    for row in rows:
        if scored(row) and row["event"] == "first_gold":
            buckets[row["level"]].append(row["value"])
    return [(level, sum(values) / len(values)) for level, values in ((lv, buckets[lv]) for lv in levels_of(rows)) if values]


def mean_per_player(rows, event):
    totals = defaultdict(lambda: defaultdict(float))
    for row in rows:
        if scored(row) and row["event"] == event:
            totals[row["level"]][row["player"]] += row["value"]
    bars = []
    for level in levels_of(rows):
        people = players_on(rows, level)
        if not people:
            continue
        total = sum(totals[level].get(player, 0.0) for player in people)
        bars.append((level, total / len(people)))
    return bars


def mean_margin(rows):
    buckets = defaultdict(list)
    for row in rows:
        if scored(row) and row["event"] == "round_end":
            buckets[row["level"]].append(row["value"])
    return [(level, sum(values) / len(values)) for level, values in ((lv, buckets[lv]) for lv in levels_of(rows)) if values]


def mean_losses(rows):
    losses = defaultdict(lambda: defaultdict(int))
    for row in rows:
        if scored(row) and row["event"] == "round_end" and row["won"] != "1":
            losses[row["level"]][row["player"]] += 1
    bars = []
    for level in levels_of(rows):
        people = players_on(rows, level)
        if not people:
            continue
        total = sum(losses[level].get(player, 0) for player in people)
        bars.append((level, total / len(people)))
    return bars


def mean_retries(rows):
    starts = defaultdict(lambda: defaultdict(int))
    for row in rows:
        if scored(row) and row["event"] == "level_start":
            starts[row["level"]][row["player"]] += 1
    bars = []
    for level in levels_of(rows):
        people = [player for player, count in starts[level].items() if count > 0]
        if not people:
            continue
        total = sum(max(0, starts[level][player] - 1) for player in people)
        bars.append((level, total / len(people)))
    return bars


def character_picks(rows):
    latest = {}
    for row in rows:
        if row["event"] == "character_pick":
            latest[row["player"]] = row["character"]
    counts = defaultdict(int)
    for character in latest.values():
        counts[character] += 1
    return [(NAMES.get(character, character), float(counts[character])) for character in ("0", "1", "2", "3")]


def store_buys(rows):
    people = {row["player"] for row in rows if row["event"] in ("level_start", "purchase", "character_pick")}
    if not people:
        people = {row["player"] for row in rows}
    totals = defaultdict(float)
    for row in rows:
        if row["event"] == "purchase" and row["item"] in ITEMS:
            totals[row["item"]] += row["value"]
    n = len(people) or 1
    return [(item, totals[item] / n) for item in ITEMS]


def win_rate(rows):
    played = defaultdict(int)
    won = defaultdict(int)
    for row in rows:
        if row["event"] != "round_end" or row["tutorial"] == "1":
            continue
        played[row["character"]] += 1
        if row["won"] == "1":
            won[row["character"]] += 1
    return [(NAMES.get(character, character), 100.0 * won[character] / played[character])
            for character in ("0", "1", "2", "3") if played[character]]


def chart(title, bars, unit, zero_line):
    width, height = 720, 420
    left, right, top, bottom = 64, 24, 48, 72
    plot_w = width - left - right
    plot_h = height - top - bottom
    peak = max([1.0] + [abs(value) for _, value in bars]) or 1.0
    slot = plot_w / max(1, len(bars))
    bar_w = max(8, slot - 16)
    zero_y = top + plot_h / 2 if zero_line else top + plot_h
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}">',
        '<rect width="100%" height="100%" fill="#1c140f"/>',
        f'<text x="64" y="32" fill="#f4efe6" font-family="sans-serif" font-size="18">{title}</text>',
        f'<line x1="{left}" y1="{zero_y}" x2="{width - right}" y2="{zero_y}" stroke="#f4efe6" stroke-width="1"/>',
    ]
    for i, (level, value) in enumerate(bars):
        span = plot_h / 2 if zero_line else plot_h
        h = span * (abs(value) / peak)
        x = left + i * slot + (slot - bar_w) / 2
        y = zero_y - h if value >= 0 or not zero_line else zero_y
        if not zero_line:
            y = zero_y - h
        color = "#59bf66" if value >= 0 else "#d9594d"
        parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w:.1f}" height="{max(1, h):.1f}" fill="{color}"/>')
        label = f"{value:.1f}{unit}"
        parts.append(
            f'<text x="{x + bar_w / 2:.1f}" y="{y - 6:.1f}" fill="#f4efe6" font-family="sans-serif" '
            f'font-size="12" text-anchor="middle">{label}</text>'
        )
        short = level[-14:]
        tx = x + bar_w / 2
        parts.append(
            f'<text x="{tx:.1f}" y="{height - 28}" fill="#f4efe6" font-family="sans-serif" '
            f'font-size="12" text-anchor="end" transform="rotate(-40 {tx:.1f} {height - 28})">{short}</text>'
        )
    if not bars:
        parts.append('<text x="64" y="200" fill="#f4efe6" font-family="sans-serif" font-size="16">No rows for this metric yet.</text>')
    parts.append("</svg>")
    return "\n".join(parts) + "\n"


def main():
    src = Path(sys.argv[1] if len(sys.argv) > 1 else "tools/analytics/events.example.json")
    rows = load_rows(src)
    charts = {
        "first_gold": chart("Seconds to first gold (mean per player)", mean_first_gold(rows), "s", False),
        "digest": chart("Gold touched while digesting (mean per player)", mean_per_player(rows, "digest_touch"), "", False),
        "steal": chart("Gold knocked off a hook (mean per player)", mean_per_player(rows, "steal"), "", False),
        "margin": chart("Money margin, player minus miner (mean)", mean_margin(rows), "", True),
        "losses": chart("Losses per level (mean per player)", mean_losses(rows), "", False),
        "retries": chart("Retries per level (mean per player)", mean_retries(rows), "", False),
        "lives": chart("Lives lost per level (mean per player)", mean_per_player(rows, "death"), "", False),
        "characters": chart("Players who picked each character", character_picks(rows), "", False),
        "purchases": chart("Store purchases per player (mean)", store_buys(rows), "", False),
        "win_rate": chart("Win rate by character", win_rate(rows), "%", False),
    }
    for name, svg in charts.items():
        out = src.with_name(src.stem + "." + name + ".svg")
        out.write_text(svg)
        print(out)


if __name__ == "__main__":
    main()
