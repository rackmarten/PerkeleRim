using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The pocket's rules (docs/PROTOCOL.md "The pocket"): Skyrim's inventory holds MSC's beer, juice, empty
    // bottles, food and cigarettes, and its gold is MSC's money. Y puts what MSC's player carries
    // (or, hands empty, what the crosshair is on) in it; keys 3 to 8 drink a beer or eat food from
    // it, or take something to throw out into MSC's hand: an empty bottle, or once those are gone
    // Skyrim's junk (protocol/cpp/junk.h). 1 and 2 stay MSC's (hands, tools). Skyrim's
    // hotbar can put these on other numbers (protocol/cpp/hotbar.h): MSC gets the key they stand
    // for. Unity-free.
    public static class Pocket
    {
        public const int None = -1;
        public const int PocketKey = 121;  // KeyCode.Y: free in MSC and in Skyrim
        public const int FirstSlotKey = 51;  // KeyCode.Alpha3
        public const string JunkName = "junk(Clone)";  // MSC's body for a piece of Skyrim's junk (JunkLink)

        // Slot n (key 3 + n) holds this kind. Cigarettes have none: MSC
        // smokes them from its own count (I).
        static readonly int[] SlotKinds =
        {
            (int)Wire.PocketBottle, (int)Wire.PocketBeer, (int)Wire.PocketSausages,
            (int)Wire.PocketPizza, (int)Wire.PocketChips, (int)Wire.PocketMacaron,
        };

        // The kind of MSC item with this name, None when it doesn't go in the pocket (spoiled food is
        // renamed "spoiled ..."; an empty beer case "empty(itemx)").
        public static int KindOf(string name)
        {
            switch (name)
            {
                case "beer case(itemx)": return (int)Wire.PocketBeer;  // one bottle out of it
                case "empty bottle(Clone)": return (int)Wire.PocketBottle;
                case JunkName: return (int)Wire.PocketJunk;
                case "sausages(itemx)": return (int)Wire.PocketSausages;
                case "pizza(itemx)": return (int)Wire.PocketPizza;
                case "potato chips(itemx)": return (int)Wire.PocketChips;
                case "macaron box(itemx)": return (int)Wire.PocketMacaron;
                case "juice(itemx)": return (int)Wire.PocketJuice;  // no key: drunk from Skyrim's inventory
                default: return None;
            }
        }

        // Key 3..8 (KeyCode Alpha3..Alpha8) -> slot 0..5, else None.
        public static int SlotOfKey(int keyCode) =>
            keyCode >= FirstSlotKey && keyCode < FirstSlotKey + SlotKinds.Length ? keyCode - FirstSlotKey : None;

        // Beer and food are used at once (drunk, eaten); an empty bottle or junk comes out into the hand.
        public static bool Consumable(int kind) => kind != None && !Thrown(kind) && kind != (int)Wire.PocketCigarettes && kind != (int)Wire.PocketGold;

        // Taken out to be thrown (key 3): an empty bottle or junk.
        public static bool Thrown(int kind) => kind == (int)Wire.PocketBottle || kind == (int)Wire.PocketJunk;

        // What key 3 takes out: empty bottles while there are any, then junk.
        public static int ThrowKind(long bottles, long junk) =>
            bottles <= 0 && junk > 0 ? (int)Wire.PocketJunk : (int)Wire.PocketBottle;

        // Seated in a vehicle only beer and food are used (4, 5 to 8); nothing goes in or out of the hand.
        public static bool WorksInVehicle(int keyCode) => Consumable(KindOfSlot(SlotOfKey(keyCode)));

        public static int KindOfSlot(int slot) => slot >= 0 && slot < SlotKinds.Length ? SlotKinds[slot] : None;

        public static int SlotOfKind(int kind)
        {
            for (int i = 0; i < SlotKinds.Length; i++) if (SlotKinds[i] == kind) return i;
            return None;
        }

        // MSC's money (PlayerMoney, marks with pennies) as Skyrim's gold: whole marks.
        public static long Gold(float money) => money <= 0f ? 0 : (long)Math.Floor(money);

        // MSC's money for Skyrim's gold, keeping the pennies MSC had (below 0.99, so float rounding
        // never makes them a whole mark and Gold gives back gold + 1).
        public static float Money(long gold, float money) =>
            gold + (money > 0f ? Math.Min(0.99f, money - (float)Math.Floor(money)) : 0f);

        public static bool IsKey(int keyCode) => keyCode == PocketKey || SlotOfKey(keyCode) != None;

        public enum Action
        {
            Nothing,
            Pocket,          // the held item (one bottle of a held case) goes in the pocket
            PocketAimed,     // the item the crosshair is on goes in (one bottle of a case)
            Consume,         // a beer is drunk or food eaten from the pocket
            Take,            // the slot's kind comes out into the free hand
            PocketAndTake,   // the held item goes in, then the slot's kind comes out
            PocketAndConsume, // the empty bottle in the hand goes in, then a beer is drunk or food eaten
            DropAndTake,     // the held item isn't pocketable: it's dropped, then the slot's kind comes out
        }

        // What a key press does. held: kind of what MSC's hand carries (None for nothing or something
        // not pocketable), holding: the hand carries anything, aimed: kind of the item the crosshair
        // is on, available: how many of the slot's kind the pocket has (for 3, bottles and junk).
        public static Action Decide(int keyCode, int held, bool holding, int aimed, long available)
        {
            if (keyCode == PocketKey)
                return held != None ? Action.Pocket : !holding && aimed != None ? Action.PocketAimed : Action.Nothing;
            int kind = KindOfSlot(SlotOfKey(keyCode));
            if (kind == None) return Action.Nothing;
            if (Consumable(kind))
                return available <= 0 ? Action.Nothing : Thrown(held) ? Action.PocketAndConsume : Action.Consume;
            if (held == kind || (Thrown(kind) && Thrown(held))) return Action.Pocket;  // the same number again puts it back
            if (available <= 0) return Action.Nothing;
            if (!holding) return Action.Take;
            return held != None ? Action.PocketAndTake : Action.DropAndTake;
        }
    }

    // MSC's view of Skyrim's pocket counts: the last PocketCounts plus the changes MSC sent that
    // Skyrim hasn't applied yet, so a second key press before Skyrim answers sees the first.
    public class PocketLedger
    {
        struct Pending { public uint Seq; public int Kind; public int Delta; }

        readonly List<Pending> pending = new List<Pending>();
        readonly long[] counts = new long[Wire.PocketKinds];
        uint nextSeq = 1;

        public uint Session { get; private set; }
        public bool Known { get; private set; }  // a PocketCounts arrived this session

        public PocketLedger(uint session) { Reset(session); }

        // A new link session: nothing sent yet, counts unknown until Skyrim sends them.
        public void Reset(uint session)
        {
            Session = session == 0 ? 1u : session;
            nextSeq = 1;
            pending.Clear();
            Known = false;
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
        }

        public PocketChange Next(int kind, int delta) =>
            new PocketChange { session = Session, seq = nextSeq, kind = (uint)kind, delta = delta };

        // c went out to Skyrim.
        public void Sent(PocketChange c)
        {
            pending.Add(new Pending { Seq = c.seq, Kind = (int)c.kind, Delta = c.delta });
            nextSeq = c.seq + 1;
        }

        // Skyrim's counts. Counts from another session (an older MSC, or none applied yet) include
        // none of this session's changes.
        public void OnCounts(uint session, uint appliedSeq, uint[] skyrimCounts)
        {
            uint applied = session == Session ? appliedSeq : 0;
            pending.RemoveAll(p => p.Seq <= applied);
            for (int i = 0; i < counts.Length && i < skyrimCounts.Length; i++) counts[i] = skyrimCounts[i];
            Known = true;
        }

        public long Available(int kind)
        {
            if (!Known || kind < 0 || kind >= counts.Length) return 0;
            long n = counts[kind];
            foreach (var p in pending) if (p.Kind == kind) n += p.Delta;
            return n < 0 ? 0 : n;
        }
    }

    // Keeps one of MSC's counts and a pocket kind equal: cigarettes (global PlayerCigarettes and
    // Skyrim's Cigarette items) and money (PlayerMoney in whole marks and Skyrim's gold). When the
    // link comes up one side wins: MSC's cigarettes (Skyrim has none of MSC's the first time),
    // Skyrim's gold (the Skyrim save is the truth). After that a change on either side is copied to
    // the other.
    public class CountMirror
    {
        readonly bool skyrimFirst;
        bool synced;
        long last;  // the count both sides agreed on

        public CountMirror(bool skyrimFirst) { this.skyrimFirst = skyrimFirst; }

        public void Reset() { synced = false; }

        // msc: MSC's count now, skyrim: the pocket's (PocketLedger.Available). Returns the change to
        // send to Skyrim (0: none) and sets setMsc when MSC's count must become newMsc.
        public int Step(long msc, long skyrim, out bool setMsc, out long newMsc)
        {
            setMsc = false;
            newMsc = msc;
            if (msc < 0) msc = 0;
            if (!synced)
            {
                synced = true;
                if (skyrimFirst)
                {
                    last = skyrim;
                    setMsc = msc != skyrim;
                    newMsc = skyrim;
                    return 0;
                }
                last = msc;
                return (int)(msc - skyrim);
            }
            if (skyrim != last)  // picked up, dropped or sold in Skyrim
            {
                last = skyrim;
                setMsc = true;
                newMsc = skyrim;
                return 0;
            }
            if (msc == last) return 0;
            int delta = (int)(msc - last);  // smoked or paid, or bought or picked up in MSC
            last = msc;
            return delta;
        }

        // A change Step returned couldn't be sent (ring full); Step finds it again next time.
        public void Unsent(int delta)
        {
            if (synced) last -= delta;
        }
    }
}
