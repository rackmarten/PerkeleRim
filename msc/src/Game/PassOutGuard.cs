using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Keeps MSC's own blackouts (Systems/PassOut and Systems/KnockOut, Link/PassOut.cs) from moving
    // MSC's player while the link is up: Skyrim's player follows MSC's, so MSC's wake-up spot threw
    // Kumpo across Skyrim's map, and with PLAYER hanging from WakePivot the island's placement left
    // him kilometres from the pivot for AnimateWake to swing around. Skyrim keeps the drunk level, so
    // MSC would pass out again on every wake-up. While the link is up both FSMs are switched off
    // (they only move on in Update); one found going anyway (an event, a save from before) is ended
    // here: back to its idle state, PLAYER let go of WakePivot where it is, the blackout's screen
    // off and what it took (volume, mouse look, PlayerStop) given back. A load ends one the same way
    // (RestLink.OnLoadReset). The link going down gives MSC its blackouts back. A knockout ended while
    // the link is up still knocks him out the way Skyrim can show (BlackoutLink).
    public static class PassOutGuard
    {
        const float FindEvery = 1f;

        static Transform systems;
        static float nextFind;
        static readonly PlayMakerFSM[] fsms = new PlayMakerFSM[PassOut.Objects.Length];
        static readonly bool[] switchedOff = new bool[PassOut.Objects.Length];
        static bool knockedOut;

        // A knockout ended while the link was up since the last call.
        public static bool TakeKnockout()
        {
            bool was = knockedOut;
            knockedOut = false;
            return was;
        }

        // Every frame from PlayerLink, once MSC's player is found.
        public static void Update(bool linkUp, Transform player, Action<string> log)
        {
            if (player == null || !Find()) return;
            for (int i = 0; i < fsms.Length; i++)
            {
                var fsm = fsms[i];
                if (fsm == null) continue;
                var obj = PassOut.Objects[i];
                if (linkUp)
                {
                    if (PassOut.Running(obj, fsm.ActiveStateName))
                    {
                        End(fsm, obj, player, log, "while the link is up");
                        if (obj == PassOut.KnockOutObject) knockedOut = true;
                    }
                    if (fsm.enabled)
                    {
                        fsm.enabled = false;
                        switchedOff[i] = true;
                        log($"pass-out: Systems/{obj} :: {PassOut.Fsm} switched off while the link is up");
                    }
                }
                else if (switchedOff[i])
                {
                    fsm.enabled = true;
                    switchedOff[i] = false;
                    log($"pass-out: Systems/{obj} :: {PassOut.Fsm} back on, the link is down");
                }
            }
        }

        // Ends a blackout either FSM is in, or PLAYER still hanging from its WakePivot. For a load.
        public static int EndAll(Transform player, Action<string> log, string why)
        {
            if (player == null || !Find()) return 0;
            int ended = 0;
            for (int i = 0; i < fsms.Length; i++)
            {
                var fsm = fsms[i];
                if (fsm == null) continue;
                var pivot = Variable(fsm, PassOut.WakePivot);
                bool hanging = pivot != null && player.parent == pivot.transform;
                if (!PassOut.Running(PassOut.Objects[i], fsm.ActiveStateName) && !hanging) continue;
                End(fsm, PassOut.Objects[i], player, log, why);
                ended++;
            }
            return ended;
        }

        static void End(PlayMakerFSM fsm, string obj, Transform player, Action<string> log, string why)
        {
            string from = fsm.ActiveStateName;
            var idle = fsm.Fsm.GetState(PassOut.Idle(obj));
            if (idle != null && from != idle.Name) SaveSync.SwitchState.Invoke(fsm.Fsm, new object[] { idle });

            // Let go of WakePivot where he is, standing (AnimateWake would have turned him up).
            var pivot = Variable(fsm, PassOut.WakePivot);
            bool freed = pivot != null && player.parent == pivot.transform;
            if (freed)
            {
                player.SetParent(null, true);
                player.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            }

            // Pass out switched the blackout's screen on (AnimateWake turns it off again).
            int screens = 0;
            var passOut = fsm.Fsm.GetState(PassOut.PassOutState);
            if (passOut != null && passOut.Actions != null)
                foreach (var action in passOut.Actions)
                {
                    var activate = action as HutongGames.PlayMaker.Actions.ActivateGameObject;
                    if (activate == null || activate.activate == null || !activate.activate.Value) continue;
                    var target = fsm.Fsm.GetOwnerDefaultTarget(activate.gameObject);
                    if (target == null || !target.activeSelf) continue;
                    target.SetActive(false);
                    screens++;
                }

            // What Pass out, Move and State 2 took, as State 5 and Wake up give it back.
            foreach (var name in new[] { PassOut.SavePlayer, PassOut.SavePlayerCam })
            {
                var go = Variable(fsm, name);
                var look = go != null ? go.GetComponent("MouseLook") as Behaviour : null;
                if (look != null) look.enabled = true;
            }
            var globals = FsmVariables.GlobalVariables;
            var volume = globals.FindFsmFloat("GameVolume");
            if (volume != null && volume.Value < 1f) volume.Value = 1f;
            foreach (var flag in new[] { "PlayerStop", "PlayerSleeps" })
            {
                var b = globals.FindFsmBool(flag);
                if (b != null) b.Value = false;
            }
            log($"pass-out: Systems/{obj} :: {PassOut.Fsm} ended in {from} ({why}), back to {PassOut.Idle(obj)}" +
                $"{(freed ? ", PLAYER let go of WakePivot" : "")}{(screens > 0 ? ", blackout screen off" : "")}");
        }

        static GameObject Variable(PlayMakerFSM fsm, string name)
        {
            var v = fsm.FsmVariables.FindFsmGameObject(name);
            return v != null ? v.Value : null;
        }

        // Systems/PassOut and Systems/KnockOut may be inactive, so they're found under Systems.
        static bool Find()
        {
            bool all = systems != null;
            for (int i = 0; all && i < fsms.Length; i++) all = fsms[i] != null;
            if (all || Time.realtimeSinceStartup < nextFind) return systems != null;
            nextFind = Time.realtimeSinceStartup + FindEvery;
            if (systems == null)
            {
                var go = GameObject.Find("Systems");
                if (go == null) return false;
                systems = go.transform;
                for (int i = 0; i < fsms.Length; i++) switchedOff[i] = false;
            }
            for (int i = 0; i < fsms.Length; i++)
            {
                if (fsms[i] != null) continue;
                var t = systems.Find(PassOut.Objects[i]);
                if (t == null) continue;
                foreach (var f in t.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == PassOut.Fsm) fsms[i] = f;
            }
            return true;
        }
    }
}
