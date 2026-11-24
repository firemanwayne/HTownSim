"""Shared dimensions and livery-atlas layout for the clean Houston Fire ambulance (Ambulance 07).

Used by build_ambulance07.py (geometry) and make_livery_amb.py (livery drawing), so the two always agree.

Blender frame (metres): Z up, front at -Y, +X = the truck's LEFT (driver's) side, origin on the ground at the centre of
the overall length. A Chevrolet Silverado 3500HD cab-and-chassis with a Frazer type-I ambulance box (photos: Houston Fire
Ambulance 2, 8, 10, 55, 68 and Medic 60). Measured from the Hi3D GLB's orthographic silhouette, scaled to a 7.30 m truck:
wheelbase 4.18 m, tyres 0.82 m, box roof 2.9 m, cab 2.03 m wide, box 2.44 m (96 in) wide.
"""
CAB_X = 1.015
BOX_X = 1.22
BOX_Y0, BOX_Y1, BOX_Z0, BOX_Z1 = -0.62, 3.25, 0.43, 2.90
CAB_BACK, CAB_ROOF = -0.72, 2.06
LENGTH = 7.30
Y_FRONT, Y_REAR = -3.65, 3.65

# Cab side profile (y, z), counter-clockwise seen from +X: grille bottom, cab floor, back, roof, windshield base, hood.
CAB_PROFILE = [(-3.36, 0.58), (CAB_BACK, 0.58), (CAB_BACK, CAB_ROOF), (-1.38, CAB_ROOF), (-2.06, 1.58),
               (-3.30, 1.38), (-3.40, 1.26), (-3.40, 0.80)]

AX_F, AX_R, WR = -2.56, 1.62, 0.41
FRONT_WHEEL_X, REAR_WHEEL_X = 0.89, 0.955       # wheel (pair) centre planes
STEP = dict(x=1.13, y0=3.25, y1=3.62, z0=0.50, z1=0.62)

# Cab door glass (y, z) polygon (the A-pillar side slants), and the box and rear-door windows.
WIN_CAB = [(-2.02, 1.45), (-0.90, 1.45), (-0.90, 1.90), (-1.62, 1.90)]
WIN_BOX_RIGHT = dict(y0=0.12, y1=0.58, z0=2.05, z1=2.44)         # patient-door window, curb (right, -X) side
WIN_REAR = [(-0.34, 0.42), (0.34, 0.42)]                          # (x centre, width) of the two rear-door windows
WIN_REAR_Z = (1.85, 2.42)

# White band with red pin lines, bumper to tailgate.
BAND = (1.13, 1.41)

# Doors (y0, y1, z0, z1) for the livery shut lines.
CAB_DOOR = (-2.03, -0.80, 0.66, 1.99)
RIGHT_DOORS = [(-0.53, 0.00, 0.50, 2.06), (0.02, 0.78, 0.50, 2.66), (2.38, 3.20, 0.55, 1.10)]   # curb side
LEFT_DOORS = [(-0.55, -0.05, 0.50, 2.45), (0.02, 0.54, 0.50, 1.10), (0.56, 1.06, 0.50, 1.10), (2.40, 3.20, 0.55, 1.10)]
VENT_PANEL = (2.45, 3.15, 1.78, 2.60)                              # driver-side condenser grille, upper rear

# Atlas: 4096 x 2048, 270 px/m, planar regions (metres covered: sides y -3.75..3.75, z 0..3.1; top x -1.3..1.3).
ATLAS = (4096, 2048)
PPM = 270
REGIONS = {
    "left": (0, 0),
    "right": (2030, 0),
    "top": (0, 845),
    "front": (2040, 845),
    "rear": (2750, 845),
    "boxfront": (0, 1560),
    "under": (3480, 845),
}


def pix(region, x, y, z):
    """Pixel (from the top-left of the atlas) of a body point projected into a region."""
    ox, oy = REGIONS[region]
    if region == "left":
        return ox + (y + 3.75) * PPM, oy + (3.1 - z) * PPM
    if region == "right":
        return ox + (3.75 - y) * PPM, oy + (3.1 - z) * PPM
    if region == "top":
        return ox + (y + 3.75) * PPM, oy + (1.3 - x) * PPM
    if region == "front" or region == "boxfront":
        return ox + (x + 1.3) * PPM, oy + (3.1 - z) * PPM
    if region == "rear":
        return ox + (1.3 - x) * PPM, oy + (3.1 - z) * PPM
    return ox + 32, oy + 32          # "under": one dark spot


def uv(region, x, y, z):
    """Blender UV (v up) of a body point."""
    px, py = pix(region, x, y, z)
    return px / ATLAS[0], 1 - py / ATLAS[1]
