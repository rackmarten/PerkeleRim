using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PerkeleRim.Game
{
    // A test session's MSC (TestSessionMarker) runs minimized behind a hidden Skyrim while someone may be using the
    // desktop. MSC locks the cursor while the player walks (MSCLoader's SetMouseCursorFix in the player's "Update
    // Cursor" FSM, MSC's SetCursorMode, SimpleSmoothMouseLook and PlayMakerGUI), and Unity then keeps putting the
    // cursor in the middle of MSC's window: minimized, that's off screen, so Windows pins it to the screen's top left
    // corner. Here those calls (and, unless the session keeps its sound, AudioListener.volume, which MSC's
    // SetGameVolume sets from its options) go through InputRedirect to the stand-ins below, which keep the cursor
    // free and MSC silent; Guard catches a lock from anywhere else. Mouse look doesn't need the lock: it comes over
    // the link (InputShim).
    // A player's own start has no marker, and nothing here changes.
    public static class QuietSession
    {
        public static bool Active { get; private set; }
        public static bool Muted { get; private set; }

        // Reads and deletes the marker; once per process, before InputRedirect.Apply.
        public static void Start(Action<string> log)
        {
            string marker = Path.Combine(Path.GetDirectoryName(Application.dataPath), TestSessionMarker.FileName);
            if (!File.Exists(marker)) return;
            try
            {
                bool sound;
                Active = TestSessionMarker.Read(File.ReadAllText(marker), File.GetLastWriteTimeUtc(marker), DateTime.UtcNow, out sound);
                Muted = Active && !sound;
                File.Delete(marker);
            }
            catch (Exception e) { log("test session marker: " + e.Message); }
            if (!Active)
            {
                log("test session marker too old or not launch-msc.ps1's: a normal start");
                return;
            }
            Cursor.lockState = CursorLockMode.None;
            if (Muted) AudioListener.volume = 0f;
            log($"test session: the cursor stays free{(Muted ? ", MSC is muted" : ", with sound")}");
        }

        // The engine calls InputRedirect sends here in a test session (none otherwise).
        public static Dictionary<MethodBase, MethodInfo> Redirects()
        {
            var map = new Dictionary<MethodBase, MethodInfo>();
            if (!Active) return map;
            map[typeof(Screen).GetProperty("lockCursor").GetSetMethod()] = Stand("set_lockCursor");
            map[typeof(Cursor).GetProperty("lockState").GetSetMethod()] = Stand("set_lockState");
            if (Muted) map[typeof(AudioListener).GetProperty("volume").GetSetMethod()] = Stand("set_volume");
            return map;
        }

        static MethodInfo Stand(string name) => typeof(QuietSession).GetMethod(name, BindingFlags.Public | BindingFlags.Static);

        // Each frame (LinkHost): a lock that came some other way is undone, and logged once.
        static bool guardLogged;

        public static void Guard(Action<string> log)
        {
            if (!Active || Cursor.lockState == CursorLockMode.None) return;
            if (!guardLogged) { guardLogged = true; log($"test session: cursor lock {Cursor.lockState} from a call not redirected, undone"); }
            Cursor.lockState = CursorLockMode.None;
        }

        // Stand-ins, with the signatures of the calls they replace. Confined clips the cursor too, so it's None.
        // Unlike Unity's Screen.lockCursor, set_lockCursor leaves Cursor.visible alone (it's over a minimized window).
        public static void set_lockCursor(bool locked) => Cursor.lockState = CursorLockMode.None;
        public static void set_lockState(CursorLockMode mode) => Cursor.lockState = CursorLockMode.None;
        public static void set_volume(float volume) => AudioListener.volume = 0f;
    }
}
