"""Use the supplied HFD artwork on both front doors without a white rectangle."""
import bpy
import numpy as np
from mathutils import Vector

path='J:/Unity/projects/HTownSim/Sample-Asset-Images/HFD_Logo_May_2025.png'
img=bpy.data.images.load(path,check_existing=True)
img.pack()
w,h=img.size
pixels=np.empty(w*h*4,dtype=np.float32);img.pixels.foreach_get(pixels)
pixels=pixels.reshape((h,w,4))
# Trace only the outer silhouette; keep the white parts inside the artwork intact.
colored=(pixels[:,:,3]>.1)&(np.min(pixels[:,:,:3],axis=2)<.88)
ys=np.where(colored.any(axis=1))[0]
bottom,top=int(ys[0]),int(ys[-1])
rows=list(range(bottom,top+1,3))
if rows[-1]!=top:rows.append(top)
edges=[]
for y in rows:
    xs=np.where(colored[y])[0]
    if len(xs):edges.append((y,int(xs[0]),int(xs[-1])))
left=min(a for y,a,b in edges);right=max(b for y,a,b in edges)
mid_x=(left+right)/2;mid_y=(bottom+top)/2
outline=[(b,y) for y,a,b in edges]+[(a,y) for y,a,b in reversed(edges)]
material=bpy.data.materials.get('DC08 | Official supplied HFD shield')
if material is None:material=bpy.data.materials.new('DC08 | Official supplied HFD shield')
material.use_nodes=True
nodes=material.node_tree.nodes;nodes.clear()
output=nodes.new('ShaderNodeOutputMaterial');bs=nodes.new('ShaderNodeBsdfPrincipled')
bs.inputs['Roughness'].default_value=.4
tex=nodes.new('ShaderNodeTexImage');tex.image=img
material.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
material.node_tree.links.new(bs.outputs['BSDF'],output.inputs['Surface'])
for o in [q for q in bpy.data.collections['DC08 | Vehicle'].objects if q.name.startswith('Houston Fire EMS Rescue door shield')]:
    points=[o.matrix_world@Vector(v) for v in o.bound_box]
    center=Vector([(min(p[i] for p in points)+max(p[i] for p in points))/2 for i in range(3)])
    # Keep a stable physical position and preserve the original artwork's proportions.
    center.x=-.91;center.z=1.29
    sign=1 if center.y<0 else -1
    scale=.437/(top-bottom)
    inv=o.matrix_world.inverted()
    verts=[inv@Vector((center.x+sign*(px-mid_x)*scale,center.y,center.z+(py-mid_y)*scale)) for px,py in outline]
    data=bpy.data.meshes.new('Supplied Houston shield silhouette')
    data.from_pydata(verts,[],[tuple(range(len(verts)))])
    data.materials.append(material)
    uv=data.uv_layers.new(name='Full source artwork')
    for j,(px,py) in enumerate(outline):uv.data[j].uv=((px+.5)/w,(py+.5)/h)
    o.data=data
    o['artwork_source']=path
print('Replaced both front-door shields with packed supplied HFD logo; white interior retained, outer white background excluded by silhouette mesh.')
