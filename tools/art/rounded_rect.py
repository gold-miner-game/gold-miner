"""White rounded-rectangle mask, the base shape for paperize.py (e.g. life pips, felt strips).

    python tools/art/rounded_rect.py OUT WIDTH HEIGHT RADIUS [--pad 4]
"""
import argparse

from PIL import Image, ImageDraw


def main():
    p = argparse.ArgumentParser()
    p.add_argument("dst")
    p.add_argument("width", type=int)
    p.add_argument("height", type=int)
    p.add_argument("radius", type=int)
    p.add_argument("--pad", type=int, default=4, help="transparent margin, so the edge stays soft")
    a = p.parse_args()

    scale = 4   # draw large, then shrink: smooth anti-aliased corners
    w, h, r, pad = a.width * scale, a.height * scale, a.radius * scale, a.pad * scale
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    ImageDraw.Draw(img).rounded_rectangle([pad, pad, w - 1 - pad, h - 1 - pad], r, fill=(255, 255, 255, 255))
    img.resize((a.width, a.height), Image.LANCZOS).save(a.dst)


if __name__ == "__main__":
    main()
