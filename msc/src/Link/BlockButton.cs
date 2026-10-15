using System.Collections.Generic;

namespace PerkeleRim
{
    // Right click with free hands guards (Skyrim's block): decided when it goes down, like
    // PunchButton's left click (same "hands free" and the same BusyGrace for a flickering prompt).
    // While held, the click reaches MSC as its Watch key, so MSC raises the left arm and looks at the
    // watch until let go, and left click and the Hit key are dropped: no punching while guarding.
    // Otherwise (an item in hand: MSC's throw) the click stays MSC's. Unity-free.
    public sealed class BlockButton
    {
        public const int Mouse1 = 324;  // Unity KeyCode.Mouse1

        bool down;
        bool watchSent;  // the Watch key went out last frame
        float lastBusy = float.NegativeInfinity;

        public bool Blocking { get; private set; }

        // PlayerFunctions' watch states (U): the left hand they mark busy is the guard itself.
        public static bool Watching(string state)
        {
            return state == "Check hand 6" || state == "Anim on" || state == "Watch ON" || state == "Watch ON 2" ||
                   state == "Anim off" || state == "Close";
        }

        // keys: the held key codes for this MSC frame, changed in place. free: the hands are free (as
        // for PunchButton), read for this frame. watch, hit: the KeyCodes MSC's Watch and Hit actions
        // are bound to. functions: PlayerFunctions' active state. now: seconds. Call every frame,
        // before PunchButton.
        public void Apply(List<int> keys, bool free, int watch, int hit, string functions, float now)
        {
            if (!free) lastBusy = now;
            bool pressed = keys.Contains(Mouse1);
            if (pressed && !down) Blocking = now - lastBusy >= PunchButton.BusyGrace;
            if (!pressed) Blocking = false;
            down = pressed;
            if (!Blocking)
            {
                watchSent = false;
                return;
            }
            keys.Remove(Mouse1);
            keys.Remove(PunchButton.Mouse0);
            keys.Remove(hit);
            // PlayerFunctions only takes the Watch key's press in Initiate. Pressed again while the
            // watch was still going down, it is back there with the key held: let go for a frame.
            bool send = !(watchSent && functions == "Initiate");
            if (send && !keys.Contains(watch)) keys.Add(watch);
            watchSent = send;
        }

        public void Reset()
        {
            down = false;
            watchSent = false;
            Blocking = false;
            lastBusy = float.NegativeInfinity;
        }
    }
}
