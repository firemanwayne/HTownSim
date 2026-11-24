"""Draws the livery atlas for Ambulance 07 (system Python + Pillow), from the Houston Fire ambulance photos.

    python make_livery_amb.py

Writes Ambulance07_Livery.png (4096 x 2048, 270 px/m, planar regions from amb_layout.py) and the door-shield texture
HFD_Logo.png beside it, in the Unity vehicle folder. Layout follows Ambulance 2, 8, 10, 55, 68 and Medic 60:
  HFD red body, white band with red pin lines from bumper to tailgate; blue "HOUSTON FIRE" arched over the round emblem
  with "EMS" and a white heartbeat line (white-outlined blue, both sides); gold "Courage, Commitment and Compassion";
  "AMBULANCE 07" at the rear of each side; "EMERGENCY 911" in the band; the crest on the curb-side front; diamond-plate
  wheel-well panels; the driver-side condenser grille; red and lime chevrons on the rear with "HFD EMS", "A" and "07".
"""
import math
import os
import shutil
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import amb_layout as A  # noqa: E402

UNITY = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Ambulance07"
OUT = os.path.join(UNITY, "Ambulance07_Livery.png")
LOGO_SRC = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Engine07Hi3D\HFD_Logo.png"
S = A.PPM

RED = (176, 22, 26)
RED_DARK = (110, 12, 15)
SEAM = (92, 10, 13)
WHITE = (240, 240, 236)
BLUE = (28, 76, 178)
BLUE_DARK = (14, 36, 104)
GOLD = (214, 172, 64)
LIME = (206, 232, 28)
CHROME = (214, 216, 220)
STEEL = (170, 174, 180)
BLACK = (24, 25, 28)
GREY = (74, 76, 82)
FONT_BLACK = r"C:\Windows\Fonts\ariblk.ttf"
FONT_BOLD = r"C:\Windows\Fonts\arialbd.ttf"
FONT_SCRIPT = r"C:\Windows\Fonts\BRUSHSCI.TTF"

W, H = A.ATLAS
img = Image.new("RGB", (W, H), RED)
d = ImageDraw.Draw(img)


def P(region, h, z):
    """Pixel of a point in a region. Sides: h = y along the truck, z = height. Front/rear/boxfront: h = x across, z = height.
    Top: h = y along the truck, z = x across (+X = the truck's left, the top of the image)."""
    if region in ("front", "rear", "boxfront"):
        return A.pix(region, h, 0.0, z)
    if region == "top":
        return A.pix(region, z, h, 0.0)
    return A.pix(region, 0.0, h, z)


def rect(region, y0, y1, z0, z1, fill=None, outline=None, width=1):
    a, b = P(region, y0, z1), P(region, y1, z0)
    d.rectangle([min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])], fill=fill, outline=outline, width=width)


def line(region, y0, z0, y1, z1, fill, width):
    d.line([P(region, y0, z0), P(region, y1, z1)], fill=fill, width=width)


def text_box(text, font_path, box, fill, stroke=None, stroke_w=0):
    """Draws text centred and fitted in a pixel box (x0, y0, x1, y1), never mirrored."""
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    size = int(bh * 1.3)
    while size > 6:
        font = ImageFont.truetype(font_path, size)
        tb = ImageDraw.Draw(Image.new("L", (1, 1))).textbbox((0, 0), text, font=font, stroke_width=stroke_w)
        if tb[2] - tb[0] <= bw and tb[3] - tb[1] <= bh:
            break
        size -= 2
    layer = Image.new("RGBA", (tb[2] - tb[0] + 8, tb[3] - tb[1] + 8), (0, 0, 0, 0))
    ImageDraw.Draw(layer).text((4 - tb[0], 4 - tb[1]), text, font=font, fill=fill, stroke_width=stroke_w, stroke_fill=stroke)
    img.paste(layer, (int((x0 + x1) / 2 - layer.width / 2), int((y0 + y1) / 2 - layer.height / 2)), layer)


def text_at(region, text, font_path, yc, zc, width_m, height_m, fill, stroke=None, stroke_w=0):
    a, b = P(region, yc - width_m / 2, zc + height_m / 2), P(region, yc + width_m / 2, zc - height_m / 2)
    text_box(text, font_path, (min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])), fill, stroke, stroke_w)


def arc_text(text, font_path, centre, radius, cap_px, span_deg, fill, stroke, stroke_w):
    """Text on an upward arc: letters stand on a circle of `radius` px round `centre`, centred at the top."""
    font = ImageFont.truetype(font_path, int(cap_px * 1.4))
    widths = [font.getlength(ch) for ch in text]
    total = sum(widths)
    step = math.degrees(total / radius)
    scale = min(1.0, span_deg / step)
    angle = -step * scale / 2
    for ch, wch in zip(text, widths):
        arc = math.degrees(wch / radius) * scale
        a = angle + arc / 2
        angle += arc
        if ch == " ":
            continue
        tb = ImageDraw.Draw(Image.new("L", (1, 1))).textbbox((0, 0), ch, font=font, stroke_width=stroke_w)
        glyph = Image.new("RGBA", (tb[2] - tb[0] + 12, tb[3] - tb[1] + 12), (0, 0, 0, 0))
        ImageDraw.Draw(glyph).text((6 - tb[0], 6 - tb[1]), ch, font=font, fill=fill, stroke_width=stroke_w, stroke_fill=stroke)
        glyph = glyph.rotate(-a, expand=True, resample=Image.BICUBIC)
        r = radius + (tb[3] - tb[1]) / 2
        px, py = centre[0] + r * math.sin(math.radians(a)), centre[1] - r * math.cos(math.radians(a))
        img.paste(glyph, (int(px - glyph.width / 2), int(py - glyph.height / 2)), glyph)


def diamond_plate(region, y0, y1, z0, z1):
    a, b = P(region, y0, z1), P(region, y1, z0)
    x0, y0p, x1, y1p = min(a[0], b[0]), min(a[1], b[1]), max(a[0], b[0]), max(a[1], b[1])
    d.rectangle([x0, y0p, x1, y1p], fill=(190, 194, 198))
    step = 12
    for j, yy in enumerate(range(int(y0p) + 4, int(y1p) - 2, step)):
        for xx in range(int(x0) + 4 + (j % 2) * step // 2, int(x1) - 4, step):
            d.line([(xx - 3, yy + 2), (xx + 3, yy - 2)], fill=(150, 154, 160), width=3)
            d.line([(xx - 2, yy + 3), (xx + 4, yy - 1)], fill=(232, 234, 236), width=1)
    d.rectangle([x0, y0p, x1, y1p], outline=(120, 124, 130), width=2)


def emblem(region, yc, zc, diameter_m):
    """The round Houston Fire emblem: blue ring, white ring, blue field with a white Maltese cross."""
    cx, cy = P(region, yc, zc)
    r = diameter_m * S / 2        # radius in pixels
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=WHITE)
    d.ellipse([cx - r * 0.94, cy - r * 0.94, cx + r * 0.94, cy + r * 0.94], fill=BLUE_DARK)
    d.ellipse([cx - r * 0.80, cy - r * 0.80, cx + r * 0.80, cy + r * 0.80], fill=WHITE)
    d.ellipse([cx - r * 0.70, cy - r * 0.70, cx + r * 0.70, cy + r * 0.70], fill=BLUE)
    for k in range(4):
        a = math.radians(90 * k)
        ca, sa = math.cos(a), math.sin(a)
        tri = [(0.10, 0.0), (0.62, -0.34), (0.50, 0.0), (0.62, 0.34)]
        pts = [(cx + r * (u * ca - v * sa), cy + r * (u * sa + v * ca)) for u, v in tri]
        d.polygon(pts, fill=WHITE)
    d.ellipse([cx - r * 0.16, cy - r * 0.16, cx + r * 0.16, cy + r * 0.16], fill=RED)


def heartbeat(region, y_from, y_to, zc, amp_m=0.20):
    """A white ECG line (outlined blue) running from y_from to y_to at height zc."""
    n = 1 if y_to > y_from else -1
    ys = [0.0, 0.18, 0.24, 0.30, 0.36, 0.42, 0.50, 0.58, 1.0]
    zs = [0.0, 0.0, 0.5, -0.45, 1.0, -0.2, 0.0, 0.0, 0.0]
    span = abs(y_to - y_from)
    pts = [P(region, y_from + n * y * span, zc + z * amp_m * 0.5) for y, z in zip(ys, zs)]
    d.line(pts, fill=BLUE_DARK, width=11, joint="curve")
    d.line(pts, fill=WHITE, width=6, joint="curve")


def houston_block(region, yc):
    """HOUSTON FIRE arched over the emblem, then EMS with heartbeat lines either side."""
    emblem(region, yc, 2.10, 0.36)
    # The arch stands on a 1.25 m radius circle, so its baseline peaks at z 2.45 and its ends sag to about z 2.10.
    cx, cy = P(region, yc, 2.45 - 1.25)
    arc_text("HOUSTON FIRE", FONT_BLACK, (cx, cy), 1.25 * S, 0.15 * S, 100, BLUE, WHITE, 3)
    text_at(region, "EMS", FONT_BLACK, yc, 1.72, 0.98, 0.30, BLUE, WHITE, 4)
    heartbeat(region, yc - 0.52, yc - 1.12, 1.72)
    heartbeat(region, yc + 0.52, yc + 1.12, 1.72)


def number_block(region, yc, big="07"):
    text_at(region, "AMBULANCE", FONT_BOLD, yc, 2.25, 0.66, 0.085, BLUE, WHITE, 2)
    text_at(region, big, FONT_BLACK, yc, 1.90, 0.46, 0.34, BLUE, WHITE, 4)


def band(region, y0, y1):
    rect(region, y0, y1, A.BAND[0], A.BAND[1], WHITE)
    rect(region, y0, y1, A.BAND[1] - 0.045, A.BAND[1] - 0.030, RED)
    rect(region, y0, y1, A.BAND[0] + 0.030, A.BAND[0] + 0.045, RED)


def script(region, yc, zc):
    text_at(region, "Courage, Commitment and Compassion", FONT_SCRIPT, yc, zc, 1.95, 0.11, GOLD, RED_DARK, 1)


def emergency_911(region, yc):
    text_at(region, "EMERGENCY", FONT_BOLD, yc, 1.355, 0.32, 0.045, RED)
    text_at(region, "911", FONT_BLACK, yc, 1.225, 0.34, 0.15, RED)


def doors(region, door_list):
    for y0, y1, z0, z1 in door_list:
        rect(region, y0, y1, z0, z1, None, SEAM, 3)


def cab_side(region):
    band(region, -3.30, A.CAB_BACK + 0.02)
    y0, y1, z0, z1 = A.CAB_DOOR
    rect(region, y0, y1, z0, z1, None, SEAM, 3)
    # Fender line over the front wheel.
    line(region, y0, 1.0, y0 - 0.02, 1.9, SEAM, 3)


def draw_right():
    r = "right"
    cab_side(r)
    # Box: band, doors, diamond plate, text.
    band(r, A.BOX_Y0 - 0.02, A.BOX_Y1)
    doors(r, A.RIGHT_DOORS)
    diamond_plate(r, 0.85, 2.40, A.BOX_Z0, A.BAND[0])
    script(r, 0.90, 2.68)
    houston_block(r, 1.62)
    number_block(r, 2.88)
    emergency_911(r, 3.00)
    # Crest above the front compartment door.
    cx, cy = P(r, -0.28, 2.38)
    logo = Image.open(LOGO_SRC).convert("RGBA")      # the crest above the front compartment: the department logo, small
    h = int(0.30 * S)
    logo = logo.resize((int(h * logo.width / logo.height), h), Image.LANCZOS)
    img.paste(logo, (int(cx - logo.width / 2), int(cy - logo.height / 2)), logo)


def draw_left():
    r = "left"
    cab_side(r)
    band(r, A.BOX_Y0 - 0.02, A.BOX_Y1)
    doors(r, A.LEFT_DOORS)
    diamond_plate(r, 1.12, 2.40, A.BOX_Z0, A.BAND[0])
    script(r, 1.00, 2.68)
    houston_block(r, 0.70)
    number_block(r, 1.98)
    emergency_911(r, 3.00)
    # Condenser grille panel, upper rear.
    y0, y1, z0, z1 = A.VENT_PANEL
    rect(r, y0, y1, z0, z1, BLACK, STEEL, 5)
    k = z0 + 0.06
    while k < z1 - 0.04:
        line(r, y0 + 0.05, k, y1 - 0.05, k, GREY, 4)
        k += 0.045


def draw_top():
    r = "top"
    rect(r, A.Y_FRONT, A.Y_REAR, -1.3, 1.3, RED)
    diamond_plate(r, 3.25, 3.64, -1.13, 1.13)        # rear step top (z here is x across the truck)


def draw_front():
    rect("front", -1.3, 1.3, 0.0, 3.1, RED)
    rect("front", -1.02, 1.02, 0.55, 0.98, WHITE)         # white lower valance and bumper skirt on the front face


def draw_boxfront():
    rect("boxfront", -1.3, 1.3, 2.0, 3.1, RED)
    # "A" and "07" in white above the light row, as in photo A 68: seen from the front, the A is on the image's left (the
    # truck's right, x < 0) and the number on the image's right (the truck's left, x > 0).
    text_at("boxfront", "A", FONT_BOLD, -0.45, 2.72, 0.32, 0.22, WHITE)
    text_at("boxfront", "07", FONT_BOLD, 0.45, 2.72, 0.50, 0.22, WHITE)


def draw_rear():
    r = "rear"
    x0, y0 = A.pix(r, 1.30, 0, 3.0)
    x1, y1 = A.pix(r, -1.30, 0, 0.45)
    w, h = int(x1 - x0), int(y1 - y0)
    chev = Image.new("RGB", (w, h), RED)
    cd = ImageDraw.Draw(chev)
    stripe = int(0.15 * S * 1.414)
    cx = w / 2
    apex_z = 2.45
    ay = (A.pix(r, 0, 0, apex_z)[1] - y0)
    for k in range(-30, 30):
        col = LIME if k % 2 else RED
        for sgn in (-1, 1):
            off = ay + k * stripe
            pts = [(cx, off), (cx, off + stripe), (cx + sgn * w, off + stripe + w), (cx + sgn * w, off + w)]
            cd.polygon(pts, fill=col)
    img.paste(chev, (int(x0), int(y0)))
    # Rear step face and the door split, hinges, frames.
    rect(r, -1.13, 1.13, 0.50, 0.62, (190, 194, 198))
    line(r, 0.0, 0.62, 0.0, 2.80, (30, 8, 10), 4)
    for xc, ww in A.WIN_REAR:
        rect(r, xc - ww / 2 - 0.035, xc + ww / 2 + 0.035, A.WIN_REAR_Z[0] - 0.035, A.WIN_REAR_Z[1] + 0.035, BLACK)
    # HFD EMS across the doors; A and 07 at the lower corners; EMERGENCY 911; plate.
    text_at(r, "HFD EMS", FONT_BLACK, 0.0, 1.60, 1.30, 0.20, WHITE, BLACK, 3)
    for text, xc, z, ww, hh in (("A", 0.94, 2.05, 0.26, 0.40), ("07", -0.94, 2.05, 0.38, 0.40)):
        a, b = A.pix(r, xc - ww / 2, 0, z + hh / 2), A.pix(r, xc + ww / 2, 0, z - hh / 2)
        text_box(text, FONT_BOLD, (min(a[0], b[0]), a[1], max(a[0], b[0]), b[1]), WHITE, BLACK, 3)
    for xc in (0.90,):
        a, b = A.pix(r, xc + 0.25, 0, 1.44), A.pix(r, xc - 0.25, 0, 1.36)
        text_box("EMERGENCY", FONT_BOLD, (a[0], a[1], b[0], b[1]), WHITE, BLACK, 1)
        a, b = A.pix(r, xc + 0.25, 0, 1.32), A.pix(r, xc - 0.25, 0, 1.10)
        text_box("911", FONT_BLACK, (a[0], a[1], b[0], b[1]), WHITE, BLACK, 2)
    # Licence plate.
    a, b = A.pix(r, 0.18, 0, 1.02), A.pix(r, -0.18, 0, 0.82)
    d.rectangle([a[0], a[1], b[0], b[1]], fill=(236, 236, 232), outline=(60, 60, 60), width=2)
    text_box("HFD 07", FONT_BOLD, (a[0] + 6, a[1] + 6, b[0] - 6, b[1] - 6), (30, 30, 30))


def draw_under():
    x, y = A.REGIONS["under"]
    d.rectangle([x, y, x + 64, y + 64], fill=(30, 30, 32))


draw_top()
draw_front()
draw_boxfront()
draw_rear()
draw_right()
draw_left()
draw_under()
img = img.filter(ImageFilter.SMOOTH)
os.makedirs(UNITY, exist_ok=True)
img.save(OUT)
print("wrote", OUT, img.size)
shutil.copy(LOGO_SRC, os.path.join(UNITY, "HFD_Logo.png"))
print("copied HFD_Logo.png")
