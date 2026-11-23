"""Stop-motion "boil" frames: two hand-shifted redraws of a sprite (tiny rotation, scale, nudge and
a soft warp), saved next to it as NAME_b1.png and NAME_b2.png on the same canvas. Cycling the
original with these at ~10 fps looks like a paper cutout re-posed between frames.

    python tools/art/boil_variants.py SPRITE.png [SPRITE2.png ...]
"""
import sys

import numpy as np
from PIL import Image
from scipy import ndimage


def variant(img, rng):
    a = np.asarray(img.convert("RGBA"), np.float32)
    h, w = a.shape[:2]
    angle = rng.uniform(-2.6, 2.6)
    scale = rng.uniform(0.982, 1.018)
    shift = rng.uniform(-0.006, 0.006, 2) * w
    # smooth displacement field, about 1% of the size: the outline "boils" like a redrawn cutout
    amp = 0.01 * w
    field = [ndimage.zoom(rng.uniform(-1, 1, (5, 5)), (h / 5, w / 5), order=3)[:h, :w] * amp for _ in range(2)]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    cy, cx = (h - 1) / 2, (w - 1) / 2
    t = np.deg2rad(angle)
    # inverse map: output pixel -> source pixel
    dx, dy = xx - cx - shift[0], yy - cy - shift[1]
    sx = (np.cos(t) * dx + np.sin(t) * dy) / scale + cx + field[0]
    sy = (-np.sin(t) * dx + np.cos(t) * dy) / scale + cy + field[1]
    out = np.stack([ndimage.map_coordinates(a[..., c], [sy, sx], order=1, mode="constant") for c in range(4)], -1)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")


def main():
    for i, path in enumerate(sys.argv[1:]):
        rng = np.random.default_rng(100 + i)
        img = Image.open(path)
        for k in (1, 2):
            out = path[:-4] + f"_b{k}.png"
            variant(img, rng).save(out)
            print("boil", out)


if __name__ == "__main__":
    main()
