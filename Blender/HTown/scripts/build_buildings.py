"""Rebuilds HTownSim's shops, apartments and towers on their real 2018 LiDAR footprints.

Run inside Blender:  exec(open(r"J:\\Unity\\projects\\HTownSim\\Blender\\HTown\\scripts\\build_buildings.py").read())

Inputs:  data/unity_buildings.json (exported from Unity by AgentScripts/ExportBuildings.cs: heights, wall material,
         street-facing side) and data/htown_map.json (real footprints, same scene frame).
Output:  data/upgraded_buildings.json, one entry per Unity building: per-material triangle lists in Unity
         coordinates (x, y up, z), read by AgentScripts/UpgradeBuildings.cs. Also a "UpgradedBuildings" collection
         in the open Blender scene for previewing.

Per building:
  walls on the real outline up to the Unity height, a 0.6 m limestone cornice/parapet and an inset roof;
  window overlays (UV in facade-tile units) on every wall that is not a party wall;
  shops: storefront band on the street-facing walls, windows above the first storey;
  towers: limestone podium on the outline, body inset 1 m (glass curtain wall or stone with tall windows);
  rooftop units and a stair penthouse placed inside the real roof.
"""
import json
import math
import os
import random

import bmesh
import bpy
from mathutils import Vector

try:
    ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
except NameError:  # exec()'d from Blender's console, where __file__ is not defined
    ROOT = r"J:\Unity\projects\HTownSim\Blender\HTown"
UNITY = os.path.join(ROOT, "data", "unity_buildings.json")
MAP = os.path.join(ROOT, "data", "htown_map.json")
OUT = os.path.join(ROOT, "data", "upgraded_buildings.txt")

PARAPET = 0.6
PODIUM = 3.5
OVERLAY_OUT = 0.05
SHOP_GROUND = 4.5

# Facade tiles in metres (must match make_facades.py).
TILE = {
    "FacadeRes": (12.0, 12.8),
    "FacadeShop": (12.0, 14.4),
    "Storefront": (12.0, 4.5),
    "TowerWindows": (8.8, 14.4),
    "CurtainWall": (12.0, 14.4),
}

PREVIEW = {
    "Brick": "#875946",
    "Stucco": "#C9B79C",
    "SageStucco": "#A9B49A",
    "Limestone": "#E6D7B7",
    "Concrete": "#AAA697",
    "RoofTar": "#5E6265",
    "RoofWhite": "#C9CDCB",
    "Steel": "#71858C",
    "FacadeRes": "#284B59",
    "FacadeShop": "#284B59",
    "Storefront": "#2B2F33",
    "TowerWindows": "#284B59",
    "CurtainWall": "#6C93A8",
    "HouseFront": "#3E4A4F",
    "HouseSide": "#3E4A4F",
    "RoofShingle": "#4A4F52",
    "ShingleBrown": "#5B4A3E",
    "ShingleBlack": "#2E3033",
    "ShingleTan": "#7A6A58",
}


# ------------------------------------------------------------------------------------------------ geometry helpers


def area(poly):
    return sum(poly[i][0] * poly[(i + 1) % len(poly)][1] - poly[(i + 1) % len(poly)][0] * poly[i][1] for i in range(len(poly))) / 2


def ccw(poly):
    out = []
    for p in poly:
        if not out or math.dist(out[-1], p) > 0.15:
            out.append(tuple(p))
    if len(out) > 2 and math.dist(out[0], out[-1]) < 0.15:
        out.pop()
    # Drop near-collinear points so walls are clean single quads.
    changed = True
    while changed and len(out) > 3:
        changed = False
        for i in range(len(out)):
            a, b, c = out[i - 1], out[i], out[(i + 1) % len(out)]
            cross = (b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0])
            if abs(cross) < 0.02 * math.dist(a, b) * math.dist(b, c) + 1e-6:
                out.pop(i)
                changed = True
                break
    return out if area(out) > 0 else out[::-1]


def point_in(poly, x, y):
    c = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, yi = poly[i]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            c = not c
        j = i
    return c


def offset_poly(poly, d):
    """Miter offset (positive = outward for a CCW polygon), miters clamped so sharp corners do not spike."""
    n = len(poly)
    out = []
    for i in range(n):
        p0, p1, p2 = Vector(poly[i - 1]), Vector(poly[i]), Vector(poly[(i + 1) % n])
        e0 = (p1 - p0).normalized()
        e1 = (p2 - p1).normalized()
        n0 = Vector((e0.y, -e0.x))
        n1 = Vector((e1.y, -e1.x))
        m = n0 + n1
        if m.length < 1e-4:
            m = n0
        m.normalize()
        k = d / max(0.35, m.dot(n1))
        q = p1 + m * k
        out.append((q.x, q.y))
    return out


def iou_rect(poly, rect):
    xs, ys = [p[0] for p in poly], [p[1] for p in poly]
    a = (min(xs), min(ys), max(xs), max(ys))
    ix = max(0.0, min(a[2], rect[2]) - max(a[0], rect[0]))
    iy = max(0.0, min(a[3], rect[3]) - max(a[1], rect[1]))
    inter = ix * iy
    union = (a[2] - a[0]) * (a[3] - a[1]) + (rect[2] - rect[0]) * (rect[3] - rect[1]) - inter
    return inter / union if union > 0 else 0.0


# ------------------------------------------------------------------------------------------------ mesh builder


class Parts:
    """Triangles per material in Blender space (x, y, z-up), with optional UVs."""

    def __init__(self):
        self.mats = {}

    def tri_list(self, mat):
        return self.mats.setdefault(mat, {"v": [], "uv": []})

    def quad(self, mat, a, b, c, d, uvs=None):
        t = self.tri_list(mat)
        uvs = uvs or [(0, 0)] * 4
        for i in (0, 1, 2, 0, 2, 3):
            t["v"].append((a, b, c, d)[i])
            t["uv"].append(uvs[i])

    def polygon(self, mat, poly, z, up=True):
        """Triangulated flat polygon (concave-safe via bmesh)."""
        bm = bmesh.new()
        verts = [bm.verts.new((x, y, z)) for x, y in poly]
        try:
            face = bm.faces.new(verts)
        except ValueError:
            bm.free()
            return
        bmesh.ops.triangulate(bm, faces=[face])
        t = self.tri_list(mat)
        for f in bm.faces:
            vs = [tuple(v.co) for v in f.verts]
            if not up:
                vs = vs[::-1]
            for v in vs:
                t["v"].append(v)
                t["uv"].append((v[0] / 4.0, v[1] / 4.0))
        bm.free()

    def walls(self, mat, poly, z0, z1, tile=None, out=0.0, skip=None, vbase=0.0):
        """Outward-facing wall quads on every edge; with a tile, UVs run in tile units (u along the edge, v up from
        vbase, the height of a floor line)."""
        for i, (p, q) in enumerate(zip(poly, poly[1:] + poly[:1])):
            if skip and skip(i):
                continue
            length = math.dist(p, q)
            # 5 cm: drops degenerate slivers but keeps thin parts like 0.16 m porch posts.
            if length < 0.05:
                continue
            nx, ny = (q[1] - p[1]) / length * out, -(q[0] - p[0]) / length * out
            a = (p[0] + nx, p[1] + ny, z0)
            b = (q[0] + nx, q[1] + ny, z0)
            c = (q[0] + nx, q[1] + ny, z1)
            d = (p[0] + nx, p[1] + ny, z1)
            if tile:
                tw, th = tile
                # Centre whole bays on the wall so windows never get cut at a corner.
                bays = max(1, round(length / (tw / 4)))
                u1 = bays / 4
                v0, v1 = (z0 - vbase) / th, (z1 - vbase) / th
                uvs = [(0, v0), (u1, v0), (u1, v1), (0, v1)]
            else:
                uvs = None
            self.quad(mat, a, b, c, d, uvs)

    def face(self, mat, pts, hint, uv_scale=2.0):
        """Planar convex face (fan-triangulated), wound so its normal points along hint."""
        nx = ny = nz = 0.0
        for (x1, y1, z1), (x2, y2, z2) in zip(pts, pts[1:] + pts[:1]):
            nx += (y1 - y2) * (z1 + z2)
            ny += (z1 - z2) * (x1 + x2)
            nz += (x1 - x2) * (y1 + y2)
        if nx * hint[0] + ny * hint[1] + nz * hint[2] < 0:
            pts = pts[::-1]
        t = self.tri_list(mat)
        for i in range(1, len(pts) - 1):
            for p in (pts[0], pts[i], pts[i + 1]):
                t["v"].append(p)
                t["uv"].append(((p[0] + p[1]) / uv_scale, p[2] / uv_scale))

    def box(self, mat, cx, cy, z0, w, d, h, yaw=0.0):
        cs, sn = math.cos(yaw), math.sin(yaw)
        pts = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
        poly = [(cx + x * cs - y * sn, cy + x * sn + y * cs) for x, y in pts]
        self.walls(mat, poly, z0, z0 + h)
        self.polygon(mat, poly, z0 + h)

    def panel(self, mat, p, q, z0, z1):
        """Two-sided vertical panel from p to q (railings)."""
        length = math.dist(p, q)
        uvs = [(0, z0), (length, z0), (length, z1), (0, z1)]
        self.quad(mat, (p[0], p[1], z0), (q[0], q[1], z0), (q[0], q[1], z1), (p[0], p[1], z1), uvs)
        self.quad(mat, (q[0], q[1], z0), (p[0], p[1], z0), (p[0], p[1], z1), (q[0], q[1], z1), uvs)

    def slab(self, mat, quad_pts, z0, z1):
        """Closed horizontal slab over a 4-point footprint (any winding)."""
        poly = ccw(quad_pts)
        self.walls(mat, poly, z0, z1)
        self.polygon(mat, poly, z1)
        self.polygon(mat, poly[::-1], z0)


# ------------------------------------------------------------------------------------------------ houses

HOUSE_TILE = (12.0, 6.4)
HOUSE_RECT_FILL = 0.8  # outline area / minimum-rectangle area above which a house gets a pitched roof


def unity_hash(text):
    """BuildRealMidtown.StableHash, so roof pitch, gable/hip, shingle and chimney match what Unity picked."""
    h = 17
    for ch in text:
        h = (h * 31 + ord(ch)) & 0xFFFFFFFF
    return h & 0x7FFFFFFF


def shingle(h):
    """BuildRealMidtown.ShingleFor."""
    return {1: "ShingleBrown", 5: "ShingleBrown", 2: "ShingleBlack", 3: "ShingleTan"}.get(h % 6, "RoofShingle")


def convex_hull(pts):
    pts = sorted(set(pts))
    if len(pts) < 3:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in pts:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(pts):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def min_rect(poly):
    """Minimum-area enclosing rectangle: centre, unit axis along the long side, long length, short span."""
    hull = convex_hull(poly)
    best = None
    for p, q in zip(hull, hull[1:] + hull[:1]):
        length = math.dist(p, q)
        if length < 1e-6:
            continue
        ux, uy = (q[0] - p[0]) / length, (q[1] - p[1]) / length
        a = [x * ux + y * uy for x, y in hull]
        b = [-x * uy + y * ux for x, y in hull]
        w, h = max(a) - min(a), max(b) - min(b)
        if best is None or w * h < best[0]:
            ca, cb = (max(a) + min(a)) / 2, (max(b) + min(b)) / 2
            centre = (ca * ux - cb * uy, ca * uy + cb * ux)
            best = (w * h, centre, (ux, uy), w, h)
    _, centre, axis, w, h = best
    if h > w:
        axis, w, h = (-axis[1], axis[0]), h, w
    return centre, axis, w, h


def pitched_roof(parts, rect, wall_top, name, wall_mat):
    """Gable or hip roof over the house's oriented rectangle, as BuildRealMidtown.AddPitchedRoof does over its box
    (ridge along the long side, same pitch/overhang/style from the name hash), plus soffits under the eaves,
    gable-end walls and the occasional chimney."""
    h = unity_hash(name)
    pitch = math.radians(24 + (h % 5) * 2.5)
    gable = h % 3 != 0
    mat = shingle(h // 7)
    (cx, cy), (ax, ay), length, span = rect
    bx, by = -ay, ax  # across the span
    tan = math.tan(pitch)
    hs, hl, ov = span / 2, length / 2, 0.35
    ridge = wall_top + hs * tan
    eave = wall_top - ov * tan

    def P(u, v, z):  # u across the span, v along the ridge
        return (cx + bx * u + ax * v, cy + by * u + ay * v, z)

    edge = hs + ov
    slopes = []
    if gable:
        end = hl + ov
        slopes.append(([P(-edge, -end, eave), P(-edge, end, eave), P(0, end, ridge), P(0, -end, ridge)], -1))
        slopes.append(([P(edge, end, eave), P(edge, -end, eave), P(0, -end, ridge), P(0, end, ridge)], 1))
        for s in (-1, 1):
            parts.face(wall_mat, [P(-hs, s * hl, wall_top), P(hs, s * hl, wall_top), P(0, s * hl, ridge)], (ax * s, ay * s, 0))
    else:
        rh = max(0.0, hl - hs)
        e = hl + ov
        slopes.append(([P(-edge, -e, eave), P(-edge, e, eave), P(0, rh, ridge), P(0, -rh, ridge)], -1))
        slopes.append(([P(edge, e, eave), P(edge, -e, eave), P(0, -rh, ridge), P(0, rh, ridge)], 1))
        for s in (-1, 1):
            parts.face(mat, [P(-edge, s * e, eave), P(edge, s * e, eave), P(0, s * rh, ridge)], (ax * s * 0.5, ay * s * 0.5, 1))
            parts.face("Limestone", [(x, y, z - 0.08) for x, y, z in [P(-edge, s * e, eave), P(edge, s * e, eave), P(0, s * rh, ridge)]], (0, 0, -1))
    for pts, side in slopes:
        parts.face(mat, pts, (bx * side * 0.5, by * side * 0.5, 1))
        parts.face("Limestone", [(x, y, z - 0.08) for x, y, z in pts], (0, 0, -1))

    if h % 5 < 2:
        rise = hs * tan
        off = (1 if h % 2 == 0 else -1) * length * 0.2
        x, y, _ = P(0, off, 0)
        parts.box("Brick", x, y, wall_top + 0.3, 0.7, 0.7, rise + 0.6, math.atan2(ay, ax))


# ------------------------------------------------------------------------------------------------ variety

# Wall finishes. Unity creates these as tinted variants of its WorldTriplanar materials (UpgradeBuildings.WallVariants);
# the Siding* ones use the Poly Haven lap-siding scan.
WALL_FAMILIES = {
    "brick": ["Brick", "BrickDark", "BrickBuff"],
    "siding": ["SidingWhite", "SidingCream", "SidingYellow", "SidingBlue", "SidingSage", "SidingGrey"],
    "stucco": ["Stucco", "SageStucco", "StuccoTerracotta", "StuccoWhite"],
}
FLOOR_H = 3.2  # BuildRealMidtown.FloorHeight (apartments)
PORCH_DEPTH = 2.2


def wall_variant(b):
    """Stable wall finish per building (Unity's own name hash): houses are mostly painted lap siding or brick."""
    if b["style"] in ("Glass", "Striped"):
        return b["mat"]
    h = unity_hash(b["name"] + "/wall")
    r = h % 100
    if b["style"] == "House":
        family = "brick" if r < 30 else "siding" if r < 85 else "stucco"
    elif b["style"] == "Apartment":
        family = "brick" if r < 55 else "stucco"
    else:
        if r < 50:
            return b["mat"]
        family = "brick" if r < 75 else "stucco"
    names = WALL_FAMILIES[family]
    return names[(h // 100) % len(names)]


def front_wall(outline, facing, min_len):
    """Longest outline edge that faces the street side: (p, q, length, nx, ny) or None."""
    best = None
    for p, q in zip(outline, outline[1:] + outline[:1]):
        nx, ny = edge_normal(p, q)
        length = math.dist(p, q)
        if nx * facing[0] + ny * facing[1] > 0.7 and length >= min_len and (best is None or length > best[2]):
            best = (p, q, length, nx, ny)
    return best


def porch(parts, outline, facing, top, roof_mat, lot_ok):
    """Front porch: raised deck, white posts and a shed roof on the street side. Returns its wall endpoints or None."""
    fw = front_wall(outline, facing, 5.0)
    if fw is None:
        return None
    p, q, length, nx, ny = fw
    ux, uy = (q[0] - p[0]) / length, (q[1] - p[1]) / length
    width = min(6.0, 0.6 * length)
    s0 = (length - width) / 2
    a = (p[0] + ux * s0, p[1] + uy * s0)
    b = (a[0] + ux * width, a[1] + uy * width)
    c = (b[0] + nx * PORCH_DEPTH, b[1] + ny * PORCH_DEPTH)
    d = (a[0] + nx * PORCH_DEPTH, a[1] + ny * PORCH_DEPTH)
    # The porch (plus a step) must stay inside the lot, clear of the sidewalk.
    if not lot_ok([(c[0] + nx * 0.8, c[1] + ny * 0.8), (d[0] + nx * 0.8, d[1] + ny * 0.8)]):
        return None
    deck_h = 0.45
    parts.slab("Concrete", [a, b, c, d], 0.0, deck_h)
    posts = (0.1, 0.5, 0.9) if width > 4.5 else (0.1, 0.9)
    for t in posts:
        x = a[0] + (b[0] - a[0]) * t + nx * (PORCH_DEPTH - 0.2)
        y = a[1] + (b[1] - a[1]) * t + ny * (PORCH_DEPTH - 0.2)
        parts.box("Limestone", x, y, deck_h, 0.16, 0.16, 2.4, math.atan2(uy, ux))
    # Shed roof sloping away from the wall, overhanging the posts.
    zw = min(top - 0.25, deck_h + 2.95)
    zf = zw - 0.4
    o = 0.3
    a2 = (a[0] - ux * o, a[1] - uy * o)
    b2 = (b[0] + ux * o, b[1] + uy * o)
    c2 = (b2[0] + nx * (PORCH_DEPTH + o), b2[1] + ny * (PORCH_DEPTH + o))
    d2 = (a2[0] + nx * (PORCH_DEPTH + o), a2[1] + ny * (PORCH_DEPTH + o))
    top_pts = [(a2[0], a2[1], zw), (b2[0], b2[1], zw), (c2[0], c2[1], zf), (d2[0], d2[1], zf)]
    bot_pts = [(x, y, z - 0.12) for x, y, z in top_pts]
    parts.face(roof_mat, top_pts, (0, 0, 1))
    parts.face("Limestone", bot_pts, (0, 0, -1))
    for i in range(4):
        j = (i + 1) % 4
        mid = ((top_pts[i][0] + top_pts[j][0]) / 2 - (a2[0] + c2[0]) / 2, (top_pts[i][1] + top_pts[j][1]) / 2 - (a2[1] + c2[1]) / 2, 0)
        parts.face("Limestone", [top_pts[i], top_pts[j], bot_pts[j], bot_pts[i]], mid)
    return [list(a), list(b)]


def balconies(parts, outline, facing, top, skip_party):
    """Apartment balconies on street-facing walls: every other bay, every floor above the ground floor."""
    floors = int(top // FLOOR_H)
    for i, (p, q) in enumerate(zip(outline, outline[1:] + outline[:1])):
        nx, ny = edge_normal(p, q)
        length = math.dist(p, q)
        if nx * facing[0] + ny * facing[1] < 0.7 or length < 6 or skip_party(i):
            continue
        ux, uy = (q[0] - p[0]) / length, (q[1] - p[1]) / length
        bays = max(1, round(length / 3.0))
        bw = length / bays
        for k in range(0 if bays % 2 else 1, bays, 2):
            u0, u1 = k * bw + bw * 0.08, (k + 1) * bw - bw * 0.08
            a = (p[0] + ux * u0, p[1] + uy * u0)
            b = (p[0] + ux * u1, p[1] + uy * u1)
            c = (b[0] + nx * 1.2, b[1] + ny * 1.2)
            d = (a[0] + nx * 1.2, a[1] + ny * 1.2)
            for f in range(1, floors):
                z = f * FLOOR_H
                parts.slab("Concrete", [a, b, c, d], z - 0.05, z + 0.13)
                parts.panel("Railing", d, c, z + 0.13, z + 1.05)
                parts.panel("Railing", a, d, z + 0.13, z + 1.05)
                parts.panel("Railing", b, c, z + 0.13, z + 1.05)


def entrance(parts, outline, facing):
    """Apartment entrance: a glazed door and a steel canopy at the middle of the street front."""
    fw = front_wall(outline, facing, 6.0)
    if fw is None:
        return
    p, q, length, nx, ny = fw
    ux, uy = (q[0] - p[0]) / length, (q[1] - p[1]) / length
    m = (p[0] + ux * length / 2, p[1] + uy * length / 2)
    hw = 0.9
    off = 0.09  # just in front of the window overlay (0.05)
    a = (m[0] - ux * hw + nx * off, m[1] - uy * hw + ny * off)
    b = (m[0] + ux * hw + nx * off, m[1] + uy * hw + ny * off)
    parts.quad("DoorGlass", (a[0], a[1], 0.0), (b[0], b[1], 0.0), (b[0], b[1], 2.5), (a[0], a[1], 2.5))
    cw = 1.5
    ca = (m[0] - ux * cw, m[1] - uy * cw)
    cb = (m[0] + ux * cw, m[1] + uy * cw)
    parts.slab("Steel", [ca, cb, (cb[0] + nx * 1.4, cb[1] + ny * 1.4), (ca[0] + nx * 1.4, ca[1] + ny * 1.4)], 2.85, 3.0)


def build_house(parts, b, outline, best, party, wall, lot_ok):
    top = b["body"]["c"][1] + b["body"]["s"][1] / 2
    facing = b["facing"]
    rect = min_rect(outline)
    parts.walls(wall, outline, 0.0, top)
    porch_pts = None

    skip_party = party(outline)

    def front(i):
        p, q = outline[i], outline[(i + 1) % len(outline)]
        nx, ny = edge_normal(p, q)
        return nx * facing[0] + ny * facing[1] >= 0.6

    parts.walls("HouseFront", outline, 0.0, top - 0.1, HOUSE_TILE, OVERLAY_OUT, lambda i: skip_party(i) or not front(i))
    parts.walls("HouseSide", outline, 0.0, top - 0.1, HOUSE_TILE, OVERLAY_OUT, lambda i: skip_party(i) or front(i))

    if area(outline) >= HOUSE_RECT_FILL * rect[2] * rect[3]:
        pitched_roof(parts, rect, top, b["name"], wall)
        porch_pts = porch(parts, outline, facing, top, shingle(unity_hash(b["name"]) // 7), lot_ok)
        return "pitched", porch_pts
    # Irregular townhome compounds: flat roof behind a low parapet.
    parts.walls("Limestone", outline, top, top + 0.4)
    inner = offset_poly(outline, -0.25)
    if area(inner) > 4:
        parts.walls("Limestone", inner[::-1], top + 0.02, top + 0.4)
        for (p, q), (pi, qi) in zip(zip(outline, outline[1:] + outline[:1]), zip(inner, inner[1:] + inner[:1])):
            parts.quad("Limestone", (p[0], p[1], top + 0.4), (q[0], q[1], top + 0.4), (qi[0], qi[1], top + 0.4), (pi[0], pi[1], top + 0.4))
        parts.polygon("RoofTar", inner, top + 0.02)
    else:
        parts.polygon("RoofTar", outline, top + 0.4)
    return "flat", porch_pts


# ------------------------------------------------------------------------------------------------ main


def edge_normal(p, q):
    length = math.dist(p, q) or 1
    return ((q[1] - p[1]) / length, -(q[0] - p[0]) / length)


def build():
    with open(UNITY, encoding="utf8") as fh:
        unity = json.load(fh)
    with open(MAP, encoding="utf8") as fh:
        footprints = [ccw(b["p"]) for b in json.load(fh)["buildings"]]
    footprints = [f for f in footprints if len(f) >= 3 and area(f) > 8]

    # Coarse grid over footprints for matching and party-wall tests.
    cell = 40.0
    grid = {}
    for i, f in enumerate(footprints):
        xs, ys = [p[0] for p in f], [p[1] for p in f]
        for gx in range(int(min(xs) // cell), int(max(xs) // cell) + 1):
            for gy in range(int(min(ys) // cell), int(max(ys) // cell) + 1):
                grid.setdefault((gx, gy), []).append(i)

    def near(x, y):
        return grid.get((int(x // cell), int(y // cell)), [])

    # Unity's parcels, so porches stay inside their lot (clear of the sidewalk).
    lots = []
    for file in ("MidtownReal.txt", "MidtownExpanded.txt"):
        with open(os.path.join(r"J:\Unity\projects\HTownSim\Assets\HTownSim\Data", file), encoding="utf8") as fh:
            for line in fh:
                t = line.split("\t")
                if t[0] == "P":
                    lot = ccw([tuple(map(float, s.split(","))) for s in t[14].split(";")])
                    if len(lot) >= 3:
                        lots.append(lot)
    lot_cells = {}
    for i, lot in enumerate(lots):
        xs, ys = [p[0] for p in lot], [p[1] for p in lot]
        for gx in range(int(min(xs) // cell), int(max(xs) // cell) + 1):
            for gy in range(int(min(ys) // cell), int(max(ys) // cell) + 1):
                lot_cells.setdefault((gx, gy), []).append(i)

    def lot_ok(points):
        return all(any(point_in(lots[i], x, y) for i in lot_cells.get((int(x // cell), int(y // cell)), [])) for x, y in points)

    results = []
    used = []  # outline each building was built on, for the ground pass (driveways, beds, fences)
    stats = {"matched": 0, "box": 0, "irregular": 0}

    for b in unity:
        # Unity x,z -> Blender x,y.
        bc, bs = b["body"]["c"], b["body"]["s"]
        tower = b["style"] in ("Glass", "Striped")
        ref = b["podium"] if tower and b["podium"] else b["body"]
        rc, rs = ref["c"], ref["s"]
        rect = (rc[0] - rs[0] / 2, rc[2] - rs[2] / 2, rc[0] + rs[0] / 2, rc[2] + rs[2] / 2)
        top = bc[1] + bs[1] / 2

        best, best_iou = None, 0.0
        for i in set(near(rc[0], rc[2])):
            score = iou_rect(footprints[i], rect)
            if score > best_iou:
                best, best_iou = i, score
        if best is not None and best_iou >= 0.55:
            outline = footprints[best]
            stats["matched"] += 1
            xs, ys = [p[0] for p in outline], [p[1] for p in outline]
            if area(outline) < 0.85 * (max(xs) - min(xs)) * (max(ys) - min(ys)):
                stats["irregular"] += 1
        else:
            outline = [(rect[0], rect[1]), (rect[2], rect[1]), (rect[2], rect[3]), (rect[0], rect[3])]
            best = None
            stats["box"] += 1

        used.append({"key": b["key"], "style": b["style"], "facing": b["facing"], "top": b["body"]["c"][1] + b["body"]["s"][1] / 2, "p": [[round(x, 2), round(y, 2)] for x, y in outline]})
        rng = random.Random(b["name"])
        parts = Parts()
        facing = b["facing"]

        def party(poly):
            """Edge i is a party wall if a point just outside its middle is inside another footprint."""

            def test(i):
                p, q = poly[i], poly[(i + 1) % len(poly)]
                nx, ny = edge_normal(p, q)
                mx, my = (p[0] + q[0]) / 2 + nx * 1.2, (p[1] + q[1]) / 2 + ny * 1.2
                return any(j != best and point_in(footprints[j], mx, my) for j in near(mx, my))

            return test

        def street_facing(poly):
            def test(i):
                p, q = poly[i], poly[(i + 1) % len(poly)]
                nx, ny = edge_normal(p, q)
                return nx * facing[0] + ny * facing[1] < 0.6  # True = skip (not facing the street)

            return test

        wall = wall_variant(b)
        roof_mat = "RoofWhite" if rng.random() < 0.33 else "RoofTar"

        if b["style"] == "House":
            kind, porch_pts = build_house(parts, b, outline, best, party, wall, lot_ok)
            stats[kind] = stats.get(kind, 0) + 1
            if porch_pts:
                used[-1]["porch"] = porch_pts
                stats["porches"] = stats.get("porches", 0) + 1
            results.append((b, parts))
            continue

        if tower:
            podium = outline
            body = offset_poly(outline, -1.0)
            if area(body) < 30:
                body = outline
            pod_top = PODIUM
            parts.walls("Limestone", podium, 0.0, pod_top)
            parts.walls("Storefront", podium, 0.0, pod_top, TILE["Storefront"], OVERLAY_OUT, party(podium))
            parts.polygon("Limestone", podium, pod_top)
            if b["style"] == "Glass":
                parts.walls("CurtainWall", body, pod_top, top, TILE["CurtainWall"])
            else:
                parts.walls("Limestone", body, pod_top, top)
                parts.walls("TowerWindows", body, pod_top, top - 1.0, TILE["TowerWindows"], OVERLAY_OUT)
            shell = body
        else:
            shell = outline
            parts.walls(wall, shell, 0.0, top)
            if b["style"] == "Shop":
                ground = min(SHOP_GROUND, top - 0.4)
                skip_party = party(shell)
                not_street = street_facing(shell)
                parts.walls("Storefront", shell, 0.0, ground, TILE["Storefront"], OVERLAY_OUT, lambda i: skip_party(i) or not_street(i))
                if top - SHOP_GROUND >= 3.0:
                    # Unity shop floors: 4.5 m ground storey, then 3.6 m. vbase puts the tile's floor lines on them.
                    parts.walls("FacadeShop", shell, SHOP_GROUND, top - 0.3, TILE["FacadeShop"], OVERLAY_OUT, skip_party, vbase=SHOP_GROUND)
            else:
                parts.walls("FacadeRes", shell, 0.0, top - 0.2, TILE["FacadeRes"], OVERLAY_OUT, party(shell))
                balconies(parts, shell, facing, top, party(shell))
                entrance(parts, shell, facing)

        # Cornice/parapet band, inset roof well.
        parts.walls("Limestone", shell, top, top + PARAPET)
        inner = offset_poly(shell, -0.3)
        if area(inner) > 4:
            parts.walls("Limestone", inner[::-1], top + 0.02, top + PARAPET)  # reversed = faces inward
            # Parapet cap ring: outer polygon minus inner, as quads.
            for (p, q), (pi, qi) in zip(zip(shell, shell[1:] + shell[:1]), zip(inner, inner[1:] + inner[:1])):
                parts.quad("Limestone", (p[0], p[1], top + PARAPET), (q[0], q[1], top + PARAPET), (qi[0], qi[1], top + PARAPET), (pi[0], pi[1], top + PARAPET))
            parts.polygon(roof_mat, inner, top + 0.02)
            roof_poly = inner
        else:
            parts.polygon(roof_mat, shell, top + PARAPET)
            roof_poly = shell

        # Rooftop units and a stair penthouse, kept inside the real roof (not over courtyards).
        if not tower:
            xs, ys = [p[0] for p in roof_poly], [p[1] for p in roof_poly]
            units = max(1, min(8, round(area(roof_poly) / 140)))
            placed = 0
            for _ in range(units * 12):
                if placed >= units:
                    break
                w, d, h = rng.uniform(1.4, 2.6), rng.uniform(1.0, 1.8), rng.uniform(0.8, 1.3)
                x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
                corners = [(x + sx * (w / 2 + 0.6), y + sy * (d / 2 + 0.6)) for sx in (-1, 1) for sy in (-1, 1)]
                if all(point_in(roof_poly, cx, cy) for cx, cy in corners):
                    parts.box("Steel", x, y, top + 0.02, w, d, h)
                    placed += 1
            if top >= 9 and area(roof_poly) > 40:
                for _ in range(40):
                    x, y = rng.uniform(min(xs), max(xs)), rng.uniform(min(ys), max(ys))
                    corners = [(x + sx * 1.8, y + sy * 1.9) for sx in (-1, 1) for sy in (-1, 1)]
                    if all(point_in(roof_poly, cx, cy) for cx, cy in corners):
                        parts.box("Stucco", x, y, top + 0.02, 2.4, 2.6, 2.6)
                        break

        results.append((b, parts))

    print(stats)
    with open(os.path.join(ROOT, "data", "footprints_used.json"), "w", encoding="utf8") as fh:
        json.dump(used, fh)
    return results


def to_unity(results):
    """Writes per-building submeshes in Unity space. Blender (x, y, z-up) -> Unity (x, y=z, z=y); swapping two axes
    mirrors the space, so triangle winding is reversed to keep faces outward in Unity."""
    out = []
    for b, parts in results:
        subs = []
        for mat, t in parts.mats.items():
            v, uv = t["v"], t["uv"]
            verts, uvs = [], []
            for i in range(0, len(v), 3):
                for k in (0, 2, 1):
                    x, y, z = v[i + k]
                    verts.extend((round(x, 3), round(z, 3), round(y, 3)))
                    uvs.extend((round(uv[i + k][0], 4), round(uv[i + k][1], 4)))
            subs.append({"mat": mat, "v": verts, "uv": uvs})
        out.append({"key": b["key"], "name": b["name"], "subs": subs})
    # Tab-separated text like HTownSim's own data files (Unity's JsonUtility cannot read these from editor scripts):
    #   B <key> <name>
    #   M <material> <x,y,z,x,y,z,...> <u,v,u,v,...>      (triangle list, Unity space, Unity winding)
    with open(OUT, "w", encoding="utf8") as fh:
        for e in out:
            fh.write(f"B\t{e['key']}\t{e['name']}\n")
            for s in e["subs"]:
                fh.write("M\t" + s["mat"] + "\t" + ",".join(f"{x:g}" for x in s["v"]) + "\t" + ",".join(f"{x:g}" for x in s["uv"]) + "\n")
    print("wrote", OUT, round(os.path.getsize(OUT) / 1e6, 1), "MB")


def preview(results):
    """Adds the buildings to the Blender scene (one object per material) for a quick look."""
    coll = bpy.data.collections.get("UpgradedBuildings")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("UpgradedBuildings")
        bpy.context.scene.collection.children.link(coll)
    merged = {}
    for _, parts in results:
        for mat, t in parts.mats.items():
            merged.setdefault(mat, []).extend(t["v"])
    for mat, verts in merged.items():
        me = bpy.data.meshes.new("UB_" + mat)
        faces = [(i, i + 1, i + 2) for i in range(0, len(verts), 3)]
        me.from_pydata(verts, [], faces)
        me.validate()
        m = bpy.data.materials.get("UB_" + mat) or bpy.data.materials.new("UB_" + mat)
        hx = PREVIEW.get(mat, "#888888").lstrip("#")
        rgb = [int(hx[i : i + 2], 16) / 255 for i in (0, 2, 4)]
        m.diffuse_color = (*[(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in rgb], 1)
        m.use_nodes = True
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = m.diffuse_color
        me.materials.append(m)
        obj = bpy.data.objects.new("UB_" + mat, me)
        coll.objects.link(obj)


results = build()
to_unity(results)
preview(results)
