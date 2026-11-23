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
    // RenderTexture (MSC's have no CPU copy). A model is known by what it is made of (meshes,
    // textures, their placement), so identical items share one id and it goes out once per link
    // session; everything is sent again after the link comes back up (Skyrim may have restarted).
    // Ids start at a random base so a restarted MSC never reuses one Skyrim still has.
    //
    // Reading never holds a frame for long: a model is read in small steps (a texture, a band of a
    // bake, a submesh, a mesh encoded) at the end of frames, up to BudgetMs a frame (StepBudget),
    // small models several to a frame. The next model is read while the last one's chunks go out.
    // Textures are kept once read (SizedCache) while models are being read, since many models share
    // them (the Satsuma's parts), and let go once nothing is left to read for a few seconds.
    // Skyrim keeps showing what it has until a model has arrived whole.
    public class ModelSender
    {
        const int TextureMax = 1024;          // larger textures are scaled down to this
        const double BudgetMs = 6;            // reading per frame; the steps (LongestStepMs) add to it
        const int BakePixelsPerStep = 1 << 17;  // a bake step's band of rows: about this many pixels
        // Memory on top of the model being read (Unity 5's GC never compacts, so keep peaks low): the
        // textures kept between models, at most a quarter of a link-up's (about 190 MB of models,
        // mostly 1024x1024 RGBA textures), and the read models waiting to go out, about the van.
        const long TextureCacheBytes = 48L << 20;
        const long ReadAheadBytes = 32L << 20;  // no new read while this much waits to be sent
        const double IdleMs = 5000;  // nothing to read or send this long: the textures kept go

        // A model to read: Steps yields what it is doing (a string) after each bit of work and the
        // ModelData last; nothing when there is nothing to draw yet (asked again by the next call).
        class Job
        {
            public Func<IEnumerable> Steps;
            public Func<bool> Gone;  // the object it reads was destroyed: asked again when seen again
            public string What;
            public uint Id;
            public int Session;
        }

        // A read model on its way out, a chunk at a time as the ring has room.
        class Outgoing
        {
            public uint Id;
            public byte[] Blob;
            public int Offset;
        }

        readonly Action<string> log;
        readonly MonoBehaviour host;  // runs the end-of-frame reads
        readonly Dictionary<string, uint> ids = new Dictionary<string, uint>();  // model key -> id
        readonly HashSet<uint> sent = new HashSet<uint>();    // this link session
        readonly HashSet<uint> queued = new HashSet<uint>();  // waiting, being read or being sent
        readonly Queue<Job> jobs = new Queue<Job>();
        readonly Queue<Outgoing> outgoing = new Queue<Outgoing>();
        long outgoingBytes;
        readonly byte[] payload = new byte[16 + Wire.ModelChunkMax];  // sizeof(ModelChunk) + the most data
        Job current;
        IEnumerator currentSteps;
        readonly Stopwatch currentClock = new Stopwatch();  // runs while its steps do
        int currentFrames;
        string lastStep = "";  // what the reading did last, for a long frame's log line
        bool scheduled;    // an end-of-frame read is on its way this frame
        bool inspecting;
        bool wasUp;
        int session;
        uint nextId = ((uint)new System.Random().Next(1, 0x7FFF) << 16) | 1;
        readonly Stopwatch frameClock = Stopwatch.StartNew();
        readonly StepBudget budget;
        readonly SizedCache<int, ModelTextureData> textures = new SizedCache<int, ModelTextureData>(TextureCacheBytes);  // Texture instance id -> as read

        // MSC's frames from the link coming up (or Again) until the models have gone out: what a
        // player feels of the reads (logged as "model: link-up: sent ...", "model: models again: ...").
        double lastFrame = -1;
        FrameWatch watch;
        double watchStart, idleSince = -1, lastChunkAt, longestGap, longestStepMs;
        long watchReadMs;
        string longestStep, watchWhat;
        int watchModels, watchChunks;
        long watchBytes;
        const double WatchMaxMs = 180000;

        public ModelSender(MonoBehaviour host, Action<string> log)
        {
            this.host = host;
            this.log = log;
            budget = new StepBudget(BudgetMs, () => frameClock.Elapsed.TotalMilliseconds);
        }

        // Reads an object's model without sending it and logs what came out (spike command "textures").
        public void Inspect(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) log("model: no " + name);
            else if (!inspecting) host.StartCoroutine(InspectAtEndOfFrame(go.transform));
        }

        // The id of the model made of root's renderers that include accepts (by their own transform),
        // queued for sending if Skyrim doesn't have it yet. 0 when there is nothing to draw.
        public uint IdFor(Transform root, Predicate<Transform> include)
        {
            var key = Key(root, include);
            if (key == null) return 0;
            return Queue(key, () => Read(root, include), () => root == null, root.name);
        }

        // The id of a model build puts together in one go (an overlay, a particle), known by key and
        // queued for sending if Skyrim doesn't have it yet. build returns null while what it needs
        // isn't there; the next call asks again.
        public uint IdForBuilt(string key, Func<ModelData> build) => Queue("built:" + key, () => Once(build), null, key);

        // The same for a model read in steps (VehicleModel.Read: yields after each bit of work, the
        // ModelData last).
        public uint IdForRead(string key, Func<IEnumerable> steps) => Queue("built:" + key, steps, null, key);

        uint Queue(string key, Func<IEnumerable> steps, Func<bool> gone, string what)
        {
            uint id;
            if (!ids.TryGetValue(key, out id))
            {
                id = nextId++;
                ids[key] = id;
            }
            if (!sent.Contains(id) && !queued.Contains(id))
            {
                queued.Add(id);
                jobs.Enqueue(new Job { Steps = steps, Gone = gone, What = what, Id = id, Session = session });
            }
            return id;
        }

        static IEnumerable Once(Func<ModelData> build)
        {
            var model = build();
            if (model != null) yield return model;
        }

        // Sends every model again as if the link had just come up (spike command "models again"), and
        // with cold as if MSC had just started: new ids, everything read again, no texture kept.
        public void Again(bool cold)
        {
            if (watch != null) LogWatch(frameClock.Elapsed.TotalMilliseconds, false);  // the stretch before, cut short
            Forget();
            if (cold)
            {
                ids.Clear();
                textures.Clear();
            }
            if (wasUp) Watch(cold ? "models again cold" : "models again");
        }

        // Once a frame: sends read models' chunks as the ring has room, and has the next models read
        // at the end of the frame.
        public void Update(Link link)
        {
            WatchFrame();
            if (link.IsUp != wasUp)
            {
                wasUp = link.IsUp;
                if (!wasUp) Forget();
                else Watch("link-up");
            }
            if (!wasUp) return;
            Send(link);
            if ((current != null || jobs.Count > 0) && !scheduled)
            {
                scheduled = true;
                host.StartCoroutine(ReadAtEndOfFrame());
            }
        }

        void Send(Link link)
        {
            while (outgoing.Count > 0)
            {
                var o = outgoing.Peek();
                int size = ModelData.ChunkInto(o.Blob, o.Id, o.Offset, payload);
                if (!link.Send(MessageType.ModelChunk, payload, size)) break;  // ring full, next frame
                o.Offset += size - 16;
                double now = frameClock.Elapsed.TotalMilliseconds;
                if (watch != null)
                {
                    if (watchChunks > 0) longestGap = Math.Max(longestGap, now - lastChunkAt);
                    lastChunkAt = now;
                    watchChunks++;
                    watchBytes += size;
                }
                if (o.Offset < o.Blob.Length) continue;
                outgoing.Dequeue();
                outgoingBytes -= o.Blob.Length;
                sent.Add(o.Id);
                queued.Remove(o.Id);
                watchModels++;
            }
        }

        // The link went down: Skyrim may come back without anything sent so far.
        void Forget()
        {
            session++;
            sent.Clear();
            queued.Clear();
            jobs.Clear();
            outgoing.Clear();
            outgoingBytes = 0;
            current = null;
            currentSteps = null;
            log("model: link down, models go out again once it is back");
        }

        // At the end of the frame (the textures are copied through a RenderTexture): the models' read
        // steps for this frame's budget, as many models as fit.
        IEnumerator ReadAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            scheduled = false;
            if (!wasUp) yield break;
            budget.BeginFrame();
            bool any = false;
            while (!budget.Spent)
            {
                if (current == null && !Next()) break;
                any = true;
                var job = current;
                bool ended;
                currentClock.Start();
                try { ended = budget.Run(currentSteps); }
                catch (Exception e)
                {
                    log($"model: reading {job.What} FAILED: {e}");
                    current = null;  // stays queued, so it isn't retried on every scan
                    currentSteps = null;
                    continue;
                }
                finally { currentClock.Stop(); }
                lastStep = currentSteps != null ? currentSteps.Current as string ?? job.What : job.What;
                if (ended)
                {
                    current = null;
                    currentSteps = null;
                }
            }
            if (any && current != null) currentFrames++;
            if (!any) lastStep = "";
        }

        // Starts reading the next queued model, unless enough waits to be sent already.
        bool Next()
        {
            while (jobs.Count > 0 && outgoingBytes < ReadAheadBytes)
            {
                var job = jobs.Dequeue();
                if (job.Session != session) continue;
                if (job.Gone != null && job.Gone()) { queued.Remove(job.Id); continue; }
                current = job;
                currentSteps = Run(job).GetEnumerator();
                currentClock.Reset();
                currentFrames = 1;
                budget.ResetLongest();
                return true;
            }
            return false;
        }

        // A job's steps: its own, then the encode, then it goes out.
        IEnumerable Run(Job job)
        {
            ModelData model = null;
            foreach (var step in job.Steps())
            {
                var read = step as ModelData;
                if (read != null) model = read;
                yield return step as string ?? job.What;
            }
            if (model == null || model.Meshes.Count == 0)
            {
                queued.Remove(job.Id);  // not there yet, or its renderers went off since the scan: asked again
                yield break;
            }
            byte[] blob = null;
            foreach (var step in model.EncodeSteps())
            {
                if (step != null) blob = step;
                yield return "encoding " + model.Name;
            }
            if (job.Session != session) yield break;  // the link went down meanwhile
            outgoing.Enqueue(new Outgoing { Id = job.Id, Blob = blob });
            outgoingBytes += blob.Length;
            if (watch != null)
            {
                watchReadMs += currentClock.ElapsedMilliseconds;
                if (budget.LongestStepMs > longestStepMs)
                {
                    longestStepMs = budget.LongestStepMs;
                    longestStep = budget.LongestStep;
                }
            }
            log($"model: {job.Id:X8} {model.Name} has {model.Meshes.Count} meshes, {model.VertexCount} vertices, {model.Textures.Count} textures, " +
                $"{blob.Length} bytes in {(blob.Length + Wire.ModelChunkMax - 1) / Wire.ModelChunkMax} chunks, read in {currentClock.ElapsedMilliseconds} ms over {currentFrames} frames " +
                $"(longest step {budget.LongestStepMs:0.0} ms: {budget.LongestStep}; textures kept {textures.Count}, {textures.Size >> 20} MB)");
        }

        void Watch(string what)
        {
            watchWhat = what;
            watch = new FrameWatch();
            watchStart = frameClock.Elapsed.TotalMilliseconds;
            watchModels = watchChunks = 0;
            watchBytes = watchReadMs = 0;
            longestGap = longestStepMs = 0;
            longestStep = "";
        }

        void WatchFrame()
        {
            double now = frameClock.Elapsed.TotalMilliseconds;
            if (lastFrame >= 0 && watch != null)
            {
                double ms = now - lastFrame;
                watch.Add(ms);
                if (ms > FrameWatch.HitchMs)
                    log($"model: a {ms:0} ms frame, {(lastStep.Length > 0 ? "reading (last step: " + lastStep + ")" : "no model read in it")}, " +
                        $"{outgoing.Count} read models waiting to go out");
            }
            lastFrame = now;
            bool idle = jobs.Count == 0 && current == null && outgoing.Count == 0;
            if (!idle) idleSince = -1;
            else if (idleSince < 0) idleSince = now;
            bool settled = idle && now - idleSince >= IdleMs;
            if (settled && textures.Count > 0)
            {
                log($"model: nothing left to read, letting go of {textures.Count} textures kept ({textures.Size >> 20} MB, {textures.Hits} reused, {textures.Misses} read)");
                textures.Clear();
            }
            if (watch == null) return;
            bool done = watchModels > 0 && settled;
            if (done || now - watchStart >= WatchMaxMs) LogWatch(now, done);
        }

        // The stretch's frames and what was sent; done: it ended IdleMs ago.
        void LogWatch(double now, bool done)
        {
            log($"model: {watchWhat}: sent {watchModels} models, {watchChunks} chunks, {watchBytes} bytes in {(now - watchStart - (done ? IdleMs : 0)) / 1000:0.0} s" +
                $"{(done ? "" : " (cut short)")}: {watch.Summary()}; reading took {watchReadMs:0} ms, longest step {longestStepMs:0.0} ms ({longestStep}), " +
                $"longest gap between chunks {longestGap:0} ms");
            watch = null;
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
        // on the island (kilometers from Unity's origin) left noise, so equal items got different keys.
        public static Matrix4x4 RelativeTo(Transform root, Transform t)
        {
            var m = Matrix4x4.identity;
            for (var p = t; p != null && p != root; p = p.parent) m = Matrix4x4.TRS(p.localPosition, p.localRotation, p.localScale) * m;
            return Matrix4x4.Scale(root.lossyScale) * m;
        }

        IEnumerator InspectAtEndOfFrame(Transform root)
        {
            inspecting = true;
            yield return new WaitForEndOfFrame();
            inspecting = false;
            if (root == null) yield break;
            ModelData model = null;
            try
            {
                foreach (var step in Read(root, t => true))
                    if (step is ModelData) model = (ModelData)step;
            }
            catch (Exception e)
            {
                log("model: reading " + root.name + " FAILED: " + e);
                yield break;
            }
            if (model == null) yield break;
            log($"model: {model.Name} has {model.Meshes.Count} meshes, {model.VertexCount} vertices, {model.Textures.Count} textures");
            for (int i = 0; i < model.Textures.Count; i++) SavePng(model.Textures[i], $"PerkeleRim_texture_{i}.png");
            foreach (var mesh in model.Meshes) log($"model: mesh {mesh.Name} {UvRange(mesh.Uvs)}");
        }

        IEnumerable Read(Transform root, Predicate<Transform> include)
        {
            // Read over several frames: should the item or a part of it go meanwhile (eaten, drunk,
            // despawned), there is no model, rather than part of one under the whole item's id; the
            // next scan asks again.
            var model = new ModelData { Name = root.name };
            var materials = new Materials();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (root == null || renderer == null) yield break;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!renderer.enabled || filter == null || filter.sharedMesh == null || !include(renderer.transform)) continue;
                foreach (var step in AddRenderer(model, materials, renderer, RelativeTo(root, renderer.transform), PathBelow(root, renderer.transform), -1, null))
                    yield return step;
                if (renderer == null) yield break;
            }
            if (root == null) yield break;
            yield return model;
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
        // root), named name and "name#<n>" from the second submesh on. Steps: the mesh's arrays, each
        // material's textures and bake, each submesh. Stops when the renderer is destroyed meanwhile
        // (the caller then takes out what it added).
        public IEnumerable AddRenderer(ModelData model, Materials materials, MeshRenderer renderer, Matrix4x4 m, string name, int node, Look look)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null) yield break;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var shared = renderer.sharedMaterials;
            int subMeshes = mesh.subMeshCount;
            yield return name;
            for (int sub = 0; sub < subMeshes; sub++)
            {
                if (renderer == null || mesh == null) yield break;
                var material = sub < shared.Length ? shared[sub] : null;
                if (look != null && look.Keep != null && !look.Keep(material)) continue;
                var triangles = mesh.GetTriangles(sub);
                if (triangles.Length == 0) continue;
                if (material != null)
                    foreach (var step in AddMaterial(model, materials, material, look)) yield return step;
                if (renderer == null) yield break;
                int index;
                if (material == null || !materials.Index.TryGetValue(MaterialKey(material, look), out index)) index = -1;  // none, or destroyed meanwhile
                var scale = material != null ? material.mainTextureScale : Vector2.one;
                var offset = material != null ? material.mainTextureOffset : Vector2.zero;
                var data = Submesh(sub == 0 ? name : name + "#" + sub, index >= 0 ? materials.TextureOf[index] : -1, m, vertices, normals, uvs, scale, offset, triangles);
                data.Material = index;
                data.Node = node;
                model.Meshes.Add(data);
                yield return name;
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

        static string MaterialKey(Material material, Look look) => material.GetInstanceID() + (look != null ? "/" + look.Key : "");

        // One material per MSC material, with a texture made of the material's main texture with its
        // color tint and metal baked in, so two materials sharing a texture (an atlas) may still look
        // different. Materials that bake to the same texture (copies of one material, as the van's
        // windows have) share it. Steps: each texture read, the bake's bands; materials.Index has it after.
        IEnumerable AddMaterial(ModelData model, Materials materials, Material material, Look look)
        {
            string key = MaterialKey(material, look);
            if (materials.Index.ContainsKey(key)) yield break;

            var mainTexture = look != null && look.Diffuse != null ? look.Diffuse(material) : material.mainTexture;
            var metalTexture = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;
            ModelTextureData main = null, metalMap = null;
            if (mainTexture != null)
            {
                main = ReadTexture(materials.Pixels, mainTexture);
                yield return "texture " + mainTexture.name;
            }
            if (metalTexture != null)
            {
                metalMap = ReadTexture(materials.Pixels, metalTexture);
                yield return "texture " + metalTexture.name;
            }
            if (material == null) yield break;  // destroyed meanwhile
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
            string materialName = material.name;
            int texture;
            ModelTextureData baked = null;
            if (materials.Baked.TryGetValue(bakeKey, out texture)) baked = model.Textures[texture];
            else
            {
                int rows = Math.Max(1, BakePixelsPerStep / Math.Max(1, main != null ? main.Width : 1));
                foreach (var step in ModelTextureData.BakeSteps(main, tint, bakeMetal, metallic, rows))
                {
                    if (step != null) baked = step;
                    else yield return "baking " + materialName;
                }
                texture = model.Textures.Count;
                model.Textures.Add(baked);
                materials.Baked[bakeKey] = texture;
            }

            int index = model.Materials.Count;
            model.Materials.Add(data);
            materials.TextureOf.Add(texture);
            materials.Index[key] = index;
            log($"model: material {materialName} shader \"{unity.Shader}\" queue {unity.RenderQueue} flags {data.Flags} cutoff {data.AlphaCutoff:0.##} " +
                $"tint ({tint[0]:0.##},{tint[1]:0.##},{tint[2]:0.##},{tint[3]:0.##}) metal {unity.Metallic:0.##} gloss {data.Glossiness:0.##} " +
                $"specular {data.Specular[0]:0.##} -> average {Average(baked.Rgba)}");
            yield return "material " + materialName;
        }

        // A texture as read: from this model's, then the ones kept from earlier models, else read now.
        ModelTextureData ReadTexture(Dictionary<int, ModelTextureData> pixels, Texture texture)
        {
            int id = texture.GetInstanceID();
            ModelTextureData data;
            if (pixels.TryGetValue(id, out data)) return data;
            if (!textures.TryGet(id, out data))
            {
                bool direct;
                data = Pixels(texture, log, out direct);
                // Only what can't change: MSC may draw into a RenderTexture or write a readable texture.
                if (!direct && !(texture is RenderTexture)) textures.Put(id, data, data.Rgba.LongLength);
            }
            pixels[id] = data;
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

        // A particle texture as a one-square particle model (ParticleLink).
        public static ModelData Particle(string modelName, Texture texture, int columns, int rows, bool darken, Action<string> log)
        {
            if (texture == null) return null;
            var pixels = Pixels(texture, log);
            int cleared = ParticleLook.ClearHaze(pixels.Rgba);
            ParticleLook.FadeFrameEdges(pixels.Rgba, pixels.Width, pixels.Height, columns, rows);
            log($"model: particle texture {texture.name}: {cleared} of {pixels.Width * pixels.Height} pixels had only haze (alpha <= {ParticleLook.HazeFloor})");
            if (darken) ParticleLook.Blacken(pixels.Rgba);
            return ModelData.Particle(modelName, pixels);
        }

        // Straight from the texture when it is readable, else drawn into a RenderTexture and read back.
        static ModelTextureData Pixels(Texture texture, Action<string> log)
        {
            bool direct;
            return Pixels(texture, log, out direct);
        }

        // direct: read from its CPU copy (a readable texture), not drawn and read back.
        static ModelTextureData Pixels(Texture texture, Action<string> log, out bool direct)
        {
            var data = Direct(texture as Texture2D);
            direct = data != null;
            if (data == null) data = Blitted(texture);
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
            return TopRowFirst(texture.width, texture.height, pixels);
        }

        // A texture without a CPU copy reads back as the debug heap's fill bytes.
        static bool Uninitialized(Color32[] pixels)
        {
            foreach (var p in pixels)
                if (p.r != 0xCD || p.g != 0xCD || p.b != 0xCD || p.a != 0xCD) return false;
            return true;
        }

        // Unity's pixels (bottom row first, Color32 is r, g, b, a in memory) as the wire's, a row at a time.
        static unsafe ModelTextureData TopRowFirst(int width, int height, Color32[] pixels)
        {
            var rgba = new byte[width * height * 4];
            fixed (Color32* from = pixels)
            fixed (byte* to = rgba)
            {
                uint* src = (uint*)from, dst = (uint*)to;
                for (int y = 0; y < height; y++)
                {
                    uint* s = src + y * width, d = dst + (height - 1 - y) * width;
                    for (int x = 0; x < width; x++) d[x] = s[x];
                }
            }
            return new ModelTextureData { Width = width, Height = height, Rgba = rgba };
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
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);  // into its CPU copy: no Apply, nothing goes back to the GPU
                return TopRowFirst(w, h, copy.GetPixels32());
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
