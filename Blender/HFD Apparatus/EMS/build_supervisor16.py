"""Houston Fire Department Supervisor 16: a red Chevrolet Suburban, built in headless Blender.

    blender -b --python build_supervisor16.py -- export

Same frame as the ambulance (amb_layout.py): metres, Z up, front at -Y, +X = the vehicle's LEFT (driver's) side, origin on the ground at
the centre of the wheelbase. Objects: Sub_Shell (red body with roof, wheel wells cut), Sub_Glass (tinted), Sub_Trim (bumpers, grille, mirrors,
light-bar base, white stripe), Sub_Lights (lens caps: Sub_LensRed / Sub_LensClear flash, headlights and tail lights burn steady),
Sub_Decals (white lettering) and SubWheelFrontLeft/Right, SubWheelRearLeft/Right (origin on the axle, so they spin in place).
Writes Supervisor16_Clean.blend beside this script and, with "export", the FBX into the Unity vehicle folder.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
UNITY_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Supervisor16"
FBX = os.path.join(UNITY_DIR, "Supervisor16.fbx")

AX_F, AX_R, WR = -1.70, 1.60, 0.40
HALF_W = 0.99
WHEEL_X = 0.86

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


def material(name, rgb, metal=0.0, rough=0.5, alpha=1.0, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    b.inputs["Alpha"].default_value = alpha
    if emit:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*rgb, alpha)
    return m


RED = material("Sub_Red", (0.70, 0.035, 0.04), 0.4, 0.22)
WHITE = material("Sub_White", (0.92, 0.92, 0.9), 0.0, 0.5)
GLASS = material("Sub_Glass", (0.05, 0.08, 0.1), 0.0, 0.1, 0.8)
BLACK = material("Sub_Black", (0.02, 0.02, 0.022), 0.0, 0.7)
CHROME = material("Sub_Chrome", (0.80, 0.80, 0.83), 1.0, 0.18)
TYRE = material("Sub_Tyre", (0.03, 0.03, 0.03), 0.0, 0.9)
LENS_RED = material("Sub_LensRed", (0.9, 0.05, 0.05), 0.0, 0.2, 1.0, 1.5)
LENS_CLEAR = material("Sub_LensClear", (0.95, 0.97, 1.0), 0.0, 0.2, 1.0, 1.5)
HEADLAMP = material("Sub_Headlamp", (1.0, 0.96, 0.85), 0.0, 0.2, 1.0, 1.0)
TAIL = material("Sub_Tail", (0.8, 0.02, 0.02), 0.0, 0.3, 1.0, 0.8)
PIN = material("Sub_Pin", (0.55, 0.03, 0.04), 0.0, 0.4)
SHIELD_BLUE = material("Sub_ShieldBlue", (0.04, 0.22, 0.62), 0.0, 0.35)
SHIELD_GOLD = material("Sub_ShieldGold", (0.88, 0.68, 0.16), 0.6, 0.3)
EMBLEM_RED = material("Sub_EmblemRed", (0.75, 0.05, 0.06), 0.0, 0.4)
EMBLEM_BLUE = material("Sub_EmblemBlue", (0.08, 0.18, 0.55), 0.0, 0.4)


def finish(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    obj = bpy.data.objects.new(name, me)
    scene.collection.objects.link(obj)
    return obj


def box(bm, cx, cy, cz, sx, sy, sz, mat_index=0):
    r = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=r["verts"], vec=(sx, sy, sz))
    bmesh.ops.translate(bm, verts=r["verts"], vec=(cx, cy, cz))
    for f in {f for v in r["verts"] for f in v.link_faces}:
        f.material_index = mat_index
    return r["verts"]


def prism_yz(bm, pts, x0, x1, mat_index=0):
    """A solid with the (y, z) outline `pts` extruded from x0 to x1."""
    a = [bm.verts.new((x0, y, z)) for y, z in pts]
    face = bm.faces.new(a)
    face.material_index = mat_index
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    verts = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=verts, vec=(x1 - x0, 0, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return verts


# ---------------------------------------------------------------- shell
# Side profile (y, z): rear bumper line, tailgate, roof, windshield, hood, front.
SIDE = [(2.86, 0.42), (2.86, 1.40), (2.74, 1.88), (2.50, 1.99), (-0.40, 1.99), (-0.96, 1.88), (-1.22, 1.50), (-2.62, 1.24), (-2.84, 1.12),
        (-2.88, 0.78), (-2.84, 0.46), (-2.2, 0.38)]

bm = bmesh.new()
prism_yz(bm, SIDE, -HALF_W, HALF_W)
bmesh.ops.bevel(bm, geom=bm.edges[:], offset=0.04, segments=2, affect="EDGES")
shell = finish("Sub_Shell", bm, [RED])

# Wheel wells: cylinders across the body, subtracted.
for ay in (AX_F, AX_R):
    for side in (-1, 1):
        bpy.ops.mesh.primitive_cylinder_add(radius=WR + 0.08, depth=0.45, location=(side * (HALF_W - 0.1), ay, WR), rotation=(0, math.radians(90), 0))
        cutter = bpy.context.active_object
        mod = shell.modifiers.new("well", "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.object = cutter
        bpy.context.view_layer.objects.active = shell
        bpy.ops.object.modifier_apply(modifier="well")
        bpy.data.objects.remove(cutter, do_unlink=True)

# ---------------------------------------------------------------- glass
bm = bmesh.new()
for side in (-1, 1):
    x = side * (HALF_W + 0.004)
    # Side windows: front door, second row, rear quarter (slanted at the A pillar).
    for y0, y1 in ((-0.95, -0.15), (-0.08, 0.95), (1.02, 2.18)):
        top = 1.86
        bm.faces.new([bm.verts.new((x, y0 - (0.06 if y0 < -0.5 else 0), 1.42)), bm.verts.new((x, y1, 1.42)), bm.verts.new((x, y1, top)), bm.verts.new((x, y0, top))])
# Windshield and rear glass.
for pts in (
    [(-0.45, 1.97), (-0.98, 1.86), (-1.18, 1.52), (-1.18, 1.52)],
):
    pass
bm.faces.new([bm.verts.new((-0.80, -1.20, 1.52)), bm.verts.new((0.80, -1.20, 1.52)), bm.verts.new((0.80, -0.96, 1.87)), bm.verts.new((-0.80, -0.96, 1.87))])
bm.faces.new([bm.verts.new((-0.80, 2.745, 1.45)), bm.verts.new((-0.80, 2.60, 1.90)), bm.verts.new((0.80, 2.60, 1.90)), bm.verts.new((0.80, 2.745, 1.45))])
for f in bm.faces:
    f.material_index = 0
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
finish("Sub_Glass", bm, [GLASS])

# ---------------------------------------------------------------- trim
bm = bmesh.new()
box(bm, 0, -2.90, 0.50, 2.02, 0.14, 0.22, 0)     # front bumper
box(bm, 0, 2.89, 0.50, 2.00, 0.12, 0.20, 0)      # rear bumper
box(bm, 0, -2.86, 0.86, 1.40, 0.06, 0.30, 0)     # black grille
box(bm, 0, -2.875, 0.99, 1.40, 0.04, 0.03, 1)    # chrome bar across the grille
box(bm, 0, -2.90, 0.50, 1.20, 0.16, 0.12, 1)     # lower plate
# Push bar: a black tubular frame that wraps the headlamps.
box(bm, 0, -3.04, 1.14, 1.90, 0.06, 0.06, 0)
box(bm, 0, -3.04, 0.60, 1.90, 0.06, 0.06, 0)
for bx in (-0.93, -0.62, 0.62, 0.93):
    box(bm, bx, -3.04, 0.87, 0.06, 0.06, 0.60, 0)
for side in (-1, 1):
    box(bm, side * 0.96, -2.95, 0.87, 0.05, 0.22, 0.56, 0)
for side in (-1, 1):
    box(bm, side * (HALF_W + 0.13), -0.98, 1.48, 0.20, 0.16, 0.14, 0)   # mirrors
    box(bm, side * (HALF_W + 0.01), 0.0, 0.45, 0.06, 4.2, 0.12, 0)       # rocker trim
    box(bm, side * (HALF_W + 0.04), 0.35, 1.15, 0.03, 0.14, 0.03, 2)     # door handles
# Roof light bar base and rack.
box(bm, 0, -0.10, 2.06, 1.40, 0.28, 0.07, 0)
box(bm, 0, 0.85, 2.04, 1.70, 0.05, 0.06, 0)
box(bm, 0, 1.55, 2.04, 1.70, 0.05, 0.06, 0)
# White stripe along both sides, under the windows.
for side in (-1, 1):
    # White band from the front wheel arch to the rear quarter, with a red pin line above and below it.
    box(bm, side * (HALF_W + 0.006), 0.35, 1.10, 0.012, 4.5, 0.26, 3)
    box(bm, side * (HALF_W + 0.008), 0.35, 1.245, 0.012, 4.5, 0.025, 4)
    box(bm, side * (HALF_W + 0.008), 0.35, 0.955, 0.012, 4.5, 0.025, 4)
finish("Sub_Trim", bm, [BLACK, CHROME, CHROME, WHITE, PIN])

# ---------------------------------------------------------------- lights
bm = bmesh.new()
# Light bar lenses: red outer, clear inner.
for x, mat in ((-0.62, 0), (-0.22, 1), (0.22, 1), (0.62, 0)):
    box(bm, x, -0.10, 2.13, 0.34, 0.24, 0.10, mat)
# Grille / windshield warning lights.
for x, mat in ((-0.55, 0), (-0.25, 1), (0.25, 1), (0.55, 0)):
    box(bm, x, -2.88, 0.99, 0.18, 0.05, 0.06, mat)
# Headlamps and tail lights.
for side in (-1, 1):
    box(bm, side * 0.72, -2.875, 0.98, 0.36, 0.05, 0.16, 2)
    box(bm, side * 0.86, 2.86, 1.16, 0.10, 0.06, 0.55, 3)
finish("Sub_Lights", bm, [LENS_RED, LENS_CLEAR, HEADLAMP, TAIL])

# ---------------------------------------------------------------- decals (lettering)


def text(name, body, size, location, matrix):
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = body
    curve.size = size
    curve.extrude = 0.004
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    obj = bpy.data.objects.new(name, curve)
    scene.collection.objects.link(obj)
    obj.rotation_euler = matrix.to_euler()
    obj.location = location
    obj.data.materials.append(WHITE)
    return obj


# Left side (+X): reading front to rear is +Y; right side (-X): reading rear to front is -Y.
LEFT = Matrix([[0, 0, 1], [1, 0, 0], [0, 1, 0]])
RIGHT = Matrix([[0, 0, -1], [-1, 0, 0], [0, 1, 0]])
texts = []
for side, rot in ((1, LEFT), (-1, RIGHT)):
    x = side * (HALF_W + 0.011)
    # On the black rear-quarter window: SUPERVISOR 16, as on the real unit (mirrored on the other side).
    texts.append(text("T_Sup", "SUPERVISOR 16", 0.092, (x, 1.62, 1.52), rot))
texts.append(text("T_Roof", "16", 0.7, (0, 0.9, 2.001), Matrix.Identity(3)))
bpy.ops.object.select_all(action="DESELECT")
for t in texts:
    t.select_set(True)
bpy.context.view_layer.objects.active = texts[0]
bpy.ops.object.convert(target="MESH")
bpy.ops.object.join()
bpy.context.active_object.name = "Sub_Decals"

# ---------------------------------------------------------------- HFD shields (front doors) and emblems (rear quarter windows)
def shield(bm, side, cy, cz, w, h, mat_index, lift):
    x = side * (HALF_W + lift)
    pts = [(cy - w / 2, cz + h / 2), (cy + w / 2, cz + h / 2), (cy + w / 2, cz + h * 0.05), (cy, cz - h / 2), (cy - w / 2, cz + h * 0.05)]
    verts = [bm.verts.new((x, y, z)) for y, z in pts]
    face = bm.faces.new(verts if side > 0 else verts[::-1])
    face.material_index = mat_index


def disc(bm, side, cy, cz, r, mat_index, lift):
    x = side * (HALF_W + lift)
    c = bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=20)
    bmesh.ops.rotate(bm, verts=c["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    bmesh.ops.translate(bm, verts=c["verts"], vec=(x, cy, cz))
    for f in {f for v in c["verts"] for f in v.link_faces}:
        f.material_index = mat_index


bm = bmesh.new()
for side in (-1, 1):
    shield(bm, side, -0.55, 1.12, 0.36, 0.46, 0, 0.012)      # gold rim
    shield(bm, side, -0.55, 1.12, 0.30, 0.39, 1, 0.014)      # blue field
    shield(bm, side, -0.55, 1.20, 0.14, 0.12, 2, 0.016)      # white star patch
    disc(bm, side, 1.62, 1.76, 0.115, 3, 0.012)              # emblem: red rim, white ring, blue centre
    disc(bm, side, 1.62, 1.76, 0.085, 2, 0.014)
    disc(bm, side, 1.62, 1.76, 0.055, 4, 0.016)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
finish("Sub_Emblems", bm, [SHIELD_GOLD, SHIELD_BLUE, WHITE, EMBLEM_RED, EMBLEM_BLUE])

# ---------------------------------------------------------------- wheels
for name, ay, sx in (("SubWheelFrontLeft", AX_F, 1), ("SubWheelFrontRight", AX_F, -1), ("SubWheelRearLeft", AX_R, 1), ("SubWheelRearRight", AX_R, -1)):
    bm = bmesh.new()
    tyre = bmesh.ops.create_cone(bm, cap_ends=True, segments=28, radius1=WR, radius2=WR, depth=0.27)
    bmesh.ops.rotate(bm, verts=tyre["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    for f in {f for v in tyre["verts"] for f in v.link_faces}:
        f.material_index = 0
    rim = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=WR * 0.62, radius2=WR * 0.62, depth=0.28)
    bmesh.ops.rotate(bm, verts=rim["verts"], matrix=Matrix.Rotation(math.radians(90), 3, "Y"))
    for f in {f for v in rim["verts"] for f in v.link_faces}:
        f.material_index = 1
    obj = finish(name, bm, [TYRE, CHROME])
    obj.location = (sx * WHEEL_X, ay, WR)

# ---------------------------------------------------------------- save / export
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Supervisor16_Clean.blend"), copy=True)
if "export" in sys.argv:
    names = ["Sub_Shell", "Sub_Glass", "Sub_Trim", "Sub_Lights", "Sub_Decals", "Sub_Emblems", "SubWheelFrontLeft", "SubWheelFrontRight", "SubWheelRearLeft", "SubWheelRearRight"]
    for o in scene.objects:
        o.select_set(o.name in names)
    bpy.context.view_layer.objects.active = scene.objects["Sub_Shell"]
    os.makedirs(UNITY_DIR, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="OFF",
                             use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    print("EXPORTED", FBX)
print("ALL DONE")
