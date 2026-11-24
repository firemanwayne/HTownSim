import bpy
from pathlib import Path
from mathutils import Vector

out=Path(r'J:/Unity/projects/HTownSim/ArtSource/Ladder07')
scene=bpy.data.scenes['Houston Ladder 07']
bpy.context.window.scene=scene
scene.render.engine='CYCLES'
scene.cycles.samples=24
scene.cycles.use_denoising=True
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type='OPTIX'
    prefs.get_devices()
    gpu=False
    for d in prefs.devices:
        d.use=d.type!='CPU'
        gpu=gpu or d.use
    if gpu:scene.cycles.device='GPU'
except Exception as ex:
    print('CPU render fallback:',ex)
bpy.context.view_layer.update()
corners=[o.matrix_world@Vector(c) for o in scene.objects if o.type=='MESH' and o.parent and o.parent.name=='Ladder07' for c in o.bound_box]
print('Apparatus bounds:',[(min(v[i] for v in corners),max(v[i] for v in corners)) for i in range(3)])
print('Mesh objects:',sum(o.type=='MESH' for o in scene.objects))
print('Base triangles:',sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH'))
for cam,file,w,h in [('Ladder07 hero','Ladder07_Hero.png',1600,950),('Ladder07 left','Ladder07_Left.png',1800,650),('Ladder07 right','Ladder07_Right.png',1800,650),('Ladder07 front','Ladder07_Front.png',900,900),('Ladder07 rear','Ladder07_Rear.png',900,900)]:
    scene.camera=bpy.data.objects[cam]
    scene.render.resolution_x=w
    scene.render.resolution_y=h
    scene.render.resolution_percentage=100
    scene.render.filepath=str(out/file)
    bpy.ops.render.render(write_still=True)
scene.camera=bpy.data.objects['Ladder07 hero']
scene.render.resolution_x=1600
scene.render.resolution_y=950
for other in list(bpy.data.scenes):
    if other != scene:
        bpy.data.scenes.remove(other)
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(out/'Ladder07.blend'),compress=True)
