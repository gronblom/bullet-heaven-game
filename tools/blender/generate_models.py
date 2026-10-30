"""
Procedural model generator for Bullet Heaven.

Builds every model from code, gives it a detailed procedural material (wood grain, glazed
ceramic, marble, brushed steel, chitin, fabric, ...), bakes that material into ordinary image
textures (colour, roughness, metallic, normal, emission) and exports a .glb that Godot imports
directly into assets/models/. Procedural Blender materials don't survive export, baked
textures do.

Run from the repository root (recommended, takes a few minutes):

    blender --background --python tools/blender/generate_models.py
    blender --background --python tools/blender/generate_models.py -- --only ant mug --preview

On Windows, with Blender installed in the default place:

    "C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe" -b -P tools\\blender\\generate_models.py

Options (after the "--"):
    --only NAME ...   build just these models (see MODELS at the bottom)
    --out DIR         assets folder to write into (default: <repo>/assets)
    --preview         also render a PNG preview of each model into <out>/previews/
    --draft           smaller textures for quick iteration

Running it inside the Blender UI also works (Scripting tab > open file > Run Script), but it clears
the current scene first, so use an empty file.

Conventions: Blender units == game units. Characters face -Y in Blender (glTF/Godot +Z).
Props are built at their in-game size with their origin at the bottom centre.
Tested with Blender 4.0; uses only APIs that exist in 4.x and 5.x.
"""

import argparse
import math
import os
import random
import sys

import bpy
import bmesh
from mathutils import Vector

# ----------------------------------------------------------------------------------------------
# Setup
# ----------------------------------------------------------------------------------------------


def repo_root():
    here = os.path.dirname(os.path.abspath(__file__)) if "__file__" in globals() else os.getcwd()
    candidate = os.path.abspath(os.path.join(here, "..", ".."))
    if os.path.exists(os.path.join(candidate, "project.godot")):
        return candidate
    return os.getcwd()


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(description="Generate Bullet Heaven models")
    parser.add_argument("--out", default=os.path.join(repo_root(), "assets"))
    parser.add_argument("--only", nargs="*", default=None)
    parser.add_argument("--preview", action="store_true")
    parser.add_argument("--draft", action="store_true")
    return parser.parse_args(argv)


ARGS = None
TEX_SCALE = 1.0  # halves texture sizes in draft mode


def clear_scene():
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for collection in (bpy.data.objects, bpy.data.meshes, bpy.data.curves, bpy.data.materials,
                       bpy.data.images, bpy.data.lights, bpy.data.cameras):
        for item in list(collection):
            collection.remove(item)


def setup_render():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    scene.render.bake.margin = 8
    scene.render.bake.use_clear = True


def activate(obj, extra=()):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for other in extra:
        other.select_set(True)
    bpy.context.view_layer.objects.active = obj


def link(obj):
    bpy.context.collection.objects.link(obj)
    return obj


def apply_transform(obj):
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def apply_modifiers(obj):
    activate(obj)
    for mod in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)


def smooth(obj, on=True):
    for poly in obj.data.polygons:
        poly.use_smooth = on
    return obj


def with_material(obj, material):
    material = getattr(material, "mat", material)  # accept a Mat builder or a bpy Material
    obj.data.materials.clear()
    obj.data.materials.append(material)
    return obj


def tag(obj, group):
    """Put every vertex of obj in a vertex group (used to split limbs back out after baking)."""
    vg = obj.vertex_groups.new(name=group)
    vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    return obj


def anim_tag(obj, code, root=None, length=1.0):
    """Per-vertex animation data for the bug walk shader, stored in an "anim" colour attribute.
    The part code (0 body, 1-3 one side's legs front-to-back, 4-6 the other side's, 7 antennae) is
    written as bits in R/G/B (0 or 1 survive any colour-space conversion), and A holds how far along
    the limb the vertex is (0 at the hip, 1 at the tip; alpha is never gamma-converted)."""
    attr = obj.data.color_attributes.get("anim") or obj.data.color_attributes.new("anim", "FLOAT_COLOR", "POINT")
    bits = (float(code & 1), float((code >> 1) & 1), float((code >> 2) & 1))
    for i, v in enumerate(obj.data.vertices):
        weight = 0.0 if root is None else min(1.0, (v.co - Vector(root)).length / length)
        attr.data[i].color = (*bits, weight)
    return obj


def ground_and_center(obj):
    """Move geometry so its bounding box is centred on X/Y and rests on z = 0 (origin at bottom centre)."""
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    offset = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, min(zs)))
    for v in obj.data.vertices:
        v.co -= offset
    obj.data.update()
    print(f"  {obj.name}: {max(xs) - min(xs):.2f} x {max(ys) - min(ys):.2f} x {max(zs) - min(zs):.2f}")
    return obj


def split_parts(obj, pivots):
    """After baking, separate vertex groups back out into child objects with their own pivots
    (so the game can rotate them: limbs, spinning barrels)."""
    objects = [obj]
    for part, pivot in pivots.items():
        activate(obj)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="DESELECT")
        obj.vertex_groups.active_index = obj.vertex_groups[part].index
        bpy.ops.object.vertex_group_select()
        bpy.ops.mesh.separate(type="SELECTED")
        bpy.ops.object.mode_set(mode="OBJECT")
        piece = next(o for o in bpy.context.selected_objects if o is not obj)
        piece.name = part
        bpy.context.scene.cursor.location = pivot
        activate(piece)
        bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
        piece.parent = obj
        objects.append(piece)
    for o in objects:
        for group in list(o.vertex_groups):
            o.vertex_groups.remove(group)
    return objects


def join(objects, name):
    objects = [o for o in objects if o is not None]
    activate(objects[0], objects[1:])
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    return obj


# ----------------------------------------------------------------------------------------------
# Geometry helpers. Everything returns a mesh object with transforms applied.
# ----------------------------------------------------------------------------------------------


def sphere(name, radius=1.0, loc=(0, 0, 0), scale=(1, 1, 1), segments=24, rings=14, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    apply_transform(obj)
    return smooth(obj)


def cylinder(name, radius, depth, loc=(0, 0, 0), rot=(0, 0, 0), vertices=32, radius_top=None):
    if radius_top is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc, rotation=rot)
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=radius_top, depth=depth,
                                        location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    apply_transform(obj)
    return obj


def box(name, size, loc=(0, 0, 0), bevel=0.0, segments=2, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    apply_transform(obj)
    if bevel > 0:
        mod = obj.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
        apply_modifiers(obj)
    return obj


def torus(name, major, minor, loc=(0, 0, 0), rot=(0, 0, 0), major_segments=32, minor_segments=12):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, location=loc, rotation=rot,
                                     major_segments=major_segments, minor_segments=minor_segments)
    obj = bpy.context.active_object
    obj.name = name
    apply_transform(obj)
    return smooth(obj)


def tube(name, points, radii, smooth_curve=True, sides=8):
    """A tube along points with a per-point radius (legs, antennae, stems, handles)."""
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = 1.0
    curve.bevel_resolution = max(1, sides // 4 - 1)
    curve.resolution_u = 6 if smooth_curve else 1
    curve.use_fill_caps = True
    spline = curve.splines.new("NURBS" if smooth_curve else "POLY")
    spline.points.add(len(points) - 1)
    for point, co, radius in zip(spline.points, points, radii):
        point.co = (co[0], co[1], co[2], 1.0)
        point.radius = radius
    if smooth_curve:
        spline.use_endpoint_u = True
        spline.order_u = min(3, len(points))
    obj = link(bpy.data.objects.new(name, curve))
    activate(obj)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.active_object
    obj.name = name
    return smooth(obj)


def lathe(name, profile, steps=64):
    """Revolve a (radius, z) profile around Z: mugs, bowls."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    verts = [bm.verts.new((r, 0.0, z)) for r, z in profile]
    for a, b in zip(verts, verts[1:]):
        bm.edges.new((a, b))
    bm.to_mesh(mesh)
    bm.free()
    obj = link(bpy.data.objects.new(name, mesh))
    mod = obj.modifiers.new("screw", "SCREW")
    mod.axis = "Z"
    mod.steps = steps
    mod.render_steps = steps
    mod.use_merge_vertices = True
    mod.merge_threshold = 1e-4
    mod.use_normal_calculate = True
    apply_modifiers(obj)
    return smooth(obj)


def subdivide(obj, levels=1):
    mod = obj.modifiers.new("subsurf", "SUBSURF")
    mod.levels = levels
    mod.render_levels = levels
    apply_modifiers(obj)
    return obj


def deform(obj, fn):
    """Move every vertex: fn(Vector) -> Vector."""
    for v in obj.data.vertices:
        v.co = fn(v.co.copy())
    obj.data.update()
    return obj


# ----------------------------------------------------------------------------------------------
# Material helpers: a tiny node-graph builder. Each material ends in a Principled BSDF.
# ----------------------------------------------------------------------------------------------


class Mat:
    def __init__(self, name):
        self.mat = bpy.data.materials.new(name)
        self.mat.use_nodes = True
        self.nodes = self.mat.node_tree.nodes
        self.links = self.mat.node_tree.links
        self.bsdf = self.nodes.get("Principled BSDF")
        self.coords = self.nodes.new("ShaderNodeTexCoord").outputs["Object"]
        self.metallic = 0.0

    def inp(self, *names):
        for name in names:
            if name in self.bsdf.inputs:
                return self.bsdf.inputs[name]
        raise KeyError(names)

    def set(self, name, value):
        socket = self.inp(*name) if isinstance(name, tuple) else self.inp(name)
        if isinstance(value, bpy.types.NodeSocket):
            self.links.new(value, socket)
        elif isinstance(value, tuple) and len(value) == 3 and socket.type == "RGBA":
            socket.default_value = (*value, 1.0)
        else:
            socket.default_value = value
        return self

    def color(self, value):
        return self.set("Base Color", value)

    def roughness(self, value):
        return self.set("Roughness", value)

    def metal(self, value):
        self.metallic = value
        return self.set("Metallic", value)

    def emission(self, color, strength):
        self.set(("Emission Color", "Emission"), color)
        return self.set("Emission Strength", strength)

    def normal(self, socket):
        return self.set("Normal", socket)

    # --- textures ---
    def vec(self, scale=(1, 1, 1), offset=(0, 0, 0), source=None):
        mapping = self.nodes.new("ShaderNodeMapping")
        self.links.new(source or self.coords, mapping.inputs["Vector"])
        mapping.inputs["Scale"].default_value = scale
        mapping.inputs["Location"].default_value = offset
        return mapping.outputs["Vector"]

    def noise(self, scale=5.0, detail=4.0, roughness=0.5, distortion=0.0, vector=None):
        n = self.nodes.new("ShaderNodeTexNoise")
        n.inputs["Scale"].default_value = scale
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = roughness
        n.inputs["Distortion"].default_value = distortion
        self.links.new(vector or self.coords, n.inputs["Vector"])
        return n.outputs["Fac"]

    def voronoi(self, scale=5.0, vector=None, output="Distance"):
        n = self.nodes.new("ShaderNodeTexVoronoi")
        n.inputs["Scale"].default_value = scale
        self.links.new(vector or self.coords, n.inputs["Vector"])
        return n.outputs[output]

    def wave(self, kind="BANDS", axis="X", scale=5.0, distortion=0.0, detail=2.0, vector=None):
        n = self.nodes.new("ShaderNodeTexWave")
        n.wave_type = kind
        if kind == "BANDS":
            n.bands_direction = axis
        else:
            n.rings_direction = axis
        n.inputs["Scale"].default_value = scale
        n.inputs["Distortion"].default_value = distortion
        n.inputs["Detail"].default_value = detail
        self.links.new(vector or self.coords, n.inputs["Vector"])
        return n.outputs["Fac"]

    def ramp(self, fac, stops, interpolation="LINEAR"):
        n = self.nodes.new("ShaderNodeValToRGB")
        n.color_ramp.interpolation = interpolation
        elements = n.color_ramp.elements
        while len(elements) < len(stops):
            elements.new(0.5)
        for element, (position, color) in zip(elements, stops):
            element.position = position
            element.color = color if len(color) == 4 else (*color, 1.0)
        self.links.new(fac, n.inputs["Fac"])
        return n.outputs["Color"]

    def gray(self, fac, low, high, start=0.0, end=1.0):
        return self.ramp(fac, [(start, (low, low, low)), (end, (high, high, high))])

    def math(self, op, a, b=0.0, c=0.0, clamp=False):
        n = self.nodes.new("ShaderNodeMath")
        n.operation = op
        n.use_clamp = clamp
        for i, value in enumerate((a, b, c)):
            if isinstance(value, bpy.types.NodeSocket):
                self.links.new(value, n.inputs[i])
            else:
                n.inputs[i].default_value = value
        return n.outputs[0]

    def axis(self, name, vector=None):
        n = self.nodes.new("ShaderNodeSeparateXYZ")
        self.links.new(vector or self.coords, n.inputs[0])
        return n.outputs[name]

    def mix(self, fac, a, b, blend="MIX"):
        """Mix two colours (sockets or RGB tuples) by fac (socket or float)."""
        n = self.nodes.new("ShaderNodeMix")
        n.data_type = "RGBA"
        n.blend_type = blend
        sockets = {s.identifier: s for s in n.inputs}
        for key, value in (("Factor_Float", fac), ("A_Color", a), ("B_Color", b)):
            if isinstance(value, bpy.types.NodeSocket):
                self.links.new(value, sockets[key])
            else:
                sockets[key].default_value = value if key == "Factor_Float" else (*value[:3], 1.0)
        return {s.identifier: s for s in n.outputs}["Result_Color"]

    def bump(self, height, strength=0.3, distance=0.02, previous=None):
        n = self.nodes.new("ShaderNodeBump")
        n.inputs["Strength"].default_value = strength
        n.inputs["Distance"].default_value = distance
        self.links.new(height, n.inputs["Height"])
        if previous is not None:
            self.links.new(previous, n.inputs["Normal"])
        return n.outputs["Normal"]


# --- material recipes ---------------------------------------------------------------------------


def chitin(name, dark, light, rough=0.3, bands_axis=None, band_scale=6.0):
    m = Mat(name)
    var = m.noise(scale=6.0, detail=6.0, roughness=0.6)
    m.color(m.ramp(var, [(0.35, dark), (0.75, light)]))
    m.roughness(m.gray(m.noise(scale=14.0, detail=3.0), rough * 0.7, rough * 1.5))
    normal = m.bump(m.noise(scale=90.0, detail=2.0), strength=0.12, distance=0.01)
    if bands_axis:
        normal = m.bump(m.wave("BANDS", bands_axis, scale=band_scale, distortion=0.5), strength=0.35, distance=0.02, previous=normal)
    m.normal(normal)
    m.set(("Coat Weight", "Clearcoat"), 0.4)
    return m


def wood(name, light, dark, ring_axis="Z", ring_scale=3.0, rough=0.55):
    m = Mat(name)
    warped = m.vec(scale=(1, 1, 1))
    grain_noise = m.noise(scale=1.2, detail=3.0, distortion=1.5)
    rings = m.wave("RINGS", ring_axis, scale=ring_scale, distortion=6.0, detail=3.0, vector=warped)
    fine = m.noise(scale=40.0, detail=2.0, vector=m.vec(scale=(1, 1, 8) if ring_axis == "Z" else (8, 1, 1)))
    fac = m.math("MULTIPLY_ADD", rings, 0.7, m.math("MULTIPLY", grain_noise, 0.3))
    base = m.ramp(fac, [(0.2, dark), (0.8, light)])
    m.color(m.mix(0.25, base, m.ramp(fine, [(0.3, dark), (0.7, light)])))
    m.roughness(m.gray(fine, rough - 0.1, rough + 0.1))
    m.normal(m.bump(rings, strength=0.08, distance=0.02, previous=m.bump(fine, strength=0.1, distance=0.01)))
    return m


def glaze(name, color, speckle=0.15, rough=0.08):
    m = Mat(name)
    dots = m.voronoi(scale=60.0)
    darker = tuple(c * 0.75 for c in color)
    base = m.mix(m.math("LESS_THAN", dots, 0.08), color, darker)
    m.color(m.mix(m.noise(scale=3.0), base, tuple(min(1.0, c * 1.08) for c in color)) if speckle else base)
    m.roughness(m.gray(m.noise(scale=8.0), rough * 0.6, rough * 1.8))
    m.normal(m.bump(m.noise(scale=25.0, detail=2.0), strength=0.03, distance=0.01))
    return m


def painted(name, color, rough=0.4, brush_axis="Z"):
    m = Mat(name)
    scale = (1, 1, 12) if brush_axis == "Z" else (12, 1, 1)
    strokes = m.noise(scale=6.0, detail=6.0, vector=m.vec(scale=scale))
    m.color(m.mix(m.math("MULTIPLY", strokes, 0.15), color, tuple(c * 0.85 for c in color)))
    m.roughness(m.gray(strokes, rough * 0.8, rough * 1.25))
    m.normal(m.bump(strokes, strength=0.05, distance=0.01))
    return m


def metal(name, color=(0.75, 0.76, 0.78), rough=0.25, brushed_axis="X"):
    m = Mat(name)
    scale = {"X": (1, 40, 40), "Y": (40, 1, 40), "Z": (40, 40, 1)}[brushed_axis]
    streaks = m.noise(scale=4.0, detail=8.0, vector=m.vec(scale=scale))
    m.color(m.mix(m.math("MULTIPLY", streaks, 0.2), color, tuple(c * 0.85 for c in color)))
    m.metal(1.0)
    m.roughness(m.gray(streaks, rough * 0.6, rough * 1.4))
    m.normal(m.bump(streaks, strength=0.04, distance=0.005))
    return m


def plastic(name, color, rough=0.45):
    m = Mat(name)
    m.color(m.mix(m.math("MULTIPLY", m.noise(scale=4.0), 0.12), color, tuple(c * 0.85 for c in color)))
    m.roughness(m.gray(m.noise(scale=20.0), rough * 0.8, rough * 1.2))
    m.normal(m.bump(m.noise(scale=80.0), strength=0.04, distance=0.005))
    return m


def fabric(name, color, rough=0.85):
    m = Mat(name)
    weave = m.math("MULTIPLY", m.wave("BANDS", "X", scale=60.0), m.wave("BANDS", "Z", scale=60.0))
    var = m.noise(scale=5.0, detail=4.0)
    m.color(m.mix(m.math("MULTIPLY", var, 0.3), color, tuple(c * 0.7 for c in color)))
    m.roughness(rough)
    m.normal(m.bump(weave, strength=0.25, distance=0.01))
    m.set(("Sheen Weight", "Sheen"), 0.3)
    return m


def skin(name, color=(0.86, 0.62, 0.5)):
    m = Mat(name)
    blotch = m.noise(scale=8.0, detail=5.0)
    m.color(m.mix(m.math("MULTIPLY", blotch, 0.35), color, (color[0] * 0.95, color[1] * 0.75, color[2] * 0.7)))
    m.roughness(m.gray(m.noise(scale=30.0), 0.4, 0.6))
    m.normal(m.bump(m.voronoi(scale=120.0), strength=0.05, distance=0.005))
    m.set(("Subsurface Weight", "Subsurface"), 0.2)
    return m


def glow(name, color, strength=3.0):
    m = Mat(name)
    m.color(color)
    m.roughness(0.15)
    m.emission(color, strength)
    return m


# ----------------------------------------------------------------------------------------------
# Baking: every material is rendered into images on a single UV atlas, then replaced by one
# plain Principled material that glTF understands.
# ----------------------------------------------------------------------------------------------


def unwrap(obj, margin=0.004):
    activate(obj)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin)
    bpy.ops.object.mode_set(mode="OBJECT")


def _source(material, socket_name):
    """(node, output) feeding a BSDF input, or a constant node with its default value."""
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    for name in (socket_name if isinstance(socket_name, tuple) else (socket_name,)):
        if name in bsdf.inputs:
            socket = bsdf.inputs[name]
            break
    if socket.is_linked:
        return socket.links[0].from_socket
    nodes = material.node_tree.nodes
    value = socket.default_value
    if hasattr(value, "__len__"):
        node = nodes.new("ShaderNodeRGB")
        node.outputs[0].default_value = tuple(value)[:4] if len(value) == 4 else (*value, 1.0)
    else:
        node = nodes.new("ShaderNodeValue")
        node.outputs[0].default_value = value
    return node.outputs[0]


def _emission_strength(material):
    bsdf = material.node_tree.nodes.get("Principled BSDF")
    return bsdf.inputs["Emission Strength"].default_value


def _bake_pass(obj, image, kind, socket=None, strength_scale=None):
    """Bake one pass into image. kind: 'EMIT' with a BSDF socket to read, or 'NORMAL'.
    strength_scale: for the emission pass, scale each material's colour by its strength / this."""
    restore = []
    for slot in obj.material_slots:
        material = slot.material
        nodes = material.node_tree.nodes
        target = nodes.new("ShaderNodeTexImage")
        target.image = image
        for node in nodes:
            node.select = False
        target.select = True
        nodes.active = target

        if socket is not None:
            output = nodes.get("Material Output")
            original = output.inputs["Surface"].links[0].from_socket
            emission = nodes.new("ShaderNodeEmission")
            material.node_tree.links.new(_source(material, socket), emission.inputs["Color"])
            if strength_scale:
                emission.inputs["Strength"].default_value = _emission_strength(material) / strength_scale
            material.node_tree.links.new(emission.outputs[0], output.inputs["Surface"])
            restore.append((material, output, original, target, emission))
        else:
            restore.append((material, None, None, target, None))

    activate(obj)
    if kind == "NORMAL":
        bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    else:
        bpy.ops.object.bake(type=kind)

    for material, output, original, target, emission in restore:
        if output is not None:
            material.node_tree.links.new(original, output.inputs["Surface"])
            material.node_tree.nodes.remove(emission)
        material.node_tree.nodes.remove(target)
    image.pack()


def bake(obj, size=1024, emissive=False):
    size = max(64, int(size * TEX_SCALE))
    unwrap(obj)

    def new_image(suffix, color_space):
        image = bpy.data.images.new(f"{obj.name}_{suffix}", size, size, alpha=False)
        image.colorspace_settings.name = color_space
        return image

    color = new_image("color", "sRGB")
    rough = new_image("roughness", "Non-Color")
    metallic = new_image("metallic", "Non-Color")
    normal = new_image("normal", "Non-Color")
    _bake_pass(obj, color, "EMIT", socket="Base Color")
    _bake_pass(obj, rough, "EMIT", socket="Roughness")
    _bake_pass(obj, metallic, "EMIT", socket="Metallic")
    _bake_pass(obj, normal, "NORMAL")
    glow_image = None
    glow_strength = max(_emission_strength(slot.material) for slot in obj.material_slots)
    if emissive and glow_strength > 0:
        # Bake colour x strength normalised to the brightest material; the strength goes back on export.
        glow_image = new_image("emission", "sRGB")
        _bake_pass(obj, glow_image, "EMIT", socket=("Emission Color", "Emission"), strength_scale=glow_strength)

    # One plain material that the glTF exporter maps 1:1.
    m = bpy.data.materials.new(f"{obj.name}_baked")
    m.use_nodes = True
    nodes, links = m.node_tree.nodes, m.node_tree.links
    bsdf = nodes.get("Principled BSDF")

    def tex(image, x):
        node = nodes.new("ShaderNodeTexImage")
        node.image = image
        node.location = (x, 0)
        return node

    links.new(tex(color, -600).outputs["Color"], bsdf.inputs["Base Color"])
    links.new(tex(rough, -600).outputs["Color"], bsdf.inputs["Roughness"])
    links.new(tex(metallic, -600).outputs["Color"], bsdf.inputs["Metallic"])
    normal_map = nodes.new("ShaderNodeNormalMap")
    links.new(tex(normal, -800).outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    if glow_image is not None:
        emission_input = bsdf.inputs["Emission Color"] if "Emission Color" in bsdf.inputs else bsdf.inputs["Emission"]
        links.new(tex(glow_image, -600).outputs["Color"], emission_input)
        bsdf.inputs["Emission Strength"].default_value = glow_strength

    obj.data.materials.clear()
    obj.data.materials.append(m)
    return obj


def export(objects, name, subfolder="models"):
    folder = os.path.join(ARGS.out, subfolder)
    os.makedirs(folder, exist_ok=True)
    path = os.path.join(folder, name + ".glb")
    activate(objects[0], objects[1:])
    has_anim = any(o.type == "MESH" and o.data.color_attributes.get("anim") for o in objects)
    for o in objects:
        if o.type == "MESH" and o.data.color_attributes.get("anim"):
            o.data.color_attributes.active_color = o.data.color_attributes["anim"]
    options = dict(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                   export_image_format="JPEG", export_jpeg_quality=88, export_image_quality=88,
                   export_vertex_color="ACTIVE" if has_anim else "NONE",  # Blender 4.2+
                   export_colors=has_anim)                                 # Blender 4.0-4.1
    known = bpy.ops.export_scene.gltf.get_rna_type().properties.keys()
    bpy.ops.export_scene.gltf(**{k: v for k, v in options.items() if k in known})
    print(f"  wrote {os.path.relpath(path, ARGS.out)} ({os.path.getsize(path) // 1024} KB)")
    if ARGS.preview:
        render_preview(objects, name)


def render_preview(objects, name):
    """Studio render for eyeballing a model (not used by the game)."""
    lo = Vector((min(v[i] for o in objects for v in (o.matrix_world @ Vector(c) for c in o.bound_box)) for i in range(3)))
    hi = Vector((max(v[i] for o in objects for v in (o.matrix_world @ Vector(c) for c in o.bound_box)) for i in range(3)))
    center, extent = (lo + hi) / 2, (hi - lo).length

    camera = link(bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam")))
    camera.location = center + Vector((0.9, -1.4, 0.9)).normalized() * extent * 1.25
    camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = camera
    lights = []
    for loc, energy in (((1.0, -1.2, 2.0), 250), ((-1.5, 0.5, 1.0), 60)):
        light = link(bpy.data.objects.new("PreviewLight", bpy.data.lights.new("PreviewLight", "AREA")))
        light.data.energy = energy * extent * extent / 4
        light.data.size = extent
        light.location = center + Vector(loc) * extent
        light.rotation_euler = (center - light.location).to_track_quat("-Z", "Y").to_euler()
        lights.append(light)
    world = bpy.data.worlds.new("PreviewWorld") if not bpy.context.scene.world else bpy.context.scene.world
    bpy.context.scene.world = world
    if bpy.app.version < (5, 0, 0):
        world.use_nodes = True  # always on (and deprecated) from 5.0
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.2, 0.21, 0.23, 1)

    scene = bpy.context.scene
    scene.view_settings.view_transform = "Standard"  # neutral colours, close to what the textures hold
    scene.render.resolution_x = scene.render.resolution_y = 480
    scene.cycles.samples = 48
    scene.cycles.use_denoising = False  # not every Blender build ships the denoiser
    scene.render.film_transparent = False
    folder = os.path.join(ARGS.out, "previews")
    os.makedirs(folder, exist_ok=True)
    scene.render.filepath = os.path.join(folder, name + ".png")
    bpy.ops.render.render(write_still=True)
    scene.cycles.samples = 4
    for obj in [camera, *lights]:
        bpy.data.objects.remove(obj)


# ----------------------------------------------------------------------------------------------
# Models
# ----------------------------------------------------------------------------------------------


def insect_leg(name, hip, foot_dir, length, thickness, side):
    """Jointed leg: up and out from the hip, then down to the ground (z = 0)."""
    out = Vector((side * foot_dir[0], foot_dir[1], 0)).normalized()
    knee = hip + out * length * 0.45 + Vector((0, 0, length * 0.25))
    ankle = hip + out * length * 0.85 + Vector((0, 0, -hip.z * 0.55))
    foot = Vector((ankle.x + out.x * length * 0.25, ankle.y + out.y * length * 0.25, 0.01))
    return tube(name, [hip, knee, ankle, foot], [thickness, thickness * 0.8, thickness * 0.55, thickness * 0.35],
                smooth_curve=False, sides=8)


def build_ant():
    shell = chitin("ant_chitin", (0.035, 0.012, 0.008), (0.16, 0.05, 0.025), rough=0.3)
    gaster_shell = chitin("ant_gaster", (0.03, 0.01, 0.008), (0.14, 0.045, 0.02), rough=0.25, bands_axis="Y", band_scale=4.0)
    eye = chitin("ant_eye", (0.01, 0.01, 0.01), (0.05, 0.05, 0.05), rough=0.1)

    h = 0.38  # body height above ground
    parts = [
        with_material(sphere("head", 0.2, (0, -0.62, h + 0.06), (1.0, 1.1, 0.9), 20, 12), shell),
        with_material(sphere("eyeL", 0.06, (0.16, -0.66, h + 0.1), segments=12, rings=8), eye),
        with_material(sphere("eyeR", 0.06, (-0.16, -0.66, h + 0.1), segments=12, rings=8), eye),
        with_material(sphere("thorax", 0.15, (0, -0.25, h), (0.9, 1.7, 0.95), 20, 12, rot=(0.25, 0, 0)), shell),
        with_material(sphere("petiole", 0.07, (0, 0.04, h - 0.02), segments=12, rings=8), shell),
        with_material(sphere("gaster", 0.27, (0, 0.38, h + 0.04), (1.0, 1.25, 0.95), 24, 14, rot=(-0.2, 0, 0)), gaster_shell),
    ]
    for side in (1, -1):
        # Elbowed antennae and curved mandibles.
        antenna_root = (side * 0.08, -0.74, h + 0.18)
        parts.append(anim_tag(with_material(tube(f"antenna{side}", [antenna_root, (side * 0.2, -0.85, h + 0.42),
                                                                   (side * 0.3, -1.08, h + 0.3)], [0.018, 0.016, 0.02]), shell),
                              7, antenna_root, 0.45))
        parts.append(with_material(tube(f"mandible{side}", [(side * 0.08, -0.78, h), (side * 0.1, -0.9, h - 0.04),
                                                           (side * 0.02, -0.97, h - 0.05)], [0.03, 0.022, 0.008]), shell))
        for i, (y, spread) in enumerate(((-0.38, (0.8, -0.6)), (-0.25, (1.0, 0.0)), (-0.12, (0.8, 0.7)))):
            hip = Vector((side * 0.09, y, h - 0.05))
            leg = with_material(insect_leg(f"leg{side}{i}", hip, spread, 0.62, 0.028, side), shell)
            parts.append(anim_tag(leg, (1 if side > 0 else 4) + i, hip, 0.62))
    for part in parts:
        if not part.data.color_attributes.get("anim"):
            anim_tag(part, 0)
    ant = join(parts, "ant")
    return [bake(ant, 512)]


def build_beetle():
    shell = Mat("beetle_shell")
    stripes = shell.wave("BANDS", "X", scale=9.0)  # lengthwise grooves along the wing cases
    var = shell.noise(scale=4.0, detail=5.0)
    seam = shell.math("LESS_THAN", shell.math("ABSOLUTE", shell.axis("X")), 0.012)
    base = shell.ramp(var, [(0.3, (0.02, 0.16, 0.08)), (0.7, (0.08, 0.38, 0.18))])
    shell.color(shell.mix(seam, base, (0.01, 0.03, 0.02)))
    shell.metal(0.8)
    shell.roughness(shell.gray(var, 0.12, 0.3))
    shell.normal(shell.bump(stripes, strength=0.25, distance=0.01, previous=shell.bump(seam, strength=0.6, distance=0.02)))
    body = chitin("beetle_body", (0.02, 0.025, 0.02), (0.08, 0.1, 0.08), rough=0.3)

    h = 0.3
    elytra = sphere("elytra", 0.42, (0, 0.15, h + 0.05), (1.0, 1.45, 0.62), 28, 16)
    deform(elytra, lambda v: Vector((v.x, v.y, max(v.z, h - 0.05))))  # flat underside
    parts = [
        with_material(elytra, shell.mat),
        with_material(sphere("pronotum", 0.3, (0, -0.48, h + 0.02), (1.05, 0.7, 0.55), 24, 12), shell.mat),
        with_material(sphere("head", 0.17, (0, -0.78, h - 0.02), (1.0, 0.9, 0.7), 20, 10), body),
    ]
    for side in (1, -1):
        antenna_root = (side * 0.08, -0.9, h)
        parts.append(anim_tag(with_material(tube(f"antenna{side}", [antenna_root, (side * 0.25, -1.1, h + 0.12),
                                                                   (side * 0.42, -1.25, h + 0.1)], [0.02, 0.018, 0.015]), body),
                              7, antenna_root, 0.5))
        parts.append(with_material(tube(f"mandible{side}", [(side * 0.07, -0.92, h - 0.06), (side * 0.05, -1.02, h - 0.08),
                                                           (side * 0.0, -1.05, h - 0.08)], [0.035, 0.025, 0.01]), body))
        for i, (y, spread) in enumerate(((-0.45, (0.8, -0.5)), (-0.15, (1.0, 0.1)), (0.15, (0.8, 0.8)))):
            hip = Vector((side * 0.18, y, h - 0.08))
            leg = with_material(insect_leg(f"leg{side}{i}", hip, spread, 0.72, 0.04, side), body)
            parts.append(anim_tag(leg, (1 if side > 0 else 4) + i, hip, 0.72))
    for part in parts:
        if not part.data.color_attributes.get("anim"):
            anim_tag(part, 0)
    beetle = join(parts, "beetle")
    return [bake(beetle, 512)]


def build_runner():
    """Little explorer, ~1.2 tall. Limbs stay separate objects (LegL/LegR/ArmL/ArmR) so the game can swing them."""
    suit = fabric("suit", (0.85, 0.32, 0.06))
    boots = plastic("boots", (0.18, 0.11, 0.07), rough=0.6)
    face = skin("skin")
    dark = plastic("dark", (0.03, 0.03, 0.035), rough=0.2)
    pack = fabric("pack", (0.22, 0.3, 0.2))
    helmet = plastic("helmet", (0.92, 0.9, 0.85), rough=0.25)
    bulb = glow("bulb", (1.0, 0.85, 0.35), 4.0)

    torso = join([
        with_material(sphere("torso", 0.22, (0, 0, 0.6), (1.0, 0.85, 1.25), 24, 14), suit.mat),
        with_material(cylinder("belt", 0.205, 0.06, (0, 0, 0.42), vertices=24), dark.mat),
        with_material(box("backpack", (0.3, 0.16, 0.34), (0, 0.22, 0.66), bevel=0.04, segments=3), pack.mat),
        with_material(sphere("head", 0.24, (0, -0.02, 1.02), (1.0, 0.95, 0.95), 28, 16), face.mat),
        with_material(sphere("helmet", 0.26, (0, 0.01, 1.07), (1.02, 1.0, 0.8), 28, 16), helmet.mat),
        with_material(sphere("eyeL", 0.035, (0.08, -0.22, 1.02), segments=12, rings=8), dark.mat),
        with_material(sphere("eyeR", 0.035, (-0.08, -0.22, 1.02), segments=12, rings=8), dark.mat),
        with_material(tube("antenna", [(0, 0.05, 1.27), (0.02, 0.08, 1.42), (0.05, 0.06, 1.52)], [0.012, 0.01, 0.008]), dark.mat),
        with_material(sphere("bulb", 0.045, (0.05, 0.06, 1.55), segments=12, rings=8), bulb.mat),
    ], "Body")
    tag(torso, "Body")

    limbs = []
    for side, suffix in ((1, "L"), (-1, "R")):
        leg = join([
            with_material(tube(f"thigh{suffix}", [(side * 0.1, 0, 0.42), (side * 0.1, -0.01, 0.12)], [0.075, 0.065]), suit.mat),
            with_material(box(f"boot{suffix}", (0.13, 0.22, 0.12), (side * 0.1, -0.04, 0.06), bevel=0.04, segments=3), boots.mat),
        ], f"Leg{suffix}")
        tag(leg, f"Leg{suffix}")
        arm = join([
            with_material(tube(f"arm{suffix}", [(side * 0.21, 0, 0.74), (side * 0.27, 0, 0.55), (side * 0.27, -0.03, 0.42)],
                               [0.055, 0.05, 0.045]), suit.mat),
            with_material(sphere(f"hand{suffix}", 0.05, (side * 0.27, -0.03, 0.39), segments=12, rings=8), face.mat),
        ], f"Arm{suffix}")
        tag(arm, f"Arm{suffix}")
        limbs += [leg, arm]

    runner = join([torso, *limbs], "Body")
    bake(runner, 1024, emissive=True)

    # Split the limbs back out with their pivots at hip / shoulder.
    return split_parts(runner, {"LegL": (0.1, 0, 0.42), "LegR": (-0.1, 0, 0.42),
                                "ArmL": (0.21, 0, 0.74), "ArmR": (-0.21, 0, 0.74)})


def build_gem():
    gem = Mat("gem")
    facets = gem.voronoi(scale=6.0)
    gem.color(gem.ramp(facets, [(0.0, (0.15, 1.0, 0.45)), (1.0, (0.02, 0.5, 0.2))]))
    gem.roughness(0.05)
    gem.emission((0.2, 1.0, 0.45), 2.5)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.5, location=(0, 0, 0.7))
    obj = bpy.context.active_object
    obj.name = "gem"
    obj.scale = (0.7, 0.7, 1.4)
    apply_transform(obj)
    with_material(obj, gem.mat)
    return [bake(obj, 256, emissive=True)]


def build_mug(name, color):
    """Radius 3, height 6, handle towards +X. Origin at the bottom centre of the cup."""
    ceramic = glaze(f"{name}_glaze", color)
    coffee = Mat(f"{name}_coffee")
    coffee.color(coffee.ramp(coffee.noise(scale=3.0), [(0.3, (0.02, 0.008, 0.004)), (0.8, (0.06, 0.025, 0.01))]))
    coffee.roughness(0.18)
    r, h = 3.0, 6.0
    cup = lathe("cup", [(0.0, 0.0), (r * 0.85, 0.0), (r * 0.92, 0.15), (r, h * 0.85), (r, h - 0.1), (r - 0.12, h),
                        (r - 0.3, h - 0.08), (r - 0.32, 0.5), (0.0, 0.42)])
    surface = cylinder("coffee", r - 0.3, 0.05, (0, 0, h * 0.78), vertices=48)
    handle = tube("handle", [(r - 0.1, 0, h * 0.8), (r + 1.4, 0, h * 0.85), (r + 1.6, 0, h * 0.45), (r + 1.2, 0, h * 0.25),
                             (r - 0.1, 0, h * 0.3)], [0.32, 0.36, 0.38, 0.36, 0.32], sides=12)
    mug = join([with_material(cup, ceramic.mat), with_material(surface, coffee.mat), with_material(handle, ceramic.mat)], name)
    return [bake(mug, 1024)]


def build_apple():
    """About 4.6 tall, origin at the bottom."""
    skin_mat = Mat("apple_skin")
    # Vertical red streaks over a yellow-green ground colour that shows near the stem.
    streaks = skin_mat.noise(scale=3.0, detail=8.0, roughness=0.65, vector=skin_mat.vec(scale=(7, 7, 0.6)))
    blush = skin_mat.ramp(streaks, [(0.25, (0.3, 0.005, 0.01)), (0.5, (0.55, 0.02, 0.02)), (0.7, (0.75, 0.08, 0.03)),
                                    (0.9, (0.8, 0.5, 0.08))])
    top = skin_mat.math("MAXIMUM", skin_mat.math("SUBTRACT", skin_mat.axis("Z"), 3.4), 0.0)
    near_stem = skin_mat.math("MINIMUM", skin_mat.math("MULTIPLY", top, 1.2), 1.0)
    colored = skin_mat.mix(near_stem, blush, (0.62, 0.55, 0.08))
    lenticels = skin_mat.math("LESS_THAN", skin_mat.voronoi(scale=14.0), 0.05)
    skin_mat.color(skin_mat.mix(lenticels, colored, (0.9, 0.78, 0.45)))
    skin_mat.roughness(skin_mat.gray(skin_mat.noise(scale=6.0), 0.18, 0.35))  # waxy shine
    skin_mat.normal(skin_mat.bump(lenticels, strength=0.12, distance=0.01,
                                  previous=skin_mat.bump(skin_mat.noise(scale=2.0, detail=3.0), strength=0.08, distance=0.05)))
    stem_mat = wood("apple_stem", (0.35, 0.22, 0.1), (0.15, 0.08, 0.03), "Z", 10.0, rough=0.8)

    apple = sphere("apple", 2.4, (0, 0, 2.3), (1.0, 1.0, 0.95), 40, 24)

    def shape(v):
        r = math.hypot(v.x, v.y)
        rel = v.z - 2.3
        dimple = 0.7 * math.exp(-(r / 0.7) ** 2)
        if rel > 0:
            v.z -= dimple * (rel / 2.3)
        else:
            v.z += 0.35 * dimple * (-rel / 2.3)
            v.x *= 0.92 + 0.08 * (1 + rel / 2.3)  # narrower towards the bottom
            v.y *= 0.92 + 0.08 * (1 + rel / 2.3)
        bulge = 1.0 + 0.04 * math.sin(math.atan2(v.y, v.x) * 5)
        v.x *= bulge
        v.y *= bulge
        return v

    deform(apple, shape)
    stem = tube("stem", [(0, 0, 3.9), (0.08, 0, 4.4), (0.3, 0.05, 4.8)], [0.09, 0.08, 0.07])
    leaf = sphere("leaf", 0.5, (0.65, 0.1, 4.55), (1.0, 0.45, 0.06), 16, 8, rot=(0.3, -0.5, 0.4))
    leaf_mat = plastic("leaf", (0.15, 0.4, 0.08), rough=0.5)
    obj = join([with_material(apple, skin_mat.mat), with_material(stem, stem_mat.mat), with_material(leaf, leaf_mat.mat)], "apple")
    return [bake(obj, 2048)]


def leaf_material(name, color=(0.12, 0.38, 0.06)):
    m = Mat(name)
    veins = m.wave("BANDS", "X", scale=12.0, distortion=2.0)
    m.color(m.mix(m.math("MULTIPLY", m.noise(scale=6.0), 0.4), color, tuple(c * 0.6 for c in color)))
    m.roughness(0.45)
    m.normal(m.bump(veins, strength=0.2, distance=0.01))
    m.set(("Subsurface Weight", "Subsurface"), 0.15)
    return m


def build_strawberry():
    """About 3.6 tall with its leafy cap; origin at the bottom (tip)."""
    berry = Mat("strawberry")
    cells = berry.voronoi(scale=9.0)
    seeds = berry.math("LESS_THAN", cells, 0.12)
    pits = berry.math("MINIMUM", berry.math("MULTIPLY", cells, 4.0), 1.0)  # dimples around each seed
    z = berry.axis("Z")
    ripe = berry.ramp(berry.noise(scale=3.0, detail=4.0), [(0.3, (0.45, 0.005, 0.01)), (0.75, (0.8, 0.03, 0.03))])
    shoulders = berry.math("MINIMUM", berry.math("MAXIMUM", berry.math("MULTIPLY", berry.math("SUBTRACT", z, 3.3), 3.0), 0.0), 1.0)
    flesh = berry.mix(berry.math("MULTIPLY", shoulders, 0.5), ripe, (0.95, 0.7, 0.5))
    berry.color(berry.mix(seeds, flesh, (0.85, 0.7, 0.2)))
    berry.roughness(berry.gray(berry.noise(scale=8.0), 0.12, 0.3))  # wet shine
    berry.normal(berry.bump(pits, strength=0.6, distance=0.05))
    berry.set(("Subsurface Weight", "Subsurface"), 0.3)

    body = sphere("berry", 1.5, (0, 0, 1.6), (1.0, 1.0, 1.15), 40, 28)

    def shape(v):
        t = max(0.0, min(1.0, v.z / 3.4))
        f = 0.25 + 0.75 * min(1.0, t / 0.7) ** 0.7
        v.x *= f
        v.y *= f
        return v

    deform(body, shape)
    green = leaf_material("calyx")
    parts = [with_material(body, berry.mat)]
    top = max(v.co.z for v in body.data.vertices)
    for i in range(8):
        angle = i * math.tau / 8
        parts.append(with_material(sphere(f"sepal{i}", 0.6, (math.cos(angle) * 0.6, math.sin(angle) * 0.6, top - 0.12),
                                          (1.0, 0.32, 0.05), 12, 6, rot=(0, -0.35, angle)), green))
    parts.append(with_material(tube("stem", [(0, 0, top - 0.1), (0.05, 0.02, top + 0.4), (0.18, 0.05, top + 0.7)],
                                    [0.08, 0.07, 0.06]), green))
    return [bake(ground_and_center(join(parts, "strawberry")), 1024)]


def build_orange():
    """Radius 3.5, origin at the bottom."""
    peel = Mat("orange_peel")
    pores = peel.voronoi(scale=28.0)
    peel.color(peel.ramp(peel.noise(scale=2.0, detail=5.0), [(0.2, (0.8, 0.26, 0.005)), (0.8, (0.93, 0.4, 0.02))]))
    peel.roughness(peel.gray(pores, 0.25, 0.5))
    peel.normal(peel.bump(pores, strength=0.5, distance=0.02,
                          previous=peel.bump(peel.noise(scale=1.5, detail=2.0), strength=0.1, distance=0.05)))
    peel.set(("Subsurface Weight", "Subsurface"), 0.1)
    fruit = sphere("orange", 3.5, (0, 0, 3.3), (1.0, 1.0, 0.94), 48, 32)
    button = cylinder("button", 0.35, 0.15, (0, 0, 6.6), vertices=10)
    stem_mat = leaf_material("orange_button", (0.3, 0.3, 0.08))
    obj = join([with_material(fruit, peel.mat), with_material(button, stem_mat.mat)], "orange")
    return [bake(ground_and_center(obj), 2048)]


def build_banana():
    """A banana lying on the floor, about 15 long (X), curving in the ground plane. Origin at bottom centre."""
    skin_mat = Mat("banana_skin")
    x = skin_mat.math("ABSOLUTE", skin_mat.axis("X"))
    green_ends = skin_mat.math("MINIMUM", skin_mat.math("MAXIMUM", skin_mat.math("MULTIPLY", skin_mat.math("SUBTRACT", x, 5.0), 0.6), 0.0), 1.0)
    tips = skin_mat.math("GREATER_THAN", x, 7.1)
    spots = skin_mat.math("LESS_THAN", skin_mat.voronoi(scale=2.5), 0.07)
    base = skin_mat.ramp(skin_mat.noise(scale=1.5, detail=4.0), [(0.3, (0.8, 0.55, 0.03)), (0.7, (0.95, 0.75, 0.08))])
    base = skin_mat.mix(skin_mat.math("MULTIPLY", green_ends, 0.8), base, (0.45, 0.5, 0.08))
    base = skin_mat.mix(spots, base, (0.25, 0.12, 0.03))
    skin_mat.color(skin_mat.mix(tips, base, (0.12, 0.08, 0.04)))
    skin_mat.roughness(skin_mat.gray(skin_mat.noise(scale=4.0), 0.35, 0.55))
    skin_mat.normal(skin_mat.bump(skin_mat.noise(scale=2.0, detail=2.0), strength=0.08, distance=0.05))
    points, radii = [], []
    for i in range(9):
        t = -1.0 + 2.0 * i / 8
        points.append((t * 7.0, 1.6 * (1 - t * t), 1.25))
        radii.append(1.25 * (1 - 0.55 * abs(t) ** 2.5))
    points.append((7.8, 0.0, 1.35))   # stem
    radii.append(0.35)
    banana = tube("banana", points, radii, sides=12)
    return [bake(ground_and_center(with_material(banana, skin_mat.mat)), 2048)]


def build_cherries():
    """Two cherries joined by their stems, about 3.4 tall. Origin at bottom centre."""
    cherry = Mat("cherry")
    cherry.color(cherry.ramp(cherry.noise(scale=3.0, detail=4.0), [(0.3, (0.18, 0.0, 0.01)), (0.75, (0.42, 0.01, 0.03))]))
    cherry.roughness(cherry.gray(cherry.noise(scale=10.0), 0.06, 0.15))
    cherry.normal(cherry.bump(cherry.noise(scale=1.0, detail=2.0), strength=0.05, distance=0.05))
    cherry.set(("Subsurface Weight", "Subsurface"), 0.2)
    stems = leaf_material("cherry_stem", (0.25, 0.35, 0.08))
    parts = []
    for i, (cx, cy) in enumerate(((-0.95, 0.0), (0.9, 0.35))):
        c = sphere(f"cherry{i}", 1.0, (cx, cy, 0.95), (1.0, 1.0, 0.95), 32, 20)
        deform(c, lambda v, cx=cx, cy=cy: Vector((v.x, v.y, v.z - 0.25 * math.exp(-((v.x - cx) ** 2 + (v.y - cy) ** 2) / 0.08)
                                                   if v.z > 1.4 else v.z)))
        parts.append(with_material(c, cherry.mat))
        parts.append(with_material(tube(f"stem{i}", [(cx, cy, 1.75), (cx * 0.6, cy * 0.6, 2.6), (0.1, 0.1, 3.4)],
                                        [0.06, 0.05, 0.05]), stems))
    return [bake(ground_and_center(join(parts, "cherries")), 1024)]


def build_wand():
    """Ball-lightning wand at the runner's scale: grip at the origin, pointing forward (-Y)."""
    wood_mat = wood("wand_wood", (0.45, 0.28, 0.14), (0.2, 0.11, 0.05), "Y", 30.0, rough=0.5)
    brass = metal("wand_brass", (0.85, 0.62, 0.25), rough=0.25)
    crystal = glow("wand_crystal", (0.45, 0.8, 1.0), 3.0)
    stick = tube("stick", [(0, 0.08, 0), (0.01, -0.15, 0.01), (-0.01, -0.35, 0.0), (0, -0.52, 0.01)],
                 [0.028, 0.024, 0.02, 0.016], sides=8)
    parts = [
        with_material(stick, wood_mat),
        with_material(torus("ring", 0.03, 0.008, (0, -0.52, 0.01), rot=(math.pi / 2, 0, 0), major_segments=16, minor_segments=6), brass),
        with_material(sphere("orb", 0.055, (0, -0.58, 0.01), segments=16, rings=10), crystal),
    ]
    for i in range(3):
        angle = i * math.tau / 3
        parts.append(with_material(tube(f"prong{i}", [(0, -0.52, 0.01), (math.cos(angle) * 0.04, -0.6, 0.01 + math.sin(angle) * 0.04)],
                                        [0.008, 0.005]), brass))
    return [bake(join(parts, "Wand"), 512, emissive=True)]


def build_minigun():
    """Minigun at the runner's scale: grip at the origin, barrels forward (-Y) as a child "Barrels" that spins."""
    gunmetal = metal("gunmetal", (0.12, 0.12, 0.13), rough=0.35)
    steel = metal("barrel_steel", (0.55, 0.56, 0.58), rough=0.25, brushed_axis="Y")
    accent = plastic("accent", (0.95, 0.45, 0.05), rough=0.4)
    body = join([
        with_material(box("housing", (0.12, 0.22, 0.12), (0, -0.05, 0.03), bevel=0.015, segments=2), gunmetal),
        with_material(box("handle", (0.04, 0.05, 0.1), (0, 0.04, -0.05), bevel=0.01, segments=2), gunmetal),
        with_material(box("stripe", (0.125, 0.04, 0.125), (0, -0.1, 0.03)), accent),
        with_material(cylinder("drum", 0.07, 0.08, (0.09, 0.0, 0.03), rot=(0, math.pi / 2, 0), vertices=20), accent),
    ], "Minigun")
    tag(body, "Body")
    axis = (0.0, -0.16, 0.03)
    barrel_parts = [with_material(cylinder("hub", 0.05, 0.04, (0, -0.17, 0.03), rot=(math.pi / 2, 0, 0), vertices=16), gunmetal),
                    with_material(cylinder("collar", 0.055, 0.03, (0, -0.44, 0.03), rot=(math.pi / 2, 0, 0), vertices=16), gunmetal)]
    for i in range(6):
        angle = i * math.tau / 6
        barrel_parts.append(with_material(cylinder(f"barrel{i}", 0.015, 0.36,
                                                   (math.cos(angle) * 0.035, -0.35, 0.03 + math.sin(angle) * 0.035),
                                                   rot=(math.pi / 2, 0, 0), vertices=10), steel))
    barrels = tag(join(barrel_parts, "Barrels"), "Barrels")
    gun = join([body, barrels], "Minigun")
    bake(gun, 512)
    return split_parts(gun, {"Barrels": axis})


def build_stool():
    """Seat radius 3 at height 9, origin at the bottom centre."""
    oak = wood("stool_oak", (0.62, 0.42, 0.24), (0.36, 0.22, 0.11), "Z", 4.0)
    legs_mat = wood("stool_legs", (0.55, 0.37, 0.2), (0.3, 0.18, 0.09), "X", 18.0)
    seat = cylinder("seat", 3.0, 0.8, (0, 0, 8.6), vertices=48)
    bevel = seat.modifiers.new("bevel", "BEVEL")
    bevel.width = 0.2
    bevel.segments = 3
    apply_modifiers(seat)
    smooth(seat)
    parts = [with_material(seat, oak.mat)]
    for i in range(4):
        angle = math.pi / 4 + i * math.pi / 2
        top = Vector((math.cos(angle) * 1.8, math.sin(angle) * 1.8, 8.25))
        foot = Vector((math.cos(angle) * 2.3, math.sin(angle) * 2.3, 0.0))
        parts.append(with_material(tube(f"leg{i}", [foot, top], [0.32, 0.27], smooth_curve=False, sides=12), legs_mat.mat))
    parts.append(with_material(torus("footrest", 2.15, 0.12, (0, 0, 3.0)), legs_mat.mat))
    return [bake(join(parts, "stool"), 1024)]


def build_toy_block():
    """Unit toy block (scaled per use in game, tinted with paint colours). Origin at bottom centre."""
    beech = wood("block_beech", (0.88, 0.76, 0.58), (0.7, 0.55, 0.38), "Y", 2.0, rough=0.5)
    block = box("block", (1, 1, 1), (0, 0, 0.5), bevel=0.06, segments=3)
    return [bake(with_material(block, beech.mat), 512)]


def build_ball():
    """Rubber ball, diameter 8, origin at the bottom."""
    rubber = Mat("ball_rubber")
    band = rubber.math("LESS_THAN", rubber.math("ABSOLUTE", rubber.math("SUBTRACT", rubber.axis("Z"), 4.0)), 0.9)
    star = rubber.math("LESS_THAN", rubber.voronoi(scale=0.35), 0.18)
    base = rubber.mix(band, (0.08, 0.35, 0.85), (0.98, 0.8, 0.12))
    rubber.color(rubber.mix(star, base, (0.9, 0.12, 0.1)))
    rubber.roughness(rubber.gray(rubber.noise(scale=12.0), 0.35, 0.55))
    rubber.normal(rubber.bump(rubber.noise(scale=60.0, detail=1.0), strength=0.05, distance=0.01))
    ball = sphere("ball", 4.0, (0, 0, 4.0), segments=48, rings=28)
    return [bake(with_material(ball, rubber.mat), 1024)]


def build_dog_bowl():
    """Steel bowl, radius 4, height 1.5, filled with kibble. Origin at the bottom."""
    steel = metal("bowl_steel", rough=0.22, brushed_axis="Z")
    kibble_mat = Mat("kibble")
    pores = kibble_mat.voronoi(scale=12.0)
    kibble_mat.color(kibble_mat.ramp(kibble_mat.noise(scale=4.0), [(0.3, (0.3, 0.15, 0.06)), (0.7, (0.5, 0.28, 0.12))]))
    kibble_mat.roughness(0.75)
    kibble_mat.normal(kibble_mat.bump(pores, strength=0.4, distance=0.02))
    bowl = lathe("bowl", [(0.0, 0.0), (3.4, 0.0), (3.6, 0.1), (4.0, 1.4), (4.1, 1.5), (3.9, 1.5), (3.5, 0.35), (0.0, 0.3)])
    parts = [with_material(bowl, steel.mat)]
    rng = random.Random(4)
    for i in range(70):
        angle, dist = rng.random() * math.tau, math.sqrt(rng.random()) * 3.2
        z = 0.55 + (1.0 - dist / 3.2) * 0.6 + rng.random() * 0.15
        k = sphere(f"kibble{i}", 0.32, (math.cos(angle) * dist, math.sin(angle) * dist, z), (1.0, 1.0, 0.6), 8, 6,
                   rot=(rng.random(), rng.random(), rng.random()))
        parts.append(with_material(k, kibble_mat.mat))
    return [bake(join(parts, "dog_bowl"), 1024)]


def build_cereal_box():
    """4 wide (X) x 7 deep (Y) x 14 tall, big printed faces towards +-X. Origin at the bottom centre."""
    print_mat = Mat("cereal_print")
    y, z = print_mat.axis("Y"), print_mat.axis("Z")
    # Big red sun logo on the big faces, a white banner and blocky "text" lines.
    dist = print_mat.math("SQRT", print_mat.math("ADD", print_mat.math("POWER", y, 2.0),
                                                 print_mat.math("POWER", print_mat.math("SUBTRACT", z, 9.0), 2.0)))
    logo = print_mat.math("LESS_THAN", dist, 2.3)
    banner = print_mat.math("MULTIPLY", print_mat.math("GREATER_THAN", z, 3.5), print_mat.math("LESS_THAN", z, 5.5))
    text = print_mat.math("MULTIPLY", banner, print_mat.math("GREATER_THAN", print_mat.wave("BANDS", "Y", scale=1.6), 0.6))
    base = print_mat.ramp(print_mat.math("MULTIPLY", z, 0.07), [(0.0, (0.95, 0.65, 0.05)), (1.0, (0.98, 0.82, 0.15))])
    colored = print_mat.mix(logo, base, (0.85, 0.12, 0.08))
    colored = print_mat.mix(banner, colored, (0.97, 0.95, 0.9))
    colored = print_mat.mix(text, colored, (0.1, 0.2, 0.55))
    print_mat.color(colored)
    print_mat.roughness(print_mat.gray(print_mat.noise(scale=10.0), 0.45, 0.65))
    print_mat.normal(print_mat.bump(print_mat.noise(scale=30.0, detail=2.0), strength=0.08, distance=0.01))
    cereal = box("cereal_box", (4, 7, 14), (0, 0, 7), bevel=0.06, segments=2)
    return [bake(with_material(cereal, print_mat.mat), 1024)]


def build_book(name, cover_color):
    """Hardcover book, 9 (X) x 6.5 (Y) x 1.6 tall. Origin at the bottom centre; spine on -X."""
    cloth = fabric(f"{name}_cloth", cover_color, rough=0.75)
    pages = Mat(f"{name}_pages")
    lines = pages.wave("BANDS", "Z", scale=40.0)
    pages.color(pages.ramp(lines, [(0.4, (0.82, 0.78, 0.66)), (0.6, (0.95, 0.92, 0.82))]))
    pages.roughness(0.85)
    pages.normal(pages.bump(lines, strength=0.2, distance=0.01))
    cover_t = 0.12
    parts = [
        with_material(box("bottom", (9, 6.5, cover_t), (0, 0, cover_t / 2), bevel=0.03), cloth.mat),
        with_material(box("top", (9, 6.5, cover_t), (0, 0, 1.6 - cover_t / 2), bevel=0.03), cloth.mat),
        with_material(box("pages", (8.6, 6.2, 1.6 - 2 * cover_t), (0.15, 0, 0.8)), pages.mat),
        with_material(cylinder("spine", 0.8, 6.5, (-4.35, 0, 0.8), rot=(math.pi / 2, 0, 0), vertices=24), cloth.mat),
    ]
    smooth(parts[-1])
    return [bake(join(parts, name), 1024)]


def build_pencil():
    """12 long along X, 0.5 thick, hexagonal. Origin at the bottom centre."""
    paint = painted("pencil_paint", (0.95, 0.75, 0.08), rough=0.3, brush_axis="X")
    cedar = wood("pencil_wood", (0.85, 0.65, 0.45), (0.7, 0.5, 0.32), "X", 6.0)
    graphite = metal("graphite", (0.12, 0.12, 0.13), rough=0.5)
    ferrule = metal("ferrule", (0.75, 0.7, 0.5), rough=0.3)
    eraser = plastic("eraser", (0.95, 0.55, 0.6), rough=0.8)
    r = 0.25
    parts = [
        with_material(cylinder("shaft", r, 9.6, (-0.6, 0, r), rot=(0, math.pi / 2, 0), vertices=6), paint.mat),
        with_material(cylinder("cone", r, 1.4, (4.9, 0, r), rot=(0, math.pi / 2, 0), vertices=12, radius_top=0.06), cedar.mat),
        with_material(cylinder("tip", 0.06, 0.25, (5.72, 0, r), rot=(0, math.pi / 2, 0), vertices=8, radius_top=0.0), graphite.mat),
        with_material(cylinder("ferrule", r * 1.05, 0.6, (-5.7, 0, r), rot=(0, math.pi / 2, 0), vertices=16), ferrule.mat),
        with_material(cylinder("eraser", r, 0.5, (-6.25, 0, r), rot=(0, math.pi / 2, 0), vertices=16), eraser.mat),
    ]
    return [bake(join(parts, "pencil"), 512)]


def build_cutting_board():
    """Board 24 long (X) x 4 wide (Y) x 0.4 thick with a handle hole. Origin at the bottom centre."""
    board_wood = wood("board_wood", (0.78, 0.55, 0.32), (0.5, 0.32, 0.16), "X", 1.5, rough=0.6)
    board = box("board", (24, 4, 0.4), (0, 0, 0.2), bevel=0.12, segments=3)
    hole = cylinder("hole", 0.55, 2.0, (10.8, 0, 0.2), vertices=24)
    boolean = board.modifiers.new("hole", "BOOLEAN")
    boolean.object = hole
    boolean.operation = "DIFFERENCE"
    apply_modifiers(board)
    bpy.data.objects.remove(hole)
    return [bake(with_material(board, board_wood.mat), 1024)]


def build_counter():
    """Kitchen island: 52 x 26 cabinets, 10 tall with a 53 x 27 marble top. Origin at the bottom centre."""
    marble = Mat("marble")
    veins = marble.wave("BANDS", "X", scale=0.35, distortion=14.0, detail=10.0, vector=marble.vec(scale=(1, 0.6, 1)))
    cloud = marble.noise(scale=0.6, detail=6.0)
    vein_line = marble.math("LESS_THAN", marble.math("ABSOLUTE", marble.math("SUBTRACT", veins, 0.5)), 0.035)
    base = marble.ramp(cloud, [(0.3, (0.82, 0.82, 0.8)), (0.7, (0.95, 0.95, 0.93))])
    marble.color(marble.mix(marble.math("MULTIPLY", vein_line, 0.7), base, (0.35, 0.35, 0.38)))
    marble.roughness(marble.gray(cloud, 0.06, 0.14))
    marble.normal(marble.bump(vein_line, strength=0.05, distance=0.01))
    cabinet = painted("cabinet_paint", (0.42, 0.55, 0.5), rough=0.45)
    door = painted("door_paint", (0.47, 0.6, 0.55), rough=0.4)
    plinth = plastic("plinth", (0.12, 0.13, 0.13), rough=0.6)
    steel = metal("handle_steel", rough=0.2)

    w, d, h = 52.0, 26.0, 10.0
    parts = [
        with_material(box("carcass", (w, d, h - 0.6 - 0.6), (0, 0, 0.6 + (h - 1.2) / 2)), cabinet.mat),
        with_material(box("plinth", (w - 0.8, d - 0.8, 0.6), (0, 0, 0.3)), plinth.mat),
        with_material(box("top", (w + 1, d + 1, 0.6), (0, 0, h - 0.3), bevel=0.1, segments=3), marble.mat),
    ]

    def doors(count, length, along_x, offset, sign):
        for i in range(count):
            t = -length / 2 + length / count * (i + 0.5)
            size_along = length / count - 0.5
            center = (t, sign * offset, 4.8) if along_x else (sign * offset, t, 4.8)
            size = (size_along, 0.25, 7.6) if along_x else (0.25, size_along, 7.6)
            parts.append(with_material(box(f"door{along_x}{sign}{i}", size, center, bevel=0.08, segments=2), door.mat))
            # Raised inner panel.
            inner = (size_along - 1.4, 0.15, 6.0) if along_x else (0.15, size_along - 1.4, 6.0)
            inner_center = (center[0], center[1] + sign * 0.15, 4.8) if along_x else (center[0] + sign * 0.15, center[1], 4.8)
            parts.append(with_material(box(f"panel{along_x}{sign}{i}", inner, inner_center, bevel=0.15, segments=2), door.mat))
            # Bar handle near the top edge of each door.
            hx = center[0] + (size_along / 2 - 0.8 if along_x else sign * 0.5)
            hy = center[1] + (sign * 0.5 if along_x else size_along / 2 - 0.8)
            parts.append(with_material(cylinder(f"handle{along_x}{sign}{i}", 0.15, 1.8, (hx, hy, 6.8), vertices=12), steel.mat))

    doors(6, w - 1, True, d / 2 + 0.12, 1)
    doors(6, w - 1, True, d / 2 + 0.12, -1)
    doors(3, d - 1, False, w / 2 + 0.12, 1)
    doors(3, d - 1, False, w / 2 + 0.12, -1)
    return [bake(join(parts, "counter"), 2048)]


def build_floor_textures():
    """Tileable 8 x 8 unit patch (2 x 2 tiles of 4 units) baked into textures/floor_*.png."""
    tiles = Mat("floor")
    x, y = tiles.axis("X"), tiles.axis("Y")
    fx = tiles.math("FRACT", tiles.math("DIVIDE", x, 4.0))
    fy = tiles.math("FRACT", tiles.math("DIVIDE", y, 4.0))
    edge = tiles.math("MINIMUM", tiles.math("MINIMUM", fx, tiles.math("SUBTRACT", 1.0, fx)),
                      tiles.math("MINIMUM", fy, tiles.math("SUBTRACT", 1.0, fy)))
    grout = tiles.math("LESS_THAN", edge, 0.02)
    parity = tiles.math("MODULO", tiles.math("ADD", tiles.math("FLOOR", tiles.math("DIVIDE", x, 4.0)),
                                             tiles.math("FLOOR", tiles.math("DIVIDE", y, 4.0))), 2.0)
    parity = tiles.math("ABSOLUTE", parity)
    cloud = tiles.noise(scale=0.8, detail=6.0)
    light = tiles.ramp(cloud, [(0.3, (0.55, 0.52, 0.46)), (0.7, (0.64, 0.6, 0.53))])
    dark = tiles.ramp(cloud, [(0.3, (0.18, 0.22, 0.24)), (0.7, (0.25, 0.29, 0.31))])
    tile_color = tiles.mix(parity, light, dark)
    tiles.color(tiles.mix(grout, tile_color, (0.3, 0.28, 0.25)))
    tiles.roughness(tiles.mix(grout, tiles.gray(cloud, 0.12, 0.3), (0.85, 0.85, 0.85)))
    tiles.normal(tiles.bump(tiles.math("SUBTRACT", 1.0, grout), strength=0.6, distance=0.03,
                            previous=tiles.bump(tiles.noise(scale=6.0), strength=0.05, distance=0.01)))
    bpy.ops.mesh.primitive_plane_add(size=8, location=(4, 4, 0))
    plane = bpy.context.active_object
    plane.name = "floor"
    with_material(plane, tiles.mat)

    folder = os.path.join(ARGS.out, "textures")
    os.makedirs(folder, exist_ok=True)
    size = max(64, int(1024 * TEX_SCALE))
    for suffix, kind, socket, space in (("albedo", "EMIT", "Base Color", "sRGB"), ("roughness", "EMIT", "Roughness", "Non-Color"),
                                        ("normal", "NORMAL", None, "Non-Color")):
        image = bpy.data.images.new(f"floor_{suffix}", size, size, alpha=False)
        image.colorspace_settings.name = space
        _bake_pass(plane, image, kind, socket=socket)
        image.filepath_raw = os.path.join(folder, f"floor_{suffix}.png")
        image.file_format = "PNG"
        image.save()
        print(f"  wrote textures/floor_{suffix}.png")
    return None


MODELS = {
    "ant": build_ant,
    "beetle": build_beetle,
    "runner": build_runner,
    "gem": build_gem,
    "mug_white": lambda: build_mug("mug_white", (0.92, 0.92, 0.9)),
    "mug_red": lambda: build_mug("mug_red", (0.72, 0.12, 0.1)),
    "apple": build_apple,
    "strawberry": build_strawberry,
    "orange": build_orange,
    "banana": build_banana,
    "cherries": build_cherries,
    "wand": build_wand,
    "minigun": build_minigun,
    "stool": build_stool,
    "toy_block": build_toy_block,
    "ball": build_ball,
    "dog_bowl": build_dog_bowl,
    "cereal_box": build_cereal_box,
    "book_blue": lambda: build_book("book_blue", (0.12, 0.22, 0.5)),
    "book_gold": lambda: build_book("book_gold", (0.7, 0.52, 0.18)),
    "pencil": build_pencil,
    "cutting_board": build_cutting_board,
    "counter": build_counter,
    "floor": build_floor_textures,
}


def main():
    global ARGS, TEX_SCALE
    ARGS = parse_args()
    TEX_SCALE = 0.5 if ARGS.draft else 1.0
    names = ARGS.only or list(MODELS)
    unknown = [n for n in names if n not in MODELS]
    if unknown:
        raise SystemExit(f"Unknown model(s): {unknown}. Choose from: {', '.join(MODELS)}")

    print(f"Generating {len(names)} model(s) into {ARGS.out}")
    for name in names:
        print(f"- {name}")
        clear_scene()
        setup_render()
        objects = MODELS[name]()
        if objects:
            export(objects, name)
    print("Done.")


if __name__ == "__main__":
    main()
