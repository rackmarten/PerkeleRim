using System;
using System.Collections.Generic;
using System.Text;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's Teimo as one of MSC's own models for Skyrim (ModelSender, by name, like the death
    // overlay): the shopkeeper behind his counter, his skinned meshes baked in a standing pose (a
    // frame of one of his own animation clips) and his plain meshes (glasses, cap) as they are,
    // relative to his root with his feet at 0. Read from the running game, nothing shipped. Skyrim
    // hangs it on its Teimo at the stall in Riverwood (skse/src/teimo/Teimo.cpp).
    public static class TeimoModel
    {
        public const string Name = "PerkeleRim.Teimo";

        // The clip and the time in it his pose is baked from; null bakes him as he stands now. Set by
        // the spike's "teimo pose <clip> <seconds>".
        public static string PoseClip = "";
        public static float PoseTime;
        // The model's key for ModelSender: a new pose is a new model.
        public static string Key => Name + "/" + PoseClip + "/" + PoseTime;
        const float RetryEvery = 5f;  // seconds between looks while MSC has no Teimo to draw
        static float nextTry;
        static string failed;  // the last failure logged, so each is logged once

        // Every SkinnedMeshRenderer below an object named like Teimo, grouped by the highest such
        // object: one group per Teimo MSC has (the shop's, others it may keep elsewhere).
        static Dictionary<Transform, List<Renderer>> Groups()
        {
            var groups = new Dictionary<Transform, List<Renderer>>();
            foreach (var r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (r == null || r.gameObject == null) continue;
                Transform top = null;
                for (var t = r.transform; t != null; t = t.parent)
                    if (t.name.IndexOf("teimo", StringComparison.OrdinalIgnoreCase) >= 0) top = t;
                if (top == null) continue;
                List<Renderer> list;
                if (!groups.TryGetValue(top, out list)) groups[top] = list = new List<Renderer>();
                list.Add(r);
            }
            return groups;
        }

        // The shop's Teimo: the group under STORE (in his shop), else the biggest.
        public static Transform Find()
        {
            Transform best = null;
            int bestCount = 0;
            foreach (var g in Groups())
            {
                bool shop = Path(g.Key).StartsWith("STORE", StringComparison.OrdinalIgnoreCase);
                int score = g.Value.Count + (shop ? 1000 : 0);
                if (score > bestCount) { best = g.Key; bestCount = score; }
            }
            return best;
        }

        // ModelSender's build: null while MSC has no Teimo loaded.
        public static ModelData Build(ModelSender sender, Action<string> log)
        {
            // ModelSender asks again every frame until this returns a model: look only now and then.
            if (Time.realtimeSinceStartup < nextTry) return null;
            nextTry = Time.realtimeSinceStartup + RetryEvery;
            var root = Find();
            if (root == null) return Fail("no Teimo in MSC's scene yet", log);
            Pose(root, log);
            var model = new ModelData { Name = Name };
            var materials = new ModelSender.Materials();
            var baked = new List<Mesh>();
            try
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeSelf) continue;
                    var skinned = r as SkinnedMeshRenderer;
                    Mesh mesh;
                    Matrix4x4 m = ModelSender.RelativeTo(root, r.transform);
                    if (skinned != null)
                    {
                        if (skinned.sharedMesh == null) continue;
                        mesh = new Mesh();
                        skinned.BakeMesh(mesh);  // the renderer's own space, its scale applied
                        baked.Add(mesh);
                        var s = r.transform.lossyScale;
                        m = m * Matrix4x4.Scale(new Vector3(1f / s.x, 1f / s.y, 1f / s.z));
                    }
                    else if (r is MeshRenderer)
                    {
                        var filter = r.GetComponent<MeshFilter>();
                        mesh = filter != null ? filter.sharedMesh : null;
                    }
                    else continue;
                    sender.AddMesh(model, materials, r, mesh, m, ModelSender.PathBelow(root, r.transform), -1, null);
                }
            }
            finally
            {
                foreach (var mesh in baked) UnityEngine.Object.Destroy(mesh);
            }
            if (model.Meshes.Count == 0) return Fail("nothing to draw on " + Path(root), log);
            failed = null;
            float floor = Floor(model);
            log($"teimo: {Path(root)} baked ({PoseName()}): {model.Meshes.Count} meshes, {model.VertexCount} vertices, " +
                $"{model.Textures.Count} textures, feet {floor:0.0} units below his root, raised to 0");
            return model;
        }

        static ModelData Fail(string why, Action<string> log)
        {
            if (why != failed) log("teimo: " + why + $", looking again every {RetryEvery:0} s");
            failed = why;
            return null;
        }

        static string PoseName() => string.IsNullOrEmpty(PoseClip) ? "as he stands now" : $"{PoseClip} at {PoseTime:0.00} s";

        // Samples PoseClip on his Animation (legacy clips, as MSC's people have), so the bake doesn't
        // catch him mid-gesture or in his bind pose while his shop is out of sight.
        static void Pose(Transform root, Action<string> log)
        {
            if (string.IsNullOrEmpty(PoseClip)) return;
            foreach (var anim in root.GetComponentsInChildren<Animation>(true))
            {
                var state = anim[PoseClip];
                if (state == null) continue;
                var playing = new List<string>();
                foreach (AnimationState s in anim) if (anim.IsPlaying(s.name)) playing.Add(s.name);
                anim.Stop();
                state.enabled = true;
                state.weight = 1f;
                state.time = PoseTime;
                anim.Sample();
                state.enabled = false;
                foreach (var name in playing) anim.Play(name, PlayMode.StopSameLayer);  // he carries on in his shop
                return;
            }
            log($"teimo: no clip {PoseClip} on his Animation, baked as he stands");
        }

        // Moves every vertex up so his lowest one is at 0 (his feet on the ground he stands on in
        // Skyrim); the drop he had below his root.
        static float Floor(ModelData model)
        {
            float low = float.MaxValue;
            foreach (var mesh in model.Meshes)
                for (int i = 2; i < mesh.Positions.Length; i += 3) low = Mathf.Min(low, mesh.Positions[i]);
            foreach (var mesh in model.Meshes)
                for (int i = 2; i < mesh.Positions.Length; i += 3) mesh.Positions[i] -= low;
            return -low;
        }

        static string Path(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        // Spike "teimo": every Teimo MSC has, what he is made of, his animation clips and FSMs.
        public static void Dump(Action<string> log)
        {
            foreach (var g in Groups())
            {
                var root = g.Key;
                log($"teimo: {Path(root)} active {root.gameObject.activeInHierarchy}, {g.Value.Count} skinned renderers, " +
                    $"at {root.position} facing {root.forward} scale {root.lossyScale}");
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var parts = new StringBuilder();
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (c == null || c is Transform) continue;
                        parts.Append(' ').Append(c.GetType().Name);
                        var fsm = c as PlayMakerFSM;
                        if (fsm != null) parts.Append(":").Append(fsm.FsmName).Append("[").Append(fsm.ActiveStateName).Append("]");
                        var r = c as Renderer;
                        if (r != null) parts.Append(r.enabled ? "" : "(off)");
                    }
                    if (parts.Length == 0 && t.childCount > 0 && Depth(root, t) > 3) continue;  // bones
                    log($"teimo:   {ModelSender.PathBelow(root, t)}{(t.gameObject.activeSelf ? "" : " (inactive)")}{parts}");
                }
                foreach (var anim in root.GetComponentsInChildren<Animation>(true))
                {
                    var clips = new List<string>();
                    foreach (AnimationState s in anim) clips.Add($"{s.name} {s.length:0.0}s{(anim.IsPlaying(s.name) ? " playing" : "")}");
                    log($"teimo:   Animation on {ModelSender.PathBelow(root, anim.transform)}: {string.Join(", ", clips.ToArray())}");
                }
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                    log($"teimo:   Animator on {ModelSender.PathBelow(root, animator.transform)}: controller {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")}");
            }
            var shop = Find();
            log("teimo: the shop's Teimo is " + (shop != null ? Path(shop) : "not there"));
        }

        static int Depth(Transform root, Transform t)
        {
            int d = 0;
            for (; t != null && t != root; t = t.parent) d++;
            return d;
        }
    }
}
