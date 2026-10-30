namespace PerkeleRim
{
    // MSC's middle finger (V) shown to Skyrim's law. The gesture starts when the player's
    // PlayerFunctions FSM enters Finger (from Check hand 3, which refuses while the left hand is busy;
    // docs/FSM_MAP.md), read once a frame. Each start counts once; Skyrim decides who minds it.
    // Unity-free.
    public sealed class FingerTrigger
    {
        const string FingerState = "Finger";

        string last;

        // PlayerFunctions' active state this frame. True when the finger just went up.
        public bool Step(string state)
        {
            bool started = state == FingerState && last != FingerState;
            last = state;
            return started;
        }

        // MSC doesn't have the player: a gesture still playing when it comes back isn't a new one.
        public void Reset() => last = FingerState;
    }
}
