using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Writes an object's whole hierarchy, inactive objects too, to PerkeleRim_tree_<name>.txt in the game
    // folder (spike command "tree <name>"): per transform its name, whether it is active, its tag and
    // layer, its own mesh, rigidbody, colliders and FSMs. "tree roots" lists every root object with a
    // rigidbody instead (loose parts, items, vehicles), with where it is.
    public static class TreeDump
    {
        public static void Run(string name)
        {
            if (name.Equals("roots", StringComparison.OrdinalIgnoreCase))
            {
                Roots();
                return;
            }
            Transform root = null;
            foreach (Transform t in Resources.FindObjectsOfTypeAll(typeof(Transform)))
                if (t.parent == null && t.name == name && t.gameObject.hideFlags == HideFlags.None)
                {
                    root = t;
                    if (t.gameObject.activeInHierarchy) break;
                }
            if (root == null)
            {
                SpikeLog.Write("tree: no root object named " + name);
                return;
            }
            var sb = new StringBuilder();
            int count = 0;
            Write(sb, root, 0, ref count);
            var file = Path.Combine(SpikeLog.GameDir, "PerkeleRim_tree_" + Safe(name) + ".txt");
            File.WriteAllText(file, sb.ToString());
            SpikeLog.Write($"tree: {count} objects below {name} written to {file}");
        }

        static void Write(StringBuilder sb, Transform t, int depth, ref int count)
        {
            count++;
            var go = t.gameObject;
            sb.Append(' ', depth * 2).Append(t.name);
            if (!go.activeSelf) sb.Append(" [off]");
            if (go.tag != "Untagged") sb.Append(" tag=").Append(go.tag);
            sb.Append(" layer=").Append(LayerMask.LayerToName(go.layer));
            var filter = go.GetComponent<MeshFilter>();
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                sb.Append(" mesh=").Append(filter != null && filter.sharedMesh != null ? filter.sharedMesh.name + "/" + filter.sharedMesh.vertexCount : "none")
                    .Append(renderer.enabled ? "" : "(disabled)");
            if (go.GetComponent<SkinnedMeshRenderer>() != null) sb.Append(" skinned");
            var body = go.GetComponent<Rigidbody>();
            if (body != null) sb.Append($" body={body.mass:0.#}kg{(body.isKinematic ? ",kinematic" : "")}");
            int colliders = go.GetComponents<Collider>().Length;
            if (colliders > 0) sb.Append(" colliders=").Append(colliders);
            foreach (var fsm in go.GetComponents<PlayMakerFSM>()) sb.Append(" fsm=").Append(fsm.FsmName).Append('[').Append(fsm.ActiveStateName).Append(']');
            foreach (var c in go.GetComponents<Component>())
                if (c is MonoBehaviour && !(c is PlayMakerFSM)) sb.Append(" mono=").Append(c.GetType().Name);
            var lp = t.localPosition;
            sb.Append($" at=({lp.x:0.###},{lp.y:0.###},{lp.z:0.###})");
            sb.AppendLine();
            foreach (Transform child in t) Write(sb, child, depth + 1, ref count);
        }

        static void Roots()
        {
            var sb = new StringBuilder();
            int count = 0;
            foreach (Rigidbody body in Resources.FindObjectsOfTypeAll(typeof(Rigidbody)))
            {
                var t = body.transform;
                if (t.parent != null || body.gameObject.hideFlags != HideFlags.None) continue;
                count++;
                var go = body.gameObject;
                var fsms = new StringBuilder();
                foreach (var fsm in go.GetComponents<PlayMakerFSM>()) fsms.Append(' ').Append(fsm.FsmName).Append('[').Append(fsm.ActiveStateName).Append(']');
                sb.AppendLine($"{t.name} active={go.activeInHierarchy} tag={go.tag} layer={LayerMask.LayerToName(go.layer)} {body.mass:0.#}kg{(body.isKinematic ? " kinematic" : "")} " +
                              $"children={t.childCount} at={t.position} fsms:{fsms}");
            }
            var file = Path.Combine(SpikeLog.GameDir, "PerkeleRim_tree_roots.txt");
            File.WriteAllText(file, sb.ToString());
            SpikeLog.Write($"tree: {count} root rigidbodies written to {file}");
        }

        static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
