using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Watches MSC's look-at for #988 (after a load, door handles and switches showed no prompt or
    // hand in either game until MSC's window had focus once; not reproduced since). Logs MSC's window
    // focus changes, and once per target a line with everything the look-at depends on when one of
    // MSC's control surfaces (a "Use" FSM within reach) stays under the crosshair without a prompt
    // (LookAtWatch): the camera MSC picks through, the shim's cursor, the keys MSC sees held, the
    // FSM's state and how often Unity sends its mouse messages. Quiet otherwise.
    sealed class LookAtProbe
    {
        const float Reach = 1f;  // MSC's MousePickEvent distance on doors and switches
        const float Every = 0.25f;

        readonly System.Action<string> log;
        readonly LookAtWatch watch = new LookAtWatch();
        float nextAt;
        string focus = "no focus change yet";
        float focusAt;
        int mouseEvents;
        float mouseRate;

        public LookAtProbe(System.Action<string> log) { this.log = log; }

        public void Focus(bool focused)
        {
            focus = focused ? "got focus" : "lost focus";
            focusAt = Time.realtimeSinceStartup;
            log($"look-at: MSC's window {focus}");
        }

        public void Step(bool playing)
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextAt) return;
            mouseRate = (InputRedirect.MouseEventCalls - mouseEvents) / (now - nextAt + Every);
            mouseEvents = InputRedirect.MouseEventCalls;
            nextAt = now + Every;

            var cam = Camera.main;
            var mouse = InputShim.get_mousePosition();
            PlayMakerFSM use = playing && cam != null ? UseUnder(cam.ScreenPointToRay(mouse)) : null;
            var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            var hand = globals.FindFsmBool("GUIuse");
            var text = globals.FindFsmString("GUIinteraction");
            bool prompt = (hand != null && hand.Value) || (text != null && !string.IsNullOrEmpty(text.Value));
            if (!watch.Step(use != null ? Path(use.transform) : null, prompt, now)) return;

            var sb = new StringBuilder("look-at: ");
            sb.Append(Path(use.transform)).Append(" :: Use (").Append(use.ActiveStateName).Append(") under the crosshair ");
            sb.Append($"for {LookAtWatch.StuckAfter:0} s without MSC's prompt; camera {cam.name}");
            sb.Append($" ({(cam.enabled && cam.gameObject.activeInHierarchy ? "on" : "off")}, {cam.pixelRect.width:0}x{cam.pixelRect.height:0}, {(cam.targetTexture != null ? "into a texture" : "to the screen")})");
            sb.Append($", screen {Screen.width}x{Screen.height}, shim {(InputShim.Active ? "on" : "off")}{(InputShim.HasCursor ? " with Skyrim's cursor" : "")}, mouse {mouse.x:0},{mouse.y:0}");
            var keys = new List<string>();
            foreach (var k in InputShim.Keys) keys.Add(k.ToString());
            sb.Append(", keys held [").Append(string.Join(" ", keys.ToArray())).Append(']');
            sb.Append($", cursor lock {Cursor.lockState}, MSC's window {focus}{(focusAt > 0f ? $" {now - focusAt:0} s ago" : "")}");
            sb.Append($", Unity mouse messages {mouseRate:0}/s");
            log(sb.ToString());
        }

        // The nearest "Use" FSM within reach along the ray, on any layer (doors and switches differ).
        static PlayMakerFSM UseUnder(Ray ray)
        {
            PlayMakerFSM best = null;
            float bestDistance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, Reach, ~0))
            {
                if (hit.distance >= bestDistance) continue;
                foreach (var fsm in hit.collider.GetComponents<PlayMakerFSM>())
                {
                    if (fsm.FsmName != "Use") continue;
                    best = fsm;
                    bestDistance = hit.distance;
                    break;
                }
            }
            return best;
        }

        static string Path(Transform t)
        {
            var path = t.name;
            for (int i = 0; i < 2 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
