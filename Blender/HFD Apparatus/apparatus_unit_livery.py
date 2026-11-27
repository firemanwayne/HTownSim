"""Drive unit-number texture tiles from a Blender object property."""
import json

import bpy

UNIT_NUMBER = "Unit Number"
DEFAULT_NUMBER = 99


def mark_coordinates():
    """Map an atlas rectangle into its selected tile and mask out the surrounding UVs."""
    group = bpy.data.node_groups.new("Apparatus | Number tile coordinates", "ShaderNodeTree")
    for name in ("UV", "Rectangle Origin", "Inverse Rectangle Size", "Tile"):
        group.interface.new_socket(name=name, in_out="INPUT", socket_type="NodeSocketVector")
    group.interface.new_socket(name="Tile UV", in_out="OUTPUT", socket_type="NodeSocketVector")
    group.interface.new_socket(name="Inside Rectangle", in_out="OUTPUT", socket_type="NodeSocketFloat")
    nodes, links = group.nodes, group.links
    inputs = nodes.new("NodeGroupInput").outputs
    outputs = nodes.new("NodeGroupOutput").inputs

    def operation(node_type, mode, left, right):
        node = nodes.new(node_type)
        node.operation = mode
        links.new(left, node.inputs[0])
        if isinstance(right, (tuple, int, float)):
            node.inputs[1].default_value = right
        else:
            links.new(right, node.inputs[1])
        return node.outputs[0]

    def vector(mode, left, right):
        return operation("ShaderNodeVectorMath", mode, left, right)

    def scalar(mode, left, right):
        return operation("ShaderNodeMath", mode, left, right)

    offset = vector("SUBTRACT", inputs["UV"], inputs["Rectangle Origin"])
    local = vector("MULTIPLY", offset, inputs["Inverse Rectangle Size"])
    selected = vector("ADD", local, inputs["Tile"])
    links.new(vector("MULTIPLY", selected, (.1, .1, 1)), outputs["Tile UV"])
    axes = nodes.new("ShaderNodeSeparateXYZ")
    links.new(local, axes.inputs[0])
    x, y = axes.outputs["X"], axes.outputs["Y"]
    lower = scalar("GREATER_THAN", scalar("MINIMUM", x, y), 0)
    upper = scalar("LESS_THAN", scalar("MAXIMUM", x, y), 1)
    links.new(scalar("MULTIPLY", lower, upper), outputs["Inside Rectangle"])
    return group


def install_numbers(shell, rig, generated, *, base_image, manifest="unit_markings.json"):
    data = json.loads((generated / manifest).read_text())
    atlas_width, atlas_height = data["atlas"]
    material = shell.data.materials[0]
    nodes, links = material.node_tree.nodes, material.node_tree.links
    shader = nodes.get("Principled BSDF")
    uv = nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    uv.location = (-1050, 400)
    base = nodes.new("ShaderNodeTexImage")
    base.label = "Base livery"
    base.image = bpy.data.images.load(str(generated / base_image))
    base.location = (-800, 400)
    links.new(uv.outputs[0], base.inputs["Vector"])
    color = base.outputs["Color"]

    tile = nodes.new("ShaderNodeCombineXYZ")
    tile.label = "Unit Number • texture tile"
    tile.location = (-1050, 150)
    for axis, expression in (("X", "n%10"), ("Y", "9-floor(n/10)")):
        driver = tile.inputs[axis].driver_add("default_value").driver
        variable = driver.variables.new()
        variable.name = "number"
        variable.type = "SINGLE_PROP"
        variable.targets[0].id = rig
        variable.targets[0].data_path = f'["{UNIT_NUMBER}"]'
        driver.expression = expression.replace("n", "min(max(round(number),0),99)")

    toggle_tiles = {}
    for name in dict.fromkeys(marking["toggle"] for marking in data["markings"] if "toggle" in marking):
        selector = nodes.new("ShaderNodeCombineXYZ")
        selector.label = name
        selector.location = (-1050, -90 - 160 * len(toggle_tiles))
        selector.inputs["Y"].default_value = 9
        driver = selector.inputs["X"].driver_add("default_value").driver
        variable = driver.variables.new()
        variable.name = "enabled"
        variable.type = "SINGLE_PROP"
        variable.targets[0].id = rig
        variable.targets[0].data_path = f'["{name}"]'
        driver.expression = "enabled"
        toggle_tiles[name] = selector

    coordinates = mark_coordinates()
    for index, marking in enumerate(data["markings"], 1):
        x, y, width, height = marking["rect"]
        position = nodes.new("ShaderNodeGroup")
        position.node_tree = coordinates
        position.label = f"Marking {index} • position and tile"
        position.location = (-800, -350 * index)
        links.new(uv.outputs[0], position.inputs["UV"])
        selector = toggle_tiles.get(marking.get("toggle"), tile)
        links.new(selector.outputs[0], position.inputs["Tile"])
        position.inputs["Rectangle Origin"].default_value = (x / atlas_width, 1 - (y + height) / atlas_height, 0)
        position.inputs["Inverse Rectangle Size"].default_value = (atlas_width / width, atlas_height / height, 1)
        image = nodes.new("ShaderNodeTexImage")
        image.image = bpy.data.images.load(str(generated / marking["image"]))
        image.extension = "CLIP"
        image.location = (-530, -350 * index)
        links.new(position.outputs["Tile UV"], image.inputs["Vector"])
        opacity = nodes.new("ShaderNodeMath")
        opacity.operation = "MULTIPLY"
        opacity.location = (-250, -350 * index)
        links.new(position.outputs["Inside Rectangle"], opacity.inputs[0])
        links.new(image.outputs["Alpha"], opacity.inputs[1])
        mix = nodes.new("ShaderNodeMixRGB")
        mix.location = (index * 185, 400)
        links.new(opacity.outputs[0], mix.inputs[0])
        links.new(color, mix.inputs[1])
        links.new(image.outputs["Color"], mix.inputs[2])
        color = mix.outputs[0]
    shader.location = (185 * (len(data["markings"]) + 1), 400)
    nodes.get("Material Output").location = (shader.location.x + 300, 400)
    links.new(color, shader.inputs["Base Color"])


