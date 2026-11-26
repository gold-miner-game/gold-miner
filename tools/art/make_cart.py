"""Walking miners (Red Round / Gold Round) -> felt Meowmeows riding mine carts: cut the Meshy poses.

A fixed miner works a winch over a timber mine shaft (PaperMiners). A walking one has to read as
mobile even standing still, so it rides a wheeled mine cart: a red riveted tub with a stitched red
felt rim (Red Round), or an ochre-gold tub heaped with nuggets and a sack (Gold Round). The tub is
painted WITHOUT wheels; PaperMineCart adds two turning spoked wheels (Miner/WinchReel) as separate
cut-outs, so the wheels can roll a notch per pose.

Sources (Meshy image-to-image, 1024 px, transparent): docs/images/roam-enemies/cart_{red,gold}_{lurk,aim,reel,hit}.png
Output: Assets/Resources/Art/Enemies/Cart{Red,Gold}_{Lurk,Aim,Reel,Hit}.png on 360 px canvases
(1.41 units at 256 px per unit; x1.1 hole size = about 1.35 world across), cream cut-paper margin on
solid shapes only. Lurk also gets two rolling frames, rocked +-3 degrees about the tub's base (_w1/_w2):
the hole's root is a physics body, so the rocking is drawn.
Usage: python tools/art/make_cart.py   (from the repo root)
"""
from pathlib import Path
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/roam-enemies"
OUT = ROOT / "Assets/Resources/Art/Enemies"
SIZE = 360
EDGE = 6
THIN = 9
PAPER = (233, 220, 192)
POSES = ("Lurk", "Aim", "Reel", "Hit")

def clean(im):
    a = im.split()[3].point(lambda v: 0 if v < 24 else v)
    im.putalpha(a)
    return im

def cut_edge(im):
    a = im.split()[3].point(lambda v: 255 if v > 90 else 0)
    a = a.filter(ImageFilter.MinFilter(THIN)).filter(ImageFilter.MaxFilter(THIN))   # no margin on rope or bits
    grown = a.filter(ImageFilter.MaxFilter(EDGE * 2 + 1)).filter(ImageFilter.GaussianBlur(1.0))
    card = Image.new("RGBA", im.size, PAPER + (0,))
    card.putalpha(grown)
    card.alpha_composite(im)
    return card

def load(path):
    im = clean(Image.open(path).convert("RGBA"))
    im = im.resize((SIZE - 2 * EDGE, SIZE - 2 * EDGE), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE))
    canvas.alpha_composite(im, (EDGE, EDGE))
    return cut_edge(canvas)

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for kind in ("Red", "Gold"):
        for pose in POSES:
            img = load(SRC / f"cart_{kind.lower()}_{pose.lower()}.png")
            img.save(OUT / f"Cart{kind}_{pose}.png")
            if pose == "Lurk":
                base = (SIZE // 2, int(SIZE * 0.86))
                for tag, deg in (("w1", 3), ("w2", -3)):
                    img.rotate(deg, resample=Image.BICUBIC, center=base).save(OUT / f"Cart{kind}_Lurk_{tag}.png")
    print("wrote", sorted(p.name for p in OUT.glob("Cart*.png")))

if __name__ == "__main__":
    main()
