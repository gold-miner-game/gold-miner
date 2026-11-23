"""Give a white primitive sprite a papercraft surface without changing its shape.

The alpha channel is copied unchanged (same outline, same physics shape); only the white fill
becomes light paper: fibre grain plus a soft shade toward the cut edge. Colour still comes from the
SpriteRenderer tint, so gameplay colour codes (e.g. green barrier rocks) keep working.

    python tools/art/paperize.py ORIGINAL OUT --style paper|stone [--seed 1]
"""
import argparse

import numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage


def fbm(h, w, rng, octaves=5):
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = 4 * 2 ** o
        small = rng.random((cells, cells)).astype(np.float32)
        layer = np.asarray(Image.fromarray(small).resize((w, h), Image.BICUBIC), np.float32)
        out += amp * layer
        total += amp
        amp *= 0.55
    out /= total
    return (out - out.min()) / max(out.max() - out.min(), 1e-6)


def fibres(h, w, rng, count):
    img = Image.new("L", (w, h), 0)
    px = np.zeros((h, w), np.float32)
    for _ in range(count):
        x, y = rng.random() * w, rng.random() * h
        ang = rng.random() * np.pi
        length = 6 + rng.random() * 22
        for t in np.linspace(0, 1, int(length)):
            xi, yi = int(x + np.cos(ang) * length * t) % w, int(y + np.sin(ang) * length * t) % h
            px[yi, xi] = 1.0
    img = Image.fromarray((px * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.7))
    return np.asarray(img, np.float32) / 255.0


def main():
    p = argparse.ArgumentParser()
    p.add_argument("src")
    p.add_argument("dst")
    p.add_argument("--style", choices=["paper", "stone"], default="paper")
    p.add_argument("--seed", type=int, default=1)
    a = p.parse_args()

    src = Image.open(a.src)
    has_alpha = src.mode in ("RGBA", "LA")
    rgba = np.asarray(src.convert("RGBA"), np.float32)
    h, w = rgba.shape[:2]
    alpha = rgba[..., 3] if has_alpha else np.full((h, w), 255, np.float32)
    rng = np.random.default_rng(a.seed)

    inside = alpha > 127
    if not has_alpha:   # opaque square: its edges are the image border
        inside = np.zeros((h, w), bool)
        inside[1:-1, 1:-1] = True
    dist = ndimage.distance_transform_edt(inside)
    scale = max(h, w) / 256.0

    grain = 0.6 * fbm(h, w, rng) + 0.4 * fbm(h, w, rng, 3)
    fib = fibres(h, w, rng, int(260 * scale * scale))
    if a.style == "stone":
        base = 0.70 + 0.28 * grain - 0.025 * fib
        rim = 0.20 * np.exp(-dist / (10 * scale))          # darker cut edge
        lip = 0.08 * np.exp(-((dist - 7 * scale) ** 2) / (2 * (3 * scale) ** 2))   # raised paper lip
        light = 0.06 * (np.linspace(1, -1, h)[:, None] + np.linspace(-1, 1, w)[None, :]) / 2
        value = base - rim + lip + light
    else:
        base = 0.93 + 0.07 * grain - 0.05 * fib
        rim = 0.10 * np.exp(-dist / (7 * scale))
        value = base - rim
    value = np.clip(value, 0, 1) * 255

    out = np.dstack([value, value, value, alpha]).astype(np.uint8)
    if has_alpha:
        Image.fromarray(out, "RGBA").save(a.dst)
    else:
        Image.fromarray(out[..., :3], "RGB").save(a.dst)
    print(f"{a.style}: {a.src} -> {a.dst}")


if __name__ == "__main__":
    main()
