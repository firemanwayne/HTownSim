"""Builds the HTown city map scene in Blender from data/htown_map.json.

Run inside Blender:  exec(open(r"J:\\Blender\\Projects\\HTown\\scripts\\build_map.py").read())

Frame: HTownSim scene metres. Unity X -> Blender X, Unity Z -> Blender Y, up is Z. The Unity HUD map camera
covers x -845..560, z -450..1575, so the "MapTop" camera renders exactly that window.
"""
import json
import math
import os

import bmesh
import bpy

ROOT = r"J:\Blender\Projects\HTown"
DATA = os.path.join(ROOT, "data", "htown_map.json")

WIN = dict(x0=-845.0, x1=560.0, z0=-450.0, z1=1575.0)
FLOOR_H = 3.6
LOT_H = 0.15

# Palette: HTownSim visual direction (asphalt, concrete, brick, trim, apparatus, glazing, accent) pushed toward a
# dark dispatch-console map so it sits on the HUD's navy panel.
PAL = {
    "street": "#3B464C",
    "street_major": "#56636A",
    "freeway": "#6E6A5E",
    "ramp": "#5E5B52",
    "sidewalk": "#4F5A5F",
    "lot": "#182024",
    "lot_vacant": "#20292D",
    "park": "#2E4A34",
    "tree": "#4C7451",
    "bld_res": "#946F5C",
    "bld_com": "#7F8D93",
    "bld_pub": "#968D72",
    "bld_tall": "#AEBDC2",
    "station": "#B52E32",
    "rail": "#FFAF52",
    "hydrant": "#FFD34D",
    "ground": "#2A3439",
}


def srgb(hex_color, a=1.0):
    h = hex_color.lstrip("#")
    c = [int(h[i : i + 2], 16) / 255 for i in (0, 2, 4)]
    lin = [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c]
    return (*lin, a)


def material(name, hex_color, roughness=0.9, emission=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = srgb(hex_color)
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = srgb(hex_color)
        bsdf.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = srgb(hex_color)
    return mat


def building_material():
    """Roofs lighter than walls, colour from the per-face attribute 'tint'."""
    mat = bpy.data.materials.get("Buildings") or bpy.data.materials.new("Buildings")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        if n.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(n)
    out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "tint"
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Normal"], sep.inputs[0])
    # Roof (normal z ~ 1) keeps the tint, walls drop to 62 %.
    ramp = nt.nodes.new("ShaderNodeMapRange")
    ramp.inputs["From Min"].default_value = 0.2
    ramp.inputs["From Max"].default_value = 0.9
    ramp.inputs["To Min"].default_value = 0.62
    ramp.inputs["To Max"].default_value = 1.0
    nt.links.new(sep.outputs["Z"], ramp.inputs["Value"])
    mul = nt.nodes.new("ShaderNodeMix")
    mul.data_type = "RGBA"
    mul.blend_type = "MULTIPLY"
    mul.inputs["Factor"].default_value = 1.0
    nt.links.new(attr.outputs["Color"], mul.inputs["A"])
    comb = nt.nodes.new("ShaderNodeCombineColor")
    for k in ("Red", "Green", "Blue"):
        nt.links.new(ramp.outputs["Result"], comb.inputs[k])
    nt.links.new(comb.outputs["Color"], mul.inputs["B"])
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.85
    nt.links.new(mul.outputs["Result"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    mat.diffuse_color = srgb(PAL["bld_com"])
    return mat


def new_object(name, bm, mat, collection):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    obj.data.materials.append(mat)
    collection.objects.link(obj)
    return obj


def clean(pts):
    """Drop duplicate and collinear-repeat points; keep counter-clockwise order."""
    out = []
    for p in pts:
        if not out or abs(out[-1][0] - p[0]) > 0.05 or abs(out[-1][1] - p[1]) > 0.05:
            out.append(p)
    if len(out) > 2 and abs(out[0][0] - out[-1][0]) < 0.05 and abs(out[0][1] - out[-1][1]) < 0.05:
        out.pop()
    area = sum(out[i][0] * out[(i + 1) % len(out)][1] - out[(i + 1) % len(out)][0] * out[i][1] for i in range(len(out)))
    return out if area > 0 else out[::-1]


def add_flat(bm, pts, z):
    pts = clean(pts)
    if len(pts) < 3:
        return None
    verts = [bm.verts.new((x, y, z)) for x, y in pts]
    try:
        return bm.faces.new(verts)
    except ValueError:
        return None


def add_prism(bm, pts, z0, z1, tint_layer, tint):
    pts = clean(pts)
    if len(pts) < 3:
        return
    bottom = [bm.verts.new((x, y, z0)) for x, y in pts]
    top = [bm.verts.new((x, y, z1)) for x, y in pts]
    faces = []
    try:
        faces.append(bm.faces.new(top))
    except ValueError:
        return
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        try:
            faces.append(bm.faces.new((bottom[i], bottom[j], top[j], top[i])))
        except ValueError:
            pass
    for f in faces:
        for loop in f.loops:
            loop[tint_layer] = tint


CLIP = 520.0  # metres past the playable window that lines are kept (the rail data runs ~3 km north)


def clip_line(pts):
    def ok(p):
        return WIN["x0"] - CLIP < p[0] < WIN["x1"] + CLIP and WIN["z0"] - CLIP < p[1] < WIN["z1"] + CLIP

    return [p for p in pts if ok(p)]


def add_ribbon(bm, pts, width, z):
    pts = clip_line(pts)
    hw = width / 2
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        dx, dy = x1 - x0, y1 - y0
        length = math.hypot(dx, dy)
        if length < 0.01:
            continue
        nx, ny = -dy / length * hw, dx / length * hw
        quad = [(x0 + nx, y0 + ny), (x0 - nx, y0 - ny), (x1 - nx, y1 - ny), (x1 + nx, y1 + ny)]
        add_flat(bm, quad, z)
    for x, y in pts:
        add_disc(bm, x, y, hw, z, 8)


def add_disc(bm, x, y, r, z, seg=10):
    add_flat(bm, [(x + r * math.cos(a), y + r * math.sin(a)) for a in (2 * math.pi * i / seg for i in range(seg))], z)


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


def dist_to_poly(poly, x, y):
    best = float("inf")
    for (x0, y0), (x1, y1) in zip(poly, poly[1:] + poly[:1]):
        dx, dy = x1 - x0, y1 - y0
        t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy or 1)))
        best = min(best, math.hypot(x0 + t * dx - x, y0 + t * dy - y))
    return 0.0 if point_in(poly, x, y) else best


def build():
    with open(DATA, encoding="utf8") as fh:
        d = json.load(fh)

    scene = bpy.context.scene
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in list(bpy.data.collections):
        bpy.data.collections.remove(coll)

    def coll(name):
        c = bpy.data.collections.new(name)
        scene.collection.children.link(c)
        return c

    c_ground, c_roads, c_lots, c_bld, c_detail, c_overlay = (
        coll("Ground"),
        coll("Roads"),
        coll("Lots"),
        coll("Buildings"),
        coll("Detail"),
        coll("Overlay_Hydrants"),
    )

    # Ground plane = street surface, slightly past the margin.
    bm = bmesh.new()
    m = 1400.0  # the hero camera looks down steeply enough that this edge stays out of frame
    add_flat(bm, [(WIN["x0"] - m, WIN["z0"] - m), (WIN["x1"] + m, WIN["z0"] - m), (WIN["x1"] + m, WIN["z1"] + m), (WIN["x0"] - m, WIN["z1"] + m)], -0.3)
    new_object("Ground", bm, material("Street", PAL["ground"]), c_ground)

    # Road ribbons by class (parcels sit on top, so only the right-of-way shows).
    widths = {"FREEWAY": 30, "HIGHWAY": 26, "FRONTAGE": 12, "RAMP": 9, "MAJOR": 16, "LOCAL": 11}
    groups = {"street": [], "street_major": [], "freeway": [], "ramp": []}
    for r in d["roads"]:
        k = r["k"]
        key = "freeway" if k in ("FREEWAY", "HIGHWAY") else "ramp" if k in ("RAMP", "FRONTAGE") else "street_major" if k == "MAJOR" else "street"
        groups[key].append((r["p"], widths.get(k, 11)))
    for key, items in groups.items():
        bm = bmesh.new()
        z = {"street": 0.0, "street_major": 0.01, "ramp": 0.02, "freeway": 0.03}[key]
        if key in ("freeway", "ramp"):
            z += 9.0  # elevated: the freeway decks cast shadows onto the streets below
        for pts, w in items:
            add_ribbon(bm, pts, w, z)
        obj = new_object(f"Road_{key}", bm, material(f"Road_{key}", PAL[key]), c_roads)
        if key in ("freeway", "ramp"):
            deck = obj.modifiers.new("Deck", "SOLIDIFY")
            deck.thickness = 1.6
            deck.offset = -1.0

    # Lots (parcels). Parks and the two fire-station lots get their own materials.
    parks = [clean(p["p"]) for p in d["parks"]]
    # Station geocodes sit on the kerb line, so take the parcel nearest each one.
    station_lots = [min(d["parcels"], key=lambda p: dist_to_poly(p["p"], s["x"], s["z"]))["p"] for s in d["stationsGame"]]
    station_lot_ids = {id(p) for p in station_lots}

    # Sidewalk/lot-line layer at full parcel size, lots inset on top so every lot edge reads as a thin line.
    bm_side = bmesh.new()
    for p in d["parcels"]:
        add_flat(bm_side, p["p"], LOT_H - 0.05)
    new_object("Sidewalks", bm_side, material("Sidewalk", PAL["sidewalk"]), c_lots)

    bm_lot, bm_vac, bm_stn = bmesh.new(), bmesh.new(), bmesh.new()
    for p in d["parcels"]:
        pts = p["p"]
        if id(pts) in station_lot_ids:
            add_flat(bm_stn, pts, LOT_H)
        elif p["c"].startswith("C") and p["ba"] == 0:
            add_flat(bm_vac, pts, LOT_H)
        else:
            add_flat(bm_lot, pts, LOT_H)
    for bm_x in (bm_lot, bm_vac, bm_stn):
        bmesh.ops.inset_individual(bm_x, faces=list(bm_x.faces), thickness=0.9, use_even_offset=True)
    new_object("Lots", bm_lot, material("Lot", PAL["lot"]), c_lots)
    new_object("Lots_Vacant", bm_vac, material("LotVacant", PAL["lot_vacant"]), c_lots)
    new_object("Lots_Station", bm_stn, material("StationLot", PAL["station"], emission=0.6), c_lots)

    bm = bmesh.new()
    for pts in parks:
        add_flat(bm, pts, LOT_H + 0.05)
    new_object("Parks", bm, material("Park", PAL["park"]), c_lots)

    # Park trees: a loose grid of canopies inside each park, jittered by a stable hash.
    bm = bmesh.new()
    for pts in parks:
        xs, ys = [p[0] for p in pts], [p[1] for p in pts]
        step = 9.0
        x = min(xs) + step / 2
        while x < max(xs):
            y = min(ys) + step / 2
            while y < max(ys):
                h = math.sin(x * 12.9898 + y * 78.233) * 43758.5453
                jx, jy = (h % 1 - 0.5) * 5, ((h * 7) % 1 - 0.5) * 5
                if point_in(pts, x + jx, y + jy):
                    r = 3.0 + (h * 3) % 1 * 1.8
                    sphere = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
                    bmesh.ops.translate(bm, verts=sphere["verts"], vec=(x + jx, y + jy, LOT_H + r * 0.8))
                y += step
            x += step
    new_object("ParkTrees", bm, material("Tree", PAL["tree"]), c_lots)

    # Street trees: every ~13 m along each local/major street, 10.5 m either side of the centre line, kept only
    # where the spot is outside every parcel and clear of cross streets (so intersections stay open).
    cell = 30.0
    parcel_grid, seg_grid = {}, {}
    for i, p in enumerate(d["parcels"]):
        xs, ys = [q[0] for q in p["p"]], [q[1] for q in p["p"]]
        for gx in range(int(min(xs) // cell), int(max(xs) // cell) + 1):
            for gy in range(int(min(ys) // cell), int(max(ys) // cell) + 1):
                parcel_grid.setdefault((gx, gy), []).append(i)
    street_roads = [r for r in d["roads"] if r["k"] in ("LOCAL", "MAJOR")]
    for ri, r in enumerate(street_roads):
        for (x0, y0), (x1, y1) in zip(r["p"], r["p"][1:]):
            for gx in range(int(min(x0, x1) // cell) - 1, int(max(x0, x1) // cell) + 2):
                for gy in range(int(min(y0, y1) // cell) - 1, int(max(y0, y1) // cell) + 2):
                    seg_grid.setdefault((gx, gy), []).append((ri, x0, y0, x1, y1))

    def in_parcel(x, y):
        return any(point_in(d["parcels"][i]["p"], x, y) for i in parcel_grid.get((int(x // cell), int(y // cell)), ()))

    def near_other_street(x, y, own):
        for ri, x0, y0, x1, y1 in seg_grid.get((int(x // cell), int(y // cell)), ()):
            if ri == own:
                continue
            dx, dy = x1 - x0, y1 - y0
            t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / (dx * dx + dy * dy or 1)))
            if math.hypot(x0 + t * dx - x, y0 + t * dy - y) < 16:
                return True
        return False

    bm = bmesh.new()
    planted = 0
    for ri, r in enumerate(street_roads):
        carry = 6.0
        for (x0, y0), (x1, y1) in zip(r["p"], r["p"][1:]):
            length = math.hypot(x1 - x0, y1 - y0)
            if length < 0.01:
                continue
            ux, uy = (x1 - x0) / length, (y1 - y0) / length
            s = carry
            while s < length:
                for side in (-1, 1):
                    x = x0 + ux * s - uy * 10.5 * side
                    y = y0 + uy * s + ux * 10.5 * side
                    h = math.sin(x * 12.9898 + y * 78.233) * 43758.5453 % 1
                    if h < 0.18 or in_parcel(x, y) or near_other_street(x, y, ri):
                        continue
                    rad = 2.6 + h * 1.6
                    sphere = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=rad)
                    bmesh.ops.translate(bm, verts=sphere["verts"], vec=(x, y, 4.0 + rad * 0.6))
                    planted += 1
                s += 13.0
            carry = s - length
    new_object("StreetTrees", bm, material("StreetTree", "#46704C"), c_detail)
    print("street trees", planted)

    # Buildings: 2018 LiDAR footprints extruded by the floors HTownSim uses.
    bm = bmesh.new()
    tint = bm.loops.layers.color.new("tint")
    for b in d["buildings"]:
        floors = b.get("f") or 1
        if b.get("a", 999) < 80:
            # Sheds and kiosks inherit their parcel's floor count; keep them low instead of making spires.
            floors = min(floors, 1)
        k = b.get("k", "")
        height = max(3.5, floors * FLOOR_H)
        bx = sum(p[0] for p in b["p"]) / len(b["p"])
        by = sum(p[1] for p in b["p"]) / len(b["p"])
        if any(point_in(lot, bx, by) for lot in station_lots):
            col = PAL["station"]
        elif floors >= 12:
            col = PAL["bld_tall"]
        elif k.startswith(("A", "B")):
            col = PAL["bld_res"]
        elif k.startswith("X"):
            col = PAL["bld_pub"]
        else:
            col = PAL["bld_com"]
        add_prism(bm, b["p"], LOT_H, LOT_H + height, tint, srgb(col))
    new_object("Buildings", bm, building_material(), c_bld)

    # METRORail Red Line on Main St.
    bm = bmesh.new()
    for r in d["rail"]:
        add_ribbon(bm, r["p"], 2.2, 0.3)
    new_object("Rail", bm, material("Rail", PAL["rail"], emission=1.5), c_detail)

    # Hydrants (separate collection so they can be rendered as an overlay).
    bm = bmesh.new()
    for h in d["hydrants"]:
        add_disc(bm, h["x"], h["z"], 2.4, 0.4, 10)
    new_object("Hydrants", bm, material("Hydrant", PAL["hydrant"], emission=2.0), c_overlay)
    c_overlay.hide_render = True

    print({k: len(v) for k, v in d.items() if isinstance(v, list)})


def setup_render():
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    cx = (WIN["x0"] + WIN["x1"]) / 2
    cy = (WIN["z0"] + WIN["z1"]) / 2
    width = WIN["x1"] - WIN["x0"]
    depth = WIN["z1"] - WIN["z0"]

    cam_data = bpy.data.cameras.new("MapTop")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = max(width, depth)
    cam_data.clip_start = 1
    cam_data.clip_end = 3000
    cam = bpy.data.objects.new("MapTop", cam_data)
    cam.location = (cx, cy, 1200)
    scene.collection.objects.link(cam)
    scene.camera = cam

    hero_data = bpy.data.cameras.new("Hero")
    hero_data.lens = 45
    hero_data.clip_start = 5
    hero_data.clip_end = 8000
    hero = bpy.data.objects.new("Hero", hero_data)
    hero.location = (cx + 1150, cy - 1500, 1500)
    scene.collection.objects.link(hero)
    track = hero.constraints.new("TRACK_TO")
    target = bpy.data.objects.new("HeroTarget", None)
    target.location = (cx - 60, cy + 120, 0)
    scene.collection.objects.link(target)
    track.target = target

    sun_data = bpy.data.lights.new("Sun", "SUN")
    sun_data.energy = 3.2
    sun_data.angle = math.radians(1.5)
    sun_data.color = srgb("#FFE2B8")[:3]
    sun = bpy.data.objects.new("Sun", sun_data)
    # Classic cartographic light from the upper left (north-west of the map frame).
    sun.rotation_euler = (math.radians(48), 0, math.radians(-135))
    scene.collection.objects.link(sun)

    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    # Slate dusk sky (README mood); also the cool ambient fill in the shadows.
    bg.inputs["Color"].default_value = srgb("#7F93A6")
    bg.inputs["Strength"].default_value = 1.0

    r = scene.render
    r.resolution_y = 2048
    r.resolution_x = round(2048 * width / depth)
    r.resolution_percentage = 100
    r.film_transparent = False
    # The view-transform enum is dynamic (OCIO config), so try the flat-colour one and keep the default if missing.
    try:
        scene.view_settings.view_transform = "Standard"
    except TypeError as err:
        print("view transform unchanged:", err)


build()
setup_render()
