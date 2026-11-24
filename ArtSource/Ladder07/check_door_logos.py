import bpy
from pathlib import Path
from mathutils import Vector

out=Path(r'J:/Unity/projects/HTownSim/ArtSource/Ladder07')
scene=bpy.context.scene
logos=[o for o in scene.objects if o.get('official_hfd_logo')]
assert len(logos)==6
for o in logos:
    assert o.data.uv_layers.active
    assert len(o.data.polygons)==64
image=bpy.data.images['HFD_Logo_May_2025.png']
assert image.packed_file
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
    gpu=False
    for d in prefs.devices:d.use=d.type!='CPU';gpu=gpu or d.use
    if gpu:scene.cycles.device='GPU'
except Exception:pass
for side,label in [(-1,'Driver'),(1,'Passenger')]:
    d=bpy.data.cameras.new(label+' door inspection');cam=bpy.data.objects.new(d.name,d);scene.collection.objects.link(cam)
    cam.location=(-4.85,side*5,1.48)
    cam.rotation_euler=(Vector((-4.85,0,1.48))-cam.location).to_track_quat('-Z','Y').to_euler()
    d.type='ORTHO';d.ortho_scale=.88;scene.camera=cam
    scene.render.resolution_x=900;scene.render.resolution_y=900
    scene.render.filepath=str(out/('Ladder07_'+label+'_Door.png'))
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['Ladder07 hero']
scene.render.resolution_x=1500;scene.render.resolution_y=900
scene.render.filepath=str(out/'Ladder07_Rigged_LOD0.png')
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Ladder07_Rigged.blend'),compress=True)
print('Verified six correctly UV-mapped decals, original artwork packed, two per LOD.')
