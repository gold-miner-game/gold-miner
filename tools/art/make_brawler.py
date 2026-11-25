"""Red Square -> felt Meowmeow brawler: cut the Meshy-painted poses into game sprites.

Sources (Meshy image-to-image from Miner/MinerGlare.png, 1024 px, transparent):
    docs/images/roam-enemies/brawler_{guard,windup,punch,bonk}.png
Output: Assets/Resources/Art/Enemies/Brawler_*.png, 480 px canvases (1.875 units at the art
rules' 256 px per unit; x0.8 square size = 1.5 world units), with a cream cut-paper
margin so the charcoal felt reads on the dark mine floor; every pose on the same canvas and scale so a pose change never jumps.
Guard also gets two walk frames, tipped +-5 degrees about the feet and lifted a hop
(_w1/_w2): the square's root is a physics body, so its walk is drawn, not rotated.
Usage: python tools/art/make_brawler.py   (from the repo root)
"""
from pathlib import Path
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/roam-enemies"
OUT = ROOT / "Assets/Resources/Art/Enemies"
SIZE = 480
EDGE = 9                      # cream cut-paper margin, px of the 480 canvas
PAPER = (233, 220, 192)

def clean(im):
    r, g, b, a = im.split()
    a = a.point(lambda v: 0 if v < 24 else v)
    im.putalpha(a)
    return im

def cut_edge(im):
    """A hand-cut puppet's paper margin: the figure's own outline grown by EDGE, in cream card."""
    a = im.split()[3].point(lambda v: 255 if v > 90 else 0)
    grown = a.filter(ImageFilter.MaxFilter(EDGE * 2 + 1)).filter(ImageFilter.GaussianBlur(1.2))
    card = Image.new("RGBA", im.size, PAPER + (0,))
    card.putalpha(grown)
    card.alpha_composite(im)
    return card

def load(name):
    im = clean(Image.open(SRC / f"brawler_{name}.png").convert("RGBA"))
    im = im.resize((SIZE - 2 * EDGE, SIZE - 2 * EDGE), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    canvas.alpha_composite(im, (EDGE, EDGE))
    return cut_edge(canvas)

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for src, dst in [("guard", "Guard"), ("windup", "Windup"), ("punch", "Punch"), ("bonk", "Bonk")]:
        load(src).save(OUT / f"Brawler_{dst}.png")
    guard = load("guard")
    feet = (SIZE // 2, int(SIZE * 0.86))
    for tag, deg, lift in [("w1", 5, 7), ("w2", -5, 7)]:
        tipped = guard.rotate(deg, resample=Image.BICUBIC, center=feet)
        frame = Image.new("RGBA", (SIZE, SIZE))
        frame.alpha_composite(tipped, (0, -lift))
        frame.save(OUT / f"Brawler_Guard_{tag}.png")
    print("wrote", sorted(p.name for p in OUT.glob("Brawler_*.png")))

if __name__ == "__main__":
    main()
