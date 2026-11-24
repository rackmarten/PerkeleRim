using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The Satsuma bay next to the garage (GarageBay, layout and slot map in Link/SatsumaBay):
    //  - the one-time move (BayMoveSatsuma): the Satsuma's body onto its stand, as MSC has it built
    //    (attached parts stay on it), and every loose Satsuma part at MSC's home onto its slot;
    //  - the garage board (BayPartsHome, BayVehiclesHome): every loose Satsuma part on the island
    //    or at MSC's home back onto its slot, every vehicle to its spot by the garage (the body too);
    //  - BayStand: a save that had the move, with an MSC that hasn't placed the body this session
    //    (restarted since): the body back on its stand;
    //  - BayCheck: counts the parts on and off their slots, for the test kit.
    // Waits for the island's ground at the stand (20 s at most), then does it all in one frame and
    // answers GarageDone. MSC's shops and junkyard keep their parts: only home and the island count.
    public unsafe class GarageMover
    {
        const float GroundWait = 20f;
        const float Clearance = 0.02f;   // meters between a part's bottom and the surface it's set on
        const float HomeRadius = 60f;    // meters around MSC's home (YARD/Building/Garage)
        const float IslandBelow = 300f;  // Unity y: above it is the island (at y 1000), below MSC's own map
        const float OnSlot = 0.35f;      // meters from its slot (flat) a part still counts as on it
        const float BodyDrop = 0.03f;    // meters above the stand the body is set down at

        readonly Island island;
        readonly Func<Vector3, Vector3?> findGround;
        readonly Action<string> log;
        readonly Func<VehicleLink> satsuma;
        readonly Func<VehicleLink[]> vehicles;
        readonly Func<Transform> player;
        readonly List<GarageMessages.Request> pending = new List<GarageMessages.Request>();
        float since;
        bool bodyPlaced;  // this MSC session has set the body down on its stand (or seen it placed)

        public GarageMover(Island island, Func<Vector3, Vector3?> findGround, Func<VehicleLink> satsuma, Func<VehicleLink[]> vehicles, Func<Transform> player, Action<string> log)
        {
            this.island = island;
            this.findGround = findGround;
            this.satsuma = satsuma;
            this.vehicles = vehicles;
            this.player = player;
            this.log = log;
        }

        public void OnMessage(byte[] payload, int size)
        {
            GarageMessages.Request r;
            if (!GarageMessages.DecodeBay(payload, size, out r))
            {
                log($"garage: bad GarageBay ({size} bytes)");
                return;
            }
            if (pending.Count == 0) since = Time.realtimeSinceStartup;
            pending.Add(r);
            log($"garage: asked for {What(r.What)} for gen {r.Generation}");
        }

        // Every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(Link link, bool playing)
        {
            if (pending.Count == 0 || !playing || link == null) return;
            var r = pending[0];
            if (r.Generation != island.Generation)
            {
                log($"garage: the island moved on to gen {island.Generation}, {What(r.What)} for gen {r.Generation} dropped");
                Answer(link, GarageMessages.Done(r, false, false, 0, 0, 0, 0));
                return;
            }
            float sx, sy, sz;
            SatsumaBay.ToWorld(SatsumaBay.BodyX, SatsumaBay.BodyY, SatsumaBay.StandTop, r.Position.x, r.Position.y, r.Position.z, r.Heading, out sx, out sy, out sz);
            if (!findGround(Island(sx, sy, sz)).HasValue)
            {
                if (Time.realtimeSinceStartup - since < GroundWait) return;
                log($"garage: no ground at the bay after {GroundWait} s, {What(r.What)} not done");
                Answer(link, GarageMessages.Done(r, false, false, 0, 0, 0, 0));
                return;
            }

            bool body = false;
            int slotted = 0, floor = 0, brought = 0, missed = 0;
            var car = satsuma();
            if ((r.What & (Wire.BayMoveSatsuma | Wire.BayVehiclesHome)) != 0 || ((r.What & Wire.BayStand) != 0 && !bodyPlaced))
                body = SetBodyDown(car, r);
            else body = car != null && car.Car != null && OnStand(car.Car, r);
            if ((r.What & (Wire.BayMoveSatsuma | Wire.BayPartsHome)) != 0)
                PutParts(r, (r.What & Wire.BayPartsHome) != 0, out slotted, out floor);
            if ((r.What & Wire.BayVehiclesHome) != 0)
                brought = BringVehicles(r);
            if ((r.What & Wire.BayCheck) != 0)
                Count(r, out slotted, out missed);
            log($"garage: {What(r.What)} done: body {(body ? "on its stand" : "not there")}, {slotted} parts on their slots, {floor} on the floor, {brought} vehicles home{((r.What & Wire.BayCheck) != 0 ? $", {missed} off their slots" : "")}");
            Answer(link, GarageMessages.Done(r, true, body, slotted, floor, brought, missed));
        }

        void Answer(Link link, GarageDone done)
        {
            pending.RemoveAt(0);
            since = Time.realtimeSinceStartup;
            var payload = new byte[sizeof(GarageDone)];
            fixed (byte* p = payload) *(GarageDone*)p = done;
            if (!link.Send(MessageType.GarageDone, payload, payload.Length)) log("garage: GarageDone didn't fit in the ring");
        }

        static string What(uint what)
        {
            var parts = new List<string>();
            if ((what & Wire.BayMoveSatsuma) != 0) parts.Add("the move");
            if ((what & Wire.BayPartsHome) != 0) parts.Add("parts home");
            if ((what & Wire.BayVehiclesHome) != 0) parts.Add("vehicles home");
            if ((what & Wire.BayStand) != 0) parts.Add("the stand");
            if ((what & Wire.BayCheck) != 0) parts.Add("a check");
            return parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "nothing";
        }

        Vector3 Island(float x, float y, float z)
        {
            var at = island.SkyrimToIsland(new Vec3(x, y, z));
            return new Vector3(at.x, at.y, at.z);
        }

        Vector3 BayPoint(GarageMessages.Request r, float bx, float by, float bz)
        {
            float x, y, z;
            SatsumaBay.ToWorld(bx, by, bz, r.Position.x, r.Position.y, r.Position.z, r.Heading, out x, out y, out z);
            return Island(x, y, z);
        }

        // Unity yaw of a direction in the bay frame (radians clockwise from the bay's +X).
        static Quaternion Yaw(GarageMessages.Request r, float along) => Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(r.Heading + along), 0f);

        // ---- The body ----

        bool SetBodyDown(VehicleLink link, GarageMessages.Request r)
        {
            if (link == null) return false;
            link.Find();
            var car = link.Car;
            if (car == null) { log("garage: no Satsuma in MSC"); return false; }
            var p = player();
            if (p != null && p.IsChildOf(car)) { log("garage: Kumpo sits in the Satsuma, left where it is"); return false; }
            var rotation = Yaw(r, 0f);
            var at = BayPoint(r, SatsumaBay.BodyX, SatsumaBay.BodyY, SatsumaBay.StandTop);
            // The stand's top under the body: the higher of its two crates.
            float top = float.MinValue;
            foreach (var x in SatsumaBay.StandX)
            {
                var g = findGround(BayPoint(r, SatsumaBay.BodyX + x, SatsumaBay.BodyY, SatsumaBay.StandTop));
                if (g.HasValue) top = Mathf.Max(top, g.Value.y);
            }
            if (top == float.MinValue) top = at.y;
            car.rotation = rotation;
            car.position = at;
            float bottom;
            if (Bottom(car, out bottom)) at.y += top + BodyDrop - bottom;
            link.SetDown(at, rotation);
            bodyPlaced = true;
            log($"garage: the Satsuma's body on its stand at ({at.x:0.00},{at.y:0.00},{at.z:0.00}), stand top {top:0.00}");
            return true;
        }

        bool OnStand(Transform car, GarageMessages.Request r)
        {
            var at = BayPoint(r, SatsumaBay.BodyX, SatsumaBay.BodyY, SatsumaBay.StandTop);
            var d = car.position - at;
            d.y = 0f;
            return d.magnitude < 0.5f;
        }

        // ---- The parts ----

        // MSC's home: its garage, else the yard.
        static Vector3? Home()
        {
            var garage = GameObject.Find("YARD/Building/Garage");
            if (garage != null) return garage.transform.position;
            var yard = GameObject.Find("YARD");
            return yard != null ? yard.transform.position : (Vector3?)null;
        }

        // The Satsuma's loose parts: MSC keeps them under CARPARTS (PartsCar, PartsMotor, PartsGT, ...)
        // tagged PART while off the car; at home (within HomeRadius of it, in MSC's own map) or, with
        // island, on the island too. Not the ones in Kumpo's hands, nor the parts' own triggers.
        List<Rigidbody> LooseParts(bool island)
        {
            var list = new List<Rigidbody>();
            var home = Home();
            var p = player();
            var car = satsuma() != null ? satsuma().Car : null;
            foreach (Rigidbody body in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
            {
                var t = body.transform;
                if (!body.gameObject.activeInHierarchy || !t.CompareTag("PART") || t.root.name != "CARPARTS") continue;
                if ((p != null && t.IsChildOf(p)) || (car != null && t.IsChildOf(car))) continue;
                bool onIsland = t.position.y > IslandBelow;
                bool atHome = !onIsland && home.HasValue && Flat(t.position - home.Value) < HomeRadius;
                if (atHome || (island && onIsland)) list.Add(body);
            }
            // Same parts, same order: by name, then by MSC's instance id.
            list.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.name, b.name);
                return byName != 0 ? byName : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });
            return list;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        List<SatsumaBay.Part> Describe(List<Rigidbody> bodies)
        {
            var parts = new List<SatsumaBay.Part>();
            foreach (var b in bodies)
            {
                var size = Size(b.transform);
                parts.Add(new SatsumaBay.Part { Name = b.name, Longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z)), Height = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) });
            }
            return parts;
        }

        void PutParts(GarageMessages.Request r, bool fromIsland, out int slotted, out int floor)
        {
            slotted = floor = 0;
            var bodies = LooseParts(fromIsland);
            var placed = SatsumaBay.Assign(Describe(bodies));
            for (int i = 0; i < bodies.Count; i++)
            {
                var t = bodies[i].transform;
                Vector3 spot;
                float along;
                if (placed[i].Slot >= 0)
                {
                    var s = SatsumaBay.Slots[placed[i].Slot];
                    spot = BayPoint(r, s.X, s.Y, s.Z);
                    along = s.Along;
                    if (SatsumaBay.SlotsFor(t.name).Contains(placed[i].Slot)) slotted++;  // its own (shelf or floor)
                    else floor++;
                }
                else
                {
                    float x, y, z;
                    SatsumaBay.PileSpot(placed[i].Pile, out x, out y, out z);
                    spot = BayPoint(r, x, y, z);
                    along = 0f;
                    floor++;
                }
                Place(bodies[i], spot, Yaw(r, along));
            }
            log($"garage: {bodies.Count} loose parts {(fromIsland ? "at home and on the island" : "at home")} put in the bay");
        }

        // Sets the part on the surface at spot (a board, the floor), its long side along the slot.
        void Place(Rigidbody body, Vector3 spot, Quaternion yaw)
        {
            var t = body.transform;
            t.position = spot;
            // Whichever of the two quarter turns lays it longer along the slot.
            var along = yaw * Vector3.forward;
            t.rotation = yaw;
            float a = Extent(t, along);
            t.rotation = yaw * Quaternion.Euler(0f, 90f, 0f);
            float b = Extent(t, along);
            if (a >= b) t.rotation = yaw;
            var ground = findGround(spot);
            float bottom;
            if (Bottom(t, out bottom) && ground.HasValue) t.position += Vector3.up * (ground.Value.y + Clearance - bottom);
            body.position = t.position;
            body.rotation = t.rotation;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        void Count(GarageMessages.Request r, out int on, out int off)
        {
            on = off = 0;
            var bodies = LooseParts(true);
            // Only the ones in the bay count.
            var inBay = new List<Rigidbody>();
            foreach (var b in bodies)
            {
                var local = ToBay(r, b.transform.position);
                if (Mathf.Abs(local.x) < 460f && Mathf.Abs(local.y) < 270f) inBay.Add(b);
            }
            var placed = SatsumaBay.Assign(Describe(inBay));
            for (int i = 0; i < inBay.Count; i++)
            {
                if (placed[i].Slot < 0 || !SatsumaBay.Listed(inBay[i].name)) continue;
                var s = SatsumaBay.Slots[placed[i].Slot];
                var spot = BayPoint(r, s.X, s.Y, s.Z);
                if (Flat(inBay[i].transform.position - spot) < OnSlot) on++;
                else off++;
            }
        }

        // An island point in the bay's frame (Skyrim units).
        Vector2 ToBay(GarageMessages.Request r, Vector3 unity)
        {
            var s = island.IslandToSkyrim(new Vec3(unity.x, unity.y, unity.z));
            float dx = s.x - r.Position.x, dy = s.y - r.Position.y;
            float sh = (float)Math.Sin(r.Heading), ch = (float)Math.Cos(r.Heading);
            return new Vector2(dx * sh + dy * ch, -dx * ch + dy * sh);
        }

        // ---- The vehicles ----

        // Where each goes, in the bay's frame (Skyrim units, heading from the bay's +X): the van onto
        // the garage's pit (the garage is 520 to the bay's right, its pit's top about 63 up), the rest
        // in a row on the open ground in front of both.
        static readonly string[] SpotNames = { "Hayosiko", "Jonnez", "Kekmet", "Gifu" };
        static readonly float[][] Spots =
        {
            new[] { 0f, -520f, 63f, 0f },
            new[] { 560f, -250f, 0f, 0f },
            new[] { 600f, 60f, 0f, 0f },
            new[] { 760f, -520f, 0f, 0f },
        };

        int BringVehicles(GarageMessages.Request r)
        {
            int n = 0;
            var p = player();
            foreach (var v in vehicles())
            {
                int i = Array.IndexOf(SpotNames, v.Name);
                if (i < 0 || v.Car == null || p == null || v.Carries(p)) continue;
                var s = Spots[i];
                float x, y, z;
                SatsumaBay.ToWorld(s[0], s[1], s[2], r.Position.x, r.Position.y, r.Position.z, r.Heading, out x, out y, out z);
                v.TowTo(p, new Vec3(x, y, z), r.Heading + s[3]);
                n++;
            }
            return n;
        }

        // ---- Shapes ----

        static Vector3 Size(Transform t)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? b.size : new Vector3(0.3f, 0.3f, 0.3f);
        }

        // How far the part reaches along a flat direction (renderer bounds follow a turn at once).
        static float Extent(Transform t, Vector3 along)
        {
            var size = Size(t);
            return Mathf.Abs(size.x * along.x) + Mathf.Abs(size.z * along.z);
        }

        // The lowest point of what it draws (renderer bounds follow a move at once, collider bounds
        // only after the next physics step); for the body that's its attached parts too.
        static bool Bottom(Transform t, out float bottom)
        {
            bottom = float.MaxValue;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
                if (r.enabled) bottom = Mathf.Min(bottom, r.bounds.min.y);
            return bottom != float.MaxValue;
        }
    }
}
