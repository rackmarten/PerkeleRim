using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // Turns the latest InputState into what MSC's Input calls see for one frame: held key codes and
    // per-frame axis values. The mouse axes arrive accumulated (docs/PROTOCOL.md#input), so this
    // keeps the totals from the previous frame and hands out the difference. Unity-free.
    public sealed class InputFrame
    {
        bool seeded;
        double lastMouseX, lastMouseY, lastMouseWheel;

        public bool OwnsPlayer { get; private set; }
        public float MouseX { get; private set; }
        public float MouseY { get; private set; }
        public float MouseWheel { get; private set; }
        public float Horizontal { get; private set; }
        public float Vertical { get; private set; }
        public uint ScreenWidth { get; private set; }  // Skyrim back buffer, 0 = unknown
        public uint ScreenHeight { get; private set; }
        public readonly List<int> Keys = new List<int>();

        // Call once per MSC frame with the newest state.
        public void Update(ref InputState s)
        {
            // The first state after a reset or a handover only seeds the totals: Skyrim doesn't
            // publish while it owns the player, so the mouse moved since then would arrive as one
            // giant turn.
            bool wasOwned = OwnsPlayer;
            OwnsPlayer = (s.flags & Wire.InputOwnsPlayer) != 0;
            bool fresh = seeded && wasOwned && OwnsPlayer;
            MouseX = fresh ? (float)(s.mouseX - lastMouseX) : 0f;
            MouseY = fresh ? (float)(s.mouseY - lastMouseY) : 0f;
            MouseWheel = fresh ? (float)(s.mouseWheel - lastMouseWheel) : 0f;
            lastMouseX = s.mouseX;
            lastMouseY = s.mouseY;
            lastMouseWheel = s.mouseWheel;
            seeded = true;
            Horizontal = s.horizontal;
            Vertical = s.vertical;
            ScreenWidth = s.screenWidth;
            ScreenHeight = s.screenHeight;

            Keys.Clear();
            for (int i = 0; i < 512; i++)
                if (s.IsKeyDown(i)) Keys.Add(i);
        }

        // Link down or no input yet: nothing held, and the next state seeds the mouse again.
        public void Reset()
        {
            seeded = false;
            OwnsPlayer = false;
            MouseX = MouseY = MouseWheel = Horizontal = Vertical = 0f;
            ScreenWidth = ScreenHeight = 0;
            Keys.Clear();
        }
    }
}
