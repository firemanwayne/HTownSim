"""Reference and render review refinements; run after BuildDistrictChief08.py."""
import bpy
import math
from mathutils import Vector, Matrix

sc = bpy.context.scene
coll = bpy.data.collections['DC08 | Vehicle']
ctrl = bpy.data.objects['DC08_CONTROLS']

# Keep glass and gasket outside the cab's sloping front panel at every height.
for name, offset in [('Windshield rubber',.014),('Panoramic windshield',.023)]:
    o=bpy.data.objects[name]
    for p in o.data.vertices:
        p.co.x=-1.57+(p.co.z-1.67)*1.1-offset
    o.data.update()

# Lettering proportions: large rear-facing unit numbers and bolder side titles.
for o in coll.objects:
    if o.type != 'FONT':continue
    if o.name.startswith('Side district chief'):o.data.size=.28
    if o.name.startswith('Side unit number'):o.data.size=.65
    if o.name.startswith('Rear unit number'):
        o.data.size=.45;o.location.z=1.91
    if o.name.startswith('Rear Houston Fire'):o.data.size=.20

# Replace straight rear title with the reference's shallow arch.
for o in list(coll.objects):
    if o.name.startswith('Rear district chief') or o.name.startswith('Rear arch letter'):
        bpy.data.objects.remove(o,do_unlink=True)
font=bpy.data.fonts.get('Arial Narrow Bold') or bpy.data.fonts.load('C:/Windows/Fonts/ARIALNB.TTF')
chars='DISTRICT CHIEF'
for i,ch in enumerate(chars):
    if ch==' ':continue
    y=(i-(len(chars)-1)/2)*.129
    z=2.075+.112*(1-(y/.78)**2)
    slope=-2*.112*y/(.78**2)
    angle=math.atan(slope)
    u=Vector((0,math.cos(angle),math.sin(angle)))
    v=Vector((0,-math.sin(angle),math.cos(angle)))
    q=Matrix((u,v,u.cross(v))).transposed().to_quaternion()
    for outline in [True,False]:
        data=bpy.data.curves.new('Rear arch typography','FONT')
        data.body=ch;data.font=font;data.align_x='CENTER';data.align_y='CENTER'
        data.size=.295;data.extrude=.0005;data.offset=.005 if outline else 0
        data.materials.append(bpy.data.materials['DC08 | Satin black powdercoat' if outline else 'DC08 | Reflective white'])
        o=bpy.data.objects.new('Rear arch letter '+str(i)+(' outline' if outline else ''),data)
        coll.objects.link(o);o.parent=ctrl;o.location=(3.347 if outline else 3.35,y,z)
        o.rotation_mode='QUATERNION';o.rotation_quaternion=q

# Cut photo decals to insignia silhouettes rather than visible rectangular patches.
shapes={
 'Houston Fire EMS Rescue door shield':[(590,216),(617,222),(636,238),(633,271),(620,302),(596,327),(572,309),(557,279),(552,242),(561,224)],
 'Houston Fire District Chief door crest':[(862,224),(876,230),(897,226),(914,242),(900,251),(902,279),(914,295),(895,314),(870,328),(840,318),(822,301),(834,286),(831,258),(819,246),(836,230),(851,233)]}
for name,pixels in shapes.items():
    for o in [o for o in coll.objects if o.name.startswith(name) and o.type=='MESH']:
        old=o.data
        # Existing vertices encode the correctly mirrored local decal basis.
        v0,v1,v2,v3=[v.co.copy() for v in old.vertices[:4]]
        xa,ya,xb,yb=(550,213,639,328) if 'shield' in name else (819,222,914,328)
        coords=[]
        for px,py in pixels:
            coords.append(v0+(v1-v0)*(px-xa)/(xb-xa)+(v3-v0)*(yb-py)/(yb-ya))
        data=bpy.data.meshes.new(name+' silhouette');data.from_pydata(coords,[],[tuple(range(len(coords)))])
        for m in old.materials:data.materials.append(m)
        uv=data.uv_layers.new(name='Reference crop UV')
        for j,(px,py) in enumerate(pixels):uv.data[j].uv=(px/1628,1-py/539)
        o.data=data

# Keep the emergency LEDs saturated red under studio exposure.
m=bpy.data.materials['DC08 | RED emergency LED']
n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
n.inputs['Emission Strength'].driver_add('default_value').driver.expression='v*2.5'
for i in range(6):
    m=bpy.data.materials['DC08 | AMBER arrow module %02d'%(i+1)]
    n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    d=n.inputs['Emission Strength'].driver_add('default_value').driver
    d.expression=f'9*((v==4) or (v==1 and (a<0.5 or (frame//4)%6>={i})) or (v==2 and (a<0.5 or (frame//4)%6>={5-i})) or (v==3 and (a<0.5 or (frame//4)%3>={2-min(i,5-i)})))'

# Deeper red lacquer and less blown-out metallic roof reflections.
paint=bpy.data.materials['DC08 | Fire red clearcoat']
n=next(n for n in paint.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
n.inputs['Base Color'].default_value=(.36,.006,.012,1)
n.inputs['Metallic'].default_value=.18
n.inputs['Roughness'].default_value=.28
n.inputs['Coat Weight'].default_value=.3

def level(material):
    return next(n for n in bpy.data.materials[material].node_tree.nodes if n.type=='BSDF_PRINCIPLED').inputs['Emission Strength'].default_value

def refresh(frame):
    ctrl.update_tag();sc.frame_set(frame);bpy.context.view_layer.update()

# Test actual evaluated driver values, then restore the presentation state.
ctrl['green_command']=0.0;refresh(1)
assert level('DC08 | GREEN command beacon')==0
ctrl['green_command']=1.0;refresh(2)
assert level('DC08 | GREEN command beacon')==10
ctrl['green_command']=0.0
ctrl['arrow_mode']=0;refresh(3)
assert all(level('DC08 | AMBER arrow module %02d'%(i+1))==0 for i in range(6))
ctrl['arrow_mode']=4;refresh(4)
assert all(level('DC08 | AMBER arrow module %02d'%(i+1))==9 for i in range(6))
ctrl['arrow_mode']=1;refresh(1)
assert [level('DC08 | AMBER arrow module %02d'%(i+1)) for i in range(6)]==[9,0,0,0,0,0]
ctrl['arrow_mode']=2;refresh(1)
assert [level('DC08 | AMBER arrow module %02d'%(i+1)) for i in range(6)]==[0,0,0,0,0,9]
ctrl['arrow_mode']=3;refresh(1)
assert [level('DC08 | AMBER arrow module %02d'%(i+1)) for i in range(6)]==[0,0,9,9,0,0]
ctrl['arrow_mode']=0;refresh(1)
print('PASS: green beacon off/on; arrow off/all/left/right/split evaluated emissions.')
sc.camera=bpy.data.objects['DC08 Front three-quarter']
