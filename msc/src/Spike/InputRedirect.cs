using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Harmony;
using UnityEngine;

namespace PerkeleRim.Spike
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

        public static void Apply()
        {
            // The spike host can be recreated when MSC returns to the menu; patch only once per process.
            if (applied) return;
            applied = true;
            var harmony = HarmonyInstance.Create("PerkeleRim.Spike.InputRedirect");
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
                            SpikeLog.Write($"redirect: failed {type.FullName}.{m.Name}: {e.Message}");
                        }
                    }
                }
            }

            SpikeLog.Write($"redirect: scanned {scanned} methods, patched {patched}, failed {failed}, call sites {callSitesReplaced}, {(DateTime.Now - start).TotalMilliseconds:0} ms");
            foreach (var kv in unshimmed) SpikeLog.Write($"redirect: left unshimmed Input.{kv.Key} x{kv.Value}");
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
