"""Build the Houston District Chief 08 reference vehicle in a new Blender scene.

Run inside Blender. Dimensions are reference-derived approximations, in metres.
Vehicle forward is -X, up is +Z. Original scenes are retained.
"""
import bpy
import math
from mathutils import Vector, Matrix
from pathlib import Path

OUT = Path('J:/Unity/projects/HTownSim/Blender/HFD Apparatus/Chiefs/DistrictChief08')
OUT.mkdir(parents=True, exist_ok=True)
scene = bpy.data.scenes.new('HFD District Chief 08')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
vehicle = bpy.data.collections.new('DC08 | Vehicle')
scene.collection.children.link(vehicle)
studio = bpy.data.collections.new('DC08 | Presentation')
scene.collection.children.link(studio)
root = bpy.data.objects.new('DC08_CONTROLS', None)
vehicle.objects.link(root)
root.empty_display_size = .45
controls = {'emergency_red':1.0, 'scene_front':0.0, 'reverse_white':0.0,
            'fog_lights':0.0, 'headlights':0.0, 'green_command':0.0,
            'mirror_amber':0.0, 'arrow_mode':0, 'arrow_animate':1.0}
for key, value in controls.items():
    root[key] = value
    root.id_properties_ui(key).update(min=0, max=4 if key == 'arrow_mode' else 1,
        description=('0 off / 1 left / 2 right / 3 split / 4 all; play timeline' if key == 'arrow_mode' else '0 off / 1 on'))
root['unit'] = 'Houston Fire Department | District Chief 08'
root['reference_note'] = 'Reference-derived F-250 crew cab; dimensions approximate. Reference F-350 badge replaced with requested F-250.'

def mat(name, color, metal=0, rough=.4):
    m = bpy.data.materials.new('DC08 | '+name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    n = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value = (*color, 1)
    n.inputs['Metallic'].default_value = metal
    n.inputs['Roughness'].default_value = rough
    return m

red = mat('Fire red clearcoat', (.48,.012,.022), .32,.25)
next(n for n in red.node_tree.nodes if n.type == 'BSDF_PRINCIPLED').inputs['Coat Weight'].default_value = .45
black = mat('Satin black powdercoat',(.014,.019,.023),.35,.32)
rubber = mat('Tire rubber',(.012,.015,.019),0,.88)
trim = mat('Window rubber',(.009,.012,.015),0,.64)
chrome = mat('Polished aluminum and chrome',(.62,.68,.73),.92,.2)
steel = mat('Undercarriage steel',(.065,.075,.085),.75,.5)
white = mat('Reflective white',(.89,.92,.9),.15,.3)
yellow = mat('Fluorescent yellow lime chevrons',(.7,.95,.008),.05,.32)
glass = mat('Blue charcoal tinted glass',(.018,.049,.074),.5,.13)
mirror = mat('Mirror glass',(.5,.62,.7),1,.075)
blue = mat('Ford badge blue',(.008,.025,.10),.35,.25)
gold = mat('Badge gold',(.85,.55,.025),.65,.32)
clear = mat('Clear optical lens',(.58,.7,.76),.35,.2)
tailred = mat('Tail lamp red lens',(.45,.003,.008),.2,.22)

def driver(socket, key, expression='v*8'):
    d = socket.driver_add('default_value').driver
    d.expression = expression
    v = d.variables.new()
    v.name = 'v'
    v.type = 'SINGLE_PROP'
    v.targets[0].id = root
    v.targets[0].data_path = '["'+key+'"]'
    return d

def luminous(name, color, key, expression='v*8'):
    m = mat(name,color,.18,.24)
    n = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    n.inputs['Emission Color'].default_value = (*color,1)
    driver(n.inputs['Emission Strength'],key,expression)
    return m

redled = luminous('RED emergency LED', (1,.008,.012),'emergency_red')
frontwhite = luminous('WHITE front scene LED',(.86,.94,1),'scene_front','v*12')
reversewhite = luminous('WHITE reversing LED',(.86,.94,1),'reverse_white','v*10')
fogwhite = luminous('WHITE fog LED',(.92,.95,1),'fog_lights')
headwhite = luminous('WHITE headlamp',(.9,.94,1),'headlights')
amber = luminous('AMBER mirror indicator',(1,.24,.005),'mirror_amber')
green = luminous('GREEN command beacon',(.006,.65,.12),'green_command','v*10')
arrows = []
for i in range(6):
    m = luminous('AMBER arrow module %02d' % (i+1),(1,.26,.003),'arrow_mode','0')
    n = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    d = n.inputs['Emission Strength'].driver_add('default_value').driver
    a = d.variables.new(); a.name='a'; a.type='SINGLE_PROP'
    a.targets[0].id=root; a.targets[0].data_path='["arrow_animate"]'
    d.expression = f'9*((v==4) or (v==1 and (a<0.5 or (frame//4)%6>={i})) or (v==2 and (a<0.5 or (frame//4)%6>={5-i})) or (v==3 and (a<0.5 or (frame//4)%3>={min(i,5-i)})))'
    arrows.append(m)

def finish(o,name,m,bevel=0, parent=True):
    o.name=name
    for c in list(o.users_collection): c.objects.unlink(o)
    vehicle.objects.link(o)
    if parent: o.parent=root
    if m: o.data.materials.append(m)
    if bevel:
        b=o.modifiers.new('Manufactured edge radii','BEVEL'); b.width=bevel; b.segments=3
        n=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return o

def box(name,loc,size,m,bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object
    o.dimensions=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,m,bevel)

def mesh(name,verts,faces,m,bevel=0):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data); vehicle.objects.link(o); o.parent=root
    if m: data.materials.append(m)
    if bevel:
        b=o.modifiers.new('Edge radius','BEVEL'); b.width=bevel; b.segments=3
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o

def polyextrude(name,profile,ya,yb,m,bevel=.015):
    verts=[(x,y,z) for y in (ya,yb) for x,z in profile]
    n=len(profile)
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,2*n))]
    faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    return mesh(name,verts,faces,m,bevel)

def cyl(name,loc,r,depth,m,axis=(0,0,1),vertices=40):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=depth,location=loc)
    o=bpy.context.object; o.rotation_mode='QUATERNION'; o.rotation_quaternion=Vector(axis).to_track_quat('Z','Y')
    finish(o,name,m,.006)
    for p in o.data.polygons:p.use_smooth=True
    return o

def rod(name,a,b,r,m):
    a,b=Vector(a),Vector(b)
    return cyl(name,(a+b)/2,r,(b-a).length,m,b-a,20)

def torus(name,loc,major,minor,m,axis=(0,1,0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major,minor_radius=minor,major_segments=64,minor_segments=16,location=loc)
    o=bpy.context.object; o.rotation_mode='QUATERNION'; o.rotation_quaternion=Vector(axis).to_track_quat('Z','Y')
    finish(o,name,m)
    for p in o.data.polygons:p.use_smooth=True
    return o

font=bpy.data.fonts.load('C:/Windows/Fonts/ARIALNB.TTF')
def surface_rotation(u,v):
    u,v=Vector(u),Vector(v); n=u.cross(v)
    return Matrix((u,v,n)).transposed().to_quaternion()

def text(name,body,loc,size,m,u=(1,0,0),v=(0,0,1),width=None,outline=False):
    data=bpy.data.curves.new(name,'FONT'); data.body=body; data.font=font
    data.align_x='CENTER'; data.align_y='CENTER'; data.size=size; data.extrude=.0005
    o=bpy.data.objects.new(name,data); vehicle.objects.link(o); o.parent=root
    o.location=loc; o.rotation_mode='QUATERNION'; o.rotation_quaternion=surface_rotation(u,v)
    data.materials.append(m)
    bpy.context.view_layer.update()
    if width and o.dimensions.length:
        localwidth=max((p[0] for p in o.bound_box))-min((p[0] for p in o.bound_box))
        if localwidth>width:o.scale.x=width/localwidth
    if outline:
        d=data.copy(); d.offset=.005; d.materials.clear(); d.materials.append(black)
        q=bpy.data.objects.new(name+' | dark outline',d); vehicle.objects.link(q); q.parent=root
        q.location=Vector(loc)-Vector(u).cross(Vector(v))*.002
        q.rotation_mode='QUATERNION'; q.rotation_quaternion=o.rotation_quaternion; q.scale=o.scale
    return o

# Frame, axles and suspension. Four wheels remain separate assemblies.
for y in [-.62,.62]:box('Chassis rail',(.05,y,.56),(6.15,.13,.19),steel)
for x in [-2.12,2.08]:
    cyl('Solid axle',(x,0,.51),.075,2.08,steel,(0,1,0))
    box('Differential',(x,0,.51),(.3,.32,.27),steel,.10)
    for s in [-1,1]:
        for z in [.64,.665,.69]:box('Leaf spring',(x,s*.64,z),(1.1,.075,.018),steel,.005)
        rod('Shock absorber',(x-.18,s*.72,.51),(x+.19,s*.72,1.02),.035,chrome)
rod('Driveshaft',(-2.12,0,.5),(2.08,0,.5),.055,steel)

for x,label in [(-2.12,'Front'),(2.08,'Rear')]:
    for s,side in [(-1,'Driver'),(1,'Passenger')]:
        y=s*1.015; z=.525
        torus(label+' '+side+' tire',(x,y,z),.385,.135,rubber)
        # Broad center tread surface and molded shoulder tread blocks.
        cyl(label+' tire center',(x,y,z),.505,.24,rubber,(0,1,0),64)
        verts=[]; faces=[]
        for j in range(56):
            angle=j*2*math.pi/56
            for band in [-1,0,1]:
                a=angle+band*.018
                center=Vector((x+.505*math.sin(a),y+band*.088,z+.505*math.cos(a)))
                tangent=Vector((math.cos(a),0,-math.sin(a)))
                radial=Vector((math.sin(a),0,math.cos(a)))
                base=len(verts)
                for aa,bb,cc in [(-1,-1,-1),(-1,1,-1),(1,1,-1),(1,-1,-1),(-1,-1,1),(-1,1,1),(1,1,1),(1,-1,1)]:
                    verts.append(center+tangent*aa*.021+Vector((0,bb*.038,0))+radial*cc*.011)
                faces.extend([tuple(base+k for k in f) for f in [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]])
        mesh(label+' '+side+' tread blocks',verts,faces,rubber)
        facey=y+s*.143
        cyl(label+' wheel rim',(x,facey,z),.299,.055,chrome,(0,1,0),64)
        cyl(label+' rim recess',(x,facey+s*.031,z),.245,.012,steel,(0,1,0),64)
        cyl(label+' steel wheel dish',(x,facey+s*.04,z),.215,.024,chrome,(0,1,0),64)
        torus(label+' rolled rim lip',(x,facey+s*.032,z),.28,.016,chrome)
        for j in range(8):
            a=j*math.pi/4
            cyl('Wheel ventilation recess',(x+.185*math.sin(a),facey+s*.055,z+.185*math.cos(a)),.038,.007,black,(0,1,0),20)
            cyl('Wheel lug nut',(x+.109*math.sin(a),facey+s*.065,z+.109*math.cos(a)),.018,.027,chrome,(0,1,0),6)
        cyl('Hub cap',(x,facey+s*.067,z),.082,.065,chrome,(0,1,0))
        cyl('Hub badge',(x,facey+s*.104,z),.051,.006,blue,(0,1,0))
        text('Wheel tire branding','SUPER DUTY',(x,facey+s*.008,z+.37),.041,steel,(1 if s==-1 else -1,0,0))

# Lower side panels with true open wheel arches, not black circles on a box.
for s in [-1,1]:
    for xa,xb,cx,label in [(-3.17,-1.4,-2.12,'Front fender'),(1.01,3.25,2.08,'Bed quarter')]:
        profile=[(xa,1.49),(xb,1.49),(xb,.66),(cx+.61,.66)]
        profile += [(cx+.61*math.cos(t),.55+.66*math.sin(t)) for t in [j*math.pi/24 for j in range(25)]]
        profile += [(xa,.66)]
        polyextrude(label,profile,s*.88,s*1.04,red,.025)
        # Thin painted flare following arch.
        vs=[]
        for j in range(33):
            a=j*math.pi/32
            for r in [.615,.652]:vs.append((cx+r*math.cos(a),s*1.063,.55+(r+.05)*math.sin(a)))
        mesh(label+' arch lip',vs,[(2*j,2*j+1,2*j+3,2*j+2) for j in range(32)],red)
        box(label+' wheelwell shadow',(cx,s*.855,1.19),(1.0,.04,.13),trim)
    box('Crew cab rocker',(-.2,s*.99,.73),(2.5,.13,.21),red,.035)
    box('Running board',(-.13,s*1.13,.55),(2.8,.22,.10),black,.035)
    for x in [-1.05,.75]:rod('Step bracket',(x,s*.65,.58),(x,s*1.13,.53),.035,steel)
    for j in range(6):box('Step grip rib',(-.12,s*(1.06+j*.027),.607),(2.5,.01,.012),rubber,.003)
box('Cab lower volume',(-.2,0,1.1),(2.45,1.99,.77),red,.075)
box('Bed upper rail volume',(2.13,0,1.405),(2.24,2.05,.19),red,.025)
box('Bed front bulkhead',(1.1,0,1.1),(.12,2.0,.7),red)
box('Tailgate',(3.23,0,1.08),(.105,1.98,.75),red,.035)

# Hood and upper crew-cab silhouette.
polyextrude('Sculpted hood',[(-3.18,1.41),(-3.13,1.61),(-2.8,1.68),(-1.6,1.69),(-1.48,1.53)],-.98,.98,red,.055)
for s in [-1,1]:
    polyextrude('Hood raised shoulder',[(-3.03,1.64),(-2.74,1.70),(-1.66,1.72),(-1.57,1.68)],s*.70,s*.91,red,.018)
cabprofile=[(-1.6,1.43),(-1.57,1.67),(-.91,2.27),(-.67,2.33),(.83,2.33),(1.00,2.24),(1.03,1.43)]
polyextrude('Crew cab upper shell',cabprofile,-.98,.98,red,.055)
box('Cab roof',(.08,0,2.302),(1.64,1.9,.085),red,.055)
for s in [-1,1]:
    # Door glazing and B pillar. Side plane inset from drip rails.
    for label,p in [('Front door',[(-1.4,1.65),(-.84,2.205),(-.20,2.205),(-.20,1.65)]),
                    ('Rear door',[(-.10,1.65),(-.10,2.205),(.74,2.205),(.85,2.13),(.85,1.65)])]:
        polyextrude(label+' rubber surround',p,s*.982,s*1.003,trim,.028)
        center=Vector((sum(v[0] for v in p)/len(p),sum(v[1] for v in p)/len(p)))
        inset=[tuple(center+(Vector(v)-center)*.93) for v in p]
        polyextrude(label+' tinted glass',inset,s*1.004,s*1.009,glass,.014)
    # White body stripe with narrow red keylines and pinstripes.
    for xa,xb in [(-3.10,-1.5),(-1.48,-.16),(-.145,.98),(1.04,3.18)]:
        box('White side stripe',((xa+xb)/2,s*1.045,1.375),(xb-xa,.008,.175),white,.001)
        for z in [1.255,1.495]:box('White stripe pinline',((xa+xb)/2,s*1.046,z),(xb-xa,.009,.018),white,.001)
    for x in [-1.48,-.15,1.0]:box('Door vertical shut line',(x,s*1.046,1.07),(.009,.009,.77),trim,.002)
    for x in [-.48,.65]:
        box('Door handle recess',(x,s*1.056,1.437),(.27,.018,.065),black,.023)
        box('Door pull',(x-.015,s*1.079,1.451),(.18,.04,.027),black,.01)
    for x in [-.8,.40]:box('Door sill contour',(x,s*1.032,.83),(1.05,.017,.025),red,.01)
    # Tow mirrors: two arms, tall split housing, silver lower reflector and amber strip.
    for z in [1.65,1.73]:rod('Tow mirror arm',(-1.10,s*.99,z),(-1.17,s*1.30,z),.031,black)
    box('Tow mirror housing',(-1.18,s*1.40,1.79),(.22,.25,.39),black,.055)
    box('Mirror main glass',(-1.059,s*1.40,1.85),(.008,.205,.23),mirror,.018)
    box('Mirror convex lower glass',(-1.057,s*1.40,1.69),(.008,.205,.065),mirror,.012)
    box('Mirror amber signal lens',(-1.304,s*1.405,1.733),(.012,.19,.052),amber,.008)
    box('Mirror outer amber signal',(-1.20,s*1.531,1.733),(.14,.012,.05),amber,.008)
    for j in range(5):box('Mirror signal optic',(-1.314,s*(1.33+j*.034),1.734),(.008,.013,.03),amber,.003)
    # Vertical fender vent and requested F-250 badge.
    box('Fender vent',(-1.49,s*1.057,1.30),(.115,.025,.41),black,.015)
    text('F250 fender badge','F-250',(-1.49,s*1.077,1.39),.055,chrome,(1 if s==-1 else -1,0,0),width=.11)
    box('Fuel filler' if s==-1 else 'Bed access panel',(1.32,s*1.065,1.11),(.23,.012,.21),red,.03)
    box('Rear bedside step',(1.25,s*1.06,.75),(.34,.10,.20),black,.04)

# Front windshield plane follows the rake; visible wipers at its base.
mesh('Windshield rubber',[(-1.55,-.93,1.69),(-1.55,.93,1.69),(-.90,.87,2.255),(-.90,-.87,2.255)],[(0,1,2,3)],trim)
mesh('Panoramic windshield',[(-1.546,-.87,1.72),(-1.546,.87,1.72),(-.938,.83,2.216),(-.938,-.83,2.216)],[(0,1,2,3)],glass)
for y in [-.43,.42]:rod('Windshield wiper',(-1.566,y-.28,1.735),(-1.55,y+.26,1.75),.012,black)
rod('Radio whip antenna',(-1.65,.67,1.73),(-1.64,.67,2.53),.006,black)

# Camper with side lift-up compartment doors, rubber seals and chrome paddle latches.
polyextrude('Commercial camper shell',[(1.06,1.51),(1.06,2.31),(1.15,2.38),(3.20,2.38),(3.29,2.29),(3.29,1.51)],-1.02,1.02,red,.035)
box('Camper rounded roof',(2.17,0,2.366),(2.24,2.08,.085),red,.04)
for s in [-1,1]:
    box('Camper side door gasket',(2.17,s*1.034,1.969),(2.12,.024,.674),black,.022)
    box('Camper side lift-up door',(2.17,s*1.052,1.969),(2.055,.018,.616),red,.012)
    box('Camper continuous hinge',(2.17,s*1.073,2.287),(2.14,.04,.026),black,.008)
    box('Camper latch plate',(2.17,s*1.078,1.763),(.14,.024,.105),chrome,.012)
    box('Camper paddle handle',(2.17,s*1.094,1.769),(.085,.009,.044),steel,.006)
    u=(1 if s==-1 else -1,0,0)
    # Unit number goes to the rear on each side, matching the source layout.
    text('Side district chief','DISTRICT CHIEF',(1.91,s*1.074,2.025),.213,white,u,width=1.49,outline=True)
    text('Side unit number','08',(2.96,s*1.075,2.02),.47,white,u,width=.42,outline=True)
    text('Bed HOUSTON FIRE','HOUSTON FIRE',(2.14,s*1.060,1.377),.143,red,u,width=1.22,outline=True)

# Rear face: chevron bands clipped into the camper panel and tailgate stripe.
def clip(poly,axis,bound,greater):
    out=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        ia=(a[axis]>=bound) if greater else (a[axis]<=bound)
        ib=(b[axis]>=bound) if greater else (b[axis]<=bound)
        if ia:out.append(a)
        if ia!=ib:
            t=(bound-a[axis])/(b[axis]-a[axis]);out.append(tuple(a[k]+t*(b[k]-a[k]) for k in range(2)))
    return out

def chevrons(name,x,zlo,zhi,halfwidth):
    for side in [-1,1]:
        for j in range(-5,8):
            lo=j*.52; hi=lo+.26
            # Rising towards center forms the upward-pointing rear safety chevron.
            p=[(0,lo),(halfwidth,lo-halfwidth),(halfwidth,hi-halfwidth),(0,hi)]
            p=clip(clip(p,1,zlo,True),1,zhi,False)
            if len(p)>=3:mesh(name,[(x,side*y,z) for y,z in p],[tuple(range(len(p)))],yellow)

box('Rear camper door gasket',(3.303,0,1.94),(.025,2.0,.79),black,.02)
box('Rear camper door',(3.32,0,1.95),(.018,1.91,.72),red,.01)
chevrons('Camper reflective chevron',3.332,1.60,2.30,.945)
chevrons('Tailgate reflective chevron',3.288,1.08,1.36,.94)
box('Rear camper bottom sill',(3.35,0,1.548),(.08,2.07,.055),black,.012)
box('Rear top light rail',(3.30,0,2.395),(.15,2.10,.11),black,.04)
box('Camper rear paddle latch',(3.35,0,1.69),(.033,.18,.125),chrome,.016)
box('Camper latch recess',(3.371,0,1.70),(.012,.113,.071),steel,.012)
text('Rear district chief','DISTRICT CHIEF',(3.348,0,2.13),.205,white,(0,1,0),width=1.63,outline=True)
text('Rear unit number','08',(3.35,0,1.875),.34,white,(0,1,0),outline=True)
text('Rear Houston Fire','HOUSTON FIRE',(3.296,0,.884),.151,white,(0,1,0),width=1.25,outline=True)
text('Tailgate emboss','S U P E R   D U T Y',(3.294,0,1.025),.048,red,(0,1,0),width=1.5)
box('Tailgate handle recess',(3.308,0,1.47),(.04,.45,.10),black,.03)
box('Tailgate handle',(3.334,0,1.47),(.015,.25,.035),steel,.01)

# LED panel helper: mounting bezel, optical cover and individual diode optics.
def panel(name,center,u,v,width,height,material,cols=6,rows=2):
    center=Vector(center); u=Vector(u); v=Vector(v); n=u.cross(v)
    rot=surface_rotation(u,v)
    for suffix,w,h,d,offset,m in [('bezel',width+.027,height+.026,.025,0,black),('lens',width,height,.012,.018,clear)]:
        o=box(name+' '+suffix,center+n*offset,(w,h,d),m,min(height*.23,.012))
        o.rotation_mode='QUATERNION';o.rotation_quaternion=rot
    for col in range(cols):
        for row in range(rows):
            p=center+n*.027+u*((col+.5)/cols-.5)*width*.89+v*((row+.5)/rows-.5)*height*.7
            o=box(name+' diode',p,(width*.64/cols,height*.50/rows,.008),material,.004)
            o.rotation_mode='QUATERNION';o.rotation_quaternion=rot

# Six amber arrow modules, central white work lamp and four red corner modules.
for i,y in enumerate([-.84,-.56,-.28,.28,.56,.84]):
    panel('Arrow AMBER %02d'%(i+1),(3.389,y,2.397),(0,1,0),(0,0,1),.19,.046,arrows[i],6,1)
panel('Rear upper WHITE work lamp',(3.391,0,2.397),(0,1,0),(0,0,1),.24,.05,reversewhite,8,1)
for s in [-1,1]:
    for z in [1.77,2.12]:panel('Rear corner RED',(3.355,s*.985,z),(0,0,1),(0,-1,0),.16,.047,redled,6,1)
    panel('Tailgate RED',(3.327,s*.58,1.22),(0,1,0),(0,0,1),.20,.071,redled)
    for x,z in [(-2.61,1.49),(2.90,1.16)]:panel('Side RED',(x,s*1.079,z),(1 if s==-1 else -1,0,0),(0,0,1),.18,.065,redled)

# Green command beacon, explicitly controlled from DC08_CONTROLS.
cyl('Green beacon base',(2.94,0,2.445),.093,.035,black)
cyl('Green beacon lens',(2.94,0,2.51),.075,.10,green,vertices=48)
for z in [2.48,2.505,2.53]:torus('Beacon lens ring',(2.94,0,z),.075,.003,clear,(0,0,1))
cyl('Beacon rounded cap',(2.94,0,2.566),.065,.014,green)

# Roof emergency bar with alternating red and clear optics.
for y in [-.61,.61]:box('Lightbar foot',(-.18,y,2.38),(.20,.16,.10),black)
box('Roof lightbar chassis',(-.18,0,2.455),(.29,1.63,.07),chrome,.025)
for j in range(10):
    y=-.727+j*.162
    panel('Roof front RED',(-.337,y,2.502),(0,-1,0),(0,0,1),.143,.067,redled,5,2)
    panel('Roof rear RED',(-.022,y,2.502),(0,1,0),(0,0,1),.143,.067,redled,5,2)
box('Roof lightbar clear lid',(-.18,0,2.55),(.29,1.65,.018),clear,.009)

# Front fascia: chromed surround, black slatted grille, stacked lamps, fogs.
box('Front chrome fascia',(-3.185,0,1.25),(.10,2.03,.64),chrome,.065)
box('Black grille surround',(-3.25,0,1.28),(.065,1.54,.49),black,.055)
for z in [1.08,1.18,1.29,1.40,1.50]:
    box('Grille horizontal slat',(-3.29,0,z),(.035,1.48,.028),steel,.008)
for y in [-.63,-.42,-.21,0,.21,.42,.63]:box('Grille vertical web',(-3.28,y,1.29),(.025,.023,.42),black,.005)
for s in [-1,1]:
    box('Headlamp chrome housing',(-3.25,s*.884,1.29),(.12,.27,.56),chrome,.034)
    for z in [1.12,1.45]:
        box('Headlamp reflector',(-3.322,s*.884,z),(.012,.22,.19),clear,.018)
        cyl('Headlamp projector',(-3.338,s*.884,z),.068,.015,headwhite,(-1,0,0),32)
        for yy in [-.075,.075]:rod('Headlamp reflector flute',(-3.337,s*.884+yy,z-.075),(-3.337,s*.884+yy,z+.075),.006,chrome)
    box('Front amber turn signal',(-3.334,s*.884,1.29),(.018,.22,.073),amber,.01)
box('Front chrome bumper',(-3.24,0,.885),(.21,2.10,.28),chrome,.045)
box('Front bumper black lower valance',(-3.24,0,.697),(.17,2.02,.13),black,.025)
for s in [-1,1]:
    box('Fog lamp black inset',(-3.354,s*.84,.895),(.017,.31,.17),black,.03)
    cyl('Fog lamp chrome ring',(-3.37,s*.84,.90),.071,.018,chrome,(-1,0,0))
    cyl('Round fog light',(-3.382,s*.84,.90),.055,.018,fogwhite,(-1,0,0))
    rod('Front tow loop',(-3.37,s*.39,.79),(-3.49,s*.39,.79),.026,steel)
text('Hood SUPER DUTY','S U P E R   D U T Y',(-3.21,0,1.62),.052,red,(0,-1,0),width=1.22)

# Grille guard uprights are tall swept plates; two red lights and white scene bar.
for s in [-1,1]:
    polyextrude('Grille guard push upright',[(-3.55,.60),(-3.64,.67),(-3.64,1.62),(-3.59,1.72),(-3.51,1.70),(-3.45,1.51),(-3.46,.64)],s*.66-.035,s*.66+.035,black,.025)
    for z in [.73,1.47]:cyl('Guard attachment bolt',(-3.647,s*.66,z),.012,.008,chrome,(-1,0,0),6)
for z in [.68,1.03,1.59]:rod('Grille guard cross tube',(-3.53,-.66,z),(-3.53,.66,z),.036,black)
for s in [-1,1]:panel('Grille RED panel',(-3.56,s*.36,1.43),(0,-1,0),(0,0,1),.22,.085,redled,8,2)
panel('Front WHITE scene bar',(-3.57,0,1.15),(0,-1,0),(0,0,1),.67,.093,frontwhite,20,2)
for s in [-1,1]:
    cyl('Siren speaker',(-3.47,s*.49,1.16),.087,.08,black,(-1,0,0))
    for j in range(5):box('Siren grille slit',(-3.517,s*.49,1.11+j*.025),(.008,.12,.008),steel,.001)
box('Front license bracket',(-3.58,0,.81),(.035,.35,.18),black,.014)
text('Front plate unit','DC 08',(-3.601,0,.82),.052,white,(0,-1,0))

# Oval Ford badges front and rear.
for x,z,u in [(-3.339,1.30,(0,-1,0)),(3.328,1.22,(0,1,0))]:
    o=cyl('Ford oval chrome',(x,0,z),1,.013,chrome,(-1 if x<0 else 1,0,0),64)
    o.scale=(.165,.069,1)
    o=cyl('Ford oval blue',(x+(-.01 if x<0 else .01),0,z),1,.014,blue,(-1 if x<0 else 1,0,0),64)
    o.scale=(.147,.057,1)
    text('Ford script','Ford',(x+(-.022 if x<0 else .022),0,z),.070,white,u,width=.23)

# Tail lamps include separate white reversing sections; auxiliary bumper lights.
for s in [-1,1]:
    box('Rear tail lamp housing',(3.28,s*1.025,1.073),(.14,.16,.58),black,.035)
    box('Rear red tail lens',(3.366,s*1.025,1.073),(.055,.135,.54),tailred,.025)
    box('Tail lamp reverse section',(3.399,s*1.025,1.04),(.012,.085,.18),reversewhite,.01)
    for z in [.84,.89,1.24,1.29]:box('Tail lamp optical rib',(3.404,s*1.025,z),(.008,.11,.009),tailred,.003)
box('Rear bumper black step',(3.36,0,.73),(.30,2.17,.10),black,.03)
for s in [-1,1]:
    box('Rear chrome bumper wing',(3.40,s*.69,.62),(.23,.76,.22),chrome,.035)
    panel('Bumper WHITE reverse',(3.529,s*.58,.635),(0,1,0),(0,0,1),.095,.075,reversewhite,4,3)
    for j in range(9):box('Bumper step grip',(3.35+j*.017,s*.70,.786),(.009,.66,.008),rubber,.002)
box('Rear license recess',(3.40,0,.61),(.15,.57,.21),black,.02)
text('Rear plate','TEXAS  DC08',(3.487,0,.62),.043,white,(0,1,0),width=.34)
box('Receiver hitch crossmember',(3.24,0,.39),(.17,1.1,.12),steel)
box('Tow receiver',(3.46,0,.37),(.32,.13,.13),black)
box('Hitch socket',(3.627,0,.37),(.006,.083,.083),trim,.001)
for s in [-1,1]:torus('Safety chain loop',(3.48,s*.18,.38),.05,.011,steel,(1,0,0))
rod('Exhaust pipe',(2.65,.65,.43),(3.49,1.14,.43),.065,steel)
cyl('Exhaust polished tip',(3.45,1.12,.43),.088,.26,chrome,(.8,.55,0))
cyl('Exhaust dark opening',(3.56,1.195,.43),.071,.008,trim,(.8,.55,0))

# Photographic door insignia: original reference patches, with packed source image.
ref=bpy.data.images.load('J:/Unity/projects/HTownSim/Sample-Asset-Images/DSC00518_watermark_resize_2500_2500_closure2_insert_bottom_right.jpg',check_existing=True)
ref.pack()
def badge_patch(name,x,z,w,h,uvrect,s):
    y=s*1.062; u=(1 if s==-1 else -1)
    verts=[(x-u*w/2,y,z-h/2),(x+u*w/2,y,z-h/2),(x+u*w/2,y,z+h/2),(x-u*w/2,y,z+h/2)]
    o=mesh(name,verts,[(0,1,2,3)],None)
    m=mat(name+' photographic decal',(.6,.02,.025),.05,.45)
    nodes=m.node_tree.nodes; bs=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    tex=nodes.new('ShaderNodeTexImage');tex.image=ref
    m.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    o.data.materials.append(m)
    xa,ya,xb,yb=uvrect
    uv=[(xa/1628,1-yb/539),(xb/1628,1-yb/539),(xb/1628,1-ya/539),(xa/1628,1-ya/539)]
    layer=o.data.uv_layers.new(name='Reference crop UV')
    for i in range(4):layer.data[i].uv=uv[i]
for s in [-1,1]:
    badge_patch('Houston Fire EMS Rescue door shield',-.91,1.29,.31,.38,(550,213,639,328),s)
    badge_patch('Houston Fire District Chief door crest',.39,1.29,.32,.36,(819,222,914,328),s)

# Presentation, cameras and render settings live in their own collection.
def presentation(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    studio.objects.link(o);o.parent=None
    return o
floor=box('Studio floor',(0,0,-.055),(200,200,.08),mat('Studio ground',(.065,.085,.105),.1,.65),0)
presentation(floor)
world=bpy.data.worlds.new('DC08 neutral studio world');world.use_nodes=True
world.node_tree.nodes.get('Background').inputs[0].default_value=(.28,.34,.44,1)
world.node_tree.nodes.get('Background').inputs[1].default_value=.5
scene.world=world
def area(name,loc,power,size,color):
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=color
    o=bpy.data.objects.new(name,data);studio.objects.link(o);o.location=loc
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
area('Key softbox',(-3,-4,7),1800,6,(1,.91,.82))
area('Fill softbox',(0,5,5),1500,5,(.76,.86,1))
area('Rear rim',(5,-1,6),2100,4,(1,1,1))
def camera(name,loc,target,lens=52):
    data=bpy.data.cameras.new(name);o=bpy.data.objects.new(name,data);studio.objects.link(o)
    o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();data.lens=lens
    return o
frontcam=camera('DC08 Front three-quarter',(-9.6,-9.2,5.3),(0,0,1.15),53)
rearcam=camera('DC08 Rear three-quarter',(9.5,8.5,4.8),(.2,0,1.2),53)
camera('DC08 Rear detail',(8.8,0,2.5),(3.2,0,1.45),58)
camera('DC08 Driver profile',(0,-11,2.8),(0,0,1.2),50)
scene.camera=frontcam
try:scene.render.engine='CYCLES'
except TypeError:pass
scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.fps=24;scene.frame_start=1;scene.frame_end=144
scene.frame_set(1)
scene.render.filepath=str(OUT/'DC08_Front.png')
notes=bpy.data.texts.new('DC08_README')
notes.write('''HOUSTON FIRE DEPARTMENT — DISTRICT CHIEF 08
Select DC08_CONTROLS > Object Properties > Custom Properties.
All lamp controls: 0 off, 1 on. Green command beacon is independently toggleable.
arrow_mode: 0 off, 1 left, 2 right, 3 split, 4 all amber.
Play timeline for progressive arrow sequencing. arrow_animate=0 gives steady illumination.
Arrow direction is as seen looking at the rear of the truck.
reverse_white controls both tail lamp reversing sections, two bumper work lights, and rear upper work light.
scene_front controls the large white grille guard scene light.
Emergency lights, mirrors, fog lamps and headlights have independent controls.
All light controls drive emissive materials; these are Blender controls, not a Unity controller.
Vehicle forward is -X; dimensions are approximated from supplied photographs.
Reference shows F-350 fender badging; modeled as requested F-250.
Original source scene retained. Studio geometry is separate from vehicle collection.
Door crests are packed photographic crops; other lettering and chevrons are geometry.
This is an exterior model; no detailed interior, door rig or engine-ready LOD/bakes.
''')
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True);bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for ar in screen.areas:
        if ar.type=='VIEW_3D':
            ar.spaces.active.region_3d.view_distance=9
            ar.spaces.active.region_3d.view_location=(0,0,1.1)
            ar.spaces.active.region_3d.view_rotation=frontcam.rotation_euler.to_quaternion()
            ar.spaces.active.clip_end=300
exec(compile(open('J:/Unity/projects/HTownSim/AgentScripts/RefineDistrictChief08.py', encoding='utf-8').read(), 'RefineDistrictChief08.py', 'exec'))
exec(compile(open('J:/Unity/projects/HTownSim/AgentScripts/UpdateDistrictChief08Lightbar.py', encoding='utf-8').read(), 'UpdateDistrictChief08Lightbar.py', 'exec'))
exec(compile(open('J:/Unity/projects/HTownSim/AgentScripts/SeatDistrictChief08Details.py', encoding='utf-8').read(), 'SeatDistrictChief08Details.py', 'exec'))
exec(compile(open('J:/Unity/projects/HTownSim/AgentScripts/UpdateDistrictChief08Logo.py', encoding='utf-8').read(), 'UpdateDistrictChief08Logo.py', 'exec'))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HFD_District_Chief_08.blend'))
print('SAVED',OUT/'HFD_District_Chief_08.blend')
print('Vehicle objects:',len(vehicle.objects),'Materials:',len(bpy.data.materials))
