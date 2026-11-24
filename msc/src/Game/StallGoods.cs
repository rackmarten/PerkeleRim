using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Teimo's stall in Riverwood as a shop (TeimoStall, TeimoPay, TeimoShop; rules in
    // msc/src/Link/StallShop.cs). On each spot Skyrim sends, MSC sets out one of its shop's goods: a
    // look-alike of the product (the renderers of its spawner's CreatePrefab, a box collider, a
    // rigidbody, tagged PART like a store item) that MSC's hand picks up, carries, drops and throws
    // like the real thing, and that ItemLink draws in Skyrim. Its crosshair prompt is the shop's own
    // ("BEER 149.00 MK"). Goods lying on the counter are the bill, as a click on a shelf product
    // adds it to MSC's register; Kumpo pays by talking to Teimo (TeimoPay), and MSC's register's
    // rule takes the total off PlayerMoney (Skyrim's gold follows), each good becoming a real item
    // from MSC's spawner where it lies, Kumpo's from then on. A good carried past 8 m from Teimo
    // unpaid becomes a real item too, stolen (Skyrim's bounty). The look-alikes have no FSMs, so
    // MSC never saves them; Teimo sets out new ones for the gone ones once Kumpo is away.
    public class StallGoods
    {
        const float Clearance = 0.02f;  // meters between a good's bottom and what it's set on
        const float GroundWait = 20f;   // seconds the spots wait for their ground
        const float OffSpot = 0.3f;     // meters from its spot an unpaid good counts as moved
        const float Fallen = 1f;        // meters below its spot an untouched good counts as fallen
        const float Retry = 2f;         // seconds before a spot that couldn't be set out is tried again
        const float Occupied = 0.15f;   // meters around a spot where a physics item means it's taken

        readonly Island island;
        readonly Func<Vector3, Vector3?> findGround;
        readonly TeimoVoice voice;
        readonly Action<string> swear;
        readonly Action<string> log;

        class Live
        {
            public int Spot;
            public uint Product;
            public GameObject Go;
            public float Price;
            public bool Stolen;      // carried off: turns real once the hand has let go of it
            public bool WasInHand;   // and it was in the hand then: the real item goes back in
            public Vector3 Home;     // where it was set out
            public bool Touched;     // Kumpo has had it in his hand: Teimo puts it back once he's away
        }

        StallShop.Stall stall;
        Vec3 anchor;  // the island's anchor the goods were set out on
        Live[] spots = new Live[0];
        bool[] filled = new bool[0];
        float[] retryAt = new float[0];  // a spot that couldn't be set out waits till then
        float since;
        readonly float[] prices = new float[StallShop.Products.Length];
        readonly string[] labels = new string[StallShop.Products.Length];
        readonly List<uint> pays = new List<uint>();
        StallShop.Bill bill;
        float notEnoughUntil;
        string promptSet;
        FsmString interaction;
        FsmFloat money;
        int paidCount, stolenCount;

        public StallGoods(Island island, Func<Vector3, Vector3?> findGround, TeimoVoice voice, Action<string> swear, Action<string> log)
        {
            this.island = island;
            this.findGround = findGround;
            this.voice = voice;
            this.swear = swear;
            this.log = log;
        }

        public void OnStall(byte[] payload, int size)
        {
            StallShop.Stall got;
            if (!StallShop.Decode(payload, size, out got))
            {
                log($"stall: malformed TeimoStall ({size} bytes)");
                return;
            }
            Clear("a new stall");  // even for the same generation: a restarted Skyrim counts its anchors from 1 again
            stall = got;
            anchor = island.Anchor;
            spots = new Live[got.Goods.Count];
            filled = new bool[got.Goods.Count];
            retryAt = new float[got.Goods.Count];
            since = Time.realtimeSinceStartup;
            ReadPrices();
            log($"stall: {got.Goods.Count} goods to set out for gen {got.Generation}, Teimo at ({got.Teimo.x:0},{got.Teimo.y:0},{got.Teimo.z:0})");
        }

        public void OnPay(byte[] payload, int size)
        {
            uint seq;
            if (!StallShop.DecodePay(payload, size, out seq)) { log($"stall: malformed TeimoPay ({size} bytes)"); return; }
            pays.Add(seq);
        }

        // Every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(Link link, bool playing, Vector3 feet)
        {
            if (stall == null) return;
            if (island.Generation != stall.Generation || island.Anchor.x != anchor.x || island.Anchor.y != anchor.y || island.Anchor.z != anchor.z)
            {
                Clear($"the island moved on to gen {island.Generation}");
                stall = null;
                return;
            }
            if (!playing) return;
            var kumpo = island.IslandToSkyrim(new Vec3(feet.x, feet.y, feet.z));
            bool away = StallShop.KumpoAway(kumpo, stall);
            var hand = PocketLink.PickUp();
            var held = HeldObject(hand);

            Restock(away, held);
            TakeOff(link, hand, held);
            foreach (var seq in pays) Pay(link, seq, held);
            pays.Clear();
            UpdateBill(link, held);
            Prompt(hand);
        }

        // ---- Setting out ----

        void Restock(bool away, GameObject held)
        {
            foreach (var live in spots)
                if (live != null && live.Go != null && live.Go == held) live.Touched = true;
            // Teimo tidies up behind Kumpo's back: unpaid goods he moved go back to their spots
            // (one that fell off its spot by itself stays where it fell).
            if (away)
                for (int i = 0; i < spots.Length; i++)
                {
                    var live = spots[i];
                    if (live == null || live.Go == null || live.Stolen || !live.Touched || live.Go == held) continue;
                    if (Vector3.Distance(live.Go.transform.position, live.Home) > OffSpot)
                    {
                        log($"stall: Teimo puts {live.Go.name} back");
                        Destroy(live);
                        spots[i] = null;
                    }
                }
            // An untouched good that fell well below its spot (its ground gone, knocked off) is set out again.
            for (int i = 0; i < spots.Length; i++)
            {
                var live = spots[i];
                if (live == null || live.Go == null || live.Touched || live.Stolen) continue;
                if (live.Home.y - live.Go.transform.position.y > Fallen)
                {
                    log($"stall: {live.Go.name} fell off spot {i}, Teimo sets out another");
                    Destroy(live);
                    spots[i] = null;
                    filled[i] = false;
                }
            }
            // One a frame: a spot never set out this anchor at once, a gone one only behind his back.
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i] != null && spots[i].Go != null) continue;
                if (filled[i] && !away) continue;
                if (now < retryAt[i]) continue;
                var at = Spot(i);
                if (!at.HasValue) continue;
                var ground = findGround(at.Value);
                if (!ground.HasValue)
                {
                    if (now - since > GroundWait && !filled[i])
                    {
                        log($"stall: no ground at spot {i} after {GroundWait} s, left empty for now");
                        filled[i] = true;
                    }
                    retryAt[i] = now + Retry;
                    continue;
                }
                var taken = ItemAt(new Vector3(at.Value.x, ground.Value.y, at.Value.z));
                if (taken != null)
                {
                    filled[i] = true;  // something lies there (a good Kumpo bought and left): later
                    retryAt[i] = now + Retry;
                    continue;
                }
                if (!SetOut(i, at.Value, ground.Value.y)) retryAt[i] = now + Retry;
                return;
            }
        }

        Vector3? Spot(int i)
        {
            if (stall == null || i >= stall.Goods.Count) return null;
            var p = island.SkyrimToIsland(stall.Goods[i].Position);
            return new Vector3(p.x, p.y, p.z);
        }

        // A physics item (anything with a rigidbody: the island's own chunks have none) on the spot.
        static Rigidbody ItemAt(Vector3 spot)
        {
            foreach (var c in Physics.OverlapSphere(spot + Vector3.up * (Occupied + 0.02f), Occupied))
                if (c.attachedRigidbody != null) return c.attachedRigidbody;
            return null;
        }

        bool SetOut(int i, Vector3 at, float board)
        {
            filled[i] = true;
            var good = stall.Goods[i];
            if (!StallShop.Known(good.Product)) { log($"stall: spot {i} has an unknown product {good.Product}"); return false; }
            var prefab = Prefab(StallShop.Products[good.Product].Name);
            if (prefab == null) return false;
            var go = MakeLook(prefab, $"{StallShop.Products[good.Product].Name.ToLowerInvariant()}(Teimo's)");
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(good.Heading), 0f);
            float bottom;
            if (Bottom(go, out bottom)) go.transform.position += Vector3.up * (board + Clearance - bottom);
            var body = go.GetComponent<Rigidbody>();
            body.position = go.transform.position;
            body.rotation = go.transform.rotation;
            spots[i] = new Live { Spot = i, Product = good.Product, Go = go, Price = prices[good.Product], Home = go.transform.position };
            log($"stall: {go.name} set out on spot {i} for {prices[good.Product]:0.00} mk");
            return true;
        }

        // The product's own look without its FSMs: its renderers' meshes and materials under the
        // same transforms (so ItemLink sends the same model as for the real item), a box around
        // them, a rigidbody of its mass, tagged and layered as MSC's hand picks store items up.
        static GameObject MakeLook(GameObject prefab, string name)
        {
            var root = new GameObject(name);
            root.transform.localScale = prefab.transform.localScale;
            CopyRenderers(prefab.transform, root.transform);
            root.tag = "PART";
            root.layer = 19;
            var box = root.AddComponent<BoxCollider>();
            Bounds local;
            if (LocalBounds(root.transform, out local))
            {
                box.center = local.center;
                box.size = Vector3.Max(local.size, new Vector3(0.04f, 0.04f, 0.04f));
            }
            var body = root.AddComponent<Rigidbody>();
            var own = prefab.GetComponent<Rigidbody>();
            body.mass = own != null ? own.mass : 1f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            return root;
        }

        static void CopyRenderers(Transform from, Transform to)
        {
            var filter = from.GetComponent<MeshFilter>();
            var renderer = from.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null && filter.sharedMesh != null && renderer.enabled)
            {
                to.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                to.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            foreach (Transform child in from)
            {
                if (!child.gameObject.activeSelf || child.GetComponentsInChildren<MeshRenderer>(true).Length == 0) continue;
                var copy = new GameObject(child.name);
                copy.transform.parent = to;
                copy.transform.localPosition = child.localPosition;
                copy.transform.localRotation = child.localRotation;
                copy.transform.localScale = child.localScale;
                copy.layer = 19;
                CopyRenderers(child, copy.transform);
            }
        }

        // What the renderers cover, in root's own space.
        static bool LocalBounds(Transform root, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var b = filter.sharedMesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        static bool Bottom(GameObject item, out float bottom)
        {
            bottom = float.MaxValue;
            foreach (var r in item.GetComponentsInChildren<Renderer>())
                bottom = Mathf.Min(bottom, r.bounds.min.y);
            return bottom != float.MaxValue;
        }

        readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        float prefabRetry;

        GameObject Prefab(string product)
        {
            GameObject prefab;
            if (prefabs.TryGetValue(product, out prefab) && (prefab != null || Time.realtimeSinceStartup < prefabRetry)) return prefab;
            prefabRetry = Time.realtimeSinceStartup + 10f;
            var go = GameObject.Find("Spawner/CreateItems");
            if (go != null)
                foreach (var f in go.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == product)
                    {
                        var v = f.FsmVariables.FindFsmGameObject("CreatePrefab");
                        if (v != null) prefab = v.Value;
                    }
            if (prefab == null) log($"stall: no CreatePrefab in Spawner/CreateItems :: {product}");
            prefabs[product] = prefab;
            return prefab;
        }

        // MSC's own prices: each product's Buy FSM under STORE/LOD/ActivateStore (its Price, what its
        // "Register add" puts on the bill), else the price read from it when this was written.
        void ReadPrices()
        {
            for (int i = 0; i < prices.Length; i++) { prices[i] = StallShop.Products[i].Price; labels[i] = StallShop.Products[i].Label; }
            var found = new List<string>();
            foreach (PlayMakerFSM fsm in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                if (fsm == null || fsm.FsmName != "Buy" || fsm.transform.parent == null) continue;
                for (int i = 0; i < prices.Length; i++)
                {
                    var p = StallShop.Products[i];
                    if (fsm.gameObject.name != p.Name || fsm.transform.parent.name != p.Group) continue;
                    var price = fsm.FsmVariables.FindFsmFloat("Price");
                    if (price != null && price.Value > 0f)
                    {
                        prices[i] = price.Value;
                        found.Add($"{p.Name} {price.Value:0.00}");
                    }
                }
            }
            log($"stall: MSC's prices {string.Join(", ", found.ToArray())} ({found.Count} of {prices.Length} read from its shop)");
        }

        // ---- The bill ----

        static GameObject HeldObject(PlayMakerFSM hand)
        {
            if (hand == null || (hand.ActiveStateName != "Part picked" && hand.ActiveStateName != "Item picked")) return null;
            var picked = hand.FsmVariables.FindFsmGameObject("PickedObject");
            return picked != null ? picked.Value : null;
        }

        Vec3 SkyrimAt(GameObject go)
        {
            var p = go.transform.position;
            return island.IslandToSkyrim(new Vec3(p.x, p.y, p.z));
        }

        StallShop.Bill Counted(GameObject held, List<Live> on)
        {
            var b = new StallShop.Bill();
            foreach (var live in spots)
            {
                if (live == null || live.Go == null || live.Stolen || live.Go == held) continue;
                if (!StallShop.OnCounter(SkyrimAt(live.Go), stall)) continue;
                b.Add(live.Product, live.Price);
                if (on != null) on.Add(live);
            }
            return b;
        }

        void UpdateBill(Link link, GameObject held)
        {
            var now = Counted(held, null);
            if (now.Same(bill)) return;
            if (now.Count > bill.Count) voice.Till("cash_register_1", stall.Counter);  // MSC's register as a good goes on the bill
            bill = now;
            Send(link, Wire.ShopBill, bill, Money(), 0);
            log($"stall: {bill.Count} goods on the counter, {bill.Total:0.00} mk");
        }

        void Pay(Link link, uint seq, GameObject held)
        {
            var on = new List<Live>();
            var due = Counted(held, on);
            if (due.Count == 0) { log($"stall: Kumpo asks to pay ({seq}), but nothing lies on the counter"); return; }
            float has = Money();
            if (!StallShop.CanPay(due.Total, has))
            {
                notEnoughUntil = Time.realtimeSinceStartup + StallShop.NotEnoughSeconds;
                swear("not enough money at Teimo's");
                Send(link, Wire.ShopShort, due, has, seq);
                log($"stall: {due.Total:0.00} mk due, Kumpo has {has:0.00}: not enough money");
                return;
            }
            // Each good turns real first; he pays only for those MSC's spawner made (a good it didn't
            // stays a look-alike on the counter).
            var paid = new StallShop.Bill();
            foreach (var live in on)
            {
                string was = live.Go.name;
                var real = MakeReal(live, false);
                if (real == null)
                {
                    log($"stall: MSC's spawner made no {StallShop.Products[live.Product].Name} for {was}, not charged");
                    continue;
                }
                spots[live.Spot] = null;
                paid.Add(live.Product, live.Price);
                log($"stall: paid for {was}, now {real.name}");
            }
            if (paid.Count == 0) return;
            if (money != null) money.Value = has - paid.Total;
            voice.Till("cash_register_2", stall.Counter);
            paidCount += paid.Count;
            Send(link, Wire.ShopPaid, paid, Money(), seq);
            log($"stall: Kumpo paid {paid.Total:0.00} mk for {paid.Count} goods, {Money():0.00} mk left");
        }

        // Goods past 8 m from Teimo unpaid are stolen: each becomes a real item where it is (in the
        // hand again once the hand has let go of the look-alike).
        void TakeOff(Link link, PlayMakerFSM hand, GameObject held)
        {
            var taken = new StallShop.Bill();
            foreach (var live in spots)
            {
                if (live == null || live.Go == null) continue;
                if (!live.Stolen && (live.Touched || live.Go == held) && StallShop.TakenAway(SkyrimAt(live.Go), stall))
                {
                    live.Stolen = true;
                    taken.Add(live.Product, live.Price);
                    if (live.Go == held && hand != null)
                    {
                        live.WasInHand = true;
                        hand.SendEvent("DROP_PART");  // let go; taken again as the real item below
                    }
                    log($"stall: {live.Go.name} carried off unpaid");
                }
            }
            if (taken.Count > 0)
            {
                stolenCount += taken.Count;
                Send(link, Wire.ShopStolen, taken, Money(), 0);
            }
            for (int i = 0; i < spots.Length; i++)
            {
                var live = spots[i];
                if (live == null || !live.Stolen || live.Go == null) continue;
                if (live.Go == HeldObject(hand)) continue;  // still letting go
                if (live.WasInHand && hand != null && hand.ActiveStateName != "Look for object") continue;  // its drop's short wait
                string was = live.Go.name;
                var real = MakeReal(live, true);
                spots[i] = null;
                log($"stall: {was} stolen, now {(real != null ? real.name : "nothing: the spawner made none")}{(live.WasInHand ? ", back in the hand" : "")}");
                if (real != null && live.WasInHand && hand != null && hand.ActiveStateName == "Look for object")
                {
                    // What a click on the item does (PocketLink): Look for object -> State 1 -> ... -> Part picked.
                    hand.FsmVariables.FindFsmGameObject("RaycastHitObject").Value = real;
                    hand.SendEvent("FINISHED");
                }
            }
        }

        // The good as a real item from MSC's spawner, where the look-alike is and moving as it did;
        // null when the spawner made nothing new (the look-alike stays, unless dropAnyway).
        GameObject MakeReal(Live live, bool dropAnyway)
        {
            var at = live.Go.transform.position;
            var turn = live.Go.transform.rotation;
            var own = live.Go.GetComponent<Rigidbody>();
            var velocity = own != null ? own.velocity : Vector3.zero;
            var spin = own != null ? own.angularVelocity : Vector3.zero;
            var real = ShelfStocker.Spawn(StallShop.Products[live.Product].Name, log);
            if (real == null && !dropAnyway) return null;
            Destroy(live);
            if (real == null) return null;
            real.transform.position = at;
            real.transform.rotation = turn;
            var body = real.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = at;
                body.rotation = turn;
                body.velocity = velocity;
                body.angularVelocity = spin;
            }
            return real;
        }

        void Destroy(Live live)
        {
            if (live.Go != null) UnityEngine.Object.Destroy(live.Go);
            live.Go = null;
        }

        float Money()
        {
            if (money == null) money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            return money != null ? money.Value : 0f;
        }

        void Send(Link link, uint kind, StallShop.Bill b, float has, uint seq)
        {
            var payload = StallShop.EncodeShop(kind, b, has, seq);
            if (link == null || !link.Send(MessageType.TeimoShop, payload, payload.Length)) log($"stall: TeimoShop {kind} didn't go to Skyrim");
        }

        // ---- The prompt ----

        // MSC's Buy FSM's prompt while the crosshair is on a good ("BEER 149.00 MK"), and its
        // register's "NOT ENOUGH MONEY" for 3 s after a payment he couldn't make.
        void Prompt(PlayMakerFSM hand)
        {
            if (interaction == null) interaction = FsmVariables.GlobalVariables.FindFsmString("GUIinteraction");
            if (interaction == null) return;
            string text = null;
            if (Time.realtimeSinceStartup < notEnoughUntil) text = StallShop.NotEnough;
            else if (hand != null && hand.ActiveStateName == "Look for object")
            {
                var aimed = hand.FsmVariables.FindFsmGameObject("RaycastHitObject");
                var live = aimed != null ? Find(aimed.Value) : null;
                if (live != null) text = StallShop.Prompt(live.Product, live.Price);
            }
            if (text != null)
            {
                interaction.Value = text;
                promptSet = text;
            }
            else if (promptSet != null)
            {
                if (interaction.Value == promptSet) interaction.Value = "";
                promptSet = null;
            }
        }

        Live Find(GameObject go)
        {
            if (go == null) return null;
            foreach (var live in spots)
                if (live != null && live.Go != null && (live.Go == go || go.transform.IsChildOf(live.Go.transform))) return live;
            return null;
        }

        void Clear(string why)
        {
            int n = 0;
            var hand = PocketLink.PickUp();
            var held = HeldObject(hand);
            foreach (var live in spots)
            {
                if (live == null || live.Go == null) continue;
                if (live.Go == held && hand != null) hand.SendEvent("DROP_PART");
                Destroy(live);
                n++;
            }
            spots = new Live[0];
            filled = new bool[0];
            retryAt = new float[0];
            bill = new StallShop.Bill();
            pays.Clear();
            if (n > 0) log($"stall: {n} goods taken back in ({why})");
        }

        // ---- The spike's and the test kit's ----

        // "shop": every good, where and how.
        public string Describe()
        {
            if (stall == null) return "stall: none";
            var lines = new List<string> { $"stall: gen {stall.Generation}, bill {bill.Count} goods {bill.Total:0.00} mk, paid {paidCount}, stolen {stolenCount}" };
            for (int i = 0; i < spots.Length; i++)
            {
                var live = spots[i];
                if (live == null || live.Go == null) { lines.Add($"  {i}: empty"); continue; }
                var at = SkyrimAt(live.Go);
                lines.Add($"  {i}: {live.Go.name} {live.Price:0.00} mk at ({at.x:0},{at.y:0},{at.z:0}){(StallShop.OnCounter(at, stall) ? " on the counter" : "")}");
            }
            return string.Join("\n", lines.ToArray());
        }

        // "shop take beer": the first good of that product into MSC's hand, as a click on it does.
        public bool Take(string product)
        {
            var hand = PocketLink.PickUp();
            if (hand == null || hand.ActiveStateName != "Look for object") { log("stall: the hand is busy"); return false; }
            foreach (var live in spots)
            {
                if (live == null || live.Go == null || live.Stolen) continue;
                if (!string.Equals(StallShop.Products[live.Product].Name, product, StringComparison.OrdinalIgnoreCase)) continue;
                hand.FsmVariables.FindFsmGameObject("RaycastHitObject").Value = live.Go;
                hand.SendEvent("FINISHED");
                log($"stall: {live.Go.name} into the hand");
                return true;
            }
            log($"stall: no {product} out on the stall");
            return false;
        }

        // "shop counter": what the hand holds put down on the counter, as dropping it there does.
        public bool ToCounter()
        {
            var hand = PocketLink.PickUp();
            var held = HeldObject(hand);
            var live = Find(held);
            if (live == null || stall == null) { log("stall: the hand holds none of Teimo's goods"); return false; }
            hand.SendEvent("DROP_PART");
            var c = island.SkyrimToIsland(stall.Counter);
            var right = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(stall.Heading), 0f) * Vector3.right;
            var at = new Vector3(c.x, c.y, c.z);
            foreach (var offset in new[] { -0.22f, 0.22f, 0f })  // beside whatever lies there already
            {
                var spot = new Vector3(c.x, c.y, c.z) + right * offset;
                bool free = true;
                foreach (var hit in Physics.OverlapSphere(spot + Vector3.up * 0.15f, 0.15f))
                    if (hit.attachedRigidbody != null && hit.attachedRigidbody.gameObject != live.Go) free = false;
                if (free) { at = spot; break; }
            }
            var ground = findGround(at + Vector3.up * 0.3f);
            live.Go.transform.position = ground.HasValue ? ground.Value : at;
            live.Go.transform.rotation = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(stall.Heading), 0f);
            float bottom;
            if (ground.HasValue && Bottom(live.Go, out bottom)) live.Go.transform.position += Vector3.up * (ground.Value.y + Clearance - bottom);
            var body = live.Go.GetComponent<Rigidbody>();
            body.position = live.Go.transform.position;
            body.rotation = live.Go.transform.rotation;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            log($"stall: {live.Go.name} put on the counter");
            return true;
        }
    }
}
