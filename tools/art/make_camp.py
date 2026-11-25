"""Character camp (CampHub) set pieces: cut the Meshy paintings into game sprites.

Sources (Meshy image-to-image in the keyframe's cut-paper style, 1024 px, transparent):
    docs/images/camp/campfire_{1,2,3}.png  (same fire, three flame shapes: flickers on the boil clock)
    docs/images/camp/home_{pig,cat,squirrel,frog,penguin,rhino}.png  (each hero's home: mud wallow,
    box bed, hollow stump, lily pond, igloo, rocky cave)
Output: Assets/Resources/Art/Camp/Campfire_{1,2,3}.png and Home_{Pig,Cat,Squirrel,Frog,Penguin,Rhino}.png,
512 px canvases.
The three fire frames are cropped with ONE shared box, so the stones and logs never jump between
frames. Like the other set props, no paper margin: they lie on the floor, not stand on it as puppets.
Usage: python tools/art/make_camp.py   (from the repo root)
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/camp"
OUT = ROOT / "Assets/Resources/Art/Camp"
SIZE = 512

def clean(im):
    a = im.split()[3].point(lambda v: 0 if v < 24 else v)
    im.putalpha(a)
    return im

def fit(im, box):
    im = im.crop(box)
    side = max(im.width, im.height)
    square = Image.new("RGBA", (side, side))
    square.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    return square.resize((SIZE, SIZE), Image.LANCZOS)

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    fires = [clean(Image.open(SRC / f"campfire_{i}.png").convert("RGBA")) for i in (1, 2, 3)]
    boxes = [f.getbbox() for f in fires]
    union = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    for i, f in enumerate(fires, 1):
        fit(f, union).save(OUT / f"Campfire_{i}.png")
    for name in ("pig", "cat", "squirrel", "frog", "penguin", "rhino"):
        im = clean(Image.open(SRC / f"home_{name}.png").convert("RGBA"))
        fit(im, im.getbbox()).save(OUT / f"Home_{name.capitalize()}.png")
    print("wrote", sorted(p.name for p in OUT.glob("*.png")))

if __name__ == "__main__":
    main()
