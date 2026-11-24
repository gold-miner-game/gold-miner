"""Procedural papercraft effect sprites (white/cream, so code tints still apply).

    python tools/art/make_fx.py OUT_DIR

BlockDome.png  pop-up card shield: three nested half discs of paper, flat side down (block flash)
Slash.png      torn-paper slash: a ring band from 0 to -50 degrees, bright leading edge (attack swing)
"""
import sys

import numpy as np
from PIL import Image
from scipy import ndimage


def grain(h, w, rng):
    g = np.zeros((h, w), np.float32)
    amp = 1.0
    for o in range(4):
        cells = 6 * 2 ** o
        small = rng.random((cells, cells)).astype(np.float32)
        g += amp * np.asarray(Image.fromarray(small).resize((w, h), Image.BICUBIC), np.float32)
        amp *= 0.5
    return (g - g.min()) / (g.max() - g.min())


def soft(mask, px=1.2):
    return np.clip(ndimage.gaussian_filter(mask.astype(np.float32), px), 0, 1)


def block_dome(out):
    w, h = 512, 256
    rng = np.random.default_rng(3)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    # image row 0 is the top; the flat side is the bottom edge
    d = np.hypot(xx + 0.5 - w / 2, (h - yy - 0.5)) / (w / 2)
    ang = np.arctan2(h - yy - 0.5, xx + 0.5 - w / 2)
    wobble = 0.012 * np.sin(ang * 23 + 1.3) + 0.008 * np.sin(ang * 41)   # hand-cut edge
    radii, tones = [0.98, 0.76, 0.52], [0.86, 0.94, 1.0]
    val = np.zeros((h, w), np.float32)
    alpha = np.zeros((h, w), np.float32)
    g = grain(h, w, rng)
    for r, tone in zip(radii, tones):
        inside = d < r + wobble
        layer = soft(inside)
        rim = np.exp(-((r + wobble - d) * (w / 2)) / 6.0) * inside    # darker cut edge
        lift = soft(np.roll(inside, -5, axis=0)) * (1 - layer) * 0.5   # shadow under the next layer down
        val = val * (1 - layer) + layer * (tone * (0.93 + 0.07 * g) - 0.18 * rim)
        val -= lift * 0.25
        alpha = np.maximum(alpha, layer)
    # stitched seam on the outer layer
    seam = np.exp(-((d - 0.9) ** 2) / (2 * 0.006 ** 2)) * (np.sin(ang * 46) > 0.1)
    val = val * (1 - 0.35 * seam)
    out_img = np.dstack([np.clip(val, 0, 1) * 255] * 3 + [alpha * 255]).astype(np.uint8)
    Image.fromarray(out_img, "RGBA").save(out)


def slash(out):
    size = 512
    r = size / 2
    rng = np.random.default_rng(5)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    px, py = xx + 0.5 - r, (size - yy - 0.5) - r            # y up
    d = np.hypot(px, py) / r
    a = np.degrees(np.arctan2(py, px))
    tail = 50.0
    torn_out = 1.0 - 0.035 * np.abs(np.sin(a * 0.9 + 2)) - 0.02 * np.abs(np.sin(a * 2.7))
    torn_in = 0.55 + 0.04 * np.abs(np.sin(a * 1.7 + 1)) + 0.03 * (rng.random(a.shape) > 0.995)
    band = (a <= 0) & (a >= -tail) & (d >= torn_in) & (d <= torn_out)
    along = np.clip(1 - (-a / tail), 0, 1)                  # 1 at the leading edge
    across = 1 - np.abs(d - 0.8) / 0.25
    alpha = soft(band, 1.0) * np.clip(along ** 1.6 * (0.45 + 0.55 * across) + 0.0, 0, 1)
    # paper fibres streaking along the swing
    streak = 0.85 + 0.15 * np.sin(d * 140 + rng.random() * 6)
    lead = np.exp(-((a - 0) ** 2) / (2 * 3.5 ** 2))         # bright cut at the leading edge
    val = np.clip((0.9 + 0.1 * grain(size, size, rng)) * streak + 0.25 * lead, 0, 1)
    img = np.dstack([val * 255, val * 252, val * 240, alpha * 255]).astype(np.uint8)
    Image.fromarray(img, "RGBA").save(out)


if __name__ == "__main__":
    o = sys.argv[1]
    block_dome(o + "/BlockDome.png")
    slash(o + "/Slash.png")
    print("ok")
