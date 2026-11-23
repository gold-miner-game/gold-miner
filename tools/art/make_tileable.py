"""Make a texture tile seamlessly: cross-fade it with a half-offset copy near its edges.

    python tools/art/make_tileable.py IN OUT [--size 1024] [--blend 0.25]
"""
import argparse

import numpy as np
from PIL import Image


def main():
    p = argparse.ArgumentParser()
    p.add_argument("src")
    p.add_argument("dst")
    p.add_argument("--size", type=int, default=1024)
    p.add_argument("--blend", type=float, default=0.25, help="fraction of the tile used for the fade")
    a = p.parse_args()

    img = np.asarray(Image.open(a.src).convert("RGB").resize((a.size, a.size), Image.LANCZOS), dtype=np.float32)
    shifted = np.roll(img, (a.size // 2, a.size // 2), axis=(0, 1))
    # Weight of the original: 1 in the middle, fading to 0 at every edge (where the shifted copy is seamless).
    t = np.linspace(0, 1, a.size)
    ramp = np.clip(np.minimum(t, 1 - t) / a.blend, 0, 1)
    ramp = ramp * ramp * (3 - 2 * ramp)
    w = np.minimum.outer(ramp, ramp)[..., None]
    out = img * w + shifted * (1 - w)
    Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).save(a.dst)
    print("saved", a.dst)


if __name__ == "__main__":
    main()
