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
    parser.add_argument("--icons", action="store_true", help="also render a square icon with a transparent background into <out>/icons/")
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

    def seamless_noise(self, period, freq=(1.0, 1.0), detail=4.0, roughness=0.5, distortion=0.0, x=None, y=None, extra=None):
        """Noise over the XY plane that repeats every `period` units in X and Y (the plane is wrapped onto
        a 4D torus), so baked textures tile without seams. freq: features per unit along X and Y."""
        x = x if x is not None else self.axis("X")
        y = y if y is not None else self.axis("Y")
        k = 2 * math.pi / period
        rx, ry = freq[0] * period / (2 * math.pi), freq[1] * period / (2 * math.pi)
        ax, ay = self.math("MULTIPLY", x, k), self.math("MULTIPLY", y, k)
        combine = self.nodes.new("ShaderNodeCombineXYZ")
        self.links.new(self.math("MULTIPLY", self.math("COSINE", ax), rx), combine.inputs[0])
        self.links.new(self.math("MULTIPLY", self.math("SINE", ax), rx), combine.inputs[1])
        cy = self.math("MULTIPLY", self.math("COSINE", ay), ry)
        self.links.new(self.math("ADD", cy, extra) if extra is not None else cy, combine.inputs[2])
        n = self.nodes.new("ShaderNodeTexNoise")
        n.noise_dimensions = "4D"
        self.links.new(combine.outputs[0], n.inputs["Vector"])
        self.links.new(self.math("MULTIPLY", self.math("SINE", ay), ry), n.inputs["W"])
        n.inputs["Scale"].default_value = 1.0
        n.inputs["Detail"].default_value = detail
        n.inputs["Roughness"].default_value = roughness
        n.inputs["Distortion"].default_value = distortion
        return n.outputs["Fac"]

    def random(self, a, b=0.0):
        """Stable pseudo-random 0..1 for integer cell coordinates (a, b): per plank, per strip, ..."""
        combine = self.nodes.new("ShaderNodeCombineXYZ")
        for i, value in enumerate((a, b)):
            if isinstance(value, bpy.types.NodeSocket):
                self.links.new(value, combine.inputs[i])
            else:
                combine.inputs[i].default_value = value
        n = self.nodes.new("ShaderNodeTexWhiteNoise")
        n.noise_dimensions = "3D"
        self.links.new(combine.outputs[0], n.inputs["Vector"])
        return n.outputs["Value"]

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


def leather(name, color, rough=0.5):
    """Worn leather: fine pebbled grain, creases and darker, shinier wear."""
    m = Mat(name)
    grain = m.voronoi(scale=45.0)
    creases = m.noise(scale=3.0, detail=6.0, distortion=1.0)
    m.color(m.mix(m.math("MULTIPLY", creases, 0.6), color, tuple(c * 0.55 for c in color)))
    m.roughness(m.gray(creases, rough - 0.15, rough + 0.15))
    m.normal(m.bump(grain, strength=0.25, distance=0.01, previous=m.bump(creases, strength=0.2, distance=0.05)))
    return m


def gold(name, dirt=0.5):
    """Old gold: polished highs, dark grime collected in big soft patches."""
    m = Mat(name)
    grime = m.noise(scale=2.5, detail=6.0, roughness=0.6)
    m.color(m.mix(m.math("MULTIPLY", m.math("MAXIMUM", m.math("SUBTRACT", grime, 0.45), 0.0), dirt * 3.0),
                  (1.0, 0.72, 0.28), (0.25, 0.15, 0.05)))
    m.metal(1.0)
    m.roughness(m.gray(grime, 0.15, 0.6))
    m.normal(m.bump(m.noise(scale=30.0, detail=3.0), strength=0.15, distance=0.01))
    return m


def mahogany(name, axis="Z", scale=1.5):
    return wood(name, (0.32, 0.12, 0.06), (0.13, 0.04, 0.02), axis, scale, rough=0.35)


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
    if ARGS.icons:
        render_preview(objects, name, icon=True)


def render_preview(objects, name, icon=False):
    """Studio render for eyeballing a model, or with icon=True a 256 px icon on a transparent
    background for the game's menu cards (assets/icons/)."""
    lo = Vector((min(v[i] for o in objects for v in (o.matrix_world @ Vector(c) for c in o.bound_box)) for i in range(3)))
    hi = Vector((max(v[i] for o in objects for v in (o.matrix_world @ Vector(c) for c in o.bound_box)) for i in range(3)))
    center, extent = (lo + hi) / 2, (hi - lo).length

    camera = link(bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam")))
    camera.location = center + Vector((0.9, -1.4, 0.9)).normalized() * extent * 1.4
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
    scene.render.resolution_x = scene.render.resolution_y = 256 if icon else 480
    scene.cycles.samples = 64 if icon else 48
    scene.cycles.use_denoising = False  # not every Blender build ships the denoiser
    scene.render.film_transparent = icon
    scene.render.image_settings.color_mode = "RGBA" if icon else "RGB"
    if icon:
        camera.location = center + Vector((0.9, -1.4, 0.7)).normalized() * extent * 1.45
        camera.rotation_euler = (center - camera.location).to_track_quat("-Z", "Y").to_euler()
    folder = os.path.join(ARGS.out, "icons" if icon else "previews")
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
    base = shell.ramp(var, [(0.3, (0.16, 0.05, 0.02)), (0.7, (0.42, 0.17, 0.06))])  # copper-bronze
    shell.color(shell.mix(seam, base, (0.03, 0.01, 0.005)))
    shell.metal(0.8)
    shell.roughness(shell.gray(var, 0.12, 0.3))
    shell.normal(shell.bump(stripes, strength=0.25, distance=0.01, previous=shell.bump(seam, strength=0.6, distance=0.02)))
    body = chitin("beetle_body", (0.02, 0.015, 0.012), (0.08, 0.05, 0.04), rough=0.3)

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
    """The hero, 1.35 tall with realistic proportions (about 7.5 heads), in the spirit of early-90s
    cinematic platformers: short brown hair, a brown leather bomber jacket worn open over a white
    T-shirt, blue jeans, brown boots, a holster belt.
    Limbs stay separate objects (LegL/LegR/ArmL/ArmR) so the game can swing them; a hanging hand
    sits about 0.47 below the shoulder pivot."""
    jacket = leather("jacket", (0.32, 0.17, 0.08), rough=0.45)
    collar = leather("collar", (0.22, 0.11, 0.05), rough=0.5)
    tee = fabric("tee", (0.86, 0.85, 0.8), rough=0.9)
    denim = Mat("denim")
    twill = denim.wave("BANDS", "Z", scale=160.0, vector=denim.vec(scale=(1, 1, 1), offset=(0, 0, 0)))
    denim.color(denim.mix(denim.math("MULTIPLY", denim.noise(scale=6.0, detail=6.0), 0.5),
                          (0.12, 0.18, 0.32), (0.25, 0.32, 0.48)))
    denim.roughness(0.85)
    denim.normal(denim.bump(twill, strength=0.15, distance=0.004))
    boot = leather("boot", (0.2, 0.11, 0.05), rough=0.45)
    sole = plastic("sole", (0.05, 0.04, 0.035), rough=0.8)
    belt = leather("belt", (0.08, 0.05, 0.035), rough=0.4)
    steel = metal("buckle", (0.7, 0.7, 0.72), rough=0.3)
    face = skin("skin", (0.86, 0.64, 0.52))
    hair = fabric("hair", (0.2, 0.11, 0.06), rough=0.65)
    dark = plastic("eyes", (0.04, 0.03, 0.03), rough=0.1)

    # Proportions (z up): feet 0, knees 0.37, hips 0.68, waist 0.8, chest 0.98, shoulders 1.1,
    # chin 1.14, head top 1.35.
    # Jacket: an egg tapered from broad shoulders to the waist, with the white tee showing down the front.
    body = sphere("jacket_body", 0.15, (0, 0.01, 0.94), (1.2, 0.62, 1.42), 32, 18)
    deform(body, lambda v: Vector((v.x * (0.78 + 0.32 * min(1.0, max(0.0, (v.z - 0.76) / 0.3))), v.y, v.z)))
    parts = [
        with_material(body, jacket),
        with_material(box("tee", (0.085, 0.02, 0.3), (0, -0.085, 0.95), bevel=0.02, segments=3), tee),
        with_material(torus("jacket_collar", 0.058, 0.016, (0, 0.012, 1.115), rot=(0.25, 0, 0), major_segments=20, minor_segments=8), collar),
        with_material(cylinder("belt", 0.125, 0.04, (0, 0, 0.73), vertices=24), belt),
        with_material(box("buckle", (0.04, 0.012, 0.03), (0, -0.112, 0.73), bevel=0.004), steel),
        with_material(box("holster", (0.04, 0.06, 0.12), (-0.135, 0.0, 0.67), bevel=0.012, segments=2), belt),
        with_material(sphere("hips", 0.13, (0, 0, 0.7), (1.0, 0.72, 0.6), 20, 10), denim.mat),
        with_material(cylinder("neck", 0.04, 0.08, (0, 0.0, 1.14), vertices=16), face),
        # Head: slightly long face, short swept hair with a side parting.
        with_material(sphere("head", 0.085, (0, -0.005, 1.25), (0.88, 0.98, 1.12), 28, 16), face),
        with_material(sphere("jaw", 0.06, (0, -0.03, 1.195), (1.0, 0.9, 0.7), 16, 10), face),
        with_material(sphere("nose", 0.014, (0, -0.088, 1.245), (0.8, 1.0, 1.5), 10, 8), face),
        # Short hair: a cap over the top and back of the skull, a little swept-up fringe at the front.
        with_material(sphere("hair", 0.09, (0.0, 0.012, 1.272), (0.93, 1.0, 0.92), 24, 12), hair),
        with_material(sphere("fringe", 0.035, (0.015, -0.06, 1.322), (1.5, 0.9, 0.55), 14, 8, rot=(0.35, 0, 0.15)), hair),
    ]
    hair_obj = parts[-2]
    # Cut the cap off below a line that rises from the nape to the forehead, so the face stays clear.
    deform(hair_obj, lambda v: Vector((v.x, v.y, max(v.z, 1.21 + 0.09 * max(0.0, min(1.0, (0.05 - v.y) / 0.13))))))
    for side in (1, -1):
        parts.append(with_material(sphere(f"eye{side}", 0.011, (side * 0.03, -0.077, 1.262), (1.0, 0.6, 0.7), 10, 6), dark))
        parts.append(with_material(tube(f"brow{side}", [(side * 0.015, -0.08, 1.285), (side * 0.045, -0.077, 1.287)], [0.006, 0.005]), hair))
        parts.append(with_material(sphere(f"ear{side}", 0.018, (side * 0.077, 0.0, 1.25), (0.5, 0.9, 1.3), 10, 8), face))
        # Jacket fronts hanging open either side of the white tee, closing in towards the collar.
        parts.append(with_material(box(f"lapel{side}", (0.035, 0.02, 0.26), (side * 0.058, -0.087, 0.96), bevel=0.006, segments=2,
                                       rot=(0, -side * 0.12, 0)), collar))
    torso = tag(join(parts, "Body"), "Body")

    limbs = []
    for side, suffix in ((1, "L"), (-1, "R")):
        leg = join([
            with_material(tube(f"jeans{suffix}", [(side * 0.07, 0, 0.7), (side * 0.072, -0.01, 0.37), (side * 0.072, 0.0, 0.1)],
                               [0.062, 0.048, 0.042]), denim.mat),
            with_material(box(f"boot{suffix}", (0.085, 0.19, 0.1), (side * 0.072, -0.035, 0.06), bevel=0.03, segments=3), boot),
            with_material(box(f"sole{suffix}", (0.09, 0.2, 0.025), (side * 0.072, -0.035, 0.012), bevel=0.008, segments=2), sole),
        ], f"Leg{suffix}")
        tag(leg, f"Leg{suffix}")
        arm = join([
            with_material(sphere(f"deltoid{suffix}", 0.046, (side * 0.17, 0.005, 1.06), (1.0, 1.0, 0.9), 16, 10), jacket),
            with_material(tube(f"sleeve{suffix}", [(side * 0.185, 0, 1.05), (side * 0.2, 0.01, 0.86), (side * 0.205, -0.02, 0.69)],
                               [0.046, 0.042, 0.038]), jacket),
            with_material(cylinder(f"cuff{suffix}", 0.04, 0.03, (side * 0.205, -0.02, 0.68), vertices=14), collar),
            with_material(sphere(f"hand{suffix}", 0.032, (side * 0.205, -0.025, 0.635), (0.85, 1.0, 1.3), 12, 8), face),
            with_material(sphere(f"thumb{suffix}", 0.013, (side * 0.19, -0.05, 0.65), segments=8, rings=6), face),
        ], f"Arm{suffix}")
        tag(arm, f"Arm{suffix}")
        limbs += [leg, arm]

    runner = join([torso, *limbs], "Body")
    bake(runner, 2048)

    # Split the limbs back out with their pivots at hip / shoulder.
    return split_parts(runner, {"LegL": (0.07, 0, 0.7), "LegR": (-0.07, 0, 0.7),
                                "ArmL": (0.17, 0, 1.08), "ArmR": (-0.17, 0, 1.08)})


def build_gem():
    """XP crystal: a cut amber sugar crystal with a warm inner glow (not neon)."""
    gem = Mat("gem")
    facets = gem.voronoi(scale=6.0)
    gem.color(gem.ramp(facets, [(0.0, (1.0, 0.72, 0.3)), (1.0, (0.55, 0.22, 0.03))]))
    gem.roughness(0.08)
    gem.emission((1.0, 0.55, 0.15), 0.9)
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
    top = max(v.co.z for v in fruit.data.vertices)
    deform(fruit, lambda v: Vector((v.x, v.y, v.z - 0.25 * math.exp(-(v.x * v.x + v.y * v.y) / 0.5) if v.z > 3.3 else v.z)))
    calyx_mat = Mat("orange_calyx")
    calyx_mat.color(calyx_mat.ramp(calyx_mat.noise(scale=20.0), [(0.3, (0.12, 0.13, 0.04)), (0.7, (0.3, 0.28, 0.1))]))
    calyx_mat.roughness(0.75)
    calyx_mat.normal(calyx_mat.bump(calyx_mat.noise(scale=60.0), strength=0.4, distance=0.01))
    parts = [with_material(fruit, peel.mat)]
    for i in range(5):  # five dried sepals in a star
        angle = i * math.tau / 5
        parts.append(with_material(sphere(f"sepal{i}", 0.22, (math.cos(angle) * 0.2, math.sin(angle) * 0.2, top - 0.22),
                                          (1.0, 0.55, 0.18), 10, 6, rot=(0, 0, angle)), calyx_mat))
    parts.append(with_material(cylinder("nub", 0.09, 0.22, (0, 0, top - 0.08), vertices=8, radius_top=0.07), calyx_mat))
    obj = join(parts, "orange")
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


def butcher_block(name):
    """End-to-end oak strips glued into a worktop: each strip its own tone, grain along X."""
    m = Mat(name)
    y = m.axis("Y")
    strip = m.math("FLOOR", m.math("DIVIDE", y, 1.2))
    tone = m.random(strip)
    grain = m.noise(scale=1.0, detail=6.0, distortion=0.4, vector=m.vec(scale=(0.25, 6.0, 6.0)))
    flecks = m.noise(scale=1.0, detail=2.0, vector=m.vec(scale=(1.5, 30.0, 30.0)))
    base = m.ramp(grain, [(0.25, (0.12, 0.055, 0.02)), (0.75, (0.36, 0.19, 0.08))])  # dark oiled walnut-oak
    base = m.mix(m.math("MULTIPLY", tone, 0.55), base, (0.09, 0.045, 0.018))
    joint = m.math("LESS_THAN", m.math("ABSOLUTE", m.math("SUBTRACT", m.math("FRACT", m.math("DIVIDE", y, 1.2)), 0.5)), 0.49)
    m.color(m.mix(m.math("SUBTRACT", 1.0, joint), m.mix(m.math("MULTIPLY", flecks, 0.25), base, (0.2, 0.1, 0.04)), (0.1, 0.05, 0.02)))
    m.roughness(m.gray(m.noise(scale=0.5, detail=3.0), 0.3, 0.55))  # oiled, worn in places
    m.normal(m.bump(flecks, strength=0.12, distance=0.01, previous=m.bump(joint, strength=0.3, distance=0.01)))
    return m


def build_counter(name="counter", w=52.0, d=26.0, doors_long=6, doors_short=3, texture=2048):
    """Kitchen cabinets w x d, 10 tall, with a butcher-block top overhanging by 0.5 all round.
    Navy painted doors with raised panels and brass bar handles. Origin at the bottom centre."""
    top_mat = butcher_block(f"{name}_top")
    cabinet = painted(f"{name}_paint", (0.07, 0.1, 0.17), rough=0.45)
    door = painted(f"{name}_door", (0.1, 0.14, 0.24), rough=0.4)
    plinth = plastic(f"{name}_plinth", (0.05, 0.05, 0.06), rough=0.6)
    brass = metal(f"{name}_brass", (0.8, 0.58, 0.28), rough=0.25)

    h = 10.0
    parts = [
        with_material(box("carcass", (w, d, h - 0.8 - 0.6), (0, 0, 0.6 + (h - 1.4) / 2)), cabinet.mat),
        with_material(box("plinth", (w - 0.8, d - 0.8, 0.6), (0, 0, 0.3)), plinth.mat),
        with_material(box("top", (w + 1, d + 1, 0.8), (0, 0, h - 0.4), bevel=0.12, segments=3), top_mat.mat),
    ]

    def doors(count, length, along_x, offset, sign):
        for i in range(count):
            t = -length / 2 + length / count * (i + 0.5)
            size_along = length / count - 0.5
            center = (t, sign * offset, 4.8) if along_x else (sign * offset, t, 4.8)
            size = (size_along, 0.25, 7.4) if along_x else (0.25, size_along, 7.4)
            parts.append(with_material(box(f"door{along_x}{sign}{i}", size, center, bevel=0.08, segments=2), door.mat))
            # Raised inner panel.
            inner = (size_along - 1.4, 0.15, 5.8) if along_x else (0.15, size_along - 1.4, 5.8)
            inner_center = (center[0], center[1] + sign * 0.15, 4.8) if along_x else (center[0] + sign * 0.15, center[1], 4.8)
            parts.append(with_material(box(f"panel{along_x}{sign}{i}", inner, inner_center, bevel=0.15, segments=2), door.mat))
            # Brass bar handle near the top edge of each door.
            hx = center[0] + (size_along / 2 - 0.8 if along_x else sign * 0.5)
            hy = center[1] + (sign * 0.5 if along_x else size_along / 2 - 0.8)
            parts.append(with_material(cylinder(f"handle{along_x}{sign}{i}", 0.15, 1.8, (hx, hy, 6.6), vertices=12), brass.mat))

    doors(doors_long, w - 1, True, d / 2 + 0.12, 1)
    doors(doors_long, w - 1, True, d / 2 + 0.12, -1)
    doors(doors_short, d - 1, False, w / 2 + 0.12, 1)
    doors(doors_short, d - 1, False, w / 2 + 0.12, -1)
    return [bake(join(parts, name), texture)]


def build_fridge():
    """Stainless steel fridge-freezer, 24 wide (X) x 20 deep (Y) x 45 tall, doors facing -Y. Origin at bottom centre."""
    steel = metal("fridge_steel", (0.72, 0.73, 0.75), rough=0.3, brushed_axis="Z")
    dark = plastic("fridge_gasket", (0.04, 0.04, 0.045), rough=0.6)
    handle_steel = metal("fridge_handle", (0.85, 0.86, 0.88), rough=0.15)
    w, d, h = 24.0, 20.0, 45.0
    split = 30.0  # freezer below, fridge above
    parts = [
        with_material(box("body", (w, d - 1.0, h), (0, 0.5, h / 2), bevel=0.3, segments=3), steel),
        with_material(box("freezer_door", (w - 0.4, 1.0, split - 1.2), (0, -d / 2 + 0.5, 0.8 + (split - 1.2) / 2), bevel=0.25, segments=3), steel),
        with_material(box("fridge_door", (w - 0.4, 1.0, h - split - 0.6), (0, -d / 2 + 0.5, split + (h - split - 0.6) / 2), bevel=0.25, segments=3), steel),
        with_material(box("gap", (w - 0.2, 0.9, 0.35), (0, -d / 2 + 0.55, split - 0.2)), dark),
        with_material(box("kick", (w - 1.0, 0.6, 0.8), (0, -d / 2 + 0.6, 0.4)), dark),
    ]
    for z0, z1 in ((split + 2.0, h - 2.0), (3.0, split - 3.0)):
        x = w / 2 - 2.5
        parts.append(with_material(tube(f"handle{z0}", [(x, -d / 2 - 0.1, z0), (x, -d / 2 - 1.1, z0 + 0.6),
                                                       (x, -d / 2 - 1.1, z1 - 0.6), (x, -d / 2 - 0.1, z1)],
                                        [0.3, 0.3, 0.3, 0.3], smooth_curve=False, sides=12), handle_steel))
    return [bake(join(parts, "fridge"), 2048)]


OAK = ((0.12, 0.06, 0.025), (0.25, 0.14, 0.06), (0.4, 0.24, 0.11), (0.42, 0.27, 0.13), (0.09, 0.05, 0.02))
WALNUT = ((0.05, 0.02, 0.01), (0.12, 0.05, 0.022), (0.22, 0.1, 0.045), (0.25, 0.12, 0.05), (0.03, 0.015, 0.008))


def build_floor_textures(name="floor", palette=OAK, row_width=2.0, board_length=16.0):
    """Seamless 16 x 16 unit patch of floorboards (rows `row_width` wide, boards `board_length` long,
    staggered), baked into textures/{name}_*.jpg. Every board gets its own tone and grain, with
    small gaps between."""
    period = 16.0
    dark_grain, mid_grain, light_grain, light_board, dark_board = palette
    f = Mat("floor")
    x = f.math("ADD", f.axis("X"), period / 2)  # patch spans 0..16 in both axes
    y = f.math("ADD", f.axis("Y"), period / 2)
    row = f.math("FLOOR", f.math("DIVIDE", y, row_width))
    row_id = f.math("MODULO", row, period / row_width)
    stagger = f.math("MULTIPLY", f.random(row_id, 7.0), board_length)
    along_x = f.math("DIVIDE", f.math("ADD", x, stagger), board_length)
    board_id = f.math("MODULO", f.math("FLOOR", along_x), period / board_length)  # repeats with the patch
    tone = f.random(row_id, board_id)
    along = f.math("FRACT", along_x)
    across = f.math("FRACT", f.math("DIVIDE", y, row_width))
    gap_side = f.math("LESS_THAN", f.math("MINIMUM", across, f.math("SUBTRACT", 1.0, across)), 0.012)
    gap_end = f.math("LESS_THAN", f.math("MINIMUM", along, f.math("SUBTRACT", 1.0, along)), 0.0015)
    gap = f.math("MAXIMUM", gap_side, gap_end)

    # Long grain along each board (stretched along X), shifted per board so neighbours differ.
    shift = f.math("MULTIPLY", tone, 37.0)
    grain = f.seamless_noise(period, freq=(0.12, 2.5), detail=8.0, roughness=0.6, distortion=0.3, x=x, y=y, extra=shift)
    fine = f.seamless_noise(period, freq=(0.6, 18.0), detail=3.0, x=x, y=y, extra=shift)
    oak = f.ramp(grain, [(0.25, dark_grain), (0.5, mid_grain), (0.75, light_grain)])
    oak = f.mix(f.math("MULTIPLY", fine, 0.45), oak, tuple(c * 0.8 for c in dark_grain))
    board = f.mix(f.math("MULTIPLY", tone, 0.4), oak, light_board)                 # lighter boards
    board = f.mix(f.math("MULTIPLY", f.math("SUBTRACT", 1.0, tone), 0.35), board, dark_board)  # and darker ones
    f.color(f.mix(gap, board, (0.04, 0.025, 0.015)))

    # Satin lacquer, scuffed in big soft patches; gaps are dull.
    wear = f.seamless_noise(period, freq=(0.25, 0.25), detail=4.0, x=x, y=y)
    f.roughness(f.mix(gap, f.gray(wear, 0.22, 0.55), (0.9, 0.9, 0.9)))
    edges = f.math("MAXIMUM", f.math("LESS_THAN", f.math("MINIMUM", across, f.math("SUBTRACT", 1.0, across)), 0.03),
                   f.math("LESS_THAN", f.math("MINIMUM", along, f.math("SUBTRACT", 1.0, along)), 0.003))
    f.normal(f.bump(f.math("SUBTRACT", 1.0, edges), strength=0.5, distance=0.02,
                    previous=f.bump(fine, strength=0.15, distance=0.01)))

    bpy.ops.mesh.primitive_plane_add(size=period, location=(0, 0, 0))
    plane = bpy.context.active_object
    plane.name = name
    with_material(plane, f.mat)
    bake_texture_set(plane, name, 2048)
    if ARGS.preview:
        render_floor_preview(plane, name)
    return None


def bake_texture_set(plane, name, size, emissive=False):
    """Bakes a flat patch's material into textures/{name}_albedo/roughness/normal(/emission).jpg."""
    folder = os.path.join(ARGS.out, "textures")
    os.makedirs(folder, exist_ok=True)
    size = max(64, int(size * TEX_SCALE))
    passes = [("albedo", "EMIT", "Base Color", "sRGB"), ("roughness", "EMIT", "Roughness", "Non-Color"),
              ("normal", "NORMAL", None, "Non-Color")]
    if emissive:
        passes.append(("emission", "EMIT", ("Emission Color", "Emission"), "sRGB"))
    for suffix, kind, socket, space in passes:
        image = bpy.data.images.new(f"{name}_{suffix}", size, size, alpha=False)
        image.colorspace_settings.name = space
        _bake_pass(plane, image, kind, socket=socket)
        image.filepath_raw = os.path.join(folder, f"{name}_{suffix}.jpg")
        image.file_format = "JPEG"
        image.save()
        print(f"  wrote textures/{name}_{suffix}.jpg")
    if ARGS.preview and name != "floor" and not name.startswith("floor"):
        render_floor_preview(plane, name)


def render_floor_preview(plane, name="floor"):
    """View across the baked floor (repeated 3 x 3) to judge it and check the seams."""
    baked = bpy.data.materials.new("floor_baked")
    baked.use_nodes = True
    nodes, links = baked.node_tree.nodes, baked.node_tree.links
    bsdf = nodes["Principled BSDF"]
    mapping = nodes.new("ShaderNodeMapping")
    links.new(nodes.new("ShaderNodeTexCoord").outputs["UV"], mapping.inputs["Vector"])
    mapping.inputs["Scale"].default_value = (3, 3, 1)

    def tex(name):
        t = nodes.new("ShaderNodeTexImage")
        t.image = bpy.data.images[name]
        links.new(mapping.outputs["Vector"], t.inputs["Vector"])
        return t

    links.new(tex(f"{name}_albedo").outputs["Color"], bsdf.inputs["Base Color"])
    links.new(tex(f"{name}_roughness").outputs["Color"], bsdf.inputs["Roughness"])
    normal_map = nodes.new("ShaderNodeNormalMap")
    links.new(tex(f"{name}_normal").outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    plane.scale = (3, 3, 1)
    with_material(plane, baked)
    render_preview([plane], name)


def build_armchair():
    """Chesterfield armchair, 12 wide (X) x 11 deep (Y), facing -Y. Climbable in steps:
    seat 4, arms 5.6, back 7.2 (matches the Study's collision). Origin at bottom centre."""
    hide = Mat("chesterfield")
    cells = hide.voronoi(scale=1.2, vector=hide.vec(scale=(1, 1, 1.4)))  # button-tufted diamonds
    creases = hide.noise(scale=2.0, detail=6.0, distortion=1.0)
    base = hide.ramp(creases, [(0.3, (0.07, 0.012, 0.008)), (0.7, (0.2, 0.035, 0.02))])  # oxblood leather
    hide.color(hide.mix(hide.math("LESS_THAN", cells, 0.12), base, (0.02, 0.005, 0.004)))
    hide.roughness(hide.gray(creases, 0.3, 0.55))
    hide.normal(hide.bump(cells, strength=0.5, distance=0.05, previous=hide.bump(hide.voronoi(scale=40.0), strength=0.2, distance=0.01)))
    feet = mahogany("chair_feet")
    w, d = 12.0, 11.0
    parts = [
        with_material(box("base", (w, d, 3.0), (0, 0, 2.0), bevel=0.6, segments=4), hide),
        with_material(box("cushion", (w - 4.0, d - 3.0, 1.0), (0, -0.5, 3.5), bevel=0.4, segments=4), hide),
        with_material(box("back", (w, 2.0, 7.2 - 0.5), (0, d / 2 - 1.0, 0.5 + (6.7 / 2)), bevel=0.7, segments=4), hide),
    ]
    for side in (1, -1):
        parts.append(with_material(box(f"arm{side}", (2.2, d - 2.0, 5.1), (side * (w / 2 - 0.95), -0.5, 0.5 + 5.1 / 2), bevel=0.6, segments=4), hide))
        parts.append(with_material(cylinder(f"roll{side}", 1.1, d - 2.2, (side * (w / 2 - 1.0), -0.5, 4.5), rot=(math.pi / 2, 0, 0), vertices=24), hide))
        for fy in (-d / 2 + 1.5, d / 2 - 1.5):
            parts.append(with_material(cylinder(f"foot{side}{fy}", 0.4, 0.6, (side * (w / 2 - 1.2), fy, 0.3), vertices=12, radius_top=0.3), feet))
    return [bake(join(parts, "armchair"), 2048)]


def build_footstool():
    """Round tufted leather footstool, radius 2.8, 3.2 tall, on turned wooden feet."""
    hide = leather("footstool_leather", (0.22, 0.04, 0.025), rough=0.4)
    feet = mahogany("footstool_feet")
    top = cylinder("cushion", 2.8, 2.4, (0, 0, 2.0), vertices=40)
    bevel = top.modifiers.new("bevel", "BEVEL")
    bevel.width = 0.6
    bevel.segments = 5
    apply_modifiers(top)
    smooth(top)
    parts = [with_material(top, hide)]
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        parts.append(with_material(cylinder(f"foot{i}", 0.35, 0.8, (math.cos(a) * 2.0, math.sin(a) * 2.0, 0.4), vertices=12, radius_top=0.28), feet))
    for i in range(6):  # buttons
        a = i * math.tau / 6
        parts.append(with_material(sphere(f"button{i}", 0.12, (math.cos(a) * 1.3, math.sin(a) * 1.3, 3.18), segments=8, rings=6), hide))
    return [bake(join(parts, "footstool"), 1024)]


def build_desk():
    """Big partners' desk, 30 (X) x 14 (Y), top at 13: leather writing surface, drawers, turned legs."""
    wood_mat = mahogany("desk_wood", "X", 2.0)
    inlay = leather("desk_leather", (0.08, 0.16, 0.08), rough=0.5)
    brass = metal("desk_brass", (0.85, 0.62, 0.28), rough=0.25)
    w, d, h = 30.0, 14.0, 13.0
    parts = [
        with_material(box("top", (w, d, 0.8), (0, 0, h - 0.4), bevel=0.15, segments=3), wood_mat),
        with_material(box("leather", (w - 4.0, d - 4.0, 0.06), (0, 0, h + 0.01)), inlay),
        with_material(box("apron", (w - 1.0, d - 1.0, 2.0), (0, 0, h - 1.8), bevel=0.1), wood_mat),
    ]
    for side in (1, -1):  # two drawer pedestals
        x = side * (w / 2 - 5.0)
        parts.append(with_material(box(f"pedestal{side}", (8.0, d - 1.0, h - 3.6), (x, 0, 0.6 + (h - 3.6) / 2), bevel=0.1), wood_mat))
        parts.append(with_material(box(f"plinth{side}", (8.4, d - 0.6, 0.6), (x, 0, 0.3), bevel=0.1), wood_mat))
        for i in range(3):
            z = 1.8 + i * 3.2
            parts.append(with_material(box(f"drawer{side}{i}", (7.0, 0.2, 2.8), (x, -d / 2 + 0.4, z + 1.2), bevel=0.08), wood_mat))
            parts.append(with_material(cylinder(f"pull{side}{i}", 0.25, 0.3, (x, -d / 2 + 0.2, z + 1.4), rot=(math.pi / 2, 0, 0), vertices=12), brass))
    return [bake(join(parts, "desk"), 2048)]


def build_idol():
    """Golden fertility idol, ~3.5 tall: squat body, big head, carved face. Origin at bottom."""
    g = gold("idol_gold", dirt=0.6)
    eye = plastic("idol_eye", (0.03, 0.02, 0.01), rough=0.4)
    parts = [
        with_material(cylinder("plinth", 0.9, 0.3, (0, 0, 0.15), vertices=24), g),
        with_material(sphere("body", 0.75, (0, 0, 1.1), (1.0, 0.85, 1.05), 24, 14), g),
        with_material(sphere("head", 0.62, (0, -0.05, 2.35), (1.0, 0.95, 1.1), 24, 14), g),
        with_material(sphere("nose", 0.12, (0, -0.62, 2.3), (1.0, 0.8, 1.4), 12, 8), g),
        with_material(box("mouth", (0.3, 0.06, 0.05), (0, -0.57, 2.05)), eye),
        with_material(cylinder("headdress", 0.55, 0.25, (0, -0.05, 2.95), vertices=24, radius_top=0.42), g),
        with_material(cylinder("headdress2", 0.38, 0.25, (0, -0.05, 3.18), vertices=24, radius_top=0.22), g),
    ]
    for side in (1, -1):
        parts.append(with_material(sphere(f"eye{side}", 0.11, (side * 0.22, -0.55, 2.5), (1.0, 0.5, 0.7), 12, 8), eye))
        parts.append(with_material(tube(f"arm{side}", [(side * 0.6, -0.1, 1.5), (side * 0.85, -0.35, 1.1), (side * 0.35, -0.65, 0.95)],
                                        [0.16, 0.14, 0.12]), g))
        parts.append(with_material(sphere(f"ear{side}", 0.2, (side * 0.62, 0.0, 2.4), (0.4, 0.8, 1.2), 12, 8), g))
    return [bake(join(parts, "idol"), 1024)]


def build_globe():
    """Floor-standing antique globe, ~14 tall: turned wooden stand, brass meridian, sepia map."""
    sea = Mat("globe_map")
    land = sea.math("GREATER_THAN", sea.noise(scale=0.6, detail=6.0, roughness=0.55), 0.55)
    paper = sea.ramp(sea.noise(scale=3.0, detail=4.0), [(0.3, (0.45, 0.35, 0.18)), (0.7, (0.62, 0.5, 0.28))])
    lat = sea.math("LESS_THAN", sea.math("ABSOLUTE", sea.math("SUBTRACT", sea.math("FRACT", sea.math("MULTIPLY", sea.axis("Z"), 1.2)), 0.5)), 0.015)
    land_col = sea.mix(land, paper, (0.32, 0.3, 0.12))
    sea.color(sea.mix(sea.math("MULTIPLY", lat, 0.5), land_col, (0.2, 0.12, 0.05)))
    sea.roughness(0.35)
    sea.normal(sea.bump(land, strength=0.2, distance=0.02))
    stand = mahogany("globe_stand")
    brass = metal("globe_brass", (0.85, 0.62, 0.28), rough=0.25)
    parts = [
        with_material(sphere("globe", 4.0, (0, 0, 9.5), segments=48, rings=28, rot=(0.4, 0, 0)), sea),
        with_material(torus("meridian", 4.3, 0.12, (0, 0, 9.5), rot=(math.pi / 2, 0.4, 0), major_segments=48), brass),
        with_material(torus("horizon", 4.4, 0.25, (0, 0, 9.5), major_segments=48), stand),
        with_material(cylinder("pillar", 0.5, 5.0, (0, 0, 3.0), vertices=20, radius_top=0.35), stand),
        with_material(cylinder("base", 2.8, 0.5, (0, 0, 0.25), vertices=32, radius_top=2.5), stand),
    ]
    return [bake(join(parts, "globe"), 2048)]


def build_chest():
    """Treasure chest, 8 (X) x 5 (Y) x 5 tall with a rounded lid, iron bands, gold spilling out front."""
    planks = wood("chest_wood", (0.35, 0.2, 0.1), (0.15, 0.08, 0.04), "X", 3.0, rough=0.7)
    iron = metal("chest_iron", (0.2, 0.19, 0.18), rough=0.6)
    coins = gold("chest_gold", dirt=0.2)
    parts = [
        with_material(box("box", (8.0, 5.0, 3.6), (0, 0, 1.8), bevel=0.1), planks),
        with_material(cylinder("lid", 2.5, 8.0, (0, 0, 3.6), rot=(0, math.pi / 2, 0), vertices=32), planks),
        with_material(box("lock", (0.8, 0.3, 1.0), (0, -2.55, 3.3), bevel=0.05), iron),
    ]
    deform(parts[1], lambda v: Vector((v.x, v.y, max(v.z, 3.6) if v.z < 3.6 else 3.6 + (v.z - 3.6) * 0.55)))
    for x in (-3.0, 0.0, 3.0):
        parts.append(with_material(box(f"band{x}", (0.35, 5.15, 3.65), (x, 0, 1.82)), iron))
        parts.append(with_material(torus(f"lidband{x}", 2.5 * 0.78, 0.12, (x, 0, 3.6), rot=(0, math.pi / 2, 0), major_segments=24), iron))
    rng = random.Random(9)
    for i in range(18):
        parts.append(with_material(cylinder(f"coin{i}", 0.35, 0.06, (rng.uniform(-3, 3), -2.5 - rng.uniform(0.2, 1.8), 0.03 + rng.random() * 0.1),
                                            rot=(rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), 0), vertices=16), coins))
    return [bake(join(parts, "chest"), 2048)]


def build_urn():
    """Ancient amphora, ~7 tall: terracotta with a black-figure meander band. Origin at bottom."""
    clay = Mat("urn_clay")
    z = clay.axis("Z")
    band = clay.math("MULTIPLY", clay.math("GREATER_THAN", z, 3.2), clay.math("LESS_THAN", z, 4.2))
    meander = clay.math("GREATER_THAN", clay.wave("BANDS", "X", scale=3.0, distortion=1.0, vector=clay.vec(scale=(1, 1, 0))), 0.5)
    base = clay.ramp(clay.noise(scale=4.0, detail=5.0), [(0.3, (0.45, 0.17, 0.07)), (0.7, (0.62, 0.27, 0.12))])
    clay.color(clay.mix(clay.math("MULTIPLY", band, meander), base, (0.03, 0.02, 0.015)))
    clay.roughness(clay.gray(clay.noise(scale=6.0), 0.5, 0.8))
    clay.normal(clay.bump(clay.noise(scale=50.0), strength=0.15, distance=0.01))
    urn = lathe("urn", [(0.0, 0.0), (0.9, 0.0), (0.7, 0.4), (1.8, 2.5), (2.0, 3.6), (1.5, 5.0), (0.7, 5.8), (0.65, 6.6),
                        (0.95, 6.9), (0.8, 7.0), (0.0, 6.95)], steps=48)
    parts = [with_material(urn, clay)]
    for side in (1, -1):
        parts.append(with_material(tube(f"handle{side}", [(side * 0.7, 0, 6.3), (side * 1.6, 0, 6.2), (side * 1.7, 0, 5.2), (side * 1.4, 0, 4.7)],
                                        [0.15, 0.15, 0.15, 0.15]), clay))
    return [bake(join(parts, "urn"), 1024)]


def build_candle():
    """Brass candlestick with a dripping wax candle and a glowing flame, ~4.5 tall."""
    wax = Mat("wax")
    wax.color((0.92, 0.86, 0.72))
    wax.roughness(0.4)
    wax.set(("Subsurface Weight", "Subsurface"), 0.5)
    brass = metal("candle_brass", (0.85, 0.62, 0.28), rough=0.25)
    flame = glow("flame", (1.0, 0.6, 0.2), 8.0)
    parts = [
        with_material(cylinder("dish", 1.0, 0.2, (0, 0, 0.1), vertices=24), brass),
        with_material(cylinder("stem", 0.18, 1.6, (0, 0, 1.0), vertices=12), brass),
        with_material(cylinder("cup", 0.45, 0.3, (0, 0, 1.9), vertices=16), brass),
        with_material(cylinder("candle", 0.3, 2.0, (0, 0, 3.05), vertices=16), wax),
        with_material(sphere("flame", 0.14, (0, 0, 4.3), (1.0, 1.0, 2.0), 12, 8), flame),
    ]
    for i in range(4):
        a = i * math.tau / 4 + 0.4
        parts.append(with_material(sphere(f"drip{i}", 0.08, (math.cos(a) * 0.3, math.sin(a) * 0.3, 3.6 + 0.3 * (i % 2)), (1, 1, 3.0), 8, 6), wax))
    return [bake(join(parts, "candle"), 512, emissive=True)]


def build_panel_wall():
    """One 16-wide, 36-tall bay of dark wood panelling (skirting, wainscot, tall raised panel,
    picture rail, crown), facing -Y. Tiled along the Study's walls."""
    wood_mat = mahogany("panel_wood", "Z", 1.2)
    rail = mahogany("panel_rail", "X", 3.0)
    w, h = 16.0, 36.0
    parts = [with_material(box("back", (w, 1.0, h), (0, 0, h / 2)), wood_mat),
             with_material(box("skirting", (w, 1.5, 1.6), (0, -0.4, 0.8), bevel=0.1), rail),
             with_material(box("dado", (w, 1.6, 0.6), (0, -0.5, 9.0), bevel=0.15), rail),
             with_material(box("picture", (w, 1.5, 0.5), (0, -0.4, 30.0), bevel=0.15), rail),
             with_material(box("crown", (w, 2.2, 1.2), (0, -0.7, h - 0.6), bevel=0.25, segments=3), rail)]
    for x in (-w / 4, w / 4):
        parts.append(with_material(box(f"low{x}", (w / 2 - 1.6, 0.6, 5.5), (x, -0.6, 4.95), bevel=0.35, segments=3), wood_mat))
        parts.append(with_material(box(f"high{x}", (w / 2 - 1.6, 0.6, 18.0), (x, -0.6, 19.6), bevel=0.35, segments=3), wood_mat))
    return [bake(join(parts, "panel_wall"), 1024)]


def build_rug():
    """Persian rug 40 x 28: deep red field, medallion, patterned borders, fringes. Thin slab."""
    m = Mat("rug")
    x, y = m.axis("X"), m.axis("Y")
    ax, ay = m.math("ABSOLUTE", x), m.math("ABSOLUTE", y)
    edge = m.math("MINIMUM", m.math("SUBTRACT", 20.0, ax), m.math("SUBTRACT", 14.0, ay))
    border = m.math("LESS_THAN", edge, 3.5)
    stripe = m.math("MULTIPLY", m.math("GREATER_THAN", edge, 0.8), m.math("LESS_THAN", edge, 1.1))
    diamond = m.math("ADD", m.math("DIVIDE", ax, 9.0), m.math("DIVIDE", ay, 6.0))
    medallion = m.math("LESS_THAN", diamond, 1.0)
    rings = m.math("FRACT", m.math("MULTIPLY", diamond, 3.0))
    motif = m.math("GREATER_THAN", m.voronoi(scale=0.6, vector=m.vec(scale=(1, 1, 0))), 0.45)
    field = m.mix(m.math("MULTIPLY", motif, 0.6), (0.28, 0.03, 0.03), (0.12, 0.08, 0.2))
    med = m.ramp(rings, [(0.0, (0.08, 0.1, 0.25)), (0.35, (0.65, 0.48, 0.2)), (0.6, (0.42, 0.05, 0.04)), (0.85, (0.75, 0.65, 0.45))], "CONSTANT")
    col = m.mix(medallion, field, med)
    col = m.mix(border, col, m.mix(m.math("GREATER_THAN", m.wave("BANDS", "X", scale=1.2, distortion=2.0), 0.5), (0.08, 0.1, 0.25), (0.6, 0.45, 0.2)))
    col = m.mix(stripe, col, (0.75, 0.65, 0.45))
    wear = m.noise(scale=0.25, detail=4.0)
    m.color(m.mix(m.math("MULTIPLY", m.math("MAXIMUM", m.math("SUBTRACT", wear, 0.55), 0.0), 2.0), col, (0.45, 0.38, 0.32)))
    m.roughness(0.95)
    m.normal(m.bump(m.noise(scale=80.0, detail=2.0), strength=0.4, distance=0.01))
    m.set(("Sheen Weight", "Sheen"), 0.5)
    rug = box("rug", (40.0, 28.0, 0.12), (0, 0, 0.06))
    fringe = Mat("fringe")
    fringe.color((0.8, 0.74, 0.6))
    fringe.roughness(0.9)
    parts = [with_material(rug, m)]
    for side in (1, -1):
        parts.append(with_material(box(f"fringe{side}", (1.0, 27.0, 0.05), (side * 20.5, 0, 0.03)), fringe))
    return [bake(join(parts, "rug"), 2048)]


# --- Kitchen extras -----------------------------------------------------------------------------

def build_ruler():
    """Wooden ruler, 24 long (X) x 3 wide x 0.3 thick, with printed tick marks. Origin at bottom centre."""
    m = wood("ruler_wood", (0.85, 0.66, 0.4), (0.68, 0.48, 0.26), "X", 1.0, rough=0.45)
    ticks = Mat("ruler_ticks")
    x, y = ticks.axis("X"), ticks.axis("Y")
    tick = ticks.math("LESS_THAN", ticks.math("FRACT", ticks.math("MULTIPLY", x, 2.0)), 0.06)
    long_tick = ticks.math("LESS_THAN", ticks.math("FRACT", x), 0.03)
    edge = ticks.math("GREATER_THAN", y, 0.6)
    marks = ticks.math("MULTIPLY", edge, ticks.math("MAXIMUM", tick, ticks.math("MULTIPLY", long_tick, ticks.math("GREATER_THAN", y, 0.2))))
    base = ticks.ramp(ticks.noise(scale=4.0), [(0.3, (0.8, 0.6, 0.35)), (0.7, (0.9, 0.72, 0.45))])
    ticks.color(ticks.mix(marks, base, (0.05, 0.04, 0.03)))
    ticks.roughness(0.45)
    body = box("ruler", (24.0, 3.0, 0.3), (0, 0, 0.15), bevel=0.04, segments=2)
    face = box("face", (23.8, 2.9, 0.01), (0, 0, 0.305))
    return [bake(join([with_material(body, m), with_material(face, ticks)], "ruler"), 1024)]


def build_eraser():
    """Pink rubber eraser shaped into a wedge rising towards +X (4 long, 3 wide, 1 high), with a
    printed blue sleeve. Used as a kicker ramp. Origin at bottom centre."""
    rubber = Mat("eraser_rubber")
    rubber.color(rubber.ramp(rubber.noise(scale=6.0), [(0.3, (0.85, 0.42, 0.48)), (0.7, (0.95, 0.55, 0.6))]))
    rubber.roughness(0.85)
    rubber.normal(rubber.bump(rubber.noise(scale=50.0), strength=0.3, distance=0.01))
    sleeve = plastic("eraser_sleeve", (0.1, 0.25, 0.65), rough=0.5)
    mesh = bpy.data.meshes.new("eraser")
    bm = bmesh.new()
    l, w, h = 4.0, 3.0, 1.0
    v = [bm.verts.new(co) for co in ((-l / 2, -w / 2, 0), (l / 2, -w / 2, 0), (l / 2, w / 2, 0), (-l / 2, w / 2, 0),
                                     (l / 2, -w / 2, h), (l / 2, w / 2, h))]
    for face in ((0, 3, 2, 1), (0, 1, 4), (3, 5, 2), (1, 2, 5, 4), (0, 4, 5, 3)):
        bm.faces.new([v[i] for i in face])
    bm.to_mesh(mesh)
    bm.free()
    wedge = link(bpy.data.objects.new("eraser", mesh))
    bevel = wedge.modifiers.new("bevel", "BEVEL")
    bevel.width = 0.08
    bevel.segments = 2
    apply_modifiers(wedge)
    band = box("sleeve", (1.2, w + 0.04, 0.9), (1.2, 0, 0.42))
    deform(band, lambda p: Vector((p.x, p.y, min(p.z, (p.x + l / 2) / l * h + 0.02))))
    return [bake(join([with_material(wedge, rubber), with_material(band, sleeve)], "eraser"), 512)]


def build_toaster():
    """Chrome two-slot toaster, 6 (X) x 4 (Y) x 4.6 tall, on the kitchen counter. Origin at bottom centre."""
    chrome = metal("toaster_chrome", (0.8, 0.81, 0.84), rough=0.12, brushed_axis="X")
    black = plastic("toaster_black", (0.04, 0.04, 0.045), rough=0.5)
    body = box("body", (6.0, 4.0, 4.4), (0, 0, 2.3), bevel=0.7, segments=5)
    parts = [with_material(body, chrome),
             with_material(box("base", (6.2, 4.2, 0.3), (0, 0, 0.15), bevel=0.1), black)]
    for y in (-0.8, 0.8):
        parts.append(with_material(box(f"slot{y}", (4.4, 0.45, 0.06), (0, y, 4.5)), black))
    parts.append(with_material(box("lever", (0.5, 0.8, 0.3), (2.0, -2.2, 3.0), bevel=0.08), black))
    parts.append(with_material(cylinder("knob", 0.35, 0.3, (-1.8, -2.15, 1.5), rot=(math.pi / 2, 0, 0), vertices=16), black))
    return [bake(join(parts, "toaster"), 1024)]


def build_parachute():
    """MDK-style round canopy at the runner's scale: striped panels, strings down to a harness at
    the origin (between the shoulders). Shown above the runner while gliding."""
    canopy = Mat("canopy")
    angle = canopy.nodes.new("ShaderNodeVectorMath")
    angle.operation = "NORMALIZE"
    canopy.links.new(canopy.coords, angle.inputs[0])
    sep = canopy.nodes.new("ShaderNodeSeparateXYZ")
    canopy.links.new(angle.outputs[0], sep.inputs[0])
    atan = canopy.math("ARCTAN2", sep.outputs["Y"], sep.outputs["X"])
    panel = canopy.math("MODULO", canopy.math("FLOOR", canopy.math("MULTIPLY", canopy.math("ADD", atan, math.pi), 8.0 / math.tau)), 2.0)
    canopy.color(canopy.mix(panel, (0.9, 0.35, 0.05), (0.95, 0.92, 0.85)))
    canopy.roughness(0.7)
    canopy.normal(canopy.bump(canopy.noise(scale=12.0), strength=0.2, distance=0.01))
    canopy.set(("Sheen Weight", "Sheen"), 0.4)
    cord = plastic("cord", (0.2, 0.18, 0.15), rough=0.7)
    dome = sphere("canopy", 0.9, (0, 0, 1.4), (1.0, 1.0, 0.55), 32, 16)
    deform(dome, lambda p: Vector((p.x, p.y, max(p.z, 1.4 - 0.05))))
    parts = [with_material(dome, canopy)]
    for i in range(8):
        a = i * math.tau / 8
        parts.append(with_material(tube(f"cord{i}", [(math.cos(a) * 0.88, math.sin(a) * 0.88, 1.36), (0, 0, 0.0)], [0.006, 0.006],
                                        smooth_curve=False, sides=4), cord))
    return [bake(join(parts, "parachute"), 1024)]


# --- The Windowsill (boss level) ------------------------------------------------------------------

PAINTED = ((0.62, 0.6, 0.55), (0.72, 0.7, 0.64), (0.8, 0.78, 0.72), (0.86, 0.84, 0.78), (0.55, 0.53, 0.48))


def build_window_frame():
    """Painted wooden window frame, 120 wide (X) x 60 tall (Z), 2 deep, with a central mullion and
    a transom; open panes (the glass is added in Godot). Faces -Y. Origin at bottom centre."""
    paint = painted("frame_paint", (0.82, 0.8, 0.75), rough=0.45, brush_axis="Z")
    w, h, d = 120.0, 60.0, 2.0
    parts = [with_material(box("bottom", (w, d, 3.0), (0, 0, 1.5), bevel=0.3), paint),
             with_material(box("top", (w, d, 3.0), (0, 0, h - 1.5), bevel=0.3), paint),
             with_material(box("left", (3.0, d, h), (-w / 2 + 1.5, 0, h / 2), bevel=0.3), paint),
             with_material(box("right", (3.0, d, h), (w / 2 - 1.5, 0, h / 2), bevel=0.3), paint),
             with_material(box("mullion", (2.0, d, h), (0, 0, h / 2), bevel=0.3), paint),
             with_material(box("transom", (w, d, 2.0), (0, 0, h * 0.7), bevel=0.3), paint)]
    for x in (-w / 4, w / 4):
        parts.append(with_material(box(f"bar{x}", (1.0, d * 0.6, h * 0.7), (x, 0, h * 0.35), bevel=0.15), paint))
    return [bake(join(parts, "window_frame"), 2048)]


def build_pot(name, plant):
    """Terracotta flower pot (radius 4, 6 tall) with a plant: 'cactus' or 'basil'. Origin at bottom."""
    clay = Mat(f"{name}_clay")
    clay.color(clay.ramp(clay.noise(scale=3.0, detail=5.0), [(0.3, (0.5, 0.2, 0.08)), (0.7, (0.66, 0.3, 0.13))]))
    clay.roughness(0.8)
    clay.normal(clay.bump(clay.noise(scale=40.0), strength=0.2, distance=0.01))
    soil = Mat(f"{name}_soil")
    soil.color(soil.ramp(soil.voronoi(scale=20.0), [(0.0, (0.05, 0.03, 0.02)), (1.0, (0.15, 0.09, 0.05))]))
    soil.roughness(0.95)
    soil.normal(soil.bump(soil.voronoi(scale=20.0), strength=0.5, distance=0.02))
    pot = lathe("pot", [(0.0, 0.0), (3.0, 0.0), (3.6, 5.0), (4.1, 5.1), (4.1, 6.0), (3.7, 6.0), (3.5, 5.4), (0.0, 5.4)], steps=40)
    parts = [with_material(pot, clay), with_material(cylinder("soil", 3.5, 0.2, (0, 0, 5.4), vertices=40), soil)]
    if plant == "cactus":
        green = Mat(f"{name}_cactus")
        ribs = green.wave("BANDS", "X", scale=2.0, vector=green.vec(scale=(1, 1, 0)))
        green.color(green.ramp(green.noise(scale=4.0), [(0.3, (0.08, 0.25, 0.08)), (0.7, (0.15, 0.38, 0.12))]))
        green.roughness(0.55)
        green.normal(green.bump(ribs, strength=0.5, distance=0.05))
        spine = plastic(f"{name}_spines", (0.9, 0.88, 0.75), rough=0.5)
        trunk = sphere("trunk", 1.8, (0, 0, 9.5), (1.0, 1.0, 2.6), 24, 16)
        arm1 = tube("arm1", [(1.5, 0, 9.0), (3.0, 0, 9.3), (3.2, 0, 11.5)], [0.7, 0.75, 0.6], sides=12)
        arm2 = tube("arm2", [(-1.4, 0, 10.5), (-2.8, 0, 10.7), (-3.0, 0, 12.3)], [0.6, 0.6, 0.5], sides=12)
        parts += [with_material(trunk, green), with_material(arm1, green), with_material(arm2, green)]
        rng = random.Random(4)
        for i in range(60):
            a, z = rng.random() * math.tau, 6.5 + rng.random() * 6.0
            r = 1.8 * math.sqrt(max(0.0, 1 - ((z - 9.5) / 4.7) ** 2)) + 0.05
            parts.append(with_material(cylinder(f"spine{i}", 0.03, 0.4, (math.cos(a) * r, math.sin(a) * r, z),
                                                rot=(0, math.pi / 2, a), vertices=4), spine))
    else:
        leaf = leaf_material(f"{name}_leaf", (0.1, 0.35, 0.06))
        stem = leaf_material(f"{name}_stem", (0.15, 0.3, 0.08))
        rng = random.Random(6)
        for i in range(7):
            a = i * math.tau / 7
            top = (math.cos(a) * 1.5, math.sin(a) * 1.5, 9.0 + rng.random() * 2.5)
            parts.append(with_material(tube(f"stem{i}", [(math.cos(a) * 0.4, math.sin(a) * 0.4, 5.5), top], [0.15, 0.1]), stem))
            for j in range(4):
                b = a + rng.uniform(-1.2, 1.2)
                z = 7.0 + j * 1.0 + rng.random()
                c = (math.cos(a) * (0.6 + j * 0.25) + math.cos(b) * 0.8, math.sin(a) * (0.6 + j * 0.25) + math.sin(b) * 0.8, z)
                parts.append(with_material(sphere(f"leaf{i}{j}", 0.75, c, (1.0, 0.55, 0.12), 12, 8, rot=(0.3, -0.4, b)), leaf))
    return [bake(join(parts, name), 1024)]


def build_watering_can():
    """Galvanised watering can, ~9 tall with spout and handle, spout towards +X. Origin at bottom."""
    zinc = metal("zinc", (0.6, 0.62, 0.62), rough=0.45)
    body = cylinder("body", 3.0, 6.5, (0, 0, 3.25), vertices=40)
    parts = [with_material(body, zinc),
             with_material(tube("spout", [(2.5, 0, 1.5), (5.5, 0, 5.5), (7.0, 0, 8.0)], [0.45, 0.35, 0.3], sides=12), zinc),
             with_material(cylinder("rose", 0.6, 0.6, (7.2, 0, 8.3), rot=(0, 0.6, 0), vertices=16), zinc),
             with_material(tube("handle", [(-2.0, 0, 6.5), (-1.0, 0, 9.0), (1.5, 0, 9.0), (2.0, 0, 6.5)], [0.25, 0.25, 0.25, 0.25]), zinc)]
    return [bake(join(parts, "watering_can"), 1024)]


def build_mortar():
    """Seed mortar: a short olive tube on a tripod, shells stacked beside it. About 0.6 long (icon / props)."""
    olive = metal("mortar_olive", (0.25, 0.3, 0.14), rough=0.55)
    steel = metal("mortar_steel", (0.4, 0.4, 0.42), rough=0.35)
    seed = Mat("mortar_seed")
    seed.color(seed.ramp(seed.noise(scale=8.0), [(0.3, (0.35, 0.2, 0.08)), (0.7, (0.55, 0.35, 0.15))]))
    seed.roughness(0.5)
    tube_obj = cylinder("tube", 0.06, 0.5, (0, -0.1, 0.3), rot=(math.radians(50), 0, 0), vertices=24)
    parts = [
        with_material(tube_obj, olive),
        with_material(cylinder("base", 0.14, 0.03, (0, 0.1, 0.015), vertices=24), steel),
        with_material(tube("leg1", [(0, -0.15, 0.32), (0.12, -0.3, 0.0)], [0.012, 0.012]), steel),
        with_material(tube("leg2", [(0, -0.15, 0.32), (-0.12, -0.3, 0.0)], [0.012, 0.012]), steel),
    ]
    for i in range(3):
        parts.append(with_material(sphere(f"seed{i}", 0.045, (0.2 + i * 0.07, 0.12, 0.04), (0.8, 0.8, 1.2), 12, 8), seed.mat))
    return [bake(join(parts, "mortar"), 512)]


def build_void_pulser():
    """Void Pulser at the runner's scale: a stubby gun with a ringed barrel and a violet plasma sphere
    in a cage on top. Grip at the origin, barrel forward (-Y)."""
    body = metal("void_body", (0.08, 0.07, 0.1), rough=0.35)
    trim = metal("void_trim", (0.55, 0.5, 0.62), rough=0.3)
    plasma = glow("void_plasma", (0.6, 0.25, 1.0), 4.0)
    rot = (math.pi / 2, 0, 0)
    parts = [
        with_material(box("receiver", (0.11, 0.24, 0.12), (0, -0.05, 0.04), bevel=0.025, segments=3), body),
        with_material(box("grip", (0.05, 0.06, 0.12), (0, 0.04, -0.05), bevel=0.012), body),
        with_material(cylinder("barrel", 0.035, 0.22, (0, -0.25, 0.04), rot=rot, vertices=20), body),
        with_material(sphere("orb", 0.055, (0, -0.06, 0.15), segments=20, rings=12), plasma),
    ]
    for i, y in enumerate((-0.18, -0.25, -0.32)):
        parts.append(with_material(torus(f"ring{i}", 0.048, 0.012, (0, y, 0.04), rot=rot, major_segments=20, minor_segments=8), trim))
    for i in range(3):
        a = i * math.tau / 3
        parts.append(with_material(tube(f"cage{i}", [(math.cos(a) * 0.07, -0.06 + math.sin(a) * 0.07, 0.09),
                                                      (math.cos(a) * 0.05, -0.06 + math.sin(a) * 0.05, 0.22)], [0.008, 0.008]), trim))
    return [bake(join(parts, "VoidPulser"), 512, emissive=True)]


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
    "void_pulser": build_void_pulser,
    "mortar": build_mortar,
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
    "back_counter": lambda: build_counter("back_counter", 120.0, 10.0, 14, 1),
    "fridge": build_fridge,
    "floor": build_floor_textures,
    "ruler": build_ruler,
    "eraser": build_eraser,
    "toaster": build_toaster,
    "parachute": build_parachute,
    # The Windowsill (boss level)
    "window_frame": build_window_frame,
    "pot_cactus": lambda: build_pot("pot_cactus", "cactus"),
    "pot_basil": lambda: build_pot("pot_basil", "basil"),
    "watering_can": build_watering_can,
    "floor_sill": lambda: build_floor_textures("floor_sill", PAINTED, row_width=4.0, board_length=16.0),
    # The Study
    "armchair": build_armchair,
    "footstool": build_footstool,
    "desk": build_desk,
    "idol": build_idol,
    "globe": build_globe,
    "chest": build_chest,
    "urn": build_urn,
    "candle": build_candle,
    "panel_wall": build_panel_wall,
    "rug": build_rug,
    "book_red": lambda: build_book("book_red", (0.32, 0.05, 0.03)),
    "book_green": lambda: build_book("book_green", (0.06, 0.18, 0.08)),
    "floor_walnut": lambda: build_floor_textures("floor_walnut", WALNUT, row_width=1.2, board_length=8.0),
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
