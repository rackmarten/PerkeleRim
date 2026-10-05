"""
Builds a Skyrim SE NIF of an MSC vehicle from tools/unity-export's model.json, in Blender with PyNifly.

  blender -b --factory-startup --python build_nif.py -- <model.json> <out.nif> <textures dir> <vehicle json>

<textures dir> holds the DDS files (converted from the exported PNGs). It must sit under a folder
named "textures", PyNifly writes texture paths from there on. <vehicle json> picks parts, see
hayosiko.json.

The NIF: root NiNode, the body's shapes straight under it, and per wheel a NiNode "<Wheel>" at the
wheel center (steers about Z) with a child NiNode "<Wheel>:Spin" (spins about X). Skyrim space:
+Y forward, +Z up, +X right, 1 unit = 0.0142875 m.
"""
import json
import os
import re
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PYNIFLY = os.path.join(HERE, "..", ".deps", "pynifly")
UNITS_PER_METER = 1.0 / 0.0142875
FLAT_NORMAL = "flat_n.dds"

# Unity (left-handed, Y up) to Skyrim (right-handed, Z up): swap Y and Z. A reflection, so triangles
# get their winding reversed. Same mapping as protocol/cpp/coords.h UnityToSkyrim.
UNITY_TO_SKYRIM = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    if len(a) != 4:
        raise SystemExit("usage: build_nif.py -- <model.json> <out.nif> <textures dir> <vehicle json>")
    return a


def local_matrix(node):
    t = Matrix.Translation(Vector(node["position"]))
    x, y, z, w = node["rotation"]
    r = Quaternion((w, x, y, z)).to_matrix().to_4x4()
    s = Matrix.Diagonal(Vector(node["scale"] + [1.0]))
    return t @ r @ s


def node_paths(nodes):
    """Path of each node below the root ("" for the root), like hierarchy names in Unity."""
    paths = []
    for n in nodes:
        p = n["parent"]
        paths.append("" if p < 0 else (n["name"] if paths[p] == "" else paths[p] + "/" + n["name"]))
    return paths


def world_matrices(nodes):
    """Each node's matrix relative to the root (the root's own transform left out), Unity space."""
    mats = []
    for n in nodes:
        mats.append(Matrix.Identity(4) if n["parent"] < 0 else mats[n["parent"]] @ local_matrix(n))
    return mats


def to_skyrim(m):
    """A Unity-space (meters) transform as the same transform in Skyrim space (units)."""
    s = Matrix.Diagonal(Vector((UNITS_PER_METER,) * 3 + (1.0,)))
    return s @ UNITY_TO_SKYRIM @ m @ UNITY_TO_SKYRIM.inverted() @ s.inverted()


def write_flat_normal(path, size=16):
    """Uncompressed BGRA DDS: normal straight out (0.5, 0.5, 1), alpha 0. Skyrim reads the specular
    mask from the normal map's alpha; without a normal map every shape shines white in the sun."""
    import struct
    header = struct.pack("<4s7I44x8I5I", b"DDS ", 124, 0x100F, size, size, size * 4, 0, 0,
                         32, 0x41, 0, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000,
                         0x1000, 0, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(header + bytes((255, 128, 128, 0)) * (size * size))


def make_material(name, info, tex_dir, settings, shaders_blend, shader_io):
    mat = bpy.data.materials.new(name)
    tree = mat.node_tree
    tree.nodes.clear()
    out = tree.nodes.new("ShaderNodeOutputMaterial")
    out.name = "Material Output"
    shader = shader_io.append_groupnode(tree, "SkyrimShader:Default", "SkyrimShader:Default", shaders_blend)
    tree.links.new(shader.outputs[0], out.inputs[0])

    diffuse = next((t for t in info["textures"] if t["slot"] == "diffuse"), None)
    tex = None
    if diffuse:
        dds = os.path.join(tex_dir, os.path.splitext(diffuse["file"])[0] + ".dds")
        tex = tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(dds, check_existing=True)
        tree.links.new(tex.outputs["Color"], shader.inputs["Diffuse"])
    elif "Diffuse" in shader.inputs:
        shader.inputs["Diffuse"].default_value = (1.0, 1.0, 1.0, 1.0)

    normal = tree.nodes.new("ShaderNodeTexImage")
    normal.image = bpy.data.images.load(os.path.join(tex_dir, FLAT_NORMAL), check_existing=True)
    normal.image.colorspace_settings.name = "Non-Color"
    tree.links.new(normal.outputs["Color"], shader.inputs["Normal"])

    if any(re.search(p, name, re.I) for p in settings["alpha_blend_materials"]):
        alpha = shader_io.append_groupnode(tree, "AlphaProperty", "Alpha Property", shaders_blend)
        tree.links.new(alpha.outputs[0], shader.inputs["Alpha Property"])
        if tex:
            tree.links.new(tex.outputs["Alpha"], alpha.inputs["Alpha"])
        alpha.inputs["Alpha Test"].default_value = False
        alpha.inputs["Alpha Blend"].default_value = True
        alpha.inputs["Source Blend Mode"].default_value = 6       # SRC_ALPHA
        alpha.inputs["Destination Blend Mode"].default_value = 7  # INV_SRC_ALPHA
    return mat


def build_mesh(name, mesh, submesh, matrix, material, color):
    """One Blender object per submesh: PyNifly writes one shader per shape."""
    verts_u = mesh["vertices"]
    used = sorted(set(submesh["triangles"]))
    remap = {v: i for i, v in enumerate(used)}
    verts = [(matrix @ Vector(verts_u[v * 3:v * 3 + 3])).to_tuple() for v in used]
    tris = submesh["triangles"]
    faces = [(remap[tris[i]], remap[tris[i + 1]], remap[tris[i + 2]]) for i in range(0, len(tris), 3)]
    # Unity's front faces wind the other way round from the NIF's once the Unity->Skyrim reflection is
    # applied (checked in game). A mirrored part (negative scale) reflects once more and needs no flip.
    if matrix.to_3x3().determinant() < 0:
        faces = [(a, c, b) for a, b, c in faces]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate()
    if mesh.get("uv"):
        layer = me.uv_layers.new(name="UVMap")
        uv = mesh["uv"]
        for loop in me.loops:
            v = used[loop.vertex_index]
            # Unity and Blender both put the UV origin bottom left; PyNifly flips to NIF's on export.
            layer.data[loop.index].uv = (uv[v * 2], uv[v * 2 + 1])
    if mesh.get("normals"):
        n_u = mesh["normals"]
        rot = matrix.to_3x3().inverted().transposed()
        normals = [(rot @ Vector(n_u[v * 3:v * 3 + 3])).normalized().to_tuple() for v in used]
        me.normals_split_custom_set_from_vertices(normals)
    if color is not None:
        attr = me.color_attributes.new(name="VERTEX_COLOR", type="BYTE_COLOR", domain="CORNER")
        for d in attr.data:
            d.color = color
    obj = bpy.data.objects.new(name, me)
    obj.data.materials.append(material)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def main():
    model_path, out_nif, tex_dir, settings_path = args()
    with open(model_path, encoding="utf-8") as f:
        model = json.load(f)
    with open(settings_path, encoding="utf-8") as f:
        settings = json.load(f)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    sys.path.insert(0, PYNIFLY)
    import io_scene_nifly
    io_scene_nifly.register()
    from io_scene_nifly.nif import shader_io
    shaders_blend = os.path.join(PYNIFLY, "io_scene_nifly", "blender_assets", "Shaders.blend")

    write_flat_normal(os.path.join(tex_dir, FLAT_NORMAL))
    nodes = model["nodes"]
    paths = node_paths(nodes)
    worlds = world_matrices(nodes)
    skip = [re.compile(p) for p in settings["skip"]]

    scene = bpy.context.scene
    root = bpy.data.objects.new(settings["root_name"], None)
    scene.collection.objects.link(root)

    # Wheel nodes: steer empty at the wheel center, spin empty under it.
    anchors = []  # (path prefix, empty, Skyrim-space matrix of the empty)
    for wheel in settings["wheels"]:
        i = paths.index(wheel["node"])
        center = to_skyrim(worlds[i]).to_translation()
        steer = bpy.data.objects.new(wheel["name"], None)
        steer.location = center
        steer.parent = root
        spin = bpy.data.objects.new(wheel["name"] + ":Spin", None)
        spin.parent = steer
        scene.collection.objects.link(steer)
        scene.collection.objects.link(spin)
        anchor = Matrix.Translation(center)
        anchors.append((wheel["node"] + "/" + wheel["spin"] + "/", spin, anchor))
        if wheel.get("steers"):
            anchors.append((wheel["node"] + "/", steer, anchor))
    anchors.sort(key=lambda a: -len(a[0]))  # most specific prefix first

    materials = {}
    count = 0
    for i, node in enumerate(nodes):
        mesh = node.get("mesh")
        if not mesh or any(p.search(paths[i]) for p in skip):
            continue
        parent, anchor = root, Matrix.Identity(4)
        for prefix, empty, m in anchors:
            if (paths[i] + "/").startswith(prefix):
                parent, anchor = empty, m
                break
        # Unity-space vertex -> van space (meters) -> Skyrim axes and units -> the anchor's frame.
        matrix =anchor.inverted() @ Matrix.Diagonal(Vector((UNITS_PER_METER,) * 3 + (1.0,))) @ UNITY_TO_SKYRIM @ worlds[i]
        for s, sub in enumerate(mesh["submeshes"]):
            mname = sub["material"]
            if not mname or mname in settings["skip_materials"] or not sub["triangles"]:
                continue
            info = model["materials"].get(mname, {"color": [1, 1, 1, 1], "textures": []})
            if mname not in materials:
                materials[mname] = make_material(mname, info, tex_dir, settings, shaders_blend, shader_io)
            c = info["color"]
            color = None if all(abs(x - 1.0) < 1e-3 for x in c[:3]) else (c[0], c[1], c[2], 1.0)
            name = f"{paths[i]}:{s}".replace("/", "|")
            obj = build_mesh(name, mesh, sub, matrix, materials[mname], color)
            obj.parent = parent
            count += 1

    print(f"build_nif: {count} shapes, {len(materials)} materials")
    os.makedirs(os.path.dirname(out_nif), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in scene.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    result = bpy.ops.export_scene.pynifly(filepath=out_nif, target_game="SKYRIMSE", blender_xf=False,
                                          export_colors=True, export_modifiers=False)
    print(f"build_nif: export {result} -> {out_nif}")
    if not os.path.exists(out_nif):
        raise SystemExit("build_nif: no NIF written")


main()
