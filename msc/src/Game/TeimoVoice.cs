using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Teimo's voice for his stall in Riverwood (TeimoLine): MSC's own clip from its Teimo
    // MasterAudio group, by the variation's name, played from where Skyrim's Teimo stands on the
    // island, so it fades and pans as Kumpo walks about. One line at a time: a new one cuts the
    // last (Skyrim decides who speaks when, protocol/cpp/teimo_talk.h).
    public class TeimoVoice
    {
        const float Near = 2f;   // meters: full volume this close
        const float Far = 25f;   // and silent from here (Skyrim's kHears is 20 m)

        readonly Island island;
        readonly Action<string> log;
        AudioSource source;
        AudioSource till;  // the stall's till sounds (MSC's Store group), beside his lines

        public TeimoVoice(Island island, Action<string> log)
        {
            this.island = island;
            this.log = log;
        }

        public void OnLine(byte[] payload, int size)
        {
            string name;
            Vec3 at;
            if (!TeimoLines.Decode(payload, size, out name, out at))
            {
                log($"teimo: malformed TeimoLine ({size} bytes)");
                return;
            }
            Say(name, at);
        }

        // Also the spike's "teimo say <variation>", from where the player stands.
        public void Say(string name, Vec3? skyrim)
        {
            var clip = Find("Teimo", name);
            if (clip == null)
            {
                log($"teimo: no clip {name} in MSC's Teimo group");
                return;
            }
            if (source == null) source = MakeSource("PerkeleRim.TeimoVoice");
            Play(source, clip, skyrim);
            log($"teimo: says {name} ({clip.name}, {clip.length:0.0} s)");
        }

        // MSC's till sound (Store group: cash_register_1 as a good goes on the bill, cash_register_2
        // as it's paid) from where the counter is, Skyrim world space.
        public void Till(string name, Vec3 skyrim)
        {
            var clip = Find("Store", name);
            if (clip == null)
            {
                log($"teimo: no clip {name} in MSC's Store group");
                return;
            }
            if (till == null) till = MakeSource("PerkeleRim.TeimoTill");
            Play(till, clip, skyrim);
        }

        static AudioSource MakeSource(string name)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var made = go.AddComponent<AudioSource>();
            made.rolloffMode = AudioRolloffMode.Linear;
            made.minDistance = Near;
            made.maxDistance = Far;
            made.dopplerLevel = 0f;
            made.loop = false;
            return made;
        }

        void Play(AudioSource source, AudioClip clip, Vec3? skyrim)
        {
            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            bool placed = skyrim.HasValue && island.HasAnchor;
            if (placed)
            {
                var p = island.SkyrimToIsland(skyrim.Value);
                source.transform.position = new Vector3(p.x, p.y, p.z);
            }
            else if (listener != null) source.transform.position = listener.transform.position;
            source.spatialBlend = placed ? 1f : 0f;
            source.clip = clip;
            source.Play();
        }

        // The clip of MasterAudio/<group>/<name> (a variation: paivaa1, whose clip is päivää), else a
        // clip of that name in the same group; never another character's.
        static AudioClip Find(string group, string name)
        {
            AudioClip byClip = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (s == null) continue;
                var parent = s.transform.parent;
                if (parent == null || parent.name != group || parent.parent == null || parent.parent.name != "MasterAudio") continue;
                if (s.gameObject.name == name) return s.clip;  // the variation, even without a clip: nothing else stands in
                if (byClip == null && s.clip != null && s.clip.name == name) byClip = s.clip;
            }
            return byClip;
        }
    }
}
