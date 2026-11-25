"""Export the HPD patrol unit 46837 (HPD_Patrol_46837.blend) as a static scene prop for Unity.

    blender -b --python export_hpd.py

The source scene is a presentation model (about 600 parts, vehicle forward -X, on a studio floor). The export:
  - drops the studio floor, camera and lights, applies every modifier and turns the lettering into mesh;
  - names the lamp materials by what they do, so the game can drive them (the same scheme as the District Chief 08 export):
      HPD_Flash_Red / HPD_Flash_Blue            the lightbar's red and blue LED modules (the game alternates them)
      HPD_Steady_Headlight, HPD_Steady_Fog      burn with the headlights
      HPD_Lamp_TurnL_Front / HPD_Lamp_TurnR_Front   the amber corner lamps; HPD_Lamp_TurnL_Rear / HPD_Lamp_TurnR_Rear the red
                                                wraparound lamps (the blinkers; L is the driver's side)
      HPD_Lamp_Brake  the tail lamps and the high mounted stop lamp;   HPD_Lamp_Reverse  the clear tail inserts
    and gives every other material a file-safe name (HPD_<name>);
  - joins the wheel parts into HPD_WheelFrontLeft/Right and HPD_WheelRearLeft/Right, each with its pivot on the axle (so the game
    can spin and steer them), and everything else into HPD_Body;
  - turns the car to the game's frame (front at -Y, Z up, origin on the ground at the centre of the wheelbase)
    and writes the FBX (-Z forward, Y up, textures embedded) into Assets/HTownSim/Vehicles/HpdPatrol.
The source .blend is not saved.
"""
import math
import os
import re

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "HPD_Patrol_46837.blend")
UNITY_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\HpdPatrol"
FBX = os.path.join(UNITY_DIR, "HpdPatrol.fbx")

bpy.ops.wm.open_mainfile(filepath=SOURCE)
scene = bpy.data.scenes[0]
bpy.context.window.scene = scene

for o in scene.objects:
    o.hide_set(False)
    o.hide_viewport = False
    o.hide_select = False


def is_studio(o):
    return o.type == "MESH" and any(s.material and "Studio" in s.material.name for s in o.material_slots)


objects = [o for o in scene.objects if o.type in ("MESH", "FONT") and not is_studio(o)]

# ------------------------------------------------------------------ materials by role


def safe(name):
    return "HPD_" + re.sub(r"[^A-Za-z0-9]+", "_", name.replace("HPD ", "", 1)).strip("_")


role_cache = {}


def role_material(source, role):
    if role not in role_cache:
        copy = source.copy()
        copy.name = role
        role_cache[role] = copy
    return role_cache[role]


def centre_of(o):
    return o.matrix_world @ (sum((Vector(c) for c in o.bound_box), Vector()) / 8)


def base_name(o):
    return re.sub(r"\.\d+$", "", o.name)


for o in objects:
    side = "L" if centre_of(o).y < 0 else "R"  # the driver's side is -Y in the source
    name = base_name(o)
    for slot in o.material_slots:
        m = slot.material
        if m is None:
            continue
        if m.name == "HPD Red LEDs":
            slot.material = role_material(m, "HPD_Flash_Red")
        elif m.name == "HPD Blue LEDs":
            slot.material = role_material(m, "HPD_Flash_Blue")
        elif name == "Headlamp amber corner":
            slot.material = role_material(m, "HPD_Lamp_Turn%s_Front" % side)
        elif name == "Wraparound brake lamp":
            slot.material = role_material(m, "HPD_Lamp_Turn%s_Rear" % side)
        elif name in ("Rear red tail lamp", "Tail lamp red inner", "High mounted stop lamp"):
            slot.material = role_material(m, "HPD_Lamp_Brake")
        elif name == "Tail lamp clear U insert":
            slot.material = role_material(m, "HPD_Lamp_Reverse")
        elif name == "Headlamp clear cover":
            slot.material = role_material(m, "HPD_Steady_Headlight")
        elif name == "Foglamp optic":
            slot.material = role_material(m, "HPD_Steady_Fog")
for m in bpy.data.materials:
    if m.name.startswith("HPD "):
        m.name = safe(m.name)

# ------------------------------------------------------------------ apply modifiers, lettering to mesh
bpy.ops.object.select_all(action="DESELECT")
for o in objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.object.make_single_user(object=True, obdata=True, material=False)
bpy.ops.object.convert(target="MESH")
meshes = [o for o in scene.objects if o.type == "MESH" and not is_studio(o)]

# One UV layer called UVMap on every mesh, so joining keeps the badge's artwork coordinates.
for o in meshes:
    layers = o.data.uv_layers
    if len(layers) == 0:
        layers.new(name="UVMap")
    else:
        keep = layers[0]
        keep.name = "UVMap"
        for extra in [layer for layer in layers if layer.name != "UVMap"]:
            layers.remove(extra)

# Unparent, keeping world transforms.
for o in meshes:
    world = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = world


def centre(o):
    return o.matrix_world @ (sum((Vector(c) for c in o.bound_box), Vector()) / 8)


tyres = {o.name: o for o in meshes if o.name.startswith("Road tire")}
if len(tyres) != 4:
    raise RuntimeError("expected four 'Road tire' parts, found %d" % len(tyres))
wheel_centres = {name: centre(o) for name, o in tyres.items()}
front_x = min(c.x for c in wheel_centres.values())
rear_x = max(c.x for c in wheel_centres.values())
axle_z = sum(c.z for c in wheel_centres.values()) / 4


def wheel_name(c):
    # Front is -X; the driver's side (left) is -Y.
    return "HPD_Wheel%s%s" % ("Front" if c.x < (front_x + rear_x) / 2 else "Rear", "Left" if c.y < 0 else "Right")


target = {name: wheel_name(c) for name, c in wheel_centres.items()}
WHEEL_WORDS = ("tire", "wheel", "center cap", "lug", "hub", "rim")
wheel_parts = {name: [] for name in target}
body = []
for o in meshes:
    lower = o.name.lower()
    c = centre(o)
    nearest = min(wheel_centres, key=lambda k: (wheel_centres[k] - c).length)
    near = (Vector((c.x, c.z)) - Vector((wheel_centres[nearest].x, wheel_centres[nearest].z))).length < 0.5 and abs(c.y) > 0.7
    if near and any(w in lower for w in WHEEL_WORDS) and "arch" not in lower and "unibody" not in lower and "well" not in lower:
        wheel_parts[nearest].append(o)
    else:
        body.append(o)


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    if len(parts) > 1:
        bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = name
    joined.data.name = name
    return joined


# Game frame: centre of the wheelbase on the ground at the origin, front turned from -X to -Y.
to_game = Matrix.Rotation(math.radians(90), 4, "Z") @ Matrix.Translation(Vector((-(front_x + rear_x) / 2, 0, 0)))

finals = []
for tyre_name, parts in wheel_parts.items():
    w = join(parts, target[tyre_name])
    w.data.transform(w.matrix_world)
    w.matrix_world = Matrix.Identity(4)
    w.data.transform(to_game)
    pivot = to_game @ wheel_centres[tyre_name]
    w.data.transform(Matrix.Translation(-pivot))
    w.location = pivot
    finals.append(w)

b = join(body, "HPD_Body")
b.data.transform(b.matrix_world)
b.matrix_world = Matrix.Identity(4)
b.data.transform(to_game)
finals.append(b)

print("wheelbase", round(rear_x - front_x, 3), "axle height", round(axle_z, 3), "wheel parts", {target[k]: len(v) for k, v in wheel_parts.items()},
      "body tris", sum(len(p.vertices) - 2 for p in b.data.polygons), "body size", tuple(round(v, 2) for v in b.dimensions))
print("materials", sorted({m.name for o in finals for m in o.data.materials if m}))

bpy.ops.object.select_all(action="DESELECT")
for o in finals:
    o.select_set(True)
bpy.context.view_layer.objects.active = b
os.makedirs(UNITY_DIR, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="FACE",
                         use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="COPY", embed_textures=True)
print("EXPORTED", FBX)
