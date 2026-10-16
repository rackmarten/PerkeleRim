using System;
using System.Collections.Generic;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Stocks the garage's shelves once per save (StockShelves, Skyrim's cosave remembers it): one
    // store item per spot (ShelfStock picks which), made by the store's own spawner
    // (Spawner/CreateItems, the FSM named after the item, SPAWNITEM, the new item in its "New"
    // variable, as when buying it) and put on its spot once the anchor's island has ground there.
    // One item a frame; they're MSC's items from then on, saved with its game and drawn in Skyrim
    // like any other (ItemLink). MSC's world is the same whichever Skyrim save is loaded, so a spot
    // that still holds an item (stocked for another save, or this one in an earlier session) is
    // left as it is.
    public class ShelfStocker
    {
        const float Clearance = 0.02f;  // meters between the item's bottom and the board it's set on
        const float GroundWait = 20f;  // seconds the spots wait for their ground
        const float Occupied = 0.25f;  // meters around a spot where an item means it's stocked already

        readonly Island island;
        readonly Func<Vector3, Vector3?> findGround;
        readonly Action<string> log;
        uint generation;
        readonly List<ShelfStock.Spot> spots = new List<ShelfStock.Spot>();
        int next;
        float since;

        public ShelfStocker(Island island, Func<Vector3, Vector3?> findGround, Action<string> log)
        {
            this.island = island;
            this.findGround = findGround;
            this.log = log;
        }

        public void OnMessage(byte[] payload, int size)
        {
            List<ShelfStock.Spot> got;
            if (!ShelfStock.Decode(payload, size, out generation, out got))
            {
                log($"shelves: bad StockShelves ({size} bytes)");
                return;
            }
            spots.Clear();
            spots.AddRange(got);
            next = 0;
            since = Time.realtimeSinceStartup;
            log($"shelves: {spots.Count} spots to stock for gen {generation}");
        }

        // Every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(bool playing)
        {
            if (next >= spots.Count || !playing) return;
            if (island.Generation != generation)
            {
                log($"shelves: the island moved on to gen {island.Generation} before the shelves were stocked, {spots.Count - next} items dropped");
                spots.Clear();
                return;
            }
            var spot = spots[next];
            var at = island.SkyrimToIsland(spot.Position);
            var position = new Vector3(at.x, at.y, at.z);
            var board = findGround(position);
            if (!board.HasValue)
            {
                if (Time.realtimeSinceStartup - since < GroundWait) return;
                log($"shelves: no ground at the shelves after {GroundWait} s, {spots.Count - next} items dropped");
                spots.Clear();
                return;
            }
            next++;
            var taken = ItemAt(position);
            if (taken != null)
            {
                log($"shelves: spot {next - 1} already holds {taken.name}" + (next == spots.Count ? ", shelves stocked" : ""));
                return;
            }
            var item = Spawn(spot.Item);
            if (item == null) return;
            // Set down just above the board by its own shape: food dropped from a fixed height
            // sometimes started inside the board and fell through it.
            item.transform.position = position;
            item.transform.rotation = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(spot.Heading), 0f);
            float bottom;
            if (Bottom(item, out bottom))
                item.transform.position += Vector3.up * (board.Value.y + Clearance - bottom);
            var body = item.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = item.transform.position;
                body.rotation = item.transform.rotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            log($"shelves: {item.name} on spot {next - 1}" + (next == spots.Count ? ", shelves stocked" : ""));
        }

        // The lowest point of what the item draws (renderer bounds follow a move right away,
        // collider bounds only after the next physics step).
        static bool Bottom(GameObject item, out float bottom)
        {
            bottom = float.MaxValue;
            foreach (var r in item.GetComponentsInChildren<Renderer>())
                bottom = Mathf.Min(bottom, r.bounds.min.y);
            return bottom != float.MaxValue;
        }

        // A physics item (anything with a rigidbody: the island's own chunks have none) on the spot.
        static Rigidbody ItemAt(Vector3 spot)
        {
            foreach (var c in Physics.OverlapSphere(spot, Occupied))
                if (c.attachedRigidbody != null) return c.attachedRigidbody;
            return null;
        }

        GameObject Spawn(string fsmName)
        {
            var go = GameObject.Find("Spawner/CreateItems");
            PlayMakerFSM spawner = null;
            if (go != null)
                foreach (var f in go.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == fsmName) spawner = f;
            if (spawner == null)
            {
                log($"shelves: no Spawner/CreateItems :: {fsmName}");
                return null;
            }
            spawner.SendEvent("SPAWNITEM");
            var made = spawner.FsmVariables.FindFsmGameObject("New");
            if (made == null || made.Value == null)
            {
                log($"shelves: Spawner/CreateItems :: {fsmName} made nothing");
                return null;
            }
            return made.Value;
        }
    }
}
