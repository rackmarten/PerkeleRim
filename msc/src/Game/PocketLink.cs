using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The pocket on MSC's side (rules in Link/Pocket.cs, docs/PROTOCOL.md "The pocket"): while
    // handed over, Y puts what the player carries (or, hands empty, what the crosshair is on) into
    // Skyrim's inventory and keys 3 to 8 use what's in it. The hand is MSC's pick-up FSM
    // (1Hand_Assemble/Hand :: PickUp): a carried item is its PickedObject in "Part picked" (store
    // items are tagged PART) or "Item picked".
    //  - pocketed: a beer case gives one bottle (the case is thrown away with its last one), food
    //    goes the way MSC's own garbage does (its Use FSM's GARBAGE: never loaded again), an empty
    //    bottle is destroyed;
    //  - used: a beer is MSC's own beer drinking (DRINKBEER: the animation, then the empty bottle
    //    MSC throws goes back into the pocket instead, to be thrown at someone), food a new one from the store's spawner eaten at once (its Use FSM put in Eat);
    //  - taken out: an empty bottle, a copy of the one MSC throws after a beer (BottleBeerFly), put
    //    in the hand the way a click on it would.
    // Also keeps MSC's cigarette count (PlayerCigarettes) and Skyrim's Cigarette items the same
    // (CigaretteMirror), while the link is up.
    public unsafe class PocketLink
    {
        const string HandPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand";
        const string FunctionsPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera";  // :: PlayerFunctions (smoking, gestures)
        const float DrinkTime = 7f;  // a beer case keeps PlayerFunctions off this long while its beer is drunk
        const string SpawnerPath = "Spawner/CreateItems";
        const string BottleTemplate = "BottleBeerFly";
        const int CaseBottles = 24;
        const float TakeTimeout = 5f;  // seconds for a new item to load and land in the hand
        const float FreshFor = 3f;     // the spawner copies a shop bag spot's (spoiled) condition for a moment
        const float HoldAhead = 0.75f, HoldBelow = 0.15f;  // where a new item waits, from the camera (MSC picks up within 1 m)

        readonly Action<string> log;
        readonly PocketLedger ledger = new PocketLedger(NewSession());
        readonly CigaretteMirror cigarettes = new CigaretteMirror();
        readonly Queue<int> presses = new Queue<int>();
        readonly HashSet<int> keysDown = new HashSet<int>();
        bool wasUp;
        static PlayMakerFSM pickUp;
        PlayMakerFSM functions;  // off while a beer from the pocket is drunk
        float functionsBackAt;
        FsmInt cigaretteGlobal;

        // The item being taken out, until it is in the hand.
        GameObject taking;
        int takingKind = Pocket.None;
        PlayMakerFSM takingUse;  // its Use FSM (null for an empty bottle)
        bool trimmed;            // a new beer case is down to one bottle (spike "hold 0")
        bool eating;             // eaten as soon as it has loaded, never held
        float takingSince;

        // A beer from the pocket is being drunk: the empty bottle MSC throws after it (a new copy of
        // BottleBeerFly) goes back into the pocket, until emptyUntil (-1 = none).
        const float EmptyWait = DrinkTime + 3f;
        float emptyUntil = -1f, nextEmptyLook;
        readonly HashSet<int> bottlesBefore = new HashSet<int>();

        public PocketLink(Action<string> log) { this.log = log; }

        static uint NewSession() => (uint)UnityEngine.Random.Range(1, int.MaxValue);

        // Skyrim's keys this frame (InputFrame.Keys, while MSC has the player); presses of the
        // pocket keys are handled in Update.
        public void Keys(List<int> held)
        {
            foreach (var k in held)
                if (Pocket.IsKey(k) && keysDown.Add(k)) presses.Enqueue(k);
            keysDown.RemoveWhere(k => !held.Contains(k));
        }

        // Spike command "pocket <key>": as if Skyrim's key went down.
        public void Press(int keyCode) => presses.Enqueue(keyCode);

        // Spike command "hold <kind>": a new item of a kind into the hand, the pocket untouched.
        public void Hold(int kind) { if (taking == null && takingKind == Pocket.None) StartTake(null, kind, false); }

        public void OnCounts(byte[] payload, int size)
        {
            if (size < sizeof(PocketCounts)) return;
            PocketCounts c;
            fixed (byte* p = payload) c = *(PocketCounts*)p;
            var counts = new uint[Wire.PocketKinds];
            for (int i = 0; i < counts.Length; i++) counts[i] = c.counts[i];
            bool first = !ledger.Known;
            ledger.OnCounts(c.session, c.appliedSeq, counts);
            if (first) log($"pocket: Skyrim has {string.Join(", ", Array.ConvertAll(counts, n => n.ToString()))} (beer, bottles, sausages, pizza, chips, macaron boxes, cigarettes)");
        }

        // Once a frame. playing: MSC has the player on the current island; inGame: MSC's PLAYER exists.
        public void Update(Link link, bool playing, bool inGame)
        {
            if (functions != null && Time.realtimeSinceStartup >= functionsBackAt)
            {
                functions.enabled = true;
                functions = null;
            }
            if (link.IsUp != wasUp)
            {
                wasUp = link.IsUp;
                if (wasUp)
                {
                    ledger.Reset(NewSession());
                    cigarettes.Reset();
                }
            }
            if (!link.IsUp) { presses.Clear(); return; }
            if (inGame) MirrorCigarettes(link);
            if (emptyUntil >= 0f) CatchEmpty(link);
            if (taking != null || takingKind != Pocket.None) { AdvanceTake(); presses.Clear(); return; }
            if (!playing) { presses.Clear(); return; }
            if (ledger.Known) TopUp(link);
            if (presses.Count > 0) Press(link, presses.Dequeue());
        }

        bool Send(Link link, int kind, int delta)
        {
            var change = ledger.Next(kind, delta);
            var payload = new byte[sizeof(PocketChange)];
            fixed (byte* p = payload) *(PocketChange*)p = change;
            if (!link.Send(MessageType.PocketChange, payload, payload.Length)) return false;
            ledger.Sent(change);
            return true;
        }

        // Endless supplies: what was used, thrown or put away comes back. Available counts the changes
        // Skyrim hasn't applied yet, so nothing is sent twice; a full ring retries next frame.
        void TopUp(Link link)
        {
            for (int kind = 0; kind < Wire.PocketKinds; kind++)
            {
                long add = Pocket.Missing(kind, ledger.Available(kind));
                if (add > 0 && Send(link, kind, (int)add)) log($"pocket: topped up, +{add} of kind {kind}");
            }
        }

        void MirrorCigarettes(Link link)
        {
            if (!ledger.Known) return;
            if (cigaretteGlobal == null) cigaretteGlobal = FsmVariables.GlobalVariables.FindFsmInt("PlayerCigarettes");
            if (cigaretteGlobal == null) return;
            bool set;
            long value;
            int delta = cigarettes.Step(cigaretteGlobal.Value, ledger.Available((int)Wire.PocketCigarettes), out set, out value);
            if (set)
            {
                log($"pocket: cigarettes {cigaretteGlobal.Value} -> {value} from Skyrim");
                cigaretteGlobal.Value = (int)value;
            }
            if (delta == 0) return;
            if (Send(link, (int)Wire.PocketCigarettes, delta)) log($"pocket: cigarettes {delta:+0;-0} to Skyrim (MSC has {cigaretteGlobal.Value})");
            else cigarettes.Unsent(delta);
        }

        // ---- Keys ----

        // MSC's hand carries something (for PunchButton: a left click drops it).
        public static bool Carrying()
        {
            var hand = PickUp();
            return hand != null && Holding(hand.ActiveStateName);
        }

        internal static PlayMakerFSM PickUp()
        {
            if (pickUp != null) return pickUp;
            var hand = GameObject.Find(HandPath);
            if (hand != null)
                foreach (var f in hand.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "PickUp") pickUp = f;
            return pickUp;
        }

        static bool Holding(string state) => state == "Part picked" || state == "Item picked";

        static bool Global(string name)
        {
            var b = FsmVariables.GlobalVariables.FindFsmBool(name);
            return b != null && b.Value;
        }

        // Driving or riding: only eating and drinking (Pocket.WorksInVehicle).
        static bool InVehicle()
        {
            var vehicle = FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
            return vehicle != null && !string.IsNullOrEmpty(vehicle.Value);
        }

        // What MSC's hand would pick up on a click: its MousePick, 1 m through the crosshair on the
        // item layers (19, 15, 9). Null for nothing, or something MSC doesn't pick up.
        public static GameObject Aimed()
        {
            var cam = Camera.main;
            RaycastHit hit;
            if (cam == null || !Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 1f, (1 << 19) | (1 << 15) | (1 << 9)))
                return null;
            var go = hit.collider.gameObject;
            if (hit.collider.attachedRigidbody == null) return null;
            return go.CompareTag("PART") || go.CompareTag("ITEM") || go.CompareTag("TOOL") || go.CompareTag("RAGDOLL") ? go : null;
        }

        static int KindOfItem(GameObject go)
        {
            int kind = go != null ? Pocket.KindOf(go.name) : Pocket.None;
            return kind == (int)Wire.PocketBeer && Bottles(go) <= 0 ? Pocket.None : kind;
        }

        void Press(Link link, int key)
        {
            var hand = PickUp();
            if (hand == null) { log("pocket: no pick-up hand (" + HandPath + ")"); return; }
            string state = hand.ActiveStateName;
            bool holding = Holding(state);
            // As MSC's own checks: a tool, ax or flashlight out keeps the pick-up hand busy; smoking
            // doesn't. Seated, the hand doesn't matter: only beer and food work there.
            bool inVehicle = InVehicle();
            if (inVehicle ? !Pocket.WorksInVehicle(key) : !holding && state != "Look for object")
            {
                log($"pocket: {(inVehicle ? "in a vehicle" : "hands busy (" + state + ")")}, key {(KeyCode)key} ignored");
                return;
            }
            var held = holding ? hand.FsmVariables.FindFsmGameObject("PickedObject").Value : null;
            int heldKind = KindOfItem(held);
            var aimed = holding ? null : Aimed();
            int aimedKind = KindOfItem(aimed);
            int kind = Pocket.KindOfSlot(Pocket.SlotOfKey(key));
            var action = Pocket.Decide(key, heldKind, holding, aimedKind, kind != Pocket.None ? ledger.Available(kind) : 0);
            switch (action)
            {
                case Pocket.Action.Pocket:
                    PocketItem(link, held, heldKind, true);
                    break;
                case Pocket.Action.PocketAimed:
                    PocketItem(link, aimed, aimedKind, false);
                    break;
                case Pocket.Action.Consume:
                    if (kind == (int)Wire.PocketBeer) DrinkBeer(link);
                    else StartTake(link, kind, true);
                    break;
                case Pocket.Action.Take:
                    StartTake(link, kind, false);
                    break;
                case Pocket.Action.PocketAndTake:
                    // A case with more than the one bottle taken out is put down, not emptied bottle by bottle.
                    if (heldKind == (int)Wire.PocketBeer && Bottles(held) > 1) Drop(held);
                    else if (!PocketItem(link, held, heldKind, true)) return;
                    StartTake(link, kind, false);
                    break;
                case Pocket.Action.DropAndTake:
                    Drop(held);
                    StartTake(link, kind, false);
                    break;
                default:
                    if (kind != Pocket.None || key == Pocket.PocketKey)
                        log($"pocket: key {(KeyCode)key} does nothing (holding {(held != null ? held.name : "nothing")}, {(kind != Pocket.None ? ledger.Available(kind) : 0)} in slot)");
                    break;
            }
        }

        // ---- Into the pocket ----

        static PlayMakerFSM UseFsm(GameObject go)
        {
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Use") return f;
            return null;
        }

        static int Bottles(GameObject go) => go != null ? BeerGift.Bottles(go.transform) : -1;

        void Drop(GameObject held)
        {
            var hand = PickUp();
            if (hand != null && held != null) hand.SendEvent("DROP_PART");  // global: lets go like a left click
        }

        // An item in the hand (inHand) or the one the crosshair is on into the pocket.
        bool PocketItem(Link link, GameObject held, int kind, bool inHand)
        {
            if (!Send(link, kind, 1)) { log("pocket: the link is full, nothing pocketed"); return false; }
            if (kind == (int)Wire.PocketBeer)
            {
                var box = held.transform;
                var bottle = box.GetChild(box.childCount - 1);
                bottle.parent = null;  // Destroy waits for the frame's end; the case counts its children before that
                UnityEngine.Object.Destroy(bottle.gameObject);
                var use = UseFsm(held);
                var destroyed = use != null ? use.FsmVariables.FindFsmInt("DestroyedBottles") : null;
                if (destroyed != null) destroyed.Value++;
                if (box.childCount == 0)
                {
                    if (inHand) Drop(held);
                    if (use != null) use.SendEvent("GARBAGE");
                    log("pocket: the case's last beer pocketed, the empty case thrown away");
                }
                else log($"pocket: a beer pocketed, {box.childCount} left in the case");
                return true;
            }
            if (inHand) Drop(held);
            var itemUse = UseFsm(held);
            if (itemUse != null) itemUse.SendEvent("GARBAGE");  // food: gone, and never loaded again
            else UnityEngine.Object.Destroy(held);              // an empty bottle isn't saved
            log($"pocket: {held.name} pocketed");
            return true;
        }

        // ---- Out of the pocket ----

        // MSC's own beer drinking, as from a case (beer case :: Use sends DRINKBEER): the animation,
        // the needs, then the empty bottle thrown. Not while a drink is going (it would cut it short).
        void DrinkBeer(Link link)
        {
            // A beer case checks the left hand (busy drinking, pushing, giving the finger) and the helmet.
            if (Global("PlayerDrinkOn") || Global("PlayerHandLeft") || Global("PlayerHelmet")) { log("pocket: can't drink now (left hand busy or helmet on)"); return; }
            if (!Send(link, (int)Wire.PocketBeer, -1)) { log("pocket: the link is full, no beer"); return; }
            bottlesBefore.Clear();
            foreach (var go in EmptyBottles()) bottlesBefore.Add(go.GetInstanceID());
            emptyUntil = Time.realtimeSinceStartup + EmptyWait;
            PlayMakerFSM.BroadcastEvent("DRINKBEER");
            // As the case does: no smoking or gestures until the beer is down.
            var camera = GameObject.Find(FunctionsPath);
            if (camera != null)
                foreach (var f in camera.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "PlayerFunctions") { functions = f; f.enabled = false; functionsBackAt = Time.realtimeSinceStartup + DrinkTime; }
            log($"pocket: drinking a beer ({ledger.Available((int)Wire.PocketBeer)} left)");
        }

        // MSC's empty beer bottles in the world: copies of BottleBeerFly, renamed by their Go FSM.
        static List<GameObject> EmptyBottles()
        {
            var list = new List<GameObject>();
            foreach (Rigidbody body in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
            {
                var go = body.gameObject;
                if (go.activeInHierarchy && (go.name == "empty bottle(Clone)" || go.name == BottleTemplate + "(Clone)")) list.Add(go);
            }
            return list;
        }

        // The pocket beer's empty bottle, once MSC has thrown it: into the pocket instead of the
        // ground. None turns up when the drink was cut short.
        void CatchEmpty(Link link)
        {
            float now = Time.realtimeSinceStartup;
            if (now > emptyUntil) { emptyUntil = -1f; log("pocket: no empty bottle after the beer (the drink was cut short?)"); return; }
            if (now < nextEmptyLook) return;
            nextEmptyLook = now + 0.1f;
            foreach (var go in EmptyBottles())
            {
                if (bottlesBefore.Contains(go.GetInstanceID())) continue;
                if (!Send(link, (int)Wire.PocketBottle, 1)) return;  // the link is full: next look
                UnityEngine.Object.Destroy(go);
                emptyUntil = -1f;
                log($"pocket: the beer's empty bottle went into the pocket ({ledger.Available((int)Wire.PocketBottle)} empty)");
                return;
            }
        }

        void StartTake(Link link, int kind, bool eat)
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (eat && Global("PlayerHelmet")) { log("pocket: can't eat with the helmet on"); return; }  // Eat would refuse
            if (link != null && !Send(link, kind, -1)) { log("pocket: the link is full, nothing taken out"); return; }
            var made = Make(kind);
            if (made == null)
            {
                log($"pocket: kind {kind} couldn't be made, back into the pocket");
                if (link != null && !Send(link, kind, 1)) log("pocket: and the link is full: one lost");
                return;
            }
            taking = made;
            takingKind = kind;
            takingUse = UseFsm(made);
            trimmed = kind != (int)Wire.PocketBeer;
            eating = eat && takingUse != null;
            if (eating)  // never seen: eaten as soon as it has loaded
                foreach (var r in made.GetComponentsInChildren<Renderer>()) r.enabled = false;
            takingSince = Time.realtimeSinceStartup;
            PlaceAhead(made, cam.transform);
            log($"pocket: {(eating ? "eating" : "taking out")} {made.name}");
        }

        GameObject Make(int kind)
        {
            if (kind == (int)Wire.PocketBottle)
            {
                GameObject template = null;
                foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                    if (go.name == BottleTemplate && go.transform.parent == null && go.GetComponent<Rigidbody>() != null) { template = go; break; }
                if (template == null) return null;
                var bottle = (GameObject)UnityEngine.Object.Instantiate(template);
                // Its Go FSM would throw it (the drinking animation's toss); in the hand it just stays.
                foreach (var f in bottle.GetComponents<PlayMakerFSM>()) f.enabled = false;
                bottle.name = "empty bottle(Clone)";
                bottle.SetActive(true);
                return bottle;
            }
            string product = kind == (int)Wire.PocketBeer ? "Beer"
                : kind == (int)Wire.PocketSausages ? "Sausages"
                : kind == (int)Wire.PocketPizza ? "Pizza"
                : kind == (int)Wire.PocketChips ? "Chips"
                : kind == (int)Wire.PocketMacaron ? "MacaronBox" : null;
            var items = product != null ? GameObject.Find(SpawnerPath) : null;
            if (items == null) return null;
            foreach (var f in items.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName != product) continue;
                f.SendEvent("SPAWNITEM");
                return f.FsmVariables.FindFsmGameObject("New").Value;
            }
            return null;
        }

        static void PlaceAhead(GameObject go, Transform cam)
        {
            go.transform.position = cam.position + cam.forward * HoldAhead - Vector3.up * HoldBelow;
            go.transform.rotation = Quaternion.Euler(0f, cam.eulerAngles.y, 0f);
            var body = go.GetComponent<Rigidbody>();
            if (body != null) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }

        void AdvanceTake()
        {
            float age = Time.realtimeSinceStartup - takingSince;
            if (taking == null) { log("pocket: the item taken out is gone"); EndTake(); return; }
            if (age > TakeTimeout)
            {
                log($"pocket: {taking.name} didn't get into the hand in {TakeTimeout} s (hand {(pickUp != null ? pickUp.ActiveStateName : "none")}), left where it is");
                EndTake();
                return;
            }
            var cam = Camera.main;
            if (cam != null) PlaceAhead(taking, cam.transform);
            if (takingUse != null && age < FreshFor)
            {
                var condition = takingUse.FsmVariables.FindFsmFloat("Condition");
                if (condition != null) condition.Value = 100f;
            }
            // A new item loads its saved state first (Use: State 2, State 1, then Wait player or Wait
            // player 2, Wait button with the crosshair on it).
            if (takingUse != null && !takingUse.ActiveStateName.StartsWith("Wait")) return;
            if (eating)
            {
                // What F on it does: the sounds and needs, then Destroy.
                SaveSync.SwitchState.Invoke(takingUse.Fsm, new object[] { takingUse.Fsm.GetState("Eat") });
                log($"pocket: ate {taking.name} ({ledger.Available(takingKind)} left)");
                EndTake();
                return;
            }
            if (!trimmed)
            {
                var box = taking.transform;
                for (int i = box.childCount - 1; i >= 1; i--)
                {
                    var bottle = box.GetChild(i);
                    bottle.parent = null;
                    UnityEngine.Object.Destroy(bottle.gameObject);
                }
                var destroyed = takingUse.FsmVariables.FindFsmInt("DestroyedBottles");
                if (destroyed != null) destroyed.Value = CaseBottles - box.childCount;
                trimmed = true;
            }
            var hand = PickUp();
            if (hand == null) return;
            if (Holding(hand.ActiveStateName) && hand.FsmVariables.FindFsmGameObject("PickedObject").Value == taking)
            {
                log($"pocket: {taking.name} in the hand ({ledger.Available(takingKind)} left in the pocket)");
                EndTake();
                return;
            }
            if (hand.ActiveStateName != "Look for object") return;  // still letting go of what it held, or on its way
            // What a click on the item does: Look for object -> State 1 (PickedObject = RaycastHitObject) -> ... -> Part picked.
            hand.FsmVariables.FindFsmGameObject("RaycastHitObject").Value = taking;
            hand.SendEvent("FINISHED");
        }

        void EndTake()
        {
            taking = null;
            takingKind = Pocket.None;
            takingUse = null;
            eating = false;
        }
    }
}
