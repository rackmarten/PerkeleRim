using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's SoundController plays its crash and scrape sounds for any collision of a non-wheel
    // collider, so a hop on a small bump on Skyrim's roads sounded like a crash. A Harmony prefix on
    // its OnCollisionEnter drops the collisions of the registered vehicles that are slower than
    // Bumps.QuietBelow; harder ones play as before.
    public static class BumpSound
    {
        static readonly List<Transform> cars = new List<Transform>();
        static bool installed;
        static Action<string> log = s => { };

        public static void Register(Transform car, Action<string> logLine)
        {
            log = logLine;
            if (!cars.Contains(car)) cars.Add(car);
            if (installed) return;
            installed = true;
            var target = typeof(SoundController).GetMethod("OnCollisionEnter", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (target == null) { log("bumps: SoundController.OnCollisionEnter not found, small bumps still play the crash sound"); return; }
            try
            {
                var harmony = HarmonyInstance.Create("PerkeleRim.BumpSound");
                harmony.Patch(target, new HarmonyMethod(typeof(BumpSound).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)), null);
                log("bumps: collisions under " + Bumps.QuietBelow + " m/s play no crash sound on the moped");
            }
            catch (Exception e) { log("bumps: patching SoundController FAILED: " + e.Message); }
        }

        static bool Prefix(SoundController __instance, Collision collInfo)
        {
            if (collInfo == null || !Bumps.QuietCollision(collInfo.relativeVelocity.magnitude)) return true;
            foreach (var car in cars)
                if (car != null && __instance.transform.IsChildOf(car)) return false;
            return true;
        }
    }
}
