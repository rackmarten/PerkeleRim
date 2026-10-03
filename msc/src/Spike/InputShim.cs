using System.Collections.Generic;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Stand-in for UnityEngine.Input. InputRedirect rewrites every Input.* call in the game's
    // assemblies (cInput included) to the method with the same name and signature here.
    // While Active is false everything passes through to the real Input; while true the game
    // only sees the virtual state below, so it can be driven while the window has no focus.
    public static class InputShim
    {
        public static bool Active;

        // Desired state, written by whatever drives the input (spike script, later the protocol).
        public static readonly HashSet<KeyCode> Keys = new HashSet<KeyCode>();
        public static readonly HashSet<string> Buttons = new HashSet<string>();
        public static readonly Dictionary<string, float> Axes = new Dictionary<string, float>();

        // Per-frame snapshots, so Down/Up edges are stable within a frame.
        static int frame = -1;
        static HashSet<KeyCode> curKeys = new HashSet<KeyCode>(), prevKeys = new HashSet<KeyCode>();
        static HashSet<string> curButtons = new HashSet<string>(), prevButtons = new HashSet<string>();

        public static void Clear()
        {
            Keys.Clear();
            Buttons.Clear();
            Axes.Clear();
        }

        static void Sync()
        {
            int f = Time.frameCount;
            if (f == frame) return;
            frame = f;
            var k = prevKeys; prevKeys = curKeys; curKeys = k;
            curKeys.Clear();
            foreach (var key in Keys) curKeys.Add(key);
            var b = prevButtons; prevButtons = curButtons; curButtons = b;
            curButtons.Clear();
            foreach (var btn in Buttons) curButtons.Add(btn);
        }

        static float Axis(string name)
        {
            float v;
            return Axes.TryGetValue(name, out v) ? v : 0f;
        }

        static bool Held(KeyCode k) { Sync(); return curKeys.Contains(k); }
        static bool Down(KeyCode k) { Sync(); return curKeys.Contains(k) && !prevKeys.Contains(k); }
        static bool Up(KeyCode k) { Sync(); return !curKeys.Contains(k) && prevKeys.Contains(k); }

        public static float GetAxis(string axisName) => Active ? Axis(axisName) : Input.GetAxis(axisName);
        public static float GetAxisRaw(string axisName) => Active ? Axis(axisName) : Input.GetAxisRaw(axisName);

        public static bool GetButton(string buttonName) { if (!Active) return Input.GetButton(buttonName); Sync(); return curButtons.Contains(buttonName); }
        public static bool GetButtonDown(string buttonName) { if (!Active) return Input.GetButtonDown(buttonName); Sync(); return curButtons.Contains(buttonName) && !prevButtons.Contains(buttonName); }
        public static bool GetButtonUp(string buttonName) { if (!Active) return Input.GetButtonUp(buttonName); Sync(); return !curButtons.Contains(buttonName) && prevButtons.Contains(buttonName); }

        public static bool GetKey(KeyCode key) => Active ? Held(key) : Input.GetKey(key);
        public static bool GetKeyDown(KeyCode key) => Active ? Down(key) : Input.GetKeyDown(key);
        public static bool GetKeyUp(KeyCode key) => Active ? Up(key) : Input.GetKeyUp(key);

        public static bool GetKey(string name) => Active ? Held(ParseKey(name)) : Input.GetKey(name);
        public static bool GetKeyDown(string name) => Active ? Down(ParseKey(name)) : Input.GetKeyDown(name);
        public static bool GetKeyUp(string name) => Active ? Up(ParseKey(name)) : Input.GetKeyUp(name);

        public static bool GetMouseButton(int button) => Active ? Held(KeyCode.Mouse0 + button) : Input.GetMouseButton(button);
        public static bool GetMouseButtonDown(int button) => Active ? Down(KeyCode.Mouse0 + button) : Input.GetMouseButtonDown(button);
        public static bool GetMouseButtonUp(int button) => Active ? Up(KeyCode.Mouse0 + button) : Input.GetMouseButtonUp(button);

        public static bool get_anyKey() { if (!Active) return Input.anyKey; Sync(); return curKeys.Count > 0 || curButtons.Count > 0; }
        public static bool get_anyKeyDown()
        {
            if (!Active) return Input.anyKeyDown;
            Sync();
            foreach (var k in curKeys) if (!prevKeys.Contains(k)) return true;
            return false;
        }

        // Unity key names ("w", "left shift", "mouse 0") to KeyCode. Covers what MSC plausibly uses.
        static readonly Dictionary<string, KeyCode> Named = new Dictionary<string, KeyCode>
        {
            { "space", KeyCode.Space }, { "return", KeyCode.Return }, { "enter", KeyCode.Return },
            { "escape", KeyCode.Escape }, { "tab", KeyCode.Tab }, { "backspace", KeyCode.Backspace },
            { "left shift", KeyCode.LeftShift }, { "right shift", KeyCode.RightShift },
            { "left ctrl", KeyCode.LeftControl }, { "right ctrl", KeyCode.RightControl },
            { "left alt", KeyCode.LeftAlt }, { "right alt", KeyCode.RightAlt },
            { "up", KeyCode.UpArrow }, { "down", KeyCode.DownArrow }, { "left", KeyCode.LeftArrow }, { "right", KeyCode.RightArrow },
            { "mouse 0", KeyCode.Mouse0 }, { "mouse 1", KeyCode.Mouse1 }, { "mouse 2", KeyCode.Mouse2 },
        };

        public static KeyCode ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            string n = name.ToLower();
            KeyCode k;
            if (Named.TryGetValue(n, out k)) return k;
            if (n.Length == 1 && n[0] >= 'a' && n[0] <= 'z') return KeyCode.A + (n[0] - 'a');
            if (n.Length == 1 && n[0] >= '0' && n[0] <= '9') return KeyCode.Alpha0 + (n[0] - '0');
            if (n.Length >= 2 && n[0] == 'f' && int.TryParse(n.Substring(1), out int fn) && fn >= 1 && fn <= 15) return KeyCode.F1 + (fn - 1);
            return KeyCode.None;
        }
    }
}
