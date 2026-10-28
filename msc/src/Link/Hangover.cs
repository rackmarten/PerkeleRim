namespace PerkeleRim
{
    // MSC's hangover (FPSCamera :: Hangover, the camera shake after sleeping drunk) kept with a
    // Skyrim save (Game/SaveSync): its float variables and its state. TimeLeft is what keeps it
    // shaking; at 0 the FSM ends the shake its own way. Unity-free, so the tests cover it.
    public static class Hangover
    {
        public const string Path = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera";
        public const string Fsm = "Hangover";
        public const string TimeLeft = "TimeLeft";

        // The state a load enters, or null to leave the FSM where it is. Only a save with a hangover
        // going while MSC has none starts it (from the saved state); otherwise the restored
        // variables are enough: a running hangover takes the save's time and strength, and a save
        // without one (TimeLeft 0, or a snapshot from before this was kept) lets it run out.
        public static string StateToEnter(string savedState, float savedTimeLeft, string currentState, float currentTimeLeft)
        {
            if (string.IsNullOrEmpty(savedState) || savedTimeLeft <= 0f || currentTimeLeft > 0f) return null;
            return savedState == currentState ? null : savedState;
        }
    }
}
