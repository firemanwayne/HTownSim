"""Builds HTownSim's lot surfaces, driveways, front walks, planting beds, shrubs, fences and parking stalls.

Run inside Blender (after build_buildings.py, which writes data/footprints_used.json):
    exec(open(r"J:\\Blender\\Projects\\HTown\\scripts\\build_ground.py").read())

Inputs:
  HTownSim's own parcel files (Assets/HTownSim/Data/MidtownReal.txt and MidtownExpanded.txt), filtered exactly as
    BuildRealMidtown builds them (old window + expansion, both station yards skipped);
  data/htown_map.json land use (City of Houston / HCAD LANDUSE per parcel: parking, vacant, residential...);
  data/footprints_used.json (each building's outline, style and street side).
Output: data/upgraded_ground.txt (200 m tiles, per-material triangles in Unity space) for UpgradeGround.Apply, and an
  "UpgradedGround" collection in Blender for previewing.

Per parcel:
  park -> GroundPark; bare commercial lot that Unity already stripes and fills with parked cars -> GroundParking;
  HCAD parking -> GroundParking + painted stalls; vacant -> GroundVacant; residential -> GroundLawn with, per house,
  a concrete driveway and front walk to the street, a mulch bed with shrubs along the front wall, and a 1.8 m wood
  privacy fence on the side and rear lot lines behind the house front; commercial with buildings -> asphalt apron
  with painted stalls wherever a full stall fits clear of the buildings.
"""
import json
import math
import os
import random
import sys

import bpy

sys.path.insert(0, r"J:\Blender\Projects\HTown\scripts")
import importlib  # noqa: E402

import geom  # noqa: E402

importlib.reload(geom)
from geom import Grid, Parts, area, ccw, centroid, dist_to_poly_edges, edge_normal, offset_poly, point_in  # noqa: E402

ROOT = r"J:\Blender\Projects\HTown"
UNITY_DATA = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Data"
OUT = os.path.join(ROOT, "data", "upgraded_ground.txt")

# BuildRealMidtown constants.
LOT_TOP = 0.215
OLD = (-520.0, 560.0, -450.0, 620.0)
NEW = (-845.0, 560.0, -450.0, 1575.0)
STATION7 = (12.3, -26.6)

# Heights above the lot surface (z-fighting safe at driving distance).
PAVE_Z = LOT_TOP + 0.012
PAINT_Z = LOT_TOP + 0.018
BED_Z = LOT_TOP + 0.09
FENCE_H = 1.8

PORCH_DEPTH = 2.2  # build_buildings.PORCH_DEPTH
DRIVE_W = 3.0
WALK_W = 1.2
BED_D = 1.1
STALL_W = 2.75
STALL_D = 5.4
AISLE = 7.2
STRIPE = 0.12
TILE = 200.0


# ------------------------------------------------------------------------------------------------ inputs


def read_parcels():
    """HTownSim parcels as BuildRealMidtown builds them (same window rules)."""
    out = []
    station8 = None
    for file, window, expanded in (("MidtownReal.txt", OLD, False), ("MidtownExpanded.txt", NEW, True)):
        with open(os.path.join(UNITY_DATA, file), encoding="utf8") as fh:
            lines = fh.read().split("\n")
        for line in lines:
            t = line.split("\t")
            if t[0] == "H":
                station8 = (t[4].split(" ", 1)[0], t[4].split(" ", 1)[1] if " " in t[4] else "")
            if t[0] != "P":
                continue
            cx, cz = float(t[1]), float(t[2])
            x0, x1, z0, z1 = window
            if not (x0 <= cx <= x1 and z0 <= cz <= z1):
                continue
            if expanded and OLD[0] <= cx <= OLD[1] and OLD[2] <= cz <= OLD[3]:
                continue
            poly = ccw([tuple(map(float, s.split(","))) for s in t[14].split(";")])
            if len(poly) < 3:
                continue
            out.append({"c": (cx, cz), "a": float(t[7]), "f": int(float(t[8])), "k": t[9], "s": t[12], "n": t[13], "m": int(float(t[15] or 0)), "p": poly, "exp": expanded})
    # Station yards: Station 7's lot and the largest parcel at Station 8's address.
    yards = {id(p) for p in out if math.dist(p["c"], STATION7) < 4}
    if station8:
        cands = [p for p in out if p["exp"] and p["n"] == station8[0] and p["s"].upper() == station8[1].upper()]
        if cands:
            yards.add(id(max(cands, key=lambda p: p["a"])))
    return [p for p in out if id(p) not in yards]


def classify(parcel, landuse, has_building):
    k, f, m, a = parcel["k"], parcel["f"], parcel["m"], parcel["a"]
    lu = landuse.get("ld", "") if landuse else ""
    if f == 0 and k.startswith("X") and a >= 700:
        return "park"
    unity_lot = f == 0 and m == 0 and a >= 250 and not k.startswith("A") and not k.startswith("C")
    if unity_lot:
        return "unity_lot"
    if "Parking" in lu and "Garage" not in lu:
        return "parking"
    if "Vacant" in lu or (not has_building and k.startswith("C")):
        return "vacant"
    if k[:1] in ("A", "B", "O") or "Res" in lu or "Apartment" in lu or "Condo" in lu:
        return "residential"
    if not has_building:
        return "vacant"
    return "commercial"


# ------------------------------------------------------------------------------------------------ features


def frame_from(edge_p, edge_q):
    """Local frame of a street edge: u along the edge, v inward (into the lot)."""
    ux, uy = edge_q[0] - edge_p[0], edge_q[1] - edge_p[1]
    length = math.hypot(ux, uy) or 1
    ux, uy = ux / length, uy / length
    vx, vy = -uy, ux  # inward for a CCW ring (outward normal is (uy, -ux))
    ox, oy = edge_p

    def to_world(u, v):
        return (ox + ux * u + vx * v, oy + uy * u + vy * v)

    def to_local(x, y):
        dx, dy = x - ox, y - oy
        return (dx * ux + dy * uy, dx * vx + dy * vy)

    return to_world, to_local, length


def rect(to_world, u0, u1, v0, v1):
    return [to_world(u0, v0), to_world(u1, v0), to_world(u1, v1), to_world(u0, v1)]


def front_edge(poly, facing):
    """Parcel edge that faces the street side of its building (or the longest edge)."""
    best, score = None, -1e9
    for i, (p, q) in enumerate(zip(poly, poly[1:] + poly[:1])):
        nx, ny = edge_normal(p, q)
        length = math.dist(p, q)
        s = (nx * facing[0] + ny * facing[1]) * 100 + length if facing != [0, 0] else length
        if s > score:
            best, score = i, s
    return best


def inside_all(poly, pts, margin=0.0):
    return all(point_in(poly, x, y) and (margin == 0 or dist_to_poly_edges(poly, x, y) >= margin) for x, y in pts)


def clear_of(buildings, pts, gap):
    for b in buildings:
        for x, y in pts:
            if point_in(b, x, y) or dist_to_poly_edges(b, x, y) < gap:
                return False
    return True


def house_features(parts, parcel, house, rng):
    """Driveway, front walk, mulch bed with shrubs for one house. Returns the house's front-line depth (v) or None."""
    poly = parcel["p"]
    i = front_edge(poly, house["facing"])
    p, q = poly[i], poly[(i + 1) % len(poly)]
    to_world, to_local, length = frame_from(p, q)
    hl = [to_local(x, y) for x, y in house["p"]]
    bu0, bu1 = min(u for u, _ in hl), max(u for u, _ in hl)
    vb = min(v for _, v in hl)
    if bu1 - bu0 < 3 or vb < -1:
        return None
    feats = []
    # A front porch (from build_buildings) covers this span of the front yard to PORCH_DEPTH from the wall.
    porch_span = None
    if house.get("porch"):
        pu = sorted(to_local(x, y)[0] for x, y in house["porch"])
        porch_span = (pu[0] - 0.2, pu[1] + 0.2)

    def stop_v(u0, u1):
        """How far into the lot a strip at u0..u1 may run: to the wall, or to the porch steps if it meets the porch."""
        if porch_span and u1 > porch_span[0] and u0 < porch_span[1]:
            return vb - PORCH_DEPTH - 0.05
        return vb

    if vb >= 2.0:
        left = rng.random() < 0.5
        du0, du1 = (bu0 + 0.2, bu0 + 0.2 + DRIVE_W) if left else (bu1 - 0.2 - DRIVE_W, bu1 - 0.2)
        dv = stop_v(du0, du1)
        drive = rect(to_world, du0, du1, 0.15, dv)
        if dv > 1.0 and inside_all(poly, drive):
            parts.polygon("GroundPaving", drive, PAVE_Z)
            feats.append((du0, du1))
        mid = (bu0 + bu1) / 2 + (0.8 if left else -0.8)
        wu0, wu1 = mid - WALK_W / 2, mid + WALK_W / 2
        if all(wu1 < a or wu0 > b for a, b in feats):
            wv = stop_v(wu0, wu1)
            walk = rect(to_world, wu0, wu1, 0.15, wv)
            if wv > 0.8 and inside_all(poly, walk):
                parts.polygon("GroundPaving", walk, PAVE_Z)
                feats.append((wu0, wu1))
    if porch_span:
        feats.append(porch_span)
    # Mulch bed along the front wall, broken where the driveway and walk reach the house.
    if vb >= BED_D + 0.3:
        cuts = sorted(feats)
        spans, start = [], bu0 + 0.3
        for a, b in cuts:
            if a - 0.2 > start:
                spans.append((start, a - 0.2))
            start = max(start, b + 0.2)
        if bu1 - 0.3 > start:
            spans.append((start, bu1 - 0.3))
        for a, b in spans:
            if b - a < 1.2:
                continue
            bed = rect(to_world, a, b, vb - BED_D, vb - 0.05)
            if not inside_all(poly, bed):
                continue
            parts.prism("GroundMulch", bed, LOT_TOP, BED_Z)
            u = a + 0.6
            while u < b - 0.5:
                x, y = to_world(u + rng.uniform(-0.2, 0.2), vb - BED_D / 2)
                r = rng.uniform(0.4, 0.65)
                parts.blob("Shrub", x, y, BED_Z - 0.05, r, r * rng.uniform(1.1, 1.6), seg=6, seed=rng.random() * 6)
                u += rng.uniform(1.3, 1.9)
    return (i, to_world, to_local, vb)


def fences(parts, parcel, front_info, done, faces_street):
    """Wood privacy fence on side and rear lot lines shared with a neighbour (never along a street), only behind the
    house's front line."""
    poly = parcel["p"]
    fi, to_world, to_local, vb = front_info
    line_v = vb + 1.0
    for j, (p, q) in enumerate(zip(poly, poly[1:] + poly[:1])):
        if j == fi or math.dist(p, q) < 1.0 or faces_street(p, q):
            continue
        a, b = to_local(*p), to_local(*q)
        # Clip the segment to v >= line_v (behind the house front).
        if a[1] < line_v and b[1] < line_v:
            continue
        if a[1] < line_v or b[1] < line_v:
            t = (line_v - a[1]) / (b[1] - a[1])
            cut = (a[0] + (b[0] - a[0]) * t, line_v)
            a, b = (cut, b) if a[1] < line_v else (a, cut)
        pw, qw = to_world(*a), to_world(*b)
        key = tuple(sorted([(round(pw[0]), round(pw[1])), (round(qw[0]), round(qw[1]))]))
        if key in done or math.dist(pw, qw) < 1.0:
            continue
        done.add(key)
        # Pull the fence 0.1 m inside the lot so it never sits on the sidewalk's edge.
        nx, ny = edge_normal(p, q)
        pw = (pw[0] - nx * 0.1, pw[1] - ny * 0.1)
        qw = (qw[0] - nx * 0.1, qw[1] - ny * 0.1)
        parts.panel("Fence", pw, qw, LOT_TOP, LOT_TOP + FENCE_H)
        # Posts every 2.4 m.
        n = max(1, int(math.dist(pw, qw) / 2.4))
        for s in range(n + 1):
            x = pw[0] + (qw[0] - pw[0]) * s / n
            y = pw[1] + (qw[1] - pw[1]) * s / n
            parts.prism("Fence", [(x - 0.06, y - 0.06), (x + 0.06, y - 0.06), (x + 0.06, y + 0.06), (x - 0.06, y + 0.06)], LOT_TOP, LOT_TOP + FENCE_H + 0.08)


def stalls(parts, parcel, buildings, facing):
    """Paints 90-degree stalls in double-loaded rows parallel to the street edge, clear of buildings."""
    poly = parcel["p"]
    i = front_edge(poly, facing)
    p, q = poly[i], poly[(i + 1) % len(poly)]
    to_world, to_local, _ = frame_from(p, q)
    loc = [to_local(x, y) for x, y in poly]
    umin, umax = min(u for u, _ in loc), max(u for u, _ in loc)
    vmax = max(v for _, v in loc)
    count = 0
    v0 = 1.0
    while v0 + STALL_D <= vmax:
        for row_v in (v0, v0 + STALL_D + AISLE):
            if row_v + STALL_D > vmax:
                continue
            run = []
            u = umin + 0.5
            while u + STALL_W <= umax - 0.5:
                corners = rect(to_world, u, u + STALL_W, row_v, row_v + STALL_D)
                if inside_all(poly, corners, 0.4) and clear_of(buildings, corners + [to_world(u + STALL_W / 2, row_v + STALL_D / 2)], 1.5):
                    run.append(u)
                else:
                    if len(run) >= 3:
                        count += paint_run(parts, to_world, run, row_v)
                    run = []
                u += STALL_W
            if len(run) >= 3:
                count += paint_run(parts, to_world, run, row_v)
        v0 += 2 * STALL_D + AISLE + 0.6
    return count


def paint_run(parts, to_world, run, row_v):
    for u in run + [run[-1] + STALL_W]:
        parts.polygon("PaintWhite", rect(to_world, u - STRIPE / 2, u + STRIPE / 2, row_v, row_v + STALL_D), PAINT_Z)
    return len(run)


# ------------------------------------------------------------------------------------------------ main


def build():
    parcels = read_parcels()
    with open(os.path.join(ROOT, "data", "htown_map.json"), encoding="utf8") as fh:
        landuse_polys = [dict(lu, p=ccw(lu["p"])) for lu in json.load(fh)["landuse"]]
    landuse_polys = [lu for lu in landuse_polys if len(lu["p"]) >= 3]
    lu_grid = Grid([lu["p"] for lu in landuse_polys])
    with open(os.path.join(ROOT, "data", "footprints_used.json"), encoding="utf8") as fh:
        used = json.load(fh)
    for u in used:
        u["p"] = ccw(u["p"])
    b_grid = Grid([u["p"] for u in used])

    tiles = {}
    stats = {}
    fence_done = set()
    stall_count = 0
    p_grid = Grid([p["p"] for p in parcels])

    def faces_street(p, q):
        """True when the far side of lot edge p->q (3 m out from its middle) is not inside any parcel: a street."""
        nx, ny = edge_normal(p, q)
        mx, my = (p[0] + q[0]) / 2 + nx * 3.0, (p[1] + q[1]) / 2 + ny * 3.0
        return not any(point_in(parcels[i]["p"], mx, my) for i in p_grid.near(mx, my))

    for parcel in parcels:
        cx, cy = parcel["c"]
        poly = parcel["p"]
        lu = next((landuse_polys[i] for i in lu_grid.near(cx, cy) if point_in(landuse_polys[i]["p"], cx, cy)), None)
        mine = [used[i] for i in set(sum((b_grid.near(x, y) for x, y in poly), [])) if point_in(poly, *centroid(used[i]["p"]))]
        kind = classify(parcel, lu, bool(mine))
        stats[kind] = stats.get(kind, 0) + 1
        key = (int(cx // TILE), int(cy // TILE))
        parts = tiles.setdefault(key, Parts())
        rng = random.Random(f"{cx:.1f},{cy:.1f}")

        surface = {"park": "GroundPark", "unity_lot": "GroundParking", "parking": "GroundParking", "vacant": "GroundVacant", "residential": "GroundLawn", "commercial": "GroundParking"}[kind]
        parts.polygon(surface, poly, LOT_TOP)

        if kind == "residential":
            for house in mine:
                if house["style"] != "House":
                    continue
                info = house_features(parts, parcel, house, rng)
                if info:
                    fences(parts, parcel, info, fence_done, faces_street)
        elif kind in ("parking", "commercial") and parcel["a"] >= 250:
            facing = mine[0]["facing"] if mine else [0, 0]
            stall_count += stalls(parts, parcel, [m["p"] for m in mine], facing)

    entries = [(f"tile/{tx}_{ty}", f"GroundTile_{tx}_{ty}", parts) for (tx, ty), parts in sorted(tiles.items())]
    geom.write_unity_text(OUT, entries)
    print(stats, "stalls", stall_count, "fences", len(fence_done), "tiles", len(entries), "->", OUT, round(os.path.getsize(OUT) / 1e6, 1), "MB")
    return entries


PREVIEW = {
    "GroundLawn": "#6E8A4E",
    "GroundPark": "#5E8A4A",
    "GroundVacant": "#8A8458",
    "GroundParking": "#4A5257",
    "GroundPaving": "#B9B3A3",
    "GroundMulch": "#5A3E2B",
    "Shrub": "#3F5B3A",
    "Fence": "#7A5C42",
    "PaintWhite": "#DAD8CC",
}


def preview(entries):
    coll = bpy.data.collections.get("UpgradedGround")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("UpgradedGround")
        bpy.context.scene.collection.children.link(coll)
    merged = {}
    for _, _, parts in entries:
        for mat, t in parts.mats.items():
            merged.setdefault(mat, []).extend(t["v"])
    for mat, verts in merged.items():
        me = bpy.data.meshes.new("UG_" + mat)
        me.from_pydata(verts, [], [(i, i + 1, i + 2) for i in range(0, len(verts) - 2, 3)])
        me.validate()
        m = bpy.data.materials.get("UG_" + mat) or bpy.data.materials.new("UG_" + mat)
        hx = PREVIEW.get(mat, "#888888").lstrip("#")
        rgb = [int(hx[i : i + 2], 16) / 255 for i in (0, 2, 4)]
        m.diffuse_color = (*[(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in rgb], 1)
        m.use_nodes = True
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = m.diffuse_color
        me.materials.append(m)
        obj = bpy.data.objects.new("UG_" + mat, me)
        coll.objects.link(obj)


entries = build()
preview(entries)
