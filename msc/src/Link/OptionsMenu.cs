namespace PerkeleRim
{
    // MSC's options menu while Skyrim shows it (F1, InputState kInputMscMenu; docs/PROTOCOL.md, "MSC's
    // menu in Skyrim"): when to open it, and which of its buttons do nothing from Skyrim. Unity-free;
    // msc/src/Game/MscMenuLink.cs does the Unity side.
    public sealed class OptionsMenu
    {
        public const float RetrySeconds = 1f;  // the menu didn't come up from a press: press again

        // Leaving MSC's game from Skyrim would drop the link and Kumpo with it.
        static readonly string[] Ignored = { "Btn_Quit", "Btn_ConfirmQuit" };

        float nextOpen;

        // Skyrim wants the menu up: PlayerLink stops closing it.
        public bool Holding { get; private set; }

        // Every frame. requested: InputState asks for the menu (and MSC has the player); open: it is
        // up. True when MSC should open it this frame.
        public bool Step(bool requested, bool open, float now)
        {
            if (!requested)
            {
                Holding = false;
                nextOpen = 0f;
                return false;
            }
            Holding = true;
            if (open || now < nextOpen) return false;
            nextOpen = now + RetrySeconds;
            return true;
        }

        // Whether Skyrim's pause freezes MSC's clock (kInputPaused). Not while Skyrim shows MSC's menu:
        // its toggles sit out a 0.2 s scaled Wait after each flip and its sliders move by Mouse X per
        // second, so with time frozen neither worked. MSC never pauses for its own menu either.
        public static bool Freezes(bool skyrimPaused, bool menuShown) => skyrimPaused && !menuShown;

        // A button below Systems/OptionsMenu/Menu that is off while Skyrim shows the menu.
        public static bool IsIgnored(string buttonName)
        {
            foreach (var name in Ignored)
                if (buttonName == name) return true;
            return false;
        }
    }
}
