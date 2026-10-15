using System.Collections.Generic;

namespace PerkeleRim
{
    // Puts MSC's hand actions next to WASD while Skyrim has the keyboard: each alias key below
    // becomes the key MSC's action is bound to, for as long as it is held. MSC's own keys (N, P, O,
    // J, I) still work, and its bindings stay as they are, so standalone MSC keeps its keys. M never
    // arrives (it opens Skyrim's map, protocol/cpp/input.h), so the finger moves to Z. In a vehicle the
    // on-foot aliases stay MSC's car keys (X clutch, G and B shifting, T boost). The key table is in
    // docs/PROTOCOL.md, Input. Unity-free.
    public sealed class KeyRemap
    {
        // Unity KeyCodes
        public const int Z = 122, X = 120, G = 103, T = 116, B = 98;
        public const int Mouse2 = 325;  // middle button

        struct Alias
        {
            public int Key;
            public string Action;  // MSC's cInput action
            public bool OnFootOnly;
        }

        static readonly Alias[] Aliases =
        {
            new Alias { Key = Z, Action = "Finger" },
            new Alias { Key = Mouse2, Action = "Swear" },
            new Alias { Key = X, Action = "Urinate", OnFootOnly = true },
            new Alias { Key = G, Action = "Push", OnFootOnly = true },
            new Alias { Key = T, Action = "Hitchhike", OnFootOnly = true },
            new Alias { Key = B, Action = "Smoking", OnFootOnly = true },
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
            foreach (var a in Aliases)
            {
                if (a.OnFootOnly && driving) continue;
                if (!keys.Remove(a.Key)) continue;
                targets.Add(bound[a.Action]);
            }
            foreach (var k in targets) if (!keys.Contains(k)) keys.Add(k);
        }
    }
}
