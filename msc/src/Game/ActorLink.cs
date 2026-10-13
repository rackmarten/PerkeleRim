using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Skyrim's actors near the player (ActorList) as capsules on the island, and MSC's punch.
    //  - One kinematic CapsuleCollider per listed actor, moved with Skyrim's pose (carried on by its
    //    velocity between lists), on a layer of its own that only collides with the player's: the
    //    player bumps into people, while vehicles, their wheels and items pass through for now.
    //  - MSC's Hit key (H) throws MSC's own punch; a moment later, when the fist is out, a fist-sized
    //    sweep from the camera finds the capsule it lands on and Skyrim gets a PunchEvent.
    public unsafe class ActorLink
    {
        const float CarryOnMax = 0.25f;  // seconds a pose is carried on by its velocity at most
        const float PunchDelay = 0.15f;  // seconds from the key to the fist being out
        const float PunchReach = 1.2f;   // meters from the camera
        const float FistRadius = 0.15f;

        readonly Action<string> log;
        readonly Dictionary<uint, CapsuleCollider> capsules = new Dictionary<uint, CapsuleCollider>();
        readonly List<uint> gone = new List<uint>();
        List<ActorBody> bodies = new List<ActorBody>();
        GameObject root;
        int layer = -1;  // the capsules' own layer, -1 = not picked yet
        uint listFrame;
        float listTime;
        float punchAt = -1f;  // when the pending punch's fist is out, -1 = none
        readonly Island island;

        public ActorLink(Island island, Action<string> log)
        {
            this.island = island;
            this.log = log;
        }

        // Call every LateUpdate. playing: MSC owns the player and it stands on the current island;
        // driving: it sits in a vehicle (no punching); view: MSC's rendering camera.
        public void Update(Link link, bool playing, Transform player, bool driving, Transform view)
        {
            ActorList list;
            if (!playing || player == null || !link.ReadActors(out list))
            {
                Clear();
                listFrame = 0;
                punchAt = -1f;
                return;
            }
            if (list.frame != listFrame)
            {
                listFrame = list.frame;
                listTime = Time.time;
                bodies = Actors.FromList(ref list, island);
            }
            if (layer < 0) PickLayer(player.gameObject.layer);
            Place();
            Punch(link, driving, view);
        }

        // An unused layer that collides with the player's layer only.
        void PickLayer(int playerLayer)
        {
            for (int i = 31; i >= 8 && layer < 0; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) layer = i;
            if (layer < 0)
            {
                layer = 0;
                log("actors: no free layer, capsules go on Default and collide with everything");
                return;
            }
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(layer, i, i != playerLayer);
            log($"actors: capsules on layer {layer}, colliding with the player's layer {playerLayer} ({LayerMask.LayerToName(playerLayer)}) only");
        }

        void Place()
        {
            float carry = Mathf.Min(Time.time - listTime, CarryOnMax);
            gone.Clear();
            foreach (var id in capsules.Keys) gone.Add(id);
            foreach (var b in bodies)
            {
                gone.Remove(b.Id);
                CapsuleCollider capsule;
                if (!capsules.TryGetValue(b.Id, out capsule)) capsule = capsules[b.Id] = Create(b.Id);
                capsule.radius = b.Radius;
                capsule.height = Mathf.Max(b.Height, b.Radius * 2f);
                var feet = new Vector3(b.Feet.x, b.Feet.y, b.Feet.z) + new Vector3(b.Velocity.x, b.Velocity.y, b.Velocity.z) * carry;
                capsule.transform.position = feet + Vector3.up * (capsule.height * 0.5f);
            }
            foreach (var id in gone)
            {
                UnityEngine.Object.Destroy(capsules[id].gameObject);
                capsules.Remove(id);
            }
        }

        CapsuleCollider Create(uint id)
        {
            if (root == null)
            {
                root = new GameObject("PerkeleRim.Actors");
                UnityEngine.Object.DontDestroyOnLoad(root);
            }
            var go = new GameObject($"PerkeleRim.Actor {id:X8}") { layer = layer };
            go.transform.parent = root.transform;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1;  // upright
            return capsule;
        }

        void Clear()
        {
            if (capsules.Count == 0) return;
            foreach (var c in capsules.Values) UnityEngine.Object.Destroy(c.gameObject);
            capsules.Clear();
            bodies.Clear();
            log("actors: cleared the capsules");
        }

        void Punch(Link link, bool driving, Transform view)
        {
            if (!driving && view != null && punchAt < 0f && cInput.GetKeyDown("Hit")) punchAt = Time.time + PunchDelay;
            if (punchAt < 0f || Time.time < punchAt) return;
            punchAt = -1f;
            if (driving || view == null) return;

            float carry = Mathf.Min(Time.time - listTime, CarryOnMax);
            var now = new List<ActorBody>(bodies.Count);
            foreach (var b in bodies)
            {
                var moved = b;
                moved.Feet = new Vec3(b.Feet.x + b.Velocity.x * carry, b.Feet.y + b.Velocity.y * carry, b.Feet.z + b.Velocity.z * carry);
                now.Add(moved);
            }
            var from = view.position;
            var way = view.forward;
            float distance;
            int hit = Actors.Punch(new Vec3(from.x, from.y, from.z), new Vec3(way.x, way.y, way.z), PunchReach, FistRadius, now, out distance);
            if (hit < 0)
            {
                log($"actors: punch hit nobody ({now.Count} actors near)");
                return;
            }

            var skyrimWay = Coords.UnityToSkyrim(new Vec3(way.x, way.y, way.z));
            float length = Mathf.Sqrt(skyrimWay.x * skyrimWay.x + skyrimWay.y * skyrimWay.y + skyrimWay.z * skyrimWay.z);
            var punch = new PunchEvent { actor = now[hit].Id };
            punch.direction[0] = skyrimWay.x / length;
            punch.direction[1] = skyrimWay.y / length;
            punch.direction[2] = skyrimWay.z / length;
            var payload = new byte[sizeof(PunchEvent)];
            fixed (byte* p = payload) *(PunchEvent*)p = punch;
            bool sent = link.Send(MessageType.PunchEvent, payload, payload.Length);
            log($"actors: punch landed on {now[hit].Id:X8} at {distance:0.00} m{(sent ? "" : ", but the ring is full")}");
        }
    }
}
