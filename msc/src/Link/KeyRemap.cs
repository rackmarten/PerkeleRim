using System.Collections.Generic;

namespace PerkeleRim
{
    // Puts MSC's hand actions next to WASD while Skyrim has the keyboard: each alias key below
    // becomes the key MSC's action is bound to, for as long as it is held. MSC's own keys (N, P, O,
    // J, I, G, B) still work, and its bindings stay as they are, so standalone MSC keeps its keys. M
    // never arrives (it opens Skyrim's map, protocol/cpp/input.h), so the finger moves to V (Z is
    // Skyrim's shout). Alt pisses (Skyrim sends it only after a clean tap, protocol/cpp/piss_tap.h), C smokes ("cigs") and B zooms, so Ctrl crouches on foot.
    // Driving, Shift and Ctrl shift gears up and down, and G and T stay MSC's shift up and boost. T
    // on foot is Skyrim's Wait menu, so the hitchhike thumb is only MSC's own O.
    // The key table is in docs/PROTOCOL.md, Input. Unity-free.
    public sealed class KeyRemap
    {
        // Unity KeyCodes
        public const int V = 118, G = 103, T = 116, B = 98, C = 99;
        public const int LeftShift = 304, LeftControl = 306, LeftAlt = 308;
        public const int Mouse2 = 325;  // middle button

        enum When { Always, OnFoot, Driving }

        struct Alias
        {
            public int Key;
            public string Action;  // MSC's cInput action
            public When When;
        }

        static readonly Alias[] Aliases =
        {
            new Alias { Key = V, Action = "Finger" },
            new Alias { Key = Mouse2, Action = "Swear" },
            new Alias { Key = C, Action = "Smoking" },
            new Alias { Key = B, Action = "Zoom" },
            new Alias { Key = LeftAlt, Action = "Urinate" },  // a tap's pulse, like a press of MSC's P
            new Alias { Key = G, Action = "Push", When = When.OnFoot },
            new Alias { Key = LeftControl, Action = "Crouch", When = When.OnFoot },
            new Alias { Key = LeftShift, Action = "ShiftUp", When = When.Driving },
            new Alias { Key = LeftControl, Action = "ShiftDown", When = When.Driving },
        };

        // MSC's default bindings (docs/FSM_MAP.md), until Bind reads the player's own.
        readonly Dictionary<string, int> bound = new Dictionary<string, int>
        {
            { "Finger", 109 },     // M
            { "Swear", 110 },      // N
            { "Urinate", 112 },    // P
            { "Push", 106 },       // J
            { "Hitchhike", 111 },  // O
            { "Smoking", 105 },    // I
            { "Crouch", 99 },      // C
            { "Zoom", 306 },       // LeftControl
            { "ShiftUp", 103 },    // G
            { "ShiftDown", 98 },   // B
        };

        readonly List<int> targets = new List<int>();

        // MSC's binding for one cInput action; actions without an alias are ignored.
        public void Bind(string action, int key)
        {
            if (bound.ContainsKey(action)) bound[action] = key;
        }

        public int KeyOf(string action) => bound[action];

        // keys: the held key codes for this MSC frame, changed in place. driving: the player sits in
        // a vehicle it drives.
        public void Apply(List<int> keys, bool driving)
        {
            targets.Clear();
            // Every alias reads the keys as they came (C held for Ctrl's crouch isn't taken for
            // smoking): sources are removed in the loop, targets added after it.
            foreach (var a in Aliases)
            {
                if (a.When == When.OnFoot && driving || a.When == When.Driving && !driving) continue;
                if (!keys.Remove(a.Key)) continue;
                targets.Add(bound[a.Action]);
            }
            foreach (var k in targets) if (!keys.Contains(k)) keys.Add(k);
        }
    }
}
