namespace PerkeleRim
{
    // What makes up the Satsuma for Skyrim (Game/SatsumaParts): MSC's car is a body with parts on it,
    // and each part that is on is drawn from a model of its own (PartSet), so a part going on or
    // coming off changes only that part. Unity-free, so the tests cover it.
    //
    // MSC puts a part on in one of two ways (its triggers' Assembly FSMs, docs/FSM_MAP.md): most parts
    // have a copy under the car named "<part>(xxxxx)" that it switches on (the loose "<part>(Clone)" is
    // destroyed), and the rest (doors, fenders, the hood, wheels) are the loose part itself hung under
    // one of the car's pivots. Either way the part has a Removal FSM for taking it off, or that name.
    public static class SatsumaModel
    {
        public const string ObjectName = "SATSUMA(557kg, 248)";
        public const string Name = "Satsuma";  // its body's model and PlayerCurrentVehicle

        // The body shell itself (and its mass) is a copy like the parts' but always there: the body.
        static readonly string[] Body = { "car body(xxxxx)", "car body masse(xxxxx)" };

        // Whether an object below the car is a part of it (on or not): named like one, or with a
        // Removal FSM (the wheels are "wheel_regula").
        public static bool IsPart(string name, bool hasRemoval)
        {
            if (name == null) return false;
            foreach (var b in Body)
                if (name == b) return false;
            return hasRemoval || name.EndsWith("(xxxxx)") || name.EndsWith("(Clone)");
        }

        // The triggers the spike command "satsuma half" puts parts on through, as a player would: the
        // body panels, the front seats, the dashboard and the radiator (half a car, seen from outside).
        public static readonly string[] HalfBuilt =
        {
            "trigger_door_left", "trigger_door_right", "trigger_fender_left", "trigger_fender_right", "trigger_hood",
            "trigger_bootlid", "trigger_bumper_front", "trigger_bumper_rear", "trigger_grille", "trigger_seat_driver",
            "trigger_seat_passenger", "trigger_dashboard", "trigger_radiator",
        };
    }
}
