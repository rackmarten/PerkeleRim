using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Sends MSC's own models to Skyrim (ModelChunk messages), read from the running game: every
    // active MeshRenderer below an object, with its diffuse textures copied through a RenderTexture
    // (MSC's have no CPU copy), at the end of a frame. Spike: the first beer case goes once the
    // player is handed over, and spike command "model <name>" sends any object GameObject.Find finds.
    public class ModelSender
    {
        const string FirstModel = "beer case(itemx)";
        const int TextureMax = 1024;  // larger textures are scaled down to this

        readonly Action<string> log;
        readonly MonoBehaviour host;  // runs the end-of-frame reads
        bool reading;
        readonly Queue<byte[]> pending = new Queue<byte[]>();
        string requested = FirstModel;
        uint nextId = 1;

        public ModelSender(MonoBehaviour host, Action<string> log)
        {
            this.host = host;
            this.log = log;
        }

        public void Request(string name) => requested = name;

        // Reads an object's model without sending it and logs what came out (spike command "textures").
        public void Inspect(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) log("model: no " + name);
            else if (!reading) host.StartCoroutine(ReadAtEndOfFrame(go, false));
        }

        public void Update(Link link, bool playing)
        {
            if (!playing) return;
            if (requested != null && pending.Count == 0 && !reading)
            {
                var go = GameObject.Find(requested);
                if (go == null) log("model: no " + requested);
                else host.StartCoroutine(ReadAtEndOfFrame(go, true));
                requested = null;
            }
            while (pending.Count > 0)
            {
                var payload = pending.Peek();
                if (!link.Send(MessageType.ModelChunk, payload, payload.Length)) break;  // ring full, next frame
                pending.Dequeue();
                if (pending.Count == 0) log("model: sent");
            }
        }

        IEnumerator ReadAtEndOfFrame(GameObject go, bool send)
        {
            reading = true;
            yield return new WaitForEndOfFrame();
            reading = false;
            if (go == null) yield break;
            var clock = Stopwatch.StartNew();
            ModelData model;
            try { model = Read(go.transform); }
            catch (Exception e) { log("model: reading " + go.name + " FAILED: " + e); yield break; }
            if (!send)
            {
                log($"model: {model.Name} has {model.Meshes.Count} meshes, {model.VertexCount} vertices, {model.Textures.Count} textures");
                yield break;
            }
            var blob = model.Encode();
            foreach (var payload in ModelData.Split(blob, nextId++)) pending.Enqueue(payload);
            log($"model: {model.Name} has {model.Meshes.Count} meshes, {model.VertexCount} vertices, {model.Textures.Count} textures, " +
                $"{blob.Length} bytes in {pending.Count} chunks, read in {clock.ElapsedMilliseconds} ms");
        }

        ModelData Read(Transform root)
        {
            var model = new ModelData { Name = root.name };
            var textures = new Dictionary<int, int>();  // Texture instance id -> index in model
            var toRoot = root.worldToLocalMatrix;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (!renderer.enabled || mesh == null) continue;

                var m = toRoot * renderer.transform.localToWorldMatrix;
                var vertices = mesh.vertices;
                var normals = mesh.normals;
                var uvs = mesh.uv;
                var materials = renderer.sharedMaterials;
                string path = PathBelow(root, renderer.transform);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var material = sub < materials.Length ? materials[sub] : null;
                    int texture = TextureIndex(model, textures, material);
                    var scale = material != null ? material.mainTextureScale : Vector2.one;
                    var offset = material != null ? material.mainTextureOffset : Vector2.zero;
                    model.Meshes.Add(Submesh(sub == 0 ? path : path + "#" + sub, texture, m, vertices, normals, uvs, scale, offset,
                        mesh.GetTriangles(sub)));
                }
            }
            return model;
        }

        // Only the vertices this submesh uses, moved into the root's space.
        static ModelMeshData Submesh(string name, int texture, Matrix4x4 m, Vector3[] vertices, Vector3[] normals, Vector2[] uvs,
            Vector2 uvScale, Vector2 uvOffset, int[] triangles)
        {
            var remap = new Dictionary<int, int>();
            var used = new List<int>();
            var tris = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                int index;
                if (!remap.TryGetValue(triangles[i], out index))
                {
                    index = used.Count;
                    remap[triangles[i]] = index;
                    used.Add(triangles[i]);
                }
                tris[i] = index;
            }
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            bool hasUvs = uvs != null && uvs.Length == vertices.Length;
            var p = new float[used.Count * 3];
            var n = new float[used.Count * 3];
            var t = new float[used.Count * 2];
            for (int i = 0; i < used.Count; i++)
            {
                var v = m.MultiplyPoint3x4(vertices[used[i]]);
                var nv = hasNormals ? m.MultiplyVector(normals[used[i]]).normalized : Vector3.up;
                var uv = hasUvs ? Vector2.Scale(uvs[used[i]], uvScale) + uvOffset : Vector2.zero;
                p[i * 3] = v.x; p[i * 3 + 1] = v.y; p[i * 3 + 2] = v.z;
                n[i * 3] = nv.x; n[i * 3 + 1] = nv.y; n[i * 3 + 2] = nv.z;
                t[i * 2] = uv.x; t[i * 2 + 1] = uv.y;
            }
            return ModelMeshData.FromUnity(name, texture, p, n, t, tris, Determinant(m) < 0f);
        }

        static float Determinant(Matrix4x4 m) =>
            m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) -
            m.m01 * (m.m10 * m.m22 - m.m12 * m.m20) +
            m.m02 * (m.m10 * m.m21 - m.m11 * m.m20);

        static string PathBelow(Transform root, Transform t)
        {
            if (t == root) return root.name;
            string path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        int TextureIndex(ModelData model, Dictionary<int, int> known, Material material)
        {
            var texture = material != null ? material.mainTexture : null;
            if (texture == null) return -1;
            int index;
            if (known.TryGetValue(texture.GetInstanceID(), out index)) return index;
            index = model.Textures.Count;
            model.Textures.Add(Pixels(texture, log));
            known[texture.GetInstanceID()] = index;
            return index;
        }

        // Straight from the texture when it is readable, else drawn into a RenderTexture and read back.
        static ModelTextureData Pixels(Texture texture, Action<string> log)
        {
            var data = Direct(texture as Texture2D) ?? Blitted(texture);
            var t2 = texture as Texture2D;
            log($"model: texture {texture.name} {(t2 != null ? t2.format.ToString() : texture.GetType().Name)} {texture.width}x{texture.height} -> " +
                $"{data.Width}x{data.Height} average {Average(data.Rgba)}");
            return data;
        }

        static ModelTextureData Direct(Texture2D texture)
        {
            if (texture == null || texture.width > Wire.ModelTextureSizeMax || texture.height > Wire.ModelTextureSizeMax) return null;
            Color32[] pixels;
            try { pixels = texture.GetPixels32(); }
            catch (Exception) { return null; }  // not readable
            if (pixels == null || pixels.Length != texture.width * texture.height || Uninitialized(pixels)) return null;
            return ModelTextureData.FromUnity(texture.width, texture.height, Bytes(pixels));
        }

        // A texture without a CPU copy reads back as the debug heap's fill bytes.
        static bool Uninitialized(Color32[] pixels)
        {
            foreach (var p in pixels)
                if (p.r != 0xCD || p.g != 0xCD || p.b != 0xCD || p.a != 0xCD) return false;
            return true;
        }

        static byte[] Bytes(Color32[] pixels)
        {
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = pixels[i].a;
            }
            return bytes;
        }

        static string Average(byte[] rgba)
        {
            long r = 0, g = 0, b = 0, a = 0;
            for (int i = 0; i < rgba.Length; i += 4) { r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; a += rgba[i + 3]; }
            long n = Math.Max(1, rgba.Length / 4);
            return $"({r / n},{g / n},{b / n},{a / n})";
        }

        static ModelTextureData Blitted(Texture texture)
        {
            int w = texture.width, h = texture.height;
            while (w > TextureMax || h > TextureMax)
            {
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var active = RenderTexture.active;
            var copy = new Texture2D(w, h, TextureFormat.ARGB32, false);  // Unity 5.0 ReadPixels refuses RGBA32
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                copy.Apply();
                return ModelTextureData.FromUnity(w, h, Bytes(copy.GetPixels32()));
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
