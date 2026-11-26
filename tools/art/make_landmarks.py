"""World-map landmarks (PaperLandmarks): cut the two Meshy sheets into one sprite per landmark.

Sources (Meshy image-to-image, cut-paper style, transparent, 2 x 2 cells each): docs/images/landmarks/
    sheet_a.png  camp shelter, market stall / music kiosk, chalkboard school
    sheet_b.png  mine entrance, log fort / red-eyed lair, stone ring
Output: Assets/Resources/Art/Landmarks/{Camp,Market,MusicKiosk,School,Mine,Fort,Lair,StoneRing}.png,
512 px squares, with the heroes' thin cream cut-paper margin (solid shapes only) so they read on the
dark map floor.
Usage: python tools/art/make_landmarks.py   (from the repo root)
"""
from pathlib import Path
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/landmarks"
OUT = ROOT / "Assets/Resources/Art/Landmarks"
SIZE = 512
EDGE = 7
THIN = 9
PAPER = (233, 220, 192)
SHEETS = {"sheet_a.png": [["Camp", "Market"], ["MusicKiosk", "School"]],
          "sheet_b.png": [["Mine", "Fort"], ["Lair", "StoneRing"]]}

def clean(im):
    a = im.split()[3].point(lambda v: 0 if v < 24 else v)
    im.putalpha(a)
    return im

def cut_edge(im):
    a = im.split()[3].point(lambda v: 255 if v > 90 else 0)
    a = a.filter(ImageFilter.MinFilter(THIN)).filter(ImageFilter.MaxFilter(THIN))
    grown = a.filter(ImageFilter.MaxFilter(EDGE * 2 + 1)).filter(ImageFilter.GaussianBlur(1.0))
    card = Image.new("RGBA", im.size, PAPER + (0,))
    card.putalpha(grown)
    card.alpha_composite(im)
    return card

def square(im):
    """Tight: the drawing fills the square edge to edge (only the paper margin around it), so a
    landmark sized to one map tile is one tile of drawing, like the hexagon and rocks it sits among."""
    im = im.crop(im.getbbox())
    side = max(im.width, im.height)
    sq = Image.new("RGBA", (side, side))
    sq.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    m = int(side * EDGE * 2 / (SIZE - 2 * EDGE))   # room for the cut-paper margin only
    padded = Image.new("RGBA", (side + 2 * m, side + 2 * m))
    padded.alpha_composite(sq, (m, m))
    return padded.resize((SIZE, SIZE), Image.LANCZOS)

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for sheet, grid in SHEETS.items():
        im = clean(Image.open(SRC / sheet).convert("RGBA"))
        cw, ch = im.width // 2, im.height // 2
        for r, row in enumerate(grid):
            for c, name in enumerate(row):
                cut_edge(square(im.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch)))).save(OUT / f"{name}.png")
    print("wrote", sorted(p.name for p in OUT.glob("*.png")))

if __name__ == "__main__":
    main()
