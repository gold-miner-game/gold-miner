"""Cut a transparent sprite sheet (a rows x cols grid) into square sprites.

Each cell is cropped to its visible pixels, centred on a square canvas and resized, so the
art fills the frame edge to edge like the old primitive shapes (same in-game size).

    python tools/art/slice_sheet.py SHEET ROWS COLS OUT_DIR NAME1,NAME2,... [--size 256] [--margin 0.02]
A name of "-" skips that cell.
"""
import argparse
import os

import numpy as np
from PIL import Image


def main():
    p = argparse.ArgumentParser()
    p.add_argument("sheet")
    p.add_argument("rows", type=int)
    p.add_argument("cols", type=int)
    p.add_argument("out_dir")
    p.add_argument("names")
    p.add_argument("--size", type=int, default=256)
    p.add_argument("--margin", type=float, default=0.02)
    p.add_argument("--alpha", type=int, default=12, help="alpha below this counts as empty")
    p.add_argument("--match-area", default="", help="names (a+b+c) scaled to cover a one-unit disc's area")
    a = p.parse_args()

    sheet = Image.open(a.sheet).convert("RGBA")
    names = a.names.split(",")
    w, h = sheet.size
    cw, ch = w / a.cols, h / a.rows
    os.makedirs(a.out_dir, exist_ok=True)
    for i, name in enumerate(names):
        if name == "-":
            continue
        r, c = divmod(i, a.cols)
        cell = sheet.crop((round(c * cw), round(r * ch), round((c + 1) * cw), round((r + 1) * ch)))
        arr = np.array(cell)
        arr[arr[..., 3] < a.alpha] = 0
        ys, xs = np.nonzero(arr[..., 3])
        if len(xs) == 0:
            raise SystemExit(f"cell {i} ({name}) is empty")
        art = Image.fromarray(arr).crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
        side = int(max(art.size) * (1 + 2 * a.margin))
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(art, ((side - art.width) // 2, (side - art.height) // 2))
        out_size = a.size
        if a.match_area and name in a.match_area.split("+"):
            # Grow the canvas (more pixels, same pixels-per-unit) until the art covers as much as a
            # disc filling one unit: an irregular nugget then looks as big as the circle it replaces.
            cover = (np.asarray(canvas)[..., 3] > 127).mean()
            out_size = int(round(a.size * np.sqrt((np.pi / 4) / max(cover, 1e-3))))
        canvas = canvas.resize((out_size, out_size), Image.LANCZOS)
        path = os.path.join(a.out_dir, name + ".png")
        canvas.save(path)
        print(f"{name}: {art.size[0]}x{art.size[1]} -> {path}")


if __name__ == "__main__":
    main()
