using System;
using System.Reflection;
using Harmony;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Keeps MSC's player alive while Skyrim has handed it over: Skyrim's health is the only one, and
    // MSC's death (its death screen, a reload, permadeath deleting the save) would break the link's
    // game. What MSC would have died of becomes PlayerDamage for Skyrim instead, except hunger and
    // thirst, which stay at 200 for Skyrim to drain health on (protocol/cpp/needs.h).
    //
    // Vehicle crashes kill through the global DEATH event (FPSCamera :: Death turns it into the
    // Crash death and activates Systems/Death). A Harmony prefix on Fsm.ProcessEvent, which every
    // PlayMaker event goes through, drops DEATH while Active. Other deaths (hunger, thirst, urine,
    // stress, drowning, wasps, the train) activate Systems/Death directly with PlayMaker's
    // ActivateGameObject; a prefix on its OnEnter skips that while Active. It has to be before:
    // Activate Dead Body destroys the player's CharacterController, CharacterMotor and
    // FPSInputController in its first frame, and the player can't be put back together after.
    // The watchdog in Update still switches Systems/Death off should anything else turn it on.
    // A need death leaves Database/PlayerDatabase :: Simulation in a state with no way out, so
    // MSC's needs would stop for good; Update sends it on to Loop.
    public static class DeathGuard
    {
        // One crash sends DEATH to many FSMs, and a wreck can keep sending it while it tumbles.
        const float HitCooldown = 1.5f;

        // Set every frame by PlayerLink: true while Skyrim has handed the player to MSC.
        public static bool Active;

        static bool installed;
        static Action<string> log;
        static float lastHit = -100f;
        static uint pendingCause;
        static float pendingSeverity;
        // A crash MSC survives (CrashSensor), held back FatalWindow seconds: if MSC kills on the same hit,
        // its death replaces it.
        const float FatalWindow = 0.3f;
        static float pendingCrash;
        static float crashAt = -100f;
        static float lastImpact, lastImpactAt = -100f;
        static bool crashTaken;
        static GameObject death;  // Systems/Death, inactive until MSC kills the player

        public static void Install(Action<string> log)
        {
            DeathGuard.log = log;
            if (installed) return;
            installed = true;
            var target = typeof(Fsm).GetMethod("ProcessEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(FsmEvent), typeof(FsmEventData) }, null);
            if (target == null) { log("death: Fsm.ProcessEvent not found, MSC deaths aren't caught"); return; }
            try
            {
                var harmony = HarmonyInstance.Create("PerkeleRim.DeathGuard");
                harmony.Patch(target, new HarmonyMethod(typeof(DeathGuard).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)), null);
                log("death: MSC's DEATH event is caught while Skyrim has handed the player over");
            }
            catch (Exception e) { log("death: patching Fsm.ProcessEvent FAILED: " + e.Message); }
            try
            {
                var harmony = HarmonyInstance.Create("PerkeleRim.DeathGuard.Activate");
                harmony.Patch(typeof(HutongGames.PlayMaker.Actions.ActivateGameObject).GetMethod("OnEnter"),
                    new HarmonyMethod(typeof(DeathGuard).GetMethod(nameof(ActivatePrefix), BindingFlags.Static | BindingFlags.NonPublic)), null);
                log("death: MSC's own deaths (hunger, thirst, drowning, ...) are kept from starting while Skyrim has handed the player over");
            }
            catch (Exception e) { log("death: patching ActivateGameObject FAILED, need deaths will break MSC's player: " + e.Message); }
        }

        static bool ActivatePrefix(HutongGames.PlayMaker.Actions.ActivateGameObject __instance)
        {
            if (!Active || __instance.activate == null || !__instance.activate.Value) return true;
            var target = __instance.Fsm != null ? __instance.Fsm.GetOwnerDefaultTarget(__instance.gameObject) : null;
            if (target == null || target.name != "Death" || target.transform.parent == null || target.transform.parent.name != "Systems") return true;
            var starved = StarvedNeed(__instance.Fsm);
            if (starved != null)
            {
                // Hunger or thirst at 200: Skyrim drains health for as long as it stays there
                // (protocol/cpp/needs.h), so the need goes back to where it was instead of the free
                // reset to 0, and no crash damage.
                TakeCauses(target);
                var need = FsmVariables.GlobalVariables.FindFsmFloat(starved);
                if (need != null) need.Value = Needs.Full;
                if (Time.realtimeSinceStartup - lastStarvedLog > 60f)
                {
                    lastStarvedLog = Time.realtimeSinceStartup;
                    log($"death: {starved} reached MSC's death line, kept at {Needs.Full} for Skyrim's drain instead");
                }
            }
            else
                Hit(Wire.DamageOther, $"{Path(__instance.Fsm)} :: {__instance.Fsm.Name} in {__instance.Fsm.ActiveStateName} would start Systems/Death:{TakeCauses(target)}");
            __instance.Finish();
            return false;
        }

        // Clears the causes a death set on Systems/Death :: Activate Dead Body, and names them.
        static string TakeCauses(GameObject deathObject)
        {
            string causes = "";
            foreach (var fsm in deathObject.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Activate Dead Body") continue;
                foreach (var b in fsm.FsmVariables.BoolVariables)
                    if (b.Value) { causes += " " + b.Name; b.Value = false; }
            }
            return causes.Length > 0 ? causes : " no cause set";
        }

        static float lastStarvedLog = -100f;

        // Simulation's thirst (State 6) and hunger (State 7) deaths: the global they starve on, else null.
        static string StarvedNeed(Fsm fsm)
        {
            if (fsm == null || fsm.Name != "Simulation") return null;
            switch (fsm.ActiveStateName)
            {
                case "State 6": return "PlayerThirst";
                case "State 7": return "PlayerHunger";
                default: return null;
            }
        }

        // Simulation's need deaths (thirst, hunger, urine, stress, burn): each sets its need to 0 and
        // starts Systems/Death, then waits for the game to end.
        static readonly string[] NeedDeathStates = { "State 6", "State 7", "State 9", "State 10", "Burn" };
        static PlayMakerFSM simulation;

        static void ResumeNeeds()
        {
            if (simulation == null)
            {
                var database = GameObject.Find("Database/PlayerDatabase");
                if (database == null) return;
                foreach (var fsm in database.GetComponents<PlayMakerFSM>())
                    if (fsm.FsmName == "Simulation") simulation = fsm;
                if (simulation == null) return;
            }
            var state = simulation.ActiveStateName;
            if (Array.IndexOf(NeedDeathStates, state) < 0) return;
            var loop = simulation.Fsm.GetState("Loop");
            if (loop == null) return;
            SaveSync.SwitchState.Invoke(simulation.Fsm, new object[] { loop });
            log($"death: MSC's needs went on from {state} (a need death's dead end) to Loop");
        }

        static bool Prefix(Fsm __instance, FsmEvent fsmEvent)
        {
            if (!Active || fsmEvent == null || fsmEvent.Name != "DEATH") return true;
            Hit(Wire.DamageCrash, $"DEATH to {__instance.GameObjectName} :: {__instance.Name}");
            // Which of MSC's FSMs take part in a death, for when one of them leaves something half done.
            if (Time.realtimeSinceStartup - lastHit < 0.1f && blockedLogged++ < 20)
                log($"death: kept DEATH from {Path(__instance)} :: {__instance.Name} (in state {__instance.ActiveStateName})");
            return false;
        }

        static int blockedLogged;

        static string Path(Fsm fsm)
        {
            var go = fsm.GameObject;
            if (go == null) return fsm.GameObjectName;
            string path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return path;
        }

        static void Hit(uint cause, string what)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastHit < HitCooldown) return;
            lastHit = now;
            blockedLogged = 0;
            pendingCause = cause;
            pendingSeverity += 1f;
            log($"death: MSC would have died ({what}), Skyrim takes the hit"
                + (now - lastImpactAt < 2f ? $", last impact {lastImpact:0.0} m/s {now - lastImpactAt:0.00} s before" : ""));
            if (now - crashAt < FatalWindow) pendingCrash = 0f;
        }

        // Where each vehicle's DriverHeadPivot (the driver's head and camera, PLAYER inside while
        // seated) belongs. A fatal crash throws the driver out through the windshield before MSC
        // sends DEATH: the pivot leaves the vehicle and falls, with the player in it, through the
        // island. Without the death that follows it stayed out, and every later drive fell through.
        class Seat
        {
            public Transform Pivot, Parent;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool HadBody;
        }

        static readonly System.Collections.Generic.List<Seat> seats = new System.Collections.Generic.List<Seat>();
        static bool wasActive;
        static float lastSeatLog = -10f;

        static readonly System.Collections.Generic.HashSet<Transform> knownSeats = new System.Collections.Generic.HashSet<Transform>();
        static float nextSeatScan;
        const float SeatScanEvery = 2f;

        // Finds driver seats not seen yet: a vehicle's LOD (with its seat) may be inactive at the
        // handover, and FindObjectsOfType skips inactive objects. Remembers each seat's pivots,
        // everything below DriverHeadPivot but the player (CameraPivot, Pivot, ...), and holds its
        // joints.
        static void FindNewSeats()
        {
            nextSeatScan = Time.realtimeSinceStartup + SeatScanEvery;
            foreach (var seatJoint in UnityEngine.Object.FindObjectsOfType<Joint>())  // far fewer than transforms; every seat a crash can throw has one
            {
                var t = seatJoint.transform;
                if (t.name != "DriverHeadPivot" || t.parent == null || !knownSeats.Add(t)) continue;
                int pivots = 0;
                foreach (var c in t.GetComponentsInChildren<Transform>(true))
                    if (!InPlayer(c, t))
                    {
                        seats.Add(new Seat { Pivot = c, Parent = c.parent, Position = c.localPosition, Rotation = c.localRotation, HadBody = c.GetComponent<Rigidbody>() != null });
                        pivots++;
                    }
                string joints = "";
                foreach (var joint in t.GetComponents<Joint>())
                {
                    heldJoints.Add(new HeldJoint { Joint = joint, Vehicle = t.root.name, Force = joint.breakForce, Torque = joint.breakTorque });
                    joints += $", joint held unbreakable (broke at {joint.breakForce}/{joint.breakTorque})";
                }
                log($"death: {t.root.name} driver seat remembered ({pivots} pivots{joints})");
            }
            KeepJointsHeld();
        }

        // Each DriverHeadPivot hangs in its vehicle by a ConfigurableJoint. A hard hit breaks it,
        // and its Death FSM then throws the driver out (DEATH, the dead body set loose); the pivot
        // is a free body from then on, falling through the island or fighting PutDriversBack every
        // frame. While handed over the joints don't break (CrashSensor charges the hit instead).
        class HeldJoint
        {
            public Joint Joint;
            public string Vehicle;
            public float Force, Torque;  // what MSC wants once Skyrim has the player back
            public bool GoneLogged, SetLogged;
        }

        static readonly System.Collections.Generic.List<HeldJoint> heldJoints = new System.Collections.Generic.List<HeldJoint>();

        // Starts or ends the handover: lets go of the held joints (back to what MSC set), then
        // with a handover finds the seats afresh.
        static void HoldSeats(bool hold)
        {
            foreach (var held in heldJoints)
                if (held.Joint != null)
                {
                    held.Joint.breakForce = held.Force;
                    held.Joint.breakTorque = held.Torque;
                }
            heldJoints.Clear();
            seats.Clear();
            knownSeats.Clear();
            if (hold) FindNewSeats();
        }

        // Every frame, after MSC's scripts and before the next physics step: MSC sets the van's seat
        // joint to break at 300 every frame, so the hold goes back on each time, and what MSC set is
        // what the joint gets back when Skyrim takes the player.
        static void KeepJointsHeld()
        {
            foreach (var held in heldJoints)
            {
                if (held.Joint == null)
                {
                    if (!held.GoneLogged) log($"death: the driver seat joint of {held.Vehicle} is gone (broken before the hold?); reload to get it back");
                    held.GoneLogged = true;
                    continue;
                }
                if (held.Joint.breakForce >= Unbreakable && held.Joint.breakTorque >= Unbreakable) continue;
                if (held.Joint.breakForce < Unbreakable) held.Force = held.Joint.breakForce;
                if (held.Joint.breakTorque < Unbreakable) held.Torque = held.Joint.breakTorque;
                if (!held.SetLogged) log($"death: MSC sets the {held.Vehicle} driver seat joint to break at {held.Force} / {held.Torque}, held unbreakable again each frame");
                held.SetLogged = true;
                held.Joint.breakForce = Mathf.Infinity;
                held.Joint.breakTorque = Mathf.Infinity;
            }
        }

        const float Unbreakable = 1e30f;  // PhysX keeps Mathf.Infinity as float.MaxValue

        static bool InPlayer(Transform t, Transform seat)
        {
            for (; t != null && t != seat; t = t.parent)
                if (t.name == "PLAYER") return true;
            return false;
        }

        static void PutDriversBack()
        {
            foreach (var s in seats)
            {
                if (s.Pivot == null || s.Parent == null) continue;
                var body = s.Pivot.GetComponent<Rigidbody>();
                bool loose = body != null && !s.HadBody;
                if (s.Pivot.parent == s.Parent && !loose && (s.Pivot.localPosition - s.Position).sqrMagnitude < 1f) continue;  // a lean moves the inner pivots a little
                if (loose) UnityEngine.Object.Destroy(body);
                else if (body != null && !body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    // With its joint gone (it broke before the hold) physics would throw it again
                    // every frame: kinematic, it rides along in the seat.
                    if (s.Pivot.GetComponent<Joint>() == null)
                    {
                        body.isKinematic = true;
                        log($"death: the {s.Parent.root.name} driver seat ({s.Pivot.name}) lost its joint, it rides in the seat kinematic now");
                    }
                }
                s.Pivot.parent = s.Parent;
                s.Pivot.localPosition = s.Position;
                s.Pivot.localRotation = s.Rotation;
                if (Time.realtimeSinceStartup - lastSeatLog < 1f) continue;  // MSC may keep throwing it; once a second is plenty
                lastSeatLog = Time.realtimeSinceStartup;
                string states = "";
                foreach (var fsm in s.Pivot.GetComponents<PlayMakerFSM>()) states += $" {fsm.FsmName}:{fsm.ActiveStateName}";
                log($"death: the crash threw the driver out of {s.Parent.root.name} ({s.Pivot.name}), put back in the seat{(loose ? " (its loose body removed)" : "")}; FSMs{states}");
            }
        }

        // Once a frame, after MSC's scripts ran: switches MSC's death off again if it started.
        public static void Update()
        {
            if (Active != wasActive)
            {
                wasActive = Active;
                HoldSeats(Active);
            }
            if (!Active)
            {
                pendingSeverity = 0f;  // Skyrim has the player back; an old hit must not land on the next handover
                pendingCrash = 0f;
                return;
            }
            if (Time.realtimeSinceStartup >= nextSeatScan) FindNewSeats();
            KeepJointsHeld();
            PutDriversBack();
            ResumeNeeds();
            if (death == null)
            {
                var systems = GameObject.Find("Systems");
                var t = systems != null ? systems.transform.Find("Death") : null;
                if (t == null) return;
                death = t.gameObject;
            }
            if (!death.activeSelf) return;
            death.SetActive(false);
            Hit(Wire.DamageOther, "Systems/Death came on:" + TakeCauses(death));
        }

        // CrashSensor measured an impact on the driven vehicle (m/s) and what it costs (0 = nothing).
        public static void Impact(float impact, float severity)
        {
            if (!Active) return;
            float now = Time.realtimeSinceStartup;
            lastImpact = impact;
            lastImpactAt = now;
            if (severity <= 0f) return;
            if (now - lastHit < HitCooldown) return;  // the hit MSC just died of, already counted
            pendingCrash = Math.Max(pendingCrash, severity);
            crashAt = now;
        }

        // A hit for Skyrim: what MSC would have died of since the last one went out, plus a crash it
        // survived once MSC had its chance to call it fatal.
        public static bool TakeDamage(out PlayerDamage damage)
        {
            crashTaken = pendingCrash > 0f && Time.realtimeSinceStartup - crashAt >= FatalWindow;
            float severity = pendingSeverity + (crashTaken ? pendingCrash : 0f);
            damage = new PlayerDamage { cause = pendingSeverity > 0f ? pendingCause : Wire.DamageCrash, severity = severity };
            return severity > 0f;
        }

        public static void DamageSent()
        {
            pendingSeverity = 0f;
            if (crashTaken) pendingCrash = 0f;
        }

        static AudioSource voice;  // on MSC's listener, so the grunt is heard wherever the player is

        // The player died in Skyrim (PlayerDeath): MSC's death grunt, one of its MasterAudio group
        // "Death" clips (death01..05), without anything else of MSC's death. Played straight on the
        // listener in 2D: through MasterAudio the group plays at its own object, out of earshot.
        public static void PlayDeathSound()
        {
            var group = GameObject.Find("MasterAudio/Death");
            var clips = group != null ? group.GetComponentsInChildren<AudioSource>(true) : new AudioSource[0];
            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            AudioClip clip = null;
            if (clips.Length > 0) clip = clips[UnityEngine.Random.Range(0, clips.Length)].clip;
            if (clip != null && listener != null)
            {
                if (voice == null || voice.gameObject != listener.gameObject) voice = listener.gameObject.AddComponent<AudioSource>();
                voice.spatialBlend = 0f;
                voice.PlayOneShot(clip, 1f);
                log($"death: the player died in Skyrim, MSC plays {clip.name}");
                return;
            }
            log($"death: no death clip on the listener ({clips.Length} clips, listener {(listener != null)}), trying MasterAudio");
            PlayThroughMasterAudio();
        }

        static void PlayThroughMasterAudio()
        {
            Type masterAudio = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                masterAudio = assembly.GetType("MasterAudio", false);
                if (masterAudio != null) break;
            }
            var play = masterAudio != null
                ? masterAudio.GetMethod("PlaySoundAndForget", BindingFlags.Static | BindingFlags.Public, null,
                    new[] { typeof(string), typeof(float), typeof(float?), typeof(float), typeof(string) }, null)
                : null;
            if (play == null) { log("death: MasterAudio.PlaySoundAndForget not found, no death sound"); return; }
            try
            {
                var played = play.Invoke(null, new object[] { "Death", 1f, null, 0f, null });
                log($"death: the player died in Skyrim, MSC's death sound {(played is bool && !(bool)played ? "did NOT play" : "plays")}");
            }
            catch (Exception e) { log("death: playing the death sound FAILED: " + e.Message); }
        }
    }
}
