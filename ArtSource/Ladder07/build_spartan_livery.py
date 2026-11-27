"""Create the reusable Spartan rear-mount asset from the authored Ladder 07 rig."""
from pathlib import Path
from collections import deque
from math import cos, radians, sin
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree


NUMBER_NAMES = ("Badge unit", "Front unit", "Bumper 07", "Tail side unit", "Rear 07", "HOUSTON LADDER 07")
TEXAS_NAMES = ("Texas insignia", "Texas blue field", "Texas red field", "Texas white star")
OLD_LIGHTBAR_NAMES = ("Roof lightbar mounting", "Roof emergency lightbar", "Roof red LED")
UNIT_PROPERTY = "Unit Number"
DEFAULT_UNIT_NUMBER = 99
MATCH_DISTANCE = 0.00008


def update_rig(rig):
    rig.update_tag()
    bpy.context.view_layer.update()


def read_markings(source_path, rig_scene):
    with bpy.data.libraries.load(str(source_path), link=False) as (source, target):
        target.scenes = ["Houston Ladder 07"]
    source_scene = target.scenes[0]
    bpy.context.window.scene = source_scene
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    markings = []
    for obj in source_scene.objects:
        if not ((obj.type == "FONT" and obj.name.startswith(NUMBER_NAMES))
                or (obj.type == "MESH" and obj.name.startswith(TEXAS_NAMES + OLD_LIGHTBAR_NAMES))):
            continue
        if obj.name.startswith(OLD_LIGHTBAR_NAMES):
            # Match the bevel settings the original rig builder used when merging the bars.
            for modifier in list(obj.modifiers):
                if modifier.type == "BEVEL":
                    if modifier.width <= .006:
                        obj.modifiers.remove(modifier)
                    else:
                        modifier.segments = 1 if modifier.width < .03 else 2
            bpy.context.view_layer.update()
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(graph), depsgraph=graph)
        points = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
        tree = KDTree(len(points))
        for index, point in enumerate(points):
            tree.insert(point, index)
        tree.balance()
        markings.append((obj.name, obj.get("rig_part", "chassis"), obj.matrix_world.copy(), obj.data.copy(), tree))
        bpy.data.meshes.remove(mesh)
    bpy.context.window.scene = rig_scene
    source_objects = list(source_scene.objects)
    bpy.data.scenes.remove(source_scene)
    for obj in source_objects:
        bpy.data.objects.remove(obj, do_unlink=True)
    return markings


def remove_baked_markings(rig, markings):
    for part in sorted({mark[1] for mark in markings}):
        obj = bpy.data.objects[f"L07_{part}_LOD0"]
        matches = [mark for mark in markings if mark[1] == part]
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        removed = {name: 0 for name, *_ in matches}
        to_delete = []
        world_vertices = {vertex: obj.matrix_world @ vertex.co for vertex in bm.verts}
        vertices_by_marking = {
            name: {vertex for vertex, point in world_vertices.items() if tree.find(point)[2] <= MATCH_DISTANCE}
            for name, _, _, _, tree in matches
        }
        for face in bm.faces:
            material = obj.data.materials[face.material_index]
            for name, _, _, curve, tree in matches:
                if material.name.split(".")[0] != curve.materials[0].name.split(".")[0]:
                    continue
                if all(vertex in vertices_by_marking[name] for vertex in face.verts):
                    removed[name] += 1
                    to_delete.append(face)
                    break
        if any(count == 0 for count in removed.values()):
            bm.free()
            raise RuntimeError(f"Couldn't isolate all baked markings: {removed}")
        bmesh.ops.delete(bm, geom=to_delete, context="FACES")
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.update()
        print(f"Removed original {part} livery faces: {removed}")
        for level, ratio in ((1, .35), (2, .10)):
            rig["LOD"] = level
            update_rig(rig)
            lower = bpy.data.objects[f"L07_{part}_LOD{level}"]
            lower.data = obj.data.copy()
            modifier = lower.modifiers.new("Distance mesh reduction", "DECIMATE")
            modifier.ratio = ratio
            bpy.context.view_layer.objects.active = lower
            bpy.ops.object.modifier_apply(modifier=modifier.name)
    rig["LOD"] = 0
    update_rig(rig)


def make_number_group(scene, rig, template):
    options = bpy.data.collections.new(f"Glyph library | {template.body}")
    # Build each label once per format. The collection is available to Geometry Nodes
    # without being linked into the visible scene.
    for number in range(100):
        font = template.copy()
        font.body = template.body[:-2] + f"{number:02d}"
        font_object = bpy.data.objects.new("Temporary number text", font)
        scene.collection.objects.link(font_object)
        bpy.context.view_layer.update()
        graph = bpy.context.evaluated_depsgraph_get()
        data = bpy.data.meshes.new_from_object(font_object.evaluated_get(graph), depsgraph=graph)
        data.name = f"{template.body[:-2]}{number:02d}"
        option = bpy.data.objects.new(f"{number:02d} | {template.body[:-2]}", data)
        options.objects.link(option)
        bpy.data.objects.remove(font_object, do_unlink=True)
        bpy.data.curves.remove(font)

    group = bpy.data.node_groups.new(f"Unit number | {template.body}", "GeometryNodeTree")
    group.interface.new_socket(name="Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    unit = group.nodes.new("ShaderNodeValue")
    unit.label = UNIT_PROPERTY
    unit.outputs[0].default_value = DEFAULT_UNIT_NUMBER
    driver = unit.outputs[0].driver_add("default_value").driver
    var = driver.variables.new()
    var.name = "v"
    var.type = "SINGLE_PROP"
    var.targets[0].id = rig
    var.targets[0].data_path = f'["{UNIT_PROPERTY}"]'
    driver.expression = "min(99,max(0,v))"
    output = group.nodes.new("NodeGroupOutput")
    collection = group.nodes.new("GeometryNodeCollectionInfo")
    collection.inputs["Collection"].default_value = options
    collection.inputs["Separate Children"].default_value = True
    collection.inputs["Reset Children"].default_value = True
    point = group.nodes.new("GeometryNodeMeshLine")
    point.inputs["Count"].default_value = 1
    instance = group.nodes.new("GeometryNodeInstanceOnPoints")
    instance.inputs["Pick Instance"].default_value = True
    realize = group.nodes.new("GeometryNodeRealizeInstances")
    group.links.new(unit.outputs[0], instance.inputs["Instance Index"])
    group.links.new(collection.outputs["Instances"], instance.inputs["Instance"])
    group.links.new(point.outputs["Mesh"], instance.inputs["Points"])
    group.links.new(instance.outputs["Instances"], realize.inputs["Geometry"])
    group.links.new(realize.outputs["Geometry"], output.inputs["Geometry"])
    return group


def install_numbers(scene, rig, markings, livery):
    rig[UNIT_PROPERTY] = DEFAULT_UNIT_NUMBER
    rig.id_properties_ui(UNIT_PROPERTY).update(
        default=DEFAULT_UNIT_NUMBER, min=0, max=99, soft_min=0, soft_max=99,
        description="Apparatus identifier. Displays as two digits (00–99)."
    )
    groups = {}
    prototype_scene = bpy.data.scenes.new("Temporary glyph generation")
    bpy.context.window.scene = prototype_scene
    for _, _, _, template, _ in markings:
        key = (template.body, template.materials[0].name)
        if key not in groups:
            groups[key] = make_number_group(prototype_scene, rig, template)
    bpy.context.window.scene = scene
    bpy.data.scenes.remove(prototype_scene)
    results = []
    for name, part, world, template, _ in markings:
        key = (template.body, template.materials[0].name)
        group = groups[key]
        obj = bpy.data.objects.new(f"Unit marking | {name}", bpy.data.meshes.new(f"Marking | {name}"))
        livery.objects.link(obj)
        obj.parent = bpy.data.objects[f"L07_{part}_LOD0"].parent
        obj.matrix_world = world
        modifier = obj.modifiers.new(UNIT_PROPERTY, "NODES")
        modifier.node_group = group
        results.append(obj)
    return results


def install_shared_logo(rig, livery):
    hfd_logos = [obj for obj in bpy.context.scene.objects if obj.get("official_hfd_logo")]
    assert len(hfd_logos) == 6, "Keep both original HFD front door emblems at each LOD."
    original_image = next(node.image for node in hfd_logos[0].active_material.node_tree.nodes if node.type == "TEX_IMAGE")
    material = bpy.data.materials.new("Station Logo | Both Crew Doors")
    material.use_nodes = True
    material.surface_render_method = "DITHERED"
    nodes = material.node_tree.nodes
    shader = nodes.get("Principled BSDF")
    shader.inputs["Roughness"].default_value = .32
    texture = nodes.new("ShaderNodeTexImage")
    texture.name = "Station Logo Image"
    texture.label = "Replace this one image to update both doors"
    texture.image = original_image
    texture.location = (-360, 160)
    material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    material.node_tree.links.new(texture.outputs["Alpha"], shader.inputs["Alpha"])
    nodes.active = texture

    logos = []
    for side, label in ((-1, "Driver"), (1, "Officer")):
        # The rear crew door spans x=-3.24 to -2.33. Put the station art
        # ahead of the existing unit badge and below the crew-door glass.
        center_x, center_z, half_size = -3.13, 1.43, .0875
        y = side * 1.266
        points = [(center_x - side * x * half_size, y, center_z + z * half_size)
                  for x, z in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
        mesh = bpy.data.meshes.new(f"Station logo | {label} crew door")
        mesh.from_pydata(points, [], [(0, 1, 2, 3)])
        mesh.materials.append(material)
        uv = mesh.uv_layers.new(name="Station Logo")
        coords = ((0, 0), (1, 0), (1, 1), (0, 1))
        for loop in mesh.loops:
            uv.data[loop.index].uv = coords[loop.vertex_index]
        obj = bpy.data.objects.new(mesh.name, mesh)
        livery.objects.link(obj)
        obj.parent = rig
        logos.append(obj)
    return logos


def texas_artwork_runs(image):
    """Clip the outer white JPEG background while preserving the enclosed white flag."""
    source_width, source_height = image.size
    width, height = source_width // 2, source_height // 2
    source = image.pixels[:]
    light = bytearray(
        min(source[offset:offset + 3]) > .8
        for y in range(height)
        for x in range(width)
        for offset in [((2 * y) * source_width + 2 * x) * 4]
    )
    exterior = bytearray(width * height)
    fringe = deque()

    def visit(x, y):
        if 0 <= x < width and 0 <= y < height:
            index = y * width + x
            if light[index] and not exterior[index]:
                exterior[index] = 1
                fringe.append((x, y))

    for x in range(width):
        visit(x, 0)
        visit(x, height - 1)
    for y in range(height):
        visit(0, y)
        visit(width - 1, y)
    while fringe:
        x, y = fringe.popleft()
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            visit(x + dx, y + dy)

    assert exterior[0] and not exterior[height // 2 * width + width * 3 // 4]
    for y in range(height):
        x = 0
        while x < width:
            if exterior[y * width + x]:
                x += 1
                continue
            start = x
            while x < width and not exterior[y * width + x]:
                x += 1
            yield start / width, x / width, y / height, (y + 1) / height


def install_texas_artwork(rig, livery, image_path):
    image = bpy.data.images.load(str(image_path), check_existing=True)
    image.pack()
    material = bpy.data.materials.new("Texas | Cab Artwork")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = image
    nodes.active = texture
    material.node_tree.links.new(texture.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
    runs = list(texas_artwork_runs(image))
    decals = []
    for side, label in ((-1, "Driver"), (1, "Officer")):
        vertices, faces, uvs = [], [], []
        for left, right, bottom, top in runs:
            index = len(vertices)
            faces.append(tuple(range(index, index + 4)))
            for u, v in ((left, bottom), (right, bottom), (right, top), (left, top)):
                vertices.append((-3.76 - side * (u - .5) * .54, side * 1.270, 2.13 + (v - .5) * .52))
                uvs.append((u, v))
        mesh = bpy.data.meshes.new(f"Texas artwork | {label} side")
        mesh.from_pydata(vertices, [], faces)
        mesh.materials.append(material)
        uv = mesh.uv_layers.new(name="Texas Artwork")
        for loop in mesh.loops:
            uv.data[loop.index].uv = uvs[loop.vertex_index]
        obj = bpy.data.objects.new(mesh.name, mesh)
        livery.objects.link(obj)
        obj.parent = rig
        decals.append(obj)
    return decals


def validate(scene, rig, numbers, logos):
    def sample(value, lod):
        rig[UNIT_PROPERTY], rig["LOD"] = value, lod
        update_rig(rig)
        graph = bpy.context.evaluated_depsgraph_get()
        result = []
        for obj in numbers:
            mesh = obj.evaluated_get(graph).data
            assert len(mesh.polygons) > 0, obj.name
            group = obj.modifiers[0].node_group
            assert all(driver.is_valid for driver in group.animation_data.drivers), obj.name
            options = next(node.inputs["Collection"].default_value for node in group.nodes
                           if node.bl_idname == "GeometryNodeCollectionInfo")
            expected = sorted(options.objects, key=lambda option: option.name)[min(99, max(0, value))]
            tree = KDTree(len(expected.data.vertices))
            for vertex in expected.data.vertices:
                tree.insert(vertex.co, vertex.index)
            tree.balance()
            assert len(mesh.vertices) == len(expected.data.vertices) and all(
                tree.find(vertex.co)[2] < 0.00001 for vertex in mesh.vertices
            ), (obj.name, value, expected.name, len(mesh.vertices), len(expected.data.vertices))
            result.append(tuple(tuple(v.co) for v in mesh.vertices))
        return result

    original = sample(7, 0)
    assert all(a != b for a, b in zip(original, sample(16, 0)))
    assert sample(16, 0) == sample(16, 1) == sample(16, 2)
    assert sample(99, 0) == sample(123, 0)
    assert sample(0, 0) == sample(-1, 0)
    assert logos[0].active_material == logos[1].active_material
    hfd_logos = [obj for obj in scene.objects if obj.get("official_hfd_logo")]
    assert len(hfd_logos) == 6
    assert all(obj.active_material != logos[0].active_material for obj in hfd_logos)
    input_node = logos[0].active_material.node_tree.nodes["Station Logo Image"]
    original_image = input_node.image
    assert original_image.packed_file
    replacement = bpy.data.images.new("Station logo replacement test", width=1, height=1, alpha=True)
    input_node.image = replacement
    assert all(obj.active_material.node_tree.nodes["Station Logo Image"].image == replacement for obj in logos)
    input_node.image = original_image
    bpy.data.images.remove(replacement)
    assert all(-3.24 < vertex.co.x < -2.33 for logo in logos for vertex in logo.data.vertices)
    sample(DEFAULT_UNIT_NUMBER, 0)
    print(f"Verified {len(numbers)} number locations at 07, 16, 00 and 99, all LODs, and two shared-logo doors.")


def connect_aerial_tip():
    # The cage is wider than the last fly. Tie it into both upper and lower chords.
    pivot = Vector((4.24, 0, 3.16))
    for level in range(3):
        mesh = bpy.data.objects[f"L07_aerial_3_LOD{level}"].data
        white = mesh.materials.find("L07_Aerial white powder coat")
        aluminum = mesh.materials.find("L07_Brushed aluminum")
        assert white >= 0 and aluminum >= 0, mesh.name
        bm = bmesh.new()
        bm.from_mesh(mesh)

        def tube(start, end, radius, material):
            start, end = Vector(start) - pivot, Vector(end) - pivot
            axis = end - start
            transform = axis.to_track_quat("Z", "Y").to_matrix().to_4x4()
            transform.translation = (start + end) / 2
            vertices = bmesh.ops.create_cone(
                bm, cap_ends=True, cap_tris=False, segments=12 if level == 0 else 8,
                radius1=radius, radius2=radius, depth=axis.length, matrix=transform,
            )["verts"]
            for face in {face for vertex in vertices for face in vertex.link_faces}:
                face.material_index = material

        for side in (-1, 1):
            tube((-5.16, side * .275, 3.245), (-5.58, side * .57, 3.024), .031, white)
            tube((-5.16, side * .275, 3.465), (-5.58, side * .57, 3.626), .026, white)
        tube((-5.14, 0, 2.94), (-5.71, 0, 2.94), .042, aluminum)
        bm.normal_update()
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()


def install_front_lightbars(scene, rig):
    assembly = bpy.data.collections.new("Spartan | Front roof lightbars")
    scene.collection.children.link(assembly)

    def material(name, color, metallic=0.0, roughness=.28):
        result = bpy.data.materials.new(name)
        result.diffuse_color = (*color, 1)
        result.use_nodes = True
        shader = result.node_tree.nodes["Principled BSDF"]
        shader.inputs["Base Color"].default_value = result.diffuse_color
        shader.inputs["Metallic"].default_value = metallic
        shader.inputs["Roughness"].default_value = roughness
        return result, shader

    cover, lens = material("Front lightbar | Clear polycarbonate", (.95, .98, 1), roughness=.10)
    cover.diffuse_color = (.95, .98, 1, .13)
    cover.surface_render_method = "DITHERED"
    lens.inputs["Alpha"].default_value = .13
    metal, _ = material("Front lightbar | Brushed tray", (.29, .31, .33), metallic=.78)
    lid, _ = material("Front lightbar | Brushed aluminum lid", (.58, .61, .64), metallic=.82, roughness=.31)
    hardware, _ = material("Front lightbar | Stainless lens screws", (.42, .44, .47), metallic=.90, roughness=.23)
    bracket, _ = material("Front lightbar | Black mount and gasket", (.027, .030, .034), metallic=.55)
    red, _ = material("Front lightbar | Red LED panel", (.62, .021, .028), roughness=.22)
    white, _ = material("Front lightbar | White LED panel", (.87, .90, .89), roughness=.22)
    diode, _ = material("Front lightbar | LED optics", (.88, .91, .94), metallic=.15)

    def prism(name, lower, upper, surface, recess=None):
        n = len(lower)
        faces = [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        faces.extend((i, (i + 1) % n, (i + 1) % n + n, i + n) for i in range(n))
        data = bpy.data.meshes.new(name)
        vertices = lower + upper
        if recess is not None:
            inner_rim, seat = recess
            vertices += inner_rim + seat
            faces[1] = tuple(range(3 * n, 4 * n))
            for ring in (n, 2 * n):
                faces.extend((ring + i, ring + (i + 1) % n,
                              ring + (i + 1) % n + n, ring + i + n) for i in range(n))
        data.from_pydata(vertices, [], faces)
        bm = bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(data)
        bm.free()
        data.materials.append(surface)
        obj = bpy.data.objects.new(name, data)
        assembly.objects.link(obj)
        obj.parent = rig
        return obj

    a, b, c = .32, .153, .062
    def housing_outline(inset):
        x, y = a - inset, b - inset
        chamfer = c - (2 - 2**.5) * inset
        return [(-x + chamfer, -y), (x - chamfer, -y), (x, -y + chamfer), (x, y - chamfer),
                (x - chamfer, y), (-x + chamfer, y), (-x, y - chamfer), (-x, -y + chamfer)]

    outline = housing_outline(0)
    # Put the trays on short brackets above the lower front roof crown.
    floor = 2.852
    graph = bpy.context.evaluated_depsgraph_get()
    for side, label in ((-1, "Driver"), (1, "Officer")):
        long_x, long_y = sin(radians(30)), cos(radians(30))

        def point(u, v, z):
            return (-5.42 + long_x*u - long_y*v, side*(.87 + long_y*u + long_x*v), z)

        def layer(name, layout, bottom, top, surface):
            return prism(f"{label} lightbar | {name}", [point(*uv, bottom) for uv in layout],
                         [point(*uv, top) for uv in layout], surface)

        lens_rim = housing_outline(.0495)
        prism(f"{label} lightbar | Clear wraparound lens",
              [point(u, v, floor + .027) for u, v in outline],
              [point(u, v, floor + .139) for u, v in outline], cover,
              recess=([point(u, v, floor + .139) for u, v in lens_rim],
                      [point(u, v, floor + .132) for u, v in lens_rim]))
        layer("Aluminum lid mounting flange", housing_outline(.050),
              floor + .110, floor + .132, lid)
        layer("Opaque aluminum top cover", housing_outline(.050),
              floor + .132, floor + .136, lid)
        layer("Aluminum lower tray", outline, floor, floor + .021, metal)
        layer("Housing gasket", [(u*.99, v*.99) for u, v in outline], floor + .021, floor + .027, bracket)

        for index, start in enumerate(outline):
            end = outline[(index + 1) % len(outline)]
            edge_start, edge_end = Vector(start), Vector(end)
            edge_tangent = (edge_end - edge_start).normalized()
            outward = Vector((edge_tangent.y, -edge_tangent.x))
            for screw, mix in enumerate((.21, .79) if index in (0, 4) else (.5,), 1):
                center = edge_start.lerp(edge_end, mix)

                def fastener_point(along, z, depth):
                    uv = center + edge_tangent * along + outward * depth
                    return point(uv.x, uv.y, floor + .122 + z)

                rim = [(cos(radians(angle))*.0048, sin(radians(angle))*.0048) for angle in range(0, 360, 30)]
                prefix = f"{label} lightbar | Lens screw {index + 1}.{screw}"
                prism(prefix, [fastener_point(u, z, -.012) for u, z in rim],
                      [fastener_point(u, z, .003) for u, z in rim], hardware)
                slot = [(-.003, -.00055), (.003, -.00055), (.003, .00055), (-.003, .00055)]
                prism(prefix + " drive slot", [fastener_point(u, z, .003) for u, z in slot],
                      [fastener_point(u, z, .0035) for u, z in slot], bracket)

            start, end = Vector(start) * .89, Vector(end) * .89
            tangent = (end - start).normalized()
            inward = Vector((-tangent.y, tangent.x)) * .011
            count = 3 if index in (0, 4) else 1
            for module in range(count):
                left = start.lerp(end, (module + .07) / count)
                right = start.lerp(end, (module + .93) / count)
                is_white = index == 4 and module == 1
                color = "White" if is_white else "Red"
                suffix = f"{index + 1}.{module + 1}"
                layer(f"{color} LED panel {suffix}", [left, right, right + inward, left + inward],
                      floor + .043, floor + .115, white if is_white else red)
                for column in range(3):
                    center = left.lerp(right, (column + .5) / 3)
                    half_width = min(.009, (right - left).length / 9)
                    across = tangent * half_width
                    outward = inward * -.28
                    layer(f"LED optic {suffix}.{column + 1}",
                          [center - across + outward, center + across + outward, center + across, center - across],
                          floor + .069, floor + .090, diode if is_white else red)

        for number, u in enumerate((-.183, .183), 1):
            base_v = .05
            x, y, _ = point(u, base_v, floor)
            hit, position, _, _, obj, _ = scene.ray_cast(graph, Vector((x, y, floor - .007)), Vector((0, 0, -1)))
            assert hit and obj.name == "L07_chassis_LOD0" and 2.70 < position.z < floor, (label, number, obj)
            pad = [(u + du, base_v + dv) for du, dv in ((-.057, -.040), (.057, -.040), (.057, .040), (-.057, .040))]
            layer(f"Roof mounting foot {number}", pad, position.z - .018, position.z + .014, bracket)
            section = [(-.034, -.016), (.034, -.016), (.034, .016), (-.034, .016)]
            prism(f"{label} lightbar | Raked mount {number}",
                  [point(u + du, base_v + dv, position.z + .008) for du, dv in section],
                  [point(u + du, -.019 + dv, floor + .002) for du, dv in section], bracket)

        wire = bpy.data.curves.new(f"{label} lightbar feed", "CURVE")
        wire.dimensions = "3D"
        wire.bevel_depth = .005
        wire.bevel_resolution = 2
        spline = wire.splines.new("BEZIER")
        spline.bezier_points.add(2)
        for handle, uvz in zip(spline.bezier_points, ((0, -.07, floor), (-.03, -.095, floor - .040), (-.125, -.04, 2.780))):
            handle.co = point(*uvz)
            handle.handle_left_type = handle.handle_right_type = "AUTO"
        wire.materials.append(bracket)
        cable = bpy.data.objects.new(wire.name, wire)
        assembly.objects.link(cable)
        cable.parent = rig

    assert sum("White LED panel" in obj.name for obj in assembly.objects) == 2
    assert sum("Raked mount" in obj.name for obj in assembly.objects) == 4
    assert sum("Opaque aluminum top cover" in obj.name for obj in assembly.objects) == 2
    assert sum("Lens screw" in obj.name and "drive slot" not in obj.name for obj in assembly.objects) == 20
    print("Verified mirrored roof bars, two roof-supported mounts per bar, and one white front panel per bar.")


def main():
    source, output = (Path(argument) for argument in sys.argv[sys.argv.index("--") + 1:])
    if output.resolve() in {Path(bpy.data.filepath).resolve(), source.resolve()}:
        raise ValueError("The output must be separate from both original Blender sources.")
    scene = bpy.context.scene
    rig = bpy.data.objects["Ladder07_CONTROLS"]
    markings = read_markings(source, scene)
    numbers_only = [mark for mark in markings if mark[0].startswith(NUMBER_NAMES)]
    assert len(numbers_only) == 10 and len(markings) == 28, [mark[0] for mark in markings]
    remove_baked_markings(rig, markings)
    connect_aerial_tip()
    install_front_lightbars(scene, rig)
    livery = bpy.data.collections.new("Spartan | Reusable Livery")
    scene.collection.children.link(livery)
    numbers = install_numbers(scene, rig, numbers_only, livery)
    logos = install_shared_logo(rig, livery)
    texas_image = source.parents[2] / "Sample-Asset-Images" / "texas-flag-state-free-vector.jpg"
    texas = install_texas_artwork(rig, livery, texas_image)
    validate(scene, rig, numbers, logos)
    assert texas[0].active_material == texas[1].active_material
    for level in range(3):
        rig["LOD"] = level
        update_rig(rig)
        graph = bpy.context.evaluated_depsgraph_get()
        for side, decal in zip((-1, 1), texas):
            hit, _, _, _, obj, _ = scene.ray_cast(graph, Vector((-3.76, side * 3, 2.13)), Vector((0, -side, 0)))
            assert hit and obj.original == decal, (level, side, obj)
    rig["LOD"] = 0
    update_rig(rig)
    print("Verified the new Texas artwork on both sides at all three detail levels.")
    rig.name = "Spartan_RearMount_CONTROLS"
    scene.name = "Spartan Rear-Mount Ladder Truck"
    rig["Livery Instructions"] = "Set Unit Number (00–99). To change both doors, replace Station Logo Image in the shared Station Logo | Both Crew Doors material."
    notes = bpy.data.texts.new("SPARTAN_LIVERY_README")
    notes.write(
        "Select Spartan_RearMount_CONTROLS and open Object Properties > Custom Properties.\n"
        "Set Unit Number from 0 to 99. It displays as two digits at all 10 apparatus marking locations.\n"
        "The 107 ft reach lettering is part of the model and does not change.\n"
        "Select either Station logo crew-door object and open the Shader Editor.\n"
        "In Station Logo | Both Crew Doors, replace Station Logo Image with a transparent PNG.\n"
        "Both doors share that image at every LOD. Use File > External Data > Pack Resources to embed a replacement.\n"
    )
    for obj in list(bpy.context.selected_objects):
        obj.select_set(False)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.file.pack_all()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output), compress=True)


if __name__ == "__main__":
    main()
