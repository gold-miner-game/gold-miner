"""World-map miners (PaperMiners, map mode): the burrow-digging Meowmeow ("Burrow Bandit").

Source: docs/images/roam-enemies/red_round_{duck,v1,pull,hurt}.png, the parked Red Round design (Meshy
image-to-image from Miner/MinerGlare): a charcoal felt cat with a red bandana in a torn-paper earth mound.
All four share one crop (the union of their outlines), so the mound stays put while the cat changes pose.
The Pull painting's hemp rope is inpainted away: the map's hook runs on an iron chain from the burrow
itself (PaperClaws), and the painted rope pointed one fixed way whatever the shot.
Output: Assets/Resources/Art/Burrow/{Duck,Aim,Pull,Hurt}.png, 256 x 256, mound centred on the burrow.
Usage: python tools/art/make_burrow.py   (from the repo root)
"""
from pathlib import Path
import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/roam-enemies"
OUT = ROOT / "Assets/Resources/Art/Burrow"
POSES = {"Duck": "red_round_duck", "Aim": "red_round_v1", "Pull": "red_round_pull", "Hurt": "red_round_hurt"}


def erase_rope(im):
    """The tan hemp rope from the paws down to the lower right: masked by colour inside that region and
    inpainted (colour and alpha), so the felt and paper mound close over where it lay."""
    a = np.array(im).astype(np.int16)
    r, g, b, al = a[..., 0], a[..., 1], a[..., 2], a[..., 3]
    tan = (r > 110) & (g > 85) & (b < 140) & (np.abs(r - g) < 50) & (r - b > 20) & (al > 0)
    region = np.zeros_like(tan)
    region[560:1024, 560:1024] = True        # below and right of the paws (the teeth are cream too)
    mask = (tan & region).astype(np.uint8) * 255
    mask = cv2.dilate(mask, np.ones((7, 7), np.uint8))
    rgb = cv2.inpaint(np.array(im)[..., :3], mask, 7, cv2.INPAINT_TELEA)
    alpha = cv2.inpaint(np.array(im)[..., 3], mask, 7, cv2.INPAINT_TELEA)
    alpha[alpha < 128] = 0                    # off the mound the rope lay on bare floor: gone, not ghosted
    # and any thin flecks of it left out there: keep only the cat-and-mound shape itself
    count, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 0).astype(np.uint8), connectivity=8)
    if count > 2:
        keep = 1 + int(np.argmax(stats[1:, cv2.CC_STAT_AREA]))
        alpha[(labels != keep) & (labels != 0)] = 0
    out = np.dstack([rgb, alpha])
    return Image.fromarray(out.astype(np.uint8), "RGBA")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ims = {}
    for name, src in POSES.items():
        im = Image.open(SRC / f"{src}.png").convert("RGBA")
        im.putalpha(im.split()[3].point(lambda v: 0 if v < 24 else v))
        if name == "Pull":
            im = erase_rope(im)
        ims[name] = im
    boxes = [im.getbbox() for im in ims.values()]
    l, t = min(b[0] for b in boxes), min(b[1] for b in boxes)
    r, b = max(b[2] for b in boxes), max(b[3] for b in boxes)
    # square round the mound's centre (horizontal middle), tall enough for the popped-up cat
    cx = (l + r) // 2
    side = max(r - l, b - t) + 8
    left, top = cx - side // 2, b + 4 - side
    for name, im in ims.items():
        box = Image.new("RGBA", (side, side))
        box.alpha_composite(im.crop((left, top, left + side, top + side)))
        box.resize((256, 256), Image.LANCZOS).save(OUT / f"{name}.png")
    print("crop", (left, top, side), "->", sorted(p.name for p in OUT.glob("*.png")))


if __name__ == "__main__":
    main()
