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
    // game. What MSC would have died of becomes PlayerDamage for Skyrim instead.
    //
    // Vehicle crashes kill through the global DEATH event (FPSCamera :: Death turns it into the
    // Crash death and activates Systems/Death). A Harmony prefix on Fsm.ProcessEvent, which every
    // PlayMaker event goes through, drops DEATH while Active. Other deaths (drowning, wasps, the
    // train) activate Systems/Death directly; the watchdog in Update switches it off again in the
    // same frame and clears the cause its FSM was given.
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
            log($"death: MSC would have died ({what}), Skyrim takes the hit");
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

        static void RememberSeats()
        {
            seats.Clear();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                if (t.name == "DriverHeadPivot" && t.parent != null)
                    seats.Add(new Seat { Pivot = t, Parent = t.parent, Position = t.localPosition, Rotation = t.localRotation, HadBody = t.GetComponent<Rigidbody>() != null });
            log($"death: {seats.Count} driver seats remembered");
        }

        static void PutDriversBack()
        {
            foreach (var s in seats)
            {
                if (s.Pivot == null || s.Parent == null) continue;
                var body = s.Pivot.GetComponent<Rigidbody>();
                bool loose = body != null && !s.HadBody;
                if (s.Pivot.parent == s.Parent && !loose && (s.Pivot.localPosition - s.Position).sqrMagnitude < 0.25f) continue;
                if (loose) UnityEngine.Object.Destroy(body);
                s.Pivot.parent = s.Parent;
                s.Pivot.localPosition = s.Position;
                s.Pivot.localRotation = s.Rotation;
                if (Time.realtimeSinceStartup - lastSeatLog < 1f) continue;  // MSC may keep throwing it; once a second is plenty
                lastSeatLog = Time.realtimeSinceStartup;
                string states = "";
                foreach (var fsm in s.Pivot.GetComponents<PlayMakerFSM>()) states += $" {fsm.FsmName}:{fsm.ActiveStateName}";
                log($"death: the crash threw the driver out of {s.Parent.root.name}, put back in the seat{(loose ? " (its loose body removed)" : "")}; FSMs{states}");
            }
        }

        // Once a frame, after MSC's scripts ran: switches MSC's death off again if it started.
        public static void Update()
        {
            if (Active != wasActive)
            {
                wasActive = Active;
                if (Active) RememberSeats();
            }
            if (!Active)
            {
                pendingSeverity = 0f;  // Skyrim has the player back; an old hit must not land on the next handover
                return;
            }
            PutDriversBack();
            if (death == null)
            {
                var systems = GameObject.Find("Systems");
                var t = systems != null ? systems.transform.Find("Death") : null;
                if (t == null) return;
                death = t.gameObject;
            }
            if (!death.activeSelf) return;
            death.SetActive(false);
            string causes = "";
            foreach (var fsm in death.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Activate Dead Body") continue;
                foreach (var b in fsm.FsmVariables.BoolVariables)
                    if (b.Value) { causes += " " + b.Name; b.Value = false; }
            }
            Hit(Wire.DamageOther, "Systems/Death came on:" + (causes.Length > 0 ? causes : " no cause set"));
        }

        // A hit for Skyrim, if MSC would have died since the last one went out.
        public static bool TakeDamage(out PlayerDamage damage)
        {
            damage = new PlayerDamage { cause = pendingCause, severity = pendingSeverity };
            return pendingSeverity > 0f;
        }

        public static void DamageSent() => pendingSeverity = 0f;

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
