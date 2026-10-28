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
        public bool Paused { get; private set; }  // a Skyrim menu pauses the game (only while OwnsPlayer)
        public float MouseX { get; private set; }
        public float MouseY { get; private set; }
        public float MouseWheel { get; private set; }
        public float Horizontal { get; private set; }
        public float Vertical { get; private set; }
        public uint ScreenWidth { get; private set; }  // Skyrim back buffer, 0 = unknown
        public uint ScreenHeight { get; private set; }
        public bool MscMenu { get; private set; }     // Skyrim shows MSC's options menu (F1), only while OwnsPlayer
        public float CursorX { get; private set; }    // with MscMenu: Skyrim's cursor, 0..1 from the left
        public float CursorY { get; private set; }    // 0..1 from the top
        public readonly List<int> Keys = new List<int>();

        // Call once per MSC frame with the newest state.
        public void Update(ref InputState s)
        {
            // The first state after a reset or a handover only seeds the totals: Skyrim doesn't
            // publish while it owns the player, so the mouse moved since then would arrive as one
            // giant turn.
            bool wasOwned = OwnsPlayer;
            OwnsPlayer = (s.flags & Wire.InputOwnsPlayer) != 0;
            Paused = OwnsPlayer && (s.flags & Wire.InputPaused) != 0;
            MscMenu = OwnsPlayer && (s.flags & Wire.InputMscMenu) != 0;
            CursorX = MscMenu ? Clamp01(s.cursorX) : 0.5f;
            CursorY = MscMenu ? Clamp01(s.cursorY) : 0.5f;
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

        // Unity's Input.mousePosition for the cursor: pixels from the bottom left of a screen this size.
        public static void CursorPixels(float cursorX, float cursorY, int width, int height, out float x, out float y)
        {
            x = cursorX * width;
            y = (1f - cursorY) * height;
        }

        static float Clamp01(float v) => v > 0f ? (v < 1f ? v : 1f) : 0f;  // NaN goes to 0

        // Link down or no input yet: nothing held, and the next state seeds the mouse again.
        public void Reset()
        {
            seeded = false;
            OwnsPlayer = false;
            Paused = false;
            MscMenu = false;
            CursorX = CursorY = 0.5f;
            MouseX = MouseY = MouseWheel = Horizontal = Vertical = 0f;
            ScreenWidth = ScreenHeight = 0;
            Keys.Clear();
        }
    }
}
