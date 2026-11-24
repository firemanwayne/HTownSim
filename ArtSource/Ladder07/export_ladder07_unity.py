"""Exports Ladder 07 for Unity (run headless; the .blend is opened read-only and never saved):

    blender -b Ladder07_Rigged.blend --python export_ladder07_unity.py

- Stowed pose (all rig controls at 0), all three detail levels: every L07_LOD0/1/2 mesh keeps its _LODn suffix so the
  Unity importer builds a LODGroup under each pivot.
- The rig's pivot empties are kept as the meshes' parents (CTRL_Turntable, CTRL_Aerial_Elevation, CTRL_Aerial_Fly_1-3,
  CTRL_Outrigger_*, axles, wheel rolls, Hydraulic_base_* / Hydraulic_lift_anchor_*), with drivers and constraints
  baked to their stowed values, so the game's AerialController can move them. Cameras, lights and the floor are dropped.
- The rig root is turned +90 degrees about Z so the front points -Y (the convention the FBX settings map to Unity +Z),
  and moved so the model is centred on its length and width with the tyres on z = 0.
- Writes Assets/HTownSim/Vehicles/Ladder07/Ladder07.fbx, the door logo texture, and Ladder07_materials.json
  (per material: base colour, metallic, roughness, alpha, texture) for the Unity installer, plus the model bounds.
"""
import json
import math
import os

import bpy
from mathutils import Matrix, Vector

OUT_DIR = r"J:\Unity\projects\HTownSim\Assets\HTownSim\Vehicles\Ladder07"
FBX = os.path.join(OUT_DIR, "Ladder07.fbx")
os.makedirs(OUT_DIR, exist_ok=True)

scene = bpy.context.scene
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

# Keep the rig: the pivot empties (turntable, elevation, three fly sections, outriggers, axles, wheel rolls, hydraulic
# bases and anchors) stay as the meshes' parents, so the game can move them. Bake the stowed pose into plain transforms:
# every driver and constraint (the hydraulic damped tracks) is replaced by its evaluated value.
root = bpy.data.objects["Ladder07_CONTROLS"]
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

# Remove everything that is not the rig (cameras, lights, studio floor).
for o in list(scene.objects):
    if o not in rig:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()

# Turn so the front points -Y (the FBX settings map that to Unity +Z), by turning the rig root.
turn = Matrix.Rotation(math.radians(90), 4, "Z")
root.matrix_world = turn @ root.matrix_world
bpy.context.view_layer.update()

# Centre on length and width, tyres on the ground (bounds from the most detailed level).
def bounds(objs):
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for o in objs:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            lo = Vector(map(min, lo, w))
            hi = Vector(map(max, hi, w))
    return lo, hi

lod0 = [o for o in lod_meshes if o.name.endswith("_LOD0")]
lo, hi = bounds(lod0)
shift = Vector((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))
root.matrix_world = Matrix.Translation(shift) @ root.matrix_world
bpy.context.view_layer.update()

# Unity folds a single top node into the prefab root (and the scene then overrides its transform), so put the turned,
# centred rig root under a plain identity top node.
top = bpy.data.objects.new("Ladder07_Model", None)
scene.collection.objects.link(top)
keep = root.matrix_world.copy()
root.parent = top
root.matrix_world = keep
rig.insert(0, top)
bpy.context.view_layer.update()
lo, hi = bounds(lod0)

# Materials for Unity, and the logo texture.
mats = {}
for m in {s.material for o in lod_meshes for s in o.material_slots if s.material}:
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
                    name = os.path.splitext(bpy.path.basename(node.image.name))[0] + ".png"
                    node.image.save(filepath=os.path.join(OUT_DIR, name)) if node.image.packed_file is None else None
                    if node.image.packed_file is not None:
                        img = node.image.copy()
                        img.filepath_raw = os.path.join(OUT_DIR, name)
                        img.file_format = "PNG"
                        img.save()
                    info["texture"] = name
                    info["color"] = [1.0, 1.0, 1.0]
                    info["cutout"] = node.outputs["Alpha"].is_linked or True
    mats[m.name] = info

json.dump(dict(materials=mats, bounds=[list(lo), list(hi)],
               lod_triangles={n: sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in lod_meshes if o.name.endswith(n))
                              for n in ("_LOD0", "_LOD1", "_LOD2")}),
          open(os.path.join(OUT_DIR, "Ladder07_materials.json"), "w"), indent=1)

# With the pivot hierarchy, Unity (bake axis conversion on) reads these FBX coordinates as (x, y, -z) without turning
# Blender's Z-up into Y-up. Pre-rotate the rig root (x, y, z) -> (-x, z, y), so the truck arrives upright in Unity with
# its front at +Z and the same right-hand side as before. (The bounds in the JSON above stay in Blender's Z-up frame.)
unity_frame = Matrix(((-1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
keep = root.matrix_world.copy()
root.matrix_world = unity_frame @ keep
bpy.context.view_layer.update()

for o in scene.objects:
    o.select_set(o in rig)
bpy.context.view_layer.objects.active = root
bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={"MESH", "EMPTY"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="FACE",
                         use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP",
                         embed_textures=False)
print("EXPORTED", FBX, "meshes", len(lod_meshes), "bounds", [round(v, 3) for v in lo], [round(v, 3) for v in hi])
