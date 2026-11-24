"""Apply the supplied HFD artwork as UV-mapped, shield-shaped door decals."""
import bpy
from pathlib import Path

IMAGE=Path(r'J:/Unity/projects/HTownSim/Sample-Asset-Images/HFD_Logo_May_2025.png')

def create_decals(scene,parent,collection,suffix=''):
    image=bpy.data.images.load(str(IMAGE),check_existing=True)
    image.pack()
    width,height=image.size
    pixels=list(image.pixels)
    # Follow the outer gold border geometrically; retain the original image intact.
    rows=[]
    for y in range(height):
        hits=[x for x in range(width) if pixels[(y*width+x)*4]>.65 and pixels[(y*width+x)*4+1]>.45 and pixels[(y*width+x)*4+2]<.3]
        if hits:rows.append((y,min(hits),max(hits)))
    assert rows,'No gold shield outline found in supplied image'
    chosen=[rows[round(i*(len(rows)-1)/64)] for i in range(65)]
    material=bpy.data.materials.get('HFD Official Door Logo') or bpy.data.materials.new('HFD Official Door Logo')
    material.use_nodes=True
    nodes=material.node_tree.nodes
    shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    shader.inputs['Roughness'].default_value=.32
    shader.inputs['Metallic'].default_value=.05
    tex=next((n for n in nodes if n.type=='TEX_IMAGE'),None) or nodes.new('ShaderNodeTexImage')
    tex.image=image
    material.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    result=[]
    for side,label in [(-1,'Driver'),(1,'Passenger')]:
        verts=[];uvs=[]
        for y,left,right in chosen:
            for x in [left+.5,right-.5]:
                u=x/width;v=(y+.5)/height
                verts.append((-4.85-side*(u-.5)*.44,side*1.2655,1.47+(v-.5)*.44))
                uvs.append((u,v))
        faces=[(2*i,2*i+1,2*i+3,2*i+2) for i in range(len(chosen)-1)]
        mesh=bpy.data.meshes.new('HFD_%s_Door_Logo%s'%(label,suffix));mesh.from_pydata(verts,[],faces);mesh.materials.append(material)
        uv=mesh.uv_layers.new(name='Artwork UV')
        for loop in mesh.loops:uv.data[loop.index].uv=uvs[loop.vertex_index]
        obj=bpy.data.objects.new(mesh.name,mesh);collection.objects.link(obj);obj.parent=parent
        obj['rig_part']='chassis';obj['official_hfd_logo']=True
        result.append(obj)
    return result
