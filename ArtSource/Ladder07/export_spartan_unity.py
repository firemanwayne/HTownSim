"""Exports the Spartan rear-mount ladder truck for Unity, numbered for one unit (run headless; the .blend is never saved):

    set HFD_UNIT_NUMBER=16
    blender -b spartan_rearmount_laddertruck.blend --python export_spartan_unity.py

HFD_UNIT_NUMBER (0 to 99, default 7) is the truck's number. The rig's "Unit Number" property drives the unit markings (geometry
nodes); this sets it, turns the markings into plain meshes, and exports one FBX per number, so every ladder is the same asset:

    Assets/HTownSim/Vehicles/SpartanLadder/SpartanLadder<NN>.fbx      (SpartanLadder07.fbx, SpartanLadder16.fbx ...)
    Assets/HTownSim/Vehicles/SpartanLadder/SpartanLadder_materials.json   (per material: colour, metallic, roughness, texture)
    Assets/HTownSim/Vehicles/SpartanLadder/*.png                          (the door logo and Texas artwork textures)

What the export does, as the Ladder 07 export (export_ladder07_unity.py) did:
- Stowed pose, all three detail levels; every L07_*_LOD0/1/2 mesh keeps its _LODn suffix so Unity builds a LODGroup.
- The rig's pivot empties (CTRL_Turntable, CTRL_Aerial_Elevation, CTRL_Aerial_Fly_1-3, outriggers, axles, wheel rolls, hydraulic
  bases and anchors) stay as parents, with drivers and constraints baked to their stowed values, so AerialController can move them.
- The rig root is turned so the front points -Y and the model is centred with the tyres on z = 0.

What is new for this asset:
- The two front roof lightbars (each a hundred-odd small parts) are joined into Spartan_Lightbar_Driver and Spartan_Lightbar_Officer,
  one mesh each. Their red and white LED panels get a material of their own per bar (Spartan_LB_Red_Driver, Spartan_LB_Red_Officer,
  Spartan_LB_White_Driver, Spartan_LB_White_Officer), so the game can flash the two bars in turn.
- Every material name is cut down to letters, digits and underscores (L07_Red lenses -> L07_Red_lenses), so the game's profile can
  name lenses without spaces or bars.
"""
import json
import math
import os
import re
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

NUMBER = max(0, min(99, int(os.environ.get("HFD_UNIT_NUMBER", "7"))))
REPO = Path(__file__).resolve().parents[2]
OUT_DIR = REPO / "Assets" / "HTownSim" / "Vehicles" / "SpartanLadder"
NAME = f"SpartanLadder{NUMBER:02d}"
FBX = OUT_DIR / f"{NAME}.fbx"
OUT_DIR.mkdir(parents=True, exist_ok=True)

RIG_ROOT = "Spartan_RearMount_CONTROLS"
UNIT_PROPERTY = "Unit Number"
BAR_SIDES = ("Driver", "Officer")

scene = bpy.context.scene
root = bpy.data.objects[RIG_ROOT]


def safe(name):
    return re.sub(r"[^A-Za-z0-9_]+", "_", name).strip("_")


# 1. The number. The markings are geometry-node meshes driven by the rig's Unit Number property; the rig has to be tagged for the
# node group's driver to see the new value.
root[UNIT_PROPERTY] = NUMBER
root.update_tag()
bpy.context.view_layer.update()

lod_meshes = [o for c in ("L07_LOD0", "L07_LOD1", "L07_LOD2") for o in bpy.data.collections[c].objects if o.type == "MESH"]

# Drivers switch LOD visibility; drop them so every level is exported.
for o in lod_meshes:
    if o.animation_data:
        for fc in list(o.animation_data.drivers):
            if fc.data_path.startswith("hide"):
                o.animation_data.drivers.remove(fc)
    o.hide_viewport = False
    o.hide_render = False
    o.hide_set(False)
bpy.context.view_layer.update()

# 2. Turn the unit markings into plain meshes before the glyph library they instance is removed with the other non-rig objects.
graph = bpy.context.evaluated_depsgraph_get()
markings = [o for o in bpy.data.objects if o.name.startswith("Unit marking")]
for o in markings:
    mesh = bpy.data.meshes.new_from_object(o.evaluated_get(graph), depsgraph=graph)
    assert len(mesh.polygons) > 0, f"{o.name} has no geometry for unit {NUMBER}"

    # The realized marking must be exactly the glyph the node group picks for this number (the Unit number node group instances
    # option NUMBER of its glyph collection, in name order), not whatever number the node last held.
    group = o.modifiers[0].node_group
    options = next(n.inputs["Collection"].default_value for n in group.nodes if n.bl_idname == "GeometryNodeCollectionInfo")
    expected = sorted(options.objects, key=lambda option: option.name)[NUMBER].data
    got = sum((v.co for v in mesh.vertices), Vector((0, 0, 0)))
    want = sum((v.co for v in expected.vertices), Vector((0, 0, 0)))
    assert len(mesh.vertices) == len(expected.vertices) and (got - want).length < 1e-3, (
        f"{o.name}: unit {NUMBER} did not reach the number geometry ({len(mesh.vertices)} vertices, wanted {len(expected.vertices)})")

    o.modifiers.clear()
    o.data = mesh
print(f"Unit {NUMBER:02d}: {len(markings)} markings realized, {sum(len(o.data.polygons) for o in markings)} polygons")

# 3. Lightbars: a material of its own for each bar's red and white LED panels, then one mesh per bar.
copies = {}


def bar_material(source, kind, side):
    key = (kind, side)
    if key not in copies:
        copies[key] = source.copy()
        copies[key].name = f"Spartan_LB_{kind}_{side}"
    return copies[key]


for side in BAR_SIDES:
    parts = [o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith(f"{side} lightbar |")]
    assert parts, f"No {side} lightbar parts"
    for o in parts:
        for slot in o.material_slots:
            if slot.material is None:
                continue
            if slot.material.name == "Front lightbar | Red LED panel":
                slot.material = bar_material(slot.material, "Red", side)
            elif slot.material.name == "Front lightbar | White LED panel":
                slot.material = bar_material(slot.material, "White", side)
    with bpy.context.temp_override(active_object=parts[0], selected_editable_objects=parts, selected_objects=parts):
        bpy.ops.object.join()
    parts[0].name = f"Spartan_Lightbar_{side}"
    parts[0].data.name = f"Spartan_Lightbar_{side}"
bpy.context.view_layer.update()
assert len(copies) == 4, sorted(copies)

# 4. Keep the rig: bake the stowed pose into plain transforms (every driver and constraint replaced by its evaluated value).
rig = [root] + [o for o in root.children_recursive]
depsgraph = bpy.context.evaluated_depsgraph_get()
evaluated = {o.name: o.evaluated_get(depsgraph).matrix_world.copy() for o in rig}
for o in rig:
    if o.animation_data:
        for fc in list(o.animation_data.drivers):
            o.animation_data.drivers.remove(fc)
    for c in list(o.constraints):
        o.constraints.remove(c)
for o in sorted(rig, key=lambda o: len(o.children_recursive), reverse=True):   # parents before children
    o.matrix_world = evaluated[o.name]
bpy.context.view_layer.update()

# Remove everything that is not the rig (cameras, lights, studio floor, the glyph library, the lightbar wiring curves).
for o in list(scene.objects):
    if o not in rig:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()
rig = [o for o in rig if o.name in bpy.data.objects]

# 5. Material names the game can use in a profile.
used = {s.material for o in rig if o.type == "MESH" for s in o.material_slots if s.material}
for m in sorted(used, key=lambda m: m.name):
    m.name = safe(m.name)

# 6. Turn so the front points -Y, centre on length and width, tyres on the ground (bounds from the most detailed level).
turn = Matrix.Rotation(math.radians(90), 4, "Z")
root.matrix_world = turn @ root.matrix_world
bpy.context.view_layer.update()


def bounds(objs):
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi


lod0 = [o for o in rig if o.type == "MESH" and o.name.endswith("_LOD0")]
lo, hi = bounds(lod0)
shift = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
root.matrix_world = Matrix.Translation(shift) @ root.matrix_world
bpy.context.view_layer.update()

# Unity folds a single top node into the prefab root, so put the turned, centred rig root under a plain identity top node.
top = bpy.data.objects.new(f"{NAME}_Model", None)
scene.collection.objects.link(top)
keep = root.matrix_world.copy()
root.parent = top
root.matrix_world = keep
rig.insert(0, top)
bpy.context.view_layer.update()
lo, hi = bounds(lod0)

# 7. Materials for Unity, and the textures.
mats = {}
for m in {s.material for o in rig if o.type == "MESH" for s in o.material_slots if s.material}:
    info = dict(color=list(m.diffuse_color)[:3], metallic=0.0, roughness=0.5, alpha=1.0, texture=None, emission=[0, 0, 0])
    if m.use_nodes:
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            base = bsdf.inputs["Base Color"]
            info["color"] = list(base.default_value)[:3]
            info["metallic"] = bsdf.inputs["Metallic"].default_value
            info["roughness"] = bsdf.inputs["Roughness"].default_value
            info["alpha"] = bsdf.inputs["Alpha"].default_value
            em = bsdf.inputs.get("Emission Color")
            strength = bsdf.inputs.get("Emission Strength")
            if em is not None and strength is not None and strength.default_value > 0:
                info["emission"] = [c * strength.default_value for c in list(em.default_value)[:3]]
            if base.is_linked:
                node = base.links[0].from_node
                if node.type == "TEX_IMAGE" and node.image:
                    name = safe(os.path.splitext(bpy.path.basename(node.image.name))[0]) + ".png"
                    target = str(OUT_DIR / name)
                    img = node.image.copy()
                    img.filepath_raw = target
                    img.file_format = "PNG"
                    img.save()
                    info["texture"] = name
                    info["color"] = [1.0, 1.0, 1.0]
                    info["cutout"] = True
    mats[m.name] = info

json.dump(dict(materials=mats, bounds=[list(lo), list(hi)], unit_number=NUMBER),
          open(OUT_DIR / "SpartanLadder_materials.json", "w"), indent=1)

# 8. With the pivot hierarchy, Unity (bake axis conversion on) reads FBX coordinates as (x, y, -z) without turning Blender's Z-up into
# Y-up. Pre-rotate the rig root (x, y, z) -> (-x, z, y), so the truck arrives upright in Unity with its front at +Z.
unity_frame = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
keep = root.matrix_world.copy()
root.matrix_world = unity_frame @ keep
bpy.context.view_layer.update()

for o in scene.objects:
    o.select_set(o in rig)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(filepath=str(FBX), use_selection=True, object_types={"MESH", "EMPTY"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="FACE",
                         use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP",
                         embed_textures=False)
print("EXPORTED", FBX, "meshes", sum(1 for o in rig if o.type == "MESH"), "materials", len(mats),
      "bounds", [round(v, 3) for v in lo], [round(v, 3) for v in hi])
