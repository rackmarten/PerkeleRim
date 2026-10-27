using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using UnityEngine;

namespace PerkeleRim.Game
{
    // UnityEngine.Input's methods are engine internal calls, which Harmony can't patch. Instead,
    // find every method in the game's assemblies that calls Input.* and transpile those call
    // sites to InputShim. cInput is included, so its key bindings resolve through the shim too.
    public static class InputRedirect
    {
        static readonly string[] TargetAssemblies =
        {
            "Assembly-CSharp", "Assembly-CSharp-firstpass", "Assembly-UnityScript",
            "Assembly-UnityScript-firstpass", "cInput", "PlayMaker",
        };

        static int callSitesReplaced;
        static readonly Dictionary<string, int> unshimmed = new Dictionary<string, int>();

        static bool applied;
        static Action<string> Log;

        // Called once at the main menu, before the game scene loads (takes about 3 s).
        public static void Apply(Action<string> log)
        {
            // Patch only once per process, even if the host object gets recreated.
            if (applied) return;
            applied = true;
            Log = log;
            var harmony = HarmonyInstance.Create("PerkeleRim.InputRedirect");
            var transpiler = new HarmonyMethod(typeof(InputRedirect).GetMethod(nameof(Transpiler), BindingFlags.Static | BindingFlags.NonPublic));
            int scanned = 0, patched = 0, failed = 0;
            var start = DateTime.Now;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (Array.IndexOf(TargetAssemblies, asm.GetName().Name) < 0) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }

                var tokenCache = new Dictionary<int, bool>();
                foreach (var type in types)
                {
                    if (type == null || type.ContainsGenericParameters) continue;
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                    foreach (var m in methods)
                    {
                        if (m.IsAbstract || m.ContainsGenericParameters) continue;
                        scanned++;
                        if (!CallsInput(m, tokenCache)) continue;
                        try { harmony.Patch(m, null, null, transpiler); patched++; }
                        catch (Exception e)
                        {
                            failed++;
                            Log($"redirect: failed {type.FullName}.{m.Name}: {e.Message}");
                        }
                    }
                }
            }

            // Unity's own OnMouseEnter/Over/Down messages (PlayMaker's MOUSE system events) come from
            // SendMouseEvents in UnityEngine, which reads the real mouse; through the shim they follow
            // Skyrim's cursor in MSC's menu (F1) and the screen center otherwise, where MSC locks it.
            var mouseEvents = typeof(Input).Assembly.GetType("UnityEngine.SendMouseEvents");
            var send = mouseEvents != null ? mouseEvents.GetMethod("DoSendMouseEvents", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) : null;
            if (send == null) Log("redirect: UnityEngine.SendMouseEvents.DoSendMouseEvents not found, Unity's mouse messages read the real mouse");
            else
            {
                try { harmony.Patch(send, null, null, transpiler); patched++; }
                catch (Exception e) { failed++; Log("redirect: failed UnityEngine.SendMouseEvents.DoSendMouseEvents: " + e.Message); }
            }

            Log($"redirect: scanned {scanned} methods, patched {patched}, failed {failed}, call sites {callSitesReplaced}, {(DateTime.Now - start).TotalMilliseconds:0} ms");
            foreach (var kv in unshimmed) Log($"redirect: left unshimmed Input.{kv.Key} x{kv.Value}");
        }

        // Cheap pre-filter: look for call/callvirt opcodes whose token resolves to UnityEngine.Input.
        // Stray 0x28/0x6F operand bytes only cost a failed resolve; the transpiler does the real match.
        static bool CallsInput(MethodInfo m, Dictionary<int, bool> tokenCache)
        {
            byte[] il;
            try { il = m.GetMethodBody()?.GetILAsByteArray(); }
            catch { return false; }
            if (il == null) return false;

            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue;
                int token = BitConverter.ToInt32(il, i + 1);
                int table = (int)((uint)token >> 24);
                if (table != 0x0A && table != 0x06) continue; // MemberRef or MethodDef

                if (!tokenCache.TryGetValue(token, out bool isInput))
                {
                    try { isInput = m.Module.ResolveMethod(token)?.DeclaringType == typeof(Input); }
                    catch { isInput = false; }
                    tokenCache[token] = isInput;
                }
                if (isInput) return true;
            }
            return false;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ci in instructions)
            {
                if ((ci.opcode == OpCodes.Call || ci.opcode == OpCodes.Callvirt) && ci.operand is MethodInfo mi && mi.DeclaringType == typeof(Input))
                {
                    var shim = FindShim(mi);
                    if (shim != null)
                    {
                        ci.opcode = OpCodes.Call;
                        ci.operand = shim;
                        callSitesReplaced++;
                    }
                    else
                    {
                        unshimmed.TryGetValue(mi.Name, out int n);
                        unshimmed[mi.Name] = n + 1;
                    }
                }
                yield return ci;
            }
        }

        static MethodInfo FindShim(MethodInfo original)
        {
            var ps = original.GetParameters();
            var types = new Type[ps.Length];
            for (int i = 0; i < ps.Length; i++) types[i] = ps[i].ParameterType;
            var shim = typeof(InputShim).GetMethod(original.Name, BindingFlags.Public | BindingFlags.Static, null, types, null);
            return shim != null && shim.ReturnType == original.ReturnType ? shim : null;
        }
    }
}
