"""Build an editable mechanical rig and consolidated rendering LODs.

Run with Ladder07_Tagged_Source.blend loaded in background Blender.
"""
import bpy
import math
import json
import bmesh
from pathlib import Path
from mathutils import Vector, Matrix

OUT=Path(r'J:/Unity/projects/HTownSim/ArtSource/Ladder07')
scene=bpy.data.scenes['Houston Ladder 07']
bpy.context.window.scene=scene
scene.name='Ladder07 Rigged'
source=[o for o in scene.objects if o.parent and o.parent.name=='Ladder07']
assert all('rig_part' in o for o in source), 'Use the tagged source library produced by the current builder.'
oldroot=bpy.data.objects['Ladder07']
source_counts={'objects':len(source),'triangles':0}
rig_collection=bpy.data.collections.new('L07_Rig_Controls')
scene.collection.children.link(rig_collection)
lods=[]
for i in range(3):
    c=bpy.data.collections.new('L07_LOD%s'%i)
    scene.collection.children.link(c)
    lods.append(c)

def empty(name,parent=None,location=(0,0,0)):
    o=bpy.data.objects.new(name,None)
    rig_collection.objects.link(o)
    o.empty_display_type='PLAIN_AXES'
    o.empty_display_size=.35
    o.parent=parent
    o.location=location
    return o

rig=empty('Ladder07_CONTROLS')
rig.empty_display_type='CUBE'
rig.empty_display_size=.4
controls={
    'Steering_deg':(0.,-35.,35.,'Front axle Ackermann steering angle in degrees.'),
    'Wheel_travel_m':(0.,-10000.,10000.,'Wheel rolling distance; 0.555 m rolling radius. Vehicle root remains in place.'),
    'Aerial_azimuth_deg':(0.,-180.,180.,'Rear turntable rotation. Raise above the cab before rotating.'),
    'Aerial_elevation_deg':(0.,0.,80.,'Elevation above the transport position.'),
    'Aerial_extension':(0.,0.,1.,'Three nested fly stages, each extending 7.5 metres at full extension.'),
    'Stabilizers':(0.,0.,1.,'Extends four outriggers 1.15 m and lowers feet to ground contact.'),
    'LOD':(0,0,2,'0: presentation; 1: medium distance; 2: distant. Only one level is visible.')}
for key,(default,lo,hi,description) in controls.items():
    rig[key]=default
    rig.id_properties_ui(key).update(min=lo,max=hi,soft_min=lo,soft_max=hi,description=description)
rig['Instructions']='Use Custom Properties. Deploy stabilizers, elevate, then rotate/extend. Rigid mechanical rig; no deformation skin. LOD controls visibility. Aerial_Deploy and Wheels_Steering actions are included.'

def driver(o,path,index,prop,expression):
    f=o.driver_add(path) if index is None else o.driver_add(path,index)
    d=f.driver;d.type='SCRIPTED'
    v=d.variables.new();v.name='v';v.type='SINGLE_PROP'
    v.targets[0].id=rig;v.targets[0].data_path='["%s"]'%prop
    d.expression=expression
    return f

parts={'chassis':rig}
turret=empty('CTRL_Turntable',rig,(4.68,0,2.51))
driver(turret,'rotation_euler',2,'Aerial_azimuth_deg','v*pi/180')
elevation=empty('CTRL_Aerial_Elevation',turret,(-.44,0,.65))
driver(elevation,'rotation_euler',1,'Aerial_elevation_deg','v*pi/180')
parts['turret']=turret;parts['aerial_0']=elevation
last=elevation
for i in range(1,4):
    last=empty('CTRL_Aerial_Fly_%s'%i,last)
    driver(last,'location',0,'Aerial_extension','-7.5*v')
    parts['aerial_%s'%i]=last
axles=[-3.7973,1.7651,3.1371]
for ai,x in enumerate(axles):
    for side in [-1,1]:
        pivot=empty('CTRL_Axle%s_%s'%(ai,'L' if side<0 else 'R'),rig,(x,side*1.075,.555))
        if ai==0:
            driver(pivot,'rotation_euler',2,'Steering_deg','atan(6.2484*tan(v*pi/180)/(6.2484-%s*1.075*tan(v*pi/180)))'%side)
        roll=empty('CTRL_WheelRoll_%s_%s'%(ai,side),pivot)
        driver(roll,'rotation_euler',1,'Wheel_travel_m','-v/0.555')
        parts['wheel_%s_%s'%(ai,side)]=roll
for x in [-1.65,4.56]:
    for side in [-1,1]:
        o=empty('CTRL_Outrigger_%s_%s'%(x,side),rig,(x,side*1.13,0))
        driver(o,'location',1,'Stabilizers','%s*(1.13+1.15*v)'%side)
        driver(o,'location',2,'Stabilizers','-0.2475*v')
        parts['outrigger_%s_%s'%(x,side)]=o
bpy.context.view_layer.update()
deps=bpy.context.evaluated_depsgraph_get()
buckets={}

# Bake small edge radii once, rather than retaining thousands of live modifiers.
for o in source:
    if o.get('official_hfd_logo'):continue
    if o.type not in {'MESH','FONT','CURVE'}:continue
    if o.name.startswith(('Elevation cylinder','Hydraulic piston')):continue
    key=o.get('rig_part','chassis')
    if o.name.startswith('Ladder cradle'):key='chassis'
    if key not in parts:key='chassis'
    if o.type=='MESH':
        for mod in list(o.modifiers):
            if mod.type=='BEVEL':
                if mod.width<=.006:
                    o.modifiers.remove(mod)
                else:mod.segments=1 if mod.width<.03 else 2
    # Smooth radial surfaces; hard edges remain separated by geometry.
bpy.context.view_layer.update()
for o in source:
    if o.get('official_hfd_logo'):continue
    if o.type not in {'MESH','FONT','CURVE'}:continue
    if o.name.startswith(('Elevation cylinder','Hydraulic piston')):continue
    key=o.get('rig_part','chassis')
    if o.name.startswith('Ladder cradle'):key='chassis'
    if key not in parts:key='chassis'
    m=bpy.data.meshes.new_from_object(o.evaluated_get(deps),depsgraph=deps)
    m.calc_loop_triangles()
    source_counts['triangles']+=len(m.loop_triangles)
    bucket=buckets.setdefault(key,{'verts':[],'faces':[],'mat_indices':[],'materials':[],'smooth':[]})
    offset=len(bucket['verts'])
    matrix=parts[key].matrix_world.inverted()@o.matrix_world
    bucket['verts'].extend([tuple(matrix@v.co) for v in m.vertices])
    material_map={}
    for i,material in enumerate(m.materials):
        if material not in bucket['materials']:bucket['materials'].append(material)
        material_map[i]=bucket['materials'].index(material)
    for p in m.polygons:
        bucket['faces'].append(tuple(offset+i for i in p.vertices))
        bucket['mat_indices'].append(material_map.get(p.material_index,0))
        bucket['smooth'].append(p.use_smooth)
    bpy.data.meshes.remove(m)

base=[]
assert len(buckets)==16, 'Expected chassis, turntable, four ladder stages, six wheel groups and four stabilizers.'
for key,b in buckets.items():
    m=bpy.data.meshes.new('L07_%s_LOD0'%key)
    m.from_pydata(b['verts'],[],b['faces']);m.update()
    for material in b['materials']:m.materials.append(material)
    for p,idx,smooth in zip(m.polygons,b['mat_indices'],b['smooth']):p.material_index=idx;p.use_smooth=smooth
    bm=bmesh.new();bm.from_mesh(m)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00003)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(m);bm.free();m.update()
    o=bpy.data.objects.new('L07_%s_LOD0'%key,m);lods[0].objects.link(o);o.parent=parts[key]
    o['rig_part']=key
    base.append(o)
for o in source:bpy.data.objects.remove(o,do_unlink=True)
bpy.data.objects.remove(oldroot,do_unlink=True)

# Procedural telescopic outrigger housings remain connected throughout deployment.
def cube(name,parent,loc,size,material):
    sx,sy,sz=[a/2 for a in size]
    verts=[(-sx,-sy,-sz),(-sx,-sy,sz),(-sx,sy,-sz),(-sx,sy,sz),(sx,-sy,-sz),(sx,-sy,sz),(sx,sy,-sz),(sx,sy,sz)]
    faces=[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)]
    m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.materials.append(material)
    o=bpy.data.objects.new(name,m);lods[0].objects.link(o);o.parent=parent;o.location=loc
    base.append(o);return o
metal=bpy.data.materials['L07_Brushed aluminum']
for x in [-1.65,4.56]:
    for side in [-1,1]:
        beam=cube('L07_OutriggerBeam_%s_%s_LOD0'%(x,side),rig,(x,side*1.13,.53),(.19,.24,.16),metal)
        driver(beam,'scale',1,'Stabilizers','1+1.15*v/.24')
        driver(beam,'location',1,'Stabilizers','%s*(1.13+.575*v)'%side)
        sleeve=cube('L07_JackSleeve_%s_%s_LOD0'%(x,side),rig,(x,side*1.13,.48),(.17,.20,.32),metal)
        driver(sleeve,'location',1,'Stabilizers','%s*(1.13+1.15*v)'%side)

# Hydraulic cylinders aim at moving anchors; pistons stretch within the barrel.
for side in [-1,1]:
    anchor=empty('Hydraulic_lift_anchor_%s'%side,elevation,(-.03,side*.53,.24))
    lower=empty('Hydraulic_base_%s'%side,turret,(.24,side*.53,.07))
    track=lower.constraints.new('DAMPED_TRACK');track.target=anchor;track.track_axis='TRACK_Z'
    barrel=cube('L07_HydraulicBarrel_%s_LOD0'%side,lower,(0,0,.23),(.13,.13,.46),metal)
    piston=cube('L07_HydraulicPiston_%s_LOD0'%side,lower,(0,0,.46),(.065,.065,1.),bpy.data.materials['L07_Polished stainless'])
    for path,index,expr in [('scale',2,'max(.05,d-.35)'),('location',2,'.35+max(.05,d-.35)/2')]:
        f=piston.driver_add(path,index);d=f.driver;d.type='SCRIPTED'
        v=d.variables.new();v.name='d';v.type='LOC_DIFF';v.targets[0].id=lower;v.targets[1].id=anchor;d.expression=expr

# Preserve all articulation in each LOD. Collapse detailed surfaces at distance.
import sys
sys.path.insert(0,str(OUT))
from hfd_logo import create_decals
base.extend(create_decals(scene,rig,lods[0],'_LOD0'))
for level,ratio in [(1,.35),(2,.10)]:
    for original in base:
        o=original.copy();o.data=original.data.copy();o.name=original.name.replace('LOD0','LOD%s'%level)
        lods[level].objects.link(o)
        if len(o.data.polygons)>120:
            mod=o.modifiers.new('Distance mesh reduction','DECIMATE');mod.ratio=ratio
            bpy.context.view_layer.objects.active=o
            bpy.ops.object.modifier_apply(modifier=mod.name)
for level,c in enumerate(lods):
    for o in c.objects:
        driver(o,'hide_render',None,'LOD','v!=%s'%level)
        driver(o,'hide_viewport',None,'LOD','v!=%s'%level)

# Two editable action presets, with rest pose at frame 1.
def make_action(name,keys):
    rig.animation_data_create();rig.animation_data.action=None
    for frame,values in keys:
        for key,value in values.items():rig[key]=value;rig.keyframe_insert(data_path='["%s"]'%key,frame=frame)
    action=rig.animation_data.action;action.name=name;action.use_fake_user=True
    return action
aerial=make_action('L07_Aerial_Deploy',[
    (1,{'Stabilizers':0.,'Aerial_elevation_deg':0.,'Aerial_extension':0.,'Aerial_azimuth_deg':0.}),
    (35,{'Stabilizers':1.,'Aerial_elevation_deg':0.,'Aerial_extension':0.,'Aerial_azimuth_deg':0.}),
    (85,{'Stabilizers':1.,'Aerial_elevation_deg':55.,'Aerial_extension':0.,'Aerial_azimuth_deg':0.}),
    (125,{'Aerial_elevation_deg':55.,'Aerial_extension':.25,'Aerial_azimuth_deg':28.}),
    (180,{'Stabilizers':1.,'Aerial_elevation_deg':55.,'Aerial_extension':1.,'Aerial_azimuth_deg':28.})])
drive=make_action('L07_Wheels_Steering',[(1,{'Wheel_travel_m':0.,'Steering_deg':0.}),(40,{'Wheel_travel_m':3.,'Steering_deg':25.}),(80,{'Wheel_travel_m':6.,'Steering_deg':-25.}),(120,{'Wheel_travel_m':9.,'Steering_deg':0.})])
rig.animation_data.action=None
for key,(default,*_) in controls.items():rig[key]=default
scene.frame_start=1;scene.frame_end=180;scene.frame_set(1)
bpy.context.view_layer.update()

stats={'source':source_counts,'lods':{},'controls':list(controls),'rig_type':'Rigid hierarchical mechanical rig with property drivers; no deforming skin.'}
for level,c in enumerate(lods):
    triangles=0
    for o in c.objects:o.data.calc_loop_triangles();triangles+=len(o.data.loop_triangles)
    stats['lods'][str(level)]={'mesh_objects':len(c.objects),'triangles':triangles}

# Validate motion mathematically at rest and deployed poses.
checks=[]
for extension,elev,azimuth in [(0,0,0),(1,55,28),(1,80,-90)]:
    rig['Aerial_extension']=extension;rig['Aerial_elevation_deg']=elev;rig['Aerial_azimuth_deg']=azimuth
    rig.update_tag();bpy.context.view_layer.update()
    stage=parts['aerial_3'];tip=stage.matrix_world@Vector((-6.1-4.24,0,2.86-3.16))
    assert all(math.isfinite(v) for v in tip)
    if extension:assert tip.z>20,'Deployed ladder did not elevate correctly'
    checks.append({'extension':extension,'elevation':elev,'azimuth':azimuth,'tip_world':list(tip)})
for side in [-1,1]:
    rig['Stabilizers']=1.;rig.update_tag();bpy.context.view_layer.update()
    foot=parts['outrigger_-1.65_%s'%side].matrix_world@Vector((0,0,.2475))
    assert abs(foot.z)<.001
stats['motion_checks']=checks
for key,(default,*_) in controls.items():rig[key]=default
rig.update_tag();bpy.context.view_layer.update()

# Expose controls immediately on opening; keep reference/studio collections out of export.
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_perspective='CAMERA'
            area.spaces.active.shading.color_type='MATERIAL'
        if area.type=='PROPERTIES':area.spaces.active.context='OBJECT'
scene.camera=bpy.data.objects['Ladder07 hero']
scene.render.resolution_x=1600;scene.render.resolution_y=950
for other in list(bpy.data.scenes):
    if other!=scene:bpy.data.scenes.remove(other)
bpy.ops.file.pack_all()
notes=bpy.data.texts.new('LADDER07_RIG_README')
notes.write('Select Ladder07_CONTROLS and use Object Custom Properties.\nDeploy Stabilizers before aerial use. Elevate before slewing.\nLOD: 0 close, 1 medium, 2 distant.\nActions: L07_Aerial_Deploy, L07_Wheels_Steering. Assign an action to preview it; controls are unkeyed by default.\nNo collision automation, physics, or certified load limits. All parts remain rigid.\n'+json.dumps(stats,indent=2))
OUT.joinpath('optimization_report.json').write_text(json.dumps(stats,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Ladder07_Rigged.blend'),compress=True)
print(json.dumps(stats,indent=2))
