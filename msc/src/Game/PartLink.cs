using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The Satsuma's parts as MSC's own database has them, for Skyrim's bolt-state probe (PartState, v55). MSC keeps a
    // "Data" FSM per part under Database/DatabaseBody, DatabaseMotor and DatabaseMechanics (Installed, Bolted, Damaged,
    // ThisPart: the part's object). The part's own BoltCheck FSM has Tightness and BoltedYES, and each bolt below it a
    // Screw FSM with its Stage (0..8; docs/FSM_MAP.md, "Building the Satsuma"). MSC itself does all the assembling and
    // bolting; this only reports it. Sent while MSC has the player: what changed, a part that moved, and everything
    // again from scratch at each handover.
    public class PartLink
    {
        static readonly string[] Sections = { "DatabaseBody", "DatabaseMotor", "DatabaseMechanics" };
        const float FindEvery = 10f;    // seconds between looking for database entries again
        const float ReadEvery = 0.25f;  // and between reading them
        const int SendMax = 24;         // reports a frame at most: a handover sends all of them, the ring has models too
        const float PlacedRadius = 200f;  // meters from the player: a part further off isn't on Skyrim's ground there

        class Entry
        {
            public string Name;
            public PlayMakerFSM Data;
            public FsmBool Installed, Bolted, Damaged;
            public FsmGameObject ThisPart;
            public FsmGameObject Fitted, Loose;  // parts MSC swaps objects for (the radiator): ActivateThis and SpawnThis
            public Transform Corner;             // a wheel (no database entry): the car's wheelFL..wheelRR it hangs below
            public GameObject Part;              // ThisPart as the bolts below were read from
            public FsmFloat Tightness, Full;     // its BoltCheck, or null
            public readonly List<FsmInt> Stages = new List<FsmInt>();
            public PartReport? Sent;
        }

        readonly Island island;
        readonly Action<string> log;
        readonly List<Entry> entries = new List<Entry>();
        float nextFind, nextRead;
        bool wasPlaying;
        GameObject database;  // MSC's Database root, found once (a full scan of MSC's objects is costly)
        int shape = -1;       // the sections' child counts added up when the entries were made
        int[] stages = new int[8];
        int foundCount = -1;
        int cursor;

        public PartLink(Island island, Action<string> log)
        {
            this.island = island;
            this.log = log;
        }

        public void Update(Link link, bool playing, Vector3 feet)
        {
            if (!playing)
            {
                if (wasPlaying) foreach (var e in entries) e.Sent = null;  // from scratch at the next handover
                wasPlaying = false;
                return;
            }
            wasPlaying = true;
            float now = Time.realtimeSinceStartup;
            if (now >= nextFind)
            {
                nextFind = now + FindEvery;
                Find();
            }
            if (now < nextRead) return;
            nextRead = now + ReadEvery;
            int sent = 0;
            for (int n = 0; n < entries.Count && sent < SendMax; n++)
            {
                var e = entries[(cursor + n) % entries.Count];
                var report = Read(e, feet);
                if (report == null || !PartReport.ShouldSend(e.Sent, report.Value)) continue;
                var payload = report.Value.Encode();
                if (!link.Send(MessageType.PartState, payload, payload.Length)) break;  // full ring: next read
                if (e.Sent == null || e.Sent.Value.Flags != report.Value.Flags || e.Sent.Value.Stages != report.Value.Stages)
                    Log(e.Sent, report.Value);
                e.Sent = report;
                sent++;
            }
            if (entries.Count > 0) cursor = (cursor + sent) % entries.Count;
        }

        // Every part's state, for the spike command "parts [word]".
        public void Dump(string word)
        {
            shape = -1;  // look again from scratch
            Find();
            log($"parts: {entries.Count} database entries, Database {(database != null ? (database.activeInHierarchy ? "active" : "inactive") + ", " + database.transform.childCount + " children" : "not found")}");
            if (database != null && entries.Count == 0)
                foreach (Transform section in database.transform)
                {
                    var first = section.childCount > 0 ? section.GetChild(0) : null;
                    var fsms = new List<string>();
                    if (first != null) foreach (var f in first.GetComponents<PlayMakerFSM>()) fsms.Add(f.FsmName);
                    log($"parts: {section.name}: {section.childCount} children, the first {(first != null ? first.name : "-")} has [{string.Join(", ", fsms.ToArray())}]");
                }
            foreach (var e in entries)
            {
                if (!string.IsNullOrEmpty(word) && e.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var r = Read(e, Vector3.zero);
                if (r == null) { log($"parts: {e.Name}: no data"); continue; }
                var v = r.Value;
                log($"parts: {e.Name} ({(e.Part != null ? ItemLink.Path(e.Part.transform) : "no object")}) installed {(v.Flags & Wire.PartInstalled) != 0} " +
                    $"bolted {(v.Flags & Wire.PartBolted) != 0} bolts {v.BoltsTight}/{v.Bolts} tight, stages {v.Stages}, tightness {v.Tightness}/{v.TightnessFull}");
            }
        }

        void Log(PartReport? before, PartReport now)
        {
            if (before == null) return;  // the first report of each part isn't news
            log($"parts: {now.Name} installed {(now.Flags & Wire.PartInstalled) != 0} bolted {(now.Flags & Wire.PartBolted) != 0} " +
                $"bolts {now.BoltsTight}/{now.Bolts} tight, stages {now.Stages}");
        }

        void Find()
        {
            if (database == null)
            {
                // Through a section: an empty object elsewhere is also named Database, and GameObject.Find took that.
                var body = GameObject.Find("Database/DatabaseBody");
                database = body != null && body.transform.parent != null ? body.transform.parent.gameObject : RootWith("Database", "DatabaseBody");
            }
            if (database == null) { entries.Clear(); shape = -1; return; }
            int now = 0;
            foreach (var section in Sections)
            {
                var t = database.transform.Find(section);
                if (t != null) now += t.childCount;
            }
            if (now == shape && entries.Count > 0 && wheels.Count > 0) return;  // MSC never adds an entry: nothing new to find
            shape = now;
            var known = new Dictionary<PlayMakerFSM, Entry>();
            foreach (var e in entries) if (e.Data != null) known[e.Data] = e;
            entries.Clear();
            foreach (var section in Sections)
            {
                var root = database.transform.Find(section);
                if (root == null) continue;
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    foreach (var fsm in child.GetComponents<PlayMakerFSM>())
                    {
                        if (fsm.FsmName != "Data") continue;
                        Entry e;
                        if (!known.TryGetValue(fsm, out e))
                        {
                            var vars = fsm.FsmVariables;
                            e = new Entry
                            {
                                Name = child.name,
                                Data = fsm,
                                Installed = vars.FindFsmBool("Installed"),
                                Bolted = vars.FindFsmBool("Bolted"),
                                Damaged = vars.FindFsmBool("Damaged"),
                                ThisPart = vars.FindFsmGameObject("ThisPart"),
                                Fitted = vars.FindFsmGameObject("ActivateThis"),
                                Loose = vars.FindFsmGameObject("SpawnThis"),
                            };
                        }
                        if (e.Installed != null) entries.Add(e);
                        break;
                    }
                }
            }
            FindWheels(known);
            if (entries.Count != foundCount) log($"parts: {entries.Count} Satsuma parts in MSC's database and its wheels");
            foundCount = entries.Count;
        }

        PartReport? Read(Entry e, Vector3 feet)
        {
            if (e.Corner != null) return ReadWheel(e, feet);
            if (e.Data == null) return null;
            bool installed = e.Installed != null && e.Installed.Value;
            var part = e.ThisPart != null ? e.ThisPart.Value : null;
            if (part == null && (e.Fitted != null || e.Loose != null))  // one object on the car, another loose
                part = installed ? (e.Fitted != null ? e.Fitted.Value : null) : (e.Loose != null ? e.Loose.Value : null);
            if (part != e.Part) Bind(e, part);
            int count = e.Stages.Count;
            if (stages.Length < count) stages = new int[count];
            for (int i = 0; i < count; i++) stages[i] = e.Stages[i] != null ? e.Stages[i].Value : 0;
            int bolts, tight, sum;
            PartReport.CountBolts(stages, count, out bolts, out tight, out sum);
            bool placed = part != null && part.activeInHierarchy && (part.transform.position - feet).sqrMagnitude < PlacedRadius * PlacedRadius;
            var at = part != null ? part.transform.position : Vector3.zero;
            return new PartReport
            {
                Name = e.Name,
                Flags = PartReport.FlagsFor(installed, e.Bolted != null && e.Bolted.Value,
                    e.Damaged != null && e.Damaged.Value, placed),
                Generation = island.Generation,
                Position = placed ? island.IslandToSkyrim(new Vec3(at.x, at.y, at.z)) : new Vec3(0f, 0f, 0f),
                Bolts = bolts,
                BoltsTight = tight,
                Stages = sum,
                Tightness = e.Tightness != null ? e.Tightness.Value : 0f,
                TightnessFull = e.Full != null ? e.Full.Value : 0f,
                Object = part != null ? unchecked((uint)part.transform.GetInstanceID()) : 0u,
            };
        }

        static readonly string[] Corners = { "FL", "FR", "RL", "RR" };

        // The stock wheels have no database entry: one entry per corner of the car (WheelFL..WheelRR), the wheel
        // fitted there being the part object with a Removal FSM below the car's wheelFL (on its pivot_wheel_standard or
        // pivot_wheel_offset, where its Assembly puts it).
        void FindWheels(Dictionary<PlayMakerFSM, Entry> known)
        {
            var car = GameObject.Find("SATSUMA(557kg, 248)");
            if (car == null) return;
            foreach (var corner in Corners)
            {
                Transform wheel = null;
                foreach (var t in car.GetComponentsInChildren<Transform>(true))
                    if (t.name == "wheel" + corner) { wheel = t; break; }
                if (wheel == null) continue;
                var old = wheels.Find(w => w.Corner == wheel);
                var e = old ?? new Entry { Name = "Wheel" + corner, Corner = wheel };
                if (old == null) wheels.Add(e);
                entries.Add(e);
            }
        }

        readonly List<Entry> wheels = new List<Entry>();

        PartReport? ReadWheel(Entry e, Vector3 feet)
        {
            GameObject part = null;
            foreach (var fsm in e.Corner.GetComponentsInChildren<PlayMakerFSM>())  // active ones: a fitted wheel
                if (fsm.FsmName == "Removal" && fsm.name.StartsWith("wheel_") && (fsm.transform.parent != null && fsm.transform.parent.name.StartsWith("pivot_wheel")))
                { part = fsm.gameObject; break; }
            if (part != e.Part) Bind(e, part);
            int count = e.Stages.Count;
            if (stages.Length < count) stages = new int[count];
            for (int i = 0; i < count; i++) stages[i] = e.Stages[i] != null ? e.Stages[i].Value : 0;
            int bolts, tight, sum;
            PartReport.CountBolts(stages, count, out bolts, out tight, out sum);
            var at = part != null ? part.transform.position : e.Corner.position;
            bool placed = (at - feet).sqrMagnitude < PlacedRadius * PlacedRadius;
            return new PartReport
            {
                Name = e.Name,
                // MSC keeps no Bolted for a wheel: bolted is every nut screwed all the way in.
                Flags = PartReport.FlagsFor(part != null, part != null && bolts > 0 && tight == bolts, false, placed),
                Generation = island.Generation,
                Position = placed ? island.IslandToSkyrim(new Vec3(at.x, at.y, at.z)) : new Vec3(0f, 0f, 0f),
                Bolts = bolts,
                BoltsTight = tight,
                Stages = sum,
                Object = part != null ? unchecked((uint)part.transform.GetInstanceID()) : 0u,
            };
        }

        // The BoltCheck and the bolts' stages of the object the database entry points at now.
        static void Bind(Entry e, GameObject part)
        {
            e.Part = part;
            e.Tightness = e.Full = null;
            e.Stages.Clear();
            if (part == null) return;
            foreach (var fsm in part.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "BoltCheck") continue;
                e.Tightness = fsm.FsmVariables.FindFsmFloat("Tightness");
                e.Full = fsm.FsmVariables.FindFsmFloat("BoltedYES");
            }
            // Its bolts (BoltPM), not a filler cap's thread (OpenCap :: Screw) nor the bolts of a part fitted onto it
            // (a fender's mudflap): those have a BoltCheck of their own on the way up.
            foreach (var fsm in part.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm.FsmName != "Screw" || fsm.gameObject == part || fsm.name.IndexOf("Bolt", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (OwnedByAnother(fsm.transform, part.transform)) continue;
                var stage = fsm.FsmVariables.FindFsmInt("Stage");
                if (stage != null) e.Stages.Add(stage);
            }
        }

        // A scene root by name that has that child, active or not (GameObject.Find finds only active objects).
        static GameObject RootWith(string name, string child)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                if (go.transform.parent == null && go.hideFlags == HideFlags.None && go.name == name && go.transform.Find(child) != null) return go;
            return null;
        }

        static bool OwnedByAnother(Transform bolt, Transform part)
        {
            for (var t = bolt.parent; t != null && t != part; t = t.parent)
                foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == "BoltCheck") return true;
            return false;
        }
    }
}
