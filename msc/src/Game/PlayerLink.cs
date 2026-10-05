using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The player half of the link, next to LinkHost:
    //  - feeds InputShim from Skyrim's InputState, so MSC's own scripts walk, look and crouch;
    //  - takes the anchor from WorldspaceChange, builds the island collider there and teleports
    //    the player onto it;
    //  - writes PlayerState (Skyrim world space) every frame, valid only while Skyrim has handed
    //    the player over and the player stands on the current anchor's island.
    public unsafe class PlayerLink : MonoBehaviour
    {
        // Placeholder island until Skyrim's collision arrives (milestone 1b): a flat square,
        // IslandSize meters across, centred on Island.Base.
        const float IslandSize = 2000f;
        const int IslandCells = 20;
        // Drop the player this far above the island so it never starts inside the collider.
        const float SpawnClearance = 0.05f;

        readonly Island island = new Island();
        readonly InputFrame input = new InputFrame();
        Action<string> log;

        GameObject islandObject;
        uint placedGeneration;  // island generation the player was teleported for; 0 = none
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
                Teleport();

            var state = new PlayerState { frame = fixedFrame };
            if (owned) CloseMenu();

            bool playing = owned && player != null && island.HasAnchor && placedGeneration == island.Generation;
            MotorBoost.Active = playing;
            if (playing) FillPose(ref state);
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
            if (type != MessageType.WorldspaceChange || size < sizeof(WorldspaceChange)) return;
            WorldspaceChange c;
            fixed (byte* p = payload) c = *(WorldspaceChange*)p;
            var anchor = new Vec3(c.spawn[0], c.spawn[1], c.spawn[2]);
            island.SetAnchor(c.generation, c.worldspace, c.cell, anchor, c.spawnHeading);
            log($"player: anchor gen {c.generation} worldspace {c.worldspace:X8} cell {c.cell:X8} at ({anchor.x:0},{anchor.y:0},{anchor.z:0})");
            EnsureIsland();
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
            log($"player: found PLAYER (controller {(controller != null)}, camera {(fpsCamera != null)}, crouch fsm {(crouch != null)})");
            MotorBoost.Install(go, log);
        }

        // ---- Island ----

        void EnsureIsland()
        {
            if (islandObject != null) return;
            islandObject = new GameObject("PerkeleRim.Island");
            DontDestroyOnLoad(islandObject);
            islandObject.transform.position = ToVector3(Island.Base);
            var mesh = FlatGrid(IslandSize, IslandCells);
            islandObject.AddComponent<MeshCollider>().sharedMesh = mesh;
            log($"player: built flat island {IslandSize:0} m at ({Island.Base.x:0},{Island.Base.y:0},{Island.Base.z:0})");
        }

        // Square grid in the XZ plane, centred on the origin, triangles facing up.
        static Mesh FlatGrid(float size, int cells)
        {
            int n = cells + 1;
            float step = size / cells, half = size / 2f;
            var vertices = new Vector3[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    vertices[i * n + j] = new Vector3(i * step - half, 0f, j * step - half);

            var triangles = new int[cells * cells * 6];
            int t = 0;
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    int a = i * n + j, b = a + 1, c = a + n, d = c + 1;  // b is +z, c is +x
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }

            var mesh = new Mesh { name = "PerkeleRim.IslandFlat", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void Teleport()
        {
            placedGeneration = island.Generation;
            var target = ToVector3(island.SkyrimToIsland(island.Anchor)) + Vector3.up * SpawnClearance;
            player.position += target - Feet();
            player.rotation = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(island.AnchorHeading), 0f);
            var body = player.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic) body.velocity = Vector3.zero;
            haveLastFeet = false;
            velocity = new Vec3();
            log($"player: placed on the island for gen {island.Generation}, feet at ({target.x:0.00},{target.y:0.00},{target.z:0.00})");
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
