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
    //    the player over and the player stands on the current anchor's island, with the camera's
    //    pose (lean, reach, zoom) for Skyrim's first-person camera;
    //  - sends MSC's interaction text (LookAtText) and subtitle line (Subtitle) to Skyrim when they
    //    change, and its needs with every pose;
    //  - runs the vehicles (VehicleLink) on the same island: the van (drawn in Skyrim) on the
    //    player's right, or at the spot the anchor names (the garage), the Jonnez moped (MSC only
    //    so far) on the left;
    //  - hands out the beer Skyrim sends (GiveBeer) through BeerGift;
    //  - reports the physics items around the player (ItemLink), whose models ModelSender sends;
    //  - puts Skyrim's actors near the player on the island as capsules and sends MSC's punches
    //    that land on them (ActorLink);
    //  - shares MSC's first-person layer (hands, drinks, tools in hand) with Skyrim (HandLayerSender);
    //  - keeps MSC's player alive while handed over and sends what it would have died of as
    //    PlayerDamage (DeathGuard), plays MSC's death sound on PlayerDeath, and sends MSC's blood
    //    overlay for Skyrim's death ahead of time;
    //  - keeps MSC's state with Skyrim's saves and puts it back when one loads (SaveSync).
    public unsafe class PlayerLink : MonoBehaviour
    {
        // Drop the player this far above the ground so it never starts inside the collider.
        const float SpawnClearance = 0.05f;
        // Chunks further than this (in chunks, either axis) from the player's are dropped. Skyrim
        // streams 2 around the player and 2 around a point up to 2 ahead of it while driving, and
        // forgets a chunk once it is more than 3 from both, so it always resends what's dropped here.
        const int KeepRadius = 6;
        // Rays looking for the ground under a new anchor start this far above it.
        const float GroundSearch = 50f;
        // How often CatchFall looks, and how far above the feet a surface may be and still count
        // as the one the player stands on.
        const float FallCheckEvery = 0.25f;
        const float FallSlack = 0.3f;
        const float ThrownDistance = 6f;

        readonly Island island = new Island();
        readonly InputFrame input = new InputFrame();
        readonly PunchButton punch = new PunchButton();
        int hitKey = (int)KeyCode.H;  // MSC's Hit binding, read once the player is found
        Action<string> log;
        VehicleLink van, moped;
        VehicleLink[] vehicles;
        BeerGift beer;
        ModelSender models;
        ItemLink items;
        ActorLink actors;
        HandLayerSender hands;
        SaveSync saves;
        Collider asphalt;  // MSC's road (tag Concrete), found by LogSurfaces
        Collider groundTemplate;  // MSC ground the island chunks copy layer, tag and material from

        GameObject islandObject;
        readonly Dictionary<long, GameObject> chunks = new Dictionary<long, GameObject>();
        float nextPrune;
        uint placedGeneration;  // island generation the player was put down for; 0 = none
        uint holdingGeneration;  // generation the player is held for while its ground loads
        float holdSince;
        float nextFallCheck;
        float lastThrownLog = -10f;
        bool holdLogged;
        Behaviour motor;
        bool motorDisabled;
        bool lastOwned;
        float nextMenuClose;

        Transform player;
        Transform fpsCamera;
        Camera viewCamera;  // the camera MSC renders with, below fpsCamera
        string lookAtSent;  // interaction prompt Skyrim last got (flags:text); null = send it again
        string subtitleSent;  // subtitle Skyrim last got; null = send it again
        HutongGames.PlayMaker.FsmFloat[] needs;  // PlayerState.needs globals, in Wire.Need* order

        static readonly string[] NeedGlobals =
        {
            "PlayerThirst", "PlayerHunger", "PlayerStress", "PlayerUrine", "PlayerFatigue", "PlayerDirtiness", "PlayerDrunk",
        };
        CharacterController controller;
        PlayMakerFSM crouch;

        uint fixedFrame;
        CrashSensor crash;
        bool playing;  // as of the last LateUpdate: MSC owns the player and it stands on the current island
        bool haveLastFeet;
        Vector3 lastFeet;
        Vec3 velocity;  // Skyrim units per second

        public void Init(Action<string> log)
        {
            this.log = log;
            InputShim.Feed = FeedInput;
            Func<Vector3, Vector3?> ground = p => { Vector3 g; return FindGround(p, out g) ? g : (Vector3?)null; };
            van = new VehicleLink("HAYOSIKO(1500kg, 250)", "Hayosiko", new Vector3(3.5f, 0f, 2f), Wire.VehicleHayosiko, "PlayerKeyHayosiko", island, ground, log);
            moped = new VehicleLink("JONNEZ ES(Clone)", "Jonnez", new Vector3(-2.5f, 0f, 1.5f), 0, null, island, ground, log);
            vehicles = new[] { van, moped };
            beer = new BeerGift(log);
            models = new ModelSender(this, log);
            items = new ItemLink(island, models, ground, log);
            actors = new ActorLink(island, log);
            hands = new HandLayerSender(log);
            crash = new CrashSensor(log);
            DeathGuard.Install(log);
            saves = new SaveSync(island, vehicles, items, () => playing, () => player, log);
        }

        // Beer as if Skyrim sent GiveBeer (spike command "beer").
        public void GiveBeer(int count) => beer.Give(count);

        // Spike commands "textures" (read an object's model and log it) and "items" (what Skyrim gets).
        public void InspectModel(string name) => models.Inspect(name);
        public void DumpItems() => items.Dump();

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
            if (!input.OwnsPlayer) { punch.Reset(); return; }
            punch.Apply(input.Keys, HandsFree(), hitKey);

            InputShim.Keys.Clear();
            foreach (var k in input.Keys) InputShim.Keys.Add((KeyCode)k);
            InputShim.Axes["Mouse X"] = input.MouseX;
            InputShim.Axes["Mouse Y"] = input.MouseY;
            InputShim.Axes["Mouse ScrollWheel"] = input.MouseWheel;  // radio tuning, among others
            InputShim.Axes["Horizontal"] = input.Horizontal;
            InputShim.Axes["Vertical"] = input.Vertical;
        }

        // ---- Frame ----

        void FixedUpdate()
        {
            fixedFrame++;
            crash.Step(playing && player != null ? Driven() : null);
            if (player == null) { haveLastFeet = false; return; }
            var feet = Feet();
            if (haveLastFeet)
                velocity = Coords.UnityToSkyrim(ToVec3((feet - lastFeet) / Time.fixedDeltaTime));
            lastFeet = feet;
            haveLastFeet = true;
        }

        VehicleLink Driven()
        {
            foreach (var v in vehicles) if (v.Carries(player)) return v;
            return null;
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
            foreach (var v in vehicles) v.Find();
            UseGround();

            bool owned = link.IsUp && input.OwnsPlayer;
            if (owned != lastOwned)
            {
                lastOwned = owned;
                log(owned ? "player: Skyrim handed the player to MSC" : "player: Skyrim took the player back");
                // Test loop: with this flag file next to the game, every handover hands out a case.
                if (owned && System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), TestBeerFlag)))
                    beer.Give(TestBeerBottles);
            }

            if (player != null && island.HasAnchor && placedGeneration != island.Generation)
                Settle();
            PruneChunks();

            var state = new PlayerState { frame = fixedFrame };
            if (owned) CloseMenu();

            playing = owned && player != null && island.HasAnchor && placedGeneration == island.Generation;
            if (playing) CatchFall();
            MotorBoost.Active = playing;
            DeathGuard.Active = owned;
            DeathGuard.Update();
            SendDamage(link);
            // Skyrim has the blood ready before it is needed (shown when the player dies there).
            if (playing) models.IdForBuilt(BloodModel, () => ModelSender.Overlay(BloodModel, BloodTexture, log));
            if (playing) FillPose(ref state);
            state.generation = island.Generation;
            link.WritePlayer(ref state);
            SendLookAt(link, playing);
            SendSubtitle(link, playing);
            foreach (var v in vehicles) v.Update(playing, player, player != null ? Feet() : Vector3.zero);
            if (playing) beer.Update(player, Feet());  // where the player is in Skyrim, not left behind
            items.Update(link, playing, player, player != null ? Feet() : Vector3.zero);
            actors.Update(link, playing, player, playing && Driven() != null, viewCamera != null ? viewCamera.transform : null);
            models.Update(link);
            hands.Update(link, playing, input.ScreenWidth, input.ScreenHeight);
            saves.Update();
        }

        // MSC's blood over the view when the player dies (Systems/Death/PlayerBlood's texture), sent as
        // an overlay model; Skyrim looks it up by this name.
        const string TestBeerFlag = "PerkeleRim_testbeer.txt";
        const int TestBeerBottles = 6;

        const string BloodModel = "PlayerBlood";
        const string BloodTexture = "player_blood_vision";

        // What MSC would have died of goes to Skyrim's health (DeathGuard). A full ring retries next frame.
        void SendDamage(Link link)
        {
            PlayerDamage damage;
            if (!DeathGuard.TakeDamage(out damage)) return;
            var payload = new byte[sizeof(PlayerDamage)];
            fixed (byte* p = payload) *(PlayerDamage*)p = damage;
            if (link.Send(MessageType.PlayerDamage, payload, payload.Length)) DeathGuard.DamageSent();
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
                if ((c.flags & Wire.AnchorVehicleSpot) != 0)
                {
                    var spot = new Vec3(c.vehicleSpot[0], c.vehicleSpot[1], c.vehicleSpot[2]);
                    van.ParkAt(c.generation, spot, c.vehicleHeading);
                    log($"player: the van parks at ({spot.x:0},{spot.y:0},{spot.z:0}) heading {c.vehicleHeading:0.00} for gen {c.generation}");
                }
                ClearChunks();
            }
            else if (type == MessageType.CollisionChunk)
                OnChunk(payload, size);
            else if (type == MessageType.GiveBeer && size >= sizeof(GiveBeer))
            {
                GiveBeer g;
                fixed (byte* p = payload) g = *(GiveBeer*)p;
                beer.Give((int)g.count);
            }
            else if (type == MessageType.PlayerDeath)
                DeathGuard.PlayDeathSound();
            else if (type == MessageType.MscSave || type == MessageType.MscLoad)
                saves.OnMessage(type, payload, size);
        }

        void FindPlayer()
        {
            if (player != null) return;
            var go = GameObject.Find("PLAYER");
            if (go == null) return;
            player = go.transform;
            controller = go.GetComponent<CharacterController>();
            fpsCamera = player.Find("Pivot/AnimPivot/Camera/FPSCamera");
            viewCamera = fpsCamera != null ? fpsCamera.GetComponentInChildren<Camera>() : null;
            crouch = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Crouch") crouch = f;
            haveLastFeet = false;
            motor = go.GetComponent("CharacterMotor") as Behaviour;
            motorDisabled = false;
            ReadHitKey();
            log($"player: found PLAYER (controller {(controller != null)}, camera {(fpsCamera != null)}, view camera {(viewCamera != null ? viewCamera.name : "none")}, crouch fsm {(crouch != null)})");
            LogSurfaces();
            MotorBoost.Install(go, log);
        }

        // For PunchButton: on foot, nothing in the hands, and no MSC prompt for what the crosshair is on.
        static bool HandsFree()
        {
            var g = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            var text = g.FindFsmString("GUIinteraction");
            var use = g.FindFsmBool("GUIuse");
            var left = g.FindFsmBool("PlayerHandLeft");
            var right = g.FindFsmBool("PlayerHandRight");
            var vehicle = g.FindFsmString("PlayerCurrentVehicle");
            return (text == null || string.IsNullOrEmpty(text.Value)) && (use == null || !use.Value) &&
                   (left == null || !left.Value) && (right == null || !right.Value) &&
                   (vehicle == null || string.IsNullOrEmpty(vehicle.Value));
        }

        // The key MSC's Hit action (its punch) is bound to, H unless the player changed it.
        void ReadHitKey()
        {
            try
            {
                for (int i = 0; i < cInput.length; i++)
                    if (cInput.GetText(i, 0) == "Hit")
                        hitKey = (int)(KeyCode)Enum.Parse(typeof(KeyCode), cInput.GetText(i, 1), true);
            }
            catch (Exception e)
            {
                log("player: could not read MSC's Hit binding, punching with H: " + e.Message);
            }
            log($"player: left click with free hands punches (MSC's Hit key {(KeyCode)hitKey})");
        }

        // Every ground surface MSC's map uses (tag + physics material, with friction), once, to pick
        // the one the island copies: the wheels take their grip from it.
        void LogSurfaces()
        {
            var seen = new Dictionary<string, int>();
            var sample = new Dictionary<string, string>();
            foreach (var c in FindObjectsOfType<Collider>())
            {
                if (c.isTrigger || c.attachedRigidbody != null) continue;
                var m = c.sharedMaterial;
                string key = $"tag {c.tag} material {(m != null ? $"{m.name} (dyn {m.dynamicFriction:0.##}, static {m.staticFriction:0.##})" : "none")} layer {LayerMask.LayerToName(c.gameObject.layer)}";
                int n;
                seen.TryGetValue(key, out n);
                seen[key] = n + 1;
                if (n == 0) sample[key] = c.name;
                if (asphalt == null && c.CompareTag("Concrete") && c.gameObject.layer == LayerMask.NameToLayer("Terrain")) asphalt = c;
            }
            foreach (var kv in seen)
                if (kv.Value >= 3) log($"surface: {kv.Key} x{kv.Value} (e.g. {sample[kv.Key]})");
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
            CopyGround(go);
            log($"player: chunk ({chunk.ChunkX},{chunk.ChunkY}) gen {chunk.Generation}: {vertices.Length} vertices, {chunk.Triangles.Length / 3} triangles, {(rebuilt ? "rebuilt" : "new")}");
        }

        // Makes the island look like MSC's own ground to the vehicles, once a vehicle has shown which
        // ground that is: the player walks on anything, but the wheels may only see some layers and
        // take their grip from the surface's material or tag.
        void UseGround()
        {
            // Asphalt grips best: on gravel the van couldn't climb Skyrim's hills. Otherwise whatever
            // the vehicles stand on.
            var ground = asphalt ?? moped.Ground ?? van.Ground;
            if (ground == null || ground == groundTemplate) return;
            groundTemplate = ground;
            foreach (var go in chunks.Values) CopyGround(go);
            log($"player: island chunks now copy {groundTemplate.name} (layer {groundTemplate.gameObject.layer}, tag {groundTemplate.tag}, material {(groundTemplate.sharedMaterial != null ? groundTemplate.sharedMaterial.name : "none")})");
        }

        void CopyGround(GameObject go)
        {
            if (groundTemplate == null) return;
            go.layer = groundTemplate.gameObject.layer;
            go.tag = groundTemplate.tag;
            var collider = go.GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMaterial = groundTemplate.sharedMaterial;
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
            var carrier = Array.Find(vehicles, v => v.Carries(player));
            if (carrier != null)
                carrier.CarryTo(Feet(), ready ? ground : anchor);  // the player sits in it and comes along
            else
            {
                player.position += target - Feet();
                player.rotation = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(island.AnchorHeading), 0f);
            }
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
            bool found = false;
            foreach (var y in IslandSurfaces(p))
            {
                if (!found || Mathf.Abs(y - p.y) < Mathf.Abs(ground.y - p.y)) ground = new Vector3(p.x, y, p.z);
                found = true;
            }
            return found;
        }

        // Heights of every upward-facing island surface within GroundSearch above or below p, top
        // first. A chunk holds the land and the statics in one collider, and RaycastAll reports one
        // hit per collider, so this casts again from just below each hit.
        readonly List<float> surfaces = new List<float>();
        List<float> IslandSurfaces(Vector3 p)
        {
            surfaces.Clear();
            if (islandObject == null) return surfaces;
            var from = p + Vector3.up * GroundSearch;
            float bottom = p.y - GroundSearch;
            RaycastHit hit;
            for (int i = 0; i < 32 && from.y > bottom && Physics.Raycast(from, Vector3.down, out hit, from.y - bottom); i++)
            {
                if (hit.collider.transform.parent == islandObject.transform) surfaces.Add(hit.point.y);
                from = hit.point + Vector3.down * 0.01f;
            }
            return surfaces;
        }

        // Puts the player back on top when it has fallen through the island: no island surface
        // under its feet any more, but one above (stepped out of a vehicle into a rock, whose
        // inside isn't solid, and slipped below the land). The nearest surface above is the land
        // or floor it fell through.
        // A seated player more than ThrownDistance from its vehicle was thrown out of it (a crash
        // flings the seat pivots; DeathGuard puts the ones it knows back) and is checked too.
        void CatchFall()
        {
            if (Time.realtimeSinceStartup < nextFallCheck) return;
            nextFallCheck = Time.realtimeSinceStartup + FallCheckEvery;
            var feet = Feet();
            var carrier = Array.Find(vehicles, v => v.Carries(player));
            if (carrier != null)
            {
                float away = Vector3.Distance(feet, carrier.Position);
                if (away < ThrownDistance) return;
                if (Time.realtimeSinceStartup - lastThrownLog > 1f)
                {
                    lastThrownLog = Time.realtimeSinceStartup;
                    log($"player: seated in the {carrier.Name} but {away:0.0} m from it, path {ItemLink.Path(player)}");
                }
            }
            var heights = IslandSurfaces(feet);
            if (heights.Count == 0 || heights[heights.Count - 1] <= feet.y + FallSlack) return;  // ground below, or no island here
            float above = heights[heights.Count - 1];  // the lowest, all of them above the feet
            player.position += new Vector3(0f, above + SpawnClearance - feet.y, 0f);
            var body = player.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) body.velocity = Vector3.zero;
            haveLastFeet = false;
            log($"player: fell through the island at ({feet.x:0.00},{feet.y:0.00},{feet.z:0.00}), put back on top at {above:0.00}");
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
            if (controller != null && controller.enabled && controller.isGrounded) s.flags |= Wire.PlayerGrounded;  // Skyrim's fall damage
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
            if (viewCamera != null)
            {
                var cam = viewCamera.transform;
                CameraPose.Fill(ref s, ToVec3(cam.position - Feet()), ToVec3(cam.right), ToVec3(cam.up), viewCamera.fieldOfView);
            }
            FillNeeds(ref s);
        }

        // MSC's needs bars (and drunk level) for Skyrim's HUD.
        void FillNeeds(ref PlayerState s)
        {
            if (needs == null)
            {
                var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
                needs = new HutongGames.PlayMaker.FsmFloat[NeedGlobals.Length];
                for (int i = 0; i < needs.Length; i++)
                {
                    needs[i] = globals.FindFsmFloat(NeedGlobals[i]);
                    if (needs[i] == null) log($"player: global {NeedGlobals[i]} not found");
                }
            }
            for (int i = 0; i < needs.Length; i++)
                s.needs[i] = needs[i] != null ? needs[i].Value : 0f;
            s.flags |= Wire.PlayerNeeds;
        }

        // MSC's interaction prompt (globals GUIinteraction: "IGNITION", item names; GUIuse: the hand
        // icon on doors and hatches), sent to Skyrim when it changes, and again from scratch each time
        // MSC gets the player. A full ring retries next frame.
        void SendLookAt(Link link, bool playing)
        {
            if (!playing) { lookAtSent = null; return; }
            var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            var global = globals.FindFsmString("GUIinteraction");
            var use = globals.FindFsmBool("GUIuse");
            var text = global != null && global.Value != null ? global.Value : "";
            uint flags = use != null && use.Value ? Wire.LookAtUse : 0;
            var key = flags + ":" + text;
            if (key == lookAtSent) return;
            var payload = LookAt.Encode(text, flags);
            if (link.Send(MessageType.LookAtText, payload, payload.Length)) lookAtSent = key;
        }

        // MSC's subtitle line (global GUIsubtitle: the player's swearing), same rules as SendLookAt.
        void SendSubtitle(Link link, bool playing)
        {
            if (!playing) { subtitleSent = null; return; }
            var global = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("GUIsubtitle");
            var text = global != null && global.Value != null ? global.Value : "";
            if (text == subtitleSent) return;
            var payload = LookAt.Encode(text);
            if (link.Send(MessageType.Subtitle, payload, payload.Length)) subtitleSent = text;
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
