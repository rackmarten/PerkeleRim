using System;
using System.Collections;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The Satsuma's attached parts for Skyrim (PartSet, rules in Link/SatsumaModel): every part that
    // is on the car right now, each drawn from a model of its own (ModelSender.IdFor, read in steps like
    // an item's, with the van's glass rules) and posed relative to the body every frame. The body's own
    // model (VehicleLink, "Satsuma") leaves every part out, on or not. Parts that are off are loose
    // items (ItemLink) as before.
    //
    // The parts are looked for every half second (a part going on or coming off shows within that);
    // their content keys are asked a few a frame, so a fully built car coming up never asks for 150 at
    // once, and again in turn, so a part that changed its look (painted, a bulb in, its bolts showing
    // once it is on) gets its new model. A new look is a new read (a door is 20 MB with its textures)
    // and a new node in Skyrim that is never freed, so a part is asked again at most every RekeyEvery
    // seconds, that times its changes, and not after MaxLooks changes: one whose material keeps
    // changing (MSC fading it) stays as it last looked.
    public sealed class SatsumaParts
    {
        const float ScanEvery = 0.5f;
        const int KeysPerFrame = 4;   // while parts haven't been asked yet
        const int RekeysPerFrame = 1; // once they all were: each in turn, for a changed look
        const float RekeyEvery = 10f; // seconds between a part's asks, times the looks it had
        const int MaxLooks = 4;       // a part's models in a session at most

        readonly Action<string> log;
        readonly List<Transform> parts = new List<Transform>();  // on the car, in hierarchy order
        readonly HashSet<Transform> partSet = new HashSet<Transform>();
        // A part's model id, 0 = nothing to draw of its own (all its meshes belong to nested parts, or
        // are off): left out of the set. Not in here = not asked yet.
        readonly Dictionary<Transform, uint> models = new Dictionary<Transform, uint>();
        readonly Dictionary<Transform, Predicate<Transform>> includes = new Dictionary<Transform, Predicate<Transform>>();
        readonly Dictionary<Transform, bool> isPart = new Dictionary<Transform, bool>();  // every object below the car seen so far
        // A part's looks so far (models it had) and when it may be asked again.
        sealed class Looks
        {
            public int Count;
            public float Next;
        }
        readonly Dictionary<Transform, Looks> looks = new Dictionary<Transform, Looks>();
        Transform lastPlayer;
        List<AttachedParts.Entry> entries = new List<AttachedParts.Entry>();
        List<AttachedParts.Entry> lastEntries = new List<AttachedParts.Entry>();
        Transform car;
        float nextScan;
        int keyCursor;
        bool wasEmpty = true;

        public SatsumaParts(Action<string> log) { this.log = log; }

        public int Count => parts.Count;

        // Whether t is a part object (on or not): named like one or with a Removal FSM. Cached per object.
        bool IsPartObject(Transform t)
        {
            bool part;
            if (isPart.TryGetValue(t, out part)) return part;
            bool removal = false;
            foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Removal") removal = true;
            part = SatsumaModel.IsPart(t.name, removal);
            isPart[t] = part;
            return part;
        }

        // The part t belongs to (the nearest part object at or above it below the car), or null for the body.
        Transform PartOf(Transform t)
        {
            for (var p = t; p != null && p != car; p = p.parent)
                if (IsPartObject(p)) return p;
            return null;
        }

        // For the body's model: everything of a part, on or not, is drawn by the parts.
        public bool Excluded(Transform t) => car != null && PartOf(t) != null;

        public void Attach(Transform car)
        {
            if (car == this.car) return;
            this.car = car;
            parts.Clear();
            partSet.Clear();
            models.Clear();
            includes.Clear();
            isPart.Clear();
            looks.Clear();
            nextScan = 0f;
        }

        // Every frame the Satsuma is on the island: finds the parts, asks for their models, and writes
        // the PartSet; with onIsland false (or no car) an empty set that is nobody's.
        public void Update(Link link, bool onIsland, uint generation, ModelSender sender, Transform player)
        {
            if (link == null) return;
            var set = new PartSet();
            if (!onIsland || car == null)
            {
                link.WritePartSet(ref set);
                return;
            }
            lastPlayer = player;
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + ScanEvery;
                Find(parts, player);
                partSet.Clear();
                foreach (var t in parts) partSet.Add(t);
                var gone = new List<Transform>();
                foreach (var t in models.Keys) if (t == null || !partSet.Contains(t)) gone.Add(t);
                foreach (var t in gone)
                {
                    models.Remove(t);
                    includes.Remove(t);
                    looks.Remove(t);
                }
            }
            AskModels(sender);
            entries.Clear();
            var toCar = Quaternion.Inverse(car.rotation);
            foreach (var t in parts)
            {
                // Come off since the last scan (a door unhung): its pose isn't the car's any more.
                if (t == null || !t.IsChildOf(car)) continue;
                uint model;
                bool asked = models.TryGetValue(t, out model);
                if (asked && model == 0) continue;  // nothing of its own to draw
                var c = ModelSender.RelativeTo(car, t).GetColumn(3);
                var p = Coords.UnityToSkyrim(new Vec3(c.x, c.y, c.z));
                var r = toCar * t.rotation;
                entries.Add(new AttachedParts.Entry
                {
                    Part = unchecked((uint)t.GetInstanceID()),
                    Model = model,
                    Visible = t.gameObject.activeInHierarchy,
                    Rotation = Coords.UnityToSkyrim(new Quat(r.x, r.y, r.z, r.w)),
                    Translation = p,
                });
            }
            int left = AttachedParts.Encode(entries, Wire.VehicleSatsuma, generation, (uint)Time.frameCount, ref set);
            link.WritePartSet(ref set);
            if (!AttachedParts.SameSet(entries, lastEntries))
            {
                int pending = 0;
                foreach (var e in entries) if (e.Model == 0) pending++;
                if (pending == 0 || entries.Count != lastEntries.Count || wasEmpty)
                    log($"satsuma: {entries.Count} parts on the car{(pending > 0 ? $", {pending} models still to ask" : "")}{(left > 0 ? $", {left} left out" : "")}");
                wasEmpty = entries.Count == 0;
                var swap = lastEntries;
                lastEntries = entries;
                entries = swap;
            }
        }

        // The parts on the car right now into list: active part objects below it, not the player or
        // what he holds (a part in the hand of a player sitting in it).
        void Find(List<Transform> list, Transform player)
        {
            list.Clear();
            foreach (var t in car.GetComponentsInChildren<Transform>())  // active ones only
                if (t != car && IsPartObject(t) && (player == null || !t.IsChildOf(player))) list.Add(t);
        }

        // A few content keys a frame: parts without a model first, then each in turn.
        void AskModels(ModelSender sender)
        {
            int asked = 0;
            foreach (var t in parts)
            {
                if (asked == KeysPerFrame) return;
                if (t == null || models.ContainsKey(t)) continue;
                Ask(sender, t);
                asked++;
            }
            if (asked > 0 || parts.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < parts.Count && asked < RekeysPerFrame; i++)
            {
                keyCursor = (keyCursor + 1) % parts.Count;
                var t = parts[keyCursor];
                Looks l;
                if (t == null || (looks.TryGetValue(t, out l) && (l.Count >= MaxLooks || now < l.Next))) continue;
                Ask(sender, t);
                asked++;
            }
        }

        void Ask(ModelSender sender, Transform part)
        {
            Predicate<Transform> include;
            if (!includes.TryGetValue(part, out include))
            {
                var root = part;
                include = t => PartOf(t) == root;
                includes[part] = include;
            }
            uint before;
            bool had = models.TryGetValue(part, out before);
            uint id = sender.IdFor(part, include, VehicleModel.Plain);
            models[part] = id;
            Looks l;
            if (!looks.TryGetValue(part, out l)) looks[part] = l = new Looks { Count = 1 };
            if (had && id != before)
            {
                l.Count++;
                log(l.Count >= MaxLooks
                    ? $"satsuma: {part.name} looks different again (model {before:X8} -> {id:X8}), {l.Count} looks: kept as it is from now on"
                    : $"satsuma: {part.name} looks different now (model {before:X8} -> {id:X8}), look {l.Count}");
            }
            l.Next = Time.realtimeSinceStartup + RekeyEvery * l.Count;
        }

        // Spike command "satsuma": every part on the car with its model.
        public void Dump()
        {
            if (car == null) { log("satsuma: not found"); return; }
            var on = new List<Transform>();
            Find(on, lastPlayer);
            log($"satsuma: {on.Count} parts on the car at {car.position}");
            foreach (var t in on)
            {
                uint model;
                models.TryGetValue(t, out model);
                log($"satsuma: part {ModelSender.PathBelow(car, t)} model {model:X8}");
            }
        }

        // Spike command "satsuma half": puts the parts of SatsumaModel.HalfBuilt on as a player does,
        // through each trigger's own Assembly FSM: the loose part (its HandChild) into the hand
        // (ItemPivot), the FSM to its Find correct part state, then its ASSEMBLE event, one part a few
        // frames. MSC does the rest (its copy switched on or the part hung on its pivot, the loose one
        // gone). A part that didn't go on goes back where it lay.
        public IEnumerator Assemble(string[] triggers)
        {
            if (car == null) { log("satsuma: not found"); yield break; }
            var pivotVar = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmGameObject("ItemPivot");
            var pivot = pivotVar != null && pivotVar.Value != null ? pivotVar.Value.transform : null;
            if (pivot == null) { log("satsuma: no ItemPivot"); yield break; }
            int on = 0;
            foreach (var name in triggers)
            {
                var trigger = FindBelow(car, name);
                var fsm = trigger != null ? Fsm(trigger, "Assembly") : null;
                if (fsm == null || !trigger.gameObject.activeInHierarchy)
                {
                    log($"satsuma: {name}: {(trigger == null ? "no such trigger" : fsm == null ? "no Assembly FSM" : "off (on already)")}");
                    continue;
                }
                var handChild = fsm.FsmVariables.FindFsmString("HandChild");
                var part = handChild != null ? FindLoose(handChild.Value) : null;
                if (part == null)
                {
                    log($"satsuma: {name}: no loose {(handChild != null ? handChild.Value : "part")}");
                    continue;
                }
                var parent = part.parent;
                var position = part.position;
                var rotation = part.rotation;
                var body = part.GetComponent<Rigidbody>();
                bool kinematic = body != null && body.isKinematic;
                if (body != null) body.isKinematic = true;
                part.SetParent(pivot, false);
                part.localPosition = Vector3.zero;
                var state = fsm.Fsm.GetState("Find correct part") ?? fsm.Fsm.GetState("Wait for assembly");
                if (state != null && SaveSync.SwitchState != null) SaveSync.SwitchState.Invoke(fsm.Fsm, new object[] { state });
                yield return null;
                fsm.SendEvent("ASSEMBLE");
                for (int i = 0; i < 10; i++) yield return null;
                // Gone from the hand: destroyed (its copy switched on) or hung on its pivot.
                bool went = part == null || !part.IsChildOf(pivot);
                if (went) on++;
                else
                {
                    part.SetParent(parent, true);
                    part.position = position;
                    part.rotation = rotation;
                    if (body != null) body.isKinematic = kinematic;
                }
                log($"satsuma: {name}: {(handChild != null ? handChild.Value : "")} {(went ? "on" : "didn't go on (FSM in " + fsm.ActiveStateName + ")")}");
            }
            log($"satsuma: {on} of {triggers.Length} parts put on");
            nextScan = 0f;
        }

        // Spike command "satsuma bare": takes every part with a Removal FSM off as MSC's Remove part state
        // does (the copy switched off and the loose part back, or the part unhung), deepest first.
        public IEnumerator Strip()
        {
            if (car == null) { log("satsuma: not found"); yield break; }
            var on = new List<Transform>();  // its own list: the half-second scan refills parts meanwhile
            Find(on, lastPlayer);
            int off = 0;
            for (int i = on.Count - 1; i >= 0; i--)
            {
                var t = on[i];
                if (t == null || car == null || !t.IsChildOf(car) || !t.gameObject.activeInHierarchy) continue;
                var fsm = Fsm(t, "Removal");
                var state = fsm != null ? fsm.Fsm.GetState("Remove part") : null;
                if (state == null || SaveSync.SwitchState == null) continue;
                SaveSync.SwitchState.Invoke(fsm.Fsm, new object[] { state });
                off++;
                yield return null;
                yield return null;
            }
            nextScan = 0f;
            for (int i = 0; i < 5; i++) yield return null;
            if (car == null) yield break;
            Find(on, lastPlayer);
            log($"satsuma: took {off} parts off, {on.Count} still on (without a Removal FSM: wiring, the halfshafts, as MSC has them)");
        }

        static PlayMakerFSM Fsm(Transform t, string name)
        {
            foreach (var f in t.GetComponents<PlayMakerFSM>())
                if (f.FsmName == name) return f;
            return null;
        }

        static Transform FindBelow(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // A loose part by name: active, tagged PART, not on a vehicle.
        Transform FindLoose(string name)
        {
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
            {
                var t = body.transform;
                if (t.name == name && t.CompareTag("PART") && !t.IsChildOf(car) && t.gameObject.activeInHierarchy) return t;
            }
            return null;
        }
    }
}
