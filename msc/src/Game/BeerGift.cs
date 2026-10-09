using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Beer from Skyrim (GiveBeer: mead or ale the player picked up there), as MSC beer bottles.
    // A beer case's bottles are its children, and its Use FSM counts the ones drunk in
    // DestroyedBottles (saved, and taken off again on load). So bottles go into the nearest of the
    // player's cases that has drunk ones, as copies of a bottle still in it with the count lowered;
    // with no such case, the store's spawner makes a new one (Spawner/CreateItems :: Beer, as when
    // buying a case), which is moved next to the player and trimmed down to the bottles given.
    public class BeerGift
    {
        const int CaseBottles = 24;
        const string CaseName = "beer case(itemx)";  // a case with bottles left; an empty one is renamed
        const float SpawnTimeout = 5f;  // seconds for a new case's Use FSM to load

        readonly Action<string> log;
        int pending;
        PlayMakerFSM spawned;  // Use FSM of the case just spawned, until it can be trimmed
        int spawnedBottles;
        float spawnedAt;

        public BeerGift(Action<string> log) { this.log = log; }

        public void Give(int count)
        {
            if (count <= 0) return;
            pending += count;
            log($"beer: {count} from Skyrim, {pending} to hand out");
        }

        // Once a frame while MSC has the player (handed over and placed), so a new case lands next to
        // it in Skyrim's world; beer from before that waits.
        public void Update(Transform player, Vector3 feet)
        {
            if (spawned != null) { TrimSpawned(); return; }
            if (pending <= 0 || player == null) return;

            int room;
            var fsm = NearestCaseWithRoom(feet, out room);
            if (fsm != null)
            {
                int n = Math.Min(pending, room);
                AddBottles(fsm, n);
                pending -= n;
                log($"beer: {n} bottles into {fsm.FsmVariables.FindFsmString("ID").Value} ({fsm.transform.childCount} now)");
                return;
            }
            Spawn(player, feet, Math.Min(pending, CaseBottles));
        }

        // The player's case with drunk bottles closest to feet: under ITEMS or on its own, not the
        // uncle's or a party's.
        static PlayMakerFSM NearestCaseWithRoom(Vector3 feet, out int room)
        {
            room = 0;
            PlayMakerFSM best = null;
            float bestDistance = float.MaxValue;
            foreach (PlayMakerFSM fsm in UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM)))
            {
                if (fsm.FsmName != "Use" || fsm.gameObject.name != CaseName || fsm.transform.childCount == 0) continue;
                var root = fsm.transform.root;
                if (root != fsm.transform && root.name != "ITEMS") continue;
                if (fsm.ActiveStateName != "Wait player" && fsm.ActiveStateName != "Wait button") continue;  // loading, or being drunk from
                var destroyed = fsm.FsmVariables.FindFsmInt("DestroyedBottles");
                var consumed = fsm.FsmVariables.FindFsmBool("Consumed");
                if (destroyed == null || destroyed.Value <= 0 || (consumed != null && consumed.Value)) continue;
                float d = (fsm.transform.position - feet).sqrMagnitude;
                if (d >= bestDistance) continue;
                best = fsm;
                bestDistance = d;
                room = destroyed.Value;
            }
            return best;
        }

        static void AddBottles(PlayMakerFSM fsm, int n)
        {
            var box = fsm.transform;
            var template = box.GetChild(0);
            var destroyed = fsm.FsmVariables.FindFsmInt("DestroyedBottles");
            for (int i = 0; i < n; i++)
            {
                var bottle = (GameObject)UnityEngine.Object.Instantiate(template.gameObject);
                bottle.name = template.name;
                bottle.transform.parent = box;
                bottle.transform.localPosition = template.localPosition;
                bottle.transform.localRotation = template.localRotation;
                bottle.transform.localScale = template.localScale;
                destroyed.Value--;
            }
        }

        void Spawn(Transform player, Vector3 feet, int bottles)
        {
            PlayMakerFSM spawner = null;
            var go = GameObject.Find("Spawner/CreateItems");
            if (go != null)
                foreach (var f in go.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Beer") spawner = f;
            if (spawner == null)
            {
                log($"beer: no Spawner/CreateItems :: Beer, {pending} bottles dropped");
                pending = 0;
                return;
            }

            spawner.SendEvent("SPAWNITEM");
            var box = spawner.FsmVariables.FindFsmGameObject("New").Value;
            PlayMakerFSM use = null;
            if (box != null)
                foreach (var f in box.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Use") use = f;
            if (use == null)
            {
                log($"beer: the spawner made no case, {pending} bottles dropped");
                pending = 0;
                return;
            }

            box.transform.position = feet + player.forward * 0.7f + Vector3.up * 0.4f;
            box.transform.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            var body = box.GetComponent<Rigidbody>();
            if (body != null) body.velocity = Vector3.zero;
            pending -= bottles;
            spawned = use;
            spawnedBottles = bottles;
            spawnedAt = Time.realtimeSinceStartup;
            log($"beer: new case {box.name} next to the player for {bottles} bottles");
        }

        // A new case loads its saved state first (Use FSM: State 2, State 1, then Wait player); only
        // after that do bottles taken out and DestroyedBottles stay as set here.
        void TrimSpawned()
        {
            if (spawned.ActiveStateName != "Wait player" && spawned.ActiveStateName != "Wait button")
            {
                if (Time.realtimeSinceStartup - spawnedAt < SpawnTimeout) return;
                log($"beer: case {spawned.name} didn't load in {SpawnTimeout} s, left full");
                spawned = null;
                return;
            }
            var box = spawned.transform;
            int remove = box.childCount - spawnedBottles;
            for (int i = 0; i < remove; i++) UnityEngine.Object.Destroy(box.GetChild(i).gameObject);
            var destroyed = spawned.FsmVariables.FindFsmInt("DestroyedBottles");
            if (destroyed != null) destroyed.Value = CaseBottles - spawnedBottles;
            log($"beer: case {spawned.name} holds {spawnedBottles} bottles");
            spawned = null;
        }
    }
}
