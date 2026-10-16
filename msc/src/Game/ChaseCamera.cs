using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's chase camera (its "photo mode"): Systems/Photomode Cam :: SwitchCamera cuts from the
    // first-person camera to CameraTV, which follows the vehicle from afar, on F9 while the player
    // sits in a vehicle (Skyrim sends its F8 as F9). F9 again, F1, Escape, an MSC menu or leaving the
    // vehicle cut back. While it is on, PlayerLink sends that camera's pose instead of the player's
    // eye; MSC's mouse ray (MousePickEvent) goes through the disabled first-person camera then, so
    // picking only works again after the cut back. Plain class in PlayerLink.
    public class ChaseCamera
    {
        const string FsmPath = "Systems/Photomode Cam";
        const string FsmName = "SwitchCamera";
        const string OnState = "Create Cam";

        readonly Action<string> log;
        PlayMakerFSM fsm;
        Camera camera;
        bool searched;
        bool wasOn, wasOwned;

        public ChaseCamera(Action<string> log) { this.log = log; }

        // The camera MSC renders with while the chase camera is on, else null.
        public Camera Active
        {
            get
            {
                Find();
                if (fsm == null || camera == null || fsm.ActiveStateName != OnState) return null;
                return camera.enabled && camera.gameObject.activeInHierarchy ? camera : null;
            }
        }

        // Once a frame: logs switches, and cuts back to the first-person camera when Skyrim takes the
        // player back (F10, death), so nothing is left looking from afar. MSC on its own keeps its F9.
        public void Update(bool owned)
        {
            bool on = Active != null;
            bool released = wasOwned && !owned;
            wasOwned = owned;
            if (on && released)
            {
                fsm.SendEvent("BACK");  // Create Cam -> State 1 (cuts back on exit) -> Wait for key
                on = false;
                log("player: chase camera off, Skyrim took the player back");
            }
            else if (on != wasOn)
            {
                log(on ? $"player: chase camera on ({camera.name}, fov {camera.fieldOfView:0})" : "player: chase camera off");
            }
            wasOn = on;
        }

        void Find()
        {
            if (searched) return;
            var go = GameObject.Find(FsmPath);
            if (go == null) return;  // not in the game scene yet
            searched = true;
            foreach (var f in go.GetComponents<PlayMakerFSM>())
                if (f.FsmName == FsmName) fsm = f;
            var tv = go.transform.Find("CameraTV");
            var cameras = tv != null ? tv.GetComponentsInChildren<Camera>(true) : new Camera[0];  // inactive until used
            camera = cameras.Length > 0 ? cameras[0] : null;
            if (fsm == null || camera == null) log($"player: chase camera not found (fsm {(fsm != null)}, camera {(camera != null)})");
        }
    }
}
