#!/usr/bin/env python3
"""Draw a bar chart of round money margins.

Reads the JSON copied out of the WebGL Margins panel
(or tools/analytics/rounds.example.json) and writes an SVG next to it.

    python3 tools/analytics/plot_margin.py tools/analytics/rounds.example.json
"""

import json
import sys
from pathlib import Path


def load_rounds(path):
    data = json.loads(Path(path).read_text())
    if isinstance(data, dict):
        return data.get("rounds", [])
    return data


def bar_chart(rounds):
    width, height = 720, 420
    left, right, top, bottom = 56, 24, 48, 72
    plot_w = width - left - right
    plot_h = height - top - bottom
    zero_y = top + plot_h / 2
    peak = max([1] + [abs(int(r.get("margin", 0))) for r in rounds])
    slot = plot_w / max(1, len(rounds))
    bar_w = max(4, slot - 8)

    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}">',
        '<rect width="100%" height="100%" fill="#1c140f"/>',
        '<text x="56" y="32" fill="#f4efe6" font-family="sans-serif" font-size="18">Money margin (player − miner)</text>',
        f'<line x1="{left}" y1="{zero_y}" x2="{width - right}" y2="{zero_y}" stroke="#f4efe6" stroke-width="1"/>',
    ]
    for i, row in enumerate(rounds):
        margin = int(row.get("margin", 0))
        won = bool(row.get("playerWon", False))
        h = (plot_h / 2) * (abs(margin) / peak)
        x = left + i * slot + (slot - bar_w) / 2
        y = zero_y - h if margin >= 0 else zero_y
        color = "#59bf66" if won else "#d9594d"
        parts.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bar_w:.1f}" height="{max(1, h):.1f}" fill="{color}"/>')
        name = str(row.get("level", "?"))
        short = name[-10:]
        tx = x + bar_w / 2
        parts.append(
            f'<text x="{tx:.1f}" y="{height - 28}" fill="#f4efe6" font-family="sans-serif" '
            f'font-size="11" text-anchor="end" transform="rotate(-40 {tx:.1f} {height - 28})">{short}</text>'
        )
    parts.append("</svg>")
    return "\n".join(parts) + "\n"


def main():
    src = Path(sys.argv[1] if len(sys.argv) > 1 else "tools/analytics/rounds.example.json")
    rounds = load_rounds(src)
    out = src.with_suffix(".svg")
    out.write_text(bar_chart(rounds))
    print(out)


if __name__ == "__main__":
    main()
