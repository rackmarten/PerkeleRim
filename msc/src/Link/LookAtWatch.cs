namespace PerkeleRim
{
    // Decides when MSC's look-at looks broken (#988: after a load, door handles and switches showed
    // no prompt or hand until MSC's window had focus once): one of its control surfaces stayed under
    // the crosshair for StuckAfter seconds without MSC's prompt. Reports once per target, so a
    // control that legitimately has no prompt costs one log line. Unity-free, so the tests cover it.
    public sealed class LookAtWatch
    {
        public const float StuckAfter = 1f;

        string target;      // control surface under the crosshair, null = none
        float since;        // when it got there, or when the prompt last showed on it
        bool reported;

        // a_target: the control surface under the crosshair (null for none); a_prompt: MSC shows its
        // prompt or hand. True once when the target has been there StuckAfter seconds without it.
        public bool Step(string a_target, bool a_prompt, float a_now)
        {
            if (a_target != target)
            {
                target = a_target;
                since = a_now;
                reported = false;
            }
            if (target == null || reported) return false;
            if (a_prompt) { since = a_now; return false; }
            if (a_now - since < StuckAfter) return false;
            reported = true;
            return true;
        }
    }
}
