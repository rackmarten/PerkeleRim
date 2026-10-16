using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // H honks the vehicle the player drives (MSC has no horn key, only the van's dashboard button):
    // MSC's horn sound plays while it is held, and Skyrim gets a HornEvent on the press and every
    // ResendEvery seconds after, so the actors in the vehicle's way dive aside. The sound is the
    // first AudioClip with "horn" in its name (MSC's dashboard horn, any vehicle), then "beep" (the
    // traffic's); every candidate is logged once, to see what MSC has.
    public unsafe class VehicleHorn
    {
        public const KeyCode Key = KeyCode.H;  // PlayerLink keeps it from MSC while driving
        const float ResendEvery = 1f;
        const float SlowSpeed = 1f;  // m/s: below this the event heads where the nose points

        readonly Action<string> log;
        AudioClip clip;
        bool looked;
        AudioSource source;
        float nextSend = -1f;  // while held, -1 = not honking

        public VehicleHorn(Action<string> log) => this.log = log;

        // Every LateUpdate: driven = the vehicle the player sits in while MSC has it, else null; held:
        // H is down (read from Skyrim's input, MSC never sees it while driving).
        public void Update(Link link, VehicleLink driven, bool held)
        {
            var body = driven != null ? driven.Body : null;
            if (body == null || !held)
            {
                Stop();
                return;
            }
            if (nextSend < 0f) Start(body, driven.Name);
            if (Time.realtimeSinceStartup < nextSend) return;
            nextSend = Time.realtimeSinceStartup + ResendEvery;
            Send(link, body);
        }

        void Start(Rigidbody body, string name)
        {
            nextSend = 0f;
            Find();
            if (clip == null) return;
            if (source == null || source.gameObject != body.gameObject)
            {
                if (source != null) source.Stop();
                source = body.gameObject.AddComponent<AudioSource>();
                source.spatialBlend = 0f;  // the listener sits in the vehicle
            }
            source.clip = clip;
            source.loop = true;
            source.volume = 0.8f;
            source.Play();
            log($"horn: the player honks the {name}");
        }

        void Stop()
        {
            if (nextSend < 0f) return;
            nextSend = -1f;
            if (source != null) source.Stop();
        }

        static void Send(Link link, Rigidbody body)
        {
            if (link == null) return;
            var v = body.velocity;
            var way = v.magnitude >= SlowSpeed ? v : body.transform.forward;
            way.y = 0f;
            var skyrimWay = Coords.UnityToSkyrim(new Vec3(way.x, way.y, way.z));
            float length = Mathf.Sqrt(skyrimWay.x * skyrimWay.x + skyrimWay.y * skyrimWay.y + skyrimWay.z * skyrimWay.z);
            var horn = new HornEvent { speed = v.magnitude };
            if (length > 1e-6f)
            {
                horn.direction[0] = skyrimWay.x / length;
                horn.direction[1] = skyrimWay.y / length;
                horn.direction[2] = skyrimWay.z / length;
            }
            var payload = new byte[sizeof(HornEvent)];
            fixed (byte* p = payload) *(HornEvent*)p = horn;
            link.Send(MessageType.HornEvent, payload, payload.Length);
        }

        void Find()
        {
            if (looked) return;
            looked = true;
            AudioClip beep = null;
            var names = new System.Text.StringBuilder();
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
            {
                if (c == null) continue;
                var n = c.name.ToLowerInvariant();
                if (n.Contains("horn")) { if (clip == null) clip = c; }
                else if (n.Contains("beep")) { if (beep == null) beep = c; }
                else continue;
                names.Append(' ').Append(c.name);
            }
            if (clip == null) clip = beep;
            log(clip != null ? $"horn: MSC's horn sound is {clip.name} (candidates:{names})" : "horn: no horn sound in MSC, honking is silent");
        }
    }
}
