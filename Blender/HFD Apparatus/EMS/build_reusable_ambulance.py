"""Build the reusable ambulance with the original HFD cab-door shields.

Generate the livery with make_livery_amb.py and HFD_REUSABLE_LIVERY_DIR, then run:
blender -b --python-exit-code 1 --python build_reusable_ambulance.py -- GENERATED OUTPUT.blend
"""
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
sys.path.insert(0, str(HERE))

from apparatus_unit_livery import DEFAULT_NUMBER, UNIT_NUMBER, install_numbers
from build_ambulance07 import build


def main():
    generated, output = map(Path, sys.argv[sys.argv.index("--") + 1:])
    root = Path(os.environ.get("HFD_REPO_ROOT", HERE.parents[2]))
    logo = root / "Assets/HTownSim/Vehicles/Ambulance07/HFD_Logo.png"
    if not logo.is_file():
        raise FileNotFoundError(logo)
    build(livery=None, logo=str(logo), write_metadata=False)
    scene = bpy.context.scene
    scene.name = "Frazer Reusable Ambulance"
    rig = bpy.data.objects.new("Frazer_Ambulance_CONTROLS", None)
    scene.collection.objects.link(rig)
    rig[UNIT_NUMBER] = DEFAULT_NUMBER
    rig.id_properties_ui(UNIT_NUMBER).update(
        min=0, max=99, default=DEFAULT_NUMBER, description="Two-digit ambulance unit number (00–99)"
    )
    rig["Medic Label"] = False
    rig.id_properties_ui("Medic Label").update(
        description="Show MEDIC and M instead of AMBULANCE and A"
    )
    install_numbers(scene.objects["Amb07_Shell"], rig, generated.resolve(), base_image="Ambulance_Base_Livery.png")
    for obj in scene.objects:
        if obj != rig:
            obj.parent = rig
        obj.select_set(False)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    notes = bpy.data.texts.new("FRAZER_AMBULANCE_README")
    notes.write(
        "Select Frazer_Ambulance_CONTROLS and open Object Properties > Custom Properties.\n"
        "Set Unit Number (00–99) to update both sides, the front of the module, the rear and the plate.\n"
        "Enable Medic Label to switch both side labels to MEDIC and the front/rear A to M.\n"
        "The original HFD shields are packed with the model and remain on both cab doors.\n"
    )
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                space = area.spaces.active
                space.shading.type = "MATERIAL"
                region = space.region_3d
                region.view_location = (0, 0, 1.35)
                region.view_distance = 10
                region.view_rotation = Vector((1.5, -2, 1.15)).to_track_quat("Z", "Y")
    bpy.ops.file.pack_all()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output.resolve()), compress=True)


if __name__ == "__main__":
    main()
