using System;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's own drinking for a drink Kumpo had in Skyrim (ConsumeEvent.drink, rules in
    // Link/Consumption.cs, docs/FSM_MAP.md "Drinking"): the player's Drink FSM gets the event for that
    // bottle. DRINKBEER and DRINKVIINA are global transitions, the others work from the idle State 4
    // only; PocketLink sends one only while no drink is going, so all go to the FSM itself.
    // MSC's spirit blinds Kumpo one time in five (a BLIND transition on to its METHANOL event). For a
    // spirit from Skyrim (Skooma) every BLIND transition is pointed at its state's other way out
    // until Restore.
    // With the drink's Skyrim item (ConsumeEvent.form) Kumpo holds that item's own model: while
    // MSC's bottle is in the hand, Update sends Skyrim its pose and where the fist closes on it every
    // frame (HeldDrink), and the hand layer leaves MSC's bottle out (HandLayerSender.Hidden); Skyrim
    // puts its bottle's neck in the fist.
    static unsafe class DrinkShow
    {
        const string DrinkPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Drink";
        const string Blind = "BLIND";

        static FsmTransition[] blocked = new FsmTransition[0];
        static string[] blockedTo = new string[0];

        static uint heldForm;     // the shown drink's Skyrim item, 0 = MSC's own bottle shows
        static Transform bottle;  // MSC's bottle for it, below Drink
        static Renderer[] bottleRenderers = new Renderer[0];
        static Transform[] fingers = new Transform[0];  // the holding hand's finger joints (HeldBottle.Fingers)
        static Transform eye;     // HandCamera, the hand layer's view
        static bool held;         // Skyrim has the model in the hand (the last HeldDrink had a form)
        static readonly byte[] payload = new byte[sizeof(HeldDrink)];

        static PlayMakerFSM Fsm()
        {
            var go = GameObject.Find(DrinkPath);
            if (go == null) return null;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Drink") return fsm;
            return null;
        }

        // Starts MSC's drinking for drink (Wire.Drink*), Kumpo holding form's model (0: MSC's bottle);
        // false when there is no such drink or no FSM.
        public static bool Start(uint drink, uint form, Action<string> log)
        {
            var name = Consumption.DrinkEvent(drink);
            var fsm = Fsm();
            if (name == null || fsm == null) { log($"drink: no Drink FSM or no event for drink {drink}"); return false; }
            if (drink == Wire.DrinkSpirit) BlockBlindness(fsm, log);
            FindBottle(fsm.transform, drink, form, log);
            fsm.SendEvent(name);
            log($"drink: {name} sent (Drink was in {fsm.ActiveStateName})");
            return true;
        }

        static void FindBottle(Transform drinkObject, uint drink, uint form, Action<string> log)
        {
            heldForm = form;
            bottle = null;
            bottleRenderers = new Renderer[0];
            if (form == 0) return;
            var path = HeldBottle.Path(drink);
            var found = path != null ? drinkObject.Find(path) : null;
            var filter = found != null ? found.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
            {
                log($"drink: no bottle {path} below Drink, MSC's shows instead of {form:X8}'s");
                return;
            }
            bottle = found;
            bottleRenderers = bottle.GetComponentsInChildren<Renderer>(true);
            fingers = FindFingers(drinkObject.Find(HeldBottle.Hand(drink) ?? ""));
            log($"drink: Kumpo holds {form:X8}'s own model on {path} ({bottleRenderers.Length} renderers, {fingers.Length} fingers to grip it)");
        }

        static Transform[] FindFingers(Transform hand)
        {
            var found = new System.Collections.Generic.List<Transform>();
            if (hand != null)
                foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                    if (Array.IndexOf(HeldBottle.Fingers, t.name) >= 0) found.Add(t);
            return found.ToArray();
        }

        // Every frame (LateUpdate, MSC's animation done) while a drink is shown, and once after
        // (showing false). Skyrim's model goes where MSC's bottle is while it's in the hand.
        public static void Update(Link link, bool showing, Action<string> log)
        {
            bool inHand = showing && bottle != null && bottle.gameObject.activeInHierarchy && AnyEnabled() && FindEye();
            if (!inHand)
            {
                HandLayerSender.Hidden = null;
                if (held)
                {
                    held = false;
                    Send(link, HeldBottle.None());
                    log("drink: MSC's bottle left the hand, Skyrim drops its model");
                }
                if (!showing) { bottle = null; heldForm = 0; }
                return;
            }
            var bounds = bottle.GetComponent<MeshFilter>().sharedMesh.bounds;
            var c = bounds.center;
            var bottom = eye.InverseTransformPoint(bottle.TransformPoint(new Vector3(c.x, c.y, bounds.min.z)));
            var top = eye.InverseTransformPoint(bottle.TransformPoint(new Vector3(c.x, c.y, bounds.max.z)));
            var side = eye.InverseTransformDirection(bottle.TransformDirection(Vector3.right));
            // The fist: the middle of the fingers' joints; without them, the middle of the bottle.
            var fist = Vector3.zero;
            int counted = 0;
            foreach (var f in fingers)
                if (f != null) { fist += eye.InverseTransformPoint(f.position); counted++; }
            fist = counted > 0 ? fist / counted : (bottom + top) * 0.5f;
            var grip = HeldBottle.Grip(V(bottom), V(top), V(fist));
            HandLayerSender.Hidden = bottleRenderers;
            if (!Send(link, HeldBottle.Message(heldForm, V(bottom), V(top), V(side), grip))) return;
            if (!held) log($"drink: {heldForm:X8}'s model in the hand, base {bottom} top {top} fist {fist} grip ({grip.x:0.000}, {grip.y:0.000}, {grip.z:0.000}) (MSC camera, m)");
            held = true;
        }

        static Vec3 V(Vector3 v) => new Vec3(v.x, v.y, v.z);

        static bool AnyEnabled()
        {
            foreach (var r in bottleRenderers)
                if (r != null && r.enabled) return true;
            return false;
        }

        static bool FindEye()
        {
            if (eye != null) return true;
            var go = GameObject.Find(HandLayerSender.HandCameraPath);
            eye = go != null ? go.transform : null;
            return eye != null;
        }

        static bool Send(Link link, HeldDrink message)
        {
            fixed (byte* p = payload) *(HeldDrink*)p = message;
            return link.Send(MessageType.HeldDrink, payload, payload.Length);
        }

        static void BlockBlindness(PlayMakerFSM fsm, Action<string> log)
        {
            Restore(log);
            var found = new System.Collections.Generic.List<FsmTransition>();
            var to = new System.Collections.Generic.List<string>();
            foreach (var state in fsm.FsmStates)
            {
                string safe = null;
                foreach (var t in state.Transitions ?? new FsmTransition[0])
                    if (t.EventName != Blind && !string.IsNullOrEmpty(t.ToState)) { safe = t.ToState; break; }
                foreach (var t in state.Transitions ?? new FsmTransition[0])
                {
                    if (t.EventName != Blind) continue;
                    if (safe == null) { log($"drink: {state.Name}'s BLIND has no other way out, left alone"); continue; }
                    log($"drink: {state.Name}'s BLIND goes to {safe} instead of {t.ToState} while it's a drink from Skyrim");
                    found.Add(t);
                    to.Add(t.ToState);
                    t.ToState = safe;
                }
            }
            if (found.Count == 0) log("drink: the spirit has no BLIND transition to block (it can still blind)");
            blocked = found.ToArray();
            blockedTo = to.ToArray();
        }

        // The spirit's BLIND transitions back as MSC made them, once a shown drink is down.
        public static void Restore(Action<string> log)
        {
            if (blocked.Length == 0) return;
            for (int i = 0; i < blocked.Length; i++) blocked[i].ToState = blockedTo[i];
            log($"drink: the spirit's BLIND back to MSC's ({blocked.Length})");
            blocked = new FsmTransition[0];
            blockedTo = new string[0];
        }
    }
}
