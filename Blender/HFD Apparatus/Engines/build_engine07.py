"""Clean hard-surface Engine 07, built at the Hi3D truck's measured dimensions (Blender: front at -Y, Z up, metres).

Run inside Blender (with the Hi3D work scene "Engine43" in the open file, used as the bake source):
    exec(open(r"J:\\Blender\\Projects\\HFD Apparatus\\Engines\\build_engine07.py").read())

Builds scene "Engine07Clean":
  Engine07_Shell   cab, pump house, compartment body, front bumper, tailboard: bevelled boxes with booleans for the
                   wheel arches and window recesses. Materials: Paint (Hi3D livery baked on later), Interior.
  Engine07_Glass   tinted panes in the window recesses.     Engine07_Trim  grille, mirrors, deck gun, chassis, bar.
  Engine07_Lights  a fixture (dark housing + coloured lens cap) at every game warning lens, from lenses_engine07.json
                   mapped group by group onto the Hi3D truck's real light positions; the Unity-space lens targets are
                   written to lens_targets_engine07.json for InstallCleanEngine.
  WheelFrontLeft/Right, WheelRearLeft/Right   round tyres, rims, hubs and lug nuts, pivoting on their axles.
Unity frame: x_unity = -x, y_unity = z - 0.15, z_unity = -y (the visual sits 0.15 m down, like the old one).
"""
import json
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = r"J:\Blender\Projects\HFD Apparatus\Engines"
SCENE = "Engine07Clean"
DROP = 0.15

# Measured from the Hi3D truck (metres).
CAB = dict(x=1.38, y0=-4.45, y1=-0.70)
PUMP = dict(x=1.24, y0=-0.70, y1=0.70, z0=0.48, z1=2.55)
BODY = dict(x=1.40, y0=0.70, y1=4.45, z0=0.48, z1=2.78)
FRONT_AXLE, REAR_AXLE, AXLE_Z, TYRE_R = -2.40, 2.65, 0.545, 0.535
# Cab side windows (y0, y1, z0, z1): front door and crew door only (photos: the panel over the front wheel has a scene light).
WINDOWS = ((-3.90, -3.14, 1.76, 2.44), (-1.84, -1.10, 1.76, 2.44))
# Front bumper (Spartan spec): 99.00 in wide, 12.00 in high, angled front corners, extended 21.00 in ahead of the cab
# (cab face at y -4.45), with an embossed tread-plate apron on top and a covered compartment between the frame rails.
IN = 0.0254
BUMPER_X = CAB["x"]          # full cab width (owner's call; the spec sheet's bumper is 99.00 in)
BUMPER_Y0 = -4.45 - 21.00 * IN
BUMPER_CHAMFER = 0.13
BUMPER_Z0 = 0.55
BUMPER_Z1 = BUMPER_Z0 + 12.00 * IN
COMPARTMENT_X = 0.40          # bumper compartment / hose well: full width between the frame rails
# Bumper face, from the centre out (spec): Whelen M6 lights between the rails, SA4301 speakers just outboard of the
# rails, recessed Grover air horns (6.00 in flare) outboard of those; M6 intersection lights on the bumper tails.
M6 = (6.75 * IN, 4.31 * IN)
M6_X, SPEAKER_X, HORN_X = 0.28, 0.62, 0.93
SPEAKER = (7.36 * IN, 6.20 * IN)
HORN_FLARE_R = 3.00 * IN
Q2B = dict(w=10.50 * IN, h=10.00 * IN, d=14.00 * IN, x=1.08)
# Front roof (photo): lower roof over the front seats at ROOF_Z; light bar raised on two brackets so its top is the
# spec's 10 ft 2 in maximum height; scene lights molded into the cab shell under the bar ends at the brow corners
# (their lens centres are shared with the game's SceneLights component: x +/-0.86, centre z 2.87, lens face y -4.22).
ROOF_Z = 2.84
BAR = dict(x=1.08, y0=-3.96, y1=-3.64, z0=3.00, z1=3.10, mount_x=0.49)
BAR_LENS_FRONT, BAR_LENS_BACK = 3.88, 3.72      # Unity z of the bar's front and back LED rows (inside the dome)
# Scene lights: slim white housings with wide rectangular lenses, seated directly on the cab's brow in white U-yokes
# (no mount plate); the outer cab clearance light sits on the brow just below each lamp.
SCENE_LIGHT = dict(x=0.86,
                   housing_w=0.34, front=-4.215, back=-4.12, z0=2.80, z1=2.94,   # slim lamp housing on the brow
                   yoke_z0=2.72,                                       # yoke legs reach into the brow
                   lamp_w=0.31, lamp_z0=2.815, lamp_z1=2.925,          # wide lens
                   clearance_z=2.69, clearance_face=-4.205)            # outer cab light on the brow, under the lamp
CAB_BROW = ((-3.90, ROOF_Z), (-4.05, ROOF_Z), (-4.17, 2.76), (-4.235, 2.64))   # cab surface the mount sits on
# HFD logo decal on both front doors: centred on the door between the white band (top 1.19) and the window surround
# (bottom 1.725); 0.46 m tall at the logo's own aspect (1238 x 1523 px). Texture written by make_livery.py.
# Texas state flag: centred on the cab panel between the doors, above the front wheel, at window height (photo).
# Each decal: (material, texture, centre y, centre z, height); width follows the texture's aspect. The Unity installer
# maps Engine07_Decals' material slots in this same order.
DECAL_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Engine07Hi3D"
DECALS = (
    ("Engine07_Logo", DECAL_DIR + r"\HFD_Logo.png", (-4.02 + -3.04) / 2, (1.19 + 1.725) / 2, 0.46),
    ("Engine07_Texas", DECAL_DIR + r"\Texas_Flag.png", (-3.04 + -1.92) / 2, 2.10, 0.44),
)
# Door outlines for the livery: front door, crew door (y0, y1, z0, z1); doors open 32 in above the ground.
DOORS = ((-4.02, -3.04, 0.81, 2.62), (-1.92, -0.98, 0.81, 2.74))


# ------------------------------------------------------------------------------------------------ helpers


def new_scene():
    scn = bpy.data.scenes.get(SCENE)
    if scn is None:
        scn = bpy.data.scenes.new(SCENE)
    for o in list(scn.objects):
        if o.name.startswith(("Engine07_", "Wheel", "Cutter_")) and o.name != "Engine07_Body":
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.window.scene = scn
    return scn


def material(name, rgb, metal=0.0, rough=0.5, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    b.inputs["Alpha"].default_value = alpha
    m.diffuse_color = (*rgb, alpha)
    return m


def mesh_object(scn, name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    o = bpy.data.objects.new(name, me)
    scn.collection.objects.link(o)
    return o


def box(bm, x0, x1, y0, y1, z0, z1, mat=0):
    v = [bm.verts.new(p) for p in ((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        bm.faces.new([v[i] for i in f]).material_index = mat


def prism_x(bm, profile, x0, x1, mat=0):
    """Extrudes a side profile [(y, z), ...] (counter-clockwise seen from +X) across x0..x1."""
    a = [bm.verts.new((x0, y, z)) for y, z in profile]
    b = [bm.verts.new((x1, y, z)) for y, z in profile]
    bm.faces.new(list(reversed(a))).material_index = mat
    bm.faces.new(b).material_index = mat
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i])).material_index = mat


def prism_z(bm, outline, z0, z1, mat=0):
    """Extrudes a plan outline [(x, y), ...] (counter-clockwise seen from above) from z0 to z1."""
    a = [bm.verts.new((x, y, z0)) for x, y in outline]
    b = [bm.verts.new((x, y, z1)) for x, y in outline]
    bm.faces.new(list(reversed(a))).material_index = mat
    bm.faces.new(b).material_index = mat
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i])).material_index = mat


def apply_mods(o):
    for s in bpy.context.scene.objects:
        s.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def cutter(scn, name, bm_fn, mat):
    bm = bmesh.new()
    bm_fn(bm)
    o = mesh_object(scn, "Cutter_" + name, bm, [mat])
    o.display_type = "WIRE"
    o.hide_render = True
    return o


def cylinder_x(bm, cx, cy, cz, r, x0, x1, seg=48, mat=0):
    ring0 = [bm.verts.new((x0, cy + r * math.cos(a), cz + r * math.sin(a))) for a in (2 * math.pi * i / seg for i in range(seg))]
    ring1 = [bm.verts.new((x1, cy + r * math.cos(a), cz + r * math.sin(a))) for a in (2 * math.pi * i / seg for i in range(seg))]
    bm.faces.new(list(reversed(ring0))).material_index = mat
    bm.faces.new(ring1).material_index = mat
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((ring0[i], ring0[j], ring1[j], ring1[i])).material_index = mat


def to_blender(u):
    return Vector((-u[0], -u[2], u[1] + DROP))


def to_unity(b):
    return [-b[0], b[2] - DROP, -b[1]]


# ------------------------------------------------------------------------------------------------ shell


def build_shell(scn, mats):
    paint, interior = mats["Paint"], mats["Interior"]
    bm = bmesh.new()
    # Cab: grille face, sloped windshield, roof (side profile, front at -Y).
    # Raised roof (10 in) over the crew area, lower roof over the front seats carrying the light bar (photos/spec).
    prism_x(bm, [(-4.45, 0.42), (-0.70, 0.42), (-0.70, 2.98), (-3.28, 2.98), (-3.40, 2.84), (-4.05, 2.84), (-4.17, 2.76), (-4.24, 2.62), (-4.45, 1.70)], -CAB["x"], CAB["x"])
    box(bm, -PUMP["x"], PUMP["x"], PUMP["y0"], PUMP["y1"], PUMP["z0"], PUMP["z1"])
    box(bm, -BODY["x"], BODY["x"], BODY["y0"], BODY["y1"], BODY["z0"], BODY["z1"])
    # Front bumper: 99 in wide, 12 in face, 21 in extension (spec), ends cut back at 45 degrees (photos).
    prism_z(bm, [(-BUMPER_X, -4.40), (-BUMPER_X, BUMPER_Y0 + BUMPER_CHAMFER), (-BUMPER_X + BUMPER_CHAMFER, BUMPER_Y0),
                 (BUMPER_X - BUMPER_CHAMFER, BUMPER_Y0), (BUMPER_X, BUMPER_Y0 + BUMPER_CHAMFER), (BUMPER_X, -4.40)],
            BUMPER_Z0, BUMPER_Z1)
    box(bm, -1.30, 1.30, 4.40, 5.05, 0.48, 0.76)           # tailboard / rear step
    shell = mesh_object(scn, "Engine07_Shell", bm, [paint, interior])

    cutters = []
    # Wheel arches.
    for name, yc, r in (("ArchFront", FRONT_AXLE, 0.68), ("ArchRear", REAR_AXLE, 0.70)):
        cutters.append(cutter(scn, name, lambda b, yc=yc, r=r: cylinder_x(b, 0, yc, AXLE_Z, r, -1.6, 1.6, 64, 0), paint))
    # Window recesses (their cut faces take the Interior material). Windshield: a slab slanted like the glass.
    def windshield(b):
        n = Vector((0, -0.97, 0.24)).normalized()   # outward normal of the sloped face
        base, top = Vector((0, -4.44, 1.74)), Vector((0, -4.235, 2.58))
        for xs in ((-1.30, -0.04), (0.04, 1.30)):
            pts = []
            for p in (base, top):
                for x in xs:
                    pts.append(Vector((x, p.y, p.z)))
            quad = [pts[0], pts[1], pts[3], pts[2]]
            vs = [b.verts.new(q + n * 0.05) for q in quad] + [b.verts.new(q - n * 0.30) for q in quad]
            for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
                b.faces.new([vs[i] for i in f])
    cutters.append(cutter(scn, "Windshield", windshield, interior))
    def side_windows(b):
        for y0, y1, z0, z1 in WINDOWS:
            for s in (-1, 1):
                x_out = s * (CAB["x"] + 0.05)
                x_in = s * (CAB["x"] - 0.28)
                box(b, min(x_out, x_in), max(x_out, x_in), y0, y1, z0, z1)
    cutters.append(cutter(scn, "SideWindows", side_windows, interior))
    for c in cutters:
        m = shell.modifiers.new(c.name, "BOOLEAN")
        m.operation = "DIFFERENCE"
        m.solver = "EXACT"
        m.use_self = True   # the bumper and pump house overlap the cab as separate closed parts
        m.object = c
    apply_mods(shell)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)
    bev = shell.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.035
    bev.segments = 3
    bev.limit_method = "ANGLE"
    bev.angle_limit = math.radians(35)
    bev.harden_normals = True
    wn = shell.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    wn.keep_sharp = True
    apply_mods(shell)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35), keep_sharp_edges=True)
    return shell


# Livery atlas: planar projections of the shell, 200 px per metre, packed into 4096 x 2048 (see make_livery.py).
ATLAS = (4096, 2048)
PPM = 200
REGIONS = {
    # name: (pixel x, pixel y, width m, height m)
    "left": (0, 0, 10.1, 3.2),       # truck's left side (+X), front on the image's left
    "right": (2048, 0, 10.1, 3.2),   # truck's right side (-X), front on the image's right
    "top": (0, 660, 10.1, 2.9),      # from above, front on the image's left, truck's left side at the top
    "front": (2048, 660, 2.9, 3.2),  # from the front, truck's left side on the image's right
    "rear": (2660, 660, 2.9, 3.2),   # from behind, truck's right side on the image's right
    "under": (3300, 660, 0.5, 0.5),  # undersides: plain dark
}


def atlas_uv(region, a, b):
    """a, b in metres from the region's left/top; returns UV (v up)."""
    px, py, _, _ = REGIONS[region]
    return ((px + a * PPM) / ATLAS[0], 1 - (py + b * PPM) / ATLAS[1])


def assign_uvs(shell):
    """Planar UVs by dominant face normal, so livery drawn in side/top/front/rear views lands exactly."""
    me = shell.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        n = poly.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        cy, cz = poly.center.y, poly.center.z
        inward = (n.y > 0 and cy < 0) or (n.y < 0 and cy > 0)   # e.g. the hose well's walls facing back into the truck
        if ay > ax and ay > az and (inward or not (cy < -4.0 or cy > 4.3)):
            # Fore/aft faces mid-truck (wheel-arch liners, cab back wall, step risers): plain, never the end artwork.
            spot = atlas_uv("under", 0.25, 0.25) if cz < 1.3 else atlas_uv("front", 0.05, 0.05)
            for li in poly.loop_indices:
                uv.data[li].uv = spot
            continue
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            if az >= ax and az >= ay and n.z > 0:
                u = atlas_uv("top", co.y + 5.05, 1.45 - co.x)
            elif az >= ax and az >= ay:
                u = atlas_uv("under", 0.25, 0.25)
            elif ax >= ay:
                u = atlas_uv("left", co.y + 5.05, 3.2 - co.z) if n.x > 0 else atlas_uv("right", 5.05 - co.y, 3.2 - co.z)
            else:
                u = atlas_uv("front", co.x + 1.45, 3.2 - co.z) if n.y < 0 else atlas_uv("rear", 1.45 - co.x, 3.2 - co.z)
            uv.data[li].uv = u
    json.dump({"atlas": ATLAS, "ppm": PPM, "regions": REGIONS, "windows": WINDOWS, "doors": DOORS,
               "cab": CAB, "pump": PUMP, "body": BODY, "axles": [FRONT_AXLE, REAR_AXLE, AXLE_Z]},
              open(os.path.join(HERE, "livery_layout.json"), "w"), indent=1)


def build_glass(scn, mats):
    bm = bmesh.new()
    n = Vector((0, -0.97, 0.24)).normalized()
    base, top = Vector((0, -4.44, 1.74)), Vector((0, -4.235, 2.58))
    for xs in ((-1.28, -0.06), (0.06, 1.28)):
        quad = [Vector((xs[0], base.y, base.z)), Vector((xs[1], base.y, base.z)), Vector((xs[1], top.y, top.z)), Vector((xs[0], top.y, top.z))]
        bm.faces.new([bm.verts.new(q - n * 0.06) for q in quad])
    for y0, y1, z0, z1 in WINDOWS:
        for s in (-1, 1):
            x = s * (CAB["x"] - 0.04)
            quad = [(x, y0 + 0.02, z0 + 0.02), (x, y1 - 0.02, z0 + 0.02), (x, y1 - 0.02, z1 - 0.02), (x, y0 + 0.02, z1 - 0.02)]
            vs = [bm.verts.new(q) for q in quad]
            bm.faces.new(vs if s > 0 else list(reversed(vs)))
    o = mesh_object(scn, "Engine07_Glass", bm, [mats["Glass"]])
    return o


def sbox(bm, s, xa, xb, y0, y1, z0, z1, mat):
    """Box on one side: x from s*xa to s*xb (s = +1 truck's left, -1 right)."""
    box(bm, *sorted((s * xa, s * xb)), y0, y1, z0, z1, mat)


def cyl(bm, centre, r, depth, axis, mat, seg=24, r2=None):
    """Cylinder (or cone) along "X", "Y" or "Z" centred at centre."""
    c = bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=depth)
    if axis == "X":
        bmesh.ops.rotate(bm, verts=c["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    elif axis == "Y":
        bmesh.ops.rotate(bm, verts=c["verts"], matrix=Matrix.Rotation(math.radians(-90), 3, "X"))
    bmesh.ops.translate(bm, verts=c["verts"], vec=centre)
    for f in {f for v in c["verts"] for f in v.link_faces}:
        f.material_index = mat


def arch_trim(bm, s, x, yc, r, mat):
    """Polished stainless fender trim: a flat half ring around a wheel arch on side s."""
    seg = 32
    inner, outer = [], []
    for i in range(seg + 1):
        a = math.pi * i / seg
        for rr, lst in ((r - 0.005, inner), (r + 0.055, outer)):
            for xx in (x, x + s * 0.03):
                lst.append(bm.verts.new((xx, yc + rr * math.cos(a), AXLE_Z + rr * math.sin(a))))
    for i in range(seg):
        a0, a1 = 2 * i, 2 * (i + 1)
        for q in ((inner[a0], inner[a1], outer[a1], outer[a0]), (inner[a0 + 1], outer[a0 + 1], outer[a1 + 1], inner[a1 + 1]),
                  (outer[a0], outer[a1], outer[a1 + 1], outer[a0 + 1]), (inner[a0], inner[a0 + 1], inner[a1 + 1], inner[a1])):
            bm.faces.new(q).material_index = mat


def build_bumper_trim(bm, chrome, dark):
    """Front bumper hardware, laid out from the head-on delivery photo (Blender: front face at y = BUMPER_Y0)."""
    face = BUMPER_Y0
    zc = (BUMPER_Z0 + BUMPER_Z1) / 2
    for s in (-1, 1):
        # Grover air horns recessed in the face: 6.00 in chrome flare, dark recess, bell throat.
        cyl(bm, (s * HORN_X, face - 0.010, zc), HORN_FLARE_R + 0.008, 0.02, "Y", chrome, 32)
        cyl(bm, (s * HORN_X, face - 0.021, zc), HORN_FLARE_R - 0.012, 0.004, "Y", dark, 32)
        cyl(bm, (s * HORN_X, face - 0.012, zc), 0.032, 0.05, "Y", chrome, 24, 0.018)
        # SA4301 siren speakers: polished flat flange, dark grille with three bars.
        w, h = SPEAKER
        sbox(bm, s, SPEAKER_X - w / 2, SPEAKER_X + w / 2, face - 0.010, face + 0.004, zc - h / 2, zc + h / 2, chrome)
        sbox(bm, s, SPEAKER_X - w / 2 + 0.018, SPEAKER_X + w / 2 - 0.018, face - 0.013, face - 0.008, zc - h / 2 + 0.018, zc + h / 2 - 0.018, dark)
        for k in (-1, 0, 1):
            sbox(bm, s, SPEAKER_X - w / 2 + 0.024, SPEAKER_X + w / 2 - 0.024, face - 0.016, face - 0.012, zc + k * 0.035 - 0.005, zc + k * 0.035 + 0.005, chrome)
        # Chrome flanges round the M6 lights (the lenses are game lights in Engine07_Lights).
        w, h = M6
        sbox(bm, s, M6_X - w / 2 - 0.016, M6_X + w / 2 + 0.016, face - 0.008, face + 0.004, zc - h / 2 - 0.016, zc + h / 2 + 0.016, chrome)
        sbox(bm, s, BUMPER_X - 0.004, BUMPER_X + 0.008, -4.75 - w / 2 - 0.016, -4.75 + w / 2 + 0.016, zc - h / 2 - 0.016, zc + h / 2 + 0.016, chrome)
        # Black straps over the front lip, either side of the compartment centre (photos).
        sbox(bm, s, 0.06, 0.11, face + 0.02, -4.50, BUMPER_Z1, BUMPER_Z1 + 0.012, dark)
        sbox(bm, s, 0.06, 0.11, face - 0.012, face, BUMPER_Z1 - 0.09, BUMPER_Z1 + 0.012, dark)
    # Stainless bolt heads along the top and bottom of the face (apron bolts and channel bolts).
    for z in (BUMPER_Z1 - 0.030, BUMPER_Z0 + 0.030):
        for k in range(13):
            x = -1.08 + k * 0.18
            cyl(bm, (x, face - 0.004, z), 0.011, 0.008, "Y", chrome, 8)
    # Bumper compartment cover between the frame rails: D-ring latch at the front edge.
    cyl(bm, (0, face + 0.11, BUMPER_Z1 + 0.006), 0.035, 0.012, "Z", chrome, 20)
    cyl(bm, (0, face + 0.11, BUMPER_Z1 + 0.010), 0.022, 0.012, "Z", dark, 20)
    # Gravel shield / skirt under the face.
    box(bm, -0.58, 0.58, face + 0.05, face + 0.07, 0.40, BUMPER_Z0 + 0.01, chrome)
    # Federal Q2B siren, 10.50 W x 10.00 H x 14.00 D in, on a pedestal on the driver-side outboard apron (spec).
    qr, qd = Q2B["h"] / 2, Q2B["d"]
    qx, qy, qz = Q2B["x"], face + 0.03 + qd / 2, BUMPER_Z1 + 0.07 + qr
    box(bm, qx - Q2B["w"] / 2 + 0.04, qx + Q2B["w"] / 2 - 0.04, qy - 0.10, qy + 0.10, BUMPER_Z1, qz - qr + 0.02, dark)
    cyl(bm, (qx, qy, qz), qr, qd, "Y", chrome, 32)
    cyl(bm, (qx, qy + qd / 2 + 0.02, qz), qr * 0.7, 0.04, "Y", chrome, 24, qr * 0.5)   # motor housing taper at the back
    fy = qy - qd / 2
    cyl(bm, (qx, fy - 0.003, qz), qr - 0.010, 0.006, "Y", dark, 32)
    for rr in (0.095, 0.068, 0.040):
        cyl(bm, (qx, fy - 0.007, qz), rr, 0.004, "Y", chrome, 32)
        cyl(bm, (qx, fy - 0.009, qz), rr - 0.011, 0.004, "Y", dark, 32)
    cyl(bm, (qx, fy - 0.011, qz), 0.02, 0.006, "Y", chrome, 16)
    # Front bumper discharge to the right of the hose well: 2.00 in riser, stainless chicksan swivel, 1.50 in cap, and
    # the smooth aluminium chicksan guard on the gravel shield (spec).
    dx, dy = -0.98, face + 0.20
    cyl(bm, (dx, dy, BUMPER_Z1 + 0.06), 1.00 * IN + 0.004, 0.12, "Z", chrome, 20)
    cyl(bm, (dx, dy, BUMPER_Z1 + 0.13), 0.042, 0.05, "Z", chrome, 20)
    cyl(bm, (dx, dy - 0.05, BUMPER_Z1 + 0.15), 1.00 * IN + 0.004, 0.10, "Y", chrome, 20)
    cyl(bm, (dx, dy - 0.11, BUMPER_Z1 + 0.15), 0.036, 0.025, "Y", chrome, 6)
    box(bm, dx - 0.09, dx + 0.09, dy - 0.09, dy + 0.09, BUMPER_Z1, BUMPER_Z1 + 0.012, chrome)
    box(bm, dx - 0.12, dx + 0.12, -4.47, -4.46, BUMPER_Z1, BUMPER_Z1 + 0.22, chrome)


def stadium(cx0, cx1, y0, y1, seg=10):
    """Plan outline (counter-clockwise from above) of a bar from x cx0 to cx1 with half-round ends."""
    r = (y1 - y0) / 2
    yc = (y0 + y1) / 2
    pts = []
    for i in range(seg + 1):                      # right end, front to back
        a = -math.pi / 2 + math.pi * i / seg
        pts.append((cx1 - r + r * math.cos(a), yc + r * math.sin(a)))
    for i in range(seg + 1):                      # left end, back to front
        a = math.pi / 2 + math.pi * i / seg
        pts.append((cx0 + r + r * math.cos(a), yc + r * math.sin(a)))
    return pts


def build_roof_trim(bm, chrome, dark, white):
    """Front roof (photo): light bar on two brackets, scene light housings under its ends at the brow corners.
    Heights keep the truck at the spec's 10 ft 2 in (3.10 m) overall."""
    b = BAR
    # Light bar: aluminium base rail and a dark module tray under the clear dome (Engine07_BarDome).
    prism_z(bm, stadium(-b["x"], b["x"], b["y0"], b["y1"]), b["z0"], b["z0"] + 0.025, chrome)
    # The front and back LED modules (game lenses) mount on this tray's faces, inside the dome.
    box(bm, -b["x"] + 0.10, b["x"] - 0.10, -BAR_LENS_FRONT + 0.02, -BAR_LENS_BACK - 0.02, b["z0"] + 0.025, b["z1"] - 0.012, dark)
    for s in (-1, 1):
        # Mounting brackets: foot plate on the roof, upright, and a short brace back to the roof.
        sbox(bm, s, b["mount_x"] - 0.07, b["mount_x"] + 0.07, b["y0"] + 0.06, b["y1"] - 0.06, ROOF_Z, ROOF_Z + 0.01, chrome)
        sbox(bm, s, b["mount_x"] - 0.02, b["mount_x"] + 0.02, b["y0"] + 0.10, b["y1"] - 0.10, ROOF_Z, b["z0"], chrome)
        build_scene_light(bm, s, chrome, dark, white)


def build_scene_light(bm, s, chrome, dark, white):
    """One scene light (close-up photo): a slim white lamp housing seated on the cab's brow, held in a white U-yoke
    with knobs, a dark bezel round the wide lens and a dark grid over it for the LED rows.
    The lens itself is Engine07_SceneLenses."""
    sl = SCENE_LIGHT
    # Lamp housing seated on the brow (no mount plate), its U-yoke legs set into the brow, and the knobs.
    hw, lw = sl["housing_w"] / 2, sl["lamp_w"] / 2
    sbox(bm, s, sl["x"] - hw, sl["x"] + hw, sl["front"], sl["back"], sl["z0"], sl["z1"], white)
    for side in (-1, 1):
        ex = sl["x"] + side * (hw + 0.006)
        sbox(bm, s, ex - 0.006, ex + 0.006, sl["front"] + 0.03, sl["front"] + 0.07, sl["yoke_z0"], sl["z1"] - 0.03, white)
        cyl(bm, (s * (ex + side * 0.012), sl["front"] + 0.05, (sl["z0"] + sl["z1"]) / 2), 0.016, 0.014, "X", chrome, 12)
    # Dark bezel round the lens, and the LED grid in front of it.
    f = sl["front"]
    sbox(bm, s, sl["x"] - lw - 0.010, sl["x"] + lw + 0.010, f - 0.002, f + 0.002, sl["lamp_z0"] - 0.010, sl["lamp_z1"] + 0.010, dark)
    for k in range(1, 6):
        gx = sl["x"] - lw + k * sl["lamp_w"] / 6
        sbox(bm, s, gx - 0.002, gx + 0.002, f - 0.009, f - 0.006, sl["lamp_z0"], sl["lamp_z1"], dark)
    sbox(bm, s, sl["x"] - lw, sl["x"] + lw, f - 0.009, f - 0.006, (sl["lamp_z0"] + sl["lamp_z1"]) / 2 - 0.002, (sl["lamp_z0"] + sl["lamp_z1"]) / 2 + 0.002, dark)


def build_roof_lenses(scn, mats):
    """Light bar's clear dome and the scene lights' lens faces, as their own objects so Unity can give the dome a
    clear material and switch the scene lenses on and off."""
    b = BAR
    bm = bmesh.new()
    prism_z(bm, stadium(-b["x"], b["x"], b["y0"] - 0.004, b["y1"] + 0.004), b["z0"] + 0.025, b["z1"])
    dome = mesh_object(scn, "Engine07_BarDome", bm, [mats["BarDome"]])
    bm = bmesh.new()
    sl = SCENE_LIGHT
    for s in (-1, 1):
        # Wide rectangular lens in the dark bezel on the housing front.
        lw = sl["lamp_w"] / 2
        sbox(bm, s, sl["x"] - lw, sl["x"] + lw, sl["front"] - 0.006, sl["front"] - 0.002, sl["lamp_z0"], sl["lamp_z1"], 0)
    lenses = mesh_object(scn, "Engine07_SceneLenses", bm, [mats["SceneLens"]])
    return dome, lenses


def build_trim(scn, mats):
    """Chrome, dark trim and red trim parts, laid out from the Metro Fire delivery photos and the Spartan spec."""
    bm = bmesh.new()
    chrome, dark, red = 0, 1, 2
    box(bm, -0.50, 0.50, -4.40, 4.45, 0.30, 0.50, dark)                     # chassis rails
    # Grille: stainless frame with dark slots, 44.45 x 33.5 in (spec), upper section a little wider (photos).
    box(bm, -0.66, 0.66, -4.48, -4.43, 1.26, 1.62, chrome)
    box(bm, -0.58, 0.58, -4.48, -4.43, 0.88, 1.26, chrome)
    for row in range(9):
        z = 0.93 + row * 0.075
        half = 0.58 if z > 1.26 else 0.50
        for col in range(6):
            x0 = -half + 0.05 + col * (2 * half - 0.10) / 6
            box(bm, x0 + 0.012, x0 + (2 * half - 0.10) / 6 - 0.012, -4.485, -4.475, z, z + 0.045, dark)
    for s in (-1, 1):
        sbox(bm, s, 0.74, 1.30, -4.50, -4.44, 1.10, 1.52, chrome)          # headlight cluster housing (2 x 2 LEDs)
        sbox(bm, s, 0.74, 1.30, -4.505, -4.495, 1.30, 1.32, dark)          # divider between the two rows
        sbox(bm, s, 1.06, 1.30, -4.47, -4.44, 1.62, 1.78, chrome)          # turn signal bezel
        # West Coast mirrors on forward arms.
        sbox(bm, s, 1.36, 1.66, -4.30, -4.24, 1.86, 1.92, chrome)
        sbox(bm, s, 1.60, 1.76, -4.35, -4.21, 1.62, 2.26, chrome)
        # Grab rails beside the doors, cab-side scene light over the front wheel, wiper.
        sbox(bm, s, CAB["x"] + 0.02, CAB["x"] + 0.06, -2.98, -2.94, 1.0, 2.3, chrome)
        sbox(bm, s, CAB["x"] + 0.02, CAB["x"] + 0.06, -0.90, -0.86, 1.0, 2.4, chrome)
        sbox(bm, s, CAB["x"], CAB["x"] + 0.04, -2.56, -2.24, 2.58, 2.76, chrome)
        sbox(bm, s, 0.12, 0.88, -4.47, -4.43, 1.84, 1.87, dark)
        # Fender trims, mud flaps, body corner extrusions, beacons' bases, rear light housings.
        arch_trim(bm, s, s * CAB["x"], FRONT_AXLE, 0.68, chrome)
        arch_trim(bm, s, s * BODY["x"], REAR_AXLE, 0.70, chrome)
        sbox(bm, s, 0.80, 1.36, 3.40, 3.43, 0.16, 0.62, dark)
        for y0, y1 in ((0.70, 0.73), (4.42, 4.46)):
            sbox(bm, s, BODY["x"] - 0.01, BODY["x"] + 0.02, y0, y1, 0.50, 2.80, chrome)
        sbox(bm, s, 1.08, 1.34, 4.45, 4.47, 0.80, 1.32, chrome)            # tail light stack
        sbox(bm, s, 1.06, 1.36, 4.45, 4.47, 2.36, 2.68, chrome)            # upper rear warning light
        cyl(bm, (s * 1.24, 4.30, 2.82), 0.09, 0.08, "Z", chrome)           # beacon base on the rear corners
    build_bumper_trim(bm, chrome, dark)
    build_roof_trim(bm, chrome, dark, 3)
    # Deck gun (red) on the pump house, chrome nozzle.
    cyl(bm, (0, -0.15, PUMP["z1"] + 0.10), 0.13, 0.20, "Z", red)
    cyl(bm, (0, -0.30, PUMP["z1"] + 0.24), 0.09, 0.22, "Y", red)
    cyl(bm, (0, -0.50, PUMP["z1"] + 0.24), 0.055, 0.22, "Y", chrome, 16, 0.04)
    # Hard suction hoses on the body top, truck's right (photos).
    for x in (-1.12, -0.96):
        cyl(bm, (x, 2.60, BODY["z1"] + 0.08), 0.07, 3.4, "Y", dark, 16)
    # Rear: hose-bed cover shelf and tailboard edge.
    box(bm, -0.70, 1.06, 4.45, 4.60, 1.76, 1.82, chrome)
    box(bm, -1.30, 1.30, 5.03, 5.06, 0.48, 0.58, chrome)
    return mesh_object(scn, "Engine07_Trim", bm, [mats["Chrome"], mats["DarkTrim"], mats["RedTrim"], mats["WhiteTrim"]])


# ------------------------------------------------------------------------------------------------ wheels


def build_decals(scn):
    """Cab-side decals on both sides (driver's and passenger's), one material slot each, in DECALS order:
    one quad per side just proud of the cab skin, UVs covering the whole texture, never mirrored."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    mats = []
    for slot, (name, path, y, z, h) in enumerate(DECALS):
        mats.append(decal_material(name, path))
        px_w, px_h = bpy.data.images.load(path, check_existing=True).size
        w = h * px_w / px_h
        for s in (1, -1):
            x = s * (CAB["x"] + 0.003)
            # Seen from outside, the image's right edge is toward the rear on the left side, toward the front on the right.
            a, b = (y - w / 2, y + w / 2) if s > 0 else (y + w / 2, y - w / 2)
            corners = (((x, a, z - h / 2), (0, 0)), ((x, b, z - h / 2), (1, 0)), ((x, b, z + h / 2), (1, 1)), ((x, a, z + h / 2), (0, 1)))
            f = bm.faces.new([bm.verts.new(p) for p, _ in corners])
            f.material_index = slot
            for loop, (_, t) in zip(f.loops, corners):
                loop[uv].uv = t
    return mesh_object(scn, "Engine07_Decals", bm, mats)


def decal_material(name, path):
    """A decal material: the texture with its alpha cut out."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tex = next((n for n in nt.nodes if n.type == "TEX_IMAGE"), None) or nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path, check_existing=True)
    tex.image.reload()
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.35
    for attr, value in (("blend_method", "CLIP"), ("surface_render_method", "DITHERED")):
        try:
            setattr(m, attr, value)
        except (AttributeError, TypeError):
            pass
    return m


def build_wheel(scn, mats, name, centre, outward, dual):
    """Tyre(s) by spinning a rounded profile around the axle (X), plus a rim disc, hub and 10 lug nuts."""
    bm = bmesh.new()
    widths = [(-0.16, 0.16)] if not dual else [(-0.31, -0.03), (0.03, 0.31)]
    for wa, wb in widths:
        prof = []
        r0, r1 = 0.30, TYRE_R
        w = wb - wa
        for t, r in ((0.0, r0), (0.0, r1 - 0.06), (0.05, r1 - 0.01), (0.15, r1), (0.85, r1), (0.95, r1 - 0.01), (1.0, r1 - 0.06), (1.0, r0)):
            prof.append(bm.verts.new((wa + t * w, 0, r)))
        edges = [bm.edges.new((prof[i], prof[i + 1])) for i in range(len(prof) - 1)]
        res = bmesh.ops.spin(bm, geom=prof + edges, cent=(0, 0, 0), axis=(1, 0, 0), angle=math.radians(360), steps=48, use_merge=True)
        for f in [g for g in res["geom_last"] + bm.faces[:] if isinstance(g, bmesh.types.BMFace)]:
            f.material_index = 0
    # Rim disc and hub on the outer face.
    face_x = (0.16 if not dual else 0.31) * outward
    rim = bmesh.ops.create_cone(bm, cap_ends=True, segments=32, radius1=0.30, radius2=0.30, depth=0.02)
    bmesh.ops.rotate(bm, verts=rim["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    bmesh.ops.translate(bm, verts=rim["verts"], vec=(face_x - outward * 0.01, 0, 0))
    hub = bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.12, radius2=0.07, depth=0.10)
    bmesh.ops.rotate(bm, verts=hub["verts"], matrix=Matrix.Rotation(math.radians(90 * outward), 3, "Y"))
    bmesh.ops.translate(bm, verts=hub["verts"], vec=(face_x + outward * 0.05, 0, 0))
    for v in rim["verts"] + hub["verts"]:
        for f in v.link_faces:
            f.material_index = 1
    for k in range(10):
        a = 2 * math.pi * k / 10
        nut = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.022, radius2=0.022, depth=0.04)
        bmesh.ops.rotate(bm, verts=nut["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
        bmesh.ops.translate(bm, verts=nut["verts"], vec=(face_x + outward * 0.02, 0.19 * math.cos(a), 0.19 * math.sin(a)))
        for v in nut["verts"]:
            for f in v.link_faces:
                f.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_object(scn, name, bm, [mats["Rubber"], mats["Chrome"]])
    o.location = centre
    for s in scn.objects:
        s.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    return o


# ------------------------------------------------------------------------------------------------ lights


# Group mapping: the game's lens layout (Unity engine space) onto the Hi3D truck's lights (Unity engine space).
# Positions follow the delivery photos: 2 x 2 LED headlight clusters, turn signals above them, light bar on the
# lower front roof, clearance lights on the brow, rear tail stacks (clear, amber, red from the bottom) and upper rear
# warning lights, a row of markers on the rear top rail, and side markers along the rub rail clear of the wheel arches.
# "map" entries override the linear range for one axis with (old, new) pairs (Unity engine space).
NEW = {
    "bar": dict(x=(-0.90, 0.90), y=(2.882, 2.922), z=(BAR_LENS_BACK, BAR_LENS_FRONT), normal=None),
    "SideMarker": dict(x=(-1.43, 1.43), y=(0.62, 0.62), z=(-4.2, 4.3), normal="side",
                       map={2: [(-4.0, -4.35), (-3.13, -3.55), (-2.17, -1.20), (-0.25, 1.30), (1.95, 4.30)]}),
    "FrontLens": dict(x=(-1.14, 1.14), y=(1.05, 1.27), z=(4.51, 4.51), normal=(0, 0, 1)),
    "TurnSignal": dict(x=(-1.18, 1.18), y=(1.55, 1.55), z=(4.47, 4.47), normal=(0, 0, 1)),
    # Cab clearance lights on the brow: one under each scene light, three across the centre (photo).
    "ClearanceLight": dict(x=(-0.86, 0.86), y=(2.55, 2.55), z=(4.21, 4.21), normal=(0, 0, 1),
                           map={0: [(-0.9, -0.86), (-0.45, -0.30), (0.0, 0.0), (0.45, 0.30), (0.9, 0.86)]}),
    "RearLamp": dict(x=(-1.21, 1.21), y=(0.85, 2.43), z=(-4.47, -4.47), normal=(0, 0, -1),
                     map={1: [(0.9, 0.85), (1.27, 0.97), (1.64, 1.09), (2.44, 2.31), (2.70, 2.43)]}),
    "RearUpperMarker": dict(x=(-0.95, 0.95), y=(2.58, 2.58), z=(-4.47, -4.47), normal=(0, 0, -1)),
}
SIZE = {"bar": (0.15, 0.032), "SideMarker": (0.10, 0.05), "FrontLens": (0.18, 0.10), "TurnSignal": (0.20, 0.12),
        "ClearanceLight": (0.07, 0.045), "RearLamp": (0.18, 0.10), "RearUpperMarker": (0.12, 0.04)}


def lens_group(lens):
    return "SideMarker" if lens["name"].startswith("SideMarker") else lens["group"]


def lens_colour(lens, group):
    if group == "bar":
        n = int(lens["mesh"].replace("EmergencyLighting_Lens", "") or 0)
        col = n // 4
        return "LensRed" if min(col, 11 - col) % 2 == 0 else "LensClear"
    if group in ("TurnSignal", "ClearanceLight", "RearUpperMarker"):
        return "LensAmber"
    if group == "FrontLens":
        return "LensClear"   # the 2 x 2 LED headlight clusters read clear when off (photos)
    if group == "RearLamp":
        y = lens["c"][1]
        return "LensClear" if y < 1.1 or y > 2.0 else ("LensAmber" if y < 1.5 else "LensRed")
    if group == "SideMarker":
        return "LensAmber" if lens["c"][2] > 0 else "LensRed"
    return "LensRed"


def bumper_lens(index):
    """Game lenses moved onto the bumper's Whelen M6 lights (spec): the two inner red front flashers go to the M6s on
    the face between the frame rails, the two front side markers become the M6 intersection lights on the bumper tails.
    Returns (Unity position on the surface, Unity normal, size, colour) or None."""
    zc = (BUMPER_Z0 + BUMPER_Z1) / 2 - DROP
    face = -BUMPER_Y0
    table = {
        76: ([-M6_X, zc, face], (0, 0, 1)),       # FrontLensRedInner_L (Unity -x = truck's left)
        80: ([M6_X, zc, face], (0, 0, 1)),        # FrontLensRedInner_R
        70: ([-BUMPER_X, zc, 4.75], (-1, 0, 0)),  # SideMarker, left front
        75: ([BUMPER_X, zc, 4.75], (1, 0, 0)),    # SideMarker, right front
    }
    if index not in table:
        return None
    u, normal = table[index]
    return u, normal, M6, "LensRed"


def build_lights(scn, mats):
    lenses = json.load(open(os.path.join(HERE, "lenses_engine07.json")))
    groups = {}
    for lens in lenses:
        groups.setdefault(lens_group(lens), []).append(lens)
    targets = []
    bm = bmesh.new()
    order = ["LensRed", "LensClear", "LensAmber", "DarkTrim"]
    for g, members in groups.items():
        spec = NEW[g]
        old = [[min(l["c"][k] for l in members), max(l["c"][k] for l in members)] for k in range(3)]
        for lens in members:
            u = []
            for k, axis in enumerate("xyz"):
                pairs = spec.get("map", {}).get(k)
                if pairs:
                    u.append(min(pairs, key=lambda p: abs(p[0] - lens["c"][k]))[1])
                    continue
                lo, hi = old[k]
                nlo, nhi = spec[axis]
                t = 0.5 if hi - lo < 1e-4 else (lens["c"][k] - lo) / (hi - lo)
                u.append(nlo + t * (nhi - nlo))
            if spec["normal"] == "side":
                normal = (1 if u[0] > 0 else -1, 0, 0)
            elif spec["normal"] is None:   # light bar: front or back face
                normal = (0, 0, 1) if u[2] > (spec["z"][0] + spec["z"][1]) / 2 else (0, 0, -1)
            else:
                normal = spec["normal"]
            w, h = SIZE[g]
            if g == "ClearanceLight" and abs(lens["c"][0]) > 0.6:
                # Outer cab lights sit on the scene light mounts, under the lamps (photo).
                u[1], u[2] = SCENE_LIGHT["clearance_z"] - DROP, -SCENE_LIGHT["clearance_face"]
            colour = lens_colour(lens, g)
            override = bumper_lens(lens["i"])
            if override:
                u, normal, (w, h), colour = override
            nb = Vector((-normal[0], -normal[2], normal[1]))   # Blender direction
            centre = to_blender(u)
            # Right axis in Blender for the cap's width: horizontal and perpendicular to the normal.
            right = Vector((0, 0, 1)).cross(nb).normalized() if abs(nb.z) < 0.9 else Vector((1, 0, 0))
            up = nb.cross(right).normalized()
            depth = 0.03
            def cap(c, w, h, d, mat):
                pts = []
                for sd in (0, 1):
                    for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                        pts.append(c + right * (sx * w / 2) + up * (sz * h / 2) + nb * (sd * d))
                vs = [bm.verts.new(p) for p in pts]
                for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
                    bm.faces.new([vs[i] for i in f]).material_index = order.index(mat)
            cap(centre - nb * 0.02, w + 0.03, h + 0.03, 0.035, "DarkTrim")          # housing
            cap(centre + nb * 0.01, w, h, depth, colour)                           # lens cap
            game = centre + nb * (0.01 + depth + 0.004)
            targets.append({"i": lens["i"], "name": lens["name"], "pos": to_unity(game), "normal": [normal[0], normal[1], normal[2]],
                            "centre": to_unity(centre + nb * (0.01 + depth / 2)), "size": [w, h, depth]})
    # Decorative lamps with no game lens: the bottom (back-up) lamp of each tail stack and the rear corner beacons.
    for s in (-1, 1):
        # The headlight cluster's lower inner lamp, whose game flasher moved to the bumper M6.
        box(bm, *sorted((s * 0.862, s * 1.072)), -4.530, -4.500, 1.135, 1.265, order.index("DarkTrim"))
        box(bm, *sorted((s * 0.877, s * 1.057)), -4.555, -4.525, 1.150, 1.250, order.index("LensClear"))
        box(bm, *sorted((s * 1.12, s * 1.30)), 4.45, 4.50, 0.83, 0.93, order.index("LensClear"))
        cyl(bm, (s * 1.24, 4.30, 2.93), 0.075, 0.14, "Z", order.index("LensClear"), 20, 0.06)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_object(scn, "Engine07_Lights", bm, [mats[m] for m in order])
    json.dump(targets, open(os.path.join(HERE, "lens_targets_engine07.json"), "w"), indent=1)
    return o, len(targets)


def build():
    scn = new_scene()
    mats = {
        "Paint": material("Engine07_Paint", (0.55, 0.05, 0.04), rough=0.35),
        "Interior": material("Engine07_Interior", (0.03, 0.035, 0.04), rough=0.8),
        "Glass": material("Engine07_Glass", (0.05, 0.07, 0.08), rough=0.05, alpha=0.45),
        "Chrome": material("Engine07_Chrome", (0.8, 0.8, 0.82), metal=1.0, rough=0.15),
        "DarkTrim": material("Engine07_DarkTrim", (0.04, 0.045, 0.05), rough=0.5),
        "Rubber": material("Engine07_Rubber", (0.025, 0.025, 0.025), rough=0.85),
        "RedTrim": material("Engine07_RedTrim", (0.55, 0.04, 0.04), rough=0.3),
        "WhiteTrim": material("Engine07_WhiteTrim", (0.86, 0.87, 0.88), rough=0.3),
        "BarDome": material("Engine07_BarDome", (0.92, 0.94, 0.96), rough=0.05, alpha=0.22),
        "SceneLens": material("Engine07_SceneLens", (0.80, 0.82, 0.84), rough=0.1),
        "LensRed": material("Engine07_LensRed", (0.45, 0.02, 0.02), rough=0.15),
        "LensClear": material("Engine07_LensClear", (0.75, 0.75, 0.72), rough=0.1),
        "LensAmber": material("Engine07_LensAmber", (0.75, 0.35, 0.03), rough=0.15),
    }
    shell = build_shell(scn, mats)
    assign_uvs(shell)
    build_glass(scn, mats)
    build_trim(scn, mats)
    build_decals(scn)
    build_roof_lenses(scn, mats)
    for name, y, x, out, dual in (("WheelFrontLeft", FRONT_AXLE, 1.27, 1, False), ("WheelFrontRight", FRONT_AXLE, -1.27, -1, False),
                                  ("WheelRearLeft", REAR_AXLE, 1.10, 1, True), ("WheelRearRight", REAR_AXLE, -1.10, -1, True)):
        build_wheel(scn, mats, name, Vector((x, y, AXLE_Z)), out, dual)
    _, n = build_lights(scn, mats)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in scn.objects if o.type == "MESH")
    print("built", [o.name for o in scn.objects], "lens fixtures", n, "triangles", tris)


build()
