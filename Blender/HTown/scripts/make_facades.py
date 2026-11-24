"""Generates the facade tile textures used by the upgraded buildings (system Python + Pillow).

    python scripts/make_facades.py

Each texture tiles a whole number of bays and floors so the building UVs (metres / tile size) line up with real
floor lines. Overlay textures are transparent between windows: the building's own wall material shows through.

  facade_res.png        4 bays x 4 floors, 12.0 x 12.8 m   apartments (3.2 m floors)        RGBA cutout
  facade_shop.png       4 bays x 4 floors, 12.0 x 14.4 m   shop upper floors (3.6 m)        RGBA cutout
  storefront.png        4 bays x 1 storey, 12.0 x 4.5 m    shop ground floor                RGBA cutout
  tower_windows.png     4 bays x 4 floors,  8.8 x 14.4 m   stone (striped) towers           RGBA cutout
  curtain_wall.png      8 bays x 4 floors, 12.0 x 14.4 m   glass towers                     RGB opaque
"""
import os
import random

from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "textures")
SS = 2  # supersampling factor

# HTownSim palette.
TRIM = (230, 215, 183)
GLAZING = (40, 75, 89)
BRONZE = (43, 47, 51)
LIMESTONE = (214, 202, 176)
WARM = (255, 206, 138)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


# Lit panes of the texture being drawn (supersampled pixel boxes); save() turns them into an emission map.
LIT = []


def glass(draw, box, rng, lit_chance=0.18, blinds_chance=0.25):
    """Glass pane with a vertical sky gradient, a diagonal reflection and sometimes a lit or blinded interior."""
    x0, y0, x1, y1 = box
    top = lerp(GLAZING, (120, 150, 165), 0.45)
    bottom = lerp(GLAZING, (12, 22, 28), 0.5)
    roll = rng.random()
    if roll < lit_chance:
        LIT.append(box)
    for y in range(y0, y1):
        t = (y - y0) / max(1, y1 - y0 - 1)
        col = lerp(top, bottom, t)
        if roll < lit_chance:
            col = lerp(col, WARM, 0.55 - 0.25 * t)
        draw.line([(x0, y), (x1 - 1, y)], fill=col + (255,))
    if lit_chance <= roll < lit_chance + blinds_chance:
        slat = max(2, (y1 - y0) // 18)
        depth = rng.uniform(0.3, 0.8)
        for y in range(y0, int(y0 + (y1 - y0) * depth), slat * 2):
            draw.rectangle([x0, y, x1 - 1, y + slat - 1], fill=lerp(TRIM, (150, 140, 120), 0.4) + (255,))
    # Sky reflection streak.
    w = x1 - x0
    off = rng.uniform(-0.3, 0.5) * w
    poly = [(x0 + off, y1), (x0 + off + w * 0.35, y1), (x0 + off + w * 0.95, y0), (x0 + off + w * 0.6, y0)]
    overlay = Image.new("RGBA", draw.im.size, (0, 0, 0, 0))
    ImageDraw.Draw(overlay).polygon(poly, fill=(255, 255, 255, 34))
    mask = Image.new("L", draw.im.size, 0)
    ImageDraw.Draw(mask).rectangle(box, fill=255)
    return overlay, mask


def save(img, name):
    """Writes the texture and, from the lit panes recorded while drawing it, a matching emission map
    (<name>_emit.png: warm interior light in lit panes, black elsewhere)."""
    emit = Image.new("RGB", img.size, (0, 0, 0))
    d = ImageDraw.Draw(emit)
    for x0, y0, x1, y1 in LIT:
        for y in range(int(y0), int(y1)):
            t = (y - y0) / max(1, y1 - y0)
            d.line([(x0, y), (x1 - 1, y)], fill=lerp((255, 214, 150), (230, 150, 80), t))
    LIT.clear()
    img = img.resize((img.width // SS, img.height // SS), Image.LANCZOS)
    emit = emit.resize(img.size, Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name))
    emit.save(os.path.join(OUT, name.replace(".png", "_emit.png")))
    print("wrote", name, img.size)


def punched_windows(name, size, bays, floors, bay_m, floor_m, win_w, win_h, sill, seed, frame=TRIM):
    """Grid of punched windows with a light frame, mullion and stone sill; transparent wall between them."""
    W, H = size[0] * SS, size[1] * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = random.Random(seed)
    px_x, px_y = W / (bays * bay_m), H / (floors * floor_m)
    reflections = []
    for f in range(floors):
        for b in range(bays):
            cx = (b + 0.5) * bay_m * px_x
            floor_bottom = H - f * floor_m * px_y
            y1 = floor_bottom - sill * px_y
            y0 = y1 - win_h * px_y
            x0, x1 = cx - win_w / 2 * px_x, cx + win_w / 2 * px_x
            fr = 0.07 * px_x
            d.rectangle([x0 - fr, y0 - fr, x1 + fr, y1 + fr], fill=frame + (255,))
            box = (int(x0), int(y0), int(x1), int(y1))
            reflections.append(glass(d, box, rng))
            # Central mullion and transom.
            d.rectangle([cx - fr / 2, y0, cx + fr / 2, y1], fill=frame + (255,))
            ty = y0 + (y1 - y0) * 0.28
            d.rectangle([x0, ty - fr / 2, x1, ty + fr / 2], fill=frame + (255,))
            # Stone sill, a little wider than the opening, with a soft shadow below.
            d.rectangle([x0 - fr * 3, y1 + fr, x1 + fr * 3, y1 + fr * 3.2], fill=LIMESTONE + (255,))
            d.rectangle([x0 - fr * 2, y1 + fr * 3.2, x1 + fr * 2, y1 + fr * 4.6], fill=(0, 0, 0, 70))
            # Lintel.
            d.rectangle([x0 - fr * 2, y0 - fr * 3, x1 + fr * 2, y0 - fr], fill=LIMESTONE + (255,))
    for overlay, mask in reflections:
        img = Image.composite(Image.alpha_composite(img, overlay), img, mask)
    save(img, name)


def storefront(name="storefront.png"):
    """Bronze-framed shopfront: kickplate, big panes with lit interiors, a door, transom and a sign band left clear."""
    bays, bay_m, h_m = 4, 3.0, 4.5
    W, H = 1024 * SS, 384 * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = random.Random(7)
    px_x, px_y = W / (bays * bay_m), H / h_m
    Y = lambda m: H - m * px_y  # noqa: E731
    fr = 0.08 * px_x
    reflections = []
    # Frame block from ground to the transom top (3.45 m); 3.45..4.5 stays clear for Unity's awnings and signs.
    d.rectangle([0, Y(3.45), W, H], fill=BRONZE + (255,))
    for b in range(bays):
        bx0, bx1 = b * bay_m * px_x + fr, (b + 1) * bay_m * px_x - fr
        door = b == 1
        # Kickplate.
        if not door:
            d.rectangle([bx0, Y(0.45), bx1, H], fill=(58, 62, 66, 255))
        # Main panes (two per bay).
        lo = 0.05 if door else 0.5
        mid = (bx0 + bx1) / 2
        for x0, x1 in ((bx0, mid - fr / 2), (mid + fr / 2, bx1)):
            box = (int(x0), int(Y(2.75)), int(x1), int(Y(lo)))
            reflections.append(glass(d, box, rng, lit_chance=0.55 if not door else 0.2, blinds_chance=0.0))
        if door:
            d.rectangle([mid - fr * 0.6, Y(1.25), mid - fr * 0.2, Y(0.95)], fill=TRIM + (255,))
        # Transom panes.
        box = (int(bx0), int(Y(3.35)), int(bx1), int(Y(2.85)))
        reflections.append(glass(d, box, rng, lit_chance=0.0, blinds_chance=0.0))
    for overlay, mask in reflections:
        img = Image.composite(Image.alpha_composite(img, overlay), img, mask)
    # Thin cap flashing on top of the frame.
    d = ImageDraw.Draw(img)
    d.rectangle([0, Y(3.5), W, Y(3.42)], fill=TRIM + (255,))
    save(img, name)


def curtain_wall(name="curtain_wall.png"):
    """Opaque glass curtain wall: varied blue-grey panels, dark spandrel at each floor line, light mullions."""
    bays, floors, bay_m, floor_m = 8, 4, 1.5, 3.6
    W, H = 1024 * SS, 1228 * SS
    img = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = random.Random(11)
    px_x, px_y = W / (bays * bay_m), H / (floors * floor_m)
    base = (108, 147, 168)  # TowerGlass
    for f in range(floors):
        for b in range(bays):
            x0, x1 = b * bay_m * px_x, (b + 1) * bay_m * px_x
            yb = H - f * floor_m * px_y
            yt = yb - floor_m * px_y
            span = yb - 0.85 * px_y
            tone = rng.uniform(-0.18, 0.12)
            lit = rng.random() < 0.12
            if lit:
                LIT.append((x0, yt, x1, span))
            for y in range(int(yt), int(span)):
                t = (y - yt) / max(1, span - yt)
                col = lerp(lerp(base, (200, 220, 230), 0.35), lerp(base, (30, 50, 62), 0.55), t)
                col = lerp(col, (0, 0, 0), -tone) if tone < 0 else lerp(col, (255, 255, 255), tone)
                if lit:
                    col = lerp(col, WARM, 0.35)
                d.line([(x0, y), (x1, y)], fill=col)
            d.rectangle([x0, span, x1, yb], fill=(44, 58, 66))  # spandrel
    for b in range(bays + 1):
        x = b * bay_m * px_x
        d.rectangle([x - 0.04 * px_x, 0, x + 0.04 * px_x, H], fill=(196, 204, 206))
    for f in range(floors + 1):
        y = H - f * floor_m * px_y
        d.rectangle([0, y - 0.05 * px_y, W, y + 0.05 * px_y], fill=(170, 180, 184))
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    save(img, name)


def house(name, door):
    """Two-storey house tile, 4 bays x 2 floors (12.0 x 6.4 m): sash windows with shutters; with door=True the
    second bay of the ground floor is a panelled front door with a transom and a step."""
    bays, floors, bay_m, floor_m = 4, 2, 3.0, 3.2
    W, H = 1024 * SS, 546 * SS
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rng = random.Random(21 if door else 23)
    px_x, px_y = W / (bays * bay_m), H / (floors * floor_m)
    shutter = (62, 74, 79)
    reflections = []
    fr = 0.06 * px_x
    for f in range(floors):
        for b in range(bays):
            cx = (b + 0.5) * bay_m * px_x
            floor_bottom = H - f * floor_m * px_y
            if door and f == 0 and b == 1:
                x0, x1 = cx - 0.55 * px_x, cx + 0.55 * px_x
                y1, y0 = floor_bottom, floor_bottom - 2.25 * px_y
                d.rectangle([x0 - fr * 2, y0 - 0.45 * px_y - fr * 2, x1 + fr * 2, y1], fill=TRIM + (255,))
                d.rectangle([x0, y0, x1, y1 - fr], fill=(90, 70, 54, 255))
                for py in (0.2, 0.55):
                    for px in (0.12, 0.56):
                        d.rectangle([x0 + (x1 - x0) * px, y0 + (y1 - y0) * py, x0 + (x1 - x0) * (px + 0.32), y0 + (y1 - y0) * (py + 0.3)], fill=(75, 58, 45, 255))
                d.ellipse([x1 - 0.2 * px_x, y0 + 1.1 * px_y, x1 - 0.12 * px_x, y0 + 1.18 * px_y], fill=(200, 170, 90, 255))
                box = (int(x0), int(y0 - 0.4 * px_y), int(x1), int(y0 - fr))
                reflections.append(glass(d, box, rng, lit_chance=0.5, blinds_chance=0))
                d.rectangle([x0 - fr * 5, y1 - 0.12 * px_y, x1 + fr * 5, y1], fill=LIMESTONE + (255,))
                continue
            win_w, win_h, sill = 1.0, 1.55, 0.85
            y1 = floor_bottom - sill * px_y
            y0 = y1 - win_h * px_y
            x0, x1 = cx - win_w / 2 * px_x, cx + win_w / 2 * px_x
            # Shutters either side.
            sw = 0.42 * px_x
            for sx0 in (x0 - fr - sw - 0.04 * px_x, x1 + fr + 0.04 * px_x):
                d.rectangle([sx0, y0 - fr, sx0 + sw, y1 + fr], fill=shutter + (255,))
                for ly in range(int(y0), int(y1), max(3, int(0.12 * px_y))):
                    d.line([(sx0 + 2, ly), (sx0 + sw - 2, ly)], fill=(48, 58, 62, 255), width=max(1, SS))
            d.rectangle([x0 - fr, y0 - fr, x1 + fr, y1 + fr], fill=TRIM + (255,))
            box = (int(x0), int(y0), int(x1), int(y1))
            reflections.append(glass(d, box, rng, lit_chance=0.2, blinds_chance=0.3))
            # Sash rails: one horizontal meeting rail and muntins.
            my = (y0 + y1) / 2
            d.rectangle([x0, my - fr / 2, x1, my + fr / 2], fill=TRIM + (255,))
            d.rectangle([cx - fr / 3, y0, cx + fr / 3, y1], fill=TRIM + (255,))
            d.rectangle([x0 - fr * 2, y1 + fr, x1 + fr * 2, y1 + fr * 2.6], fill=TRIM + (255,))
    for overlay, mask in reflections:
        img = Image.composite(Image.alpha_composite(img, overlay), img, mask)
    save(img, name)


if __name__ == "__main__":
    house("house_front.png", door=True)
    house("house_side.png", door=False)
    punched_windows("facade_res.png", (1024, 1092), 4, 4, 3.0, 3.2, 1.3, 1.75, 0.9, seed=3)
    punched_windows("facade_shop.png", (1024, 1228), 4, 4, 3.0, 3.6, 1.5, 2.0, 0.95, seed=5)
    punched_windows("tower_windows.png", (626, 1024), 4, 4, 2.2, 3.6, 0.95, 2.3, 0.75, seed=9, frame=(190, 180, 160))
    storefront()
    curtain_wall()
