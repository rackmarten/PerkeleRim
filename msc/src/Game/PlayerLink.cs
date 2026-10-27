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
    //    pose (lean, reach, zoom) for Skyrim's first-person camera, or MSC's chase camera's
    //    (ChaseCamera, F8 in Skyrim) while that is on;
    //  - sends MSC's interaction text (LookAtText) and subtitle line (Subtitle) to Skyrim when they
    //    change, and its needs with every pose;
    //  - runs the vehicles (VehicleLink) on the same island, all drawn in Skyrim: the van on the
    //    player's right, or at the spot the anchor names (the garage); the Jonnez moped ([) and the
    //    Kekmet tractor (;) only once called, parked in front of the player, then left where they are
    //    (] calls the van the same way, to wherever the player stands); the van and the Kekmet burn
    //    diesel, fill up at Skyrim's dwemer oil pumps for gold and, called dry, are towed onto one
    //    (FuelLink);
    //  - puts what the player carries into Skyrim's inventory and uses it from there (Y, keys 3 to 8),
    //    and keeps MSC's cigarettes and Skyrim's the same (PocketLink);
    //  - reports the physics items around the player (ItemLink), whose models ModelSender sends;
    //  - stocks the garage's shelves with store items once per save (ShelfStocker);
    //  - puts Skyrim's actors near the player on the island as capsules and sends MSC's punches
    //    that land on them (ActorLink); H honks the driven vehicle, scattering them (VehicleHorn);
    //  - shares MSC's first-person layer (hands, drinks, tools in hand) with Skyrim (HandLayerSender);
    //  - keeps MSC's own blackouts (drunk pass-out, knockout) from moving its player while the link is
    //    up (PassOutGuard);
    //  - keeps MSC's player alive while handed over and sends what it would have died of as
    //    PlayerDamage (DeathGuard), plays MSC's death sound on PlayerDeath, and sends MSC's blood
    //    overlay for Skyrim's death ahead of time;
    //  - yells "Perkele!" (or another swear) when the player shouts in Skyrim, grunts or swears when
    //    Skyrim hurts it or the van hits someone (PlayerVoice);
    //  - sends what the player drinks, smokes and eats for Skyrim to heal by (ConsumeWatch);
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
        // On foot, rising faster than LaunchSpeed means the island threw the player (the controller
        // pushed out of a vertical face or the edge of a hole), and CatchLaunch puts it back where it
        // last stood. Measured in game: a sprint jump leaves at 8-13 m/s, a step-up is up to StepUp in
        // one physics step (25 m/s at 50 Hz), throws were 30-130 m/s. CatchFall puts the player back there too
        // when no island surface is left under the feet, if that spot is within GroundedReach.
        const float LaunchSpeed = 30f;
        // How high a step the controller climbs while MSC plays the player for Skyrim: Skyrim's rocks
        // and rubble are lumpier than MSC's ground, and the player got stuck on them. MSC's own comes
        // back when Skyrim takes the player.
        const float StepUp = 0.5f;
        const float GroundedReach = 25f;

        readonly Island island = new Island();
        readonly InputFrame input = new InputFrame();
        readonly PunchButton punch = new PunchButton();
        readonly BlockButton block = new BlockButton();
        readonly ThrowCharge charge = new ThrowCharge();
        readonly ThrowWindUp windUp = new ThrowWindUp();
        readonly KeyRemap remap = new KeyRemap();
        readonly PauseClock clock = new PauseClock();
        ChaseCamera chase;
        DebugView debugView;
        string shotPath;  // F11's PNG for MSC's view (ScreenshotEvent), saved at the end of this LateUpdate
        int hitKey = (int)KeyCode.H;  // MSC's Hit binding, read once the player is found
        int watchKey = (int)KeyCode.U;  // MSC's Watch binding, likewise
        Action<string> log;
        VehicleLink van, moped, kekmet;
        VehicleLink[] vehicles;
        BeerGift beer;  // spike "beer" only
        ShelfStocker shelves;
        FuelLink fuel;
        PocketLink pocket;
        JunkLink junk;
        ModelSender models;
        ItemLink items;
        ParticleLink particles;
        ActorLink actors;
        FingerLink finger;
        PlayerVoice voice;
        VehicleHorn horn;
        bool hornHeld;  // H held while driving, read by FeedInput before it is kept from MSC
        HandLayerSender hands;
        MscMenuLink menu;  // MSC's options menu shown in Skyrim (F1)
        SaveSync saves;
        RestLink rest;
        Collider asphalt;  // MSC's road (tag Concrete), found by LogSurfaces
        Collider groundTemplate;  // MSC ground the island chunks copy layer, tag and material from

        GameObject islandObject;
        readonly Dictionary<long, GameObject> chunks = new Dictionary<long, GameObject>();
        // Draws the chunks, flat-shaded and colored by slope (green flat, red at 60 degrees and
        // steeper, ramps over lips blue, Skyrim's roads bluish green), to compare the island with
        // Skyrim's ground: while MSC has the player (DebugView), or spike "island show|hide".
        public static bool ShowIsland;
        // The ramps over short steps on roads (Fillets): off, they bumped the vehicles more than they
        // helped and stood up as walls in gateways. Spike "island ramps on|off" to compare.
        public static bool RoadRamps;
        static Material islandMaterial;
        // Where each chunk mesh's ramps (Fillets) start in its triangle list; drawn blue.
        static readonly Dictionary<Mesh, int> filletFrom = new Dictionary<Mesh, int>();
        // Each chunk mesh's road triangles (CollisionChunk.Roads), in the chunk's own space.
        static readonly Dictionary<Mesh, float[]> roadsOf = new Dictionary<Mesh, float[]>();
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
        string gearSent;      // gear text Skyrim last got; null = send it again
        HutongGames.PlayMaker.FsmFloat[] needs;  // PlayerState.needs globals, in Wire.Need* order

        static readonly string[] NeedGlobals =
        {
            "PlayerThirst", "PlayerHunger", "PlayerStress", "PlayerUrine", "PlayerFatigue", "PlayerDirtiness", "PlayerDrunk",
        };
        CharacterController controller;
        PlayMakerFSM crouch;
        PlayMakerFSM functions;  // PlayerFunctions (finger, fist, watch), on fpsCamera

        uint fixedFrame;
        CrashSensor crash;
        bool playing;  // as of the last LateUpdate: MSC owns the player and it stands on the current island
        bool haveLastFeet;
        Vector3 lastFeet;
        Vec3 velocity;  // Skyrim units per second
        bool haveGrounded;  // lastGrounded is where the player last stood on foot, on the current island
        Vector3 lastGrounded;
        readonly StepSmoother steps = new StepSmoother();  // the eye's height over stairs (StepWalk)
        float groundedAt;  // Time.time the player last stood on something on foot
        bool haveWalkFeet;
        Vector3 walkFeet;  // feet as of the last LateUpdate, for StepWalk

        public void Init(Action<string> log)
        {
            this.log = log;
            InputShim.Feed = FeedInput;
            Func<Vector3, Vector3?> ground = p => { Vector3 g; return FindGround(p, out g) ? g : (Vector3?)null; };
            van = new VehicleLink("HAYOSIKO(1500kg, 250)", "Hayosiko", new Vector3(3.5f, 0f, 2f), Wire.VehicleHayosiko, false, "PlayerKeyHayosiko", VehicleTune.Van, island, ground, log, new Vector3(0f, 0f, 6f));
            moped = new VehicleLink("JONNEZ ES(Clone)", "Jonnez", new Vector3(0f, 0f, 2.5f), Wire.VehicleJonnez, true, null, VehicleTune.Moped, island, ground, log);
            kekmet = new VehicleLink("KEKMET(350-400psi)", "Kekmet", new Vector3(0f, 0f, 4.5f), Wire.VehicleKekmet, true, null, VehicleTune.Kekmet, island, ground, log);
            vehicles = new[] { van, moped, kekmet };
            beer = new BeerGift(log);
            shelves = new ShelfStocker(island, ground, log);
            moped.EndlessFuel = true;
            fuel = new FuelLink(island, log);
            junk = new JunkLink(island, log);
            pocket = new PocketLink(junk, log);
            models = new ModelSender(this, log);
            items = new ItemLink(island, models, ground, log);
            particles = new ParticleLink(island, models, () => Array.ConvertAll(vehicles, v => v.Car), log);
            actors = new ActorLink(island, charge, log, what => voice.Swear(what));
            finger = new FingerLink(log);
            voice = new PlayerVoice(log);
            horn = new VehicleHorn(log);
            chase = new ChaseCamera(log);
            debugView = new DebugView(SetIslandVisible, log);
            hands =new HandLayerSender(log);
            menu = new MscMenuLink(log);
            crash = new CrashSensor(log);
            DeathGuard.Install(log);
            ConsumeWatch.Install(log);
            saves = new SaveSync(island, vehicles, items, () => playing, () => player, log);
            rest = new RestLink(log);
        }

        // A beer case next to the player (spike command "beer").
        public void GiveBeer(int count) => beer.Give(count);

        // Spike command "pocket <key>": a pocket key press (Y, 3..8).
        public void PressPocketKey(int keyCode) => pocket.Press(keyCode);
        public void HoldPocketKind(int kind) => pocket.Hold(kind);

        // Spike "shout 0|1": what PlayerShout plays (1 = Unrelenting Force).
        public void Yell(uint shout, uint words) =>
            voice.Shout(shout == 0x13E07 ? Wire.ShoutUnrelentingForce : Wire.ShoutOther, shout, words, "spike");

        // Spike "hurt <cause> [damage]": what PlayerHurt plays (Wire.Hurt*: 0 hit, 1 blocked, 2 fall, 3 crash, 4 low).
        public void Hurt(uint cause, float damage) => voice.Hurt(cause, damage, "spike");

        // Spike "intro <cue>": what IntroCue plays (Wire.Intro*: 1 blackout, 2 wake).
        public void Intro(uint cue) => voice.Intro(cue, "spike");

        // Spike commands "textures" (read an object's model and log it) and "items" (what Skyrim gets).
        public void InspectModel(string name) => models.Inspect(name);
        public void DumpItems() => items.Dump();
        public void DumpParticles() => particles.Dump();
        public void PuffParticles(int count) => particles.Puff(count);

        // ---- Input (runs inside the first Input call of each frame) ----

        void FeedInput()
        {
            var link = LinkHost.Current;
            InputState s;
            if (link == null || !link.ReadInput(out s))
            {
                input.Reset();
                InputShim.Active = false;
                Freeze(false);
                return;
            }

            // Keep the mouse totals current even while Skyrim owns the player, so taking over
            // doesn't start with a jump.
            input.Update(ref s);
            // Not in the main menu: MSC loads its game there with its own time, which a Skyrim menu would stop for good.
            Freeze(OptionsMenu.Freezes(input.Paused, input.MscMenu) && Application.loadedLevelName == "GAME");
            InputShim.Active = input.OwnsPlayer;
            InputShim.HasCursor = input.MscMenu;
            if (!input.OwnsPlayer) { punch.Reset(); block.Reset(); charge.Reset(); return; }
            if (input.MscMenu)
            {
                // MSC's menu shown in Skyrim (F1): Skyrim's cursor, and every key and button as they are
                // (clicks, key rebinding), none of the combat, remap or pocket keys.
                float x, y;
                InputFrame.CursorPixels(input.CursorX, input.CursorY, Screen.width, Screen.height, out x, out y);
                InputShim.CursorX = x;
                InputShim.CursorY = y;
                punch.Reset(); block.Reset(); charge.Reset();
                InputShim.Keys.Clear();
                foreach (var k in input.Keys) InputShim.Keys.Add((KeyCode)k);
                InputShim.Axes.Clear();
                InputShim.Axes["Mouse X"] = input.MouseX;  // the sliders drag with it
                InputShim.Axes["Mouse Y"] = input.MouseY;
                return;
            }
            var state = functions != null ? functions.ActiveStateName : null;
            bool free = HandsFree(BlockButton.Watching(state));
            block.Apply(input.Keys, free, watchKey, hitKey, state, Time.realtimeSinceStartup);
            punch.Apply(input.Keys, free, hitKey, Time.realtimeSinceStartup);
            charge.Apply(input.Keys, PocketLink.Carrying(), Time.realtimeSinceStartup);
            bool driving = Driven() != null;
            remap.Apply(input.Keys, driving);
            // Driving, H is the horn (VehicleHorn) and never reaches MSC: as its Hit key it punched in the seat.
            hornHeld = driving && input.Keys.Contains((int)VehicleHorn.Key);
            if (driving) input.Keys.RemoveAll(k => k == (int)VehicleHorn.Key || k == hitKey);
            pocket.Keys(input.Keys);

            // The pocket keys are PerkeleRim's alone.
            InputShim.Keys.Clear();
            foreach (var k in input.Keys) if (!Pocket.IsKey(k)) InputShim.Keys.Add((KeyCode)k);
            InputShim.Axes["Mouse X"] = input.MouseX;
            InputShim.Axes["Mouse Y"] = input.MouseY;
            InputShim.Axes["Mouse ScrollWheel"] = input.MouseWheel;  // radio tuning, among others
            InputShim.Axes["Horizontal"] = input.Horizontal;
            InputShim.Axes["Vertical"] = input.Vertical;
        }

        // Skyrim paused by a menu (Escape, the map): MSC's time and sound stand still until it runs
        // again (a frozen time scale alone leaves the engine and radio playing).
        void Freeze(bool paused)
        {
            bool was = clock.Frozen;
            float scale = clock.Apply(paused, Time.timeScale);
            if (scale != Time.timeScale) Time.timeScale = scale;
            if (clock.Frozen == was) return;
            AudioListener.pause = clock.Frozen;
            log(clock.Frozen ? "player: Skyrim paused, MSC's time and sound frozen" : $"player: Skyrim runs again, time scale {scale:0.##}");
        }

        // ---- Frame ----

        void FixedUpdate()
        {
            fixedFrame++;
            var driven = playing && player != null ? Driven() : null;
            crash.Step(driven);
            if (player == null) { haveLastFeet = false; return; }
            var feet = Feet();
            if (!playing || driven != null) haveGrounded = false;
            else if (CatchLaunch(feet)) return;
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
            voice.Update();
            FindPlayer();
            PassOutGuard.Update(link.IsUp, player, log);
            foreach (var v in vehicles) v.Find();
            UseGround();

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
            menu.Update(owned && input.MscMenu);
            if (owned && !menu.Holding) CloseMenu();  // Skyrim shows it (F1): left open
            chase.Update(owned);
            debugView.Update(owned);

            playing = owned && player != null && island.HasAnchor && placedGeneration == island.Generation;
            if (playing) CatchFall();
            StepWalk();
            MotorBoost.Active = playing;
            var urine = needs != null && needs[(int)Wire.NeedUrine] != null ? needs[(int)Wire.NeedUrine].Value : 0f;
            MotorBoost.NeedScale = Needs.UrineSpeed(urine);  // a full bladder slows Kumpo
            DeathGuard.Active = owned;
            DeathGuard.Update();
            SendDamage(link);
            ConsumeWatch.Active = owned;
            if (!owned) ConsumeWatch.Clear();
            SendConsumed(link);
            // Skyrim has the blood ready before it is needed (shown when the player dies there).
            if (playing) models.IdForBuilt(BloodModel, () => ModelSender.Overlay(BloodModel, BloodTexture, log));
            if (playing) FillPose(ref state);
            if (player != null) state.flags |= Wire.PlayerInGame;  // Skyrim's play mode hands over only then
            if (menu.Open) state.flags |= Wire.PlayerMscMenu;  // Skyrim closes its side when Resume closes it
            state.generation = island.Generation;
            link.WritePlayer(ref state);
            SendLookAt(link, playing);
            SendSubtitle(link, playing);
            SendGear(link, playing);
            foreach (var v in vehicles) v.Update(playing, player, player != null ? Feet() : Vector3.zero, viewCamera != null ? viewCamera.transform : null);
            foreach (var v in vehicles) v.SendModel(models, player);
            if (playing && Driven() == null && cInput.GetKeyDown("Push"))
                foreach (var v in vehicles) if (v.RightIfRolled(player)) break;
            if (playing && Driven() == null)
            {
                if (InputShim.GetKeyDown(KeyCode.LeftBracket)) fuel.Call(moped, player, Feet());
                if (InputShim.GetKeyDown(KeyCode.RightBracket)) fuel.Call(van, player, Feet());
                if (InputShim.GetKeyDown(KeyCode.Semicolon)) fuel.Call(kekmet, player, Feet());
            }
            fuel.Update(link, playing, player != null ? Feet() : Vector3.zero, playing ? Driven() : null, vehicles);
            if (playing) beer.Update(player, Feet());  // where the player is in Skyrim, not left behind
            shelves.Update(playing);
            pocket.Paused = input.Paused;
            junk.Update(link, playing, player);
            pocket.Update(link, playing, player != null);
            windUp.Update(charge, Time.realtimeSinceStartup);
            items.Update(link, playing, player, player != null ? Feet() : Vector3.zero);
            particles.Update(link, playing, player, player != null ? Feet() : Vector3.zero);
            var driven = playing ? Driven() : null;
            // MSC's push stance (J toggles it): PlayerFunctions holds both hands up in its On state.
            bool pushing = functions != null && functions.ActiveStateName == "On";
            actors.Update(link, playing, player, driven != null, driven != null ? driven.Body : null, viewCamera != null ? viewCamera.transform : null,
                pushing, player != null ? Feet() : Vector3.zero);
            finger.Update(link, playing, functions, viewCamera != null ? viewCamera.transform : null);
            horn.Update(link, driven, hornHeld);
            models.Update(link);
            hands.Update(link, playing, input.ScreenWidth, input.ScreenHeight, owned ? menu.Camera : null);
            saves.Update();
            if (shotPath != null)
            {
                ViewShot.Save(shotPath, (int)input.ScreenWidth, (int)input.ScreenHeight, log);
                shotPath = null;
            }
        }

        // MSC's blood over the view when the player dies (Systems/Death/PlayerBlood's texture), sent as
        // an overlay model; Skyrim looks it up by this name.
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

        // What the player drank, smoked or ate goes to Skyrim to heal by. A full ring retries next frame.
        void SendConsumed(Link link)
        {
            uint kind;
            while (ConsumeWatch.Peek(out kind))
            {
                var consumed = new Consumed { kind = kind };
                var payload = new byte[sizeof(Consumed)];
                fixed (byte* p = payload) *(Consumed*)p = consumed;
                if (!link.Send(MessageType.Consumed, payload, payload.Length)) return;
                ConsumeWatch.Sent();
            }
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
            else if (type == MessageType.PocketCounts)
                pocket.OnCounts(payload, size);
            else if (type == MessageType.ConsumeEvent)
                pocket.OnConsume(payload, size);
            else if (type == MessageType.JunkOut)
                junk.OnOut(payload, size);
            else if (type == MessageType.PlayerDeath)
                DeathGuard.PlayDeathSound();
            else if (type == MessageType.PlayerShout)
                voice.OnShout(payload, size);
            else if (type == MessageType.PlayerHurt)
                voice.OnHurt(payload, size);
            else if (type == MessageType.IntroCue)
                voice.OnIntro(payload, size);
            else if (type == MessageType.StockShelves)
                shelves.OnMessage(payload, size);
            else if (type == MessageType.FuelStations)
                fuel.OnStations(payload, size);
            else if (type == MessageType.LoadReset)
                rest.OnLoadReset();
            else if (type == MessageType.PlayerWashed)
                rest.OnWashed();
            else if (type == MessageType.TimePassed)
                rest.OnTimePassed(payload, size);
            else if (type == MessageType.MscSave || type == MessageType.MscLoad)
                saves.OnMessage(type, payload, size);
            else if (type == MessageType.DebugCommand)
            {
                var line = LookAt.Decode(payload, size);
                if (line == null) log($"debug: malformed DebugCommand ({size} bytes)");
#if PERKELERIM_SPIKE
                else Spike.SpikeHost.Run(line);
#endif
            }
            else if (type == MessageType.ScreenshotEvent)
            {
                if (!Screenshot.Decode(payload, size, out shotPath)) log($"shot: malformed ScreenshotEvent ({size} bytes)");
            }
        }

        void FindPlayer()
        {
            if (player != null) return;
            var go = GameObject.Find("PLAYER");
            if (go == null) return;
            player = go.transform;
            controller = go.GetComponent<CharacterController>();
            ownStepOffset = -1f;  // a new controller has its own step height
            fpsCamera = player.Find("Pivot/AnimPivot/Camera/FPSCamera");
            viewCamera = fpsCamera != null ? fpsCamera.GetComponentInChildren<Camera>() : null;
            crouch = null;
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Crouch") crouch = f;
            functions = null;
            if (fpsCamera != null)
                foreach (var f in fpsCamera.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "PlayerFunctions") functions = f;
            haveLastFeet = false;
            haveGrounded = false;
            motor = go.GetComponent("CharacterMotor") as Behaviour;
            motorDisabled = false;
            ReadBindings();
            log($"player: found PLAYER (controller {(controller != null)}, camera {(fpsCamera != null)}, view camera {(viewCamera != null ? viewCamera.name : "none")}, crouch fsm {(crouch != null)})");
            LogSurfaces();
            MotorBoost.Install(go, log);
        }

        // For PunchButton and BlockButton: on foot, nothing in the hands (MSC drops what it carries on
        // a click), no MSC prompt for what the crosshair is on and nothing there MSC would pick up.
        // watching: PlayerFunctions shows the watch, so the busy left hand is the guard itself.
        static bool HandsFree(bool watching)
        {
            var g = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            var text = g.FindFsmString("GUIinteraction");
            var use = g.FindFsmBool("GUIuse");
            var left = g.FindFsmBool("PlayerHandLeft");
            var right = g.FindFsmBool("PlayerHandRight");
            var vehicle = g.FindFsmString("PlayerCurrentVehicle");
            return (text == null || string.IsNullOrEmpty(text.Value)) && (use == null || !use.Value) &&
                   (watching || left == null || !left.Value) && (right == null || !right.Value) &&
                   (vehicle == null || string.IsNullOrEmpty(vehicle.Value)) &&
                   !PocketLink.Carrying() && PocketLink.Aimed() == null;  // an item MSC picks up shows no prompt (empty bottles)
        }

        // The keys MSC's Hit action (its punch), Watch action and the remapped hand actions
        // (KeyRemap) are bound to, MSC's defaults unless the player changed them.
        void ReadBindings()
        {
            try
            {
                for (int i = 0; i < cInput.length; i++)
                {
                    var action = cInput.GetText(i, 0);
                    KeyCode key;
                    try { key = (KeyCode)Enum.Parse(typeof(KeyCode), cInput.GetText(i, 1), true); }
                    catch (ArgumentException) { continue; }  // unbound or not a KeyCode
                    if (action == "Hit") hitKey = (int)key;
                    else if (action == "Watch") watchKey = (int)key;
                    else remap.Bind(action, (int)key);
                }
            }
            catch (Exception e)
            {
                log($"player: could not read MSC's bindings, using {(KeyCode)hitKey} and {(KeyCode)watchKey} for Hit and Watch: " + e.Message);
            }
            log($"player: left click with free hands punches (MSC's Hit key {(KeyCode)hitKey}), right click guards (its Watch key {(KeyCode)watchKey})");
            log($"player: V finger ({(KeyCode)remap.KeyOf("Finger")}), middle click swear ({(KeyCode)remap.KeyOf("Swear")}), C smoke ({(KeyCode)remap.KeyOf("Smoking")}), B zoom ({(KeyCode)remap.KeyOf("Zoom")}); " +
                $"on foot X piss ({(KeyCode)remap.KeyOf("Urinate")}), G push ({(KeyCode)remap.KeyOf("Push")}), O thumb ({(KeyCode)remap.KeyOf("Hitchhike")}), Ctrl crouch ({(KeyCode)remap.KeyOf("Crouch")}); " +
                $"driving Shift gear up ({(KeyCode)remap.KeyOf("ShiftUp")}), Ctrl gear down ({(KeyCode)remap.KeyOf("ShiftDown")})");
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

            var watch = System.Diagnostics.Stopwatch.StartNew();
            int sampled = chunk.Triangles.Length;
            int fillets = RoadRamps ? Fillets.Add(ref chunk.Vertices, ref chunk.Triangles, chunk.Roads) : 0;
            watch.Stop();

            var vertices = new Vector3[chunk.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = ToVector3(chunk.Vertices[i]);
            var mesh = new Mesh { name = $"PerkeleRim.Chunk {chunk.ChunkX},{chunk.ChunkY}", vertices = vertices, triangles = chunk.Triangles };
            mesh.RecalculateBounds();
            filletFrom[mesh] = sampled;
            roadsOf[mesh] = chunk.Roads;

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
            if (old != null) { filletFrom.Remove(old); roadsOf.Remove(old); Destroy(old); }
            CopyGround(go);
            DrawChunk(go);
            log($"player: chunk ({chunk.ChunkX},{chunk.ChunkY}) gen {chunk.Generation}: {vertices.Length} vertices, {chunk.Triangles.Length / 3} triangles ({fillets} ramp), {chunk.Roads.Length / 6} road triangles, ramps took {watch.Elapsed.TotalMilliseconds:0.0} ms, {(rebuilt ? "rebuilt" : "new")}");
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
            if (collider != null && collider.sharedMesh != null) { filletFrom.Remove(collider.sharedMesh); roadsOf.Remove(collider.sharedMesh); Destroy(collider.sharedMesh); }
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Destroy(go);
        }

        // Spike "island why [m]": what the ramp pass (Fillets) made of the steep island triangles
        // within m (2) of where MSC's camera looks.
        public void LogWhy(float radius)
        {
            var cam = Camera.main;
            RaycastHit hit;
            if (cam == null || !Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 30f)
                || hit.collider.transform.parent != IslandRoot())
            {
                log("player: island why: the camera doesn't look at the island");
                return;
            }
            log($"player: island why: looking at {hit.collider.name}, {Vector3.Angle(Vector3.up, hit.normal):0} deg there, {hit.distance:0.0} m away");
            foreach (var go in chunks.Values)
            {
                var collider = go.GetComponent<MeshCollider>();
                if (collider == null || collider.sharedMesh == null) continue;
                var mesh = collider.sharedMesh;
                var at = go.transform.InverseTransformPoint(hit.point);
                if (!mesh.bounds.Intersects(new Bounds(at, Vector3.one * radius * 2))) continue;
                int sampled;
                if (!filletFrom.TryGetValue(mesh, out sampled)) sampled = mesh.triangles.Length;
                var from = mesh.vertices;
                var vertices = new Vec3[from.Length];
                for (int i = 0; i < from.Length; i++) vertices[i] = ToVec3(from[i]);
                var triangles = new int[sampled];
                Array.Copy(mesh.triangles, triangles, sampled);
                int lines = 0;
                float[] roads;
                roadsOf.TryGetValue(mesh, out roads);
                Fillets.Add(ref vertices, ref triangles, roads, (mid, reason) =>
                {
                    var off = ToVector3(mid) - at;
                    if (off.magnitude > radius || ++lines > 30) return;
                    log($"player:   ({off.x:+0.0;-0.0},{off.y:+0.00;-0.00},{off.z:+0.0;-0.0}) {reason} on {go.name}");
                });
            }
        }

        // Spike "island ramps on|off": every chunk rebuilt with or without its road ramps.
        public void SetRoadRamps(bool on)
        {
            RoadRamps = on;
            int added = 0;
            foreach (var go in chunks.Values)
            {
                var collider = go.GetComponent<MeshCollider>();
                if (collider == null || collider.sharedMesh == null) continue;
                var old = collider.sharedMesh;
                int sampled;
                if (!filletFrom.TryGetValue(old, out sampled)) sampled = old.triangles.Length;
                float[] roads;
                roadsOf.TryGetValue(old, out roads);
                var from = old.vertices;
                var vertices = new Vec3[from.Length];
                for (int i = 0; i < from.Length; i++) vertices[i] = ToVec3(from[i]);
                var triangles = new int[sampled];
                Array.Copy(old.triangles, triangles, sampled);
                if (on) added += Fillets.Add(ref vertices, ref triangles, roads);
                var points = new Vector3[vertices.Length];
                for (int i = 0; i < points.Length; i++) points[i] = ToVector3(vertices[i]);
                var mesh = new Mesh { name = old.name, vertices = points, triangles = triangles };
                mesh.RecalculateBounds();
                filletFrom[mesh] = sampled;
                roadsOf[mesh] = roads;
                collider.sharedMesh = mesh;
                filletFrom.Remove(old);
                roadsOf.Remove(old);
                Destroy(old);
                DrawChunk(go);
            }
            log($"player: road ramps {(on ? "on" : "off")}, {chunks.Count} chunks rebuilt, {added} ramp triangles");
        }

        public void SetIslandVisible(bool show)
        {
            ShowIsland = show;
            foreach (var go in chunks.Values) DrawChunk(go);
            log($"player: island {(show ? "shown" : "hidden")}, {chunks.Count} chunks");
        }

        // A render copy of the chunk's collider mesh with no shared vertices, so each triangle is
        // lit and colored on its own and a single steep cell stands out.
        static void DrawChunk(GameObject go)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            var collider = go.GetComponent<MeshCollider>();
            if (!ShowIsland || collider == null || collider.sharedMesh == null)
            {
                if (filter != null) filter.sharedMesh = null;
                return;
            }

            var source = collider.sharedMesh;
            var from = source.vertices;
            var tris = source.triangles;
            int ramps;
            if (!filletFrom.TryGetValue(source, out ramps)) ramps = tris.Length;
            float[] roads;
            roadsOf.TryGetValue(source, out roads);
            var roadColor = new Color(0.1f, 0.85f, 0.7f);
            var vertices = new Vector3[tris.Length];
            var colors = new Color[tris.Length];
            var order = new int[tris.Length];
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = from[tris[i]], b = from[tris[i + 1]], c = from[tris[i + 2]];
                float up = Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y);
                var mid = (a + b + c) / 3f;
                var flat = i < ramps && Fillets.InRoad(roads, mid.x, mid.z) ? roadColor : Color.green;
                var color = i >= ramps ? new Color(0.2f, 0.4f, 1f) : Color.Lerp(Color.red, flat, Mathf.InverseLerp(0.5f, 0.97f, up));
                vertices[i] = a; vertices[i + 1] = b; vertices[i + 2] = c;
                colors[i] = colors[i + 1] = colors[i + 2] = color;
                order[i] = i; order[i + 1] = i + 1; order[i + 2] = i + 2;
            }
            var mesh = new Mesh { name = source.name + " (drawn)", vertices = vertices, colors = colors, triangles = order };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (filter == null) filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            if (islandMaterial == null) islandMaterial = new Material(IslandShaderSource) { name = "PerkeleRim.Island" };
            renderer.sharedMaterial = islandMaterial;
        }

        // Vertex colors, vertex lit, both sides.
        const string IslandShaderSource = @"Shader ""PerkeleRim/Island"" {
    SubShader {
        Pass {
            Material { Diffuse (1,1,1,1) Ambient (1,1,1,1) }
            Lighting On
            ColorMaterial AmbientAndDiffuse
            Cull Off
        }
    }
}";

        // Spike "island steps [radius]": the island triangles within radius (m, default 8) of the
        // player steeper than 30 degrees with their top within 1 m of the feet, steepest first, with
        // the top relative to the feet and their height, so a bridge lip
        // or road hole can be read from the log.
        public void LogSteps(float radius)
        {
            if (player == null) { log("player: island steps: no player"); return; }
            var at = Feet();
            var found = new List<KeyValuePair<float, string>>();
            foreach (var go in chunks.Values)
            {
                var collider = go.GetComponent<MeshCollider>();
                if (collider == null || collider.sharedMesh == null) continue;
                var v = collider.sharedMesh.vertices;
                var t = collider.sharedMesh.triangles;
                var to = go.transform;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = to.TransformPoint(v[t[i]]), b = to.TransformPoint(v[t[i + 1]]), c = to.TransformPoint(v[t[i + 2]]);
                    var mid = (a + b + c) / 3f;
                    if (new Vector2(mid.x - at.x, mid.z - at.z).magnitude > radius) continue;
                    float slope = Vector3.Angle(Vector3.up, Vector3.Cross(b - a, c - a).normalized);
                    if (slope > 90f) slope = 180f - slope;
                    if (slope < 30f) continue;
                    float topY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    float span = topY - Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                    if (Mathf.Abs(topY - at.y) > 1f) continue;  // walls, the bridge's sides
                    var off = mid - at;
                    found.Add(new KeyValuePair<float, string>(slope,
                        $"{slope:0} deg, top {topY - at.y:+0.00;-0.00} m from the feet, {span:0.00} m tall, at ({off.x:0.0},{off.z:0.0}) on {go.name}"));
                }
            }
            found.Sort((x, y) => y.Key.CompareTo(x.Key));
            log($"player: island steps within {radius:0} m: {found.Count} triangles over 30 deg");
            for (int i = 0; i < found.Count && i < 30; i++) log("player:   " + found[i].Value);
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
            haveGrounded = false;
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
            // Standing on something isn't falling, even where the rays below miss the floor (next to
            // the garage's shelves they did, and the player was put on the shed's beams).
            if (carrier == null && controller != null && controller.isGrounded) return;
            // On foot, through a hole in the island or below it: back where the player last stood
            // (the lowest surface above could be a bridge or a rock top far over the hole).
            if (carrier == null && haveGrounded && feet.y < lastGrounded.y - FallSlack && Near(feet, lastGrounded) && !IslandBelow(feet))
            {
                PutBack(feet, lastGrounded);
                log($"player: fell into a hole at ({feet.x:0.00},{feet.y:0.00},{feet.z:0.00}), put back at ({lastGrounded.x:0.00},{lastGrounded.y:0.00},{lastGrounded.z:0.00})");
                return;
            }
            var heights = IslandSurfaces(feet);
            if (heights.Count == 0 || heights[heights.Count - 1] <= feet.y + FallSlack) return;  // ground below, or no island here
            float lowest = heights[heights.Count - 1];  // the lowest, all of them above the feet
            float above = FallRescue.Target(carrier == null, haveGrounded && Near(feet, lastGrounded), lastGrounded.y, feet.y, lowest);
            if (float.IsNaN(above)) return;  // on foot, only a ceiling above: not a floor fallen through
            PutBack(feet, new Vector3(feet.x, above, feet.z));
            log($"player: fell through the island at ({feet.x:0.00},{feet.y:0.00},{feet.z:0.00}), put back on top at {above:0.00}");
        }

        // On foot: snaps the player down onto the step below instead of letting it run off each one
        // into the air (down Skyrim's stairs at MotorBoost's speed it was airborne half the time), and
        // smooths the eye Skyrim's camera hangs at over the steps (StepSmoother). Not while rising (a
        // jump) or more than SnapWindow after the player last stood on something (a real fall).
        const float SnapWindow = 0.25f;
        float ownStepOffset = -1f;  // MSC's own CharacterController.stepOffset while ours is in
        void StepHeight(bool on)
        {
            if (controller == null) return;
            if (on && ownStepOffset < 0f)
            {
                ownStepOffset = controller.stepOffset;
                controller.stepOffset = Mathf.Max(ownStepOffset, Mathf.Min(StepUp, controller.height * 0.5f));
                log($"player: step height {ownStepOffset:0.00} m raised to {controller.stepOffset:0.00}");
            }
            else if (!on && ownStepOffset >= 0f)
            {
                controller.stepOffset = ownStepOffset;
                ownStepOffset = -1f;
            }
        }
        void StepWalk()
        {
            StepHeight(playing);
            if (!playing || controller == null || !controller.enabled || islandObject == null || Array.Find(vehicles, v => v.Carries(player)) != null)
            {
                steps.Reset();
                haveWalkFeet = false;
                return;
            }
            var feet = Feet();
            if (controller.isGrounded) groundedAt = Time.time;
            else if (Time.time - groundedAt < SnapWindow && haveWalkFeet && feet.y <= walkFeet.y)
            {
                RaycastHit hit;
                if (Physics.Raycast(feet + Vector3.up * 0.05f, Vector3.down, out hit, StepSmoother.MaxStep + 0.05f) && hit.collider.transform.parent == islandObject.transform)
                {
                    controller.Move(Vector3.down * (hit.distance - 0.04f));
                    feet = Feet();
                }
            }
            walkFeet = feet;
            haveWalkFeet = true;
            steps.Update(ToVec3(feet), controller.isGrounded, Time.deltaTime);
        }

        // Remembers where the player stands on foot, and puts it back there when it shoots up
        // faster than any jump (see LaunchSpeed). True when it was put back.
        bool CatchLaunch(Vector3 feet)
        {
            if (controller != null && controller.isGrounded)
            {
                lastGrounded = feet;
                haveGrounded = true;
                return false;
            }
            if (!haveLastFeet || !haveGrounded) return false;
            float rise = (feet.y - lastFeet.y) / Time.fixedDeltaTime;
            if (rise < LaunchSpeed || !Near(feet, lastGrounded)) return false;
            PutBack(feet, lastGrounded);
            log($"player: launched up at {rise:0.0} m/s from ({feet.x:0.00},{feet.y:0.00},{feet.z:0.00}), put back at ({lastGrounded.x:0.00},{lastGrounded.y:0.00},{lastGrounded.z:0.00})");
            return true;
        }

        static bool Near(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude < GroundedReach;

        // Any island surface under p, down to HoleDepth: where there is none the player fell into
        // a hole (or off the island) rather than off a ledge.
        const float HoleDepth = 500f;
        bool IslandBelow(Vector3 p)
        {
            if (islandObject == null) return true;
            var from = p + Vector3.up * FallSlack;
            float bottom = p.y - HoleDepth;
            RaycastHit hit;
            for (int i = 0; i < 32 && from.y > bottom && Physics.Raycast(from, Vector3.down, out hit, from.y - bottom); i++)
            {
                if (hit.collider.transform.parent == islandObject.transform) return true;
                from = hit.point + Vector3.down * 0.01f;
            }
            return false;
        }

        // Moves the player's feet to `to`, standing still.
        void PutBack(Vector3 feet, Vector3 to)
        {
            player.position += to + Vector3.up * SpawnClearance - feet;
            var body = player.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) body.velocity = Vector3.zero;
            if (motor != null)
            {
                var setVelocity = motor.GetType().GetMethod("SetVelocity", new[] { typeof(Vector3) });
                if (setVelocity != null) setVelocity.Invoke(motor, new object[] { Vector3.zero });
            }
            haveLastFeet = false;
            velocity = new Vec3();
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
            if (block.Blocking) s.flags |= Wire.PlayerBlocking;  // Skyrim's block
            if (PocketLink.ToolsOut()) s.flags |= Wire.PlayerTools;  // Skyrim shows MSC's tools icon
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
            s.throwCharge = charge.WireCharge(Time.realtimeSinceStartup);  // Skyrim's HUD shows it
            var tv = chase.Active;
            var view = tv != null ? tv : viewCamera;
            if (view != null)
            {
                var cam = view.transform;
                var eye = cam.position - Feet() + (tv == null ? Vector3.up * steps.Offset : Vector3.zero);  // glides over stairs (StepWalk)
                CameraPose.Fill(ref s, ToVec3(eye), ToVec3(cam.right), ToVec3(cam.up), view.fieldOfView);
                if (tv != null) CameraPose.Chase(ref s, cam.eulerAngles.y, cam.eulerAngles.x);
                else CameraPose.View(ref s, cam.eulerAngles.y, cam.eulerAngles.x);
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

        // The gear MSC's GUIGearObject shows bottom right while driving (global GUIgear, set by the
        // vehicle's GearIndicator FSM: "N", "2", "3 HIGH"); nothing on foot.
        void SendGear(Link link, bool playing)
        {
            if (!playing) { gearSent = null; return; }
            string text = "";
            if (Driven() != null)
            {
                var global = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("GUIgear");
                if (global != null && global.Value != null) text = global.Value;
            }
            if (text == gearSent) return;
            var payload = LookAt.Encode(text);
            if (link.Send(MessageType.GearText, payload, payload.Length)) gearSent = text;
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
            Freeze(false);
        }
    }
}
