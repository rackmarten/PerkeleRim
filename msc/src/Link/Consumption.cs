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
    }
}
