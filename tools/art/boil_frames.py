"""Registered stop-motion boil frames (docs/art-style.md, rule 4).

A boil keeps the cut-out exactly where it is: the alpha (outline) of every frame is identical to the
original and nothing moves, rotates or scales. Only the surface changes, as if the gold leaf or felt
were nudged by a hand between exposures: the light direction swings a few degrees across the
surface relief, and the fine fibre grain is re-laid. Saves NAME_b1.png and NAME_b2.png.

    python tools/art/boil_frames.py SPRITE.png [...] [--swing 14] [--grain 0.035]
"""
import argparse

import numpy as np
from PIL import Image
from scipy import ndimage


def relight(rgb, alpha, swing_deg, grain_amt, rng):
    lum = rgb @ np.array([0.299, 0.587, 0.114])
    height = ndimage.gaussian_filter(lum, 1.6)                     # surface relief from the painting itself
    gy, gx = np.gradient(height)
    base = np.deg2rad(135.0)                                        # the scene's light: top left
    turned = base + np.deg2rad(swing_deg)
    shade = lambda a: -(gx * np.cos(a) - gy * np.sin(a))
    delta = shade(turned) - shade(base)
    delta = delta / (np.abs(delta).max() + 1e-6) * 0.10             # at most +-10% brightness
    grain = ndimage.gaussian_filter(rng.standard_normal(lum.shape), 0.7) * grain_amt
    out = rgb * (1.0 + delta + grain)[..., None]
    out[alpha < 1] = rgb[alpha < 1]
    return np.clip(out, 0, 255)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("sprites", nargs="+")
    p.add_argument("--swing", type=float, default=14.0, help="degrees the light swings per frame")
    p.add_argument("--grain", type=float, default=0.035)
    a = p.parse_args()
    for i, path in enumerate(a.sprites):
        rng = np.random.default_rng(200 + i)
        src = np.asarray(Image.open(path).convert("RGBA"), np.float32)
        rgb, alpha = src[..., :3], src[..., 3]
        for k, swing in ((1, a.swing), (2, -a.swing * 0.8)):
            out = np.dstack([relight(rgb, alpha, swing, a.grain, rng), alpha]).astype(np.uint8)
            assert (out[..., 3] == src[..., 3].astype(np.uint8)).all()   # registration: identical outline
            Image.fromarray(out, "RGBA").save(path[:-4] + f"_b{k}.png")
            print("boil", path[:-4] + f"_b{k}.png")


if __name__ == "__main__":
    main()
