using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The player shouted in Skyrim (PlayerShout; Skyrim's own shout voice is muted while MSC has the
    // player): MSC yells instead, the clip ShoutLine picks among MSC's voice clips: the player's
    // swear01..swear16 with their subtitle lines (ShoutLine.SwearLines) and the Human_ soundbank
    // clips, which have none. Played straight on the listener in 2D, like DeathGuard's death grunt,
    // so it's heard wherever the player is.
    public unsafe class ShoutVoice
    {
        readonly Action<string> log;
        readonly System.Random random = new System.Random();
        AudioSource voice;
        Dictionary<string, AudioClip> clips;  // by MasterAudio variation name and by clip name, found on first use
        List<KeyValuePair<string, string>> lines;  // clip names with their subtitle line

        public ShoutVoice(Action<string> log)
        {
            this.log = log;
        }

        public void OnShout(byte[] payload, int size)
        {
            if (size < sizeof(PlayerShout)) return;
            PlayerShout s;
            fixed (byte* p = payload) s = *(PlayerShout*)p;
            Play(s.kind, $"shout {s.shout:X8}, {s.words} words");
        }

        // Also the spike's "shout 0|1".
        public void Play(uint kind, string what)
        {
            if (lines == null || lines.Count == 0) Find();
            var name = ShoutLine.Pick(kind, lines, random.NextDouble(), random.NextDouble());
            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            if (name == null || listener == null)
            {
                log($"shout: nothing to yell for {what} ({lines.Count} lines, listener {(listener != null)})");
                return;
            }
            if (voice == null || voice.gameObject != listener.gameObject) voice = listener.gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 0f;
            voice.PlayOneShot(clips[name], 1f);
            string line = "";
            foreach (var l in lines) if (l.Key == name && !string.IsNullOrEmpty(l.Value)) line = $" \"{l.Value}\"";
            log($"shout: the player yells {name}{line} ({what})");
        }

        void Find()
        {
            clips = new Dictionary<string, AudioClip>();
            lines = new List<KeyValuePair<string, string>>();
            foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (source == null || source.clip == null || !UnderMasterAudio(source.transform)) continue;
                if (!clips.ContainsKey(source.gameObject.name)) clips[source.gameObject.name] = source.clip;
                if (!clips.ContainsKey(source.clip.name)) clips[source.clip.name] = source.clip;
            }
            foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (clip != null && clip.name.StartsWith(ShoutLine.Prefix) && !clips.ContainsKey(clip.name))
                    clips[clip.name] = clip;

            var seen = new HashSet<string>();
            for (int i = 0; i < ShoutLine.SwearLines.Length; i++)
            {
                var name = ShoutLine.SwearClip(i);
                if (clips.ContainsKey(name) && seen.Add(name)) lines.Add(new KeyValuePair<string, string>(name, ShoutLine.SwearLines[i]));
            }
            foreach (var name in clips.Keys)
                if (name.StartsWith(ShoutLine.Prefix) && seen.Add(name)) lines.Add(new KeyValuePair<string, string>(name, null));
            var perkeles = new List<string>();
            foreach (var l in lines) if (ShoutLine.IsThunder(l.Value) || ShoutLine.IsPerkele(l.Key, l.Value)) perkeles.Add(l.Key);
            log($"shout: {lines.Count} MSC voice clips; Perkeles: {string.Join(", ", perkeles.ToArray())}");
        }

        static bool UnderMasterAudio(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name == "MasterAudio") return true;
            return false;
        }
    }
}
