using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // Right click with something in the hand is MSC's throw. PerkeleRim draws it like Skyrim's bow:
    // the click is held back while the button is down (the item is pulled back toward the player,
    // ThrowWindUp) and goes to MSC when the button is let go, so MSC throws then. How far it flies
    // follows how long it was held: a tap throws half of MSC's distance, a second or more twice.
    // Distance goes with the speed squared, so the launch speed is scaled by the square root of that
    // (ActorLink applies it to the flight). Unity-free.
    public sealed class ThrowCharge
    {
        public const int Mouse1 = 324;          // Unity KeyCode.Mouse1
        public const float TapFactor = 0.5f;    // of MSC's throw distance, a button down and up at once
        public const float FullFactor = 2f;     // held for FullSeconds or more
        public const float FullSeconds = 1f;
        public const float WindBackMax = 0.4f;  // meters the item is pulled back at a full charge
        public const float EyeClearance = 0.25f;  // meters it stays in front of the camera at least

        bool down;
        bool released;
        float startedAt;
        float releasedAt = float.NegativeInfinity;
        float releasedFactor = 1f;

        public bool Charging { get; private set; }

        // Share of the full charge after held seconds, 0..1.
        public static float Fraction(float held) => Math.Min(Math.Max(held / FullSeconds, 0f), 1f);

        // How far the throw goes compared with MSC's own, linear in the hold time.
        public static float Factor(float held) => TapFactor + (FullFactor - TapFactor) * Fraction(held);

        // The launch speed that goes with a distance factor (range ~ speed squared).
        public static float SpeedScale(float factor) => (float)Math.Sqrt(Math.Max(factor, 0f));

        // Meters to pull an item back from where MSC's hand holds it: Fraction of WindBackMax, never
        // closer than EyeClearance to the camera. distance: how far in front of the camera it is.
        public static float WindBack(float fraction, float distance) =>
            Math.Max(0f, Math.Min(WindBackMax * Math.Min(Math.Max(fraction, 0f), 1f), distance - EyeClearance));

        // Seconds the button has been held, 0 when it is not being charged.
        public float Held(float now) => Charging ? Math.Max(0f, now - startedAt) : 0f;

        // keys: the held key codes for this MSC frame, changed in place. carrying: MSC's hand holds an
        // item it would throw. now: seconds. Call every frame, after BlockButton (which owns a
        // right click with free hands). A press that began before the item was picked up is left
        // alone: MSC only throws on the press itself.
        public void Apply(List<int> keys, bool carrying, float now)
        {
            bool pressed = keys.Contains(Mouse1);
            bool edge = pressed && !down;
            down = pressed;
            if (!carrying)
            {
                Charging = false;  // dropped or put away: nothing to throw
                return;
            }
            if (edge) { Charging = true; startedAt = now; }
            if (!Charging) return;
            if (pressed)
            {
                keys.Remove(Mouse1);
                return;
            }
            // Let go: the click reaches MSC now, for this one frame.
            released = true;
            releasedAt = now;
            releasedFactor = Factor(now - startedAt);
            Charging = false;
            keys.Add(Mouse1);
        }

        // The distance factor of the throw that left the hand: the charge of the release within window
        // seconds before now, once; 1 (MSC's own) for an item that left some other way.
        public float Take(float now, float window)
        {
            if (!released || now - releasedAt > window) return 1f;
            released = false;
            return releasedFactor;
        }

        public void Reset()
        {
            down = false;
            Charging = false;
            released = false;
            releasedAt = float.NegativeInfinity;
        }
    }
}
