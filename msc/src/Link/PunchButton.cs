using System.Collections.Generic;

namespace PerkeleRim
{
    // Left click punches when the hands are free: MSC's own punch is its Hit key (H), far from WASD
    // for a fight. What a press does is decided when it goes down: with nothing under the crosshair
    // that MSC shows a prompt for and nothing in the hands, the click becomes the Hit key until it
    // is let go; otherwise it stays MSC's click (grab, use). MSC's own bindings stay as they are.
    // MSC's hand icon flickers on and off from frame to frame while the crosshair is on an item, so
    // a prompt seen within the last BusyGrace seconds still counts. Unity-free.
    public sealed class PunchButton
    {
        public const int Mouse0 = 323;  // Unity KeyCode.Mouse0
        public const float BusyGrace = 0.5f;

        bool down;
        float lastBusy = float.NegativeInfinity;

        public bool Punching { get; private set; }

        // keys: the held key codes for this MSC frame, changed in place. free: the hands are free (see
        // above), read for this frame. hit: the KeyCode MSC's Hit action is bound to. now: seconds.
        // Call every frame, so no prompt is missed.
        public void Apply(List<int> keys, bool free, int hit, float now)
        {
            if (!free) lastBusy = now;
            bool pressed = keys.Contains(Mouse0);
            if (pressed && !down) Punching = now - lastBusy >= BusyGrace;
            if (!pressed) Punching = false;
            down = pressed;
            if (!Punching) return;
            keys.Remove(Mouse0);
            if (!keys.Contains(hit)) keys.Add(hit);
        }

        public void Reset()
        {
            down = false;
            Punching = false;
            lastBusy = float.NegativeInfinity;
        }
    }
}
