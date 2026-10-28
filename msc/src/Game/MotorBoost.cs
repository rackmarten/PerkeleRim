using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;

namespace PerkeleRim.Game
{
    // Speeds up MSC's player while Skyrim has handed it over: MSC's walk (about 2 m/s) and run
    // (3.5 m/s) are slow next to Skyrim's run and sprint. The aim is MSC walk ~ Skyrim run and
    // MSC run ~ Skyrim sprint.
    //
    // PLAYER :: WalkSpeed copies the PlayerMovementSpeed global into the CharacterMotor's max
    // speeds every frame, so setting them from outside loses the race. Instead a Harmony prefix on
    // the motor's own update rewrites them right before it moves the player, from the global (not
    // the current value), so nothing compounds and MSC's own speeds come back as soon as Active
    // goes false. CharacterMotor is UnityScript (Assembly-UnityScript), so it's found by name.
    //
    // MSC's ground acceleration is gentle: at the boosted speeds it took about 4 s to get going
    // and as long to stop. Nothing in MSC writes it, so the prefix raises it to at least
    // MinGroundAcceleration while active and puts MSC's own value back afterwards.
    //
    // Skyrim's jump is higher than MSC's, and some ledges need it: the jump's base height is
    // raised the same way, to at least JumpHeight, and MSC's own comes back afterwards.
    public static class MotorBoost
    {
        public const float Scale = 2.75f;
        const float MinGroundAcceleration = 30f;  // m/s^2: 0 to Skyrim sprint in about 0.3 s
        const float JumpHeight = 1.1f;            // meters, about Skyrim's (76 units)

        // Set every frame by PlayerLink: true while MSC plays the player for Skyrim.
        public static bool Active;
        // Set every frame by PlayerLink: what the needs do to the speed (Needs.UrineSpeed), 1 = nothing.
        public static float NeedScale = 1f;

        static bool installed;
        static FieldInfo movementField;
        static FieldInfo accelerationField;
        static FieldInfo jumpingField;
        static FieldInfo jumpHeightField;
        static readonly List<FieldInfo> speedFields = new List<FieldInfo>();
        static HutongGames.PlayMaker.FsmFloat speedGlobal;
        static readonly Dictionary<object, float> ownAcceleration = new Dictionary<object, float>();
        static readonly Dictionary<object, float> ownJumpHeight = new Dictionary<object, float>();

        public static void Install(UnityEngine.GameObject player, Action<string> log)
        {
            speedGlobal = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat("PlayerMovementSpeed");
            if (installed) return;
            var motor = player.GetComponent("CharacterMotor");
            if (motor == null) { log("player: no CharacterMotor, speed boost off"); return; }
            installed = true;

            var type = motor.GetType();
            movementField = type.GetField("movement");
            var movement = movementField?.GetValue(motor);
            string acceleration = "none";
            if (movement != null)
            {
                foreach (var name in new[] { "maxForwardSpeed", "maxSidewaysSpeed", "maxBackwardsSpeed" })
                {
                    var f = movement.GetType().GetField(name);
                    if (f != null && f.FieldType == typeof(float)) speedFields.Add(f);
                }
                accelerationField = movement.GetType().GetField("maxGroundAcceleration");
                if (accelerationField != null && accelerationField.FieldType != typeof(float)) accelerationField = null;
                if (accelerationField != null) acceleration = ((float)accelerationField.GetValue(movement)).ToString("0.##");
            }

            jumpingField = type.GetField("jumping");
            var jumping = jumpingField?.GetValue(motor);
            string jump = "none";
            if (jumping != null)
            {
                jumpHeightField = jumping.GetType().GetField("baseHeight");
                if (jumpHeightField != null && jumpHeightField.FieldType != typeof(float)) jumpHeightField = null;
                if (jumpHeightField != null) jump = ((float)jumpHeightField.GetValue(jumping)).ToString("0.##");
            }

            // Standard Assets' motor moves in UpdateFunction (called from FixedUpdate or Update);
            // fall back to those two if it was renamed.
            var harmony = HarmonyInstance.Create("PerkeleRim.MotorBoost");
            var prefix = new HarmonyMethod(typeof(MotorBoost).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var targets = new List<string>();
            var update = type.GetMethod("UpdateFunction", all);
            foreach (var m in update != null ? new[] { update } : new[] { type.GetMethod("FixedUpdate", all), type.GetMethod("Update", all) })
            {
                if (m == null) continue;
                try { harmony.Patch(m, prefix, null); targets.Add(m.Name); }
                catch (Exception e) { log($"player: speed boost patch of {m.Name} failed: {e.Message}"); }
            }
            log($"player: speed boost x{Scale} on {type.Name}.{string.Join("/", targets.ToArray())}, {speedFields.Count} speed fields, " +
                $"global {(speedGlobal != null)}, MSC ground acceleration {acceleration} (boosted to at least {MinGroundAcceleration}), " +
                $"MSC jump height {jump} m (raised to at least {JumpHeight})");
        }

        static void Prefix(object __instance)
        {
            JumpBoost(__instance);
            if (movementField == null) return;
            var movement = movementField.GetValue(__instance);
            if (movement == null) return;

            if (!Active)
            {
                float own;
                if (accelerationField != null && ownAcceleration.TryGetValue(movement, out own))
                {
                    accelerationField.SetValue(movement, own);
                    ownAcceleration.Remove(movement);
                }
                return;
            }

            if (speedGlobal != null)
            {
                float speed = speedGlobal.Value * Scale * NeedScale;
                foreach (var f in speedFields) f.SetValue(movement, speed);
            }
            if (accelerationField != null)
            {
                float current = (float)accelerationField.GetValue(movement);
                if (!ownAcceleration.ContainsKey(movement)) ownAcceleration[movement] = current;
                accelerationField.SetValue(movement, Math.Max(ownAcceleration[movement] * Scale, MinGroundAcceleration));
            }
        }

        static void JumpBoost(object motor)
        {
            if (jumpingField == null || jumpHeightField == null) return;
            var jumping = jumpingField.GetValue(motor);
            if (jumping == null) return;
            float own;
            if (!Active)
            {
                if (ownJumpHeight.TryGetValue(jumping, out own))
                {
                    jumpHeightField.SetValue(jumping, own);
                    ownJumpHeight.Remove(jumping);
                }
                return;
            }
            if (!ownJumpHeight.TryGetValue(jumping, out own)) ownJumpHeight[jumping] = own = (float)jumpHeightField.GetValue(jumping);
            jumpHeightField.SetValue(jumping, Math.Max(own, JumpHeight));
        }
    }
}
