using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Sends MSC's own models to Skyrim (ModelChunk messages), read from the running game: the active
    // MeshRenderers of an object a filter picks, with their diffuse textures copied through a
    // RenderTexture (MSC's have no CPU copy), at the end of a frame. A model is known by what it is
    // made of (meshes, textures, their placement), so identical items share one id and it goes out
    // once per link session; everything is sent again after the link comes back up (Skyrim may have
    // restarted). Ids start at a random base so a restarted MSC never reuses one Skyrim still has.
    public class ModelSender
    {
        const int TextureMax = 1024;  // larger textures are scaled down to this

        class Job
        {
            public Transform Root;
            public Predicate<Transform> Include;
            public Func<ModelData> Build;  // instead of Root: a model put together here (overlays)
            public uint Id;
            public int Session;  // -1 = read and log only (spike command "textures")
        }

        readonly Action<string> log;
        readonly MonoBehaviour host;  // runs the end-of-frame reads
        readonly Dictionary<string, uint> ids = new Dictionary<string, uint>();  // model key -> id
        readonly HashSet<uint> sent = new HashSet<uint>();    // this link session
        readonly HashSet<uint> queued = new HashSet<uint>();  // waiting, being read or being sent
        readonly Queue<Job> jobs = new Queue<Job>();
        readonly Queue<byte[]> pending = new Queue<byte[]>();
        uint pendingId;
        bool reading;
        bool wasUp;
        int session;
        uint nextId = ((uint)new System.Random().Next(1, 0x7FFF) << 16) | 1;

        public ModelSender(MonoBehaviour host, Action<string> log)
        {
            this.host = host;
            this.log = log;
        }

        // Reads an object's model without sending it and logs what came out (spike command "textures").
        public void Inspect(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) log("model: no " + name);
            else if (!reading) host.StartCoroutine(ReadAtEndOfFrame(new Job { Root = go.transform, Include = t => true, Session = -1 }));
        }

        // The id of the model made of root's renderers that include accepts (by their own transform),
        // queued for sending if Skyrim doesn't have it yet. 0 when there is nothing to draw.
        public uint IdFor(Transform root, Predicate<Transform> include)
        {
            var key = Key(root, include);
            if (key == null) return 0;
            uint id;
            if (!ids.TryGetValue(key, out id))
            {
                id = nextId++;
                ids[key] = id;
            }
            if (!sent.Contains(id) && !queued.Contains(id))
            {
                queued.Add(id);
                jobs.Enqueue(new Job { Root = root, Include = include, Id = id, Session = session });
            }
            return id;
        }

        // The id of a model build puts together (at the end of a frame, like a read one), known by
        // key and queued for sending if Skyrim doesn't have it yet. build returns null while what it
        // needs isn't there; the next call asks again.
        public uint IdForBuilt(string key, Func<ModelData> build)
        {
            key = "built:" + key;
            uint id;
            if (!ids.TryGetValue(key, out id))
            {
                id = nextId++;
                ids[key] = id;
            }
            if (!sent.Contains(id) && !queued.Contains(id))
            {
                queued.Add(id);
                jobs.Enqueue(new Job { Build = build, Id = id, Session = session });
            }
            return id;
        }

        // Once a frame: reads one queued model at a time and sends its chunks as the ring has room.
        public void Update(Link link)
        {
            if (link.IsUp != wasUp)
            {
                wasUp = link.IsUp;
                if (!wasUp) Forget();
            }
            if (!wasUp) return;
            if (pending.Count == 0 && !reading)
            {
                while (jobs.Count > 0)
                {
                    var job = jobs.Dequeue();
                    if (job.Root == null && job.Build == null) { queued.Remove(job.Id); continue; }  // destroyed; asked again when seen again
                    host.StartCoroutine(ReadAtEndOfFrame(job));
                    break;
                }
            }
            while (pending.Count > 0)
            {
                var payload = pending.Peek();
                if (!link.Send(MessageType.ModelChunk, payload, payload.Length)) break;  // ring full, next frame
                pending.Dequeue();
                if (pending.Count > 0) continue;
                sent.Add(pendingId);
                queued.Remove(pendingId);
            }
        }

        // The link went down: Skyrim may come back without anything sent so far.
        void Forget()
        {
            session++;
            sent.Clear();
            queued.Clear();
            jobs.Clear();
            pending.Clear();
            log("model: link down, models go out again once it is back");
        }

        // What a model is made of, or null without renderers: two objects with the same key look the same.
        // Meshes and textures go by content (name, size, bounds), not instance: MSC gives every spawned
        // copy of an item (a thrown bottle) its own mesh instance.
        static string Key(Transform root, Predicate<Transform> include)
        {
            StringBuilder key = null;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (!renderer.enabled || mesh == null || !include(renderer.transform)) continue;
                if (key == null) key = new StringBuilder();
                var bounds = mesh.bounds;
                key.Append(mesh.name).Append('/').Append(mesh.vertexCount).Append('/').Append(mesh.subMeshCount)
                    .Append(Round(bounds.center)).Append(Round(bounds.size)).Append(':');
                foreach (var material in renderer.sharedMaterials)
                {
                    var texture = material != null ? material.mainTexture : null;
                    if (texture != null) key.Append(texture.name).Append('/').Append(texture.width).Append('x').Append(texture.height);
                    if (material != null)
                        key.Append(material.mainTextureScale).Append(material.mainTextureOffset).Append(material.shader != null ? material.shader.name : "")
                            .Append(Color(material, "_Color")).Append(Float(material, "_Mode")).Append(Float(material, "_Metallic"))
                            .Append(Float(material, "_Glossiness"));
                    key.Append(',');
                }
                var m = RelativeTo(root, renderer.transform);
                for (int i = 0; i < 16; i++) key.Append(Mathf.Round(m[i] * 1000f)).Append(',');  // rotation, scale and offset
                key.Append(';');
            }
            return key != null ? key.ToString() : null;
        }

        static Vector3 Round(Vector3 v) => new Vector3(Mathf.Round(v.x * 1000f), Mathf.Round(v.y * 1000f), Mathf.Round(v.z * 1000f));

        // t in the root's frame without the root's position and rotation (the model carries the scale, a
        // pose only moves and turns it). Built from local transforms: going through world matrices out
        // on the island (10 km from Unity's origin) left millimeters of noise, so equal items got
        // different keys.
        public static Matrix4x4 RelativeTo(Transform root, Transform t)
        {
            var m = Matrix4x4.identity;
            for (var p = t; p != null && p != root; p = p.parent) m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return Matrix4x4.Scale(root.lossyScale) * m;
        }

        IEnumerator ReadAtEndOfFrame(Job job)
        {
            reading = true;
            yield return new WaitForEndOfFrame();
            reading = false;
            bool send = job.Session >= 0;
            if (send && job.Session != session) yield break;  // the link went down meanwhile
            if (job.Root == null && job.Build == null)
            {
                if (send) queued.Remove(job.Id);
                yield break;
            }
            var clock = Stopwatch.StartNew();
            ModelData model;
            try { model = job.Build != null ? job.Build() : Read(job.Root, job.Include); }
            catch (Exception e)
            {
                log("model: reading " + (job.Root != null ? job.Root.name : "a built model") + " FAILED: " + e);
                yield break;  // stays queued, so it isn't retried on every scan
            }
            if (model == null)
            {
                queued.Remove(job.Id);  // not there yet; the next IdForBuilt asks again
                yield break;
            }
            if (!send)
            {
                log($"model: {model.Name} has {model.Meshes.Count} meshes, {model.VertexCount} vertices, {model.Textures.Count} textures");
                for (int i = 0; i < model.Textures.Count; i++) SavePng(model.Textures[i], $"PerkeleRim_texture_{i}.png");
                foreach (var mesh in model.Meshes) log($"model: mesh {mesh.Name} {UvRange(mesh.Uvs)}");
                yield break;
            }
            if (model.Meshes.Count == 0)
            {
                queued.Remove(job.Id);  // its renderers went off since the scan; asked again by the next one
                yield break;
            }
            var blob = model.Encode();
            foreach (var payload in ModelData.Split(blob, job.Id)) pending.Enqueue(payload);
            pendingId = job.Id;
            log($"model: {job.Id:X8} {model.Name} has {model.Meshes.Count} meshes, {model.VertexCount} vertices, {model.Textures.Count} textures, " +
                $"{blob.Length} bytes in {pending.Count} chunks, read in {clock.ElapsedMilliseconds} ms");
        }

        ModelData Read(Transform root, Predicate<Transform> include)
        {
            var model = new ModelData { Name = root.name };
            var materials = new Materials();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!renderer.enabled || filter == null || filter.sharedMesh == null || !include(renderer.transform)) continue;
                AddRenderer(model, materials, renderer, RelativeTo(root, renderer.transform), PathBelow(root, renderer.transform), -1, null);
            }
            return model;
        }

        // The MSC materials and textures already in a model being read.
        public sealed class Materials
        {
            internal readonly Dictionary<string, int> Index = new Dictionary<string, int>();  // material instance id + look -> index in model
            internal readonly Dictionary<int, ModelTextureData> Pixels = new Dictionary<int, ModelTextureData>();  // Texture instance id -> as read
            internal readonly Dictionary<string, int> Baked = new Dictionary<string, int>();  // what a texture was baked from -> index in model
            internal readonly List<int> TextureOf = new List<int>();  // material index -> texture index
        }

        // How a caller draws MSC's materials instead of as they are (the van's rules).
        public sealed class Look
        {
            public string Key = "";                       // materials read with different looks are separate model materials
            public Predicate<Material> Keep;               // false leaves the submesh out; null keeps all
            public Func<Material, Texture> Diffuse;        // the texture drawn; null = the main texture
            public Func<Material, ModelMaterialData, ModelMaterialData> Adjust;  // null = as read
        }

        // Adds renderer's submeshes to model: moved by m (into the space of node, -1 = the model's
        // root), named name and "name#<n>" from the second submesh on.
        public void AddRenderer(ModelData model, Materials materials, MeshRenderer renderer, Matrix4x4 m, string name, int node, Look look)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) return;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var shared = renderer.sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var material = sub < shared.Length ? shared[sub] : null;
                if (look != null && look.Keep != null && !look.Keep(material)) continue;
                var triangles = mesh.GetTriangles(sub);
                if (triangles.Length == 0) continue;
                int index = MaterialIndex(model, materials, material, look);
                var scale = material != null ? material.mainTextureScale : Vector2.one;
                var offset = material != null ? material.mainTextureOffset : Vector2.zero;
                var data = Submesh(sub == 0 ? name : name + "#" + sub, index >= 0 ? materials.TextureOf[index] : -1, m, vertices, normals, uvs, scale, offset, triangles);
                data.Material = index;
                data.Node = node;
                model.Meshes.Add(data);
            }
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

        public static string PathBelow(Transform root, Transform t)
        {
            if (t == root) return root.name;
            string path = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        // One material per MSC material, with a texture made of the material's main texture with its
        // color tint and metal baked in, so two materials sharing a texture (an atlas) may still look
        // different. Materials that bake to the same texture (copies of one material, as the van's
        // windows have) share it. -1 without a material.
        int MaterialIndex(ModelData model, Materials materials, Material material, Look look)
        {
            if (material == null) return -1;
            int index;
            string key = material.GetInstanceID() + (look != null ? "/" + look.Key : "");
            if (materials.Index.TryGetValue(key, out index)) return index;

            var pixels = materials.Pixels;
            var mainTexture = look != null && look.Diffuse != null ? look.Diffuse(material) : material.mainTexture;
            var metalTexture = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;
            var main = ReadTexture(pixels, mainTexture);
            var metalMap = ReadTexture(pixels, metalTexture);
            var unity = new ModelMaterialData.Unity
            {
                Shader = material.shader != null ? material.shader.name : "",
                RenderQueue = material.renderQueue,
                Mode = Float(material, "_Mode"),
                Cutoff = Float(material, "_Cutoff"),
                Metallic = metalMap != null ? Average(metalMap.Rgba, 0) : Float(material, "_Metallic"),
                Glossiness = metalMap != null ? Average(metalMap.Rgba, 3) : Float(material, "_Glossiness"),
                SpecColor = Color(material, "_SpecColor"),
                Shininess = Float(material, "_Shininess"),
                Emission = Color(material, "_EmissionColor"),
            };
            var data = ModelMaterialData.FromUnity(unity);
            if (look != null && look.Adjust != null) data = look.Adjust(material, data);
            bool standard = unity.Shader.StartsWith("Standard");
            var tint = Color(material, "_Color") ?? new[] { 1f, 1f, 1f, 1f };
            var bakeMetal = standard ? metalMap : null;
            float metallic = standard && metalMap == null ? (unity.Metallic ?? 0f) : 0f;
            string bakeKey = (mainTexture != null ? mainTexture.GetInstanceID() : 0) + "/" + (bakeMetal != null ? metalTexture.GetInstanceID() : 0) + "/" +
                Mathf.Round(metallic * 1000f) + "/" + string.Join(",", Array.ConvertAll(tint, c => Mathf.Round(c * 1000f).ToString()));
            int texture;
            ModelTextureData baked;
            if (materials.Baked.TryGetValue(bakeKey, out texture)) baked = model.Textures[texture];
            else
            {
                baked = ModelTextureData.Baked(main, tint, bakeMetal, metallic);
                texture = model.Textures.Count;
                model.Textures.Add(baked);
                materials.Baked[bakeKey] = texture;
            }

            index = model.Materials.Count;
            model.Materials.Add(data);
            materials.TextureOf.Add(texture);
            materials.Index[key] = index;
            log($"model: material {material.name} shader \"{unity.Shader}\" queue {unity.RenderQueue} flags {data.Flags} cutoff {data.AlphaCutoff:0.##} " +
                $"tint ({tint[0]:0.##},{tint[1]:0.##},{tint[2]:0.##},{tint[3]:0.##}) metal {unity.Metallic:0.##} gloss {data.Glossiness:0.##} " +
                $"specular {data.Specular[0]:0.##} -> average {Average(baked.Rgba)}");
            return index;
        }

        ModelTextureData ReadTexture(Dictionary<int, ModelTextureData> pixels, Texture texture)
        {
            if (texture == null) return null;
            ModelTextureData data;
            if (!pixels.TryGetValue(texture.GetInstanceID(), out data))
            {
                data = Pixels(texture, log);
                pixels[texture.GetInstanceID()] = data;
            }
            return data;
        }

        static string UvRange(float[] uvs)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            for (int i = 0; i + 1 < uvs.Length; i += 2)
            {
                u0 = Math.Min(u0, uvs[i]); u1 = Math.Max(u1, uvs[i]);
                v0 = Math.Min(v0, uvs[i + 1]); v1 = Math.Max(v1, uvs[i + 1]);
            }
            var first = new StringBuilder();
            for (int i = 0; i + 1 < uvs.Length && i < 12; i += 2) first.Append($" ({uvs[i]:0.###},{uvs[i + 1]:0.###})");
            return $"u {u0:0.###}..{u1:0.###} v {v0:0.###}..{v1:0.###} first{first}";
        }

        // A texture as it goes to Skyrim (top row first), saved next to the game for a look (spike
        // command "textures").
        void SavePng(ModelTextureData data, string file)
        {
            var bottomUp = new Color32[data.Width * data.Height];
            for (int y = 0; y < data.Height; y++)
                for (int x = 0; x < data.Width; x++)
                {
                    int i = ((data.Height - 1 - y) * data.Width + x) * 4;
                    bottomUp[y * data.Width + x] = new Color32(data.Rgba[i], data.Rgba[i + 1], data.Rgba[i + 2], data.Rgba[i + 3]);
                }
            var texture = new Texture2D(data.Width, data.Height, TextureFormat.ARGB32, false);
            texture.SetPixels32(bottomUp);
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), file);
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.Destroy(texture);
            log("model: saved " + path);
        }

        // A material property as Skyrim's material wants it, or null when the shader has none.
        static float? Float(Material m, string name) => m.HasProperty(name) ? m.GetFloat(name) : (float?)null;

        static float[] Color(Material m, string name)
        {
            if (!m.HasProperty(name)) return null;
            var c = m.GetColor(name);
            return new[] { c.r, c.g, c.b, c.a };
        }

        static float Average(byte[] rgba, int channel)
        {
            long sum = 0;
            for (int i = channel; i < rgba.Length; i += 4) sum += rgba[i];
            return sum / 255f / Math.Max(1, rgba.Length / 4);
        }

        // MSC's own texture as a screen overlay model (the blood over the view when the player dies):
        // null while no loaded texture has that name.
        public static ModelData Overlay(string modelName, string textureName, Action<string> log)
        {
            foreach (Texture texture in Resources.FindObjectsOfTypeAll(typeof(Texture)))
                if (texture.name == textureName)
                    return ModelData.Overlay(modelName, Pixels(texture, log));
            return null;
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
