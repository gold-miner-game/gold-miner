"""Scrat's tail (PaperSquirrelTail): cut the Meshy painting into the two tail sprites.

Source: docs/images/squirrel/tail_pieces.png (Meshy image-to-image from the in-game squirrel): left a
bushy chestnut plume, right a light inner stripe, both flat felt with dashed stitching.
The prefab's tail is two Sprites/Circle (256 px at 256 px per unit, centred) stretched into ovals:
    Tail Crown  scale 3.4986 x 7.75   (wide plume, behind)
    Tail Base   scale 1.9929 x 6.9086 (narrow stripe, in front)
Each piece is fitted into a box of its oval's proportions, then squeezed into a 256 px square, so the
prefab's own non-uniform scale draws it at its true shape, in the same outline.
Output: Assets/Resources/Art/Squirrel/TailCrown.png, TailBase.png
Usage: python tools/art/make_squirrel_tail.py   (from the repo root)
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/squirrel/tail_pieces.png"
OUT = ROOT / "Assets/Resources/Art/Squirrel"
SIZE = 256
PIECES = {"TailCrown": (0.0, 0.6, 3.4986 / 7.75), "TailBase": (0.6, 1.0, 1.9929 / 6.9086)}

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    im = Image.open(SRC).convert("RGBA")
    a = im.split()[3].point(lambda v: 0 if v < 24 else v)
    im.putalpha(a)
    for name, (x0, x1, aspect) in PIECES.items():
        piece = im.crop((int(im.width * x0), 0, int(im.width * x1), im.height))
        piece = piece.crop(piece.getbbox())
        # a box of the oval's proportions (width / height), the piece centred in it at its own shape
        h = max(piece.height, int(piece.width / aspect))
        w = int(h * aspect)
        box = Image.new("RGBA", (w, h))
        if name == "TailBase":
            # The stripe's oval sits higher than the plume's (base y 3.68 +- 3.45 vs crown 2.70 +- 3.875),
            # so the stripe is drawn only over 4-78% of its box from the bottom (world 0.5 to 5.6, inside
            # the plume, below its curl) and 70% wide, so it lies on the plume instead of poking out
            top, bottom = int(h * (1 - 0.78)), int(h * (1 - 0.04))
            pw = int(w * 0.7)
            fitted = piece.resize((pw, bottom - top), Image.LANCZOS)
            box.alpha_composite(fitted, ((w - pw) // 2, top))
        else:
            box.alpha_composite(piece, ((w - piece.width) // 2, (h - piece.height) // 2))
        box.resize((SIZE, SIZE), Image.LANCZOS).save(OUT / f"{name}.png")
    print("wrote", sorted(p.name for p in OUT.glob("*.png")))

if __name__ == "__main__":
    main()
