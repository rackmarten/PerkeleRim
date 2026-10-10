using System.IO;
using System.Text;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Writes <MSC>/PerkeleRim_hands.txt: every camera (depth, culling mask, clear flags, fov) and every
    // renderer below FPSCamera (path, layer, active/enabled, shader), to find what draws the hands.
    public static class HandsDump
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Layers");
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(n)) sb.AppendLine($"{i} {n}");
            }

            sb.AppendLine().AppendLine("# Cameras");
            foreach (Camera c in Resources.FindObjectsOfTypeAll(typeof(Camera)))
            {
                if (c == null || c.gameObject == null) continue;
                sb.AppendLine($"{FsmDump.PathOf(c.transform)} active={c.gameObject.activeInHierarchy} enabled={c.enabled} " +
                              $"depth={c.depth} mask=0x{c.cullingMask:X8} ({Layers(c.cullingMask)}) clear={c.clearFlags} " +
                              $"fov={c.fieldOfView:0.##} near={c.nearClipPlane:0.###} far={c.farClipPlane:0.#} " +
                              $"rect={c.rect} target={(c.targetTexture != null ? c.targetTexture.name : "-")} " +
                              $"path={c.actualRenderingPath} hdr={c.hdr}");
                foreach (var b in c.GetComponents<Behaviour>())
                    if (b != null && !(b is Camera)) sb.AppendLine($"    {b.GetType().Name} enabled={b.enabled}");
            }

            var fps = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera");
            sb.AppendLine().AppendLine("# Renderers below " + (fps != null ? FsmDump.PathOf(fps.transform) : "FPSCamera (not found)"));
            if (fps != null)
                foreach (var r in fps.GetComponentsInChildren<Renderer>(true))
                {
                    string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "-";
                    sb.AppendLine($"{FsmDump.PathOf(r.transform)} {r.GetType().Name} layer={r.gameObject.layer}:{LayerMask.LayerToName(r.gameObject.layer)} " +
                                  $"active={r.gameObject.activeInHierarchy} enabled={r.enabled} shader={shader}");
                }

            string file = Path.Combine(SpikeLog.GameDir, "PerkeleRim_hands.txt");
            File.WriteAllText(file, sb.ToString());
            SpikeLog.Write("hands: wrote " + file);
        }

        static string Layers(int mask)
        {
            if (mask == -1) return "all";
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) sb.Append(sb.Length > 0 ? "," : "").Append(LayerMask.LayerToName(i) is string n && n.Length > 0 ? n : i.ToString());
            return sb.ToString();
        }
    }
}
