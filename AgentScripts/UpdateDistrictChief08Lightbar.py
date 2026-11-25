"""Replace square roof-lightbar ends with angled red LED corner modules."""
import bpy
from mathutils import Vector, Matrix

collection=bpy.data.collections['DC08 | Vehicle']
root=bpy.data.objects['DC08_CONTROLS']
assembly=bpy.data.objects.get('Roof lightbar assembly')
if assembly is None:
    assembly=bpy.data.objects.new('Roof lightbar assembly',None)
    collection.objects.link(assembly)
    assembly.parent=root

for o in list(collection.objects):
    if o.name.startswith('Roof angled end') or o.name.startswith('Roof side end'):
        bpy.data.objects.remove(o,do_unlink=True)
    elif o.name.startswith(('Roof front RED','Roof rear RED')) and abs(o.location.y)>.65:
        bpy.data.objects.remove(o,do_unlink=True)

def make_mesh(name,verts,faces,material,bevel=.003):
    data=bpy.data.meshes.new(name)
    data.from_pydata(verts,[],faces);data.update()
    data.materials.append(bpy.data.materials['DC08 | '+material])
    o=bpy.data.objects.new(name,data);collection.objects.link(o);o.parent=assembly
    if bevel:
        mod=o.modifiers.new('Lens edge radii','BEVEL');mod.width=bevel;mod.segments=3
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o

def slab(name,center,u,width,height,depth,material,bevel=.003):
    u=Vector(u);v=Vector((0,0,1));n=u.cross(v);center=Vector(center)
    verts=[center+u*(a*width/2)+v*(b*height/2)+n*(c*depth/2)
           for a,b,c in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    return make_mesh(name,verts,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],material,bevel)

# Octagonal footprint: long front/back faces transition into diagonal end optics.
outline=[(-.34,-.65),(-.25,-.85),(-.11,-.85),(-.02,-.65),
         (-.02,.65),(-.11,.85),(-.25,.85),(-.34,.65)]
for name,z,depth,material in [('Roof lightbar chassis',2.455,.07,'Polished aluminum and chrome'),
                              ('Roof lightbar clear lid',2.55,.018,'Clear optical lens')]:
    old=bpy.data.objects.get(name)
    if old:bpy.data.objects.remove(old,do_unlink=True)
    verts=[(x,y,zz) for zz in [z-depth/2,z+depth/2] for x,y in outline]
    faces=[tuple(range(7,-1,-1)),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
    make_mesh(name,verts,faces,material,.008)

def module(name,a,b,columns):
    a=Vector((*a,2.503));b=Vector((*b,2.503))
    u=(b-a).normalized();n=u.cross(Vector((0,0,1)))
    center=(a+b)/2;w=(b-a).length-.016
    slab(name+' bezel',center+n*.008,u,w,.084,.02,'Satin black powdercoat',.006)
    slab(name+' clear lens',center+n*.023,u,w-.009,.069,.013,'Clear optical lens',.005)
    for col in range(columns):
        for row in range(2):
            pos=center+n*.033+u*((col+.5)/columns-.5)*(w-.02)+Vector((0,0,(row-.5)*.027))
            slab(name+' RED diode',pos,u,(w-.035)/columns*.77,.019,.006,'RED emergency LED',.002)

# Clockwise perimeter directions give outward-facing normals on every facet.
for i,label in [(0,'driver forward'),(2,'driver rearward'),(4,'passenger rearward'),(6,'passenger forward')]:
    module('Roof angled end '+label,outline[i],outline[(i+1)%8],7)
for i,label in [(1,'driver'),(5,'passenger')]:
    module('Roof side end '+label,outline[i],outline[(i+1)%8],4)

for o in collection.objects:
    if o is not assembly and o.name.startswith(('Roof lightbar','Roof front RED','Roof rear RED')):
        o.parent=assembly

assert len([o for o in collection.objects if o.name.startswith('Roof angled end') and 'bezel' in o.name])==4
assert len([o for o in collection.objects if o.name.startswith('Roof side end') and 'bezel' in o.name])==2
print('Updated: four diagonal RED LED modules plus two side-facing RED modules; existing emergency_red control reused.')
