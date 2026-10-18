namespace PerkeleRim
{
    // What MSC's player stops doing when Skyrim loads a save (LoadReset): everything that is only
    // an action of the moment, so a loaded game never carries over a stream, a cigarette, a bottle,
    // a raised hand or the zoom. The needs come back from the save (SaveSync). FSMs are on
    // PLAYER/Pivot/AnimPivot/Camera/FPSCamera (docs/FSM_MAP.md); Unity-free, applied by
    // Game/RestLink.cs.
    public static class LoadReset
    {
        // Where an FSM goes to end what it was doing: its idle state, or the one that winds the
        // action down. Names are FSM names under the first-person camera.
        public const string Piss = "Logic";                 // Piss :: Logic
        public const string Hands = "PlayerFunctions";      // push stance, finger, thumb, fist, watch, smoking
        public const string Drink = "Drink";                // FPSCamera/Drink
        public const string Zoom = "CameraSettings";        // FPSCamera/CameraSettings
        public const string Crouch = "Crouch";              // PLAYER :: Crouch

        // The state an FSM in `state` is sent to, or null to leave it (it is idle already, or it
        // isn't in the list: seated, in a car, at the computer).
        public static string Target(string fsm, string state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            switch (fsm)
            {
                case Piss:
                    // A running stream ends the way MSC ends it (State 1: stops the stream, its sound and
                    // the DrinkPiss check on the states' exit); the rest of the way in goes back to the start.
                    if (state == "Full power" || state == "Pump" || state == "State 4") return "State 1";
                    return state == "Start urinate" || state == "State 1" || state == "Seated" ? null : "Start urinate";
                case Hands:
                    return state == "Initiate" || state == "Computer" ? null : "Initiate";
                case Drink:
                    return state == "State 4" ? null : "State 4";
                case Zoom:
                    switch (state)
                    {
                        case "Calculate Zoom":
                        case "Zoom in":
                        case "Zoomed":
                        case "Off":
                            return "Zoom out";
                        case "Mouse":
                        case "Zoom in 2":
                        case "Zoomed in":
                            return "Zoom out 2";
                        default:
                            return null;
                    }
                case Crouch:
                    switch (state)
                    {
                        case "Move down 1":
                        case "Crouched 1":
                        case "Move down 2":
                        case "Crouched 2":
                            return "Move up";
                        default:
                            return null;
                    }
                default:
                    return null;
            }
        }

        // Objects below the first-person camera that are switched off: the hands of push, finger,
        // thumb, fist and watch, and the cigarette. The piss stream is ended by its own FSM.
        public static readonly string[] Objects = new[]
        {
            "Hand Push", "Lift", "MiddleFinger", "Fist", "Watch", "Smoking",
        };

        // Global bools that say a hand is busy or a drink is on.
        public static readonly string[] Flags = new[]
        {
            "PlayerHandLeft", "PlayerHandRight", "PlayerDrinkOn", "PlayerLift",
        };

        // Global events that let go of what the hand carries (any state of Hand :: PickUp).
        public static readonly string[] Drops = new[] { "DROP_PART", "DROP_TOOL" };
    }
}
