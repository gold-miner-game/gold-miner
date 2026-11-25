"""Needle Meowmeow (the sprint-stab cat): cut the Meshy-painted poses into game sprites.

Sources (Meshy image-to-image, 1024 px, transparent, side view facing right):
    docs/images/roam-enemies/needle_{prowl,windup,dash,stab,stuck,hit}.png
The wind-up and dash were repainted with a different model and came out at a slightly different
size, so each pose has a scale matched by eye on the head (prowl = 1).
Output: Assets/Resources/Art/Enemies/Needle_*.png, 576 px canvases (2.25 units at 256 px per
unit), centred on the same point, with the brawler's cream cut-paper margin.
Usage: python tools/art/make_needle.py   (from the repo root)
"""
from pathlib import Path
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/roam-enemies"
OUT = ROOT / "Assets/Resources/Art/Enemies"
SIZE = 576
HALF = 0.5                    # 1024 px painting -> 512 px at scale 1
EDGE = 9
PAPER = (233, 220, 192)
THIN = 13                     # px: features thinner than this get no paper margin
POSES = {"prowl": 1.0, "windup": 1.05, "dash": 1.1, "stab": 1.0, "stuck": 1.0, "hit": 1.0}

def clean(im):
    a = im.split()[3].point(lambda v: 0 if v < 24 else v)
    im.putalpha(a)
    return im

def cut_edge(im):
    a = im.split()[3].point(lambda v: 255 if v > 90 else 0)
    # only the solid felt body gets the margin: opening the mask drops the thin thread and needle,
    # which would otherwise read as thick cream rope
    a = a.filter(ImageFilter.MinFilter(THIN)).filter(ImageFilter.MaxFilter(THIN))
    grown = a.filter(ImageFilter.MaxFilter(EDGE * 2 + 1)).filter(ImageFilter.GaussianBlur(1.2))
    card = Image.new("RGBA", im.size, PAPER + (0,))
    card.putalpha(grown)
    card.alpha_composite(im)
    return card

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, scale in POSES.items():
        im = clean(Image.open(SRC / f"needle_{name}.png").convert("RGBA"))
        side = int(1024 * HALF * scale)
        im = im.resize((side, side), Image.LANCZOS)
        canvas = Image.new("RGBA", (SIZE, SIZE))
        off = (SIZE - side) // 2
        if off >= 0:
            canvas.alpha_composite(im, (off, off))
        else:
            canvas.alpha_composite(im.crop((-off, -off, -off + SIZE, -off + SIZE)))
        cut_edge(canvas).save(OUT / f"Needle_{name.capitalize()}.png")
    print("wrote", sorted(p.name for p in OUT.glob("Needle_*.png")))

if __name__ == "__main__":
    main()
