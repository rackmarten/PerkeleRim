using System;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's blackouts while Skyrim has handed the player over (rules in Link/Blackout.cs). MSC's own
    // Systems/PassOut and Systems/KnockOut stay off (PassOutGuard): their wake-up spot is on MSC's
    // map, which threw Kumpo off Skyrim's. Instead, past PassOut's drunk line or on a knockout the
    // guard ended, MSC puts its player to sleep as PassOut's State 2 does (PlayerSleeps, which starts
    // MSC's Hangover FSM at the drunk level, and PlayerStop) and sends Blackout. Skyrim fades out,
    // moves Kumpo to a random spot in Tamriel (a new anchor, as any teleport) and wakes him with
    // TimePassed kTimeBed | kTimeBlackout: RestLink takes the needs and the clock on by the hours as
    // for a bed, and here he sobers up by MSC's metabolism and gets up again.
    public unsafe class BlackoutLink
    {
        readonly Blackout rules = new Blackout();
        readonly Action<string> log;
        uint unsent;  // a Blackout the ring had no room for, sent again next frame

        public BlackoutLink(Action<string> log)
        {
            this.log = log;
        }

        // Every frame from PlayerLink.
        public void Update(Link link, bool owned, bool hasPlayer)
        {
            float now = Time.realtimeSinceStartup;
            bool knockedOut = PassOutGuard.TakeKnockout();
            if (!hasPlayer) return;
            if (rules.Out && (!owned || rules.TimedOut(now)))
            {
                GetUp();
                rules.Woke(now, false);
                log($"blackout: {(owned ? "Skyrim never woke him" : "Skyrim took the player back")}, he comes to where he is");
                return;
            }
            var drunk = FsmVariables.GlobalVariables.FindFsmFloat("PlayerDrunk");
            uint kind;
            if (rules.Update(owned, drunk != null ? drunk.Value : 0f, knockedOut, now, out kind))
            {
                Sleep(true);
                unsent = kind;
                log($"blackout: {(kind == Wire.BlackoutKnockout ? "knocked out" : $"passed out drunk ({(drunk != null ? drunk.Value : 0f):0.00} over {Blackout.PassOutDrunk})")}, Skyrim wakes him somewhere");
            }
            if (unsent != 0 && Send(link, unsent)) unsent = 0;
        }

        static bool Send(Link link, uint kind)
        {
            var message = new Protocol.Blackout { kind = kind };
            var payload = new byte[sizeof(Protocol.Blackout)];
            fixed (byte* p = payload) *(Protocol.Blackout*)p = message;
            return link.Send(MessageType.Blackout, payload, payload.Length);
        }

        // TimePassed (after RestLink took the needs and the clock on): with kTimeBlackout, Skyrim
        // woke him up where it moved him.
        public void OnTimePassed(byte[] payload, int size)
        {
            if (payload == null || size < sizeof(TimePassed)) return;
            TimePassed t;
            fixed (byte* p = payload) t = *(TimePassed*)p;
            if ((t.flags & Wire.TimeBlackout) == 0) return;
            var globals = FsmVariables.GlobalVariables;
            var adjusted = globals.FindFsmFloat("PlayerDrunkAdjusted");
            var weight = globals.FindFsmFloat("PlayerWeight");
            string sober = "";
            if (adjusted != null && weight != null)
            {
                float before = adjusted.Value;
                adjusted.Value = Blackout.Sober(before, weight.Value, t.hours);
                sober = $", drunk (adjusted) {before:0.00} -> {adjusted.Value:0.00}";
            }
            GetUp();
            bool wasOut = rules.Out;
            rules.Woke(Time.realtimeSinceStartup, true);
            log($"blackout: woke up after {t.hours:0.#} h{sober}{(wasOut ? "" : " (wasn't out here)")}");
        }

        // A load: whatever the save had comes from it (SaveSync); he isn't asleep here any more.
        public void Reset()
        {
            if (rules.Out) GetUp();
            rules.Reset();
            unsent = 0;
        }

        static void GetUp() => Sleep(false);

        // PassOut's State 2 puts him to sleep with these, its State 5 takes them off again.
        static void Sleep(bool on)
        {
            var globals = FsmVariables.GlobalVariables;
            foreach (var flag in new[] { "PlayerSleeps", "PlayerStop" })
            {
                var b = globals.FindFsmBool(flag);
                if (b != null) b.Value = on;
            }
        }
    }
}
