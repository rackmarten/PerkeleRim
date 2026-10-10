using System;
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
    public sealed class HandLayerSender
    {
        const string HandCameraPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/HandCamera";
        const float StatWindow = 5f;

        public static bool Enabled = true;  // spike "handlayer on|off", to compare frame times
        public static bool SaveRequested;   // spike "handlayer save"
        public static bool Tint;            // spike "handlayer tint": a see-through red background, to check Skyrim draws the layer

        readonly Action<string> log;
        readonly bool direct;
        bool warned;

        const int DefaultLayer = 0;

        Camera handCamera;
        Camera camera;
        GameObject[] strays = new GameObject[0];
        int handLayer = -1;
        bool borrowed;
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

        // Every MSC frame (LateUpdate); a_width/a_height: Skyrim's screen (InputState).
        public void Update(Link link, bool playing, uint width, uint height)
        {
            var layer = new HandLayer();
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
            if (shared != null)
            {
                layer.frame = copies;
                layer.flags = Wire.HandLayerValid;
                layer.texture = shared.Handle;
                layer.width = shared.Width;
                layer.height = shared.Height;
                layer.format = shared.Format;
                layer.costMicros = (uint)(costTicks * 1000000L / Stopwatch.Frequency);
            }
            link.WriteHandLayer(ref layer);
            LogStats();
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
                FindStrays();
                log($"hand layer: camera added below {HandCameraPath}, {strays.Length} first-person renderers on layer Default");
            }
            if (target == null || target.width != (int)width || target.height != (int)height)
            {
                if (shared != null) { shared.Dispose(); shared = null; }
                if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
                target = new RenderTexture((int)width, (int)height, 24, RenderTextureFormat.ARGB32) { name = "PerkeleRim.HandLayer" };
                target.Create();
                log($"hand layer: render texture {width}x{height}");
            }
            // HandCamera's settings (fov, clip planes, culling mask) every frame, then our own target.
            camera.CopyFrom(handCamera);
            camera.depth = handCamera.depth - 1;
            camera.targetTexture = target;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Tint ? new Color(0.3f, 0f, 0f, 0.3f) : new Color(0f, 0f, 0f, 0f);
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
        long start;

        void OnPreCull() => start = Stopwatch.GetTimestamp();

        void OnPostRender()
        {
            try { Owner?.Copy(start); }
            catch (Exception e) { HandLayerSender.Enabled = false; ModConsoleLog("hand layer: copy FAILED, off: " + e); }
        }

        static void ModConsoleLog(string line) => MSCLoader.ModConsole.Print("PerkeleRim: " + line);
    }
}
