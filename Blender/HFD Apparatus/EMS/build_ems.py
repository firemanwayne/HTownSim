import bpy, math, os, json
from mathutils import Vector, Matrix
from math import pi, sin, cos
OUT=os.path.dirname(os.path.abspath(__file__))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
sc=bpy.context.scene; sc.unit_settings.system='METRIC'; sc.render.engine='CYCLES'; sc.cycles.samples=32; sc.cycles.use_denoising=True
sc.render.resolution_x=1600; sc.render.resolution_y=1100; sc.render.resolution_percentage=100
sc.render.fps=30; sc.frame_start=1; sc.frame_end=240; sc.world.color=(.18,.18,.18); sc.view_settings.view_transform='AgX'
def coll(n):
 c=bpy.data.collections.new(n); sc.collection.children.link(c); return c
body=coll('01 Vehicle'); cabin=coll('02 Cab interior'); wheelcoll=coll('03 Wheels and steering'); lamps=coll('04 Working lights'); studio=coll('90 Studio - excluded from export'); active=body
def empty(n,loc=(0,0,0),parent=None):
 o=bpy.data.objects.new(n,None); active.objects.link(o); o.location=loc; o.parent=parent; return o
root=empty('MEDIC 07 - CONTROLS'); root.empty_display_size=.7
defaults={'Demo':1.0,'Headlights':0.0,'Brake':0.0,'Emergency':0.0,'Travel_m':0.0,'Steering_deg':0.0,'Driver_door_deg':0.0,'Passenger_door_deg':0.0}
for k,v in defaults.items():
 root[k]=v; root.id_properties_ui(k).update(min=-100 if k in ('Travel_m','Steering_deg') else 0,max=1 if k in ('Demo','Headlights','Brake','Emergency') else 90,description={'Demo':'1: timeline demonstration. 0: manual controls.','Travel_m':'Wheel spin = travel divided by 0.48 metre radius; move root separately.','Brake':'Brake lights, independent of headlights.','Emergency':'Alternating red and white double-pulse flashers.'}.get(k,k))
def drv(owner,path,expr,props,index=None):
 f=owner.driver_add(path) if index is None else owner.driver_add(path,index); d=f.driver; d.type='SCRIPTED'; d.expression=expr
 for sym,prop in props.items():
  v=d.variables.new(); v.name=sym; v.type='SINGLE_PROP'; v.targets[0].id=root; v.targets[0].data_path='["'+prop+'"]'
def material(n,c,metal=0,rough=.35):
 m=bpy.data.materials.new(n); m.diffuse_color=(*c,1); m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*c,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
 return m
red=material('Apparatus red',(.48,.012,.022),.35,.26); white=material('White reflective stripe',(.9,.92,.9),.2,.27)
chrome=material('Polished aluminium',(.65,.72,.78),.95,.2); black=material('Moulded black',(.012,.018,.023),0,.35); rubber=material('Tire rubber',(.009,.012,.016),0,.78)
blue=material('HFD blue',(.015,.06,.24),.2,.3); yellow=material('Safety yellow',(.8,.95,.025),.1,.3); gold=material('Badge gold',(.8,.49,.08),.55,.25)
fabric=material('Seat fabric',(.035,.05,.065),0,.85); dash=material('Dashboard',(.027,.033,.04),0,.6)
glass=material('Clear glazing',(.65,.8,.88),0,.06); p=glass.node_tree.nodes.get('Principled BSDF'); p.inputs['Transmission Weight'].default_value=1; p.inputs['IOR'].default_value=1.45
plate=material('Aluminium tread plate',(.46,.5,.54),.88,.32)
# Procedural crossed-wave bump provides repeating raised diamond tread.
nt=plate.node_tree; p=nt.nodes.get('Principled BSDF'); coord=nt.nodes.new('ShaderNodeTexCoord'); mapping=nt.nodes.new('ShaderNodeVectorMath'); mapping.operation='SCALE'; mapping.inputs[3].default_value=1
wave=nt.nodes.new('ShaderNodeTexWave'); wave.wave_type='BANDS'; wave.bands_direction='DIAGONAL'; wave.inputs['Scale'].default_value=65
nt.links.new(coord.outputs['Object'],wave.inputs['Vector']); bump=nt.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.3; bump.inputs['Distance'].default_value=.007; nt.links.new(wave.outputs['Color'],bump.inputs['Height']); nt.links.new(bump.outputs['Normal'],p.inputs['Normal'])
def mesh(n,verts,faces,m,parent=root,bev=0):
 me=bpy.data.meshes.new(n); me.from_pydata(verts,[],faces); me.update(); o=bpy.data.objects.new(n,me); active.objects.link(o); o.parent=parent
 if m: me.materials.append(m)
 if bev:
  b=o.modifiers.new('Manufactured edge radius','BEVEL'); b.width=bev; b.segments=3
  o.modifiers.new('Corner normals','WEIGHTED_NORMAL')
 return o
cube_faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
def box(n,loc,size,m,bev=.02,parent=root):
 x,y,z=[a/2 for a in size]; vs=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
 o=mesh(n,vs,cube_faces,m,parent,bev); o.location=loc; return o
def cyl(n,loc,r,h,m,axis='Z',parent=root,N=32):
 vs=[(r*cos(i*2*pi/N),r*sin(i*2*pi/N),z) for z in (-h/2,h/2) for i in range(N)]
 fs=[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)]
 o=mesh(n,vs,fs,m,parent,.005); o.location=loc
 if axis=='X':o.rotation_euler[1]=pi/2
 if axis=='Y':o.rotation_euler[0]=pi/2
 for p in o.data.polygons:
  if len(p.vertices)==4:p.use_smooth=True
 return o
def rod(n,a,b,r,m,parent=root):
 a,b=Vector(a),Vector(b); o=cyl(n,(a+b)/2,r,(b-a).length,m,parent=parent,N=16); o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler(); return o
def torus(n,loc,R,r,m,axis='X',parent=root):
 N,M=48,12; vs=[((R+r*cos(j*2*pi/M))*cos(i*2*pi/N),(R+r*cos(j*2*pi/M))*sin(i*2*pi/N),r*sin(j*2*pi/M)) for i in range(N) for j in range(M)]
 fs=[(i*M+j,((i+1)%N)*M+j,((i+1)%N)*M+(j+1)%M,i*M+(j+1)%M) for i in range(N) for j in range(M)]
 o=mesh(n,vs,fs,m,parent); o.location=loc
 if axis=='X':o.rotation_euler[1]=pi/2
 if axis=='Y':o.rotation_euler[0]=pi/2
 for p in o.data.polygons:p.use_smooth=True
 return o
def txt(n,string,loc,height,m,side='rear',parent=root):
 cu=bpy.data.curves.new(n,'FONT'); cu.body=string; cu.size=height; cu.align_x='CENTER'; cu.align_y='CENTER'; cu.extrude=.0008
 o=bpy.data.objects.new(n,cu); active.objects.link(o); cu.materials.append(m); o.parent=parent; o.location=loc
 u=Vector({'left':(0,-1,0),'right':(0,1,0),'front':(1,0,0),'rear':(-1,0,0)}[side]); v=Vector((0,0,1))
 o.rotation_euler=Matrix((u,v,u.cross(v))).transposed().to_euler(); return o
def profile(n,pts,x0,x1,m,bev=.02):
 N=len(pts); vs=[(x,y,z) for x in (x0,x1) for y,z in pts]; fs=[tuple(range(N-1,-1,-1)),tuple(range(N,2*N))]+[(i,(i+1)%N,(i+1)%N+N,i+N) for i in range(N)]
 return mesh(n,vs,fs,m,root,bev)
print('Building body',flush=True)
box('Ladder chassis',(0,.1,.48),(1.75,6.8,.24),black)
module=box('Patient module',(0,1.73,2.04),(2.3,3.64,2.47),red,.045)
box('Module roof cap',(0,1.73,3.3),(2.34,3.69,.09),chrome)
box('Cab floor',(0,-.9,.93),(1.96,1.82,.12),black); box('Cab rear wall',(0,-.1,1.69),(1.94,.09,1.55),red)
box('Cab roof',(0,-.85,2.69),(2.02,1.58,.13),red,.075)
profile('Contoured bonnet',[(-3.46,1.34),(-3.46,1.66),(-3.08,1.78),(-2.05,1.85),(-1.85,1.72),(-1.98,1.32)],-.96,.96,red,.065)
box('Front fascia',(0,-3.49,1.17),(1.98,.16,.78),red,.05)
box('Chrome bumper',(0,-3.6,.72),(2.16,.29,.38),chrome,.07)
box('Bumper intake',(0,-3.757,.76),(1.05,.025,.15),black,.04)
box('Grille surround',(0,-3.59,1.31),(1.39,.12,.59),chrome)
box('Grille',(0,-3.659,1.31),(1.29,.025,.48),black,.005)
for z in (1.13,1.31,1.49):box('Grille bar',(0,-3.681,z),(1.29,.018,.025),chrome,.003)
for i in range(13):box('Grille mesh',(-.57+i*.095,-3.679,1.31),(.012,.015,.45),chrome,.002)
box('Bowtie',(0,-3.709,1.32),(.27,.035,.09),gold,.005); box('Bowtie centre',(0,-3.711,1.32),(.12,.04,.15),gold,.005)
fenders=[]
for s in (-1,1):
 fenders.append(profile('Fender shoulder',[(-3.43,.86),(-3.43,1.62),(-1.82,1.77),(-1.72,.86)],min(s*.82,s*1.02),max(s*.82,s*1.02),red,.025))
 for i in range(18):
  a,b=pi*i/18,pi*(i+1)/18
  rod('Fender arch',(s*1.045,-2.55+.60*cos(a),.56+.60*sin(a)),(s*1.045,-2.55+.60*cos(b),.56+.60*sin(b)),.035,black)
 hinge=empty(('Driver' if s<0 else 'Passenger')+' door hinge',(s*.98,-1.74,1.3),root); pieces=[]
 pieces += [box('Door lower',(s*.98,-.96,1.31),(.085,1.47,.76),red),box('Door white stripe',(s*1.027,-.96,1.57),(.012,1.45,.26),white,.004)]
 for a,b in [((-1.72,1.72),(-1.36,2.63)),((-1.36,2.63),(-.22,2.63)),((-.22,2.63),(-.22,1.72)),((-.22,1.72),(-1.72,1.72))]:pieces.append(rod('Door window frame',(s*.985,*a),(s*.985,*b),.038,red))
 pieces.append(mesh('Cab door glass',[(s*.99,-1.65,1.78),(s*.99,-1.31,2.57),(s*.99,-.28,2.57),(s*.99,-.28,1.78)],[(0,1,2,3)],glass))
 pieces += [box('Door handle',(s*1.05,-.45,1.62),(.06,.20,.05),black,.02),box('Inner door panel',(s*.925,-.92,1.4),(.035,1.3,.44),dash,.03),box('Armrest',(s*.87,-.88,1.49),(.12,.55,.08),black)]
 pieces += [rod('Tow mirror arm',(s*.98,-1.58,1.92),(s*1.34,-1.65,2.04),.035,black),box('Tow mirror',(s*1.35,-1.65,2.08),(.17,.3,.31),black,.04),box('Mirror face',(s*1.35,-1.487,2.08),(.125,.012,.25),chrome,.012)]
 side='left' if s<0 else 'right'
 pieces.append(cyl('Door shield',(s*1.043,-.97,1.24),.14,.014,gold,'X'))
 pieces.append(txt('Door badge','HFD',(s*1.057,-.97,1.25),.065,blue,side))
 for o in pieces:o.parent=hinge; o.location-=hinge.location
 drv(hinge,'rotation_euler',str(-s)+'*d*pi/180',{'d':'Driver_door_deg' if s<0 else 'Passenger_door_deg'},2)
 box('Cab step',(s*1.04,-.8,.75),(.24,1.65,.1),plate,.03)
 box('Module white stripe',(s*1.157,1.72,1.45),(.018,3.6,.30),white,.004)
 box('Module lower rail',(s*1.166,1.72,.81),(.025,3.5,.055),chrome,.005)
 for y,w in ((.27,.59),(1.02,.78),(2.98,.94)):
  box('Compartment gasket',(s*1.165,y,1.04),(.016,w,.42),black,.012); box('Compartment door',(s*1.177,y,1.04),(.012,w-.035,.385),red,.01)
  cyl('Recessed latch',(s*1.19,y,1.10),.055,.018,chrome,'X'); box('Latch grip',(s*1.202,y,1.10),(.015,.06,.015),black,.002)
 for y in (1.48,2.66):box('Wheel surround plate',(s*1.17,y,1.02),(.035,.28,.48),plate,.008)
 for i in range(22):
  a,b=pi*i/22,pi*(i+1)/22
  rod('Rear arch',(s*1.18,2.08+.56*cos(a),.55+.56*sin(a)),(s*1.18,2.08+.56*cos(b),.55+.56*sin(b)),.035,chrome)
 box('Mud flap',(s*.99,2.67,.38),(.35,.05,.47),rubber,.01)
 box('Side access door gasket',(s*1.165,.48,2.17),(.018,.88,1.55),black,.02); box('Side access door',(s*1.18,.48,2.17),(.018,.83,1.49),red,.018)
 box('Side window surround',(s*1.195,.48,2.51),(.025,.47,.65),black,.06); box('Side window',(s*1.21,.48,2.51),(.013,.4,.57),glass,.045)
 cyl('Access door latch',(s*1.212,.78,1.73),.065,.025,chrome,'X')
 box('Equipment vent',(s*1.175,2.95,2.06),(.018,.55,.46),black,.006)
 for i in range(8):box('Vent louver',(s*1.195,2.95,1.87+i*.053),(.02,.52,.017),red,.002)
 txt('Houston Fire','HOUSTON FIRE',(s*1.20,1.9,2.91),.21,white,side); txt('EMS','EMS',(s*1.20,1.92,2.37),.33,blue,side)
 txt('Unit marking','MEDIC 07',(s*1.20,2.85,2.68),.15,white,side); txt('911','EMERGENCY 911',(s*1.20,2.95,1.46),.105,red,side)
 for off in (-.67,.67):
  pts=[(-.22,0),(-.1,0),(-.06,.06),(0,-.1),(.07,.11),(.12,0),(.22,0)]
  for a,b in zip(pts,pts[1:]):rod('ECG',(s*1.205,1.92+off+a[0],2.36+a[1]),(s*1.205,1.92+off+b[0],2.36+b[1]),.009,white)
# Actual wheel-arch cutouts through body shells.
bpy.context.view_layer.update()
for y,r,targets in [(-2.55,.59,fenders),(2.08,.54,[module])]:
 cutter=cyl('Arch cutter',(0,y,.56),r,3.5,None,'X')
 bpy.context.view_layer.update()
 for o in targets:
  mod=o.modifiers.new('Wheel opening','BOOLEAN'); mod.operation='DIFFERENCE'; mod.object=cutter; mod.solver='EXACT'
  bpy.context.view_layer.objects.active=o; bpy.ops.object.modifier_apply(modifier=mod.name)
 bpy.data.objects.remove(cutter,do_unlink=True)
mesh('Windscreen',[(-.92,-1.91,1.76),(.92,-1.91,1.76),(.92,-1.38,2.61),(-.92,-1.38,2.61)],[(0,1,2,3)],glass)
for x in (-.96,.96):rod('A pillar',(x,-1.94,1.73),(x,-1.4,2.65),.047,red)
rod('Windscreen gasket',(-.94,-1.92,1.76),(.94,-1.92,1.76),.022,black)
for s in (-1,1):rod('Wiper',(s*.2,-1.94,1.78),(s*.7,-1.73,2.06),.014,black)
print('Building interior',flush=True)
active=cabin
for s in (-1,1):
 box('Seat pedestal',(s*.48,-.73,1.04),(.38,.48,.18),black); box('Seat cushion',(s*.48,-.75,1.22),(.48,.56,.17),fabric,.07)
 o=box('Seat back',(s*.48,-.42,1.55),(.48,.16,.65),fabric,.07); o.rotation_euler[0]=-.1
 box('Headrest',(s*.48,-.39,1.98),(.29,.14,.22),fabric,.045)
 rod('Seatbelt',(s*.79,-.3,2.04),(s*.2,-.5,1.18),.018,black); box('Floor mat',(s*.48,-1.26,1.015),(.55,.55,.015),rubber,.01)
box('Dashboard',(0,-1.54,1.63),(1.81,.43,.22),dash,.07); box('Dashboard fascia',(0,-1.35,1.51),(1.76,.11,.32),dash)
box('Glove box',(.52,-1.277,1.47),(.54,.025,.19),black,.02)
for x in (-.77,-.2,.2,.78):
 box('Air vent',(x,-1.282,1.61),(.16,.025,.08),black,.01)
 for i in range(4):box('Vent fins',(x-.06+i*.04,-1.264,1.61),(.008,.01,.06),chrome,.001)
box('Gauge cluster',(-.48,-1.26,1.64),(.42,.06,.2),black,.03)
for x in (-.59,-.39):
 cyl('Gauge bezel',(x,-1.22,1.64),.072,.015,chrome,'Y'); cyl('Gauge face',(x,-1.21,1.64),.061,.015,black,'Y')
 rod('Gauge needle',(x,-1.198,1.64),(x+.027,-1.198,1.68),.004,white)
rod('Steering column',(-.49,-1.39,1.42),(-.49,-1.06,1.56),.045,black); torus('Steering wheel',(-.49,-1.04,1.59),.18,.021,black,'Y')
for dx,dz in ((-.15,0),(.15,0),(0,-.15)):rod('Steering spoke',(-.49,-1.04,1.59),(-.49+dx,-1.04,1.59+dz),.018,black)
box('Steering centre',(-.49,-1.03,1.59),(.11,.055,.08),black)
for x in (-.55,-.34):box('Pedal',(x,-1.46,1.1),(.09,.14,.035),rubber,.008)
box('Radio console',(0,-.77,1.22),(.3,.6,.37),black,.03)
screen=material('Display blue',(.02,.2,.32),.1,.3); box('Radio display',(0,-1.278,1.53),(.2,.015,.09),screen,.005)
for x in (-.12,.12):cyl('Radio knob',(x,-1.26,1.52),.027,.03,black,'Y')
for z in (1.31,1.39):
 for x in (-.08,0,.08):box('Switch',(x,-1.084,z),(.035,.025,.03),white,.004)
print('Building rear fittings',flush=True)
active=body
box('Rear door gasket',(0,3.565,2.02),(1.78,.025,2.14),black,.025)
for s in (-1,1):box('Rear door',(s*.437,3.59,2.02),(.854,.04,2.1),red,.018)
def clip(poly,idx,v,gt):
 out=[]
 for a,b in zip(poly,poly[1:]+poly[:1]):
  aa=a[idx]>=v if gt else a[idx]<=v; bb=b[idx]>=v if gt else b[idx]<=v
  if aa:out.append(a)
  if aa!=bb:
   t=(v-a[idx])/(b[idx]-a[idx]); out.append(tuple(a[j]+t*(b[j]-a[j]) for j in range(2)))
 return out
for s in (-1,1):
 for n in range(-5,9):
  z=n*.55; pts=[(0,z),(1.13,z+1.13),(1.13,z+1.41),(0,z+.28)]; pts=clip(clip(pts,1,.84,True),1,3.23,False)
  if len(pts)>2:mesh('Safety chevron',[(s*x,3.618,z) for x,z in pts],[tuple(range(len(pts)))],yellow)
for s in (-1,1):
 box('Rear window seal',(s*.44,3.65,2.61),(.55,.065,.65),black,.065); box('Rear glass',(s*.44,3.69,2.61),(.46,.012,.55),glass,.045)
 cyl('Rear recessed handle',(s*.17,3.67,1.92),.074,.035,chrome,'Y'); rod('Rear handle',(s*.24,3.7,1.92),(s*.11,3.7,1.92),.016,black)
 for z in (1.16,2.1,2.92):box('Rear hinge',(s*.88,3.65,z),(.065,.04,.17),chrome,.009)
txt('Rear unit','M 07',(0,3.69,1.49),.23,white); txt('Rear lettering','HFD EMS',(0,3.69,1.8),.17,white)
box('Rear plate bumper',(0,3.64,.69),(2.37,.32,.22),plate,.03); box('Rear step',(0,3.88,.57),(1.13,.38,.13),plate,.02)
for x in (-.98,.98):box('Rear bumper rubber',(x,3.84,.69),(.27,.15,.17),rubber,.02)
for x in (-.64,.64):rod('Push bar upright',(x,-3.95,.48),(x,-3.95,1.82),.048,black)
for z in (.53,.95,1.68):rod('Push bar crossmember',(-.64,-3.96,z),(.64,-3.96,z),.041,black)
box('Front licence plate',(0,-3.98,.66),(.3,.018,.13),white,.008); txt('Plate text','TEXAS EMS',(0,-3.993,.66),.034,black,'front')
rod('Aerial',(.3,-.69,2.79),(.3,-.69,3.18),.006,black)
print('Building rolling wheel rig',flush=True)
active=wheelcoll
for s in (-1,1):
 for axle,y in (('Front',-2.55),('Rear',2.08)):
  stem=axle+('_L' if s<0 else '_R')
  steer=empty(stem+' steer',(s*1.0,y,.56),root)
  if axle=='Front':drv(steer,'rotation_euler','steer*pi/180 if demo<0.5 else 0.12*sin(frame/30)',{'steer':'Steering_deg','demo':'Demo'},2)
  spin=empty(stem+' roll',(0,0,0),steer); drv(spin,'rotation_euler','travel/0.48 if demo<0.5 else min(frame,150)*0.055/0.48',{'travel':'Travel_m','demo':'Demo'},0)
  for off in ([0] if axle=='Front' else [-s*.14,s*.13]):
   torus('Tire',(off,0,0),.365,.115,rubber,'X',spin); cyl('Wheel rim',(off,0,0),.285,.22,chrome,'X',spin)
   x=off+s*.127; cyl('Wheel dish',(x,0,0),.225,.015,black,'X',spin); cyl('Wheel hub',(x+s*.014,0,0),.117,.065,chrome,'X',spin)
   for i in range(8):
    a=i*pi/4; cyl('Wheel lug',(x+s*.045,.09*sin(a),.09*cos(a)),.012,.025,chrome,'X',spin,12); cyl('Vent ring',(x+s*.008,.18*sin(a),.18*cos(a)),.037,.018,chrome,'X',spin,16)
   for i in range(44):
    a=2*pi*i/44
    for dx in (-.055,.055):
     o=box('Tire tread',(off+dx,.475*sin(a),.475*cos(a)),(.075,.043,.022),rubber,.002,spin); o.rotation_euler[0]=-a
print('Building lights and drivers',flush=True)
active=lamps
head='(head if demo<0.5 else (frame>=35))'; brake='(brake if demo<0.5 else (150<=frame<=205))'; hp={'head':'Headlights','demo':'Demo'}; bp={'brake':'Brake','demo':'Demo'}
def emissive(n,c,expr,props,base=.03,gain=10):
 m=material(n,c,.1,.22); p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Emission Color'].default_value=(*c,1); drv(p.inputs['Emission Strength'],'default_value',str(base)+'+'+str(gain)+'*('+expr+')',props); return m
hm=emissive('Headlight lens',(.9,.95,1),head,hp,0,7); bm=emissive('Brake lens',(1,.012,.008),brake,bp,.1,8)
am=emissive('Amber marker',(1,.31,.015),head,hp,.03,2); rm=emissive('Rear running lens',(.7,.006,.003),head,hp,.03,2)
def light(n,loc,c,expr,props,power=100,typ='POINT',normal=None,angle=1):
 data=bpy.data.lights.new(n,typ); data.color=c; data.energy=0; data.shadow_soft_size=.08
 if typ=='SPOT':data.spot_size=angle; data.spot_blend=.6
 o=bpy.data.objects.new(n,data); lamps.objects.link(o); o.parent=root; o.location=loc
 if normal:o.rotation_euler=Vector(normal).to_track_quat('-Z','Y').to_euler()
 drv(data,'energy',str(power)+'*('+expr+')',props); return o
for s in (-1,1):
 for z in (1.18,1.49):
  box('Headlamp reflector',(s*.85,-3.6,z),(.31,.09,.23),chrome); box('Headlamp glass',(s*.85,-3.654,z),(.27,.018,.19),hm,.018)
 light('Low beam',(s*.85,-3.69,1.36),(.87,.93,1),head,hp,160,'SPOT',(s*.08,-1,-.12),1.05)
 box('Amber indicator',(s*1.015,-3.61,1.33),(.07,.04,.45),am,.01)
 cyl('Brake lamp',(s*.95,3.84,.73),.079,.028,bm,'Y'); light('Brake spill',(s*.95,3.89,.73),(1,.015,.005),brake,bp,6)
 cyl('Running lamp',(s*.78,3.84,.73),.05,.028,rm,'Y'); cyl('Reverse lens',(s*.6,3.84,.73),.065,.028,white,'Y')
 for y in (.1,1.25,3.35):box('Side marker',(s*1.18,y,1.5),(.025,.11,.055),am,.01)
for g in (0,1):
 expr='(em if demo<0.5 else (frame>=65))*(((frame+'+str(g*6)+')%12<2) or (3<=(frame+'+str(g*6)+')%12<5))'; props={'em':'Emergency','demo':'Demo'}
 for ci,c in enumerate(((1,.015,.008),(.91,.96,1))):
  m=emissive('Warning group '+str(g)+' color '+str(ci),c,expr,props,.025,15); locs=[]
  if ci==g:
   for i in range(5):
    if i%2==g:locs.append(((-.84+i*.42,-.121,2.91),'front'))
   locs += [(((-1 if g==0 else 1)*1.0,-.121,3.15),'front'),(((-1 if g==0 else 1)*.38,-3.72,1.46),'front')]
  if ci==0:
   for s in (-1,1):
    for j,y in enumerate((.2,1.65,3.28)):
     if j%2==g:locs.append(((s*1.175,y,3.05),'left' if s<0 else 'right'))
   for i in range(5):
    if i%2==g:locs.append(((-.95+i*.475,3.62,3.11),'rear'))
  for loc,face in locs:
   normal={'front':(0,-1,0),'rear':(0,1,0),'left':(-1,0,0),'right':(1,0,0)}[face]
   size=(.04,.24,.16) if face in ('left','right') else (.25,.045,.16)
   box('Warning bezel',loc,tuple(v+(.018 if v>.1 else .006) for v in size),chrome,.02)
   pos=tuple(loc[i]+normal[i]*.025 for i in range(3)); box('Warning lens G'+str(g),pos,size,m,.02)
   light('Warning spill G'+str(g),tuple(pos[i]+normal[i]*.04 for i in range(3)),c,expr,props,18)
active=body; txt('Front module unit','M     07',(0,-.133,3.13),.25,white,'front')
print('Building studio',flush=True)
active=studio
floor=material('Studio floor',(.045,.055,.069),.12,.48); box('Ground',(0,0,-.04),(200,200,.08),floor,0,None)
def area(n,loc,power,size,c,target=(0,0,1.3)):
 d=bpy.data.lights.new(n,'AREA'); d.energy=power; d.shape='DISK'; d.size=size; d.color=c; o=bpy.data.objects.new(n,d); studio.objects.link(o); o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler()
area('Key',(-5,-4,8),1800,6,(.85,.91,1)); area('Fill',(5,-1,5),1300,5,(1,.85,.72)); area('Rim',(0,6,7),2000,5,(.8,.9,1))
def cam(n,loc,target,lens):
 d=bpy.data.cameras.new(n); o=bpy.data.objects.new(n,d); studio.objects.link(o); o.location=loc; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler(); d.lens=lens; d.clip_start=.025; return o
front=cam('CAM Front',(-9,-11,6),(0,0,1.5),52); rear=cam('CAM Rear',(9,10,5.4),(0,.8,1.6),55)
inside=cam('CAM Interior',(-1.8,-.25,2.5),(0,-1.2,1.5),22)
for n,f in [('ROLLING',1),('HEADLIGHTS',35),('EMERGENCY',65),('BRAKE + STOP',150),('RELEASE BRAKE',206)]:sc.timeline_markers.new(n,frame=f)
# Evaluate real driver state.
checks=[]
def check(ok,msg):
 checks.append({'pass':bool(ok),'check':msg})
 if not ok:raise RuntimeError(msg)
root['Demo']=0; root.update_tag(); sc.frame_set(1); bpy.context.view_layer.update()
check(abs(bpy.data.lights['Low beam'].energy)<.001,'Headlights switch off')
root['Headlights']=1; root['Brake']=1; root['Emergency']=1; root['Travel_m']=1; root['Steering_deg']=20; root.update_tag(); sc.frame_set(72); bpy.context.view_layer.update()
check(bpy.data.lights['Low beam'].energy>0,'Headlights switch on'); check(bpy.data.lights['Brake spill'].energy>0,'Brake lights switch on')
check(any(l.energy>0 for l in bpy.data.lights if l.name.startswith('Warning spill')),'Emergency flashers illuminate')
check(abs(bpy.data.objects['Front_L roll'].rotation_euler.x-1/.48)<.001,'Wheels roll by distance / radius')
check(abs(bpy.data.objects['Front_L steer'].rotation_euler.z-math.radians(20))<.001,'Front steering follows control')
check(len([o for o in cabin.objects if o.name.startswith('Seat cushion')])==2,'Two cab seats')
for k,v in defaults.items():root[k]=v
root.update_tag(); sc.frame_set(90); sc.camera=front; bpy.context.view_layer.update()
readme="""HOUSTON FIRE EMS - MEDIC 07
Reference-inspired Chevrolet-style Type I ambulance. Approximate dimensions,
metres, front -Y / right +X / up +Z. Exterior combines the supplied references;
interior is interpreted because references do not show it. Simplified HFD badge.

OPEN HFD_Medic_07.blend
Select MEDIC 07 - CONTROLS, Object Properties > Custom Properties.
Demo=1: play frames 1-240 at 30 fps. Headlights at 35, emergency at 65,
brakes at 150-205; wheels stop at 150. Demo spins wheels without moving root.
Demo=0: manual Headlights, Brake, Emergency (0 off, 1 on).
Travel_m rotates all wheels by travel / 0.48 metre radius. Move root separately.
Steering_deg turns front wheel pivots.
Driver_door_deg / Passenger_door_deg open the cab doors (0-75 degrees).
Driver/passenger seats, headrests, belts, dashboard, steering wheel, gauges,
pedals, radio, console, vents, inner door panels and floor mats are modelled.
Rear patient compartment interior is not included.
Two alternating warning groups use double pulses, with actual Blender lights.
Controls use native Blender drivers; no add-on or script auto-run is required.

FILES
.blend: functional rig, editable materials, cameras, packed photo references.
.glb: static exchange geometry; Blender drivers are not portable in glTF.
Wire the named wheel/steering pivots and light groups to your engine scripts.
build_ems.py: reproducible Blender source.
validation.json: evaluated control checks.
PNG previews: front, rear and cab interior.
Studio collection is excluded from GLB. GLB is exported with manual lights off.

This is a reference-inspired asset, not a surveyed production vehicle replica.
"""
t=bpy.data.texts.new('START HERE - controls'); t.write(readme)
open(os.path.join(OUT,'README.txt'),'w',encoding='utf8').write(readme)
open(os.path.join(OUT,'validation.json'),'w').write(json.dumps(checks,indent=2))
for n in ['amb-lights-00.png','ambulance-08.jpg','ambulance-055.jpg','ambulance-back.jpg','medic-59.jpg']:
 im=bpy.data.images.load(os.path.join(r'J:\Unity\projects\HTownSim\Sample-Asset-Images',n),check_existing=True); im.pack()
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True); bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
 for a in screen.areas:
  if a.type=='VIEW_3D':a.spaces.active.region_3d.view_distance=11; a.spaces.active.region_3d.view_location=(0,0,1.5)
print('Saving and rendering',flush=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'HFD_Medic_07.blend'))
sc.render.filepath=os.path.join(OUT,'01_Front.png'); bpy.ops.render.render(write_still=True)
sc.camera=rear; sc.render.filepath=os.path.join(OUT,'02_Rear.png'); bpy.ops.render.render(write_still=True)
root['Demo']=0; root['Headlights']=0; root['Brake']=0; root['Emergency']=0; root['Driver_door_deg']=70; root.update_tag(); sc.frame_set(1); sc.camera=inside
sc.render.filepath=os.path.join(OUT,'03_Interior.png'); bpy.ops.render.render(write_still=True)
root['Driver_door_deg']=0; root.update_tag(); sc.frame_set(1); bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for c in (body,cabin,wheelcoll,lamps):
 for o in c.objects:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=os.path.join(OUT,'HFD_Medic_07.glb'),use_selection=True,export_format='GLB',export_animations=False,export_apply=True)
print('BUILD COMPLETE',flush=True)
