"""Build the canonical reusable engine from the clean engine source.

Prepare the textures with make_livery.py and HFD_REUSABLE_LIVERY_DIR, then run:
blender -b --factory-startup --python-exit-code 1 --python build_reusable_engine.py -- GENERATED OUTPUT.blend
"""
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from apparatus_unit_livery import DEFAULT_NUMBER, UNIT_NUMBER, install_numbers
from build_engine07 import DOORS, build, build_decals

def main():
    generated, output = map(Path, sys.argv[sys.argv.index("--") + 1:])
    root = Path(os.environ.get("HFD_REPO_ROOT", Path(__file__).resolve().parents[3]))
    asset_dir = root / "Assets" / "HTownSim" / "Vehicles" / "Engine07Hi3D"
    scene = build(asset_dir=asset_dir, write_metadata=False)
    scene.name = "Spartan Reusable Engine"
    # Remove Blender's empty factory scene to keep the reusable engine as the only scene.
    for other in list(bpy.data.scenes):
        if other != scene and not other.objects:
            bpy.data.scenes.remove(other)
    rig = bpy.data.objects.new("Spartan_Engine_CONTROLS", None)
    scene.collection.objects.link(rig)
    rig[UNIT_NUMBER] = DEFAULT_NUMBER
    rig.id_properties_ui(UNIT_NUMBER).update(
        min=0, max=99, default=DEFAULT_NUMBER, description="Two-digit number used on all engine markings (00–99)"
    )
    shell = scene.objects["Engine07_Shell"]
    install_numbers(shell, rig, generated.resolve(), base_image="Engine_Base_Livery.png", manifest="engine_unit_markings.json")
    y0, y1, *_ = DOORS[1]
    logo = build_decals(
        scene, asset_dir, decals=(("Station Logo | Both Crew Doors", "HFD_Logo.png", (y0 + y1) / 2, 1.48, .34),),
        name="Station Logo | Rear Crew Doors",
    )
    texture = next(node for node in logo.active_material.node_tree.nodes if node.type == "TEX_IMAGE")
    texture.name = "Station Logo Image"
    texture.label = "Replace this image to update both rear crew doors"
    logo.active_material.node_tree.nodes.active = texture
    for obj in scene.objects:
        if obj != rig:
            obj.parent = rig
        obj.select_set(False)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    notes = bpy.data.texts.new("SPARTAN_ENGINE_README")
    notes.write(
        "Select Spartan_Engine_CONTROLS and open Object Properties > Custom Properties.\n"
        "Set Unit Number (00–99) to update both sides, both bumper ends, roof, rear door and license plate.\n"
        "To change the rear crew-door logo on both sides, select Station Logo | Rear Crew Doors,\n"
        "then replace Station Logo Image in the Shader Editor. Pack the new image before sharing.\n"
    )
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                space = area.spaces.active
                space.shading.type = "MATERIAL"
                region = space.region_3d
                region.view_location = (0, 0, 1.35)
                region.view_distance = 13
                region.view_rotation = Vector((1.5, -2, 1.15)).to_track_quat("Z", "Y")
    bpy.ops.file.pack_all()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output.resolve()), compress=True)


if __name__ == "__main__":
    main()
