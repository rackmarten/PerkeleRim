namespace PerkeleRim
{
    // Freezes MSC's time while a Skyrim menu pauses Skyrim (Escape, the map), so nothing rolls or
    // drains behind the menu, and gives back the time scale MSC had once Skyrim runs again.
    // Unity-free: the caller sets Time.timeScale to what Apply returns.
    public sealed class PauseClock
    {
        float saved = 1f;

        public bool Frozen { get; private set; }

        // paused: Skyrim reports its game paused this frame. current: MSC's Time.timeScale now.
        public float Apply(bool paused, float current)
        {
            if (paused && !Frozen)
            {
                saved = current;
                Frozen = true;
            }
            else if (!paused && Frozen)
            {
                Frozen = false;
                return saved;
            }
            return Frozen ? 0f : current;
        }
    }
}
