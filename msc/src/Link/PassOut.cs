namespace PerkeleRim
{
    // MSC's blackouts, kept from running while Skyrim drives MSC's player (Game/PassOutGuard).
    // Systems/PassOut :: Activate waits in Test alc for PlayerDrunk above 3.8, then passes out:
    // Pass out (sleep_on, GameVolume down), Move, Calc rates and the day advance, Where? (parents
    // PLAYER to WakePivot), Random pos or Cottage, Set position (WakePivot to a spawn on MSC's map),
    // Wake up, AnimateWake (turns WakePivot to stand up) and State 5 (PLAYER let go, MouseLook back
    // on), back to Test alc. Systems/KnockOut :: Activate does the same from State 1. Skyrim's
    // player follows MSC's, so the move throws Kumpo across Skyrim's map, and while PLAYER hangs
    // from WakePivot the island puts him kilometres from it and AnimateWake swings him around it.
    // Unity-free, so the tests cover it.
    public static class PassOut
    {
        public const string Fsm = "Activate";
        public const string PassOutObject = "PassOut";    // Systems/PassOut
        public const string KnockOutObject = "KnockOut";  // Systems/KnockOut
        public static readonly string[] Objects = { PassOutObject, KnockOutObject };

        // Their FSM variables: the player, its camera and the pivot it is turned about while waking.
        public const string SavePlayer = "SavePlayer";
        public const string SavePlayerCam = "SavePlayerCam";
        public const string WakePivot = "WakePivot";
        // The state that switches the blackout on (ActivateGameObject, sleep_on).
        public const string PassOutState = "Pass out";

        // The state the FSM waits in until a blackout starts, or null for one this doesn't know.
        public static string Idle(string obj)
        {
            switch (obj)
            {
                case PassOutObject: return "Test alc";
                case KnockOutObject: return "State 1";
                default: return null;
            }
        }

        // True while the FSM is anywhere in a blackout, from the first check past its idle state on.
        public static bool Running(string obj, string state)
        {
            var idle = Idle(obj);
            return idle != null && !string.IsNullOrEmpty(state) && state != idle;
        }
    }
}
