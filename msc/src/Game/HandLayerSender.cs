using System;
using System.Collections.Generic;
using System.Diagnostics;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's first-person layer for Skyrim (HandLayer slot). MSC draws its hands, drinks and tools in
    // hand with its own HandCamera (layer Player, over the world camera). A hidden copy of that
    // camera, its child, draws the same into a render texture the size of Skyrim's screen on a
    // transparent background, and right after it rendered the texture is copied into one Skyrim
    // opens on the GPU (SharedTexture). Only while the player is handed over, and only with MSC
    // started with -force-gfx-direct (scripts/launch-msc.ps1 does that).
    // While Skyrim shows MSC's options menu (F1, MscMenuLink) the same texture holds the menu
    // instead: a hidden copy of the menu's camera renders it on a transparent background
    // (kHandLayerMenu), and the hands are off.
    // The hidden render is lit neutrally (BeginRender/EndRender: fixed ambient and one light of
    // our own instead of MSC's), so the layer looks the same at any MSC hour; Skyrim tints it
    // with its own light at the player.
    public sealed class HandLayerSender
    {
        internal const string HandCameraPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/HandCamera";
        const float StatWindow = 5f;
        const float FarClip = 20f;         // HandCamera stops at 5 m, which cuts the piss stream
        const float LightsRefresh = 2f;    // seconds between scans for MSC's lights

        public static bool Enabled = true;  // spike "handlayer on|off", to compare frame times
        public static bool SaveRequested;   // spike "handlayer save"
        public static bool Tint;            // spike "handlayer tint": a see-through red background, to check Skyrim draws the layer
        public static float Ambient = 0.5f, Sun = 0.8f;  // spike "handlayer light <ambient> <sun>": the neutral lighting
        // Drawn as see-through holes in the layer (DrinkShow: MSC's bottle while Skyrim draws its own
        // there): they hide what's behind them, the fingers round the back, and show Skyrim's frame.
        public static Renderer[] Holes;

        readonly Action<string> log;
        readonly bool direct;
        bool warned;

        const int DefaultLayer = 0;

        Camera handCamera;
        Camera camera;
        Camera menuCamera;  // the copy of MSC's menu camera, below it
        Camera menuSource;  // MSC's menu camera while it has Skyrim's aspect
        GameObject[] strays = new GameObject[0];
        int handLayer = -1;
        bool borrowed;
        Light sun;  // ours, only on while the hidden camera renders
        Light[] lights = new Light[0];
        float lightsAt = -LightsRefresh;
        readonly List<Light> dimmed = new List<Light>();
        readonly List<int> dimmedMasks = new List<int>();
        AmbientState ambient;
        bool ambientLogged;
        Renderer[] strayRenderers = new Renderer[0];
        readonly List<Renderer> swapped = new List<Renderer>();
        readonly List<Material[]> swappedOriginals = new List<Material[]>();
        readonly Dictionary<Material, Material> alphaMaterials = new Dictionary<Material, Material>();
        Shader alphaShader;
        bool alphaShaderTried;
        Material holeMaterial;
        bool holeTried;
        RenderTexture target;
        SharedTexture shared;
        uint copies;
        long costTicks;  // last copy: OnPreCull to the end of OnPostRender

        float statSince;
        int statFrames;
        uint statCopies;
        long statCost;

        public HandLayerSender(Action<string> log)
        {
            this.log = log;
            direct = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-force-gfx-direct", StringComparison.OrdinalIgnoreCase));
        }

        // Every MSC frame (LateUpdate); a_width/a_height: Skyrim's screen (InputState); menu: MSC's
        // menu camera while Skyrim shows the menu (MscMenuLink.Camera), else null.
        public void Update(Link link, bool playing, uint width, uint height, Camera menu = null)
        {
            var layer = new HandLayer();
            if (menu != null && Enabled && width > 0 && height > 0 && direct)
            {
                if (camera != null) camera.enabled = false;
                Return();
                PrepareTarget(width, height);
                // MSC picks its buttons through this camera (Camera.main while its menu is up) in its
                // own window, 640 x 480; drawn at Skyrim's 16:9 the picks sat 0.75 as far from the
                // center as the cursor. With Skyrim's aspect they land where Skyrim shows the cursor.
                menu.aspect = (float)width / height;
                menuSource = menu;
                PrepareMenu(menu);
                menuCamera.enabled = true;
                Publish(ref layer, Wire.HandLayerValid | Wire.HandLayerMenu);
                link.WriteHandLayer(ref layer);
                return;
            }
            if (menuCamera != null) menuCamera.enabled = false;
            if (menuSource != null)
            {
                menuSource.ResetAspect();
                menuSource = null;
            }
            bool want = playing && Enabled && width > 0 && height > 0 && direct && FindHandCamera();
            if (playing && !direct && !warned)
            {
                warned = true;
                log("hand layer: off, MSC was not started with -force-gfx-direct");
            }
            if (!want)
            {
                if (camera != null) camera.enabled = false;
                Return();
                link.WriteHandLayer(ref layer);
                return;
            }

            Prepare(width, height);
            Borrow();
            camera.enabled = true;
            Publish(ref layer, Wire.HandLayerValid);
            link.WriteHandLayer(ref layer);
            LogStats();
        }

        void Publish(ref HandLayer layer, uint flags)
        {
            if (shared == null) return;
            layer.frame = copies;
            layer.flags = flags;
            layer.texture = shared.Handle;
            layer.width = shared.Width;
            layer.height = shared.Height;
            layer.format = shared.Format;
            layer.costMicros = (uint)(costTicks * 1000000L / Stopwatch.Frequency);
        }

        // The menu's camera (Systems/OptionsMenu/CAM: layer GUI, clears depth only over MSC's view)
        // copied every frame into our target, on transparent black. MSC's menu needs no lighting of ours.
        void PrepareMenu(Camera source)
        {
            if (menuCamera == null || menuCamera.transform.parent != source.transform)
            {
                if (menuCamera != null) UnityEngine.Object.Destroy(menuCamera.gameObject);
                var go = new GameObject("PerkeleRim.MenuLayer");
                go.transform.SetParent(source.transform, false);
                menuCamera = go.AddComponent<Camera>();
                menuCamera.enabled = false;
                go.AddComponent<HandLayerCamera>().Owner = this;
                go.GetComponent<HandLayerCamera>().Menu = true;
                log("hand layer: menu camera added below " + source.name);
            }
            menuCamera.CopyFrom(source);
            menuCamera.depth = source.depth - 1;
            menuCamera.targetTexture = target;
            menuCamera.clearFlags = CameraClearFlags.SolidColor;
            menuCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }

        bool FindHandCamera()
        {
            if (handCamera != null) return true;
            var go = GameObject.Find(HandCameraPath);
            handCamera = go != null ? go.GetComponent<Camera>() : null;
            return handCamera != null;
        }

        void Prepare(uint width, uint height)
        {
            if (camera == null)
            {
                var go = new GameObject("PerkeleRim.HandLayer");
                go.transform.SetParent(handCamera.transform, false);
                camera = go.AddComponent<Camera>();
                camera.enabled = false;
                go.AddComponent<HandLayerCamera>().Owner = this;
                var sunGo = new GameObject("PerkeleRim.HandLayerSun");
                sunGo.transform.SetParent(go.transform, false);
                sunGo.transform.localRotation = Quaternion.Euler(40f, -25f, 0f);  // from above, a little from the left
                sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.None;
                sun.enabled = false;
                FindStrays();
                log($"hand layer: camera added below {HandCameraPath}, {strays.Length} first-person renderers on layer Default");
            }
            PrepareTarget(width, height);
            // HandCamera's settings (fov, clip planes, culling mask) every frame, then our own target.
            camera.CopyFrom(handCamera);
            camera.depth = handCamera.depth - 1;
            camera.farClipPlane = Mathf.Max(handCamera.farClipPlane, FarClip);
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Tint ? new Color(0.3f, 0f, 0f, 0.3f) : new Color(0f, 0f, 0f, 0f);
        }

        void PrepareTarget(uint width, uint height)
        {
            if (target == null || target.width != (int)width || target.height != (int)height)
            {
                if (shared != null) { shared.Dispose(); shared = null; }
                if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
                target = new RenderTexture((int)width, (int)height, 24, RenderTextureFormat.ARGB32) { name = "PerkeleRim.HandLayer" };
                target.Create();
                log($"hand layer: render texture {width}x{height}");
            }
        }

        // A few first-person things below FPSCamera sit on layer Default, so MSC draws them with the
        // world camera, not HandCamera: the piss stream, sauna water, the digging bar, the tow rope.
        // They're on HandCamera's layer while the hand layer is on (Borrow/Return), so MSC's hidden
        // view draws them over its world. Swapping only around the hidden camera's render (OnPreCull)
        // doesn't work: Unity culls with the layer the object had when the frame started.
        void FindStrays()
        {
            var fps = handCamera.transform.parent != null ? handCamera.transform.parent.parent : null;  // FPSCamera/FPSCamera/HandCamera
            var found = new System.Collections.Generic.List<GameObject>();
            if (fps != null)
                foreach (var r in fps.GetComponentsInChildren<Renderer>(true))
                    if (r.gameObject.layer == DefaultLayer) found.Add(r.gameObject);
            strays = found.ToArray();
            handLayer = LayerMask.NameToLayer("Player");
            var renderers = new List<Renderer>();
            foreach (var go in strays)
            {
                var r = go.GetComponent<Renderer>();
                if (r == null) continue;
                renderers.Add(r);
                foreach (var m in r.sharedMaterials)
                    if (m != null) log($"hand layer: stray {go.name} shader {m.shader.name}");
            }
            strayRenderers = renderers.ToArray();
            if (handLayer >= 0) sun.cullingMask = 1 << handLayer;
        }

        // Right before the hidden camera renders (HandLayerCamera.OnPreCull); EndRender undoes it
        // all, so MSC's own cameras see their world as before.
        internal void BeginRender()
        {
            ambient = AmbientState.Save();
            if (!ambientLogged)
            {
                ambientLogged = true;
                log($"hand layer: MSC ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight} x{RenderSettings.ambientIntensity}");
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(Ambient, Ambient, Ambient);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;

            // MSC's own lights (its sun, lamps, headlights) off the hand layer, ours on.
            if (handLayer >= 0)
            {
                float now = Time.realtimeSinceStartup;
                if (now - lightsAt >= LightsRefresh)
                {
                    lightsAt = now;
                    lights = UnityEngine.Object.FindObjectsOfType<Light>();
                }
                int bit = 1 << handLayer;
                foreach (var l in lights)
                {
                    if (l == null || l == sun || !l.enabled || (l.cullingMask & bit) == 0) continue;
                    dimmed.Add(l);
                    dimmedMasks.Add(l.cullingMask);
                    l.cullingMask &= ~bit;
                }
                sun.color = Color.white;
                sun.intensity = Sun;
                sun.enabled = true;
            }

            SwapParticleMaterials();
            SwapHoles();
        }

        internal void EndRender()
        {
            for (int i = 0; i < swapped.Count; i++)
                if (swapped[i] != null) swapped[i].sharedMaterials = swappedOriginals[i];
            swapped.Clear();
            swappedOriginals.Clear();
            sun.enabled = false;
            for (int i = 0; i < dimmed.Count; i++)
                if (dimmed[i] != null) dimmed[i].cullingMask = dimmedMasks[i];
            dimmed.Clear();
            dimmedMasks.Clear();
            ambient.Restore();
        }

        // Unity's legacy particle shaders write ColorMask RGB, so the piss stream left the layer's
        // alpha at 0 and Skyrim showed nothing. For the hidden render only, the strays' alpha
        // blended particle materials become copies with a shader that also writes alpha.
        void SwapParticleMaterials()
        {
            foreach (var r in strayRenderers)
            {
                if (r == null || !r.enabled) continue;
                var originals = r.sharedMaterials;
                Material[] replaced = null;
                for (int i = 0; i < originals.Length; i++)
                {
                    var alpha = AlphaWriting(originals[i]);
                    if (alpha == null) continue;
                    if (replaced == null) replaced = (Material[])originals.Clone();
                    replaced[i] = alpha;
                }
                if (replaced == null) continue;
                swapped.Add(r);
                swappedOriginals.Add(originals);
                r.sharedMaterials = replaced;
            }
        }

        // Holes: all their materials become one that writes depth and transparent black, so whatever
        // is behind them is cut out (in front of them it's depth tested as usual).
        void SwapHoles()
        {
            var holes = Holes;
            if (holes == null || holes.Length == 0) return;
            if (!holeTried)
            {
                holeTried = true;
                var m = new Material(HoleSource) { name = "PerkeleRim.Hole" };
                holeMaterial = m.shader != null && m.shader.isSupported ? m : null;
                log(holeMaterial != null ? "hand layer: hole shader ready" : "hand layer: hole shader NOT supported, held bottles show MSC's");
            }
            if (holeMaterial == null) return;
            foreach (var r in holes)
            {
                if (r == null || !r.enabled) continue;
                var originals = r.sharedMaterials;
                var replaced = new Material[originals.Length];
                for (int i = 0; i < replaced.Length; i++) replaced[i] = holeMaterial;
                swapped.Add(r);
                swappedOriginals.Add(originals);
                r.sharedMaterials = replaced;
            }
        }

        // Transparent black with depth, drawn before the opaque things (the hand), both sides of its faces.
        const string HoleSource = @"Shader ""PerkeleRim/Hole"" {
SubShader {
  Tags { ""Queue""=""Geometry-1"" ""RenderType""=""Opaque"" }
  Pass { ZWrite On ZTest LEqual Cull Off Lighting Off Fog { Mode Off } Blend Off ColorMask RGBA Color (0,0,0,0) }
}
}";

        Material AlphaWriting(Material original)
        {
            if (original == null || original.shader == null || original.shader.name != "Particles/Alpha Blended") return null;
            Material alpha;
            if (alphaMaterials.TryGetValue(original, out alpha)) return alpha;
            if (!alphaShaderTried)
            {
                alphaShaderTried = true;
                var holder = new Material(AlphaBlendedSource);
                alphaShader = holder.shader != null && holder.shader.isSupported ? holder.shader : null;
                log(alphaShader != null ? "hand layer: alpha-writing particle shader ready" : "hand layer: alpha-writing particle shader NOT supported");
            }
            if (alphaShader != null)
            {
                alpha = new Material(alphaShader) { name = original.name + " (PerkeleRim alpha)" };
                alpha.CopyPropertiesFromMaterial(original);
            }
            alphaMaterials[original] = alpha;
            return alpha;
        }

        // Unity's fixed-function Particles/Alpha Blended, plus alpha: blended alpha accumulates
        // (One OneMinusSrcAlpha) so the stream covers what's behind it in Skyrim.
        const string AlphaBlendedSource = @"Shader ""PerkeleRim/Particles Alpha Blended"" {
Properties { _TintColor (""Tint Color"", Color) = (0.5,0.5,0.5,0.5) _MainTex (""Particle Texture"", 2D) = ""white"" {} }
Category {
  Tags { ""Queue""=""Transparent"" ""IgnoreProjector""=""True"" ""RenderType""=""Transparent"" }
  Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
  ColorMask RGBA Cull Off Lighting Off ZWrite Off Fog { Mode Off }
  BindChannels { Bind ""Color"", color Bind ""Vertex"", vertex Bind ""TexCoord"", texcoord }
  SubShader { Pass {
    SetTexture [_MainTex] { constantColor [_TintColor] combine constant * primary }
    SetTexture [_MainTex] { combine texture * previous DOUBLE }
  } }
}
}";

        struct AmbientState
        {
            UnityEngine.Rendering.AmbientMode mode;
            Color light;
            float intensity;
            bool fog;

            public static AmbientState Save() => new AmbientState
            {
                mode = RenderSettings.ambientMode,
                light = RenderSettings.ambientLight,
                intensity = RenderSettings.ambientIntensity,
                fog = RenderSettings.fog,
            };

            public void Restore()
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientLight = light;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.fog = fog;
            }
        }

        void Borrow()
        {
            if (borrowed || handLayer < 0) return;
            borrowed = true;
            foreach (var go in strays)
                if (go != null) go.layer = handLayer;
        }

        void Return()
        {
            if (!borrowed) return;
            borrowed = false;
            foreach (var go in strays)
                if (go != null) go.layer = DefaultLayer;
        }

        // Right after the hidden camera rendered (HandLayerCamera.OnPostRender).
        internal void Copy(long startTicks)
        {
            IntPtr source = target.GetNativeTexturePtr();
            if (source == IntPtr.Zero) return;
            if (shared == null || shared.Source != source)
            {
                if (shared != null) shared.Dispose();
                shared = SharedTexture.Create(source, log);
                if (shared == null) { Enabled = false; return; }  // logged; stays off
                log($"hand layer: shared texture {shared.Width}x{shared.Height} format {shared.Format} handle 0x{shared.Handle:X}");
            }
            shared.Update();
            if (SaveRequested) Save();
            copies++;
            costTicks = Stopwatch.GetTimestamp() - startTicks;
            statCost += costTicks;
            statCopies++;
        }

        // Spike "handlayer save": the layer as Skyrim gets it (alpha included) to PerkeleRim_handlayer.png.
        void Save()
        {
            SaveRequested = false;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.ARGB32, false);
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            RenderTexture.active = previous;
            string file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "PerkeleRim_handlayer.png");
            System.IO.File.WriteAllBytes(file, pixels.EncodeToPNG());
            UnityEngine.Object.Destroy(pixels);
            log("hand layer: saved " + file);
        }

        void LogStats()
        {
            statFrames++;
            float now = Time.realtimeSinceStartup;
            if (statSince == 0f) statSince = now;
            if (now - statSince < StatWindow) return;
            double frameMs = (now - statSince) * 1000.0 / statFrames;
            double costUs = statCopies > 0 ? statCost * 1000000.0 / Stopwatch.Frequency / statCopies : 0;
            log($"hand layer: {target.width}x{target.height} copies={statCopies} msc frame={frameMs:0.00} ms layer cpu={costUs:0} us");
            statSince = now;
            statFrames = 0;
            statCopies = 0;
            statCost = 0;
        }
    }

    // On the hidden camera: times it and hands the frame over once it has rendered.
    public class HandLayerCamera : MonoBehaviour
    {
        internal HandLayerSender Owner;
        internal bool Menu;  // MSC's options menu: no lighting of ours
        long start;

        void OnPreCull()
        {
            start = Stopwatch.GetTimestamp();
            if (Menu) return;
            try { Owner?.BeginRender(); }
            catch (Exception e) { HandLayerSender.Enabled = false; ModConsoleLog("hand layer: lighting FAILED, off: " + e); }
        }

        void OnPostRender()
        {
            if (!Menu)
                try { Owner?.EndRender(); }
            catch (Exception e) { HandLayerSender.Enabled = false; ModConsoleLog("hand layer: restore FAILED, off: " + e); }
            try { Owner?.Copy(start); }
            catch (Exception e) { HandLayerSender.Enabled = false; ModConsoleLog("hand layer: copy FAILED, off: " + e); }
        }

        static void ModConsoleLog(string line) => LinkHost.Log(line);
    }
}
