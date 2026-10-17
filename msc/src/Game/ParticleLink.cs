using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's smoke for Skyrim to draw (ParticleState): the live particles of the legacy particle
    // emitters under the vehicles Skyrim draws (exhaust), the player's tire smoke and the player's
    // cigarette smoke, every frame, in Skyrim world space. Each particle texture goes to Skyrim once
    // as a one-square model (ModelSender, ModelData.Particle); Skyrim turns the squares to its camera.
    public sealed unsafe class ParticleLink
    {
        const float Radius = 80f;      // meters from the player's feet: a system further away isn't sent
        const float ScanEvery = 2f;    // seconds between looking for the systems again
        static readonly string[] PlayerPaths = { "Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Smoking/BreathSmoke" };
        static readonly string[] LoosePaths = { "Skidmarks/SkidSmoke" };  // the player's tire smoke, moved to each skid

        public static bool Enabled = true;  // spike "smoke on|off", to compare frame times
        public static bool Sheets = true;   // spike "smoke sheet on|off": off sends every texture as one picture

        sealed class Source
        {
            public ParticleEmitter Emitter;
            public Renderer Renderer;
            public string Path;
            public bool Vehicle;  // found under a vehicle (not one of the player's own paths)
            public bool Logged;
        }

        readonly Island island;
        readonly ModelSender models;
        readonly Action<string> log;
        readonly Func<IEnumerable<Transform>> roots;  // the vehicles whose systems are mirrored
        readonly List<Source> sources = new List<Source>();
        readonly List<Source> near = new List<Source>();
        float nextScan;
        bool shown;

        public ParticleLink(Island island, ModelSender models, Func<IEnumerable<Transform>> roots, Action<string> log)
        {
            this.island = island;
            this.models = models;
            this.roots = roots;
            this.log = log;
        }

        // Call every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(Link link, bool playing, Transform player, Vector3 feet)
        {
            var state = new ParticleState { frame = (uint)Time.frameCount, generation = island.Generation };
            if (playing && Enabled && player != null)
            {
                if (Time.realtimeSinceStartup >= nextScan)
                {
                    nextScan = Time.realtimeSinceStartup + ScanEvery;
                    Scan(player);
                }
                Fill(ref state, feet);
            }
            link.WriteParticles(ref state);
            bool any = state.count > 0;
            if (any != shown)
            {
                shown = any;
                log(any ? $"particles: {state.count} shown" : "particles: none shown");
            }
        }

        public void Dump()
        {
            var view = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            foreach (var s in sources)
            {
                log($"particles: {s.Path} active {(s.Emitter != null && s.Emitter.gameObject.activeInHierarchy)} " +
                    $"n {(s.Emitter != null ? s.Emitter.particleCount : 0)}");
                if (s.Emitter == null) continue;
                var ps = s.Emitter.particles;
                for (int i = 0; i < ps.Length && i < 12; i++)
                {
                    var p = ps[i];
                    var at = s.Emitter.useWorldSpace ? p.position : s.Emitter.transform.TransformPoint(p.position);
                    log($"particles:   {Vector3.Distance(at, view):0.00} m from the camera, size {p.size:0.00}, color {p.color}, " +
                        $"energy {p.energy:0.00}/{p.startEnergy:0.00}");
                }
            }
        }

        // Spike "smoke puff": a burst from every mirrored system that is active, to look at in Skyrim.
        public void Puff(int count)
        {
            foreach (var s in sources)
                if (s.Emitter != null && s.Emitter.gameObject.activeInHierarchy)
                {
                    s.Emitter.Emit(count);
                    log($"particles: puffed {count} from {s.Path}");
                }
        }

        void Scan(Transform player)
        {
            sources.RemoveAll(s => s.Emitter == null);
            // A seated player is a child of the vehicle: its own systems (rain, piss stream) aren't exhaust.
            sources.RemoveAll(s => s.Vehicle && s.Emitter.transform.IsChildOf(player));
            foreach (var root in roots())
                if (root != null)
                    foreach (var e in root.GetComponentsInChildren<ParticleEmitter>(true))
                        if (!e.transform.IsChildOf(player)) Add(e, true);
            foreach (var path in PlayerPaths)
            {
                var t = player.Find(path);
                if (t == null) t = FindFromRoot(player, path);
                if (t != null) Add(t.GetComponent<ParticleEmitter>());
            }
            foreach (var path in LoosePaths)
            {
                var go = GameObject.Find(path);
                if (go != null) Add(go.GetComponent<ParticleEmitter>());
            }
        }

        // The player transform PlayerLink has may sit below PLAYER; the paths start at PLAYER.
        static Transform FindFromRoot(Transform player, string path)
        {
            var root = player;
            while (root.parent != null) root = root.parent;
            return root.Find(path);
        }

        void Add(ParticleEmitter emitter, bool vehicle = false)
        {
            if (emitter == null) return;
            foreach (var s in sources) if (s.Emitter == emitter) return;
            var path = ItemLink.Path(emitter.transform);
            sources.Add(new Source { Emitter = emitter, Renderer = emitter.GetComponent<Renderer>(), Path = path, Vehicle = vehicle });
            log($"particles: mirroring {path}");
        }

        void Fill(ref ParticleState state, Vector3 feet)
        {
            near.Clear();
            foreach (var s in sources)
            {
                if (s.Emitter == null || s.Renderer == null || !s.Renderer.enabled || !s.Emitter.gameObject.activeInHierarchy) continue;
                if (s.Emitter.particleCount == 0) continue;
                if ((s.Emitter.transform.position - feet).sqrMagnitude > Radius * Radius) continue;
                near.Add(s);
            }
            near.Sort((a, b) => (a.Emitter.transform.position - feet).sqrMagnitude.CompareTo((b.Emitter.transform.position - feet).sqrMagnitude));

            int n = 0;
            foreach (var s in near)
            {
                var material = s.Renderer.sharedMaterial;
                var texture = material != null ? material.mainTexture : null;
                if (texture == null) continue;
                var tex = texture;
                const float d = ParticleLook.DefaultTint;
                var tint = material.HasProperty("_TintColor") ? material.GetColor("_TintColor") : new Color(d, d, d, d);
                var pr = s.Renderer as ParticleRenderer;
                int columns = pr != null && Sheets ? Mathf.Clamp(pr.uvAnimationXTile, 1, 255) : 1;
                int rows = pr != null && Sheets ? Mathf.Clamp(pr.uvAnimationYTile, 1, 255) : 1;
                float cycles = pr != null ? pr.uvAnimationCycles : 1f;
                bool dark = ParticleLook.IsDark(tint.r, tint.g, tint.b);
                // The frame edges are faded per sheet layout, so each layout (and dark or not) is its own model.
                uint model = models.IdForBuilt($"particle:{tex.name}:{columns}x{rows}{(dark ? ":dark" : "")}",
                    () => ModelSender.Particle($"Particle {tex.name} {columns}x{rows}{(dark ? " dark" : "")}", tex, columns, rows, dark, log));
                if (model == 0) continue;
                if (!s.Logged)
                {
                    s.Logged = true;
                    log($"particles: {s.Path} draws {tex.name} {tex.width}x{tex.height} shader {(material.shader != null ? material.shader.name : "-")} tint {tint}{(dark ? " dark" : "")} " +
                        (pr != null ? $"mode {pr.particleRenderMode} tiles {pr.uvAnimationXTile}x{pr.uvAnimationYTile} " : "") + $"world {s.Emitter.useWorldSpace}");
                }

                var particles = s.Emitter.particles;
                foreach (var p in particles)
                {
                    if (n >= Wire.ParticleCapacity) break;
                    var at = s.Emitter.useWorldSpace ? p.position : s.Emitter.transform.TransformPoint(p.position);
                    var pose = new ParticlePose { model = model, size = p.size * (float)Coords.UnitsPerMeter, rotation = p.rotation * Mathf.Deg2Rad };
                    var sk = island.IslandToSkyrim(new Vec3(at.x, at.y, at.z));
                    pose.position[0] = sk.x;
                    pose.position[1] = sk.y;
                    pose.position[2] = sk.z;
                    var c = p.color;
                    pose.color[0] = ParticleLook.Channel(tint.r, c.r);
                    pose.color[1] = ParticleLook.Channel(tint.g, c.g);
                    pose.color[2] = ParticleLook.Channel(tint.b, c.b);
                    pose.color[3] = ParticleLook.Channel(tint.a, c.a);
                    if (dark) pose.color[3] = ParticleLook.DarkenAlpha(pose.color[3], pose.color[0], pose.color[1], pose.color[2]);
                    if (pose.color[3] == 0) continue;
                    pose.sheet[0] = (byte)columns;
                    pose.sheet[1] = (byte)rows;
                    pose.sheet[2] = ParticleLook.Frame(p.energy, p.startEnergy, cycles, columns, rows);
                    state.Set(n++, pose);
                }
                if (n >= Wire.ParticleCapacity) break;
            }
            state.count = (uint)n;
        }
    }
}
