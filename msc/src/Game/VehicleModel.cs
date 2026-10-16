using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // A vehicle's model for Skyrim, read from MSC's running vehicle (VanModel has the rules, for every
    // vehicle), and its pose every frame. Every MSC object of it with a mesh, switched off or not, is
    // one node straight under the model's root, its meshes in the node's frame (scale baked in);
    // VehicleParts then carries each node's pose relative to the vehicle and whether MSC draws it.
    // The player and items riding along are left out.
    public sealed unsafe class VehicleModel
    {
        Transform[] nodes = new Transform[0];  // ModelNode order
        Renderer[] renderers = new Renderer[0];

        // ModelChunk.model of the model these nodes are of, 0 until it was asked for.
        public uint Id;

        public ModelData Read(string name, Transform car, Transform player, ModelSender sender, Action<string> log)
        {
            var model = new ModelData { Name = name };
            var plain = new ModelSender.Look { Key = "van", Keep = KeepMaterial, Adjust = Blend };
            var lit = new ModelSender.Look { Key = "lit", Keep = KeepMaterial, Diffuse = GlowTexture, Adjust = (m, data) => VanModel.Lit(Blend(m, data)) };
            var materials = new ModelSender.Materials();
            var found = new List<Transform>();
            var shown = new List<Renderer>();
            int switchedOff = 0, left = 0, lights = 0;
            foreach (var renderer in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                var t = renderer.transform;
                var path = ModelSender.PathBelow(car, t);
                if ((player != null && t.IsChildOf(player)) || path.Contains("(itemx)") || VanModel.Skipped(path)) { left++; continue; }
                if (found.Count == Wire.VehiclePartCapacity)
                {
                    log($"vehicle model: more than {Wire.VehiclePartCapacity} objects, {path} left out");
                    left++;
                    continue;
                }
                var m = ModelSender.RelativeTo(car, t);
                var pose = Pose(car, t, m);
                int before = model.Meshes.Count;
                bool light = VanModel.IsLight(path);
                sender.AddRenderer(model, materials, renderer, pose.inverse * m, path.Replace('/', '|'), found.Count, light ? lit : plain);
                if (model.Meshes.Count == before) continue;
                var p = Position(pose);
                var q = Rotation(car, t);
                model.Nodes.Add(new ModelNodeData { Name = path, Translation = new[] { p.x, p.y, p.z }, Rotation = new[] { q.x, q.y, q.z, q.w } });
                found.Add(t);
                shown.Add(renderer);
                if (!Visible(renderer)) switchedOff++;
                if (light) lights++;
            }
            nodes = found.ToArray();
            renderers = shown.ToArray();
            log($"vehicle model: {name} has {nodes.Length} objects ({switchedOff} switched off, {lights} lights), {left} left out, {model.Meshes.Count} meshes");
            return model;
        }

        // Every node's pose as MSC has it right now, and whether MSC draws it.
        public void Fill(Transform car, ref VehicleParts s)
        {
            s.model = Id;
            s.count = (uint)nodes.Length;
            for (int i = 0; i < nodes.Length; i++)
            {
                var t = nodes[i];
                if (t == null) continue;  // destroyed: stays hidden
                var p = Position(Pose(car, t, ModelSender.RelativeTo(car, t)));
                var q = Rotation(car, t);
                var part = new VehiclePart { flags = Visible(renderers[i]) ? Wire.PartVisible : 0u };
                part.translation[0] = p.x;
                part.translation[1] = p.y;
                part.translation[2] = p.z;
                part.rotation[0] = q.x;
                part.rotation[1] = q.y;
                part.rotation[2] = q.z;
                part.rotation[3] = q.w;
                s.Set(i, part);
            }
        }

        static bool Visible(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy;

        // t's frame in the vehicle's (Unity space, no scale): its pivot from the local transforms
        // (RelativeTo, precise far from Unity's origin), its rotation relative to the vehicle's.
        static Matrix4x4 Pose(Transform car, Transform t, Matrix4x4 relative) =>
            Matrix4x4.TRS(relative.GetColumn(3), Quaternion.Inverse(car.rotation) * t.rotation, Vector3.one);

        static Vec3 Position(Matrix4x4 pose)
        {
            var c = pose.GetColumn(3);
            return Coords.UnityToSkyrim(new Vec3(c.x, c.y, c.z));
        }

        static Quat Rotation(Transform car, Transform t)
        {
            var r = Quaternion.Inverse(car.rotation) * t.rotation;
            return Coords.UnityToSkyrim(new Quat(r.x, r.y, r.z, r.w));
        }

        static bool KeepMaterial(Material m) => m != null && !VanModel.SkipMaterial(m.name);

        static ModelMaterialData Blend(Material m, ModelMaterialData data)
        {
            if (VanModel.BlendMaterial(m.name)) data.Flags = (data.Flags & ~Wire.MaterialCutout) | Wire.MaterialBlend;
            return data;
        }

        // What a light glows with: its "_lit" texture when it has one, else its diffuse (the light
        // textures are drawn switched on).
        static Texture GlowTexture(Material m)
        {
            foreach (var name in new[] { "_EmissionMap", "_Illum", "_DetailAlbedoMap" })
            {
                var t = m.HasProperty(name) ? m.GetTexture(name) : null;
                if (t != null && t.name.Contains("_lit")) return t;
            }
            return m.mainTexture;
        }
    }
}
