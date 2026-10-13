using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's physics items near the player, for Skyrim to draw (ItemState, models through ModelSender).
    // MSC itself handles picking them up, carrying, dropping, throwing and using them; this only reports
    // where they are. An item is a Rigidbody object that is one of MSC's items (named "...(itemx)" or
    // kept under ITEMS) or any small loose physics object (a thrown bottle). Each direct child of an
    // item that has meshes is sent as an item of its own, posed on its own: a beer case's bottles are
    // its children, so drinking one takes just that bottle away and all bottles share one model.
    public unsafe class ItemLink
    {
        const float Radius = 15f;         // meters from the player's feet to pick an item up
        const float KeepRadius = 20f;     // and to let it go again
        const float ScanEvery = 0.5f;     // seconds between looking for new items
        const float HoldMax = 10f;        // seconds a carried item waits for its ground at most
        const float LooseMassMax = 30f;   // kg: heavier loose rigidbodies aren't items
        const float VehicleMassMin = 100f;  // kg: rigidbodies below a root this heavy are vehicle parts

        class Entry
        {
            public Transform Transform;
            public uint Id;
            public uint Model;
            public Renderer[] Renderers;  // the ones its model is made of
        }

        readonly Island island;
        readonly ModelSender models;
        readonly Action<string> log;
        readonly Func<Vector3, Vector3?> findGround;  // island ground under a point, null = none yet
        readonly List<Rigidbody> holding = new List<Rigidbody>();  // carried items waiting for their ground
        float holdUntil;
        readonly List<Transform> items = new List<Transform>();  // item roots
        readonly List<Entry> entries = new List<Entry>();         // items and their pieces, closest first
        float nextScan;
        int lastCount = -1;
        uint itemsGeneration;  // anchor the items' island positions are for, 0 = none yet
        Vec3 itemsAnchor;      // its Skyrim position

        public ItemLink(Island island, ModelSender models, Func<Vector3, Vector3?> findGround, Action<string> log)
        {
            this.island = island;
            this.models = models;
            this.log = log;
            this.findGround = findGround;
        }

        // Call every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(Link link, bool playing, Transform player, Vector3 feet)
        {
            var state = new ItemState { frame = (uint)Time.frameCount, generation = island.Generation };
            if (!playing || player == null)
            {
                link.WriteItems(ref state);  // items are kept, to come along to the next anchor
                return;
            }
            Release();
            if (itemsGeneration != island.Generation)
            {
                if (itemsGeneration != 0) Carry(player, feet);
                itemsGeneration = island.Generation;
                itemsAnchor = island.Anchor;
                nextScan = 0f;
            }
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + ScanEvery;
                Scan(player, feet);
            }
            int n = 0;
            foreach (var e in entries)
            {
                if (e.Transform == null) continue;  // destroyed since the scan (a bottle drunk)
                state.Set(n++, Pose(e, player));
            }
            state.count = (uint)n;
            link.WriteItems(ref state);
        }

        // Logs every item Skyrim gets (spike command "items").
        public void Dump()
        {
            log($"items: {entries.Count} entries from {items.Count} items");
            foreach (var e in entries)
                if (e.Transform != null)
                    log($"items: {e.Id:X8} {Path(e.Transform)} model {e.Model:X8} renderers {e.Renderers.Length} " +
                        $"layer {(e.Renderers.Length > 0 && e.Renderers[0] != null ? LayerMask.LayerToName(e.Renderers[0].gameObject.layer) : "-")}");
        }

        // The items near the player for a snapshot (SaveSync), but not the ones in the player's hands
        // (they come along with the player).
        public List<Snapshot.Body> Capture(Transform player)
        {
            var list = new List<Snapshot.Body>();
            foreach (var t in items)
            {
                if (t == null || (player != null && t.IsChildOf(player))) continue;
                var body = t.GetComponent<Rigidbody>();
                bool moving = body != null && !body.isKinematic;
                var v = moving ? body.velocity : Vector3.zero;
                var a = moving ? body.angularVelocity : Vector3.zero;
                list.Add(new Snapshot.Body
                {
                    Id = t.GetInstanceID(),
                    Name = t.name,
                    Position = island.IslandToSkyrim(new Vec3(t.position.x, t.position.y, t.position.z)),
                    Rotation = new Quat(t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w),
                    Velocity = new Vec3(v.x, v.y, v.z),
                    Angular = new Vec3(a.x, a.y, a.z),
                    Bottles = BeerGift.Bottles(t),
                });
            }
            return list;
        }

        // Puts items back where a snapshot had them, on the current island, by instance id (so only
        // within the MSC session that took it). Items in the player's hands now stay there; ones
        // without ground yet are held until it arrives. Beer cases get their bottle count back.
        public void Restore(List<Snapshot.Body> saved, Transform player)
        {
            var byId = new Dictionary<int, Snapshot.Body>();
            foreach (var b in saved) byId[b.Id] = b;
            int moved = 0, held = 0;
            foreach (Rigidbody body in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
            {
                var t = body.transform;
                Snapshot.Body b;
                if (!byId.TryGetValue(t.GetInstanceID(), out b)) continue;
                byId.Remove(b.Id);
                if (player != null && t.IsChildOf(player)) continue;
                var at = island.SkyrimToIsland(b.Position);
                t.position = new Vector3(at.x, at.y, at.z);
                t.rotation = new Quaternion(b.Rotation.x, b.Rotation.y, b.Rotation.z, b.Rotation.w);
                body.position = t.position;
                body.rotation = t.rotation;
                if (findGround(t.position).HasValue)
                {
                    if (!body.isKinematic)
                    {
                        body.velocity = new Vector3(b.Velocity.x, b.Velocity.y, b.Velocity.z);
                        body.angularVelocity = new Vector3(b.Angular.x, b.Angular.y, b.Angular.z);
                    }
                }
                else if (!body.isKinematic)
                {
                    body.isKinematic = true;
                    holding.Add(body);
                    held++;
                }
                if (b.Bottles >= 0) BeerGift.SetBottles(t, b.Bottles, log);
                moved++;
            }
            if (held > 0) holdUntil = Time.realtimeSinceStartup + HoldMax;
            nextScan = 0f;
            log($"items: {moved} of {saved.Count} saved items back, {held} held for their ground" +
                (byId.Count > 0 ? $", {byId.Count} gone (used up, or from another MSC session)" : ""));
        }

        // A new anchor moved the island under the player: move the items along so each stays where it
        // was in Skyrim's world. Only the ones that end up near the player, where the new island has
        // ground; the rest stay behind. Held items come along with the player anyway.
        void Carry(Transform player, Vector3 feet)
        {
            var d = Coords.SkyrimToUnity(new Vec3(itemsAnchor.x - island.Anchor.x, itemsAnchor.y - island.Anchor.y, itemsAnchor.z - island.Anchor.z));
            var delta = new Vector3(d.x, d.y, d.z);
            float keep = KeepRadius * KeepRadius;
            int moved = 0;
            foreach (var t in items)
            {
                if (t == null || t.IsChildOf(player) || (t.position + delta - feet).sqrMagnitude > keep) continue;
                t.position += delta;
                var body = t.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = t.position;
                    if (!body.isKinematic) { body.isKinematic = true; holding.Add(body); }  // until its ground is there
                }
                moved++;
            }
            items.Clear();
            entries.Clear();
            lastCount = -1;
            holdUntil = Time.realtimeSinceStartup + HoldMax;
            log($"items: {moved} items came along to the gen {island.Generation} anchor, {holding.Count} held for their ground");
        }

        // Lets carried items fall again once the island has ground under them (or after HoldMax).
        void Release()
        {
            if (holding.Count == 0) return;
            bool timeout = Time.realtimeSinceStartup >= holdUntil;
            holding.RemoveAll(body =>
            {
                if (body == null) return true;
                if (!timeout && !findGround(body.position).HasValue) return false;
                body.isKinematic = false;
                return true;
            });
            if (holding.Count == 0) log(timeout ? "items: ground still missing, let the carried items go" : "items: carried items are on their ground");
        }

        ItemPose Pose(Entry e, Transform player)
        {
            var t = e.Transform;
            var pose = new ItemPose { id = e.Id, model = e.Model };
            var p = island.IslandToSkyrim(new Vec3(t.position.x, t.position.y, t.position.z));
            pose.position[0] = p.x;
            pose.position[1] = p.y;
            pose.position[2] = p.z;
            var r = t.rotation;
            var q = Coords.UnityToSkyrim(new Quat(r.x, r.y, r.z, r.w));
            pose.rotation[0] = q.x;
            pose.rotation[1] = q.y;
            pose.rotation[2] = q.z;
            pose.rotation[3] = q.w;
            if (t.gameObject.activeInHierarchy && AnyEnabled(e.Renderers)) pose.flags |= Wire.ItemVisible;
            if (t.IsChildOf(player)) pose.flags |= Wire.ItemHeld;
            return pose;
        }

        static bool AnyEnabled(Renderer[] renderers)
        {
            foreach (var r in renderers)
                if (r != null && r.enabled) return true;
            return false;
        }

        void Scan(Transform player, Vector3 feet)
        {
            float keep = KeepRadius * KeepRadius;
            items.RemoveAll(t => t == null || (t.position - feet).sqrMagnitude > keep);
            var known = new HashSet<Transform>(items);
            float radius = Radius * Radius;
            foreach (Rigidbody body in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
            {
                var t = body.transform;
                if (known.Contains(t) || !body.gameObject.activeInHierarchy || (t.position - feet).sqrMagnitude > radius) continue;
                if (!IsItem(body, player)) continue;
                items.Add(t);
                known.Add(t);
            }
            items.Sort((a, b) => (a.position - feet).sqrMagnitude.CompareTo((b.position - feet).sqrMagnitude));

            entries.Clear();
            foreach (var item in items)
            {
                if (entries.Count >= Wire.ItemCapacity) break;
                var root = item;
                Add(item, t => t == root);
                foreach (Transform child in item)
                {
                    if (entries.Count >= Wire.ItemCapacity) break;
                    if (known.Contains(child) || child.GetComponentInChildren<MeshRenderer>() == null) continue;
                    var piece = child;
                    Add(child, t => t.IsChildOf(piece) && !InsideItem(t, piece, known));
                }
            }
            if (entries.Count != lastCount)
            {
                lastCount = entries.Count;
                log($"items: {entries.Count} entries from {items.Count} items near the player");
            }
        }

        void Add(Transform t, Predicate<Transform> include)
        {
            var renderers = new List<Renderer>();
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
                if (include(r.transform)) renderers.Add(r);
            if (renderers.Count == 0) return;  // a root drawn only by its pieces
            entries.Add(new Entry
            {
                Transform = t,
                Id = unchecked((uint)t.GetInstanceID()),
                Model = models.IdFor(t, include),
                Renderers = renderers.ToArray(),
            });
        }

        static bool IsItem(Rigidbody body, Transform player)
        {
            var t = body.transform;
            if (t == player || t.GetComponentInChildren<MeshRenderer>() == null) return false;
            if (t.name.Contains("(itemx)") || t.root.name == "ITEMS") return true;
            if (t.parent != null && t.parent.name == "ItemPivot") return true;  // in MSC's hand (an empty bottle out of the pocket never lay loose)
            if (t.IsChildOf(player) || body.isKinematic || body.mass > LooseMassMax) return false;
            var rootBody = t.root != t ? t.root.GetComponent<Rigidbody>() : null;
            return rootBody == null || rootBody.mass < VehicleMassMin;
        }

        // Whether t (below piece) belongs to another tracked item nested in the piece, drawn on its own.
        static bool InsideItem(Transform t, Transform piece, HashSet<Transform> known)
        {
            for (var p = t; p != null && p != piece; p = p.parent)
                if (known.Contains(p)) return true;
            return false;
        }

        internal static string Path(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
