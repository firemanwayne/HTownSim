import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

out=Path(r'J:/Unity/projects/HTownSim/ArtSource/Ladder07')
scene=bpy.context.scene
rig=bpy.data.objects['Ladder07_CONTROLS']
def update(**values):
    for k,v in values.items():rig[k]=v
    rig.update_tag();bpy.context.view_layer.update()

# Verify actual meshes move with the controls, not just empty pivots.
tip=bpy.data.objects['L07_aerial_3_LOD0']
wheel=bpy.data.objects['L07_wheel_0_-1_LOD0']
rest_tip=tip.matrix_world.copy()
rest_wheel=wheel.matrix_world.copy()
update(Aerial_elevation_deg=55.,Aerial_extension=1.,Stabilizers=1.)
assert (tip.matrix_world.translation-rest_tip.translation).length>15
update(Steering_deg=25.,Wheel_travel_m=1.)
assert (wheel.matrix_world.to_quaternion().rotation_difference(rest_wheel.to_quaternion())).angle>.1
left=bpy.data.objects['CTRL_Axle0_L'].rotation_euler.z
right=bpy.data.objects['CTRL_Axle0_R'].rotation_euler.z
assert abs(left-right)>.01,'Ackermann steering angles should differ'
for level in range(3):
    update(LOD=level)
    visible=[o for o in scene.objects if o.type=='MESH' and o.name.startswith(('L07_','HFD_')) and not o.hide_render]
    assert len(visible)==30,(level,len(visible))
    assert all(o.name.endswith('LOD%s'%level) for o in visible)
invalid=[o.name for o in scene.objects if o.animation_data for f in o.animation_data.drivers if not f.driver.is_valid]
assert not invalid,invalid
rig.animation_data.action=bpy.data.actions['L07_Aerial_Deploy']
scene.frame_set(180);bpy.context.view_layer.update()
assert abs(rig['Aerial_extension']-1)<.001
scene.frame_set(1);rig.animation_data.action=None
update(Steering_deg=0.,Wheel_travel_m=0.,Aerial_elevation_deg=0.,Aerial_extension=0.,Aerial_azimuth_deg=0.,Stabilizers=0.,LOD=0)
scene.frame_set(1)

scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
    gpu=False
    for d in prefs.devices:d.use=d.type!='CPU';gpu=gpu or d.use
    if gpu:scene.cycles.device='GPU'
except Exception:pass
scene.render.resolution_x=1500;scene.render.resolution_y=900
scene.camera=bpy.data.objects['Ladder07 hero']
for level in [0,2]:
    update(LOD=level)
    scene.render.filepath=str(out/('Ladder07_Rigged_LOD%s.png'%level))
    bpy.ops.render.render(write_still=True)

update(LOD=0,Stabilizers=1.,Aerial_elevation_deg=55.,Aerial_extension=1.,Aerial_azimuth_deg=20.)
d=bpy.data.cameras.new('Deployed inspection');cam=bpy.data.objects.new('Deployed inspection',d);scene.collection.objects.link(cam)
cam.location=(-35,-48,25);target=Vector((-5,-3,15));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
d.type='ORTHO';d.ortho_scale=42;scene.camera=cam
scene.render.resolution_x=1100;scene.render.resolution_y=1300
scene.render.filepath=str(out/'Ladder07_Deployed.png')
bpy.ops.render.render(write_still=True)

update(Stabilizers=0.,Aerial_elevation_deg=0.,Aerial_extension=0.,Aerial_azimuth_deg=0.,LOD=0)
scene.camera=bpy.data.objects['Ladder07 hero'];scene.render.resolution_x=1500;scene.render.resolution_y=900
scene.frame_set(1)
rig.select_set(True);bpy.context.view_layer.objects.active=rig
result={'actual_mesh_motion':'passed','ackermann_steering':'passed','lod_visibility':'30 meshes per level, passed','driver_errors':invalid,'deployment_action':'passed'}
out.joinpath('rig_validation.json').write_text(json.dumps(result,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Ladder07_Rigged.blend'),compress=True)
print(json.dumps(result))
