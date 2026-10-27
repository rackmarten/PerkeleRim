using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's middle finger (V) at Skyrim's law: when PlayerFunctions starts the Finger gesture
    // (FingerTrigger), Skyrim gets a MiddleFinger along MSC's camera and puts a bounty on the player
    // for each guard or Jarl in front that sees it. On foot and in vehicles alike.
    public unsafe class FingerLink
    {
        readonly Action<string> log;
        readonly FingerTrigger trigger = new FingerTrigger();

        public FingerLink(Action<string> log)
        {
            this.log = log;
        }

        // Call every frame. playing: MSC owns the player and it stands on the current island;
        // functions: the player's PlayerFunctions FSM; view: MSC's rendering camera.
        public void Update(Link link, bool playing, PlayMakerFSM functions, Transform view)
        {
            if (!playing || functions == null || view == null)
            {
                trigger.Reset();
                return;
            }
            if (!trigger.Step(functions.ActiveStateName)) return;

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
            log($"finger: the middle finger is up{(sent ? "" : ", but the ring is full")}");
        }
    }
}
