"""World-map hook art (PaperClaws, map mode): cut the Meshy painting into the grapple and the chain tile.

Source: docs/images/roam-enemies/grapple_pieces.png (Meshy image-to-image in the brass claw's cut-out
style): top, a forged-iron four-prong grappling hook with a cream cut-paper edge and a red felt wrap,
pointing down (eye ring at the top); bottom, a straight horizontal iron chain.
Output (Assets/Resources/Art/Fx):
    Grapple.png    256 x 256, the grapple centred, eye ring up (the rope end), prongs down
    Chain.png      (Tiles/) a seamless tile of two whole link periods, links running left to right
Usage: python tools/art/make_grapple.py   (from the repo root)
"""
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

CREAM = (236, 226, 200)   # the grapple's own cut-paper edge, so the chain reads on the dark floor too

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/images/roam-enemies/grapple_pieces.png"
OUT = ROOT / "Assets/Resources/Art/Fx"


def main():
    im = Image.open(SRC).convert("RGBA")
    a = np.array(im)[:, :, 3]
    a[a < 24] = 0
    im.putalpha(Image.fromarray(a))
    arr = np.array(im)

    # the grapple: everything above the chain band, trimmed, centred in a square, 256 px
    top = Image.fromarray(arr[:740])
    g = top.crop(top.getbbox())
    side = max(g.width, g.height) + 8
    box = Image.new("RGBA", (side, side))
    box.alpha_composite(g, ((side - g.width) // 2, (side - g.height) // 2))
    box.resize((256, 256), Image.LANCZOS).save(OUT / "Grapple.png")

    # the chain: rows of the band, two whole periods starting at a link joint (the band's thinnest column)
    alpha = arr[:, :, 3].astype(float)
    rows = np.nonzero(alpha[740:].sum(1) > 500)[0] + 740
    y0, y1 = rows.min() - 2, rows.max() + 3
    band = alpha[y0:y1].sum(0)
    span = band[100:924] - band[100:924].mean()
    lags = range(40, 260)
    period = max(lags, key=lambda k: (span[:-k] * span[k:]).sum())
    x0 = 200 + int(np.argmin(band[200:200 + period]))
    pad = 8
    tile = Image.fromarray(arr[y0 - pad:y1 + pad, x0:x0 + 2 * period])
    # a cream cut-paper margin under the iron, like the grapple's (wraps round the tile's ends, so it stays seamless)
    wide = Image.new("RGBA", (tile.width * 3, tile.height))
    for i in range(3):
        wide.alpha_composite(tile, (i * tile.width, 0))
    edge = wide.split()[3].filter(ImageFilter.MaxFilter(9)).crop((tile.width, 0, 2 * tile.width, tile.height))
    under = Image.new("RGBA", tile.size, CREAM + (0,))
    under.putalpha(edge)
    under.alpha_composite(tile)
    tile = under
    w = 256
    tile.resize((w, max(8, round(tile.height * w / tile.width))), Image.LANCZOS).save(OUT.parent / "Tiles" / "Chain.png")
    print("period", period, "start", x0, "tile", tile.size, "->", (w, round(tile.height * w / tile.width)))


if __name__ == "__main__":
    main()
