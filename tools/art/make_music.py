"""Music art (now-playing tag, sound settings, music store): cut the Meshy paintings into sprites.

Sources (Meshy image-to-image in the keyframe's cut-paper style): docs/images/music/
    record.png        a black paper vinyl record (the background removal ate its cream label)
    radio.png         a paper radio with a transparent body (background removal again): only its
                      cream felt grille is used, as the record's new label
    radio_white.png   the same radio painted on pure white, keyed here (its brown body survives)
    icons.png         a sheet of flat paper icons, 3 x 3 cells; the top two rows are used:
                      note, bell, hand / speaker, muted speaker, star
    arrows.png        back / next song, side by side
Output: Assets/Resources/Art/Music/Record.png, Record_00..11.png (its held turns), Radio.png, Icon_{Note,Bell,Hand,Speaker,Mute,Star,Back,Next}.png
Usage: python tools/art/make_music.py   (from the repo root)
"""
from collections import deque
from pathlib import Path
from PIL import Image
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/music"
OUT = ROOT / "Assets/Resources/Art/Music"

def key_white(im, tol=28):
    """Flood the near-white background in from the edges and make it transparent."""
    a = np.array(im.convert("RGBA"))
    h, w = a.shape[:2]
    white = (a[..., :3].astype(int) > 255 - tol).all(axis=2)
    seen = np.zeros((h, w), bool)
    q = deque([(y, x) for x in range(w) for y in (0, h - 1)] + [(y, x) for y in range(h) for x in (0, w - 1)])
    for y, x in q: seen[y, x] = white[y, x]
    q = deque((y, x) for y, x in q if white[y, x])
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < h and 0 <= nx < w and not seen[ny, nx] and white[ny, nx]:
                seen[ny, nx] = True
                q.append((ny, nx))
    a[seen, 3] = 0
    out = Image.fromarray(a)
    return out.crop(out.getbbox())

def square(im, size):
    im = im.crop(im.getbbox())
    side = max(im.width, im.height)
    sq = Image.new("RGBA", (side, side))
    sq.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    return sq.resize((size, size), Image.LANCZOS)

def biggest_cream_disc(im):
    """The radio's felt grille: the largest light, opaque blob."""
    a = np.array(im.convert("RGBA")).astype(int)
    cream = (a[..., 3] > 200) & (a[..., :3].mean(axis=2) > 170)
    ys, xs = np.nonzero(cream)
    cy, cx = int(np.median(ys)), int(np.median(xs))
    r = int(np.sqrt(cream.sum() / np.pi))
    box = (cx - r, cy - r, cx + r, cy + r)
    disc = im.convert("RGBA").crop(box)
    mask = Image.new("L", disc.size, 0)
    from PIL import ImageDraw
    ImageDraw.Draw(mask).ellipse((0, 0, disc.width - 1, disc.height - 1), fill=255)
    disc.putalpha(mask)
    return disc

def icons():
    sheet = Image.open(SRC / "icons.png").convert("RGBA")
    cell = sheet.width // 3
    names = [["Note", "Bell", "Hand"], ["Speaker", "Mute", "Star"]]
    out = {}
    for r, row in enumerate(names):
        for c, name in enumerate(row):
            out[name] = square(sheet.crop((c * cell, r * cell, (c + 1) * cell, (r + 1) * cell)), 256)
    return out

def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ic = icons()
    for name, im in ic.items():
        im.save(OUT / f"Icon_{name}.png")
    # back / next song: arrows.png holds the two side by side (left half back, right half next)
    arrows = Image.open(SRC / "arrows.png").convert("RGBA")
    half = arrows.width // 2
    square(arrows.crop((0, 0, half, arrows.height)), 256).save(OUT / "Icon_Back.png")
    square(arrows.crop((half, 0, arrows.width, arrows.height)), 256).save(OUT / "Icon_Next.png")

    square(key_white(Image.open(SRC / "radio_white.png")), 256).save(OUT / "Radio.png")

    # The record, with a new label: the radio's cream felt grille and a red stitched note
    record = square(Image.open(SRC / "record.png").convert("RGBA"), 512)
    label = biggest_cream_disc(Image.open(SRC / "radio.png")).resize((200, 200), Image.LANCZOS)
    record.alpha_composite(label, (156, 156))
    note = ic["Note"].resize((96, 96), Image.LANCZOS)
    n = np.array(note).astype(float)
    n[..., 0] = n[..., 0] * 0.85 + 40; n[..., 1] *= 0.22; n[..., 2] *= 0.2   # dye it red, keep the stitching
    note = Image.fromarray(np.clip(n, 0, 255).astype(np.uint8))
    record.alpha_composite(note, (208, 200))
    from PIL import ImageDraw
    ImageDraw.Draw(record).ellipse((247, 247, 265, 265), fill=(40, 30, 26, 255))   # the spindle hole
    record.save(OUT / "Record.png")
    # 12 held turns of 30 degrees (Record_00..11), drawn one per stop-motion pose: no runtime rotation,
    # so the turning record looks the same in scroll views, scaled GUI frames and over the pause popup
    small = record.resize((256, 256), Image.LANCZOS)
    for i in range(12):
        small.rotate(-30 * i, resample=Image.BICUBIC).save(OUT / f"Record_{i:02d}.png")
    print("wrote", sorted(p.name for p in OUT.glob("*.png")))

if __name__ == "__main__":
    main()
