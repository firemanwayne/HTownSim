"""Clean hard-surface Houston Fire ambulance (Ambulance 07), built in headless Blender from the reference photos.

    blender -b --python build_ambulance07.py [-- previews]

Rebuilds the Hi3D GLB (one 5-million-triangle mesh, no separable wheels, glass or lights, mixed unit numbers) as a clean
model at its measured size (amb_layout.py). Objects, all at the origin except the wheels (origin on the axle):
  Amb07_Shell   cab (hood, windshield, roof), box, rear step: bevelled solids with boolean wheel wells and window
                recesses; planar UVs into the livery atlas (make_livery_amb.py draws it)
  Amb07_Glass   windshield, cab door glass, box and rear-door windows (tinted)
  Amb07_Trim    chrome bumper, grille, push bar, mirrors, handles, running boards, rear bumper pads, underbody
  Amb07_Lights  a housing and coloured lens cap for every warning, clearance and tail light; lens_targets_amb07.json
  Amb07_Decals  the HFD shield on both cab doors (own texture, so it stays sharp)
  WheelFrontLeft/Right, WheelRearLeft/Right   round tyres (duals at the rear), rims, hubs, lug nuts, on their axles
Writes Ambulance07_Clean.blend and the FBX into the Unity project, and (with "previews") renders check views.
"""
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import amb_layout as A  # noqa: E402

UNITY_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Ambulance07"
FBX = os.path.join(UNITY_DIR, "Ambulance07.fbx")
LIVERY = os.path.join(UNITY_DIR, "Ambulance07_Livery.png")
LOGO = os.path.join(UNITY_DIR, "HFD_Logo.png")
PREVIEWS = "previews" in sys.argv

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.name = "Ambulance07Clean"


# ------------------------------------------------------------------------------------------------ helpers


def material(name, rgb, metal=0.0, rough=0.5, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    b.inputs["Alpha"].default_value = alpha
    m.diffuse_color = (*rgb, alpha)
    return m


def mesh_object(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    o = bpy.data.objects.new(name, me)
    scene.collection.objects.link(o)
    return o


def box(bm, x0, x1, y0, y1, z0, z1, mat=0):
    v = [bm.verts.new(p) for p in ((x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1))]
    for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        bm.faces.new([v[i] for i in f]).material_index = mat


def sbox(bm, s, xa, xb, y0, y1, z0, z1, mat=0):
    """Box on one side: x from s*xa to s*xb (s = +1 truck's left, -1 right)."""
    box(bm, *sorted((s * xa, s * xb)), y0, y1, z0, z1, mat)


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


def cylinder_x_solid(bm, cy, cz, r, x0, x1, seg=48):
    cyl(bm, ((x0 + x1) / 2, cy, cz), r, abs(x1 - x0), "X", 0, seg)


def apply_mods(o):
    for s in scene.objects:
        s.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


# ------------------------------------------------------------------------------------------------ shell


def build_shell(mats):
    paint, interior = mats["Paint"], mats["Interior"]
    bm = bmesh.new()
    prism_x(bm, A.CAB_PROFILE, -A.CAB_X, A.CAB_X)
    box(bm, -A.BOX_X, A.BOX_X, A.BOX_Y0, A.BOX_Y1, A.BOX_Z0, A.BOX_Z1)
    s = A.STEP
    box(bm, -s["x"], s["x"], s["y0"], s["y1"], s["z0"], s["z1"])                  # rear step (diamond plate top)
    shell = mesh_object("Amb07_Shell", bm, [paint, interior])

    cutters = []

    def cutter(name, fn):
        cb = bmesh.new()
        fn(cb)
        for f in cb.faces:
            f.material_index = 1                    # recess walls take the Interior (dark) material
        o = mesh_object("Cutter_" + name, cb, [paint, interior])
        o.display_type = "WIRE"
        cutters.append(o)

    # Wheel wells: per side, so the cut never reaches across the cab or box.
    for sgn in (-1, 1):
        cutter("ArchFront%+d" % sgn, lambda b, sgn=sgn: cylinder_x_solid(b, A.AX_F, A.WR, 0.50, *sorted((sgn * (A.CAB_X - 0.40), sgn * (A.CAB_X + 0.05)))))
        cutter("ArchRear%+d" % sgn, lambda b, sgn=sgn: cylinder_x_solid(b, A.AX_R, A.WR, 0.53, *sorted((sgn * 0.55, sgn * (A.BOX_X + 0.05)))))

    # Windshield recess: a slab slanted like the glass.
    def windshield(b):
        (y0, z0), (y1, z1) = A.CAB_PROFILE[4], A.CAB_PROFILE[3]
        d = Vector((0, y1 - y0, z1 - z0)).normalized()
        n = Vector((0, -d.z, d.y))
        pts = [Vector((x, y, z)) for x in (-0.86, 0.86) for y, z in ((y0, z0), (y1, z1))]
        quad = [pts[0], pts[2], pts[3], pts[1]]
        vs = [b.verts.new(q + n * 0.05) for q in quad] + [b.verts.new(q - n * 0.25) for q in quad]
        for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
            b.faces.new([vs[i] for i in f])
    cutter("Windshield", windshield)

    # Cab door windows (both sides), the patient-door window (curb side only) and the two rear-door windows.
    for sgn in (-1, 1):
        cutter("CabWin%+d" % sgn, lambda b, sgn=sgn: prism_x(b, A.WIN_CAB, *sorted((sgn * (A.CAB_X - 0.25), sgn * (A.CAB_X + 0.05)))))
    w = A.WIN_BOX_RIGHT
    c = 0.05
    win = [(w["y0"] + c, w["z0"]), (w["y1"] - c, w["z0"]), (w["y1"], w["z0"] + c), (w["y1"], w["z1"] - c),
           (w["y1"] - c, w["z1"]), (w["y0"] + c, w["z1"]), (w["y0"], w["z1"] - c), (w["y0"], w["z0"] + c)]
    cutter("BoxWin", lambda b: prism_x(b, win, -(A.BOX_X + 0.05), -(A.BOX_X - 0.20)))
    for xc, ww in A.WIN_REAR:
        cutter("RearWin%+.2f" % xc, lambda b, xc=xc, ww=ww: box(b, xc - ww / 2, xc + ww / 2, A.BOX_Y1 - 0.20, A.BOX_Y1 + 0.05, *A.WIN_REAR_Z))

    for o in cutters:
        m = shell.modifiers.new(o.name, "BOOLEAN")
        m.operation = "DIFFERENCE"
        m.solver = "EXACT"
        m.use_self = True
        m.object = o
    apply_mods(shell)
    for o in cutters:
        bpy.data.objects.remove(o, do_unlink=True)
    bev = shell.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.05
    bev.segments = 3
    bev.limit_method = "ANGLE"
    bev.angle_limit = math.radians(35)
    bev.harden_normals = True
    wn = shell.modifiers.new("WeightedNormal", "WEIGHTED_NORMAL")
    wn.keep_sharp = True
    apply_mods(shell)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35), keep_sharp_edges=True)
    return shell


def assign_uvs(shell):
    """Planar UVs into the livery atlas by dominant face normal; the recess walls and mid-body end faces go to a dark spot."""
    me = shell.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        n = poly.normal
        cx, cy, cz = poly.center
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        if poly.material_index == 1:
            region = "under"
        elif az >= ax and az >= ay:
            region = "top" if n.z > 0 else "under"
        elif ax >= ay:
            region = "left" if n.x > 0 else "right"
        elif n.y < 0:
            # The box front is one large face (its centre is below the cab roof), mapped with its true projection: the part
            # hidden behind the cab just runs off the atlas; the part above the cab roof lands on the "boxfront" region.
            region = "front" if cy < -3.0 else ("boxfront" if abs(cy - A.BOX_Y0) < 0.03 else "under")
        else:
            region = "rear" if cy > 3.1 else "under"
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = A.uv(region, co.x, co.y, co.z)


# ------------------------------------------------------------------------------------------------ glass


def build_glass(mats):
    bm = bmesh.new()
    (y0, z0), (y1, z1) = A.CAB_PROFILE[4], A.CAB_PROFILE[3]
    d = Vector((0, y1 - y0, z1 - z0)).normalized()
    n = Vector((0, -d.z, d.y))
    base, top = Vector((0, y0, z0)) - n * 0.04, Vector((0, y1, z1)) - n * 0.04
    bm.faces.new([bm.verts.new((x, p.y, p.z)) for x, p in ((-0.84, base), (0.84, base), (0.84, top), (-0.84, top))])
    for sgn in (-1, 1):
        x = sgn * (A.CAB_X - 0.04)
        vs = [bm.verts.new((x, y, z)) for y, z in A.WIN_CAB]
        bm.faces.new(vs if sgn > 0 else list(reversed(vs)))
    w = A.WIN_BOX_RIGHT
    x = -(A.BOX_X - 0.04)
    bm.faces.new(list(reversed([bm.verts.new((x, y, z)) for y, z in ((w["y0"], w["z0"]), (w["y1"], w["z0"]), (w["y1"], w["z1"]), (w["y0"], w["z1"]))])))
    y = A.BOX_Y1 - 0.04
    for xc, ww in A.WIN_REAR:
        bm.faces.new(list(reversed([bm.verts.new((xc + sx * ww / 2, y, z)) for sx, z in ((-1, A.WIN_REAR_Z[0]), (1, A.WIN_REAR_Z[0]), (1, A.WIN_REAR_Z[1]), (-1, A.WIN_REAR_Z[1]))])))
    return mesh_object("Amb07_Glass", bm, [mats["Glass"]])


# ------------------------------------------------------------------------------------------------ trim

CHROME, DARK, WHITE, RUBBER = 0, 1, 2, 3


def build_trim(mats):
    bm = bmesh.new()
    # Front chrome bumper and black valance, push bar, grille, headlight housings, plate.
    box(bm, -1.05, 1.05, -3.54, -3.40, 0.50, 0.80, CHROME)
    box(bm, -0.80, 0.80, -3.50, -3.40, 0.34, 0.50, DARK)
    box(bm, -0.16, 0.16, -3.55, -3.54, 0.58, 0.72, WHITE)
    for sgn in (-1, 1):
        cyl(bm, (sgn * 0.93, -3.60, 0.88), 0.045, 0.86, "Z", DARK, 16)
        cyl(bm, (sgn * 0.93, -3.60, 1.31), 0.045, 0.06, "Z", DARK, 16)
    cyl(bm, (0, -3.60, 1.30), 0.04, 1.86, "X", DARK, 16)
    cyl(bm, (0, -3.60, 0.86), 0.035, 1.86, "X", DARK, 16)
    box(bm, -0.60, 0.60, -3.435, -3.40, 0.84, 1.28, CHROME)                        # grille frame
    box(bm, -0.54, 0.54, -3.445, -3.43, 0.88, 1.24, DARK)                          # grille mesh
    box(bm, -0.54, 0.54, -3.455, -3.445, 1.04, 1.07, CHROME)                       # centre bar
    for sgn in (-1, 1):
        sbox(bm, sgn, 0.66, 1.00, -3.43, -3.395, 0.99, 1.27, CHROME)                # headlight housings
        sbox(bm, sgn, 0.66, 1.00, -3.43, -3.395, 0.88, 0.97, DARK)                  # turn-lamp housings
        # Tow mirrors: arm, head, glass face.
        sbox(bm, sgn, 1.0, 1.28, -2.08, -2.04, 1.52, 1.58, DARK)
        sbox(bm, sgn, 1.20, 1.31, -2.14, -1.94, 1.42, 2.00, DARK)
        sbox(bm, sgn, 1.205, 1.305, -1.94, -1.925, 1.45, 1.97, CHROME)
        # Door handles, running boards, fender flares at the rear arch, mud flaps.
        sbox(bm, sgn, A.CAB_X, A.CAB_X + 0.035, -1.05, -0.88, 1.40, 1.45, CHROME)
        sbox(bm, sgn, A.CAB_X, A.CAB_X + 0.17, -1.95, -0.90, 0.42, 0.48, DARK)
        sbox(bm, sgn, 0.95, 1.23, 2.14, 2.17, 0.12, 0.55, RUBBER)
    # Underbody (frame, tank) between the wheels, and the dark gap between cab and box.
    box(bm, -0.60, 0.60, -3.2, 3.2, 0.28, 0.44, DARK)
    box(bm, -0.95, 0.95, A.CAB_BACK, A.BOX_Y0, 0.60, 2.04, DARK)
    # Rear bumper end pads (grey block, black rubber) and step edge.
    for sgn in (-1, 1):
        sbox(bm, sgn, 0.82, 1.22, 3.25, 3.66, 0.38, 0.70, WHITE)
        sbox(bm, sgn, 0.86, 1.00, 3.66, 3.70, 0.46, 0.64, RUBBER)
    box(bm, -1.13, 1.13, 3.62, 3.65, 0.50, 0.58, CHROME)
    # Rear door hinges and latches, side compartment handles (chrome ovals / T-handles), box edge rails.
    for sgn in (-1, 1):
        for z in (1.0, 1.9, 2.6):
            sbox(bm, sgn, 1.19, 1.24, 3.17, 3.27, z - 0.12, z + 0.12, CHROME)
        cyl(bm, (sgn * 0.23, 3.275, 1.05), 0.05, 0.05, "Y", CHROME, 16)
        cyl(bm, (sgn * 0.23, 3.275, 1.30), 0.05, 0.05, "Y", CHROME, 16)
    handles = {1: ((-0.22, 1.60), (0.27, 0.80), (0.81, 0.80), (2.80, 0.80)),          # driver's side compartment doors
               -1: ((-0.22, 1.60), (0.66, 1.45), (2.80, 0.80))}                      # curb side: compartment, patient door, rear
    for sgn, spots in handles.items():
        for y, z in spots:
            sbox(bm, sgn, A.BOX_X, A.BOX_X + 0.04, y - 0.07, y + 0.07, z - 0.03, z + 0.03, CHROME)
    return mesh_object("Amb07_Trim", bm, [mats["Chrome"], mats["DarkTrim"], mats["WhiteTrim"], mats["Rubber"]])


# ------------------------------------------------------------------------------------------------ lights

# name, group, lens material, centre (x, y, z) on the body surface, outward normal axis, width, height, round?
LENSES = []


# Warning-light groups use their own lens materials ("...Warn") so only they strobe in the game, not the headlights or tail lights.
WARN_GROUPS = ("BoxFront", "SideTop", "RearTop", "RearCorner", "Grille")


def lens(name, group, col, x, y, z, normal, w, h, round_=False):
    if group in WARN_GROUPS:
        # The warning lights are red and amber, never white: red alternates between two groups (A: LensRedWarn, B: LensRedWarn2).
        col = ("LensRedWarn" if x < 0 else "LensRedWarn2") if col == "LensClear" else col + "Warn"
    LENSES.append(dict(name=name, group=group, col=col, c=(x, y, z), n=normal, w=w, h=h, round=round_))


def define_lenses():
    top = A.BOX_Y0
    for sgn, side in ((-1, "R"), (1, "L")):
        lens("BoxFrontCorner_" + side, "BoxFront", "LensClear", sgn * 1.12, top, 2.78, "-y", 0.22, 0.12)
    for i, (x, col) in enumerate(zip((-0.76, -0.38, 0.0, 0.38, 0.76), ("LensRed", "LensRed", "LensClear", "LensRed", "LensRed"))):
        lens("BoxFront_%d" % i, "BoxFront", col, x, top, 2.46, "-y", 0.26, 0.10)
    for sgn, side in ((-1, "R"), (1, "L")):
        lens("Headlight_" + side, "Headlight", "LensClear", sgn * 0.83, -3.43, 1.13, "-y", 0.30, 0.16)
        lens("Turn_" + side, "Turn", "LensAmber", sgn * 0.83, -3.43, 0.925, "-y", 0.30, 0.07)
        lens("GrilleLed_" + side, "Grille", "LensRed", sgn * 0.30, -3.455, 1.18, "-y", 0.14, 0.04)
        lens("Fog_" + side, "Fog", "LensClear", sgn * 0.62, -3.54, 0.65, "-y", 0.14, 0.10)
        nx = "+x" if sgn > 0 else "-x"
        for i, y in enumerate((-0.20, 1.40, 3.05)):
            lens("BoxTop%d_%s" % (i, side), "SideTop", "LensClear", sgn * A.BOX_X, y, 2.80, nx, 0.20, 0.08)
        lens("SideMarkerA_" + side, "SideMarker", "LensAmber", sgn * A.BOX_X, 0.75, 1.27, nx, 0.12, 0.06)
        lens("SideMarkerB_" + side, "SideMarker", "LensAmber", sgn * A.BOX_X, 2.45, 1.27, nx, 0.12, 0.06)
        lens("SideMarkerC_" + side, "SideMarker", "LensRed", sgn * A.BOX_X, 3.15, 1.27, nx, 0.12, 0.06)
        lens("FenderMarker_" + side, "SideMarker", "LensAmber", sgn * A.CAB_X, -3.05, 1.20, nx, 0.10, 0.05)
        # Rear: corner columns, stop, backup, turn.
        for i, (z, col) in enumerate(((1.55, "LensAmber"), (1.90, "LensClear"), (2.25, "LensAmber"), (2.60, "LensClear"))):
            lens("RearCorner%d_%s" % (i, side), "RearCorner", col, sgn * 1.14, A.BOX_Y1, z, "+y", 0.07, 0.14)
        lens("Stop_" + side, "Stop", "LensRed", sgn * 1.02, A.BOX_Y1, 0.80, "+y", 0.11, 0.11, True)
        lens("Backup_" + side, "Backup", "LensClear", sgn * 0.76, A.BOX_Y1, 0.80, "+y", 0.11, 0.11, True)
        lens("RearTurn_" + side, "RearTurn", "LensAmber", sgn * 1.14, A.BOX_Y1, 1.15, "+y", 0.07, 0.12)
    for i, x in enumerate((-0.80, -0.27, 0.27, 0.80)):
        lens("RearTop_%d" % i, "RearTop", "LensClear", x, A.BOX_Y1, 2.82, "+y", 0.18, 0.07)


AXES = {"-y": (Vector((0, -1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
        "+y": (Vector((0, 1, 0)), Vector((1, 0, 0)), Vector((0, 0, 1))),
        "+x": (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))),
        "-x": (Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))}


def build_lights(mats, write_metadata=True):
    define_lenses()
    order = ["LensRed", "LensClear", "LensAmber", "LensRedWarn", "LensRedWarn2", "LensAmberWarn", "DarkTrim"]
    bm = bmesh.new()
    targets = []
    for L in LENSES:
        n, right, up = AXES[L["n"]]
        c = Vector(L["c"])
        w, h, depth = L["w"], L["h"], 0.03

        def cap(centre, w, h, d, mat):
            idx = order.index(mat)
            pts = [centre + right * (sx * w / 2) + up * (sz * h / 2) + n * (sd * d) for sd in (0, 1) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            vs = [bm.verts.new(p) for p in pts]
            for f in ((0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
                bm.faces.new([vs[i] for i in f]).material_index = idx

        if L["round"]:
            axis = {"+y": "Y", "-y": "Y", "+x": "X", "-x": "X"}[L["n"]]
            sgn = 1 if n.dot(Vector((1, 1, 1))) > 0 else -1
            cyl(bm, c + n * (0.005 * 1), w / 2 + 0.015, 0.03, axis, order.index("DarkTrim"), 20)
            cyl(bm, c + n * 0.022, w / 2, 0.02, axis, order.index(L["col"]), 20)
        else:
            cap(c - n * 0.015, w + 0.03, h + 0.03, 0.035, "DarkTrim")
            cap(c + n * 0.01, w, h, depth, L["col"])
        targets.append(dict(name=L["name"], group=L["group"], material=L["col"], centre=list(c + n * (0.01 + depth / 2)),
                            normal=list(n), size=[w, h, depth], round=L["round"]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_object("Amb07_Lights", bm, [mats[m] for m in order])
    if write_metadata:
        json.dump(targets, open(os.path.join(HERE, "lens_targets_amb07.json"), "w"), indent=1)
    return o, len(targets)


# ------------------------------------------------------------------------------------------------ decals


def build_decals(mats):
    """The HFD shield, centred on each cab door over the white band."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    y, z, h = -1.42, 1.22, 0.40
    w = h * 1238 / 1523
    for s in (1, -1):
        x = s * (A.CAB_X + 0.004)
        a, b = (y - w / 2, y + w / 2) if s > 0 else (y + w / 2, y - w / 2)      # never mirrored
        corners = (((x, a, z - h / 2), (0, 0)), ((x, b, z - h / 2), (1, 0)), ((x, b, z + h / 2), (1, 1)), ((x, a, z + h / 2), (0, 1)))
        f = bm.faces.new([bm.verts.new(p) for p, _ in corners])
        for loop, (_, t) in zip(f.loops, corners):
            loop[uvl].uv = t
    return mesh_object("Amb07_Decals", bm, [mats["Logo"]])


# ------------------------------------------------------------------------------------------------ wheels


def build_wheel(mats, name, centre, outward, dual):
    bm = bmesh.new()
    widths = [(-0.265, -0.03), (0.03, 0.265)] if dual else [(-0.1225, 0.1225)]
    r0, r1 = 0.26, A.WR
    for wa, wb in widths:
        w = wb - wa
        prof = [bm.verts.new((wa + t * w, 0, r)) for t, r in ((0.0, r0), (0.0, r1 - 0.05), (0.06, r1 - 0.008), (0.18, r1), (0.82, r1), (0.94, r1 - 0.008), (1.0, r1 - 0.05), (1.0, r0))]
        edges = [bm.edges.new((prof[i], prof[i + 1])) for i in range(len(prof) - 1)]
        bmesh.ops.spin(bm, geom=prof + edges, cent=(0, 0, 0), axis=(1, 0, 0), angle=math.radians(360), steps=48, use_merge=True)
    face_x = (0.265 if dual else 0.1225) * outward
    rim = bmesh.ops.create_cone(bm, cap_ends=True, segments=32, radius1=0.275, radius2=0.275, depth=0.02)
    bmesh.ops.rotate(bm, verts=rim["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    bmesh.ops.translate(bm, verts=rim["verts"], vec=(face_x - outward * 0.01, 0, 0))
    hub = bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=0.12, radius2=0.08, depth=0.09)
    bmesh.ops.rotate(bm, verts=hub["verts"], matrix=Matrix.Rotation(math.radians(90 * outward), 3, "Y"))
    bmesh.ops.translate(bm, verts=hub["verts"], vec=(face_x + outward * 0.04, 0, 0))
    for v in rim["verts"] + hub["verts"]:
        for f in v.link_faces:
            f.material_index = 1
    for k in range(8):
        a = 2 * math.pi * k / 8
        nut = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=0.022, radius2=0.022, depth=0.035)
        bmesh.ops.rotate(bm, verts=nut["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
        bmesh.ops.translate(bm, verts=nut["verts"], vec=(face_x + outward * 0.02, 0.185 * math.cos(a), 0.185 * math.sin(a)))
        for v in nut["verts"]:
            for f in v.link_faces:
                f.material_index = 1
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = mesh_object(name, bm, [mats["Rubber"], mats["Chrome"]])
    o.location = centre
    for s in scene.objects:
        s.select_set(False)
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    return o


# ------------------------------------------------------------------------------------------------ build


def build(*, livery=LIVERY, logo=LOGO, write_metadata=True):
    mats = {
        "Paint": material("Amb07_Paint", (0.55, 0.05, 0.04), rough=0.3),
        "Interior": material("Amb07_Interior", (0.03, 0.035, 0.04), rough=0.8),
        "Glass": material("Amb07_Glass", (0.05, 0.07, 0.08), rough=0.05, alpha=0.45),
        "Chrome": material("Amb07_Chrome", (0.8, 0.8, 0.82), metal=1.0, rough=0.15),
        "DarkTrim": material("Amb07_DarkTrim", (0.04, 0.045, 0.05), rough=0.5),
        "WhiteTrim": material("Amb07_WhiteTrim", (0.62, 0.64, 0.66), rough=0.4),
        "Rubber": material("Amb07_Rubber", (0.025, 0.025, 0.025), rough=0.85),
        "LensRed": material("Amb07_LensRed", (0.45, 0.02, 0.02), rough=0.15),
        "LensClear": material("Amb07_LensClear", (0.75, 0.75, 0.72), rough=0.1),
        "LensAmber": material("Amb07_LensAmber", (0.75, 0.35, 0.03), rough=0.15),
        "LensRedWarn": material("Amb07_LensRedWarn", (0.45, 0.02, 0.02), rough=0.15),
        "LensRedWarn2": material("Amb07_LensRedWarn2", (0.45, 0.02, 0.02), rough=0.15),
        "LensAmberWarn": material("Amb07_LensAmberWarn", (0.75, 0.35, 0.03), rough=0.15),
        "Logo": material("Amb07_Logo", (1, 1, 1), rough=0.4),
    }
    # Livery atlas on the paint, logo on the decal (placeholders if the images are not drawn yet).
    def image(path, size):
        if os.path.exists(path):
            return bpy.data.images.load(path)
        return bpy.data.images.new(os.path.basename(path), *size)
    for key, path, size in (("Paint", livery, A.ATLAS), ("Logo", logo, (64, 64))):
        if path is None:
            continue
        nt = mats[key].node_tree
        b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = image(path, size)
        nt.links.new(t.outputs["Color"], b.inputs["Base Color"])
        if key == "Logo":
            nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    shell = build_shell(mats)
    assign_uvs(shell)
    build_glass(mats)
    build_trim(mats)
    _, count = build_lights(mats, write_metadata)
    build_decals(mats)
    for name, x, ay, z, out, dual in (("WheelFrontLeft", A.FRONT_WHEEL_X, A.AX_F, A.WR, 1, False), ("WheelFrontRight", -A.FRONT_WHEEL_X, A.AX_F, A.WR, -1, False),
                                      ("WheelRearLeft", A.REAR_WHEEL_X, A.AX_R, A.WR, 1, True), ("WheelRearRight", -A.REAR_WHEEL_X, A.AX_R, A.WR, -1, True)):
        build_wheel(mats, name, Vector((x, ay, z)), out, dual)
    tris = sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in scene.objects if o.type == "MESH")
    print("BUILT", [o.name for o in scene.objects], "lens fixtures", count, "triangles", tris)


def previews():
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "TEXTURE"
    scene.display.shading.show_cavity = False
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    w = bpy.data.worlds.new("w")
    w.color = (0.62, 0.66, 0.70)
    scene.world = w
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.clip_start, cam.data.clip_end = 0.05, 100
    out = os.path.join(HERE, "amb_%s.png")
    views = {"front34": ((6.5, -9.5, 3.6), (0, 0, 1.4), 38), "rear34": ((-6.5, 9.5, 3.6), (0, 0, 1.4), 38), "left": ((13.5, 0, 1.6), (0, 0, 1.4), 40),
             "right": ((-13.5, 0, 1.6), (0, 0, 1.4), 40), "front": ((0, -11, 1.8), (0, 0, 1.5), 45), "rear": ((0, 11, 1.8), (0, 0, 1.5), 45),
             "top": ((0.01, 0.0, 14), (0, 0, 1.4), 38), "frontclose": ((2.6, -5.2, 1.6), (0, -3.0, 1.2), 40)}
    for name, (loc, target, lens_mm) in views.items():
        cam.location = loc
        cam.data.lens = lens_mm
        cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = out % name
        bpy.ops.render.render(write_still=True)


def export():
    names = ["Amb07_Shell", "Amb07_Glass", "Amb07_Trim", "Amb07_Lights", "Amb07_Decals", "WheelFrontLeft", "WheelFrontRight", "WheelRearLeft", "WheelRearRight"]
    for o in scene.objects:
        o.select_set(o.name in names)
    bpy.context.view_layer.objects.active = scene.objects["Amb07_Shell"]
    os.makedirs(UNITY_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="OFF",
                             use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    print("EXPORTED", FBX)


if __name__ == "__main__":
    build()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Ambulance07_Clean.blend"), copy=True)
    if PREVIEWS:
        previews()
    if "export" in sys.argv:
        export()
    print("ALL DONE")
