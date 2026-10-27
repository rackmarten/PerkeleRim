using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Junk from Skyrim's inventory in MSC's hand (docs/PROTOCOL.md "Throwing junk", rules in
    // Link/JunkBody.cs). PocketLink takes one out like an empty bottle (a PocketChange of
    // PocketJunk); Skyrim answers with a JunkOut, and this makes MSC's body for it: a copy of the
    // empty beer bottle (picked up, carried and thrown just like one), its own looks hidden and its
    // collider the item's bounds. ItemLink sends it with ItemForm, so Skyrim draws the item's own
    // model on it; its hits count the item's mass (ActorLink). Once it lies still out of the hand
    // it is gone from MSC and Skyrim drops the real item there (JunkBack, JunkLanded); put away (3
    // again, Y) it goes back without landing. When MSC stops playing every body goes: Skyrim makes
    // all of them available again on its side.
    public unsafe class JunkLink
    {
        class Body
        {
            public GameObject Go;
            public Rigidbody Rigidbody;
            public uint Session, Seq, Form;
            public float Mass;
            public bool EverHeld;  // in the hand once: before that it is still on its way there
            public readonly JunkLanding Landing = new JunkLanding();
        }

        static readonly Dictionary<int, Body> bodies = new Dictionary<int, Body>();  // by GameObject instance id
        readonly Dictionary<uint, JunkOut> answers = new Dictionary<uint, JunkOut>();  // Skyrim's, by seq
        readonly HashSet<uint> abandoned = new HashSet<uint>();  // takes PocketLink stopped waiting for
        readonly List<int> landed = new List<int>();
        readonly Island island;
        readonly Action<string> log;

        public JunkLink(Island island, Action<string> log)
        {
            this.island = island;
            this.log = log;
        }

        // The Skyrim form drawn on t, 0 when it isn't junk (ItemLink).
        public static uint FormOf(Transform t)
        {
            Body b;
            return t != null && bodies.TryGetValue(t.gameObject.GetInstanceID(), out b) ? b.Form : 0u;
        }

        public static bool IsJunk(Rigidbody body) => body != null && bodies.ContainsKey(body.gameObject.GetInstanceID());

        // What a thrown body's hit counts (ImpactEvent.mass): junk its item's, anything else its own.
        public static float MassOf(Rigidbody body)
        {
            Body b;
            if (body == null) return 0f;
            return bodies.TryGetValue(body.gameObject.GetInstanceID(), out b) ? b.Mass : body.mass;
        }

        public void OnOut(byte[] payload, int size)
        {
            if (size < sizeof(JunkOut)) return;
            JunkOut o;
            fixed (byte* p = payload) o = *(JunkOut*)p;
            if (abandoned.Remove(o.seq)) return;  // too late: Abandon gave it back already
            answers[o.seq] = o;
        }

        // Skyrim's answer to the take with seq: false while it hasn't come; then made is the new body
        // (not in the hand yet), or null when Skyrim had no junk after all.
        public bool TryMake(uint session, uint seq, out GameObject made)
        {
            made = null;
            JunkOut o;
            if (!answers.TryGetValue(seq, out o)) return false;
            answers.Remove(seq);
            if (o.form == 0) { log("junk: Skyrim had none to give"); return true; }
            made = PocketLink.NewBottle();
            if (made == null) { log("junk: no empty bottle to copy, nothing to hold it"); SendBack(session, seq, false, null); return true; }
            made.name = Pocket.JunkName;
            foreach (var r in made.GetComponentsInChildren<Renderer>(true)) r.enabled = false;  // Skyrim draws the item
            foreach (var c in made.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
            Vec3 center, size;
            JunkBody.Box(new Vec3(o.boundsMin[0], o.boundsMin[1], o.boundsMin[2]), new Vec3(o.boundsMax[0], o.boundsMax[1], o.boundsMax[2]), out center, out size);
            var box = made.AddComponent<BoxCollider>();
            box.center = new Vector3(center.x, center.y, center.z);
            box.size = new Vector3(size.x, size.y, size.z);
            bodies[made.GetInstanceID()] = new Body { Go = made, Rigidbody = made.GetComponent<Rigidbody>(), Session = session, Seq = seq, Form = o.form, Mass = o.mass };
            log($"junk: form {o.form:X8} out, a {size.x:0.00} x {size.y:0.00} x {size.z:0.00} m body, hits as {o.mass:0.##} kg");
            return true;
        }

        // PocketLink stopped waiting for the answer to a take: whatever Skyrim set aside for it is the
        // inventory's again (the JunkBack follows the take on the ring, so Skyrim has seen the take).
        public void Abandon(uint session, uint seq)
        {
            if (!answers.Remove(seq)) abandoned.Add(seq);
            SendBack(session, seq, false, null);
        }

        // A body put away (3 again, Y, a take that never reached the hand): back into Skyrim's
        // inventory, gone from MSC.
        public void Back(GameObject go)
        {
            Body b;
            if (go == null || !bodies.TryGetValue(go.GetInstanceID(), out b)) return;
            bodies.Remove(go.GetInstanceID());
            SendBack(b.Session, b.Seq, false, null);
            UnityEngine.Object.Destroy(go);
            log($"junk: form {b.Form:X8} put back");
        }

        Link link;  // the one Update had, for Back and TryMake

        void SendBack(uint session, uint seq, bool landedThere, Transform at)
        {
            var back = new JunkBack { session = session, seq = seq, flags = landedThere ? Wire.JunkLanded : 0u };
            if (at != null)
            {
                var p = island.IslandToSkyrim(new Vec3(at.position.x, at.position.y, at.position.z));
                var r = at.rotation;
                var q = Coords.UnityToSkyrim(new Quat(r.x, r.y, r.z, r.w));
                back.position[0] = p.x; back.position[1] = p.y; back.position[2] = p.z;
                back.rotation[0] = q.x; back.rotation[1] = q.y; back.rotation[2] = q.z; back.rotation[3] = q.w;
            }
            var payload = new byte[sizeof(JunkBack)];
            fixed (byte* p = payload) *(JunkBack*)p = back;
            if (link == null || !link.Send(MessageType.JunkBack, payload, payload.Length))
                log("junk: the link is full, Skyrim gets it back when MSC lets go of the player");
        }

        // Once a frame, before PocketLink. playing: MSC has the player on the current island.
        public void Update(Link link, bool playing, Transform player)
        {
            this.link = link;
            if (!playing || player == null || !link.IsUp)
            {
                // Skyrim releases them itself at the handback or with the link down; told anyway, for
                // an island rebuilt under a player MSC still has.
                if (bodies.Count > 0) log($"junk: {bodies.Count} bodies gone, back to Skyrim's inventory");
                foreach (var b in bodies.Values)
                {
                    if (link.IsUp) SendBack(b.Session, b.Seq, false, null);
                    if (b.Go != null) UnityEngine.Object.Destroy(b.Go);
                }
                bodies.Clear();
                if (!link.IsUp) { answers.Clear(); abandoned.Clear(); }
                return;
            }
            float now = Time.realtimeSinceStartup;
            landed.Clear();
            foreach (var pair in bodies)
            {
                var b = pair.Value;
                if (b.Go == null)
                {
                    landed.Add(pair.Key);
                    SendBack(b.Session, b.Seq, false, null);
                    log($"junk: form {b.Form:X8}'s body is gone, back to the inventory");
                    continue;
                }
                bool inHand = b.Go.transform.IsChildOf(player);
                if (inHand) b.EverHeld = true;
                if (!inHand && JunkBody.Lost(player.position.y - b.Go.transform.position.y))
                {
                    SendBack(b.Session, b.Seq, false, null);
                    log($"junk: form {b.Form:X8} fell off the island, back to the inventory");
                    UnityEngine.Object.Destroy(b.Go);
                    landed.Add(pair.Key);
                    continue;
                }
                float speed = b.Rigidbody != null ? b.Rigidbody.velocity.magnitude : 0f;
                if (!b.Landing.Update(inHand || !b.EverHeld, speed, now)) continue;
                SendBack(b.Session, b.Seq, true, b.Go.transform);
                log($"junk: form {b.Form:X8} landed");
                UnityEngine.Object.Destroy(b.Go);
                landed.Add(pair.Key);
            }
            foreach (var id in landed) bodies.Remove(id);
        }
    }
}
