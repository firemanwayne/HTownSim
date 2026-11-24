"""Renders every HTown map output from the scene build_map.py made.

Run inside Blender after build_map.py:  exec(open(r"J:\\Unity\\projects\\HTownSim\\Blender\\HTown\\scripts\\render_all.py").read())

Outputs (renders/):
  htown_map_hud.png       1466x2048, no street trees: for the 350 px HUD map, where trees read as speckle.
  htown_map_detail.png    2932x4096, with street trees: a full-screen / zoomed map.
  htown_hydrants.png      2932x4096, transparent: hydrant dots only, to layer over either map.
  htown_hero.png          1920x1080 three-quarter view at a warm, low dusk sun.
All top-down images cover exactly x -2250..560, z -2350..1575 (the Unity CityMap window), +Z up.
"""
import math
import os

import bpy

try:
    OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "renders")
except NameError:  # exec()'d from Blender's console, where __file__ is not defined
    OUT = r"J:\Unity\projects\HTownSim\Blender\HTown\renders"
os.makedirs(OUT, exist_ok=True)
scene = bpy.context.scene
cols = bpy.data.collections
sun = bpy.data.objects["Sun"]


def show(name, render=True):
    cols[name].hide_render = not render


def tree_objects(visible):
    for obj_name in ("StreetTrees",):
        bpy.data.objects[obj_name].hide_render = not visible


def render(name, camera, size, transparent=False, samples=64):
    scene.camera = bpy.data.objects[camera]
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = transparent
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA" if transparent else "RGB"
    scene.eevee.taa_render_samples = samples
    scene.render.filepath = os.path.join(OUT, name)
    bpy.ops.render.render(write_still=True)
    print("rendered", name)


map_sun = (math.radians(48), 0, math.radians(-135))
sky = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")

# HUD map: everything but street trees and hydrants. Set the map lighting explicitly so a previous hero render
# (or manual tweaking) never leaks into the map images.
sun.rotation_euler = map_sun
sun.data.color = (1.0, 0.76, 0.48)
sun.data.energy = 3.2
sky.inputs["Strength"].default_value = 1.0
for c in ("Ground", "Roads", "Lots", "Buildings", "Detail"):
    show(c)
show("Overlay_Hydrants", False)
tree_objects(False)
render("htown_map_hud.png", "MapTop", (1466, 2048))

# Detail map: street trees on, double resolution.
tree_objects(True)
render("htown_map_detail.png", "MapTop", (2932, 4096))

# Hydrant overlay: only the hydrant collection, transparent background.
for c in ("Ground", "Roads", "Lots", "Buildings", "Detail"):
    show(c, False)
show("Overlay_Hydrants")
render("htown_hydrants.png", "MapTop", (2932, 4096), transparent=True, samples=16)

# Hero: warm low sun from the south-west, as in the HTownSim dusk mood.
for c in ("Ground", "Roads", "Lots", "Buildings", "Detail"):
    show(c)
show("Overlay_Hydrants", False)
sun.rotation_euler = (math.radians(68), 0, math.radians(-60))
sun.data.color = (1.0, 0.72, 0.45)
sun.data.energy = 5.5
sky.inputs["Strength"].default_value = 0.45
render("htown_hero.png", "Hero", (1920, 1080), samples=96)

# Restore the map lighting.
sun.rotation_euler = map_sun
sun.data.color = (1.0, 0.76, 0.48)
sun.data.energy = 3.2
sky.inputs["Strength"].default_value = 1.0
scene.camera = bpy.data.objects["MapTop"]
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(os.path.dirname(OUT), "HTownMap.blend"))
print("saved HTownMap.blend")
