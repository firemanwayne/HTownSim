"""Export HFD District Chief 08 (DistrictChief08/HFD_District_Chief_08.blend) as a game model for Unity.

    blender -b --python export_dc08.py

The source scene is a presentation model (about 1,000 parts, vehicle forward -X, driver's side -Y). The export:
  - applies every modifier and turns the lettering into mesh;
  - renames the lamp materials by what they do, so the game can drive them:
      DC08_Flash_RedA / DC08_Flash_RedB   red emergency LEDs, driver's side / passenger's side (they alternate)
      DC08_Steady_Headlight, DC08_Steady_Fog, DC08_Steady_Command   burn with the headlights
      DC08_Lamp_TurnL / DC08_Lamp_TurnR   mirror indicators and the rear arrow-stick halves (the blinkers)
      DC08_Lamp_Brake, DC08_Lamp_Reverse  tail lamps and reversing LEDs
    and gives every other material a file-safe name (DC08_<name>);
  - joins the wheel parts into DC08_WheelFrontLeft/Right and DC08_WheelRearLeft/Right, each with its pivot on the axle,
    and everything else into DC08_Body;
  - turns the truck to the ambulance's frame (front at -Y, Z up, origin on the ground at the centre of the wheelbase)
    and writes the FBX (-Z forward, Y up, textures embedded) into Assets/HTownSim/Vehicles/DistrictChief08.
The source .blend is not saved.
"""
import math
import os
import re

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE = os.path.join(HERE, "DistrictChief08", "HFD_District_Chief_08.blend")
UNITY_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\DistrictChief08"
FBX = os.path.join(UNITY_DIR, "DistrictChief08.fbx")

WHEEL_WORDS = ("tire", "rim", "hub", "wheel lug", "wheel tire", "wheel ventilation", "steel wheel", "tire center", "rolled rim")

bpy.ops.wm.open_mainfile(filepath=SOURCE)
scene = bpy.data.scenes["HFD District Chief 08"]
bpy.context.window.scene = scene
vehicle = bpy.data.collections["DC08 | Vehicle"]

# Only the vehicle stays visible and selectable.
for o in scene.objects:
    o.hide_set(False)
    o.hide_viewport = False
    o.hide_select = False
objects = [o for o in vehicle.all_objects if o.type in ("MESH", "FONT")]

# ------------------------------------------------------------------ materials by role


def safe(name):
    return "DC08_" + re.sub(r"[^A-Za-z0-9]+", "_", name.replace("DC08 |", "")).strip("_")


role_cache = {}


def role_material(source, role):
    key = role
    if key not in role_cache:
        copy = source.copy()
        copy.name = role
        role_cache[key] = copy
    return role_cache[key]


def centre(o):
    return o.matrix_world @ (sum((Vector(c) for c in o.bound_box), Vector()) / 8)


for o in objects:
    side_left = centre(o).y < 0  # the driver's side is -Y in the source
    for slot in o.material_slots:
        m = slot.material
        if m is None:
            continue
        n = m.name
        if "RED emergency LED" in n:
            slot.material = role_material(m, "DC08_Flash_RedA" if side_left else "DC08_Flash_RedB")
        elif "WHITE headlamp" in n:
            slot.material = role_material(m, "DC08_Steady_Headlight")
        elif "WHITE fog LED" in n:
            slot.material = role_material(m, "DC08_Steady_Fog")
        elif "GREEN command beacon" in n:
            slot.material = role_material(m, "DC08_Steady_Command")
        elif "AMBER mirror indicator" in n:
            slot.material = role_material(m, "DC08_Lamp_TurnL" if side_left else "DC08_Lamp_TurnR")
        elif "AMBER arrow module" in n:
            number = int(re.search(r"(\d+)$", n).group(1))
            slot.material = role_material(m, "DC08_Lamp_TurnL" if number <= 3 else "DC08_Lamp_TurnR")
        elif "Tail lamp red lens" in n:
            slot.material = role_material(m, "DC08_Lamp_Brake")
        elif "WHITE reversing LED" in n:
            slot.material = role_material(m, "DC08_Lamp_Reverse")
for m in bpy.data.materials:
    if m.name.startswith("DC08 |") or "|" in m.name or m.name.startswith("DC08 "):
        m.name = safe(m.name)

# ------------------------------------------------------------------ apply modifiers, lettering to mesh
bpy.ops.object.select_all(action="DESELECT")
for o in objects:
    o.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.object.make_single_user(object=True, obdata=True, material=False)
bpy.ops.object.convert(target="MESH")
meshes = [o for o in vehicle.all_objects if o.type == "MESH"]

# One UV layer called UVMap on every mesh, so joining keeps the door shield's and the crests' artwork coordinates (join matches layers
# by name and drops the rest).
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

# ------------------------------------------------------------------ wheels
tyres = {name: bpy.data.objects[name] for name in ("Front Driver tire", "Front Passenger tire", "Rear Driver tire", "Rear Passenger tire")}
wheel_centres = {name: centre(o) for name, o in tyres.items()}
front_x = (wheel_centres["Front Driver tire"].x + wheel_centres["Front Passenger tire"].x) / 2
rear_x = (wheel_centres["Rear Driver tire"].x + wheel_centres["Rear Passenger tire"].x) / 2
axle_z = sum(c.z for c in wheel_centres.values()) / 4
target = {"Front Driver tire": "DC08_WheelFrontLeft", "Front Passenger tire": "DC08_WheelFrontRight",
          "Rear Driver tire": "DC08_WheelRearLeft", "Rear Passenger tire": "DC08_WheelRearRight"}
wheel_parts = {name: [] for name in target}
body = []
for o in meshes:
    lower = o.name.lower()
    c = centre(o)
    nearest = min(wheel_centres, key=lambda k: (wheel_centres[k] - c).length)
    near = (Vector((c.x, c.z)) - Vector((wheel_centres[nearest].x, wheel_centres[nearest].z))).length < 0.62 and abs(c.y) > 0.7
    if near and any(w in lower for w in WHEEL_WORDS) and "shadow" not in lower:
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

b = join(body, "DC08_Body")
b.data.transform(b.matrix_world)
b.matrix_world = Matrix.Identity(4)
b.data.transform(to_game)
finals.append(b)

print("wheelbase", round(front_x - rear_x, 3), "axle height", round(axle_z, 3), "wheel parts", {k: len(v) for k, v in wheel_parts.items()}, "body tris",
      sum(len(p.vertices) - 2 for p in b.data.polygons))
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
