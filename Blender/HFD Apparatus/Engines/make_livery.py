"""Draws Engine 07's livery atlas (system Python + Pillow), matched to the HFD Spartan delivery photos.

    python make_livery.py

Reads livery_layout.json (written by build_engine07.py: atlas regions, windows, doors, body sections) and writes
Engine07_Livery.png (4096 x 2048, 200 px per metre) into the Unity vehicle folder. Each region is a planar view of the
shell, so everything here is placed in truck metres: y along the truck (front -5.05 .. rear 5.05), z up, x across
(+X = truck's left side). Side drawings are written once in truck coordinates and mirror themselves on the right side.
  Cab: HFD red with a white band (thin line, wide stripe, thin line) under the windows; front-door HFD shield,
  station patch over the front wheel, Maltese-cross patch on the crew door; black window surrounds; diamond-plate steps.
  Pump house: black operator panel (full on the driver's side, smaller on the officer's side) with gauges, valve
  handles and colour tags; red crosslay cover and black instrument box above a stainless shelf.
  Body: red roll-up compartment doors; the band runs into a "Z" of outlined speed lines that climbs to a top band
  running to the rear; gold "Courage, Commitment and Compassion"; white "07"; stainless rub rail with red reflective.
  Front: the band across the fascia, gold HOUSTON FIRE above the grille. Rear: lime and red chevrons, red vinyl
  hose-bed cover, diamond-plate shelf, "07" compartment door, rear compartment frame and plate. Top: diamond plate,
  hose bed, roof "07".
"""
import json
import math
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
NUMBER = os.environ.get("HFD_UNIT_NUMBER", "07")  # the unit number drawn on the truck; other numbers go to the Station8 folder
OUT_DIR = os.environ.get("HFD_OUT_DIR", r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Station8")
ORIGINAL_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Engine07Hi3D"
OUT = os.path.join(ORIGINAL_DIR if NUMBER == "07" else OUT_DIR, "Engine" + NUMBER + "_Livery.png")
L = json.load(open(os.path.join(HERE, "livery_layout.json")))
W, H = L["atlas"]
S = L["ppm"]
R = L["regions"]

RED = (176, 22, 26)
RED_DARK = (112, 12, 15)
RED_SLAT = (150, 18, 22)
VINYL = (150, 20, 26)
WHITE = (240, 240, 236)
GOLD = (214, 172, 64)
LIME = (206, 232, 28)
NAVY = (24, 44, 104)
BLACK = (26, 27, 30)
CHROME = (214, 216, 220)
STEEL = (170, 174, 180)
SEAM = (82, 10, 12)
FONT_BLOCK = r"C:\Windows\Fonts\ariblk.ttf"
FONT_BOLD = r"C:\Windows\Fonts\arialbd.ttf"
FONT_SCRIPT = r"C:\Windows\Fonts\BRUSHSCI.TTF"

img = Image.new("RGB", (W, H), RED)
d = ImageDraw.Draw(img)


# ------------------------------------------------------------------------------------------------ coordinates


def side_px(side, y, z):
    px, py, _, _ = R[side]
    a = (y + 5.05) if side == "left" else (5.05 - y)
    return (px + a * S, py + (3.2 - z) * S)


def side_poly(side, pts, fill, outline=None, width=1):
    d.polygon([side_px(side, y, z) for y, z in pts], fill=fill, outline=outline, width=width)


def side_rect(side, y0, y1, z0, z1, fill, outline=None, width=1):
    a, b = side_px(side, y0, z1), side_px(side, y1, z0)
    d.rectangle([min(a[0], b[0]), a[1], max(a[0], b[0]), b[1]], fill=fill, outline=outline, width=width)


def side_line(side, p0, p1, fill, width):
    d.line([side_px(side, *p0), side_px(side, *p1)], fill=fill, width=width)


def side_circle(side, y, z, r, fill, outline=None, width=1):
    c = side_px(side, y, z)
    d.ellipse([c[0] - r * S, c[1] - r * S, c[0] + r * S, c[1] + r * S], fill=fill, outline=outline, width=width)


def region_px(region, a, b):
    px, py, _, _ = R[region]
    return (px + a * S, py + b * S)


def rect_px(region, a0, b0, a1, b1, fill, outline=None, width=1):
    p0, p1 = region_px(region, a0, b0), region_px(region, a1, b1)
    d.rectangle([min(p0[0], p1[0]), min(p0[1], p1[1]), max(p0[0], p1[0]), max(p0[1], p1[1])], fill=fill, outline=outline, width=width)


def rear_rect(x0, x1, z0, z1, fill, outline=None, width=1):
    """Rect on the rear face from truck x (Blender, +X = truck's left = image left) and height z."""
    rect_px("rear", 1.45 - max(x0, x1), 3.2 - z1, 1.45 - min(x0, x1), 3.2 - z0, fill, outline, width)


def text_box(text, font_path, box, fill, stroke=None, stroke_w=0, rotate=0):
    """Draws text centred and fitted in a pixel box (x0, y0, x1, y1), never mirrored."""
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    if rotate in (90, 270):
        bw, bh = bh, bw
    size = int(bh * 1.2)
    while size > 6:
        font = ImageFont.truetype(font_path, size)
        tb = ImageDraw.Draw(Image.new("L", (1, 1))).textbbox((0, 0), text, font=font, stroke_width=stroke_w)
        if tb[2] - tb[0] <= bw and tb[3] - tb[1] <= bh:
            break
        size -= 2
    layer = Image.new("RGBA", (int(tb[2] - tb[0]) + 8, int(tb[3] - tb[1]) + 8), (0, 0, 0, 0))
    ImageDraw.Draw(layer).text((4 - tb[0], 4 - tb[1]), text, font=font, fill=fill, stroke_width=stroke_w, stroke_fill=stroke)
    if rotate:
        layer = layer.rotate(rotate, expand=True, resample=Image.BICUBIC)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    img.paste(layer, (int(cx - layer.width / 2), int(cy - layer.height / 2)), layer)


def side_text(side, text, font, y0, y1, z0, z1, fill, stroke=None, stroke_w=0):
    a, b = side_px(side, y0, z1), side_px(side, y1, z0)
    text_box(text, font, (min(a[0], b[0]), a[1], max(a[0], b[0]), b[1]), fill, stroke, stroke_w)


def diamond_plate(x0, y0, x1, y1):
    """Embossed aluminium tread plate (pixel box)."""
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    d.rectangle([x0, y0, x1, y1], fill=(190, 194, 198))
    step = 12
    for j, yy in enumerate(range(int(y0) + 3, int(y1) - 2, step)):
        for xx in range(int(x0) + 4 + (j % 2) * step // 2, int(x1) - 4, step):
            d.line([(xx - 3, yy + 2), (xx + 3, yy - 2)], fill=(150, 154, 160), width=3)
            d.line([(xx - 2, yy + 3), (xx + 4, yy - 1)], fill=(232, 234, 236), width=1)


def side_diamond(side, y0, y1, z0, z1):
    a, b = side_px(side, y0, z1), side_px(side, y1, z0)
    diamond_plate(a[0], a[1], b[0], b[1])


# ------------------------------------------------------------------------------------------------ sides

CAB_BAND = [(0.92, 0.945), (0.98, 1.13), (1.165, 1.19)]   # thin line, wide stripe, thin line (photos)
Z_LOW = (0.92, 1.19)                                        # the body "Z" picks the band up at the same height
Z_TOP = (1.98, 2.26)
Z_BOTTOM_END, Z_TOP_START, Z_W = 1.72, 0.86, 0.28            # Z corners along the body (y)
BODY_DIVIDERS = (1.80, 3.33)


def speed_line_z(side):
    """The body "Z": outlined strokes filled with speed lines parallel to each stroke."""
    body = L["body"]
    strokes = [
        ([(body["y0"], Z_LOW[0]), (Z_BOTTOM_END, Z_LOW[0]), (Z_BOTTOM_END, Z_LOW[1]), (body["y0"], Z_LOW[1])], "h"),
        ([(Z_BOTTOM_END - Z_W, Z_LOW[0]), (Z_BOTTOM_END, Z_LOW[0]), (Z_TOP_START + Z_W, Z_TOP[1]), (Z_TOP_START, Z_TOP[1])], "d"),
        ([(Z_TOP_START, Z_TOP[0]), (body["y1"], Z_TOP[0]), (body["y1"], Z_TOP[1]), (Z_TOP_START, Z_TOP[1])], "h"),
    ]
    mask = Image.new("L", (W, H), 0)
    md = ImageDraw.Draw(mask)
    lines = Image.new("L", (W, H), 0)
    ld = ImageDraw.Draw(lines)
    for pts, kind in strokes:
        md.polygon([side_px(side, *p) for p in pts], fill=255)
        for k in range(1, 6):
            t = k / 6
            if kind == "h":
                z = pts[0][1] + t * (pts[2][1] - pts[0][1])
                ld.line([side_px(side, pts[0][0] - 0.3, z), side_px(side, pts[1][0] + 0.3, z)], fill=255, width=4)
            else:
                # Parallel to the diagonal's long edges, overshooting both ends (the mask trims them).
                ya, za = pts[0][0] + t * Z_W, Z_LOW[0]
                yb, zb = pts[3][0] + t * Z_W, Z_TOP[1]
                ey, ez = (yb - ya) * 0.2, (zb - za) * 0.2
                ld.line([side_px(side, ya - ey, za - ez), side_px(side, yb + ey, zb + ez)], fill=255, width=4)
    inner = mask.filter(ImageFilter.MinFilter(11))
    outline = ImageChops.subtract(mask, inner)
    fill = ImageChops.multiply(lines, inner.filter(ImageFilter.MinFilter(5)))
    white = ImageChops.lighter(outline, fill)
    img.paste(Image.new("RGB", (W, H), WHITE), (0, 0), white)


def patch_shield(side, cy, cz, sw, sh):
    """HFD shoulder-patch style shield: gold rim, navy field, red/white bars, HOUSTON."""
    pts = [(cy - sw / 2, cz + sh / 2), (cy + sw / 2, cz + sh / 2), (cy + sw / 2, cz - sh * 0.10), (cy, cz - sh / 2), (cy - sw / 2, cz - sh * 0.10)]
    side_poly(side, pts, GOLD)
    side_poly(side, [(cy + (y - cy) * 0.86, cz + (z - cz) * 0.86) for y, z in pts], NAVY)
    side_rect(side, cy - sw * 0.30, cy + sw * 0.30, cz - 0.02, cz + 0.07, WHITE)
    side_rect(side, cy - sw * 0.30, cy + sw * 0.30, cz - 0.10, cz - 0.02, RED)
    side_text(side, "HOUSTON", FONT_BOLD, cy - sw * 0.36, cy + sw * 0.36, cz + 0.09, cz + 0.16, GOLD)


def patch_station(side, y, z, r):
    """Round station patch: gold ring, dark red, white centre with 07."""
    side_circle(side, y, z, r, GOLD)
    side_circle(side, y, z, r * 0.88, RED_DARK)
    side_circle(side, y, z, r * 0.56, WHITE)
    c = side_px(side, y, z)
    pr = r * S
    text_box(NUMBER, FONT_BLOCK, (c[0] - pr * 0.42, c[1] - pr * 0.34, c[0] + pr * 0.42, c[1] + pr * 0.34), RED)
    text_box("STATION", FONT_BOLD, (c[0] - pr * 0.6, c[1] - pr * 0.84, c[0] + pr * 0.6, c[1] - pr * 0.62), GOLD)
    text_box("HFD", FONT_BOLD, (c[0] - pr * 0.4, c[1] + pr * 0.62, c[0] + pr * 0.4, c[1] + pr * 0.84), GOLD)


def patch_maltese(side, y, z, r):
    """Maltese cross badge: red cross with gold edge and a white centre."""
    c = side_px(side, y, z)
    pr = r * S
    arms = []
    for k in range(4):
        a = math.radians(90 * k)
        ca, sa = math.cos(a), math.sin(a)
        tri = [(0.18, 0), (1.0, -0.62), (0.78, 0), (1.0, 0.62)]
        arms.append([(c[0] + pr * (u * ca - v * sa), c[1] + pr * (u * sa + v * ca)) for u, v in tri])
    for arm in arms:
        d.polygon(arm, fill=GOLD)
        shrunk = [(c[0] + (x - c[0]) * 0.86, c[1] + (yy - c[1]) * 0.86) for x, yy in arm]
        d.polygon(shrunk, fill=(200, 28, 32))
    d.ellipse([c[0] - pr * 0.34, c[1] - pr * 0.34, c[0] + pr * 0.34, c[1] + pr * 0.34], fill=GOLD)
    d.ellipse([c[0] - pr * 0.27, c[1] - pr * 0.27, c[0] + pr * 0.27, c[1] + pr * 0.27], fill=WHITE)
    text_box(NUMBER, FONT_BLOCK, (c[0] - pr * 0.2, c[1] - pr * 0.16, c[0] + pr * 0.2, c[1] + pr * 0.16), RED)


def pump_panel(side):
    """Pump house side: black panel(s) with gauges and valves, stainless shelf, crosslay cover and instrument box."""
    pump = L["pump"]
    y0, y1, z1 = pump["y0"], pump["y1"], pump["z1"]
    driver = side == "left"
    side_diamond(side, y0, y1, 0.48, 0.60)                                       # running board
    side_rect(side, y0, y1, 0.60, 1.80, STEEL)                                   # stainless frame
    side_rect(side, y0 + 0.04, y1 - 0.04, 0.63, 1.77, BLACK)
    side_rect(side, y0, y1, 1.80, 1.86, CHROME)                                  # shelf
    # Upper: red crosslay cover toward the cab, black instrument box toward the body, stainless top edge.
    mid = (y0 + y1) / 2
    side_rect(side, y0 + 0.03, mid - 0.02, 1.88, z1 - 0.06, VINYL, RED_DARK, 3)
    for k in range(1, 3):
        yy = y0 + 0.03 + k * (mid - 0.05 - y0) / 3
        side_line(side, (yy, 1.90), (yy, z1 - 0.08), RED_DARK, 2)
    side_rect(side, mid + 0.02, y1 - 0.03, 1.88, z1 - 0.06, BLACK, STEEL, 3)
    for gy in (mid + 0.17, mid + 0.42):
        side_circle(side, gy, 2.12, 0.085, (240, 240, 236), CHROME, 4)
        side_line(side, (gy, 2.12), (gy + 0.05, 2.15), (200, 20, 20), 3)
    side_rect(side, y0, y1, z1 - 0.05, z1, CHROME)
    # Lower panel contents.
    tags = ((214, 40, 40), (40, 90, 200), (232, 200, 40), (40, 160, 70), (240, 240, 240), (230, 120, 30))
    if driver:
        for row, z in enumerate((1.66, 1.53)):                                  # gauge rows
            for k in range(9):
                gy = y0 + 0.14 + k * (y1 - y0 - 0.28) / 8
                side_circle(side, gy, z, 0.045, (236, 236, 232), CHROME, 3)
        for k in range(6):                                                       # valve T-handles with colour tags
            gy = y0 + 0.20 + k * (y1 - y0 - 0.40) / 5
            side_rect(side, gy - 0.05, gy + 0.05, 1.37, 1.40, CHROME)
            side_rect(side, gy - 0.012, gy + 0.012, 1.28, 1.37, CHROME)
            side_rect(side, gy - 0.04, gy + 0.04, 1.20, 1.24, tags[k])
        side_circle(side, mid, 0.98, 0.13, CHROME, (90, 90, 95), 4)              # steamer intake
        side_circle(side, mid, 0.98, 0.08, (40, 40, 44))
        for gy in (y0 + 0.24, y1 - 0.24):                                        # 2.5 in discharges
            side_circle(side, gy, 0.94, 0.07, CHROME, (90, 90, 95), 3)
            side_circle(side, gy, 0.94, 0.04, (40, 40, 44))
        side_rect(side, mid - 0.30, mid + 0.30, 0.70, 0.74, (232, 200, 40))      # warning labels
    else:
        for k, gy in enumerate((mid - 0.32, mid + 0.30)):
            side_circle(side, gy, 1.55, 0.07, CHROME, (90, 90, 95), 3)
            side_circle(side, gy, 1.55, 0.04, (40, 40, 44))
            side_circle(side, gy + 0.16, 1.55, 0.05, tags[k], CHROME, 3)
        side_circle(side, mid, 1.05, 0.13, CHROME, (90, 90, 95), 4)
        side_circle(side, mid, 1.05, 0.08, (40, 40, 44))
        side_circle(side, mid - 0.30, 1.05, 0.07, CHROME, (90, 90, 95), 3)
        side_circle(side, mid + 0.30, 1.25, 0.05, (232, 200, 40), CHROME, 3)


def draw_side(side):
    body, cab = L["body"], L["cab"]
    # Cab band, from the cab front to the back of the cab.
    for z0, z1 in CAB_BAND:
        side_rect(side, -4.46, cab["y1"], z0, z1, WHITE)
    # Doors: steps (diamond plate) below, outlines, handles.
    for y0, y1, z0, z1 in L["doors"]:
        sy0 = y0 if y0 < -3 else -1.74
        side_diamond(side, sy0, y1, 0.42, 0.60)
        side_diamond(side, sy0, y1, 0.64, 0.79)
        side_rect(side, y0, y1, z0, z1, None, SEAM, 3)
        hy = y1 - 0.12 if y0 < -3 else y0 + 0.12
        side_rect(side, hy - 0.08, hy + 0.08, 1.60, 1.64, CHROME, (90, 90, 95), 1)
    # Black window surrounds (the glass sits in the cut-outs).
    for y0, y1, z0, z1 in L["windows"]:
        side_rect(side, y0 - 0.035, y1 + 0.035, z0 - 0.035, z1 + 0.035, BLACK)
    # Patches: the HFD logo on the front doors and the Texas state flag over the front wheel are separate decals
    # (Engine07_Decals) so they stay sharp; the station patch is on the crew door below the window (photos).
    patch_station(side, -1.42, 1.48, 0.17)
    # Body: red roll-up doors (slats), compartment dividers, the Z, motto, number, rub rail.
    side_rect(side, body["y0"], body["y1"], 0.55, 2.70, RED)
    zz = 0.58
    while zz < 2.68:
        side_line(side, (body["y0"], zz), (body["y1"], zz), RED_SLAT, 2)
        zz += 0.045
    for yy in (body["y0"] + 0.02,) + BODY_DIVIDERS + (body["y1"] - 0.02,):
        side_rect(side, yy - 0.018, yy + 0.018, 0.55, 2.72, STEEL)
    side_rect(side, BODY_DIVIDERS[0], BODY_DIVIDERS[1], 1.42, 1.47, STEEL)      # drip rail over the rear wheel bay
    speed_line_z(side)
    side_text(side, "Courage, Commitment and Compassion", FONT_SCRIPT, 1.95, 3.20, 2.33, 2.47, GOLD, RED_DARK, 1)
    side_text(side, NUMBER, FONT_BOLD, 3.62, 4.22, 1.02, 1.72, WHITE)
    side_rect(side, body["y0"], body["y1"], 2.72, 2.78, CHROME)                 # diamond-plate top rail edge
    side_rect(side, body["y0"], body["y1"], 0.48, 0.58, CHROME)                 # rub rail
    side_rect(side, body["y0"], body["y1"], 0.515, 0.545, (200, 20, 28))         # red reflective strip
    pump_panel(side)
    # "07" on the bumper's angled end (photos).
    side_text(side, NUMBER, FONT_BOLD, -4.975, -4.86, 0.60, 0.81, WHITE)


# ------------------------------------------------------------------------------------------------ top, front, rear


def draw_top():
    for y0, y1, half in ((-0.70, 0.70, 1.24), (0.70, 4.45, 1.40), (-4.99, -4.40, 1.38), (4.40, 5.05, 1.30)):
        p0, p1 = region_px("top", y0 + 5.05, 1.45 - half), region_px("top", y1 + 5.05, 1.45 + half)
        diamond_plate(p0[0], p0[1], p1[0], p1[1])
    # Bumper compartment cover between the frame rails (smooth aluminium, hinged at the back, latch at the front).
    c0, c1 = region_px("top", -4.90 + 5.05, 1.45 - 0.40), region_px("top", -4.50 + 5.05, 1.45 + 0.40)
    d.rectangle([c0[0], c0[1], c1[0], c1[1]], fill=(204, 207, 211), outline=(110, 114, 120), width=4)
    d.line([(c1[0] - 10, c0[1] + 6), (c1[0] - 10, c1[1] - 6)], fill=(140, 144, 150), width=3)
    p0, p1 = region_px("top", 1.0 + 5.05, 1.45 - 0.80), region_px("top", 4.35 + 5.05, 1.45 + 0.60)
    d.rectangle([p0[0], p0[1], p1[0], p1[1]], fill=(46, 48, 52))
    for k in range(8):                                                           # hose loads
        yy = p0[1] + 10 + k * (p1[1] - p0[1] - 20) / 8
        col = ((214, 40, 40), (232, 200, 40))[k % 2]
        d.rectangle([p0[0] + 10, yy, p1[0] - 10, yy + (p1[1] - p0[1]) / 8 - 8], fill=col)
    c0, c1 = region_px("top", -3.0 + 5.05, 1.45 - 0.62), region_px("top", -1.6 + 5.05, 1.45 + 0.62)
    text_box(NUMBER, FONT_BLOCK, (c0[0], c0[1], c1[0], c1[1]), WHITE, RED_DARK, 4, rotate=90)


def draw_front():
    for z0, z1 in CAB_BAND:
        rect_px("front", 0.0, 3.2 - z1, 2.9, 3.2 - z0, WHITE)
    rect_px("front", 1.45 - 1.39, 3.2 - 0.87, 1.45 + 1.39, 3.2 - 0.54, RED)              # bumper face stays red
    # Black surround around the windshield; gold name between it and the grille (photos).
    rect_px("front", 1.45 - 1.33, 3.2 - 2.66, 1.45 + 1.33, 3.2 - 1.715, BLACK)
    text_box("HOUSTON FIRE", FONT_BOLD, (*region_px("front", 1.45 - 0.40, 3.2 - 1.708), *region_px("front", 1.45 + 0.40, 3.2 - 1.632)), GOLD, RED_DARK, 1)


def draw_rear():
    # Chevrons: 6 in lime and red stripes at 45 degrees, down and out from the centre, over the whole rear face.
    x0, y0 = region_px("rear", 0.05, 3.2 - 2.76)
    x1, y1 = region_px("rear", 2.85, 3.2 - 0.76)
    chev = Image.new("RGB", (int(x1 - x0), int(y1 - y0)), RED)
    cd = ImageDraw.Draw(chev)
    stripe = int(0.15 * S * 1.414)
    cx = chev.width / 2
    for k in range(-40, 40):
        col = LIME if k % 2 else RED
        for sgn in (-1, 1):
            off = k * stripe
            pts = [(cx, off), (cx, off + stripe), (cx + sgn * chev.height * 2, off + stripe + chev.height * 2), (cx + sgn * chev.height * 2, off + chev.height * 2)]
            cd.polygon(pts, fill=col)
    img.paste(chev, (int(x0), int(y0)))
    # Top diamond-plate rail; hose-bed cover (red vinyl, two flaps, black straps); diamond-plate shelf.
    a0, b0 = region_px("rear", 0.0, 3.2 - 2.78)
    a1, b1 = region_px("rear", 2.9, 3.2 - 2.70)
    diamond_plate(a0, b0, a1, b1)
    rear_rect(-0.64, 0.98, 1.82, 2.70, VINYL, RED_DARK, 3)
    rear_rect(0.15, 0.19, 1.84, 2.68, RED_DARK)
    for x in (0.62, -0.30):
        rear_rect(x - 0.03, x + 0.03, 1.82, 1.96, BLACK)
    s0, s1 = region_px("rear", 1.45 - 1.06, 3.2 - 1.82), region_px("rear", 1.45 + 0.70, 3.2 - 1.70)
    diamond_plate(s0[0], s0[1], s1[0], s1[1])
    # Upper compartment on the truck's right: diamond-plate top, red door with "07" and a chrome latch.
    t0, t1 = region_px("rear", 1.45 + 0.66, 3.2 - 2.70), region_px("rear", 1.45 + 1.02, 3.2 - 2.38)
    diamond_plate(t0[0], t0[1], t1[0], t1[1])
    rear_rect(-1.02, -0.66, 1.56, 2.38, RED, RED_DARK, 3)
    p0, p1 = region_px("rear", 1.45 + 0.70, 3.2 - 2.30), region_px("rear", 1.45 + 0.98, 3.2 - 1.94)
    text_box(NUMBER, FONT_BOLD, (p0[0], p0[1], p1[0], p1[1]), WHITE)
    c = region_px("rear", 1.45 + 0.84, 3.2 - 1.75)
    d.ellipse([c[0] - 12, c[1] - 12, c[0] + 12, c[1] + 12], fill=CHROME, outline=(90, 90, 95), width=2)
    # Lower centre compartment: stainless frame around the door.
    rear_rect(-0.56, 0.55, 0.80, 1.56, None, CHROME, 8)
    # Tail light stack and upper warning light backgrounds (the housings are chrome trim on top).
    for s in (-1, 1):
        rear_rect(s * 1.08, s * 1.34, 0.80, 1.32, CHROME)
        rear_rect(s * 1.06, s * 1.36, 2.36, 2.68, CHROME)
    # Licence plate, lower left.
    rear_rect(0.69, 1.04, 0.84, 1.01, (236, 236, 232), (60, 60, 60), 2)
    p0, p1 = region_px("rear", 1.45 - 1.02, 3.2 - 0.98), region_px("rear", 1.45 - 0.71, 3.2 - 0.88)
    text_box("HFD " + NUMBER, FONT_BOLD, (p0[0], p0[1], p1[0], p1[1]), (30, 30, 30))
    # Tailboard face: tread plate.
    t0, t1 = region_px("rear", 1.45 - 1.30, 3.2 - 0.76), region_px("rear", 1.45 + 1.30, 3.2 - 0.48)
    diamond_plate(t0[0], t0[1], t1[0], t1[1])


for side in ("left", "right"):
    draw_side(side)
draw_top()
draw_front()
draw_rear()
rect_px("under", 0, 0, 0.5, 0.5, (30, 30, 32))
img = img.filter(ImageFilter.SMOOTH)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
img.save(OUT)
print("wrote", OUT, img.size)

# Door logo decal texture: the department's logo (Sample-Asset-Images), trimmed to the shield, 1024 px tall.
LOGO_SRC = r"J:\Unity\projects\HTownSim\Sample-Asset-Images\HFD_Logo_May_2025.png"
LOGO_OUT = os.path.join(ORIGINAL_DIR, "HFD_Logo.png")
logo = Image.open(LOGO_SRC).convert("RGBA")
logo = logo.crop(logo.getchannel("A").getbbox())
logo = logo.resize((round(logo.width * 1024 / logo.height), 1024), Image.LANCZOS)
logo.save(LOGO_OUT)
print("wrote", LOGO_OUT, logo.size)

# Texas state-flag decal: the source is a JPEG on white, and the flag's own white stripe is inside the state's navy
# outline, so only the white connected to the image border is made transparent.
TEXAS_SRC = r"J:\Unity\projects\HTownSim\Sample-Asset-Images\texas-flag-state-free-vector.jpg"
TEXAS_OUT = os.path.join(ORIGINAL_DIR, "Texas_Flag.png")
tx = Image.open(TEXAS_SRC).convert("RGB")
tx = tx.resize((tx.width * 2, tx.height * 2), Image.LANCZOS)
light = tx.convert("L").point(lambda v: 255 if v > 200 else 0)
seed = light.copy()
for x in range(seed.width):
    for y in (0, seed.height - 1):
        if seed.getpixel((x, y)) == 255:
            ImageDraw.floodfill(seed, (x, y), 128)
for y in range(seed.height):
    for x in (0, seed.width - 1):
        if seed.getpixel((x, y)) == 255:
            ImageDraw.floodfill(seed, (x, y), 128)
outside = seed.point(lambda v: 255 if v == 128 else 0).filter(ImageFilter.MaxFilter(3))
alpha = ImageChops.invert(outside).filter(ImageFilter.GaussianBlur(1))
tx = tx.convert("RGBA")
tx.putalpha(alpha)
# Thin white border round the state, like the decal on the trucks (photos).
pad = 24
canvas = Image.new("RGBA", (tx.width + 2 * pad, tx.height + 2 * pad), (0, 0, 0, 0))
border_alpha = Image.new("L", canvas.size, 0)
border_alpha.paste(alpha, (pad, pad))
border_alpha = border_alpha.filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.GaussianBlur(1))
canvas.paste(Image.new("RGBA", canvas.size, (255, 255, 255, 255)), (0, 0), border_alpha)
canvas.alpha_composite(tx, (pad, pad))
tx = canvas.crop(canvas.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox())
tx.save(TEXAS_OUT)
print("wrote", TEXAS_OUT, tx.size)
