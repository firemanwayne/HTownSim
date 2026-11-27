"""Exports a reusable apparatus (engine or ambulance) for Unity, numbered for one unit. Run headless; the .blend is never saved:

    set HFD_UNIT_NUMBER=16
    blender -b "Engines/spartan_reusable_engine.blend" --python export_reusable_unity.py
    set HFD_UNIT_NUMBER=8
    set HFD_MEDIC_LABEL=1
    blender -b "EMS/frazer_reusable_ambulance.blend" --python export_reusable_unity.py

The apparatus is told from the rig in the file (Spartan_Engine_CONTROLS or Frazer_Ambulance_CONTROLS). HFD_UNIT_NUMBER (0 to 99, default 7)
is the unit's number; HFD_MEDIC_LABEL=1 (ambulance only) letters the unit MEDIC / M instead of AMBULANCE / A. One FBX is written per unit:

    Assets/HTownSim/Vehicles/SpartanEngine/SpartanEngine<NN>.fbx           (SpartanEngine07.fbx, SpartanEngine16.fbx ...)
    Assets/HTownSim/Vehicles/FrazerAmbulance/FrazerAmbulance<NN>.fbx       (FrazerMedic<NN>.fbx with the medic label)
    <name>_materials.json   per material: colour, metallic, roughness, alpha, emission, texture, cutout
    <name>_Paint.png        the livery with this unit's number in it
    *.png                   the door logo and Texas artwork textures

The number is a shader effect (drivers pick a tile of the number atlas), which an FBX cannot carry, so the paint material's final colour is
baked into <name>_Paint.png for this number (an emission bake: the colour before any shading) and the exported paint material uses it.
Every other material is exported as it is. Meshes are exported as the earlier engine and ambulance models were (axis conversion baked in,
front toward Unity's +Z); the wheels keep their own pivots. Material names are cut down to letters, digits and underscores, and the paint
material is named <name>_Paint so each unit's FBX has a material of its own for the Unity install to remap.
"""
import json
import os
import re
from pathlib import Path

import math

import bpy
from mathutils import Matrix

NUMBER = max(0, min(99, int(os.environ.get("HFD_UNIT_NUMBER", "7"))))
MEDIC = os.environ.get("HFD_MEDIC_LABEL", "0") == "1"
REPO = Path(__file__).resolve().parents[2]

KINDS = {
    "Spartan_Engine_CONTROLS": dict(folder="SpartanEngine", prefix="SpartanEngine", shell="Engine07_Shell", paint="Engine07_Paint",
                                    skip=("Cube",)),
    "Frazer_Ambulance_CONTROLS": dict(folder="FrazerAmbulance", prefix="FrazerAmbulance", shell="Amb07_Shell", paint="Amb07_Paint",
                                      skip=()),
}

scene = bpy.context.scene
rig_name = next(name for name in KINDS if name in bpy.data.objects)
kind = KINDS[rig_name]
rig = bpy.data.objects[rig_name]
if MEDIC:
    assert "Medic Label" in rig.keys(), "Only the ambulance has a medic label."

NAME = ("FrazerMedic" if MEDIC else kind["prefix"]) + f"{NUMBER:02d}"
OUT_DIR = REPO / "Assets" / "HTownSim" / "Vehicles" / kind["folder"]
FBX = OUT_DIR / f"{NAME}.fbx"
OUT_DIR.mkdir(parents=True, exist_ok=True)


def safe(name):
    return re.sub(r"[^A-Za-z0-9_]+", "_", name).strip("_")


# 1. The number: set it on the rig and let the drivers that pick the number tiles run.
scene = next(s for s in bpy.data.scenes if rig.name in s.objects)
bpy.context.window.scene = scene
rig["Unit Number"] = NUMBER
if "Medic Label" in rig.keys():
    rig["Medic Label"] = MEDIC
rig.update_tag()
bpy.context.view_layer.update()

shell = bpy.data.objects[kind["shell"]]
paint = bpy.data.materials[kind["paint"]]
assert paint.node_tree.animation_data and paint.node_tree.animation_data.drivers, "The paint material has no number drivers."


def bake_paint():
    """Bakes the paint material's base colour (with the number tiles in it) to <NAME>_Paint.png and points the material at it."""
    nodes, links = paint.node_tree.nodes, paint.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    base = bsdf.inputs["Base Color"]
    assert base.is_linked, "The paint's base colour is not driven by the livery nodes."
    source = base.links[0].from_socket
    output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL" and n.is_active_output)
    base_image = next(n.image for n in nodes if n.type == "TEX_IMAGE" and n.image and "Base_Livery" in n.image.name)
    width, height = base_image.size

    target = bpy.data.images.new(f"{NAME}_Paint", width, height, alpha=False)
    target.colorspace_settings.name = "sRGB"
    image_node = nodes.new("ShaderNodeTexImage")
    image_node.image = target

    emission = nodes.new("ShaderNodeEmission")
    links.new(source, emission.inputs["Color"])
    old_surface = output.inputs["Surface"].links[0].from_socket
    links.new(emission.outputs["Emission"], output.inputs["Surface"])

    # Cycles needs an image to bake into on every material of the object; a small throw-away one serves the others.
    spare = bpy.data.images.new("spare", 4, 4)
    added = []
    for slot in shell.material_slots:
        material = slot.material
        if material is paint:
            continue
        node = material.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = spare
        material.node_tree.nodes.active = node
        added.append((material, node))

    nodes.active = image_node
    for o in bpy.data.objects:
        o.select_set(False)
    shell.select_set(True)
    bpy.context.view_layer.objects.active = shell
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 1
    scene.render.bake.margin = 16
    scene.render.bake.use_clear = True
    bpy.ops.object.bake(type="EMIT")
    target.filepath_raw = str(OUT_DIR / f"{NAME}_Paint.png")
    target.file_format = "PNG"
    target.save()

    # The exported paint material: the baked image as its base colour, the livery nodes' effect already in it.
    links.new(old_surface, output.inputs["Surface"])
    nodes.remove(emission)
    links.new(image_node.outputs["Color"], base)
    for material, node in added:
        material.node_tree.nodes.remove(node)
    bpy.data.images.remove(spare)
    return target


baked = bake_paint()
paint.name = f"{NAME}_Paint"

# 2. Everything that is not the apparatus (cameras, lights, studio props) goes; the meshes are taken out from under the rig.
meshes = [o for o in rig.children_recursive if o.type == "MESH" and o.name not in kind["skip"]]
assert any(o.name == kind["shell"] for o in meshes)
for o in list(scene.objects):
    if o not in meshes:
        bpy.data.objects.remove(o, do_unlink=True)
# The reusable models face the other way from the earlier engine and ambulance exports (which the game's bays, wheels and beacons are
# set up for): turn them half a circle about the vertical axis so a unit arrives in Unity front toward +Z, as before.
turn = Matrix.Rotation(math.radians(180), 4, "Z")
for o in meshes:
    keep = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = turn @ keep
bpy.context.view_layer.update()

# 3. Material names the game can use in a profile.
used = {s.material for o in meshes for s in o.material_slots if s.material}
for m in sorted(used, key=lambda m: m.name):
    m.name = safe(m.name)

# 4. Materials for Unity, and the textures.
mats = {}
for m in used:
    info = dict(color=list(m.diffuse_color)[:3], metallic=0.0, roughness=0.5, alpha=1.0, texture=None, emission=[0, 0, 0], cutout=False)
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
                    if node.image is baked:
                        name = f"{NAME}_Paint.png"
                    else:
                        name = safe(os.path.splitext(bpy.path.basename(node.image.name))[0]) + ".png"
                        img = node.image.copy()
                        img.filepath_raw = str(OUT_DIR / name)
                        img.file_format = "PNG"
                        img.save()
                        info["cutout"] = True
                    info["texture"] = name
                    info["color"] = [1.0, 1.0, 1.0]
    mats[m.name] = info

json.dump(dict(materials=mats, unit_number=NUMBER, medic=MEDIC), open(OUT_DIR / f"{NAME}_materials.json", "w"), indent=1)

# 5. Export the way the earlier engine and ambulance models were: meshes only, axis conversion baked in, one FBX.
for o in bpy.data.objects:
    o.select_set(o in meshes)
bpy.context.view_layer.objects.active = bpy.data.objects[kind["shell"]]
bpy.ops.export_scene.fbx(filepath=str(FBX), use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True, mesh_smooth_type="OFF",
                         use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
print("EXPORTED", FBX, "meshes", len(meshes), "materials", len(mats))
