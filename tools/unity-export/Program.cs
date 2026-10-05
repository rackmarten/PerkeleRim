using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetStudio;
using Newtonsoft.Json;

// UnityExport <data folder> <root GameObject name> <output folder>
//
// Writes <output>/model.json (the GameObject tree in raw Unity space: local transforms, mesh arrays,
// materials) and the materials' textures as PNGs next to it, for tools/nif/build_nif.py.
// Not FBX: AssetStudioModCLI's splitObjects crashes on Unity 5.0 animation clips, and the FBX its
// libraries write sends Blender's importer into endless recursion.
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: UnityExport <data folder> <root GameObject name> <output folder>");
            return 2;
        }
        string data = args[0], name = args[1], output = args[2];

        var manager = new AssetsManager();
        manager.LoadFilesAndFolders(out _, data);

        var roots = manager.AssetsFileList
            .SelectMany(f => f.Objects)
            .OfType<GameObject>()
            .Where(g => g.m_Name == name && g.m_Transform != null && g.m_Transform.m_Father.IsNull)
            .ToList();
        if (roots.Count != 1)
        {
            Console.Error.WriteLine($"expected one root GameObject named \"{name}\", found {roots.Count}");
            return 1;
        }

        Directory.CreateDirectory(output);
        var model = new Model();
        Walk(roots[0], -1, model);

        // Textures (decoded by AssetStudio's converter) and material properties.
        var converted = new ModelConverter(roots[0], ImageFormat.Png, new List<AnimationClip>());
        foreach (var tex in converted.TextureList)
            File.WriteAllBytes(Path.Combine(output, tex.Name), tex.Data);
        foreach (var mat in converted.MaterialList)
            model.materials[mat.Name] = new MaterialOut
            {
                color = new[] { mat.Diffuse.R, mat.Diffuse.G, mat.Diffuse.B, mat.Diffuse.A },
                textures = mat.Textures.Select(t => new TextureOut
                {
                    file = t.Name,
                    slot = t.Dest == 0 ? "diffuse" : t.Dest == 3 ? "normal" : t.Dest == 2 ? "specular" : "other",
                    offset = new[] { t.Offset.X, t.Offset.Y },
                    scale = new[] { t.Scale.X, t.Scale.Y },
                }).ToList(),
            };

        File.WriteAllText(Path.Combine(output, "model.json"), JsonConvert.SerializeObject(model));
        Console.WriteLine($"exported {model.nodes.Count} nodes, {model.nodes.Count(n => n.mesh != null)} meshes, " +
            $"{model.materials.Count} materials, {converted.TextureList.Count} textures to {output}");
        return 0;
    }

    static void Walk(GameObject go, int parent, Model model)
    {
        var t = go.m_Transform;
        var node = new NodeOut
        {
            name = go.m_Name,
            parent = parent,
            position = new[] { t.m_LocalPosition.X, t.m_LocalPosition.Y, t.m_LocalPosition.Z },
            rotation = new[] { t.m_LocalRotation.X, t.m_LocalRotation.Y, t.m_LocalRotation.Z, t.m_LocalRotation.W },
            scale = new[] { t.m_LocalScale.X, t.m_LocalScale.Y, t.m_LocalScale.Z },
        };
        if (go.m_MeshFilter != null && go.m_MeshRenderer != null && go.m_MeshFilter.m_Mesh.TryGet(out var mesh))
            node.mesh = ConvertMesh(mesh, go.m_MeshRenderer);
        int index = model.nodes.Count;
        model.nodes.Add(node);
        foreach (var child in t.m_Children)
            if (child.TryGet(out var ct) && ct.m_GameObject.TryGet(out var cgo))
                Walk(cgo, index, model);
    }

    // Raw Unity data: positions/normals per vertex (xyz), first UV set, triangles per submesh with
    // absolute vertex indices, each submesh with its renderer material.
    static MeshOut ConvertMesh(Mesh mesh, Renderer renderer)
    {
        if (mesh.m_Vertices == null) mesh.ProcessData();  // AssetStudio decodes vertex data lazily
        int count = mesh.m_VertexCount;
        int stride = mesh.m_Vertices.Length / count;
        var result = new MeshOut { name = mesh.m_Name, vertices = Take3(mesh.m_Vertices, count, stride) };
        if (mesh.m_Normals != null && mesh.m_Normals.Length >= count * 3)
            result.normals = Take3(mesh.m_Normals, count, mesh.m_Normals.Length / count);
        var uv = mesh.GetUV(0);
        if (uv != null && uv.Length >= count * 2)
        {
            int c = uv.Length / count;
            result.uv = new float[count * 2];
            for (int i = 0; i < count; i++)
            {
                result.uv[i * 2] = uv[i * c];
                result.uv[i * 2 + 1] = uv[i * c + 1];
            }
        }
        int first = 0;
        for (int s = 0; s < mesh.m_SubMeshes.Count; s++)
        {
            int n = (int)mesh.m_SubMeshes[s].indexCount;
            string material = null;
            if (s < renderer.m_Materials.Count && renderer.m_Materials[s].TryGet(out var mat))
                material = mat.m_Name;
            result.submeshes.Add(new SubmeshOut
            {
                material = material,
                triangles = mesh.m_Indices.Skip(first).Take(n).Select(i => (int)i).ToArray(),
            });
            first += n;
        }
        return result;
    }

    static float[] Take3(float[] src, int count, int stride)
    {
        var dst = new float[count * 3];
        for (int i = 0; i < count; i++)
            for (int k = 0; k < 3; k++)
                dst[i * 3 + k] = src[i * stride + k];
        return dst;
    }

    class Model
    {
        public string space = "unity";  // left-handed, Y up, meters
        public List<NodeOut> nodes = new List<NodeOut>();
        public Dictionary<string, MaterialOut> materials = new Dictionary<string, MaterialOut>();
    }

    class NodeOut
    {
        public string name;
        public int parent;
        public float[] position, rotation, scale;
        public MeshOut mesh;
    }

    class MeshOut
    {
        public string name;
        public float[] vertices, normals, uv;
        public List<SubmeshOut> submeshes = new List<SubmeshOut>();
    }

    class SubmeshOut
    {
        public string material;
        public int[] triangles;
    }

    class MaterialOut
    {
        public float[] color;
        public List<TextureOut> textures;
    }

    class TextureOut
    {
        public string file, slot;
        public float[] offset, scale;
    }
}
