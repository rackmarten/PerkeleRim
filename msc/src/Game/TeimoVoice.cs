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
            var clip = Find(name);
            if (clip == null)
            {
                log($"teimo: no clip {name} in MSC's Teimo group");
                return;
            }
            if (source == null)
            {
                var go = new GameObject("PerkeleRim.TeimoVoice");
                UnityEngine.Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = Near;
                source.maxDistance = Far;
                source.dopplerLevel = 0f;
                source.loop = false;
            }
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
            float meters = listener != null && placed ? Vector3.Distance(listener.transform.position, source.transform.position) : 0f;
            log($"teimo: says {name} ({clip.name}, {clip.length:0.0} s){(placed ? $", {meters:0.0} m from the player" : ", in 2D")}");
        }

        // The clip of MasterAudio/Teimo/<name> (a variation: paivaa1, whose clip is päivää), else a
        // clip of that name in the same group; never another character's.
        static AudioClip Find(string name)
        {
            AudioClip byClip = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (s == null) continue;
                var parent = s.transform.parent;
                if (parent == null || parent.name != "Teimo" || parent.parent == null || parent.parent.name != "MasterAudio") continue;
                if (s.gameObject.name == name) return s.clip;  // the variation, even without a clip: nothing else stands in
                if (byClip == null && s.clip != null && s.clip.name == name) byClip = s.clip;
            }
            return byClip;
        }
    }
}
