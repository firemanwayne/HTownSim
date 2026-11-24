"""Houston Ladder 07, dimensioned from the supplied FY25 proposal drawing.
Run in Blender. X is longitudinal (front negative), Y is lateral, Z is up.
Creates a separate scene; does not alter the existing truck/map scenes.
"""
import bpy
import math
from mathutils import Vector, Matrix
from pathlib import Path

OUT = Path(r'J:/Unity/projects/HTownSim/ArtSource/Ladder07')
scene = bpy.data.scenes.new('Houston Ladder 07')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
root = bpy.data.objects.new('Ladder07', None)
scene.collection.objects.link(root)
root['drawing_length_inches'] = 499
root['drawing_height_inches'] = 144
root['drawing_body_width_inches'] = 100
root['drawing_wheelbase_inches'] = 246
root['reference_note'] = 'FY25 drawing estimated dimensions. Exterior details inferred from supplied Ladder 71 side/rear and RMA front photos. Unit renumbered 07.'
collections = {}
for name in ['Chassis', 'Cab', 'Body', 'Wheels', 'Aerial', 'Graphics', 'Lighting', 'Studio']:
    c = bpy.data.collections.new('L07_' + name)
    scene.collection.children.link(c)
    collections[name] = c
group = 'Chassis'
rig_part = 'chassis'

def finish(o, name, material=None, bevel=0):
    o.name = name
    for c in list(o.users_collection):
        c.objects.unlink(o)
    collections[group].objects.link(o)
    if group != 'Studio':
        o.parent = root
        o['rig_part'] = rig_part
    if material:
        o.data.materials.append(material)
    if bevel:
        m = o.modifiers.new('Manufactured edge radii', 'BEVEL')
        m.width = bevel
        m.segments = 3
        m = o.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    return o

def mat(name, color, metal=0, rough=.3):
    m = bpy.data.materials.new('L07_' + name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    return m

red = mat('Fire red enamel', (.57,.008,.018), .32, .24)
white = mat('Reflective white', (.92,.94,.9), .22, .27)
ladderwhite = mat('Aerial white powder coat', (.82,.86,.85), .38, .27)
chrome = mat('Polished stainless', (.65,.72,.76), .94,.18)
alum = mat('Brushed aluminum', (.43,.49,.53), .85,.32)
black = mat('Rubber and seals', (.012,.016,.02), .05,.55)
tire = mat('Tire rubber', (.019,.023,.026),0,.72)
glass = mat('Blue green cab glazing', (.013,.067,.083), .45,.13)
dark = mat('Grille recess', (.004,.006,.008), .1,.4)
yellow = mat('Fluorescent rear chevron', (.73,.98,.002), .1,.32)
gold = mat('Gold graphics', (.9,.58,.08),.5,.28)
blue = mat('Houston badge blue', (.012,.045,.35),.15,.3)
amber = mat('Amber lenses', (1,.28,.008), .2,.23)
lampred = mat('Red lenses', (.68,.008,.014), .2,.18)
lens = mat('Clear LED lenses', (.7,.87,.94), .35,.18)
for m in [lampred,lens,amber]:
    p=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    p.inputs['Emission Color'].default_value=m.diffuse_color
    p.inputs['Emission Strength'].default_value=.18

def box(name, loc, size, material, bevel=.01):
    sx,sy,sz=[v/2 for v in size]
    verts=[(-sx,-sy,-sz),(-sx,-sy,sz),(-sx,sy,-sz),(-sx,sy,sz),(sx,-sy,-sz),(sx,-sy,sz),(sx,sy,-sz),(sx,sy,sz)]
    faces=[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)]
    o=mesh(name,verts,faces,material,bevel)
    o.location=loc
    return o

def cyl(name, loc, radius, depth, material, axis='Z', vertices=48):
    verts=[(radius*math.cos(i*math.tau/vertices),radius*math.sin(i*math.tau/vertices),z) for z in [-depth/2,depth/2] for i in range(vertices)]
    faces=[tuple(reversed(range(vertices))),tuple(range(vertices,2*vertices))]
    faces += [(i,(i+1)%vertices,(i+1)%vertices+vertices,i+vertices) for i in range(vertices)]
    o=mesh(name,verts,faces,material,.004)
    o.location=loc
    if axis=='Y': o.rotation_euler.x=math.pi/2
    if axis=='X': o.rotation_euler.y=math.pi/2
    for p in o.data.polygons: p.use_smooth=len(p.vertices)==4
    return o

def beam(name, a, b, radius, material):
    a,b=Vector(a),Vector(b)
    o=cyl(name,(a+b)/2,radius,(b-a).length,material,vertices=12)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return o

def mesh(name, verts, faces, material, bevel=0):
    d=bpy.data.meshes.new(name)
    d.from_pydata(verts,[],faces)
    d.update()
    o=bpy.data.objects.new(name,d)
    scene.collection.objects.link(o)
    return finish(o,name,material,bevel)

def profile(name, points, y0,y1,material,bevel=.01):
    n=len(points)
    verts=[(x,y,z) for y in [y0,y1] for x,z in points]
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,verts,faces,material,bevel)

def rounded_panel(name,x,y,z,w,h,r,material):
    points=[]
    for cx,cz,start in [(x+w/2-r,z+h/2-r,0),(x-w/2+r,z+h/2-r,90),(x-w/2+r,z-h/2+r,180),(x+w/2-r,z-h/2+r,270)]:
        for k in range(9):
            a=math.radians(start+k*90/8)
            points.append((cx+r*math.cos(a),cz+r*math.sin(a)))
    return profile(name,points,y-.012,y+.012,material,0)

font=bpy.data.fonts.load('C:/Windows/Fonts/arialbd.ttf')
def text(name, body, loc, width, material, side=-1, size=.25):
    d=bpy.data.curves.new(name,'FONT')
    d.body=body
    d.font=font
    d.align_x='CENTER'
    d.align_y='CENTER'
    d.size=size
    d.extrude=.0004
    o=bpy.data.objects.new(name,d)
    scene.collection.objects.link(o)
    o.location=loc
    # local right, up, surface normal
    if side==-1: basis=((1,0,0),(0,0,1),(0,-1,0))
    elif side==1: basis=((-1,0,0),(0,0,1),(0,1,0))
    elif side==2: basis=((0,1,0),(0,0,1),(1,0,0))
    else: basis=((0,-1,0),(0,0,1),(-1,0,0))
    o.rotation_euler=Matrix(basis).transposed().to_euler()
    finish(o,name,material)
    bpy.context.view_layer.update()
    if width and o.dimensions.x+o.dimensions.y>0:
        scale=width/max(o.dimensions.x,o.dimensions.y)
        o.scale=(scale,scale,scale)
    return o

def arch(name, x, y, z, r, material):
    # Half round fender with a short vertical skirt, spanning the wheel opening.
    pts=[(x+r*math.cos(t),y,z+r*math.sin(t)) for t in [i*math.pi/48 for i in range(49)]]
    for a,b in zip(pts,pts[1:]): beam(name,a,b,.025,material)

def lamp(name, loc, size, material, face='side'):
    box(name+' chrome bezel',loc,size,chrome,.025)
    x,y,z=loc
    sx,sy,sz=size
    if face=='side':
        sign=1 if y>0 else -1
        box(name+' gasket',(x,y+sign*.012,z),(sx*.89,sy+.005,sz*.80),black,.018)
        box(name+' optic',(x,y+sign*.025,z),(sx*.81,sy+.008,sz*.65),material,.014)
        for i in range(5):
            box(name+' LED',(x+(i-2)*sx*.13,y+sign*(sy/2+.035),z),(sx*.075,.006,sz*.4),lens,.003)
    else:
        sign=1 if x>0 else -1
        box(name+' optic',(x+sign*.02,y,z),(sx+.012,sy*.82,sz*.72),material,.015)
        for i in range(4):
            box(name+' LED',(x+sign*(sx/2+.03),y+(i-1.5)*sy*.18,z),(.007,sy*.11,sz*.43),lens,.003)

# Drawing: 499 overall, 100 front overhang, 246 front axle to tandem centre.
front=-6.3373
rear=6.3373
axles=[-3.7973,1.7651,3.1371]
for y in [-.68,.68]: box('Longitudinal chassis rail',(0,y,.64),(11.8,.16,.26),dark)
for x in axles:
    cyl('Axle housing',(x,0,.55),.12,2.15,dark,'Y')
    for y in [-.65,.65]: box('Leaf spring pack',(x,y,.5),(1.15,.12,.07),black)
box('Underbody equipment',(.2,0,.75),(4.4,1.5,.45),dark,.04)

group='Wheels'
for ai,x in enumerate(axles):
    for side in [-1,1]:
        rig_part='wheel_%s_%s'%(ai,side)
        y=side*1.075
        cyl('Tire %s %s'%(ai,side),(x,y,.555),.555,.35,tire,'Y',96)
        # Layered sidewall shoulders and polished dished wheel.
        cyl('Sidewall shoulder',(x,side*1.252,.555),.488,.018,tire,'Y',96)
        cyl('Rim outer lip',(x,side*1.269,.555),.333,.025,chrome,'Y',64)
        cyl('Rim recessed dish',(x,side*1.288,.555),.292,.026,alum,'Y',64)
        cyl('Wheel face',(x,side*1.307,.555),.233,.027,chrome,'Y',64)
        cyl('Hub',(x,side*1.338,.555),.105,.075,chrome,'Y')
        cyl('Hub center',(x,side*1.38,.555),.048,.012,black,'Y')
        for k in range(10):
            a=k*math.tau/10
            cyl('Wheel ventilation hole',(x+.263*math.sin(a),side*1.31,.555+.263*math.cos(a)),.034,.009,dark,'Y',16)
            cyl('Lug nut',(x+.153*math.sin(a),side*1.334,.555+.153*math.cos(a)),.018,.026,chrome,'Y',6)
        # Multiple narrow tread ribs and shoulder blocks.
        for k in range(64):
            a=k*math.tau/64
            for offset in [-.12,.12]:
                o=box('Shoulder tread',(x+.551*math.sin(a),y+offset,.555+.551*math.cos(a)),(.039,.095,.014),tire,.002)
                o.rotation_euler.y=a
        if ai:
            cyl('Inner dual tire',(x,side*.735,.555),.55,.29,tire,'Y',64)
        rig_part='chassis'
        box('Mud flap',(x+.59,side*1.07,.44),(.045,.39,.53),black,.006)

group='Cab'
cab=profile('Gladiator raised roof crew cab',[(-5.84,.56),(-2.265,.56),(-2.265,3.03),(-4.92,3.03),(-5.12,2.78),(-5.60,2.78),(-5.84,1.65)],-1.2637,1.2637,red,.045)
# True open wheel arches, not tires buried in solid bodywork.
for x in [axles[0]]:
    cutter=cyl('Temporary arch cutter',(x,0,.555),.635,3.0,None,'Y',96)
    mod=cab.modifiers.new('Front wheel opening','BOOLEAN');mod.object=cutter
    bpy.context.view_layer.objects.active=cab
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter,do_unlink=True)
for side in [-1,1]:
    y=side*1.269
    # Door seams and two distinct side windows.
    for x in [-5.35,-4.3,-3.24,-2.33]: box('Cab door seam',(x,y,1.83),(.012,.014,2.24),dark,.003)
    for x,z,w,h in [(-4.88,2.18,.91,.84),(-2.86,2.13,.73,.78)]:
        rounded_panel('Window rubber surround',x,y+side*.012,z,w+.055,h+.055,.08,black)
        rounded_panel('Tinted cab side glass',x,y+side*.03,z,w,h,.065,glass)
    for x in [-4.38,-2.53]:
        box('Door handle recess',(x,y+side*.02,1.46),(.30,.035,.12),chrome,.02)
        box('Pull handle',(x,y+side*.045,1.47),(.21,.028,.035),chrome,.01)
    for x in [-5.42,-2.47]: beam('Cab grab rail',(x,y+side*.075,1.65),(x,y+side*.075,2.15),.015,chrome)
    arch('Front stainless fender',axles[0],side*1.296,.555,.637,chrome)
    for x,w in [(-5.14,.9),(-2.73,.71)]:
        box('Cab entry step',(x,side*1.2,.615),(w,.30,.10),alum)
        for j in range(5):box('Step traction',(x,side*(1.1+j*.04),.669),(w*.93,.012,.006),chrome,.001)
    lamp('Upper cab scene light',(-3.75,side*1.287,2.85),(.42,.055,.18),lens)
    # Split stripes at wheel opening; no graphics floating across wheel wells.
    for x,w in [(-5.16,1.22),(-2.67,.74)]:
        for z,h in [(1.01,.13),(.90,.022),(1.12,.022)]: box('Cab reflective stripe',(x,side*1.292,z),(w,.007,h),white,.001)
    # Texas flag silhouette, using a small deliberately simplified emblem.
    pts=[(-.22,.16),(-.06,.16),(-.06,.31),(.08,.31),(.09,.17),(.26,.16),(.28,.02),(.12,-.03),(.04,-.24),(-.08,-.16),(-.14,-.02),(-.26,.04)]
    profile('Texas insignia',[(x-3.76,z+2.12) for x,z in pts],y+side*.03,y+side*.036,white,0)
    profile('Texas blue field',[(-4.0,2.28),(-3.82,2.28),(-3.82,2.42),(-3.72,2.42),(-3.72,1.96),(-3.84,2.1),(-4.0,2.15)],y+side*.039,y+side*.042,blue,0)
    profile('Texas red field',[(-3.71,2.12),(-3.49,2.12),(-3.64,2.04),(-3.72,1.88)],y+side*.04,y+side*.044,red,0)
    star=[]
    for k in range(10):
        a=math.pi/2+k*math.pi/5;r=.055 if k%2==0 else .023
        star.append((-3.81+r*math.cos(a),y+side*.05,2.18+r*math.sin(a)))
    mesh('Texas white star',star,[tuple(range(10))],white)
    # Badge shaped backing and legible service/unit lettering.
    for x in [-2.88]:
        outline=[(x-.17,1.64),(x+.17,1.64),(x+.16,1.4),(x,1.27),(x-.16,1.4)]
        profile('Houston shield',outline,y+side*.034,y+side*.04,gold,.005)
        outline=[(x-.14,1.61),(x+.14,1.61),(x+.13,1.42),(x,1.3),(x-.13,1.42)]
        profile('Badge blue enamel',outline,y+side*.045,y+side*.05,blue,0)
        text('Badge city','HOUSTON',(x,y+side*.06,1.56),.25,gold,side)
        text('Badge unit','07',(x,y+side*.06,1.43),.15,gold,side)

# Front split windscreen, fascia, chrome perforated grille and paired lamps.
for side in [-1,1]:
    for name,x,w,h,material in [('Windshield black seal',-5.733,1.17,1.03,black),('Windshield glazing',-5.76,1.11,.956,glass)]:
        o=rounded_panel(name,0,0,0,w,h,.065,material)
        o.location=(x,side*.609,2.22)
        o.rotation_euler=(Matrix.Rotation(.21,4,'Y')@Matrix.Rotation(-math.pi/2,4,'Z')).to_euler()
    beam('Wiper arm',(-5.86,side*.94,1.72),(-5.86,side*.56,1.9),.012,black)
    beam('Wiper blade',(-5.872,side*.87,1.9),(-5.872,side*.25,1.9),.014,black)
box('Grille polished surround',(-5.874,0,1.19),(.065,1.46,.84),chrome,.035)
for row in range(10):
    for col in range(8):
        o=rounded_panel('Grille rounded opening',0,0,0,.142,.047,.023,dark)
        o.rotation_euler.z=-math.pi/2
        o.location=(-5.912,(col-3.5)*.17,.83+row*.078)
for side in [-1,1]:
    box('Headlight polished housing',(-5.873,side*.99,1.12),(.09,.46,.49),chrome,.03)
    for z,h in [(.78,.095),(.70,.018)]:box('Front cheek reflective stripe',(-5.884,side*1.0,z),(.012,.51,h),white,.001)
    for z in [1.0,1.24]:
        for y in [side*.89,side*1.09]: lamp('Front headlight',(-5.9,y,z),(.07,.19,.17),lens,'front')
    lamp('Front amber turn',(-5.87,side*1.115,1.57),(.06,.22,.12),amber,'front')
    beam('Mirror arm',(-5.57,side*1.27,1.66),(-5.57,side*1.64,1.85),.035,chrome)
    beam('Mirror upright',(-5.57,side*1.64,1.85),(-5.57,side*1.64,2.23),.024,chrome)
    box('Mirror shell',(-5.57,side*1.64,2.28),(.17,.16,.47),chrome,.06)
    box('Mirror glass',(-5.475,side*1.64,2.28),(.012,.125,.38),glass,.025)
box('Front bumper red',(-6.105,0,.48),(.4646,2.64,.27),red,.026)
box('Front bumper treadplate',(-6.08,0,.63),(.48,2.61,.035),alum,.007)
for y in [-1.03,1.03]: lamp('Bumper inset',(-6.345,y,.49),(.024,.22,.15),lens,'front')
for y in [-.74,.74]:
    cyl('Bumper hose port',(-6.35,y,.48),.08,.023,chrome,'X')
    cyl('Port recess',(-6.365,y,.48),.055,.009,dark,'X')
cyl('Federal siren',(-6.12,-1.02,.80),.145,.12,chrome,'X')
cyl('Siren grille',(-6.19,-1.02,.80),.12,.008,dark,'X')
for i in range(-4,5): beam('Siren grille bars',(-6.2,-1.12,.80+i*.023),(-6.2,-.92,.80+i*.023),.005,chrome)
text('Front unit','LADDER 07',(-5.918,0,1.68),.78,gold,0)
for side in [-1,1]:
    box('Roof lightbar mounting',(-5.18,side*.84,2.91),(.45,.51,.08),black)
    box('Roof emergency lightbar',(-5.18,side*.84,3.01),(.43,.53,.13),lens,.025)
    for j in [-1,0,1]:box('Roof red LED',(-5.40,side*.84+j*.15,3.015),(.012,.10,.08),lampred,.006)
    text('Bumper 07','07',(-6.10,side*1.328,.48),.30,white,side)

group='Body'
body=profile('Aluminum equipment body',[(-2.265,.55),(6.185,.55),(6.185,2.32),(3.96,2.32),(3.96,2.53),(-2.265,2.53)],-1.27,1.27,red,.022)
for x in axles[1:]:
    cutter=cyl('Temporary tandem arch cutter',(x,0,.555),.64,3,None,'Y',96)
    mod=body.modifiers.new('Tandem wheel opening','BOOLEAN');mod.object=cutter
    bpy.context.view_layer.objects.active=body
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.data.objects.remove(cutter,do_unlink=True)
for side in [-1,1]:
    y=side*1.283
    # Forebody equipment doors, above-tandem lockers, rear lockers.
    specs=[(-1.98,.43,.68,2.4),(-1.23,.99,.68,2.4),(-.10,1.11,.68,2.4),(1.19,1.31,1.43,2.4),(2.65,1.44,1.43,2.4),(4.19,1.46,1.52,2.15),(5.48,.94,.70,2.15)]
    for idx,(x,w,z0,z1) in enumerate(specs):
        mid=(z0+z1)/2;h=z1-z0
        box('Locker %s %s frame'%(side,idx),(x,y,mid),(w+.045,.035,h+.045),chrome,.006)
        box('Locker gasket',(x,y+side*.022,mid),(w+.014,.016,h+.014),black,.004)
        box('Red roller shutter',(x,y+side*.035,mid),(w,.018,h),red,.004)
        for j in range(1,int(h/.035)):
            box('Shutter horizontal joint',(x,y+side*.047,z0+j*.035),(w-.018,.003,.0024),red,.0004)
        box('Shutter lower pull',(x,y+side*.062,z0+.034),(w-.07,.045,.03),chrome,.006)
        for xx in [x-w/2+.065,x+w/2-.065]:box('Pull end block',(xx,y+side*.065,z0+.06),(.07,.06,.065),black,.008)
    for x in axles[1:]: arch('Tandem stainless fender',x,side*1.302,.555,.642,chrome)
    for x in [1.18,2.64,4.23]:
        box('Lower equipment latch',(x,y+side*.025,1.33),(.11,.028,.035),chrome,.006)
    for x in [-1.65,4.56]:
        box('Stabilizer enclosure',(x,side*1.16,.88),(.31,.25,.56),red)
        rig_part='outrigger_%s_%s'%(x,side)
        box('Stowed outrigger leg',(x,side*1.13,.43),(.14,.18,.35),dark)
        box('Stowed outrigger foot',(x,side*1.13,.27),(.43,.34,.045),alum)
        rig_part='chassis'
    lamp('Rear side scene light',(4.29,side*1.31,2.29),(.43,.07,.18),lens)
    lamp('Body amber marker',(-1.98,side*1.32,1.35),(.18,.04,.09),amber)
    cyl('Exhaust outlet',(.05,side*1.2,.44),.105,.24,chrome,'Y')
    cyl('Exhaust dark bore',(.05,side*1.327,.44),.079,.009,dark,'Y')
    # Continuous signature Z stripe, mirrored naturally in the opposite side view.
    path=[Vector((-2.24,1.0)),Vector((.40,1.0)),Vector((-.14,1.77)),Vector((6.0,1.77))]
    normals=[]
    for a,b in zip(path,path[1:]):
        d=(b-a).normalized();normals.append(Vector((-d.y,d.x)))
    miters=[normals[0]]
    for a,b in zip(normals,normals[1:]):
        n=(a+b).normalized();miters.append(n/max(.1,n.dot(b)))
    miters.append(normals[-1])
    for offset,h in [(0,.135),(-.104,.022),(.104,.022)]:
        edges=[[p+n*(offset+delta) for p,n in zip(path,miters)] for delta in [-h/2,h/2]]
        # A separate quad per segment avoids concave polygon triangulation artifacts.
        for i in range(3):
            pts=[edges[0][i],edges[0][i+1],edges[1][i+1],edges[1][i]]
            mesh('Reflective Z stripe',[(p.x,side*1.34,p.y) for p in pts],[(0,1,2,3)],white)
    text('Tail side unit','07',(5.49,side*1.35,1.18),.70,white,side)
    text('Service motto','Courage, Commitment and Compassion',(2.64,side*1.345,2.24),1.20,gold,side)
    for x in [-.97,-.66]:box('Wheel chock bracket',(x,side*.95,.45),(.24,.26,.15),alum)
box('Main roof deck',(.89,0,2.548),(6.32,2.52,.045),alum)
box('Rear turntable deck',(5.15,0,2.335),(2.08,2.53,.055),alum)

# Rear chevrons clipped as polygons to the rear face.
def clip(poly, axis, value, greater):
    result=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        ia=(a[axis]>=value) if greater else (a[axis]<=value)
        ib=(b[axis]>=value) if greater else (b[axis]<=value)
        if ia: result.append(a)
        if ia!=ib:
            t=(value-a[axis])/(b[axis]-a[axis]);result.append(tuple(a[k]+t*(b[k]-a[k]) for k in range(2)))
    return result
box('Rear fascia',(6.20,0,1.43),(.06,2.54,1.80),red)
for side in [-1,1]:
    for i in range(-5,8):
        z=i*.63
        poly=[(0,z),(side*1.27,z-1.27),(side*1.27,z-1.27+.30),(0,z+.30)]
        for axis,val,gt in [(0,-1.27,True),(0,1.27,False),(1,.53,True),(1,2.33,False)]:poly=clip(poly,axis,val,gt) if poly else []
        if poly:mesh('Rear reflective chevron',[(6.234,y,z) for y,z in poly],[tuple(range(len(poly)))],yellow)
box('Rear central shutter frame',(6.25,0,1.43),(.05,1.18,1.12),chrome,.01)
box('Rear central red shutter',(6.282,0,1.43),(.018,1.10,1.05),red,.003)
for j in range(28):box('Rear shutter groove',(6.294,0,.93+j*.036),(.003,1.08,.003),red,.001)
text('Rear LADDER','LADDER',(6.303,0,1.78),.91,white,2)
text('Rear 07','07',(6.305,0,1.43),.64,white,2)
text('Rear HOUSTON','HOUSTON',(6.303,0,1.06),.96,white,2)
box('Rear bumper',(6.25,0,.47),(.1746,2.66,.11),red,.012)
for side in [-1,1]:
    for z,m in [(.70,lens),(.84,lens),(.98,amber),(1.12,lampred)]:lamp('Rear stacked tail lamp',(6.27,side*1.07,z),(.055,.18,.13),m,'rear')
    lamp('Rear upper warning',(6.27,side*1.04,2.17),(.055,.27,.14),lens,'rear')
    beam('Rear grab handle',(6.30,side*1.24,1.15),(6.30,side*1.24,2.13),.015,chrome)
for y in [-.73,-.49,-.25,0,.25,.49,.73]:lamp('Rear traffic advisor',(6.27,y,2.28),(.035,.14,.045),amber,'rear')
cyl('Rear aerial inlet',(6.3,.74,.70),.12,.14,chrome,'X')
cyl('Rear inlet cap',(6.38,.74,.70),.096,.04,black,'X')
for y in [-.79,.79]:beam('Rear deck safety rail',(5.5,y,2.38),(5.5,y,3.33),.022,chrome)
beam('Rear deck safety rail top',(5.5,-.79,3.33),(5.5,.79,3.33),.022,chrome)
# Side access ladder at the tail corner.
for side in [-1,1]:
    for x in [5.99,6.27]:beam('Rear access ladder rail',(x,side*1.31,.59),(x,side*1.31,2.29),.022,chrome)
    for z in [.68,.96,1.24,1.52,1.80,2.08]:beam('Rear access step',(5.99,side*1.33,z),(6.27,side*1.33,z),.026,alum)

group='Aerial'
rig_part='turret'
cyl('Rear mount turntable',(4.68,0,2.42),.83,.12,dark,'Z',96)
cyl('Turntable top',(4.68,0,2.51),.78,.09,ladderwhite,'Z',96)
for side in [-1,1]:
    profile('Aerial pivot pedestal',[(3.78,2.58),(5.31,2.58),(4.22,3.42)],side*.63,side*.76,ladderwhite,.018)
    cyl('Elevation pivot',(4.24,side*.74,3.16),.14,.12,chrome,'Y')
    beam('Elevation cylinder',(4.92,side*.53,2.58),(4.28,side*.53,3.31),.095,ladderwhite)
    beam('Hydraulic piston',(4.48,side*.53,3.05),(4.21,side*.53,3.40),.05,chrome)
    text('Aerial reach','107',(4.45,side*.774,2.89),.25,dark,side)
box('Ladder cradle over cab',(-2.38,0,2.97),(.16,1.55,.22),red)
# Four nested truss sections, independent collections of beams and real open rungs.
for stage in range(4):
    rig_part='aerial_%s'%stage
    start=-5.66+stage*.13; end=4.40-stage*.29
    half=.59-stage*.105
    low=2.99+stage*.085
    high=3.63-stage*.055
    for side in [-1,1]:
        y=side*half
        box('Aerial section %s lower chord'%stage,((start+end)/2,y,low),(end-start,.065,.07),ladderwhite,.006)
        box('Aerial section %s top chord'%stage,((start+end)/2,y,high),(end-start,.045,.045),ladderwhite,.005)
        bays=18-stage
        for i in range(bays):
            x0=start+(end-start)*i/bays;x1=start+(end-start)*(i+1)/bays
            beam('Triangular aerial web',(x0,y,low),(x1,y,high),.019,ladderwhite)
            beam('Triangular aerial web',(x0,y,high),(x1,y,low),.019,ladderwhite)
    for i in range(31):
        x=start+(end-start)*i/30
        beam('Aerial rung',(x,-half,low),(x,half,low),.018,alum)
    if stage==0:
        for side in [-1,1]:
            box('Aerial lettering board',(-.6,side*.642,3.36),(3.55,.035,.40),ladderwhite,.006)
            text('HOUSTON LADDER 07','HOUSTON LADDER 07',(-.6,side*.664,3.36),3.34,red,side)
for stage in range(4):
    rig_part='aerial_%s'%stage
    beam('Aerial waterway',(-5.54+stage*.13,0,2.94),(4.44-stage*.29,0,2.94),.075-stage*.011,alum)
    cyl('Waterway coupling',(-5.54+stage*.13,0,2.94),.082-stage*.011,.10,chrome,'X')
# Tip cage, monitor and work lamps.
rig_part='aerial_3'
for side in [-1,1]:
    beam('Tip cage side',(-5.57,side*.57,3.02),(-6.01,side*.57,3.19),.018,red)
    beam('Tip cage upper',(-5.57,side*.57,3.63),(-6.01,side*.57,3.45),.018,red)
    beam('Tip cage end',(-6.01,side*.57,3.19),(-6.01,side*.57,3.45),.018,red)
    lamp('Tip floodlight',(-5.98,side*.49,3.07),(.10,.19,.17),lens,'front')
beam('Tip cage crossbar',(-6.01,-.57,3.19),(-6.01,.57,3.19),.023,black)
cyl('Monitor elbow',(-5.71,0,2.90),.11,.27,chrome,'Z')
beam('Monitor body',(-5.71,0,2.91),(-5.98,0,2.86),.095,alum)
cyl('Monitor nozzle',(-6.02,0,2.86),.075,.15,black,'X')
cyl('Nozzle rim',(-6.105,0,2.86),.082,.02,chrome,'X')
rig_part='turret'
box('Turntable control console',(5.08,-.72,2.73),(.42,.38,.37),alum,.018)
for x in [4.97,5.07,5.17]:beam('Control lever',(x,-.74,2.91),(x,-.77,3.03),.012,black)

# Studio isolated from the apparatus hierarchy.
group='Studio'
ground=mat('Studio floor',(.13,.16,.19),.05,.65)
box('Studio floor',(0,0,-.075),(200,200,.1),ground,.0)
world=bpy.data.worlds.new('L07 soft studio world');scene.world=world;world.use_nodes=True
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[0].default_value=(.26,.31,.39,1)
next(n for n in world.node_tree.nodes if n.type=='BACKGROUND').inputs[1].default_value=.45
def area(name,loc,power,size):
    d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size
    o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc
    o.rotation_euler=(Vector((0,0,1.5))-o.location).to_track_quat('-Z','Y').to_euler();finish(o,name)
area('Large left softbox',(-4,-7,10),2600,8)
area('Right rim softbox',(3,5,8),3100,7)
area('Front softbox',(-9,2,5),1800,5)
area('Rear softbox',(9,-3,6),1800,5)
def camera(name,loc,target,ortho):
    d=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,d);scene.collection.objects.link(o)
    o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
    d.type='ORTHO';d.ortho_scale=ortho;finish(o,name);return o
cam=camera('Ladder07 hero',(-11,-16,8),(0,0,1.7),16)
camera('Ladder07 left',(0,-20,1.83),(0,0,1.83),14)
camera('Ladder07 right',(0,20,1.83),(0,0,1.83),14)
camera('Ladder07 front',(-20,0,1.8),(0,0,1.8),5.2)
camera('Ladder07 rear',(20,0,1.8),(0,0,1.8),5.2)
scene.camera=cam
scene.render.resolution_x=1800;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
try:scene.render.engine='CYCLES'
except TypeError:pass
if hasattr(scene,'cycles'):
    scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.view_settings.view_transform='AgX'
for screen in bpy.data.screens:
    for a in screen.areas:
        if a.type=='VIEW_3D':
            a.spaces.active.region_3d.view_distance=17
            a.spaces.active.region_3d.view_location=Vector((0,0,1.7))
            a.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
            a.spaces.active.clip_end=1000
            a.spaces.active.shading.color_type='MATERIAL'
scene['README']='Dimensioned exterior model. Stowed 107 ft rear mount aerial. Separate wheels and ladder parts. Badges simplified; hidden mechanical details inferred. See build_ladder07.py for reproducible source.'
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True);bpy.context.view_layer.objects.active=root
OUT.mkdir(parents=True,exist_ok=True)
import sys
sys.path.insert(0,str(OUT))
from hfd_logo import create_decals
create_decals(scene,root,collections['Graphics'])
bpy.data.libraries.write(str(OUT/'Ladder07_Tagged_Source.blend'),{scene},path_remap='RELATIVE_ALL',fake_user=True,compress=True)
print('BUILT',len(scene.objects),'objects; saved',str(OUT/'Ladder07_Tagged_Source.blend'))
