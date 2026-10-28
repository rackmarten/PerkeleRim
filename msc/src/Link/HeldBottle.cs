using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The bottle of a drink shown for Skyrim (HeldDrink, protocol/cpp/held_drink.h): which of the
    // Drink object's children it is, and the message from its pose. Unity-free, so the tests cover
    // it; the caller passes points already in the camera's space.
    public static class HeldBottle
    {
        // The bottle MSC's drinking of drink has in the hand, below Drink (FSM_MAP "Drinking"): the
        // beer, booze and spirit bottles hang on Hand itself, the milk carton and juice can on their
        // own hand. Their meshes stand along +Z, top up.
        public static string Path(uint drink)
        {
            switch (drink)
            {
                case Wire.DrinkBeer: return "Hand/BeerBottle";
                case Wire.DrinkBooze: return "Hand/BoozeBottle";
                case Wire.DrinkSpirit: return "Hand/SpiritBottle";
                case Wire.DrinkMilk: return "Hand/HandMilk/Milk";
                case Wire.DrinkJuice: return "Hand/HandJuice/JuiceCan";
                default: return null;
            }
        }

        // bottom, top: the middle of the mesh's base and top; side: its +X. All in the Unity camera's
        // own space (+X right, +Y up, +Z forward, meters), as Transform.InverseTransformPoint gives them.
        public static unsafe HeldDrink Message(uint form, Vec3 bottom, Vec3 top, Vec3 side)
        {
            var m = new HeldDrink { form = form };
            // The camera's space swaps like the world's: Skyrim's camera has +Y forward, +Z up.
            var b = Coords.UnityToSkyrim(bottom);
            var t = Coords.UnityToSkyrim(top);
            var s = Coords.UnityToSkyrim(side);
            m.bottom[0] = b.x; m.bottom[1] = b.y; m.bottom[2] = b.z;
            m.top[0] = t.x; m.top[1] = t.y; m.top[2] = t.z;
            m.side[0] = s.x; m.side[1] = s.y; m.side[2] = s.z;
            return m;
        }

        // The bottle is down or thrown: Skyrim drops its model.
        public static HeldDrink None() => new HeldDrink();
    }
}
