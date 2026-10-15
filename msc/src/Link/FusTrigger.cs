namespace PerkeleRim
{
    // MSC's middle finger (Z while handed over) casts Fus in Skyrim. The gesture starts when the
    // player's PlayerFunctions FSM enters Finger (from Check hand 3, which refuses while the left
    // hand is busy; docs/FSM_MAP.md), read once a frame. A start fires at most once per Cooldown, so
    // spamming Z shouts no faster than that. Unity-free.
    public sealed class FusTrigger
    {
        public const float Cooldown = 1.5f;  // seconds
        const string FingerState = "Finger";

        string last;
        float firedAt = float.NegativeInfinity;

        // PlayerFunctions' active state this frame and the time in seconds. True when Fus fires.
        public bool Step(string state, float now)
        {
            bool started = state == FingerState && last != FingerState;
            last = state;
            if (!started || now - firedAt < Cooldown) return false;
            firedAt = now;
            return true;
        }

        // MSC doesn't have the player: a gesture still playing when it comes back isn't a new one.
        public void Reset() => last = FingerState;
    }
}
