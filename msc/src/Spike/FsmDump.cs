using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Writes PlayMaker FSMs, global variables and cInput bindings to text files in the game folder:
    //   PerkeleRim_fsm_<scene>_index.txt   every FSM: path, name, active state, state names
    //   PerkeleRim_fsm_<scene>_detail.txt  full states/actions/transitions for FSMs matching the keywords
    public static class FsmDump
    {
        public static readonly string[] DefaultKeywords =
        {
            "PLAYER", "Drink", "Piss", "Urin", "Crouch", "Death", "Drunk", "Hunger", "Thirst", "Fatigue",
            "Sleep", "Mount", "Seat", "Drive",
        };

        public static void Run(string[] keywords)
        {
            if (keywords == null || keywords.Length == 0) keywords = DefaultKeywords;
            string scene = Application.loadedLevelName;
            var index = new StringBuilder();
            var detail = new StringBuilder();

            detail.AppendLine("# Keywords: " + string.Join(", ", keywords));
            detail.AppendLine();
            DumpBindings(detail);
            DumpVariables(detail, "Global variables", FsmVariables.GlobalVariables, "");

            var fsms = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM));
            int detailed = 0;
            foreach (PlayMakerFSM fsm in fsms)
            {
                if (fsm == null || fsm.gameObject == null) continue;
                string path = PathOf(fsm.transform);
                string head = $"{path} :: {fsm.FsmName}";
                var states = fsm.FsmStates ?? new FsmState[0];
                var names = new string[states.Length];
                for (int i = 0; i < states.Length; i++) names[i] = states[i].Name;
                index.AppendLine($"{head} [{fsm.ActiveStateName}] {{{string.Join(", ", names)}}}");

                if (!Matches(head, keywords)) continue;
                detailed++;
                DumpFsm(detail, fsm, head);
            }

            File.WriteAllText(Path.Combine(SpikeLog.GameDir, $"PerkeleRim_fsm_{scene}_index.txt"), index.ToString());
            File.WriteAllText(Path.Combine(SpikeLog.GameDir, $"PerkeleRim_fsm_{scene}_detail.txt"), detail.ToString());
            SpikeLog.Write($"dump: scene {scene}, {fsms.Length} FSMs, {detailed} detailed");
        }

        static bool Matches(string s, string[] keywords)
        {
            foreach (var k in keywords)
                if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string PathOf(Transform t)
        {
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        static void DumpBindings(StringBuilder sb)
        {
            sb.AppendLine("## cInput bindings (action: primary | secondary)");
            try
            {
                for (int i = 0; i < cInput.length; i++)
                    sb.AppendLine($"- {cInput.GetText(i, 0)}: {cInput.GetText(i, 1)} | {cInput.GetText(i, 2)}");
            }
            catch (Exception e) { sb.AppendLine("(failed: " + e.Message + ")"); }
            sb.AppendLine();
        }

        static void DumpVariables(StringBuilder sb, string title, FsmVariables vars, string indent)
        {
            if (vars == null) return;
            NamedVariable[] all;
            try { all = vars.GetAllNamedVariables(); }
            catch { return; }
            if (all.Length == 0) return;
            sb.AppendLine(indent + "## " + title);
            foreach (var v in all) sb.AppendLine($"{indent}- {v.GetType().Name.Replace("Fsm", "")} {v.Name} = {Clip(v.ToString())}");
            sb.AppendLine();
        }

        static void DumpFsm(StringBuilder sb, PlayMakerFSM fsm, string head)
        {
            sb.AppendLine($"# {head}");
            sb.AppendLine($"active: {fsm.ActiveStateName}, start: {fsm.Fsm.StartState}");
            if (fsm.FsmEvents != null && fsm.FsmEvents.Length > 0)
            {
                var evs = new string[fsm.FsmEvents.Length];
                for (int i = 0; i < evs.Length; i++) evs[i] = fsm.FsmEvents[i].Name;
                sb.AppendLine("events: " + string.Join(", ", evs));
            }
            foreach (var t in fsm.FsmGlobalTransitions ?? new FsmTransition[0])
                sb.AppendLine($"global: {t.EventName} -> {t.ToState}");
            DumpVariables(sb, "variables", fsm.FsmVariables, "  ");

            foreach (var state in fsm.FsmStates ?? new FsmState[0])
            {
                sb.AppendLine($"  ## {state.Name}");
                try
                {
                    foreach (var action in state.Actions ?? new FsmStateAction[0])
                        sb.AppendLine("    " + DescribeAction(action));
                }
                catch (Exception e) { sb.AppendLine("    (actions failed: " + e.Message + ")"); }
                foreach (var t in state.Transitions ?? new FsmTransition[0])
                    sb.AppendLine($"    {t.EventName} -> {t.ToState}");
            }
            sb.AppendLine();
        }

        static string DescribeAction(FsmStateAction action)
        {
            if (action == null) return "(null)";
            var type = action.GetType();
            var sb = new StringBuilder(type.Name).Append('(');
            bool first = true;
            for (var t = type; t != null && t != typeof(FsmStateAction); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    object value;
                    try { value = f.GetValue(action); } catch { value = "?"; }
                    sb.Append(f.Name).Append('=').Append(Format(value));
                }
            }
            return sb.Append(')').ToString();
        }

        static string Format(object value)
        {
            switch (value)
            {
                case null: return "null";
                case NamedVariable nv: return nv.UseVariable ? "{" + nv.Name + "}" : Clip(nv.ToString());
                case FsmEvent ev: return "evt:" + ev.Name;
                case FsmOwnerDefault od: return od.OwnerOption == OwnerDefaultOption.UseOwner ? "owner" : Format(od.GameObject);
                case UnityEngine.Object uo: return uo == null ? "null" : uo.name;
                case string s: return '"' + Clip(s) + '"';
                case IList list:
                    var parts = new string[Math.Min(list.Count, 6)];
                    for (int i = 0; i < parts.Length; i++) parts[i] = Format(list[i]);
                    return "[" + string.Join(", ", parts) + (list.Count > parts.Length ? ", ..." : "") + "]";
                default: return Clip(value.ToString());
            }
        }

        static string Clip(string s)
        {
            if (s == null) return "null";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
        }
    }
}
