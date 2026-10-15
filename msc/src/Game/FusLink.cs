using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's middle finger (V while handed over, KeyRemap) casts Fus in Skyrim: when PlayerFunctions
    // starts the Finger gesture (FusTrigger, at most once per cooldown), Skyrim gets a MiddleFinger
    // along MSC's camera. On foot and in vehicles alike. Swearing stays MSC's own.
    public unsafe class FusLink
    {
        readonly Action<string> log;
        readonly FusTrigger trigger = new FusTrigger();
        PlayMakerFSM functions;

        public FusLink(Action<string> log)
        {
            this.log = log;
        }

        // Call every frame. playing: MSC owns the player and it stands on the current island;
        // fpsCamera: the player's FPSCamera (it holds PlayerFunctions); view: MSC's rendering camera.
        public void Update(Link link, bool playing, Transform fpsCamera, Transform view)
        {
            if (!playing || fpsCamera == null || view == null)
            {
                trigger.Reset();
                return;
            }
            if (functions == null)
            {
                foreach (var f in fpsCamera.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "PlayerFunctions") functions = f;
                if (functions == null) return;
            }
            if (!trigger.Step(functions.ActiveStateName, Time.time)) return;

            var way = view.forward;
            var skyrimWay = Coords.UnityToSkyrim(new Vec3(way.x, way.y, way.z));
            float length = Mathf.Sqrt(skyrimWay.x * skyrimWay.x + skyrimWay.y * skyrimWay.y + skyrimWay.z * skyrimWay.z);
            var finger = new MiddleFinger();
            finger.direction[0] = skyrimWay.x / length;
            finger.direction[1] = skyrimWay.y / length;
            finger.direction[2] = skyrimWay.z / length;
            var payload = new byte[sizeof(MiddleFinger)];
            fixed (byte* p = payload) *(MiddleFinger*)p = finger;
            bool sent = link.Send(MessageType.MiddleFinger, payload, payload.Length);
            log($"fus: middle finger{(sent ? "" : ", but the ring is full")}");
        }
    }
}
