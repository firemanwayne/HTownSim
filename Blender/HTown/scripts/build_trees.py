"""Models HTownSim's street-tree species and places the City of Houston tree inventory.

Run inside Blender (after build_buildings.py):
    exec(open(r"J:\\Unity\\projects\\HTownSim\\Blender\\HTown\\scripts\\build_trees.py").read())

Models (data/tree_models.txt, read by UpgradeTrees.Apply): low-poly, flat-shaded species in the project's style,
3 variants each, built at a reference size (metres) and scaled per tree in Unity. Trunks carry bark UVs (Poly Haven
bark_brown_02); canopies use one material per foliage kind.
  live oak      wide, low, lumpy canopy on a short trunk that splits into spreading limbs (Houston's signature tree)
  crape myrtle  3-5 slim smooth trunks in a vase, canopy speckled with pink flower clusters
  broadleaf     hackberry / elms / pecan / ash: rounded crown on a single trunk
  laurel oak    laurel, water, willow, shumard oaks: taller oval crown
  cypress       bald cypress: narrow cone, feathery dark green
  magnolia      southern magnolia: dense dark pyramid
  palm          tall slim trunk, spray of fronds

Placement (data/tree_places.txt): every 'Present' inventory tree inside the play area, species from SPECIES and size
from the DIAMETER class. Trees that fall inside a building are dropped; trees standing in the roadway are moved onto
the nearest sidewalk.
"""
import json
import math
import os
import random
import sys

import bpy

sys.path.insert(0, r"J:\Unity\projects\HTownSim\Blender\HTown\scripts")  # fallback when run via exec()
import importlib  # noqa: E402

import geom  # noqa: E402

importlib.reload(geom)
from geom import Grid, Parts, ccw, centroid, dist_to_poly_edges, point_in  # noqa: E402

try:
    ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
except NameError:  # exec()'d from Blender's console, where __file__ is not defined
    ROOT = r"J:\Unity\projects\HTownSim\Blender\HTown"
UNITY_DATA = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Data"
WIN = (-845.0, 560.0, -450.0, 1575.0)
SIDEWALK = 2.4
LOT_TOP = 0.215
VARIANTS = 3

SPECIES = {
    "Oak, Live": "liveoak",
    "Crape Myrtle": "crape",
    "Oak, Laurel": "laureloak",
    "Oak, Water": "laureloak",
    "Oak, Willow": "laureloak",
    "Oak, Shumard": "laureloak",
    "Oak, Mexican White": "laureloak",
    "Oak, Red": "laureloak",
    "Oak, Bur": "liveoak",
    "Cypress, Bald": "cypress",
    "Cypress, Montezuma": "cypress",
    "Magnolia, Southern": "magnolia",
    "Palm, spp.": "palm",
    "California Fan Palm": "palm",
}
# Inventory DIAMETER class (inches at breast height, lower bound) -> scale factor on the reference model.
DIAMETER_SCALE = {"00": 0.45, "05": 0.6, "10": 0.75, "15": 0.88, "20": 1.0, "25": 1.1, "30": 1.2, "35": 1.28, "40": 1.35, "45": 1.42}


# ------------------------------------------------------------------------------------------------ modelling


def trunk(parts, base, top, r0, r1, sides=6, mat="Bark"):
    """Tapered prism from base to top (3D points), bark UVs wrapping once around and 1 unit per metre up."""
    bx, by, bz = base
    tx, ty, tz = top
    axis = (tx - bx, ty - by, tz - bz)
    length = math.sqrt(sum(a * a for a in axis))
    # A perpendicular basis for the ring.
    ux, uy, uz = (axis[1], -axis[0], 0.0) if abs(axis[2]) < 0.99 * length else (1.0, 0.0, 0.0)
    ul = math.sqrt(ux * ux + uy * uy + uz * uz) or 1
    ux, uy, uz = ux / ul, uy / ul, uz / ul
    ax, ay, az = axis[0] / length, axis[1] / length, axis[2] / length
    vx, vy, vz = ay * uz - az * uy, az * ux - ax * uz, ax * uy - ay * ux
    ring = lambda c, r, k: (  # noqa: E731
        c[0] + r * (math.cos(k) * ux + math.sin(k) * vx),
        c[1] + r * (math.cos(k) * uy + math.sin(k) * vy),
        c[2] + r * (math.cos(k) * uz + math.sin(k) * vz),
    )
    for i in range(sides):
        k0, k1 = 2 * math.pi * i / sides, 2 * math.pi * (i + 1) / sides
        a, b = ring(base, r0, k0), ring(base, r0, k1)
        c, d = ring(top, r1, k1), ring(top, r1, k0)
        u0, u1 = i / sides, (i + 1) / sides
        parts.quad(mat, a, b, c, d, [(u0, 0), (u1, 0), (u1, length), (u0, length)])


def canopy(parts, mat, lobes, rng, seg=8):
    for x, y, z, rx, rz in lobes:
        parts.blob(mat, x, y, z, rx, rz, seg=seg, seed=rng.random() * 6.28)


def live_oak(rng):
    p = Parts()
    # Limbs split high enough that the canopy clears a fire engine and its chase camera (about 4.5 m at full size).
    h_split = rng.uniform(2.9, 3.4)
    trunk(p, (0, 0, 0), (0, 0, h_split), 0.42, 0.34, 7)
    lobes = []
    limbs = rng.randint(3, 4)
    for i in range(limbs):
        a = 2 * math.pi * i / limbs + rng.uniform(-0.4, 0.4)
        reach = rng.uniform(3.6, 5.0)
        end = (math.cos(a) * reach, math.sin(a) * reach, h_split + rng.uniform(1.6, 2.6))
        trunk(p, (0, 0, h_split - 0.2), end, 0.26, 0.12, 6)
        lobes.append((end[0], end[1], end[2] - 0.6, rng.uniform(2.8, 3.6), rng.uniform(2.2, 2.8)))
        mid = (end[0] * 0.55, end[1] * 0.55, end[2] + 0.4)
        lobes.append((mid[0] + rng.uniform(-0.6, 0.6), mid[1] + rng.uniform(-0.6, 0.6), mid[2] - 0.4, rng.uniform(2.6, 3.2), rng.uniform(2.0, 2.6)))
    lobes.append((0, 0, h_split + 2.6, 3.4, 2.6))
    canopy(p, "LeafOak", lobes, rng)
    return p


def crape(rng):
    p = Parts()
    stems = rng.randint(3, 5)
    lobes = []
    for i in range(stems):
        a = 2 * math.pi * i / stems + rng.uniform(-0.3, 0.3)
        lean = rng.uniform(0.9, 1.3)
        top = (math.cos(a) * lean, math.sin(a) * lean, rng.uniform(2.8, 3.4))
        trunk(p, (math.cos(a) * 0.15, math.sin(a) * 0.15, 0), top, 0.11, 0.07, 5, "BarkSmooth")
        lobes.append((top[0] * 1.15, top[1] * 1.15, top[2] - 0.5, rng.uniform(1.5, 1.9), rng.uniform(1.5, 1.9)))
    lobes.append((0, 0, 3.4, 1.8, 1.6))
    canopy(p, "LeafCrape", lobes, rng, 7)
    # Flower clusters on top of the canopy.
    for x, y, z, rx, rz in lobes:
        for _ in range(4):
            a = rng.uniform(0, 6.28)
            d = rng.uniform(0.2, rx * 0.7)
            p.blob("Bloom", x + math.cos(a) * d, y + math.sin(a) * d, z + rz * rng.uniform(0.55, 0.8), rng.uniform(0.35, 0.5), rng.uniform(0.35, 0.5), seg=5, seed=rng.random() * 6)
    return p


def broadleaf(rng):
    p = Parts()
    h = rng.uniform(3.2, 4.0)
    trunk(p, (0, 0, 0), (rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), h), 0.28, 0.18, 6)
    # A broad dome: one central mass and a ring of overlapping lobes at the same height, so it reads wide, not stacked.
    lobes = [(0, 0, h - 0.2, 3.0, 3.2)]
    n = rng.randint(4, 5)
    for i in range(n):
        a = 2 * math.pi * i / n + rng.uniform(-0.3, 0.3)
        lobes.append((math.cos(a) * 2.2, math.sin(a) * 2.2, h - 0.3 + rng.uniform(-0.3, 0.6), rng.uniform(2.0, 2.5), rng.uniform(1.8, 2.3)))
    lobes.append((rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5), h + 1.6, 2.2, 2.0))
    canopy(p, "LeafBroad", lobes, rng)
    return p


def laurel_oak(rng):
    p = Parts()
    h = rng.uniform(3.5, 4.2)
    trunk(p, (0, 0, 0), (0, 0, h + 2.0), 0.32, 0.16, 6)
    # Taller oval than the broadleaf: a big central mass with lobes spread around it at two heights.
    lobes = [(0, 0, h - 0.3, 3.3, 4.8)]
    n = rng.randint(5, 6)
    for i in range(n):
        a = 2 * math.pi * i / n + rng.uniform(-0.3, 0.3)
        z = h + (0.4 if i % 2 else 2.0) + rng.uniform(-0.3, 0.3)
        lobes.append((math.cos(a) * 2.3, math.sin(a) * 2.3, z, rng.uniform(2.0, 2.4), rng.uniform(2.0, 2.4)))
    canopy(p, "LeafOak", lobes, rng)
    return p


def cypress(rng):
    p = Parts()
    trunk(p, (0, 0, 0), (0, 0, 13.5), 0.38, 0.08, 6)
    lobes = []
    z, r = 2.4, 2.6
    while z < 13:
        lobes.append((rng.uniform(-0.2, 0.2), rng.uniform(-0.2, 0.2), z, r, 2.4))
        z += rng.uniform(1.6, 2.0)
        r *= 0.82
    canopy(p, "LeafCypress", lobes, rng, 7)
    return p


def magnolia(rng):
    p = Parts()
    trunk(p, (0, 0, 0), (0, 0, 8.5), 0.3, 0.1, 6)
    # Dense, broad, rounded pyramid: fewer, fatter, overlapping masses (a cypress is a narrow stack of tiers).
    lobes = [(0, 0, 1.2, 3.8, 3.2), (0, 0, 3.6, 3.3, 3.0), (0, 0, 6.0, 2.5, 2.6), (0, 0, 8.0, 1.6, 1.8)]
    for i in range(4):
        a = 2 * math.pi * i / 4 + rng.uniform(-0.4, 0.4)
        lobes.append((math.cos(a) * 2.0, math.sin(a) * 2.0, rng.uniform(2.0, 4.5), 2.2, 2.2))
    canopy(p, "LeafMagnolia", lobes, rng)
    return p


def palm(rng):
    p = Parts()
    h = rng.uniform(8.5, 10.5)
    lean = (rng.uniform(-0.5, 0.5), rng.uniform(-0.5, 0.5))
    trunk(p, (0, 0, 0), (lean[0], lean[1], h), 0.26, 0.2, 6, "BarkPalm")
    top = (lean[0], lean[1], h)
    fronds = rng.randint(9, 12)
    t = p.tri_list("LeafPalm")
    for i in range(fronds):
        a = 2 * math.pi * i / fronds + rng.uniform(-0.15, 0.15)
        length = rng.uniform(3.4, 4.4)
        droop = rng.uniform(1.0, 1.9)
        tip = (top[0] + math.cos(a) * length, top[1] + math.sin(a) * length, top[2] - droop)
        mid = (top[0] + math.cos(a) * length * 0.5, top[1] + math.sin(a) * length * 0.5, top[2] + 0.45)
        w = 0.85
        side = (-math.sin(a) * w, math.cos(a) * w, 0)
        quad_pts = [top, (mid[0] + side[0], mid[1] + side[1], mid[2]), tip, (mid[0] - side[0], mid[1] - side[1], mid[2])]
        for tri in ((0, 1, 2), (0, 2, 3), (0, 2, 1), (0, 3, 2)):  # two-sided
            for k in tri:
                t["v"].append(quad_pts[k])
                t["uv"].append((0, 0))
    return p


MAKERS = {"liveoak": live_oak, "crape": crape, "broadleaf": broadleaf, "laureloak": laurel_oak, "cypress": cypress, "magnolia": magnolia, "palm": palm}


# ------------------------------------------------------------------------------------------------ placement


def read_parcels():
    polys = []
    for file in ("MidtownReal.txt", "MidtownExpanded.txt"):
        with open(os.path.join(UNITY_DATA, file), encoding="utf8") as fh:
            for line in fh:
                t = line.split("\t")
                if t[0] == "P":
                    poly = ccw([tuple(map(float, s.split(","))) for s in t[14].split(";")])
                    if len(poly) >= 3:
                        polys.append(poly)
    return polys


def place():
    with open(os.path.join(ROOT, "data", "htown_map.json"), encoding="utf8") as fh:
        trees = json.load(fh)["trees"]
    with open(os.path.join(ROOT, "data", "footprints_used.json"), encoding="utf8") as fh:
        buildings = [ccw(b["p"]) for b in json.load(fh)]
    parcels = read_parcels()
    sidewalks = [geom.offset_poly(p, SIDEWALK) for p in parcels]
    b_grid = Grid(buildings)
    s_grid = Grid(sidewalks)
    p_grid = Grid(parcels)

    out = []
    stats = {"kept": 0, "moved": 0, "in_building": 0, "outside": 0, "far": 0}
    for tr in trees:
        x, y = tr["x"], tr["z"]
        if not (WIN[0] + 3 < x < WIN[1] - 3 and WIN[2] + 3 < y < WIN[3] - 3):
            stats["outside"] += 1
            continue
        if any(point_in(buildings[i], x, y) or dist_to_poly_edges(buildings[i], x, y) < 1.2 for i in b_grid.near(x, y)):
            stats["in_building"] += 1
            continue
        on_ground = any(point_in(sidewalks[i], x, y) for i in s_grid.near(x, y))
        if not on_ground:
            # In the roadway: move to the nearest point 1.2 m inside the nearest sidewalk's kerb.
            best = None
            for i in set(s_grid.near(x, y) + s_grid.near(x + 20, y) + s_grid.near(x - 20, y) + s_grid.near(x, y + 20) + s_grid.near(x, y - 20)):
                poly = parcels[i]
                d = dist_to_poly_edges(poly, x, y)
                if best is None or d < best[0]:
                    best = (d, i)
            if best is None or best[0] > SIDEWALK + 6:
                stats["far"] += 1
                continue
            poly = parcels[best[1]]
            # Walk from the tree towards the parcel's nearest edge point, stop 1.2 m outside the parcel (mid-sidewalk).
            nearest, nd = None, 1e9
            for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
                dx, dy = x1 - x0, y1 - y0
                t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy or 1)))
                px, py = x0 + t * dx, y0 + t * dy
                d = math.hypot(px - x, py - y)
                if d < nd:
                    nearest, nd = (px, py), d
            vx, vy = x - nearest[0], y - nearest[1]
            vl = math.hypot(vx, vy) or 1
            x, y = nearest[0] + vx / vl * 1.2, nearest[1] + vy / vl * 1.2
            if any(point_in(buildings[i], x, y) for i in b_grid.near(x, y)):
                stats["in_building"] += 1
                continue
            stats["moved"] += 1
        else:
            stats["kept"] += 1
        kind = SPECIES.get(tr["sp"], "broadleaf")
        scale = DIAMETER_SCALE.get(tr["d"], 0.7)
        if kind in ("palm", "cypress", "magnolia"):
            scale = 0.6 + 0.4 * scale  # these are drawn near full height already
        if kind == "crape":
            scale = 0.8 + 0.3 * scale
        rng = random.Random(f"{x:.2f},{y:.2f}")
        out.append((kind, rng.randrange(VARIANTS), x, y, rng.uniform(0, 360), round(scale * rng.uniform(0.9, 1.1), 3)))
    return out, stats


def build():
    models = []
    for kind, maker in MAKERS.items():
        for v in range(VARIANTS):
            models.append((f"model/{kind}_{v}", f"Tree_{kind}_{v}", maker(random.Random(f"{kind}{v}"))))
    geom.write_unity_text(os.path.join(ROOT, "data", "tree_models.txt"), models)
    places, stats = place()
    with open(os.path.join(ROOT, "data", "tree_places.txt"), "w", encoding="utf8") as fh:
        for kind, v, x, y, yaw, s in places:
            fh.write(f"T\tTree_{kind}_{v}\t{x:.2f}\t{y:.2f}\t{yaw:.1f}\t{s}\n")
    by_kind = {}
    for p in places:
        by_kind[p[0]] = by_kind.get(p[0], 0) + 1
    tris = {name: sum(len(t["v"]) // 3 for t in parts.mats.values()) for _, name, parts in models}
    print(stats, by_kind, "tris per model", min(tris.values()), "-", max(tris.values()))
    return models, places


def preview(models, places):
    """Lays the variants out in a row at the origin area and instances the placed trees, for a look in Blender."""
    coll = bpy.data.collections.get("UpgradedTrees")
    if coll:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new("UpgradedTrees")
        bpy.context.scene.collection.children.link(coll)
    colours = {"Bark": "#5A4636", "BarkSmooth": "#9C8A78", "BarkPalm": "#7A6A58", "LeafOak": "#4F6B45", "LeafCrape": "#5E7A4A", "Bloom": "#D9709A", "LeafBroad": "#6A8A4E", "LeafCypress": "#3E5A3C", "LeafMagnolia": "#34503A", "LeafPalm": "#6E8A4A"}
    mats = {}
    for name, hx in colours.items():
        m = bpy.data.materials.get("UT_" + name) or bpy.data.materials.new("UT_" + name)
        h = hx.lstrip("#")
        rgb = [int(h[i : i + 2], 16) / 255 for i in (0, 2, 4)]
        m.diffuse_color = (*[(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in rgb], 1)
        m.use_nodes = True
        next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED").inputs["Base Color"].default_value = m.diffuse_color
        mats[name] = m
    meshes = {}
    for _, name, parts in models:
        me = bpy.data.meshes.get(name) or bpy.data.meshes.new(name)
        me.clear_geometry()
        verts, faces, fmat = [], [], []
        order = list(parts.mats.keys())
        for mi, (mat, t) in enumerate(parts.mats.items()):
            base = len(verts)
            verts.extend(t["v"])
            faces.extend((base + i, base + i + 1, base + i + 2) for i in range(0, len(t["v"]) - 2, 3))
            fmat.extend([mi] * (len(t["v"]) // 3))
        me.from_pydata(verts, [], faces)
        me.materials.clear()
        for mat in order:
            me.materials.append(mats[mat])
        for poly, mi in zip(me.polygons, fmat):
            poly.material_index = mi
        me.validate()
        meshes[name] = me
    for kind, v, x, y, yaw, s in places:
        name = f"Tree_{kind}_{v}"
        obj = bpy.data.objects.new(name, meshes[name])
        obj.location = (x, y, LOT_TOP)
        obj.rotation_euler = (0, 0, math.radians(yaw))
        obj.scale = (s, s, s)
        coll.objects.link(obj)


models, places = build()
preview(models, places)
