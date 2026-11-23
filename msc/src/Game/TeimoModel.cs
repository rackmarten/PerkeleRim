using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's Teimo as one of MSC's own models for Skyrim (ModelSender, by name, like the death
    // overlay): the shopkeeper behind his counter, his skinned mesh baked in the pose he stands in
    // (or a frame of one of his own clips, PoseClip) and his plain meshes as they are,
    // relative to his root with his feet at 0. Read from the running game, nothing shipped. Skyrim
    // hangs it on its Teimo at the stall in Riverwood (skse/src/teimo/Teimo.cpp).
    public static class TeimoModel
    {
        public const string Name = "PerkeleRim.Teimo";

        // The clip and the time in it his pose is baked from; empty bakes him as he stands now. Set by
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

        // The shop's Teimo, STORE/TeimoInShop (MSC also has TeimoInBike, him riding home, and the
        // COPS' CopTeimo1..3): standing behind his counter.
        public const string ShopTeimo = "TeimoInShop";

        public static Transform Find()
        {
            foreach (var g in Groups())
                if (g.Key.name == ShopTeimo) return g.Key;
            return null;
        }

        // Active from t up to his root: the root itself may be off (his shop closed, him out on his
        // bicycle), and he's still read then.
        static bool ActiveBelow(Transform root, Transform t)
        {
            for (; t != null && t != root; t = t.parent)
                if (!t.gameObject.activeSelf) return false;
            return true;
        }

        // His bicycle stands next to him in the shop: left out.
        static bool Drawn(Transform root, Transform t)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name.IndexOf("bicycle", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return true;
        }

        // ModelSender's read, in steps (ModelSender.IdForRead): nothing while MSC has no Teimo loaded,
        // else what it is doing after each bit of work and the model last. Every mesh of him is baked
        // in the first step, so all of him is in one pose; the steps after only read them.
        public static IEnumerable Read(ModelSender sender, Action<string> log)
        {
            // ModelSender asks again every frame until this gives a model: look only now and then.
            if (Time.realtimeSinceStartup < nextTry) yield break;
            nextTry = Time.realtimeSinceStartup + RetryEvery;
            var root = Find();
            if (root == null)
            {
                Fail("no Teimo in MSC's scene yet", log);
                yield break;
            }
            Pose(root, log);
            string path = Path(root);
            var model = new ModelData { Name = Name };
            var materials = new ModelSender.Materials();
            var baked = new List<Mesh>();
            var parts = new List<Part>();
            try
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled || !ActiveBelow(root, r.transform) || !Drawn(root, r.transform)) continue;
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
                    parts.Add(new Part { Renderer = r, Mesh = mesh, M = m, Name = ModelSender.PathBelow(root, r.transform) });
                }
                yield return "baking Teimo";
                foreach (var part in parts)
                {
                    if (part.Renderer == null) continue;  // gone meanwhile
                    int before = model.Meshes.Count;
                    foreach (var step in sender.AddMesh(model, materials, part.Renderer, part.Mesh, part.M, part.Name, -1, null)) yield return step;
                    if (materials.Cut)
                    {
                        model.Meshes.RemoveRange(before, model.Meshes.Count - before);
                        materials.Cut = false;
                    }
                }
            }
            finally
            {
                foreach (var mesh in baked) UnityEngine.Object.Destroy(mesh);
            }
            if (model.Meshes.Count == 0)
            {
                Fail("nothing to draw on " + path, log);
                yield break;
            }
            failed = null;
            float floor = Floor(model);
            log($"teimo: {path} baked ({PoseName()}): {model.Meshes.Count} meshes, {model.VertexCount} vertices, " +
                $"{model.Textures.Count} textures, feet {floor:0.0} units below his root, raised to 0");
            yield return model;
        }

        // One mesh of him to read: baked or his own, where it sits on him.
        sealed class Part
        {
            public Renderer Renderer;
            public Mesh Mesh;
            public Matrix4x4 M;
            public string Name;
        }

        static void Fail(string why, Action<string> log)
        {
            if (why != failed) log("teimo: " + why + $", looking again every {RetryEvery:0} s");
            failed = why;
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
                // Sampled alone, then every state put back as it was: he carries on in his shop.
                var saved = new List<KeyValuePair<AnimationState, Vector3>>();  // enabled, time, weight
                foreach (AnimationState s in anim)
                {
                    saved.Add(new KeyValuePair<AnimationState, Vector3>(s, new Vector3(s.enabled ? 1f : 0f, s.time, s.weight)));
                    s.enabled = false;
                }
                state.enabled = true;
                state.weight = 1f;
                state.time = PoseTime;
                anim.Sample();
                foreach (var s in saved)
                {
                    s.Key.enabled = s.Value.x > 0f;
                    s.Key.time = s.Value.y;
                    s.Key.weight = s.Value.z;
                }
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
