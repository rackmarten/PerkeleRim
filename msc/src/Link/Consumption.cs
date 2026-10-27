using System;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // What MSC's player consumes, read from PlayMaker's state changes (docs/FSM_MAP.md). Each finished
    // one is a Consumed kind Skyrim heals by:
    //  - an alcoholic drink: the player's Drink FSM leaves the drinking animation for throwing the
    //    bottle or glass (kilju: Kilju -> Calc). Interrupted drinks never get there. Milk, juice and
    //    coffee heal nothing;
    //  - a cigarette: PlayerFunctions finds one in the pack and lights it (Check tobacco -> Activate);
    //  - food: an item's Use FSM finishes eating it (Eat or Eat 2 -> Destroy; a single sausage goes
    //    through Fresh or Grilled instead). The bottles' Use FSMs (booze, milk, spirit) pass Eat 2 as
    //    well; their drink is the Drink FSM's.
    // Unity-free.
    public static class Consumption
    {
        // One state change: the FSM's name, the state it leaves and the one it enters. drinkItem: a Use
        // FSM that hands its item to the Drink FSM (it has a "Check drink" state). 0 when nothing was
        // consumed.
        public static uint Kind(string fsm, string from, string to, bool drinkItem)
        {
            if (fsm == null || from == null || to == null) return 0;
            switch (fsm)
            {
                case "Drink":
                    if (from == "Drink anim" && to == "Throw bottle") return Wire.ConsumedBeer;
                    if (from == "Drink anim 1" && to == "Throw bottle 1") return Wire.ConsumedBooze;
                    if (from == "Drink anim 2" && to == "Throw glass") return Wire.ConsumedShot;
                    if (from == "Drink anim 8" && to == "Throw bottle 2") return Wire.ConsumedSpirit;
                    if (from == "Kilju" && to == "Calc") return Wire.ConsumedKilju;
                    return 0;
                case "PlayerFunctions":
                    return from == "Check tobacco" && to == "Activate" ? Wire.ConsumedCigarette : 0u;
                case "Use":
                    if (drinkItem || !to.StartsWith("Destroy")) return 0;
                    return from == "Eat" || from == "Eat 2" || from == "Fresh" || from == "Grilled" ? Wire.ConsumedFood : 0u;
                default:
                    return 0;
            }
        }

        // Skyrim's food and drink (ConsumeEvent, protocol/cpp/food.h) on MSC's needs: hunger and
        // thirst don't go below 0 from it (a full Kumpo stays full, he isn't stocked up for later),
        // but one already below stays there; urine just adds up.
        public static float AddNeed(float now, float delta) =>
            delta < 0f ? Math.Max(Math.Min(now, 0f), now + delta) : now + delta;

        // PlayerDrunkAdjusted, kept in MSC's own 0..8.
        public static float AddDrunk(float now, float delta) => Math.Min(8f, Math.Max(0f, now + delta));

        // The Drink FSM's event for a drink from Skyrim (ConsumeEvent.drink, Wire.Drink*), null for none.
        public static string DrinkEvent(uint drink)
        {
            switch (drink)
            {
                case Wire.DrinkBeer: return "DRINKBEER";
                case Wire.DrinkBooze: return "DRINKVIINA";
                case Wire.DrinkSpirit: return "DRINKSPIRIT";
                case Wire.DrinkMilk: return "DRINKMILK";
                case Wire.DrinkJuice: return "DRINKJUICE";
                default: return null;
            }
        }

        // Of several drinks in one menu the one shown: the strongest bottle, as food.h's Shown.
        public static uint Shown(uint was, uint now) => Rank(now) > Rank(was) ? now : was;

        static int Rank(uint drink)
        {
            switch (drink)
            {
                case Wire.DrinkJuice: return 1;
                case Wire.DrinkMilk: return 2;
                case Wire.DrinkBeer: return 3;
                case Wire.DrinkBooze: return 4;
                case Wire.DrinkSpirit: return 5;
                default: return 0;
            }
        }

        // The juice has nothing to throw after it (Juice -> Burb); the others throw their bottle,
        // glass or carton.
        public static bool Throws(uint drink) => DrinkEvent(drink) != null && drink != Wire.DrinkJuice;

        // What the Drink FSM throws after a drink, by name: beer's empty bottle and BottleBeerFly,
        // and the booze bottle, spirit bottle and milk carton by the same words.
        public static bool IsDrinkLeftover(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            return n.Contains("bottle") || n.Contains("booze") || n.Contains("spirit") || n.Contains("milk") || n.Contains("glass") || n.Contains("empty");
        }
    }
}
