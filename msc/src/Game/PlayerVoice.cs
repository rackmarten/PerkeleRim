using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's player speaks for Skyrim's while MSC has it (Skyrim's own shout voice and grunts are
    // muted meanwhile). The player shouted (PlayerShout): MSC yells instead, the clip ShoutLine picks
    // among MSC's voice clips: the player's swear01..swear16 with their subtitle lines
    // (ShoutLine.SwearLines) and the Human_ soundbank clips, which have none. Something hurt the
    // player (PlayerHurt): a grunt (the player's ouch clips) or a swear, as HurtLine decides. Played
    // straight on the listener in 2D, like DeathGuard's death grunt, so it's heard wherever the
    // player is.
    public unsafe class PlayerVoice
    {
        readonly Action<string> log;
        readonly System.Random random = new System.Random();
        readonly HurtLine hurt = new HurtLine();
        AudioSource voice;
        Dictionary<string, AudioClip> clips;  // by MasterAudio variation name and by clip name, found on first use
        List<KeyValuePair<string, string>> lines;  // clip names with their subtitle line
        List<string> grunts;  // clip names
        const float SwearEvery = 3f;
        float nextSwear;

        public PlayerVoice(Action<string> log)
        {
            this.log = log;
        }

        public void OnShout(byte[] payload, int size)
        {
            if (size < sizeof(PlayerShout)) return;
            PlayerShout s;
            fixed (byte* p = payload) s = *(PlayerShout*)p;
            Shout(s.kind, $"shout {s.shout:X8}, {s.words} words");
        }

        // Also the spike's "shout 0|1".
        public void Shout(uint kind, string what)
        {
            Find();
            Play(ShoutLine.Pick(kind, lines, random.NextDouble(), random.NextDouble()), "yells", what);
        }

        public void OnHurt(byte[] payload, int size)
        {
            if (size < sizeof(PlayerHurt)) return;
            PlayerHurt h;
            fixed (byte* p = payload) h = *(PlayerHurt*)p;
            Hurt(h.cause, h.damage, $"{CauseName(h.cause)}, {h.damage * 100f:0}% damage, {h.health * 100f:0}% health left");
        }

        // Also the spike's "hurt <cause> [damage]".
        public void Hurt(uint cause, float damage, string what)
        {
            var sound = hurt.Decide(cause, damage, Time.realtimeSinceStartup, random.NextDouble());
            if (sound == HurtSound.None) log($"hurt: quiet for {what} (cooldown)");
            else Say(sound, what);
        }

        // The vehicle the player drives hit someone: a swear, at most one every SwearEvery seconds
        // (plowing through a crowd is one outburst).
        public void Swear(string what)
        {
            if (Time.realtimeSinceStartup < nextSwear) return;
            nextSwear = Time.realtimeSinceStartup + SwearEvery;
            Say(HurtSound.Swear, what);
        }

        // Every frame: a low health swear that waited for the hurt cooldown.
        public void Update()
        {
            var sound = hurt.Due(Time.realtimeSinceStartup);
            if (sound != HurtSound.None) Say(sound, "low health, after the cooldown");
        }

        void Say(HurtSound sound, string what)
        {
            Find();
            var name = sound == HurtSound.Grunt ? HurtLine.One(grunts, random.NextDouble()) : HurtLine.PickSwear(lines, random.NextDouble());
            Play(name, sound == HurtSound.Grunt ? "grunts" : "swears", what);
        }

        static string CauseName(uint cause)
        {
            switch (cause)
            {
                case Wire.HurtHit: return "hit";
                case Wire.HurtBlocked: return "blocked hit";
                case Wire.HurtFall: return "fall";
                case Wire.HurtCrash: return "crash";
                case Wire.HurtLow: return "low health";
                default: return $"cause {cause}";
            }
        }

        void Play(string name, string verb, string what)
        {
            var listener = UnityEngine.Object.FindObjectOfType<AudioListener>();
            if (name == null || listener == null)
            {
                log($"voice: nothing to say for {what} ({lines.Count} lines, {grunts.Count} grunts, listener {(listener != null)})");
                return;
            }
            if (voice == null || voice.gameObject != listener.gameObject) voice = listener.gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 0f;
            voice.PlayOneShot(clips[name], 1f);
            string line = "";
            foreach (var l in lines) if (l.Key == name && !string.IsNullOrEmpty(l.Value)) line = $" \"{l.Value}\"";
            log($"voice: the player {verb} {name}{line} ({what})");
        }

        void Find()
        {
            if (lines != null && lines.Count > 0 && grunts.Count > 0) return;
            clips = new Dictionary<string, AudioClip>();
            lines = new List<KeyValuePair<string, string>>();
            grunts = new List<string>();
            foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
            {
                if (source == null || source.clip == null || !UnderMasterAudio(source.transform)) continue;
                if (!clips.ContainsKey(source.gameObject.name)) clips[source.gameObject.name] = source.clip;
                if (!clips.ContainsKey(source.clip.name)) clips[source.clip.name] = source.clip;
                if (HurtLine.IsGrunt(source.clip.name) && !grunts.Contains(source.clip.name)) grunts.Add(source.clip.name);
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
            log($"voice: {lines.Count} MSC voice clips, {grunts.Count} grunts ({string.Join(", ", grunts.ToArray())}); Perkeles: {string.Join(", ", perkeles.ToArray())}");
        }

        static bool UnderMasterAudio(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name == "MasterAudio") return true;
            return false;
        }
    }
}
