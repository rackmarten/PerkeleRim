using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The player half of the link, next to LinkHost:
    //  - feeds InputShim from Skyrim's InputState, so MSC's own scripts walk, look and crouch;
    //  - takes the anchor from WorldspaceChange and builds the collision island from Skyrim's
    //    CollisionChunk messages, one MeshCollider per chunk, dropping chunks the player left behind;
    //  - holds the player at a new anchor until the ground there has arrived, then puts it down;
    //  - writes PlayerState (Skyrim world space) every frame, valid only while Skyrim has handed
    //    the player over and the player stands on the current anchor's island.
    public unsafe class PlayerLink : MonoBehaviour
    {
        // Drop the player this far above the ground so it never starts inside the collider.
        const float SpawnClearance = 0.05f;
        // Chunks further than this (in chunks, either axis) from the player's are dropped. Skyrim
        // streams 2 around the player and forgets at 3, so it always resends what's dropped here.
        const int KeepRadius = 4;
        // Rays looking for the ground under a new anchor start this far above it.
        const float GroundSearch = 50f;

        readonly Island island = new Island();
        readonly InputFrame input = new InputFrame();
        Action<string> log;

        GameObject islandObject;
        readonly Dictionary<long, GameObject> chunks = new Dictionary<long, GameObject>();
        float nextPrune;
        uint placedGeneration;  // island generation the player was put down for; 0 = none
        uint holdingGeneration;  // generation the player is held for while its ground loads
        float holdSince;
        bool holdLogged;
        Behaviour motor;
        bool motorDisabled;
        bool lastOwned;
        float nextMenuClose;

        Transform player;
        Transform fpsCamera;
        CharacterController controller;
        PlayMakerFSM crouch;

        uint fixedFrame;
        bool haveLastFeet;
        Vector3 lastFeet;
        Vec3 velocity;  // Skyrim units per second

        public void Init(Action<string> log)
        {
            this.log = log;
            InputShim.Feed = FeedInput;
        }

        // ---- Input (runs inside the first Input call of each frame) ----

        void FeedInput()
        {
            var link = LinkHost.Current;
            InputState s;
            if (link == null || !link.ReadInput(out s))
            {
                input.Reset();
                InputShim.Active = false;
                return;
            }

            // Keep the mouse totals current even while Skyrim owns the player, so taking over
            // doesn't start with a jump.
            input.Update(ref s);
            InputShim.Active = input.OwnsPlayer;
            if (!input.OwnsPlayer) return;

            InputShim.Keys.Clear();
            foreach (var k in input.Keys) InputShim.Keys.Add((KeyCode)k);
            InputShim.Axes["Mouse X"] = input.MouseX;
            InputShim.Axes["Mouse Y"] = input.MouseY;
            InputShim.Axes["Horizontal"] = input.Horizontal;
            InputShim.Axes["Vertical"] = input.Vertical;
        }

        // ---- Frame ----

        void FixedUpdate()
        {
            fixedFrame++;
            if (player == null) { haveLastFeet = false; return; }
            var feet = Feet();
            if (haveLastFeet)
                velocity = Coords.UnityToSkyrim(ToVec3((feet - lastFeet) / Time.fixedDeltaTime));
            lastFeet = feet;
            haveLastFeet = true;
        }

        // LateUpdate: after MSC's scripts moved and turned the player this frame. Messages are
        // drained here, after this frame's input was read, so an anchor Skyrim sent together with
        // the handover is always applied before a pose for it goes out.
        void LateUpdate()
        {
            var link = LinkHost.Current;
            if (link == null) return;
            link.Receive(OnMessage);
            FindPlayer();

            bool owned = link.IsUp && input.OwnsPlayer;
            if (owned != lastOwned)
            {
                lastOwned = owned;
                log(owned ? "player: Skyrim handed the player to MSC" : "player: Skyrim took the player back");
            }

            if (player != null && island.HasAnchor && placedGeneration != island.Generation)
                Settle();
            PruneChunks();

            var state = new PlayerState { frame = fixedFrame };
            if (owned) CloseMenu();

            bool playing = owned && player != null && island.HasAnchor && placedGeneration == island.Generation;
            MotorBoost.Active = playing;
            if (playing) FillPose(ref state);
            state.generation = island.Generation;
            link.WritePlayer(ref state);
        }

        // Getting the mouse out of MSC's window takes Escape, which opens MSC's menu
        // (Systems/Options :: Open Menu sets PlayerInMenu and shows Systems/OptionsMenu) and freezes
        // the player. While Skyrim has handed the player over, close it again the way the menu's
        // Resume button does: through the button's own FSM when it's showing (that also saves the
        // options), otherwise by doing its last state's work directly.
        void CloseMenu()
        {
            var inMenu = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmBool("PlayerInMenu");
            if (inMenu == null || !inMenu.Value || Time.realtimeSinceStartup < nextMenuClose) return;
            nextMenuClose = Time.realtimeSinceStartup + 1f;  // the Resume FSM takes a frame or two

            var systems = GameObject.Find("Systems");
            var menu = systems != null ? systems.transform.Find("OptionsMenu") : null;
            var resume = menu != null ? menu.Find("Menu/Btn_Resume/Button") : null;
            if (resume != null && resume.gameObject.activeInHierarchy)
                foreach (var fsm in resume.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == "Button")
                    {
                        fsm.SendEvent("CLICK");
                        log("player: closed MSC's menu (Resume)");
                        return;
                    }

            inMenu.Value = false;
            if (menu != null) menu.gameObject.SetActive(false);
            log("player: closed MSC's menu");
        }

        void OnMessage(MessageType type, byte[] payload, int size)
        {
            if (type == MessageType.WorldspaceChange && size >= sizeof(WorldspaceChange))
            {
                WorldspaceChange c;
                fixed (byte* p = payload) c = *(WorldspaceChange*)p;
                var anchor = new Vec3(c.spawn[0], c.spawn[1], c.spawn[2]);
                island.SetAnchor(c.generation, c.worldspace, c.cell, anchor, c.spawnHeading);
                log($"player: anchor gen {c.generation} worldspace {c.worldspace:X8} cell {c.cell:X8} at ({anchor.x:0},{anchor.y:0},{anchor.z:0}), rebuilding the island");
                ClearChunks();
            }
            else if (type == MessageType.CollisionChunk)
                OnChunk(payload, size);
        }

        void FindPlayer()
        {
            if (player != null) return;
            var go = GameObject.Find("PLAYER");
            if (go == null) return;
            player = go.transform;
            controller = go.GetComponent<CharacterController>();
            fpsCamera = player.Find("Pivot/AnimPivot/Camera/FPSCamera");
            crouch = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Crouch") crouch = f;
            haveLastFeet = false;
            motor = go.GetComponent("CharacterMotor") as Behaviour;
            motorDisabled = false;
            log($"player: found PLAYER (controller {(controller != null)}, camera {(fpsCamera != null)}, crouch fsm {(crouch != null)})");
            MotorBoost.Install(go, log);
        }

        // ---- Island ----

        Transform IslandRoot()
        {
            if (islandObject == null)
            {
                islandObject = new GameObject("PerkeleRim.Island");
                DontDestroyOnLoad(islandObject);
            }
            return islandObject.transform;
        }

        void OnChunk(byte[] payload, int size)
        {
            if (!island.HasAnchor) return;
            string error;
            var chunk = CollisionChunk.Parse(payload, size, island, out error);
            if (chunk == null) { log("player: bad collision chunk: " + error); return; }
            if (chunk.Generation != island.Generation) return;  // for an older anchor

            var vertices = new Vector3[chunk.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = ToVector3(chunk.Vertices[i]);
            var mesh = new Mesh { name = $"PerkeleRim.Chunk {chunk.ChunkX},{chunk.ChunkY}", vertices = vertices, triangles = chunk.Triangles };
            mesh.RecalculateBounds();

            GameObject go;
            bool rebuilt = chunks.TryGetValue(chunk.Key, out go);
            if (!rebuilt)
            {
                go = new GameObject(mesh.name);
                go.transform.parent = IslandRoot();
                chunks[chunk.Key] = go;
            }
            var collider = go.GetComponent<MeshCollider>();
            if (collider == null) collider = go.AddComponent<MeshCollider>();
            var old = collider.sharedMesh;
            collider.sharedMesh = mesh;
            if (old != null) Destroy(old);
            log($"player: chunk ({chunk.ChunkX},{chunk.ChunkY}) gen {chunk.Generation}: {vertices.Length} vertices, {chunk.Triangles.Length / 3} triangles, {(rebuilt ? "rebuilt" : "new")}");
        }

        void ClearChunks()
        {
            foreach (var go in chunks.Values) DestroyChunk(go);
            chunks.Clear();
        }

        // Destroy only happens at the end of the frame, so take the chunk out of the island (and
        // physics) right away: FindGround must not mistake it for the new anchor's ground.
        static void DestroyChunk(GameObject go)
        {
            go.transform.parent = null;
            var collider = go.GetComponent<MeshCollider>();
            if (collider != null) collider.enabled = false;
            if (collider != null && collider.sharedMesh != null) Destroy(collider.sharedMesh);
            Destroy(go);
        }

        // Drops chunks the player has walked away from, once a second.
        void PruneChunks()
        {
            if (chunks.Count == 0 || player == null || placedGeneration != island.Generation || Time.realtimeSinceStartup < nextPrune) return;
            nextPrune = Time.realtimeSinceStartup + 1f;

            var at = island.IslandToSkyrim(ToVec3(Feet()));
            int cx = CollisionChunk.ChunkIndex(at.x), cy = CollisionChunk.ChunkIndex(at.y);
            var drop = new List<long>();
            foreach (var key in chunks.Keys)
            {
                int x = (int)(key >> 32), y = (int)key;
                if (Math.Max(Math.Abs(x - cx), Math.Abs(y - cy)) > KeepRadius) drop.Add(key);
            }
            foreach (var key in drop)
            {
                DestroyChunk(chunks[key]);
                chunks.Remove(key);
            }
            if (drop.Count > 0) log($"player: dropped {drop.Count} far chunks, {chunks.Count} left around ({cx},{cy})");
        }

        // Puts the player down at the anchor once the ground under it has arrived. Until then it
        // is held there with the motor off, so it doesn't fall.
        void Settle()
        {
            if (holdingGeneration != island.Generation)
            {
                holdingGeneration = island.Generation;
                holdSince = Time.realtimeSinceStartup;
                holdLogged = false;
            }

            var anchor = ToVector3(island.SkyrimToIsland(island.Anchor));
            Vector3 ground;
            bool ready = FindGround(anchor, out ground);
            var target = (ready ? ground : anchor) + Vector3.up * SpawnClearance;
            player.position += target - Feet();
            player.rotation = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(island.AnchorHeading), 0f);
            var body = player.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) body.velocity = Vector3.zero;
            haveLastFeet = false;
            velocity = new Vec3();

            if (!ready)
            {
                SetMotor(false);
                if (!holdLogged && Time.realtimeSinceStartup - holdSince > 3f)
                {
                    holdLogged = true;
                    log($"player: still waiting for the ground at the gen {island.Generation} anchor ({chunks.Count} chunks so far)");
                }
                return;
            }

            SetMotor(true);
            placedGeneration = island.Generation;
            log($"player: placed on the island for gen {island.Generation} after {Time.realtimeSinceStartup - holdSince:0.0} s, feet at ({target.x:0.00},{target.y:0.00},{target.z:0.00})");
        }

        // The island surface closest in height to p, straight above or below it.
        bool FindGround(Vector3 p, out Vector3 ground)
        {
            ground = p;
            if (islandObject == null) return false;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(p + Vector3.up * GroundSearch, Vector3.down, GroundSearch * 2f))
            {
                if (hit.collider.transform.parent != islandObject.transform) continue;
                if (!found || Mathf.Abs(hit.point.y - p.y) < Mathf.Abs(ground.y - p.y)) ground = hit.point;
                found = true;
            }
            return found;
        }

        void SetMotor(bool on)
        {
            if (motor == null) return;
            if (!on && motor.enabled)
            {
                motor.enabled = false;
                motorDisabled = true;
            }
            else if (on && motorDisabled)
            {
                motor.enabled = true;
                motorDisabled = false;
                // Drop whatever speed it had before it was stopped (falling, walking).
                var setVelocity = motor.GetType().GetMethod("SetVelocity", new[] { typeof(Vector3) });
                if (setVelocity != null) setVelocity.Invoke(motor, new object[] { Vector3.zero });
            }
        }

        // ---- Pose ----

        void FillPose(ref PlayerState s)
        {
            s.flags = Wire.PlayerValid;
            var pos = island.IslandToSkyrim(ToVec3(Feet()));
            s.position[0] = pos.x;
            s.position[1] = pos.y;
            s.position[2] = pos.z;
            s.velocity[0] = velocity.x;
            s.velocity[1] = velocity.y;
            s.velocity[2] = velocity.z;
            s.heading = Coords.UnityAngleToSkyrim(player.eulerAngles.y);
            if (fpsCamera != null)
            {
                float pitch = fpsCamera.eulerAngles.x;
                if (pitch > 180f) pitch -= 360f;
                s.pitch = pitch * Mathf.Deg2Rad;
            }
            s.stance = Stance.FromCrouchState(crouch != null ? crouch.ActiveStateName : null);
        }

        // Bottom of the player's capsule, which is what Skyrim's position means.
        Vector3 Feet()
        {
            if (controller == null) return player.position;
            var center = player.TransformPoint(controller.center);
            return center - Vector3.up * (controller.height * 0.5f * player.lossyScale.y);
        }

        // ---- Helpers ----

        static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);
        static Vector3 ToVector3(Vec3 v) => new Vector3(v.x, v.y, v.z);

        void OnDestroy()
        {
            if (InputShim.Feed == FeedInput) InputShim.Feed = null;
        }
    }
}
