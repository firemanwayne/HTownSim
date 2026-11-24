"""Draws the "07" decal atlas for the Hi3D engine (system Python + Pillow).

    python make_decals_07.py

Reads decals_07.json (written in Blender: each decal's size, text colour and the paint colour sampled around the old
"43"), packs one cell per decal into a 1024 x 1024 atlas (Engine07_Decals.png in the Unity vehicle folder) and writes
the cells' UV rectangles back into decals_07.json for the Blender step that builds the decal quads.
"""
import json
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = os.path.join(HERE, "decals_07.json")
NUMBER = os.environ.get("HFD_UNIT_NUMBER", "07")  # the unit number drawn on the truck; other numbers go to the Station8 folder
OUT_DIR = os.environ.get("HFD_OUT_DIR", r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Station8")
OUT = (
    r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Engine07Hi3D\Engine07_Decals.png"
    if NUMBER == "07"
    else os.path.join(OUT_DIR, "Engine" + NUMBER + "_Decals.png")
)
SIZE = 1024
CELL_W = 330  # three cells per row, so all seven fit in 1024 x 1024
FEATHER = 0.12  # fraction of the cell's shorter side that fades out at the edges

FONT_CANDIDATES = [r"C:\Windows\Fonts\ariblk.ttf", r"C:\Windows\Fonts\impact.ttf", r"C:\Windows\Fonts\arialbd.ttf"]
FONT = next(p for p in FONT_CANDIDATES if os.path.exists(p))

TEXT = {"white": ((238, 236, 228), (95, 14, 10)), "red": ((172, 34, 30), (110, 110, 118))}


def to8(c):
    return tuple(int(max(0, min(1, v)) * 255 + 0.5) for v in c)


def cell(decal):
    w = CELL_W
    h = int(CELL_W * decal["h"] / decal["w"])
    img = Image.new("RGBA", (w, h), to8(decal["bg"]) + (255,))
    d = ImageDraw.Draw(img)
    fill, stroke = TEXT[decal["text"]]
    # Fit "07" to about 82% of the cell height (or width, if narrower).
    size = int(h * 0.95)
    while True:
        font = ImageFont.truetype(FONT, size)
        x0, y0, x1, y1 = d.textbbox((0, 0), NUMBER, font=font, stroke_width=max(2, size // 22))
        if (x1 - x0) <= w * 0.8 and (y1 - y0) <= h * 0.82:
            break
        size -= 4
    sw = max(2, size // 22)
    d.text(((w - (x1 - x0)) / 2 - x0, (h - (y1 - y0)) / 2 - y0), NUMBER, font=font, fill=fill + (255,), stroke_width=sw, stroke_fill=stroke + (255,))
    # Feathered alpha so the patch melts into the surrounding paint.
    edge = max(2, int(min(w, h) * FEATHER))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rectangle([edge, edge, w - edge, h - edge], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(edge / 2))
    img.putalpha(mask)
    return img


def main():
    decals = json.load(open(SPEC))
    atlas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    x = y = row_h = 0
    for decal in decals:
        img = cell(decal)
        if x + img.width > SIZE:
            x, y, row_h = 0, y + row_h + 8, 0
        atlas.paste(img, (x, y))
        # UVs: Blender/Unity v runs bottom-up.
        decal["uv"] = [x / SIZE, 1 - (y + img.height) / SIZE, (x + img.width) / SIZE, 1 - y / SIZE]
        x += img.width + 8
        row_h = max(row_h, img.height)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    atlas.save(OUT)
    if NUMBER == "07":
        json.dump(decals, open(SPEC, "w"), indent=1)
    print("font", os.path.basename(FONT), "-> wrote", OUT, "with", len(decals), "decals")


if __name__ == "__main__":
    main()
