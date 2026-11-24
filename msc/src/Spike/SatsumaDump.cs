using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // "satsuma": writes <MSC>/PerkeleRim_satsuma.txt, what the Satsuma and its parts are made of in the
    // running game: the car's root, the loose physics objects (name, tag, layer, mass, size, FSMs, where,
    // how far from the car), the car's own hierarchy, MSC's part database (Database/*: each part's Data
    // FSM variables) and the scene's roots. For working out the garage's slot map; reads only.
    public static class SatsumaDump
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            Transform car = null;
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType(typeof(Transform)))
                if (t.parent == null && t.name.StartsWith("SATSUMA", StringComparison.Ordinal)) { car = t; break; }
            var player = GameObject.Find("PLAYER");
            sb.AppendLine($"# car {(car != null ? car.name + " at " + car.position : "none")}, player {(player != null ? player.transform.position.ToString() : "none")}");
            if (car != null)
            {
                var body = car.GetComponent<Rigidbody>();
                sb.AppendLine($"car body: {(body != null ? $"{body.mass} kg kinematic {body.isKinematic} com {body.centerOfMass}" : "none")} bounds {Bounds(car)}");
            }

            sb.AppendLine();
            sb.AppendLine("## loose rigidbodies (active, not under the car or the player)");
            int n = 0;
            foreach (Rigidbody rb in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
            {
                var t = rb.transform;
                if (car != null && t.IsChildOf(car)) continue;
                if (player != null && t.IsChildOf(player.transform)) continue;
                if (!rb.gameObject.activeInHierarchy) continue;
                if (t.name.Contains("(itemx)") && !t.CompareTag("PART")) continue;
                n++;
                float d = car != null ? Vector3.Distance(car.position, t.position) : -1f;
                sb.AppendLine($"{FsmDump.PathOf(t)} | tag {t.tag} layer {LayerMask.LayerToName(t.gameObject.layer)} | {rb.mass:0.0} kg kin {rb.isKinematic} | size {Bounds(t)} | at {t.position} d {d:0.0} | fsms {Fsms(t.gameObject)}");
            }
            sb.AppendLine($"({n} loose)");

            if (car != null)
            {
                sb.AppendLine();
                sb.AppendLine("## the car's hierarchy (objects with an FSM, a rigidbody or a renderer, depth 6)");
                Walk(sb, car, 0);
            }

            sb.AppendLine();
            sb.AppendLine("## Database");
            var db = GameObject.Find("Database");
            if (db == null) sb.AppendLine("(no Database root)");
            else
                foreach (var t in db.GetComponentsInChildren<Transform>(true))
                    foreach (var f in t.GetComponents<PlayMakerFSM>())
                        sb.AppendLine($"{FsmDump.PathOf(t)} :: {f.FsmName} [{f.ActiveStateName}] {Vars(f.FsmVariables)}");

            sb.AppendLine();
            sb.AppendLine("## scene roots");
            foreach (Transform t in UnityEngine.Object.FindObjectsOfType(typeof(Transform)))
                if (t.parent == null) sb.AppendLine($"{t.name} at {t.position} active {t.gameObject.activeSelf}");

            File.WriteAllText(Path.Combine(SpikeLog.GameDir, "PerkeleRim_satsuma.txt"), sb.ToString());
            SpikeLog.Write($"cmd: satsuma: {n} loose rigidbodies, car {(car != null ? car.name : "none")}, written to PerkeleRim_satsuma.txt");
        }

        static void Walk(StringBuilder sb, Transform t, int depth)
        {
            if (depth > 6) return;
            var fsms = t.GetComponents<PlayMakerFSM>();
            var rb = t.GetComponent<Rigidbody>();
            var r = t.GetComponent<Renderer>();
            if (fsms.Length > 0 || rb != null || (r != null && depth <= 3))
                sb.AppendLine($"{new string(' ', depth * 2)}{t.name} | tag {t.tag} layer {LayerMask.LayerToName(t.gameObject.layer)} active {t.gameObject.activeSelf}" +
                              (rb != null ? $" | body {rb.mass:0.0} kg kin {rb.isKinematic}" : "") + (fsms.Length > 0 ? " | fsms " + Fsms(t.gameObject) : ""));
            foreach (Transform c in t) Walk(sb, c, depth + 1);
        }

        static string Fsms(GameObject go)
        {
            var list = new List<string>();
            foreach (var f in go.GetComponents<PlayMakerFSM>()) list.Add($"{f.FsmName}[{f.ActiveStateName}]");
            return string.Join(",", list.ToArray());
        }

        static string Vars(FsmVariables vars)
        {
            var list = new List<string>();
            try
            {
                foreach (var v in vars.BoolVariables) list.Add($"{v.Name}={v.Value}");
                foreach (var v in vars.IntVariables) list.Add($"{v.Name}={v.Value}");
                foreach (var v in vars.StringVariables) list.Add($"{v.Name}=\"{v.Value}\"");
                foreach (var v in vars.GameObjectVariables) list.Add($"{v.Name}->{(v.Value != null ? v.Value.name : "null")}");
            }
            catch (Exception e) { list.Add("(failed: " + e.Message + ")"); }
            return string.Join(" ", list.ToArray());
        }

        static string Bounds(Transform t)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? $"{b.size.x:0.00}x{b.size.y:0.00}x{b.size.z:0.00}" : "-";
        }
    }
}
