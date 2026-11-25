"""Seat exterior details against evaluated body surfaces, preserving editable parts."""
import bpy
from mathutils import Vector

collection=bpy.data.collections['DC08 | Vehicle']
root=bpy.data.objects['DC08_CONTROLS']
deps=bpy.context.evaluated_depsgraph_get()

def bounds(o):
    p=[o.matrix_world@Vector(v) for v in o.bound_box]
    return Vector([min(v[i] for v in p) for i in range(3)]),Vector([max(v[i] for v in p) for i in range(3)])

def center(o):
    a,b=bounds(o)
    return (a+b)/2

def attach(o,target):
    matrix=o.matrix_world.copy();o.parent=target;o.matrix_world=matrix

def shift(o,delta):
    m=o.matrix_world.copy();m.translation+=delta;o.matrix_world=m

def hit(targets,point,normal):
    bpy.context.view_layer.update()
    start=point+normal*.5
    hits=[]
    for target in targets:
        ob=target.evaluated_get(deps);inv=ob.matrix_world.inverted()
        ok,loc,n,face=ob.ray_cast(inv@start,inv.to_3x3()@(-normal),distance=2)
        if ok:
            world=ob.matrix_world@loc
            hits.append(((world-start).length,world,target))
    if not hits:return None
    return min(hits,key=lambda h:h[0])[1:]

def seat(o,targets,normal,gap=.0004):
    result=hit(targets,center(o),normal)
    if result is None:
        raise RuntimeError('No mounting surface for '+o.name)
    point,target=result
    corners=[o.matrix_world@Vector(v) for v in o.bound_box]
    back=min(p.dot(normal) for p in corners)
    shift(o,normal*(point.dot(normal)+gap-back))
    attach(o,target)

def support(name,y):
    o=bpy.data.objects.get(name)
    if o:return o
    x,z=3.306,1.955
    w,d,h=.069,.105,.75
    verts=[(x+a*w/2,y+b*d/2,z+c*h/2) for a,b,c in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    data=bpy.data.meshes.new(name);data.from_pydata(verts,[],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    o=bpy.data.objects.new(name,data);collection.objects.link(o);o.parent=root
    data.materials.append(bpy.data.materials['DC08 | Satin black powdercoat'])
    b=o.modifiers.new('Rail edge radius','BEVEL');b.width=.006;b.segments=3
    o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o

rails=[support('Rear camper lamp mounting rail '+label,y) for label,y in [('driver',-.985),('passenger',.985)]]
bpy.context.view_layer.update()
body_names=['Crew cab lower volume','Cab lower volume','Front fender','Bed quarter','Bed upper rail volume','Commercial camper shell','Sculpted hood','Crew cab upper shell','Camper side lift-up door']
body=[o for o in collection.objects if any(o.name==n or o.name.startswith(n+'.') for n in body_names)]
tail=bpy.data.objects['Tailgate'];door=bpy.data.objects['Rear camper door']

# Thin markings sit at submillimetre decal offsets, not centimetre stand-offs.
count=0
for o in list(collection.objects):
    if o.type=='FONT':
        targets=None;normal=None
        if o.name.startswith(('Rear Houston Fire','Tailgate emboss')):targets=[tail];normal=Vector((1,0,0))
        elif o.name.startswith(('Rear arch letter','Rear unit number')):targets=[door];normal=Vector((1,0,0))
        elif o.name.startswith(('Side district chief','Side unit number','Bed HOUSTON FIRE')):
            targets=body;normal=Vector((0,1 if center(o).y>0 else -1,0))
        if targets:
            o.data.extrude=.00005
            # Outline layer and letter layer remain separated to avoid z fighting.
            gap=.00025 if ('outline' in o.name) else .00065
            seat(o,targets,normal,gap);count+=1
    elif o.type=='MESH':
        if o.name.startswith('Tailgate reflective chevron'):seat(o,[tail],Vector((1,0,0)),.00015);count+=1
        elif o.name.startswith('Camper reflective chevron'):seat(o,[door],Vector((1,0,0)),.00015);count+=1
        elif o.name.startswith(('White side stripe','White stripe pinline')):
            o.dimensions.y=.00015
            bpy.context.view_layer.update()
            seat(o,body,Vector((0,1 if center(o).y>0 else -1,0)),.00015);count+=1
        elif o.name.startswith(('Houston Fire EMS Rescue door shield','Houston Fire District Chief door crest')):
            seat(o,body,Vector((0,1 if center(o).y>0 else -1,0)),.00065);count+=1

# Move complete lamp assemblies together. Lens/diode layering is preserved.
def panels(prefix,targets,normal_fn):
    members=[o for o in collection.objects if o.name.startswith(prefix)]
    bezels=[o for o in members if 'bezel' in o.name]
    groups={b:[] for b in bezels}
    for o in members:
        if o in bezels:continue
        b=min(bezels,key=lambda b:(center(o)-center(b)).length)
        groups[b].append(o)
    for b,parts in groups.items():
        # Detach children for idempotent group movement on repeated runs.
        for o in parts:attach(o,root)
        before=b.matrix_world.translation.copy()
        seat(b,targets,normal_fn(b),-.0005)
        delta=b.matrix_world.translation-before
        for o in parts:shift(o,delta);attach(o,b)
        b['mounting_gap_m']=-.0005
    return len(bezels)

rear=Vector((1,0,0))
fixtures=panels('Rear corner RED',rails,lambda o:rear)
fixtures+=panels('Tailgate RED',[tail],lambda o:rear)
fixtures+=panels('Side RED',body,lambda o:Vector((0,1 if center(o).y>0 else -1,0)))
fixtures+=panels('Bumper WHITE reverse',[o for o in collection.objects if o.name.startswith('Rear chrome bumper wing')],lambda o:rear)
fixtures+=panels('Arrow AMBER',[bpy.data.objects['Rear top light rail']],lambda o:rear)
fixtures+=panels('Rear upper WHITE',[bpy.data.objects['Rear top light rail']],lambda o:rear)

# Handles/latches and rear badge supports also need physical contact.
for o in list(collection.objects):
    if o.name.startswith('Camper rear paddle latch'):seat(o,[door],rear,-.0005)
    elif o.name.startswith('Camper latch recess'):seat(o,[bpy.data.objects['Camper rear paddle latch']],rear,-.0005)
    elif o.name.startswith('Tailgate handle recess'):
        shift(o,Vector((0,0,1.39-center(o).z)))
        seat(o,[tail],rear,-.0005)
    elif o.name.startswith('Tailgate handle') and 'recess' not in o.name:
        shift(o,Vector((0,0,1.39-center(o).z)))
        seat(o,[bpy.data.objects['Tailgate handle recess']],rear,-.0005)

# Seat remaining badges, door hardware and trim that shared the same offset issue.
for o in list(collection.objects):
    if o.name.startswith(('Door vertical shut line','Door sill contour','Fender vent','Fuel filler','Bed access panel')):
        seat(o,body,Vector((0,1 if center(o).y>0 else -1,0)),-.0005)
for prefix in ['Camper latch plate','Camper paddle handle','Door handle recess','Door pull']:
    for o in [q for q in collection.objects if q.name.startswith(prefix)]:
        targets=body
        prior={'Camper paddle handle':'Camper latch plate','Door pull':'Door handle recess'}.get(prefix)
        if prior:targets=[q for q in collection.objects if q.name.startswith(prior)]
        seat(o,targets,Vector((0,1 if center(o).y>0 else -1,0)),-.0005)
for base in [o for o in collection.objects if o.name.startswith('Ford oval chrome')]:
    rearward=center(base).x>0
    pieces=[o for o in collection.objects if o.name.startswith(('Ford oval blue','Ford script')) and (center(o).x>0)==rearward]
    for o in pieces:attach(o,root)
    old=base.matrix_world.translation.copy()
    seat(base,[tail] if rearward else [bpy.data.objects['Black grille surround']],Vector((1 if rearward else -1,0,0)),-.0005)
    delta=base.matrix_world.translation-old
    for o in pieces:shift(o,delta);attach(o,base)

bpy.context.view_layer.update()
# Numeric verification of the explicitly reported defects against evaluated surfaces.
for o in collection.objects:
    if o.name.startswith(('Rear Houston Fire','Rear arch letter','Rear unit number')):
        target=tail if o.name.startswith('Rear Houston Fire') else door
        p,t=hit([target],center(o),rear)
        a,b=bounds(o)
        assert 0 <= a.x-p.x < .0015,(o.name,a.x-p.x)
for o in collection.objects:
    if o.name.startswith('Rear corner RED') and 'bezel' in o.name:
        p,t=hit(rails,center(o),rear);a,b=bounds(o)
        assert -.001 < a.x-p.x < .001,(o.name,a.x-p.x)
print('PASS:',count,'surface markings seated;',fixtures,'lamp assemblies mounted. Rear lettering <1.5 mm; corner bezels contact rails.')
