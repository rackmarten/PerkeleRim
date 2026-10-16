using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // While MSC has the player, its window shows what the player stands on instead of nothing: the
    // collision island drawn (PlayerLink.SetIslandVisible: colored by slope, roads bluish green) in
    // midday light. Only the look of midday: MSC's clock runs on. MAP/SUN/Pivot/SUN :: Color steps
    // through two-hour states that also set GlobalHour and GlobalTime, so instead of switching it
    // (the old spike "day on"), what it and MAP/SUN/Pivot :: Rotation write every frame in Update (the
    // sun's angle, its light color, ambient and fog) is overwritten here in LateUpdate with Color's
    // own daytime values. Taking the player back stops that, and the FSMs put MSC's hour back on
    // their next frame.
    public sealed class DebugView
    {
        const string PivotPath = "MAP/SUN/Pivot";
        const string SunPath = "MAP/SUN/Pivot/SUN";
        // Pivot's X angle: Color sets 300 at 10:00 and 330 at 12:00, so 345 is 13:00.
        const float NoonAngle = 345f;

        readonly Action<bool> showIsland;
        readonly Action<string> log;
        bool on;
        Transform pivot;
        Light sun;
        Color sunColor, ambient, fog;

        public DebugView(Action<bool> showIsland, Action<string> log)
        {
            this.showIsland = showIsland;
            this.log = log;
        }

        // Every LateUpdate: owned = MSC has the player.
        public void Update(bool owned)
        {
            if (owned != on)
            {
                on = owned;
                showIsland(on);
                log(on ? "debug view: island shown, sun held at midday" : "debug view: island hidden, MSC's own sun");
            }
            if (on && Find()) HoldNoon();
        }

        bool Find()
        {
            if (sun != null && pivot != null) return true;
            var sunObject = GameObject.Find(SunPath);
            var pivotObject = GameObject.Find(PivotPath);
            if (sunObject == null || pivotObject == null) return false;
            PlayMakerFSM color = null;
            foreach (var f in sunObject.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Color") color = f;
            var light = sunObject.GetComponent<Light>();
            if (color == null || light == null) return false;
            sunColor = ColorOf(color, "White", new Color(0.985f, 0.957f, 0.884f));
            ambient = ColorOf(color, "Shadow", new Color(0.282f, 0.282f, 0.426f));
            fog = ColorOf(color, "RainColorDay", new Color(0.322f, 0.341f, 0.434f));
            sun = light;
            pivot = pivotObject.transform;
            log($"debug view: sun found, light {sunColor}, ambient {ambient}, fog {fog}");
            return true;
        }

        static Color ColorOf(PlayMakerFSM fsm, string name, Color fallback)
        {
            var v = fsm.FsmVariables.GetFsmColor(name);
            return v != null ? v.Value : fallback;
        }

        void HoldNoon()
        {
            pivot.localRotation = Quaternion.Euler(NoonAngle, 0f, 0f);
            sun.color = sunColor;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fogColor = fog;
        }
    }
}
