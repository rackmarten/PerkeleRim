using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using PerkeleRim.Game;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Hidden-MSC spike driver, kept as a debug tool. Lives on a DontDestroyOnLoad object and:
    //  - forces runInBackground and logs tick rate, focus, minimized state and player pose every second
    //  - polls <MSC>/PerkeleRim_cmd.txt (written by scripts/spike.ps1), runs it, then deletes it
    //
    // Commands (first line):
    //   dump [keyword ...]   FSM index + detail files (FsmDump)
    //   continue             main menu only: load the saved game as the Continue button would
    //   beer <count>         a beer case with that many bottles next to the player (BeerGift)
    //   pocket <key>         press a pocket key while handed over: Y, or 3..8 (PocketLink)
    //   hold <kind>          a new pocket kind (0 beer .. 5 macaron box) into MSC's hand, pocket untouched
    //   inspect <text>       objects named like text, inactive ones too: tag, layer, physics, FSMs
    //   items                log the items Skyrim gets (ItemLink): id, path, model id
    //   parts [word]         the Satsuma's parts as MSC's database has them: installed, bolted, bolts tight (PartLink)
    //   hands                cameras, layers and first-person renderers to PerkeleRim_hands.txt
    //   handlayer on|off|tint|save  share MSC's first-person layer with Skyrim or not (HandLayerSender); tint toggles a
    //                        red background, save writes the layer to PerkeleRim_handlayer.png;
    //                        handlayer light <ambient> <sun> sets its neutral lighting (0.5 0.8)
    //   snapshot save|load <name>  what Skyrim's MscSave / MscLoad do (SaveSync), with the snapshot in
    //                        <MSC>/PerkeleRim_snapshots/<name> and key 1
    //   island show|hide     draw the collision island, colored by slope, roads bluish green (PlayerLink;
    //                        shown by itself while MSC has the player, DebugView)
    //   island steps [m]     log the island triangles over 30 degrees within m (8) of the player whose top is within 1 m of the feet
    //   island why [m]       what the ramp pass made of the steep triangles within m (2) of where MSC's camera looks
    //   global <name> <v>    set a global PlayMaker float or int (global PlayerUrine 100, global PlayerCigarettes 20)
    //   drunk <level>        MSC's drunk level (0..8), set at its source PlayerDrunkAdjusted (by PlayerWeight),
    //                        since a global PlayerDrunk is overwritten from it the next frame
    //   fsmvar <object> <fsm> <var> <v>  set a float, int or bool of every FSM of that name on objects of
    //                        that name (fsmvar FPSCamera Hangover TimeLeft 0 ends MSC's hangover shaking)
    //   spawn <product> [n]  a shop product in front of the player, from its Spawner/CreateItems FSM
    //                        (spawn Sausages 2; Pizza, Chips, MacaronBox, Cigarettes, Beer, ...)
    //   event <NAME>         broadcast a global PlayMaker event (DRINKBEER: the beer drinking animation)
    //   aim <words>          stand at the nearest control surface ("Use" FSM) with those words in its path, looking
    //                        at it (aim doorl Handle); a second later, what the crosshair is on and MSC's prompt.
    //                        aim up: looks at the sky, where nothing has a prompt; aim stop: lets go of the look
    //   fetch <name> [eyes]  the nearest loose part named so put down by the player (fetch wheel_regula), or with eyes
    //                        held in front of his eyes until a left click picks it up (fetch door left eyes)
    //   fit <trigger...>     those parts put on through MSC's own Assembly (fit trigger_discbrake_fl), for setups
    //   freeze <root> [m|off] a vehicle's body held still (kinematic) as on a stand, lifted m metres (its wheels off the
    //                        ground, freeze SATSUMA 0.3), or let go (freeze SATSUMA off)
    //   tighten <words>      every bolt with those words in its path turned all the way in by MSC's TIGHTEN, for setups
    //   offer <trigger>      the part in MSC's hand counted as come into that place on the car (offer trigger_door_left):
    //                        MSC checks it, shows its assemble icon and waits for the left click
    //   spanner <words>      the player at the spanner in the open spanner set that fits that part's bolts, looking at
    //                        it, for the left click with the tools out that takes it (spanner door left(Clone))
    //   wrench <words>       MSC's wrench size set to the nearest bolt's with those words in its path (wrench door left(Clone))
    //   look <words>         like aim, at any enabled collider with those words in its path (a trigger too):
    //                        look trigger_door_left (a part's place on the car), look door left(Clone) BoltPM #2
    //                        (#n: the n-th in a fixed order, by path and place among siblings; a trailing $: the
    //                        path ends with the last word, look door left(Clone)$ for the part's own collider)
    //   textures <name>      read an object's model (GameObject.Find) without sending it and log it
    //   shot                 MSC's view to <MSC>/PerkeleRim_shot.png, the way F11 saves it (ViewShot)
    //   override on|off      InputShim on/off with nothing pressed (on: the link no longer feeds it)
    //   script               following lines are steps: "<seconds>; <token>; <token>..."
    //                        token = KeyCode name (W, LeftShift, Mouse0), axis "Mouse X=3",
    //                        or virtual button "button:Fire1"
    //
    // If <MSC>/PerkeleRim_autoload.txt exists, "continue" runs by itself on the main menu.
    public class SpikeHost : MonoBehaviour
    {
        // Sends SPAWNITEM to one Spawner/CreateItems FSM (it is a global event: broadcast, every shop
        // product would spawn) and moves what it made from the store's bag spot to in front of the player.
        // The spawner copies the condition of whatever lies at the bag spot (an old bag: spoiled food),
        // so the new item's Use FSM is held at Condition 100 while it loads.
        void Spawn(string product, int count)
        {
            PlayMakerFSM spawner = null;
            var items = GameObject.Find("Spawner/CreateItems");
            if (items != null)
                foreach (var f in items.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == product) spawner = f;
            var player = GameObject.Find("PLAYER");
            if (spawner == null || player == null) { SpikeLog.Write($"cmd: no Spawner/CreateItems :: {product} or no PLAYER"); return; }
            var t = player.transform;
            for (int i = 0; i < count; i++)
            {
                spawner.SendEvent("SPAWNITEM");
                var made = spawner.FsmVariables.FindFsmGameObject("New").Value;
                if (made == null) { SpikeLog.Write($"cmd: {product} spawned nothing"); return; }
                made.transform.position = t.position + t.forward * 0.8f + t.right * (0.25f * (i - (count - 1) / 2f)) + Vector3.up * 0.3f;
                var body = made.GetComponent<Rigidbody>();
                if (body != null) body.velocity = Vector3.zero;
                foreach (var f in made.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Use" && f.FsmVariables.FindFsmFloat("Condition") != null) StartCoroutine(KeepFresh(f));
                SpikeLog.Write($"cmd: spawned {made.name} in front of the player");
            }
        }

        // Tag, layer, physics and FSMs of every object whose name contains text (prefabs and inactive
        // templates included), at most 20.
        static void Inspect(string text)
        {
            int n = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                if (go.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++n > 20) { SpikeLog.Write("cmd: inspect: more than 20, stopped"); return; }
                string path = go.name;
                for (var p = go.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var body = go.GetComponent<Rigidbody>();
                var fsms = new List<string>();
                foreach (var f in go.GetComponents<PlayMakerFSM>()) fsms.Add($"{f.FsmName} [{f.ActiveStateName}]");
                SpikeLog.Write($"cmd: inspect {path} active {go.activeInHierarchy} tag {go.tag} layer {go.layer} hide {go.hideFlags} " +
                               $"body {(body != null ? $"{body.mass} kg kinematic {body.isKinematic}" : "none")} colliders {go.GetComponentsInChildren<Collider>(true).Length} " +
                               $"children {go.transform.childCount} at {go.transform.position} fsms {string.Join(", ", fsms.ToArray())}");
            }
            if (n == 0) SpikeLog.Write("cmd: inspect: nothing named like " + text);
        }

        // Stands MSC's player in a vehicle's DriveTrigger (docs/FSM_MAP.md, "Vehicle enter and exit"),
        // the active root whose name starts with prefix: its PlayerTrigger FSM then shows ENTER DRIVING
        // MODE and takes Return. For scenarios that need Kumpo at the wheel.
        static void Drive(string prefix)
        {
            var player = GameObject.Find("PLAYER");
            if (player == null || prefix.Length == 0) { SpikeLog.Write("cmd: drive: no PLAYER or no vehicle"); return; }
            foreach (Transform t in FindObjectsOfType<Transform>())
            {
                if (t.parent != null || !t.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var child in t.GetComponentsInChildren<Transform>())
                {
                    if (child.name != "DriveTrigger") continue;
                    player.transform.position = child.position;
                    SpikeLog.Write($"cmd: drive: player put in {t.name}'s DriveTrigger at {child.position}");
                    return;
                }
            }
            SpikeLog.Write("cmd: drive: no active vehicle named " + prefix + "* with a DriveTrigger");
        }

        // Teleports MSC's player next to a scene object, for filming MSC itself (the pub, the shop).
        static void Goto(string text)
        {
            var player = GameObject.Find("PLAYER");
            if (player == null) { SpikeLog.Write("cmd: goto: no PLAYER"); return; }
            GameObject found = null;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                if (go.hideFlags != HideFlags.None || !go.name.Equals(text, StringComparison.OrdinalIgnoreCase)) continue;
                // An inactive one (a far LOD) counts only under a root that is in the scene, not a prefab asset.
                if (go.activeInHierarchy) { found = go; break; }
                if (found == null && GameObject.Find(go.transform.root.name) != null) found = go;
            }
            if (found != null)
            {
                player.transform.position = found.transform.position + Vector3.up;
                SpikeLog.Write($"cmd: goto: player put above {found.name} at {player.transform.position}");
                return;
            }
            SpikeLog.Write("cmd: goto: nothing named " + text);
        }

        // Stands the player at the nearest control surface (an enabled "Use" FSM with an enabled collider, within
        // AimReach) whose path has every word of text, facing it: on the ground straight out from the
        // vehicle's side, 0.4 m from it (well within the 1 m reach of doors and switches), PLAYER turned to it,
        // the camera's MouseLook pitched at it; and keeps him looking at it for AimHold seconds, aiming again
        // from where the camera is as he settles. Logs the candidates, then a second on what the crosshair is
        // on and MSC's prompt (GUIuse, GUIinteraction): the test kit's look-at check (scenarios/look-at.json).
        // "aim up" looks at the sky instead, where nothing has a prompt; "aim stop" ends a hold. AimReach covers the
        // van on the garage's pit seen from the shack.
        const float AimReach = 60f;
        const int AimHold = 10;  // longer than a scenario's wait for the hand and its look a moment later
        int aims;  // a new aim ends the last one's hold

        void Aim(string text)
        {
            var player = GameObject.Find("PLAYER");
            var cam = Camera.main;
            if (player == null || cam == null) { SpikeLog.Write("cmd: aim: no PLAYER or no Camera.main"); return; }
            aims++;
            if (text.Length == 0) { SpikeLog.Write("cmd: aim <words in a control surface's path> (aim doorl Handle), aim up or aim stop"); return; }
            if (text.Equals("stop", StringComparison.OrdinalIgnoreCase)) { SpikeLog.Write("cmd: aim: stopped"); return; }
            if (text.Equals("up", StringComparison.OrdinalIgnoreCase))
            {
                SpikeLog.Write("cmd: aim: up, " + Pitch(cam.transform, player.transform, 80f));
                return;
            }
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            PlayMakerFSM best = null;
            float bestDistance = AimReach;
            int n = 0;
            foreach (var fsm in FindObjectsOfType<PlayMakerFSM>())
            {
                var collider = fsm.GetComponent<Collider>();
                if (fsm.FsmName != "Use" || !fsm.enabled || collider == null || !collider.enabled) continue;
                var path = FullPath(fsm.transform);
                bool all = true;
                foreach (var w in words) all &= path.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!all) continue;
                float d = Vector3.Distance(player.transform.position, fsm.transform.position);
                if (d >= AimReach) continue;
                if (++n <= 12) SpikeLog.Write($"cmd: aim: candidate {path} [{fsm.ActiveStateName}] {d:0.0} m");
                if (d < bestDistance) { best = fsm; bestDistance = d; }
            }
            if (best == null) { SpikeLog.Write($"cmd: aim: no control surface within {AimReach} m with '{text}' in its path"); return; }
            StartCoroutine(AimAt(best, player.transform));
        }

        // "fetch <name> eyes": the nearest loose part named so held still 0.6 m in front of MSC's camera, under its
        // crosshair, until MSC's hand has it (a left click, PickUp: it is then below ItemPivot) or 8 s went; then it is a
        // physics body again. The real pick-up with the part where a shelf would hold it, at any ground.
        System.Collections.IEnumerator FetchToEyes(string name)
        {
            var player = GameObject.Find("PLAYER");
            var body = player != null ? NearestLoose(name, player.transform) : null;
            if (body == null) { SpikeLog.Write("cmd: fetch: no loose part named " + name); yield break; }
            bool kinematic = body.isKinematic;
            body.isKinematic = true;
            SpikeLog.Write($"cmd: fetch: {FullPath(body.transform)} held in front of the eyes for a click");
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                if (body == null) yield break;
                var pivot = body.transform.parent;
                if (pivot != null && pivot.name == "ItemPivot") { SpikeLog.Write("cmd: fetch: MSC's hand has it"); break; }
                var cam = Camera.main;
                if (cam != null)
                {
                    body.transform.position = cam.transform.position + cam.transform.forward * 0.6f;
                    body.transform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
                }
                yield return null;
            }
            if (body != null) body.isKinematic = kinematic;
        }

        static Rigidbody NearestLoose(string name, Transform player)
        {
            Rigidbody best = null;
            float bestDistance = float.MaxValue;
            foreach (var body in FindObjectsOfType<Rigidbody>())
            {
                var t = body.transform;
                if (!t.CompareTag("PART") || t.IsChildOf(player) || !t.name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (t.parent != null && t.root.name != "CARPARTS") continue;  // loose only: a fitted door keeps its rigidbody
                float d = Vector3.Distance(player.position, t.position);
                if (d < bestDistance) { best = body; bestDistance = d; }
            }
            return best;
        }

        // "fetch <name>": the nearest loose part (a rigidbody tagged PART, at a scene root or under CARPARTS) named so, or starting so, put
        // down 1.2 m in front of the player on the ground, still: for scenarios that need one of the Satsuma's parts
        // MSC keeps elsewhere (its home garage, before the Skyrim garage has it).
        void Fetch(string name)
        {
            var player = GameObject.Find("PLAYER");
            if (player == null || name.Length == 0) { SpikeLog.Write("cmd: fetch <part name>, in game"); return; }
            var best = NearestLoose(name, player.transform);
            if (best == null) { SpikeLog.Write("cmd: fetch: no loose part named " + name); return; }
            float bestDistance = Vector3.Distance(player.transform.position, best.transform.position);
            var p = player.transform;
            // Behind, left, right, then in front (pr.satsuma here parks the car in front): the first spot whose ground
            // is the island's (no rigidbody under it) with room for the part.
            var at = Vector3.zero;
            bool found = false;
            foreach (var dir in new[] { -p.forward, -p.right, p.right, p.forward, -p.forward * 2f, -p.right * 2f, p.right * 2f })
            {
                var top = p.position + dir * 1.5f + Vector3.up * 2f;
                RaycastHit hit = default(RaycastHit);
                float nearest = float.MaxValue;
                foreach (var h in Physics.RaycastAll(top, Vector3.down, 5f))
                    if (!h.collider.isTrigger && h.distance < nearest) { hit = h; nearest = h.distance; }
                if (nearest == float.MaxValue || hit.collider.attachedRigidbody != null) continue;  // nothing, or the car, another part
                var spot = hit.point + Vector3.up * 0.5f;
                bool blocked = false;
                foreach (var c in Physics.OverlapSphere(spot + Vector3.up * 0.3f, 0.45f))
                    if (!c.isTrigger && !c.transform.IsChildOf(best.transform)) blocked = true;
                if (blocked) continue;
                at = spot;
                found = true;
                break;
            }
            if (!found) at = p.position + p.forward * 1f + Vector3.up * 0.5f;  // nowhere free (a narrow interior): just in front, as before
            best.transform.position = at;
            best.transform.rotation = Quaternion.Euler(0f, p.eulerAngles.y, 0f);
            best.velocity = Vector3.zero;
            best.angularVelocity = Vector3.zero;
            SpikeLog.Write($"cmd: fetch: {FullPath(best.transform)} from {bestDistance:0} m away, now at {at}");
        }

        // "spanner <words>": the spanner in MSC's spanner set (ITEMS/spanner set(itemx), opened with a left click on its lid)
        // whose size fits the nearest bolt with those words in its path: the player stood at it, looking at it, for the
        // left click with the tools out that takes it (2Spanner/Pick, which sets ToolWrenchSize from the spanner's "mesh"
        // scale, as the bolt's size is its own scale; docs/FSM_MAP.md, "Building the Satsuma"). The real path to a wrench
        // size; "wrench" is the shortcut.
        void Spanner(string text)
        {
            var player = GameObject.Find("PLAYER");
            if (player == null || text.Length == 0) { SpikeLog.Write("cmd: spanner <words in a part's path>, in game"); return; }
            float size;
            var bolt = NearestBolt(text, player.transform, out size);
            if (bolt == null) { SpikeLog.Write($"cmd: spanner: no bolt with '{text}' in its path"); return; }
            Collider best = null;
            float bestMiss = 0.02f;  // MSC's own tolerance
            foreach (var c in FindObjectsOfType<Collider>())
            {
                if (!c.enabled || c.gameObject.layer != 12 || c.transform.root.name != "ITEMS" || !c.name.StartsWith("spanner", StringComparison.OrdinalIgnoreCase)) continue;
                var mesh = c.transform.Find("mesh");
                if (mesh == null) continue;
                float miss = Mathf.Abs(mesh.localScale.x - size);
                if (miss <= bestMiss) { best = c; bestMiss = miss; }
            }
            if (best == null) { SpikeLog.Write($"cmd: spanner: no {size:0.###} spanner out in the spanner set (is it open?)"); return; }
            SpikeLog.Write($"cmd: spanner: {FullPath(best.transform)} for {FullPath(bolt)} (size {size:0.###})");
            aims++;
            StartCoroutine(AimAt(best, null, player.transform));
        }

        static Transform NearestBolt(string text, Transform player, out float size)
        {
            size = 0f;
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.FsmName != "Screw" || f.name != "BoltPM" || f.gameObject.hideFlags != HideFlags.None) continue;
                var path = FullPath(f.transform);
                bool all = true;
                foreach (var w in words) all &= path.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!all) continue;
                float d = Vector3.Distance(player.position, f.transform.position);
                if (d < bestDistance) { best = f.transform; bestDistance = d; }
            }
            if (best != null) size = best.localScale.x;
            return best;
        }

        // "wrench <words>": the size MSC's tools have (global ToolWrenchSize, and SelectItem's OldWrench, which the tools
        // take out again) set to the nearest bolt's whose path has every word: its x scale, as MSC's 2Spanner/Pick sets it
        // from a spanner taken out of the spanner set (docs/FSM_MAP.md, "Building the Satsuma"). A scenario's shortcut for
        // walking to the set and picking the right spanner.
        void Wrench(string text)
        {
            var player = GameObject.Find("PLAYER");
            var size = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat("ToolWrenchSize");
            if (player == null || size == null || text.Length == 0) { SpikeLog.Write("cmd: wrench <words in a part's path>, in game"); return; }
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var fsm in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)fsm;
                if (f.FsmName != "Screw" || f.name != "BoltPM" || f.gameObject.hideFlags != HideFlags.None) continue;
                var path = FullPath(f.transform);
                bool all = true;
                foreach (var w in words) all &= path.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!all) continue;
                float d = Vector3.Distance(player.transform.position, f.transform.position);
                if (d < bestDistance) { best = f.transform; bestDistance = d; }
            }
            if (best == null) { SpikeLog.Write($"cmd: wrench: no bolt with '{text}' in its path"); return; }
            size.Value = best.localScale.x;
            var select = player.transform.Find("Pivot/AnimPivot/Camera/FPSCamera/SelectItem");
            if (select != null)
                foreach (var f in select.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Selection" && f.FsmVariables.FindFsmFloat("OldWrench") != null) f.FsmVariables.FindFsmFloat("OldWrench").Value = size.Value;
            SpikeLog.Write($"cmd: wrench: ToolWrenchSize {size.Value:0.###} from {FullPath(best)} ({bestDistance:0.0} m)");
        }

        // "look <words>": like aim, at the nearest enabled collider of any kind (a trigger too: a part's place on the
        // car, trigger_door_left) whose path has every word (look door left(Clone) BoltPM: one of the door's bolts).
        void Look(string text)
        {
            var player = GameObject.Find("PLAYER");
            if (player == null || text.Length == 0) { SpikeLog.Write("cmd: look <words in an object's path>, in game"); return; }
            aims++;
            var words = new List<string>(text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int nth = 1;  // "#3": the third (a part's bolts all have one name)
            bool explicitNth = false;
            if (words.Count > 1 && words[words.Count - 1].StartsWith("#") && int.TryParse(words[words.Count - 1].Substring(1), out nth))
            {
                words.RemoveAt(words.Count - 1);
                explicitNth = true;
            }
            nth = Math.Max(1, nth);
            // "door left(Clone)$": the path ends there (the part's own collider, not its bolts' or window's).
            string ending = words.Count > 0 && words[words.Count - 1].EndsWith("$") ? words[words.Count - 1].TrimEnd('$') : null;
            var found = new List<KeyValuePair<float, Collider>>();
            foreach (var c in FindObjectsOfType<Collider>())
            {
                if (!c.enabled || c.transform.IsChildOf(player.transform)) continue;
                var path = FullPath(c.transform);
                bool all = true;
                foreach (var w in words) all &= path.IndexOf(w.TrimEnd('$'), StringComparison.OrdinalIgnoreCase) >= 0;
                if (!all || (ending != null && !path.EndsWith(ending, StringComparison.OrdinalIgnoreCase))) continue;
                float d = Vector3.Distance(player.transform.position, c.bounds.center);
                if (d < AimReach) found.Add(new KeyValuePair<float, Collider>(d, c));
            }
            // Nearest first; with #n in a fixed order instead (by path, then place among its siblings), since the
            // player moves between looks and the nearest changes: "#2" stays the same bolt.
            if (explicitNth) found.Sort((x, y) => { int c = string.CompareOrdinal(FullPath(x.Value.transform), FullPath(y.Value.transform)); return c != 0 ? c : x.Value.transform.GetSiblingIndex().CompareTo(y.Value.transform.GetSiblingIndex()); });
            else found.Sort((x, y) => x.Key.CompareTo(y.Key));
            if (found.Count < nth) { SpikeLog.Write($"cmd: look: {found.Count} within {AimReach} m with '{string.Join(" ", words.ToArray())}' in its path, no #{nth}"); return; }
            var best = found[nth - 1].Value;
            SpikeLog.Write($"cmd: look: {FullPath(best.transform)} {found[nth - 1].Key:0.0} m, #{nth} of {found.Count}");
            StartCoroutine(AimAt(best, null, player.transform));
        }

        System.Collections.IEnumerator AimAt(PlayMakerFSM target, Transform player) => AimAt(target.GetComponent<Collider>(), target, player);

        // Stands the player at a_collider (any one, a trigger too) looking at it, as above; a_fsm, when there is one, is
        // the control surface it belongs to, for the report.
        System.Collections.IEnumerator AimAt(Collider a_collider, PlayMakerFSM a_fsm, Transform player)
        {
            var target = a_collider;
            var at = target.bounds.center;
            // A solid part looked at from above (one lying on the ground, a bolt head): the top of its bounds, since its
            // middle can be as low as the ground the ray would hit first. A trigger (a part's place) by its middle.
            var ext = target.bounds.extents;
            if (a_fsm == null && !target.isTrigger && ext.y < 0.3f * Mathf.Max(ext.x, ext.z))  // lying flat, not upright
                at.y = target.bounds.max.y - Mathf.Min(0.02f, ext.y);
            var root = target.transform.root;
            // Straight out from the vehicle's side the surface is on (its right or left), else from its front or back.
            var offset = at - root.position;
            float side = Vector3.Dot(offset, root.right), along = Vector3.Dot(offset, root.forward);
            var away = Mathf.Abs(side) >= 0.3f ? root.right * Mathf.Sign(side) : root.forward * Mathf.Sign(along);
            // A loose part (its root is a folder like CARPARTS, no vehicle): from the side the player is on.
            if (root.GetComponent<Rigidbody>() == null && target.attachedRigidbody != null && target.attachedRigidbody.transform.parent == root)
                away = player.position - at;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) { away = player.position - at; away.y = 0f; }
            away.Normalize();
            // On the ground there, as high above it as the player stands now (the vehicle may be on other ground).
            float below;
            float feet = Ground(player.position, 5f, player, player, out below) ? below : 0f;
            // Out of the part's own footprint: a door lying flat is wider than the 0.4 m a handle needs.
            float clear = a_fsm == null && !target.isTrigger ? Mathf.Max(0.4f, Mathf.Min(0.9f, new Vector2(target.bounds.extents.x, target.bounds.extents.z).magnitude * 0.6f)) : 0.4f;
            var stand = at + away * clear;
            var top = new Vector3(stand.x, at.y + 1.5f, stand.z);
            // Not the vehicle's own mirrors and doors (the whole vehicle when the root is one, a rigidbody; else
            // the surface's own body), nor the player: a house's switch keeps the floor below it.
            var body = target.attachedRigidbody;
            var skip = root.GetComponent<Rigidbody>() != null ? root : body != null ? body.transform : target.transform;
            stand.y = Ground(top, 6f, skip, player, out below) ? top.y - below + feet : player.position.y;
            // A part's place on the car (a trigger), looked at with the part in hand: MSC holds it about 0.6 m along the
            // view (ItemPivot), so stand where that is the trigger's middle, as near as the eye's height allows.
            var eyeCam = Camera.main;
            if (a_fsm == null && target.isTrigger && eyeCam != null)
            {
                float dy = stand.y + (eyeCam.transform.position.y - player.position.y) - at.y;
                float flat = Mathf.Sqrt(Mathf.Max(0.6f * 0.6f - dy * dy, 0.15f * 0.15f));
                var xz = at + away * flat;
                stand.x = xz.x;
                stand.z = xz.z;
            }
            // A bolt (a trigger that isn't a part's place): of a few spots around the first, within MSC's 1 m reach,
            // the first from where the eye's ray meets this bolt before any other (a hinge's bolts sit in pairs).
            var boltSpots = new List<Vector3>();
            if (a_fsm == null && target.isTrigger && !IsPlace(target) && eyeCam != null)
            {
                var eyeUp = eyeCam.transform.position - player.position;
                foreach (float turn in new[] { 0f, 35f, -35f, 70f, -70f })
                {
                    var dir = Quaternion.Euler(0f, turn, 0f) * away;
                    foreach (float flat in new[] { 0.45f, 0.3f, 0.6f })
                    {
                        var spot = at + dir * flat;
                        spot.y = stand.y;
                        var eye = spot + eyeUp;
                        var ray = at - eye;
                        if (ray.magnitude > 0.95f) continue;
                        RaycastHit first = default(RaycastHit);
                        float nearest = float.MaxValue;
                        foreach (var h in Physics.RaycastAll(eye, ray.normalized, ray.magnitude + 0.05f, 1 << target.gameObject.layer))
                            if (h.distance < nearest) { first = h; nearest = h.distance; }
                        if (nearest == float.MaxValue || first.collider != target) continue;
                        if (!Room(spot, player)) continue;  // the player put into the door pushed it off its hinge
                        boltSpots.Add(spot);
                    }
                }
                if (boltSpots.Count > 0) stand = boltSpots[0];
                else SpikeLog.Write("cmd: aim: no spot within reach sees this bolt first, standing at the first");
            }
            int spotIndex = 0;
            player.position = stand;
            int generation = ++aims;
            for (int pass = 0; pass < AimHold * 4 && generation == aims; pass++)
            {
                if (target == null || !target.gameObject.activeInHierarchy) { SpikeLog.Write("cmd: aim: the target went away"); yield break; }
                var cam = Camera.main;
                if (cam == null) { SpikeLog.Write("cmd: aim: no Camera.main"); yield break; }
                var look = at - cam.transform.position;
                float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                float pitch = Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
                // At a trigger only the first two aims: at a part's place every turn after moves the held part, and MSC's
                // Assembly only arms again when the part comes into the trigger anew; at a bolt a turn moved the
                // crosshair off a small wheel nut while the wheel was turning it.
                if (pass == 2 && boltSpots.Count > 0 && FirstOnLayer(cam, target.gameObject.layer) != target && spotIndex + 1 < boltSpots.Count)
                {
                    // The real eye's ray met another bolt first (the predicted eye was off): the next spot, aimed anew.
                    player.position = boltSpots[++spotIndex];
                    SpikeLog.Write($"cmd: aim: another bolt was under the crosshair, spot {spotIndex + 1} of {boltSpots.Count}");
                    pass = -1;
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }
                bool still = a_fsm == null && target.isTrigger && pass >= 2;
                if (!still) player.rotation = Quaternion.Euler(0f, yaw, 0f);
                var how = $"player at {player.position}, eye {look.magnitude:0.00} m away, yaw {yaw:0}, {(still ? "held still" : Pitch(cam.transform, player, pitch))}";
                if (pass == 0) SpikeLog.Write($"cmd: aim: {FullPath(target.transform)}{(a_fsm != null ? $" [{a_fsm.ActiveStateName}]" : "")}, {how}");
                if (pass == 4) AimReport(target.transform, a_fsm, how);  // a second on
                yield return new WaitForSeconds(0.25f);  // he settles, the camera sways: aim again from where it is
            }
        }

        // "offer <trigger>": a part's place on the car (trigger_door_left, TriggerWheelFL_New, trigger_radiator) told
        // that the part in MSC's hand came into it: its Assembly FSM goes to "Find correct part", as its OnTriggerEnter
        // does, and from there it is all MSC's: it checks the hand holds its part (HandChild below ItemPivot), shows its
        // assemble icon (GUIassemble) and waits for the left click. A scenario's stand-in for holding the part exactly
        // into the trigger, which takes a player's hands.
        static void Offer(string name)
        {
            PlayMakerFSM assembly = null;
            foreach (var o in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
            {
                var f = (PlayMakerFSM)o;
                if (f.FsmName == "Assembly" && f.name == name && f.gameObject.activeInHierarchy) { assembly = f; break; }
            }
            if (assembly == null) { SpikeLog.Write($"cmd: offer: no active {name} :: Assembly (the part is on already?)"); return; }
            var held = Held();
            var want = assembly.FsmVariables.FindFsmString("HandChild");
            // Its own checks first where it has them (a wheel wants its brake on), else straight to the hand's part.
            var state = assembly.Fsm.GetState("Requirements") ?? assembly.Fsm.GetState("Find correct part") ?? assembly.Fsm.GetState("Find correct part 2");
            if (state == null || PerkeleRim.Game.SaveSync.SwitchState == null) { SpikeLog.Write($"cmd: offer: {name} has no Find correct part state"); return; }
            PerkeleRim.Game.SaveSync.SwitchState.Invoke(assembly.Fsm, new object[] { state });
            SpikeLog.Write($"cmd: offer: {FullPath(assembly.transform)} [{state.Name}], the hand holds {(held != null ? held.name : "nothing")}, it wants {(want != null ? want.Value : "?")}");
        }

        // Room for the player at feet: nothing that can be pushed (a moving body, not a trigger, not the player) within 0.3 m of two points of its
        // body, at its knees and its chest. Teleporting the player into a fitted door broke the door's hinge joint, and
        // MSC took the door off the car.
        static bool Room(Vector3 feet, Transform player)
        {
            foreach (float up in new[] { 0.4f, 1.0f })
                foreach (var c in Physics.OverlapSphere(feet + Vector3.up * up, 0.3f))
                    if (!c.isTrigger && !c.transform.IsChildOf(player) && c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;  // pushable: a hinged door, a loose part
            return true;
        }

        // The first collider on layer the camera's center ray meets within MSC's 1 m reach (MousePick's), or null.
        static Collider FirstOnLayer(Camera cam, int layer)
        {
            Collider first = null;
            float nearest = float.MaxValue;
            foreach (var h in Physics.RaycastAll(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), 1f, 1 << layer))
                if (h.distance < nearest) { first = h.collider; nearest = h.distance; }
            return first;
        }

        // A part's place on the car: a trigger with an Assembly FSM (a bolt's trigger has a Screw one).
        static bool IsPlace(Collider c)
        {
            if (!c.isTrigger) return false;
            foreach (var f in c.GetComponents<PlayMakerFSM>())
                if (f.FsmName == "Assembly") return true;
            return false;
        }

        // "freeze <root> [on|off]": the root rigidbody whose name starts so held still (kinematic, no velocity) or let go,
        // for a scenario working on the Satsuma's body as if it stood on its stand: a part's joint set up while the player
        // stood against the body once flung the whole car off.
        static void Freeze(string prefix, bool on, float lift = 0f)
        {
            foreach (var body in FindObjectsOfType<Rigidbody>())
            {
                if (body.transform.parent != null || prefix.Length == 0 || !body.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (on) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.isKinematic = on;
                // Lifted as on a stand or a jack: MSC takes a wheel off only while it doesn't touch the ground
                // (its Removal's Requirements 2 reads the wheel's SetGrip OnGroundDown).
                if (on && lift > 0f) body.transform.position += Vector3.up * lift;
                SpikeLog.Write($"cmd: freeze: {body.name} {(on ? "held still" : "let go")}{(lift > 0f ? $", lifted {lift:0.##} m" : "")}");
                return;
            }
            SpikeLog.Write("cmd: freeze: no root rigidbody named " + prefix);
        }

        // "tighten <words>": every active bolt (BoltPM :: Screw) whose path has the words sent MSC's TIGHTEN event eight
        // times, 0.35 s apart, as its wrench does per turn of the wheel: a scenario's setup (a spindle bolted on so the
        // disc brake will go on). The bolts themselves turn and count as MSC's do.
        System.Collections.IEnumerator Tighten(string text)
        {
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var bolts = new List<PlayMakerFSM>();
            foreach (var f in FindObjectsOfType<PlayMakerFSM>())
            {
                if (f.FsmName != "Screw" || f.name != "BoltPM") continue;
                var path = FullPath(f.transform);
                bool all = words.Length > 0;
                foreach (var w in words) all &= path.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0;
                if (all) bolts.Add(f);
            }
            SpikeLog.Write($"cmd: tighten: {bolts.Count} bolts with '{text}' in their path");
            for (int turn = 0; turn < 8; turn++)
            {
                foreach (var b in bolts) if (b != null) b.SendEvent("TIGHTEN");
                yield return new WaitForSeconds(0.35f);
            }
            SpikeLog.Write("cmd: tighten: done");
        }

        // What MSC's hand holds (the child of ItemPivot), or null.
        static Transform Held()
        {
            var pivot = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmGameObject("ItemPivot");
            var t = pivot != null && pivot.Value != null ? pivot.Value.transform : null;
            return t != null && t.childCount > 0 ? t.GetChild(0) : null;
        }

        // How far below from the ground is: the nearest solid hit down from there, not a trigger and nothing
        // under skip or player.
        static bool Ground(Vector3 from, float reach, Transform skip, Transform player, out float below)
        {
            below = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, reach))
            {
                var t = hit.collider.transform;
                if (!hit.collider.isTrigger && !t.IsChildOf(skip) && !t.IsChildOf(player) && hit.distance < below) below = hit.distance;
            }
            return below < float.MaxValue;
        }

        // What the crosshair is on (within the 1 m reach of doors and switches) and MSC's prompt.
        static void AimReport(Transform target, PlayMakerFSM fsm, string how)
        {
            var camera = Camera.main;
            var hits = new List<string>();
            if (camera != null)
                foreach (var hit in Physics.RaycastAll(camera.ScreenPointToRay(InputShim.get_mousePosition()), 1f, camera.cullingMask))  // 1 m: the reach on doors and switches
                    hits.Add($"{hit.collider.transform.parent?.name}/{hit.collider.name} (layer {hit.collider.gameObject.layer}) at {hit.distance:0.00} m");
            string under = hits.Count > 0 ? string.Join(", ", hits.ToArray()) : "nothing within 1 m";
            var g = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            var use = g.FindFsmBool("GUIuse");
            var interaction = g.FindFsmString("GUIinteraction");
            var assemble = g.FindFsmBool("GUIassemble");
            var disassemble = g.FindFsmBool("GUIdisassemble");
            SpikeLog.Write($"cmd: aim: a second on the crosshair is on {under}; {target.name}{(fsm != null ? $" [{fsm.ActiveStateName}]" : "")}, " +
                           $"GUIuse {(use != null && use.Value)}, GUIassemble {(assemble != null && assemble.Value)}, GUIdisassemble {(disassemble != null && disassemble.Value)}, " +
                           $"GUIinteraction '{(interaction != null ? interaction.Value : "")}'; {how}");
        }

        // Pitches MSC's camera (its MouseLook's rotationY, degrees up) on the way from the camera up to PLAYER.
        static string Pitch(Transform camera, Transform player, float degrees)
        {
            var pitched = new List<string>();
            var seen = new List<string>();
            const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            for (var t = camera; t != null && t != player; t = t.parent)
                foreach (var c in t.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var field = c.GetType().GetField("rotationY", Fields);
                    if (field == null || field.FieldType != typeof(float)) { seen.Add($"{t.name}:{c.GetType().Name}"); continue; }
                    field.SetValue(c, degrees);
                    pitched.Add($"{t.name}:{c.GetType().Name}");
                }
            return pitched.Count > 0 ? $"pitch {degrees:0} on {string.Join(", ", pitched.ToArray())}"
                                     : "nothing to pitch on the camera's way up: " + string.Join(", ", seen.ToArray());
        }

        static string FullPath(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        // Every particle system (Shuriken and legacy ParticleEmitter), inactive ones too, filtered by a
        // word in its path: what it draws with, where it simulates and whether it runs now.
        static void Particles(string text)
        {
            int n = 0;
            foreach (Component c in Resources.FindObjectsOfTypeAll(typeof(Component)))
            {
                var system = c as ParticleSystem;
                var emitter = c as ParticleEmitter;
                if (system == null && emitter == null) continue;
                if (c.gameObject.hideFlags != HideFlags.None) continue;  // prefabs and assets, not in the scene
                string path = c.name;
                for (var p = c.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                if (text.Length > 0 && path.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++n > 200) { SpikeLog.Write("cmd: particles: more than 200, stopped"); return; }
                var renderer = c.GetComponent<Renderer>();
                var mat = renderer != null ? renderer.sharedMaterial : null;
                string look = mat == null ? "no material" :
                    $"{mat.name} shader {(mat.shader != null ? mat.shader.name : "-")} tex {(mat.mainTexture != null ? mat.mainTexture.name + " " + mat.mainTexture.width + "x" + mat.mainTexture.height : "-")}";
                string run = system != null
                    ? $"shuriken playing {system.isPlaying} emit {system.enableEmission} rate {system.emissionRate} n {system.particleCount}/{system.maxParticles} " +
                      $"life {system.startLifetime} size {system.startSize} space {system.simulationSpace} sheet {(renderer is ParticleSystemRenderer ? ((ParticleSystemRenderer)renderer).renderMode.ToString() : "-")}"
                    : $"legacy emit {emitter.emit} rate {emitter.minEmission}..{emitter.maxEmission} n {emitter.particleCount} size {emitter.minSize}..{emitter.maxSize} world {emitter.useWorldSpace}";
                SpikeLog.Write($"cmd: particles {path} active {c.gameObject.activeInHierarchy} layer {c.gameObject.layer} " +
                               $"render {(renderer != null && renderer.enabled)} {run} | {look}");
            }
            if (n == 0) SpikeLog.Write("cmd: particles: none named like " + text);
        }

        // Lights of the three vehicles: Unity Light components (type, color, range, intensity, spot angle,
        // enabled and active) and every object whose name looks like a lamp.
        static void CarLights()
        {
            foreach (var root in new[] { "HAYOSIKO(1500kg, 250)", "JONNEZ ES(Clone)", "KEKMET(350-400psi)" })
            {
                var car = GameObject.Find(root);
                if (car == null) { SpikeLog.Write($"cmd: carlights: no {root}"); continue; }
                foreach (var light in car.GetComponentsInChildren<Light>(true))
                {
                    var t = light.transform;
                    string path = t.name;
                    for (var p = t.parent; p != null && p != car.transform.parent; p = p.parent) path = p.name + "/" + path;
                    var local = car.transform.InverseTransformPoint(t.position);
                    var dir = car.transform.InverseTransformDirection(t.forward);
                    SpikeLog.Write($"cmd: carlights {path}: {light.type} color {light.color} range {light.range} intensity {light.intensity} " +
                                   $"spot {light.spotAngle} shadows {light.shadows} enabled {light.enabled} active {t.gameObject.activeInHierarchy} " +
                                   $"at {local} dir {dir}");
                }
                foreach (var t in car.GetComponentsInChildren<Transform>(true))
                {
                    var n = t.name.ToLower();
                    if (n.IndexOf("light") < 0 && n.IndexOf("lamp") < 0 && n.IndexOf("beam") < 0 && n.IndexOf("interior") < 0 && n.IndexOf("dome") < 0) continue;
                    string path = t.name;
                    for (var p = t.parent; p != null && p != car.transform.parent; p = p.parent) path = p.name + "/" + path;
                    SpikeLog.Write($"cmd: carlights object {path} active {t.gameObject.activeSelf}/{t.gameObject.activeInHierarchy} " +
                                   $"fsms {t.GetComponents<PlayMakerFSM>().Length} at {car.transform.InverseTransformPoint(t.position)}");
                }
            }
        }

        // MSC's MasterAudio groups (direct children of MasterAudio) with their clips, filtered by a
        // word in the group or clip name; also loose AudioClips named like it (soundbanks).
        static void Voices(string text)
        {
            var master = GameObject.Find("MasterAudio");
            if (master == null) { SpikeLog.Write("cmd: voices: no MasterAudio"); return; }
            foreach (Transform group in master.transform)
            {
                var clips = new List<string>();
                foreach (var source in group.GetComponentsInChildren<AudioSource>(true))
                    if (source.clip != null) clips.Add($"{source.gameObject.name}={source.clip.name}");
                string all = group.name + " " + string.Join(" ", clips.ToArray());
                if (text.Length > 0 && all.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                SpikeLog.Write($"cmd: voices {group.name} ({clips.Count}): {string.Join(", ", clips.ToArray())}");
            }
            if (text.Length == 0) return;
            var loose = new List<string>();
            foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (clip != null && clip.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) loose.Add(clip.name);
            SpikeLog.Write($"cmd: voices clips like {text}: {string.Join(", ", loose.ToArray())}");
        }

        static System.Collections.IEnumerator KeepFresh(PlayMakerFSM use)
        {
            for (float until = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < until && use != null; )
            {
                use.FsmVariables.FindFsmFloat("Condition").Value = 100f;
                yield return null;
            }
            if (use != null) SpikeLog.Write($"cmd: {use.name} condition {use.FsmVariables.FindFsmFloat("Condition").Value}, state {use.ActiveStateName}");
        }

        class Step
        {
            public float Seconds;
            public List<KeyCode> Keys = new List<KeyCode>();
            public List<string> Buttons = new List<string>();
            public Dictionary<string, float> Axes = new Dictionary<string, float>();
            public string Source;
        }

        static SpikeHost instance;
        string cmdPath;
        float nextSecond;
        int updates, fixedUpdates;
        List<Step> script;
        int stepIndex;
        float stepEnds;
        bool autoloadDone;
        float menuSince = -1f;  // realtime the main menu was first seen

        static readonly string[] Needs = { "PlayerThirst", "PlayerHunger", "PlayerUrine", "PlayerFatigue", "PlayerDrunk", "PlayerStress" };

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("PerkeleRim.Spike");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SpikeHost>();
        }

        void Awake()
        {
            cmdPath = Path.Combine(SpikeLog.GameDir, "PerkeleRim_cmd.txt");
            Application.runInBackground = true;
            SpikeLog.Write("spike host started");
        }

        void FixedUpdate() => fixedUpdates++;

        void Update()
        {
            updates++;
            RunScript();

            float now = Time.realtimeSinceStartup;
            if (now < nextSecond) return;
            nextSecond = now + 1f;
            Application.runInBackground = true;
            LogProbe();
            updates = fixedUpdates = 0;
            PollCommand();

            // Real time: while Skyrim is paused (a menu open) MSC's time scale is 0 and timeSinceLevelLoad stands still.
            if (menuSince < 0f && Application.loadedLevelName == "MainMenu") menuSince = now;
            string marker = Path.Combine(SpikeLog.GameDir, AutoloadMarker.FileName);
            if (!autoloadDone && menuSince >= 0f && now - menuSince > 2f && Application.loadedLevelName == "MainMenu"
                && File.Exists(marker))
            {
                autoloadDone = true;
                SpikeLog.Write("autoload: continuing saved game");
                DropOneShotMarker(marker);
                Continue();
            }
        }

        // The launcher's marker is for this start only (AutoloadMarker); MSC started by itself later shows its menu.
        static void DropOneShotMarker(string marker)
        {
            try
            {
                if (!AutoloadMarker.IsOneShot(File.ReadAllText(marker))) return;
                File.Delete(marker);
                SpikeLog.Write("autoload: removed the launcher's marker");
            }
            catch (Exception e) { SpikeLog.Write("autoload: cannot remove the launcher's marker: " + e.Message); }
        }

        // Puts the Continue button's FSM into the state its click leads to; it then resets the
        // player globals and loads the GAME scene itself. SwitchState is private in PlayMaker 1.6.
        static void Continue()
        {
            var button = GameObject.Find("Interface/Buttons/ButtonContinue");
            PlayMakerFSM fsm = null;
            if (button != null)
                foreach (var f in button.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "SetSize") fsm = f;
            var state = fsm?.Fsm.GetState("Reset globals 2");
            if (state == null) { SpikeLog.Write("continue: button FSM not found (no save?)"); return; }
            typeof(HutongGames.PlayMaker.Fsm)
                .GetMethod("SwitchState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(fsm.Fsm, new object[] { state });
        }

        void LogProbe()
        {
            IntPtr hwnd = FindWindow("UnityWndClass", "My Summer Car");
            bool focused = hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
            bool minimized = hwnd != IntPtr.Zero && IsIconic(hwnd);
            string pose = "no PLAYER";
            var player = GameObject.Find("PLAYER");
            if (player != null)
            {
                var p = player.transform.position;
                pose = $"pos=({p.x:0.00},{p.y:0.00},{p.z:0.00}) yaw={player.transform.eulerAngles.y:0}";
                foreach (var f in player.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Crouch") pose += $" crouch='{f.ActiveStateName}'";
                foreach (var n in Needs)
                {
                    var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat(n);
                    if (v != null) pose += $" {n.Substring(6).ToLower()}={v.Value:0.#}";
                }
            }
            SpikeLog.Write($"probe fps={updates} fixed={fixedUpdates} focused={focused} minimized={minimized} " +
                           $"rib={Application.runInBackground} ts={Time.timeScale:0.##} scene={Application.loadedLevelName} " +
                           $"override={InputShim.Active} {pose}");
        }

        void PollCommand()
        {
            if (!File.Exists(cmdPath)) return;
            string[] lines;
            try { lines = File.ReadAllLines(cmdPath); File.Delete(cmdPath); }
            catch (Exception e) { SpikeLog.Write("cmd: read failed " + e.Message); return; }
            Execute(lines);
        }

        // A command line from Skyrim's hit test (the DebugCommand message), as if it came in the file.
        public static void Run(string line)
        {
            if (instance != null) instance.Execute(new[] { line });
            else SpikeLog.Write("cmd: no spike host for '" + line + "'");
        }

        void Execute(string[] lines)
        {
            if (lines.Length == 0) return;

            var words = lines[0].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return;
            SpikeLog.Write("cmd: " + lines[0].Trim());
            try
            {
                switch (words[0].ToLower())
                {
                    case "dump":
                        var kw = new string[words.Length - 1];
                        Array.Copy(words, 1, kw, 0, kw.Length);
                        FsmDump.Run(kw);
                        break;
                    case "satsuma":  // "satsuma" lists the parts on it, "satsuma here" brings it in front of the player, "satsuma half" / "satsuma bare" put half its parts on or all off
                        var satsumaLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (satsumaLink != null) satsumaLink.SatsumaCommand(words.Length > 1 ? words[1].ToLower() : "");
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "garage":  // "garage restart": the Satsuma bay as after MSC restarted with parts in it
                        var garageLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (garageLink != null) garageLink.GarageCommand(words.Length > 1 ? words[1].ToLower() : "");
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "tree":  // "tree SATSUMA(557kg, 248)" writes its whole hierarchy to PerkeleRim_tree_<name>.txt; "tree roots" every root rigidbody
                        TreeDump.Run(lines[0].Trim().Substring(4).Trim());
                        break;
                    case "override":
                        InputShim.Clear();
                        InputShim.Active = InputShim.Manual = words.Length > 1 && words[1].ToLower() == "on";
                        break;
                    case "script":
                        StartScript(lines);
                        break;
                    case "continue":
                        Continue();
                        break;
                    case "beer":
                        var link = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (link != null) link.GiveBeer(words.Length > 1 ? int.Parse(words[1]) : 1);
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "parts":  // "parts door": the Satsuma's parts as MSC's database has them, and their bolts (PartLink)
                        var partLister = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (partLister != null) partLister.DumpParts(words.Length > 1 ? words[1] : "");
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "items":
                        var lister = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (lister != null) lister.DumpItems();
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "hands":
                        HandsDump.Run();
                        break;
                    case "satsumadump":  // the Satsuma, its loose parts and MSC's part database, to PerkeleRim_satsuma.txt
                        SatsumaDump.Run();
                        break;
                    case "surfaces":  // "surfaces 15000 -14500": the island's surfaces at that Skyrim x, y (a shelf's boards)
                        var surfaceLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (surfaceLink == null || words.Length < 3) { SpikeLog.Write("cmd: surfaces <x> <y>, with PlayerLink"); break; }
                        surfaceLink.LogSurfaces(float.Parse(words[1], CultureInfo.InvariantCulture), float.Parse(words[2], CultureInfo.InvariantCulture));
                        break;
                    case "handlayer":
                        string mode = words.Length > 1 ? words[1].ToLower() : "on";
                        if (mode == "tint") PerkeleRim.Game.HandLayerSender.Tint = !PerkeleRim.Game.HandLayerSender.Tint;
                        else if (mode == "save") PerkeleRim.Game.HandLayerSender.SaveRequested = true;
                        else if (mode == "light" && words.Length > 3)
                        {
                            PerkeleRim.Game.HandLayerSender.Ambient = float.Parse(words[2], CultureInfo.InvariantCulture);
                            PerkeleRim.Game.HandLayerSender.Sun = float.Parse(words[3], CultureInfo.InvariantCulture);
                        }
                        else PerkeleRim.Game.HandLayerSender.Enabled = mode != "off";
                        SpikeLog.Write($"cmd: hand layer {(PerkeleRim.Game.HandLayerSender.Enabled ? "on" : "off")}, tint {PerkeleRim.Game.HandLayerSender.Tint}, light {PerkeleRim.Game.HandLayerSender.Ambient} {PerkeleRim.Game.HandLayerSender.Sun}");
                        break;
                    case "textures":
                        var reader = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (reader != null) reader.InspectModel(lines[0].Trim().Substring(8).Trim());
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "models":  // "models again": every model sent again as after the link came up; "models again cold" read again too, with new ids, as after MSC started
                        var sender = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (sender == null) SpikeLog.Write("cmd: no PlayerLink");
                        else if (words.Length > 1 && words[1].ToLower() == "again") sender.ModelsAgain(words.Length > 2 && words[2].ToLower() == "cold");
                        else SpikeLog.Write("cmd: models again [cold]");
                        break;
                    case "teimo":  // "teimo" lists every Teimo MSC has and what he's made of; "teimo pose idle 0.5" bakes him from that clip and time again; "teimo say paivaa1" plays a line
                        if (words.Length > 2 && words[1].ToLower() == "pose")
                        {
                            PerkeleRim.Game.TeimoModel.PoseClip = words[2] == "-" ? "" : words[2];
                            PerkeleRim.Game.TeimoModel.PoseTime = words.Length > 3 ? float.Parse(words[3], CultureInfo.InvariantCulture) : 0f;
                            SpikeLog.Write($"cmd: Teimo's pose {PerkeleRim.Game.TeimoModel.PoseClip} at {PerkeleRim.Game.TeimoModel.PoseTime}");
                        }
                        else if (words.Length > 2 && words[1].ToLower() == "say")
                        {
                            var talker = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                            if (talker != null) talker.TeimoSay(words[2]);
                            else SpikeLog.Write("cmd: no PlayerLink");
                        }
                        else PerkeleRim.Game.TeimoModel.Dump(SpikeLog.Write);
                        break;
                    case "shop":  // Teimo's stall: "shop" lists its goods, "shop take beer" puts one into the hand, "shop counter" puts the held one on the counter
                        var shopLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (shopLink == null || shopLink.Stall == null) { SpikeLog.Write("cmd: shop: no PlayerLink"); break; }
                        if (words.Length > 2 && words[1].ToLower() == "take") shopLink.Stall.Take(words[2]);
                        else if (words.Length > 1 && words[1].ToLower() == "counter") shopLink.Stall.ToCounter();
                        SpikeLog.Write("cmd: " + shopLink.Stall.Describe());
                        break;
                    case "shot":  // MSC's own view, next to the game, as F11 saves it (ViewShot); "shot bare" saves PerkeleRim_shot_bare.png
                        PerkeleRim.Game.ViewShot.Save(Path.Combine(SpikeLog.GameDir, words.Length > 1 ? "PerkeleRim_shot_" + System.Text.RegularExpressions.Regex.Replace(words[1], "[^A-Za-z0-9_-]", "_") + ".png" : "PerkeleRim_shot.png"), 0, 0, SpikeLog.Write);
                        break;
                    case "event":  // a global PlayMaker event, e.g. "event DRINKBEER" plays the beer drinking animation
                        if (words.Length > 1) PlayMakerFSM.BroadcastEvent(words[1]);
                        break;
                    case "fsmvar":  // set an FSM's float, int or bool, e.g. "fsmvar FPSCamera Hangover TimeLeft 0"
                        if (words.Length < 5) { SpikeLog.Write("cmd: fsmvar <object name> <fsm name> <variable> <value>"); break; }
                        int fsmSet = 0;
                        foreach (PlayMakerFSM fsm in Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)))
                        {
                            if (fsm == null || fsm.gameObject.name != words[1] || fsm.FsmName != words[2]) continue;
                            var vars = fsm.FsmVariables;
                            var fsmFloat = vars.FindFsmFloat(words[3]);
                            var fsmInt = fsmFloat == null ? vars.FindFsmInt(words[3]) : null;
                            var fsmBool = fsmFloat == null && fsmInt == null ? vars.FindFsmBool(words[3]) : null;
                            if (fsmFloat != null) fsmFloat.Value = float.Parse(words[4], CultureInfo.InvariantCulture);
                            else if (fsmInt != null) fsmInt.Value = int.Parse(words[4], CultureInfo.InvariantCulture);
                            else if (fsmBool != null) fsmBool.Value = bool.Parse(words[4]);
                            else continue;
                            fsmSet++;
                        }
                        SpikeLog.Write($"cmd: {words[1]} :: {words[2]} {words[3]} = {words[4]} in {fsmSet} FSMs");
                        break;
                    case "global":  // set a global PlayMaker float or int, e.g. "global PlayerUrine 100"
                        var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
                        var global = words.Length > 2 ? globals.FindFsmFloat(words[1]) : null;
                        var globalInt = words.Length > 2 && global == null ? globals.FindFsmInt(words[1]) : null;
                        if (global != null) global.Value = float.Parse(words[2], CultureInfo.InvariantCulture);
                        if (globalInt != null) globalInt.Value = int.Parse(words[2], CultureInfo.InvariantCulture);
                        SpikeLog.Write(global != null ? $"cmd: {words[1]} = {global.Value}"
                            : globalInt != null ? $"cmd: {words[1]} = {globalInt.Value}" : "cmd: no such global float or int");
                        break;
                    case "drunk":  // "drunk 2": MSC's drunk level 2, set at its source (PlayerDrunkAdjusted, by PlayerWeight; docs/FSM_MAP.md)
                        var drunkGlobals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
                        var adjusted = drunkGlobals.FindFsmFloat("PlayerDrunkAdjusted");
                        var weight = drunkGlobals.FindFsmFloat("PlayerWeight");
                        var level = drunkGlobals.FindFsmFloat("PlayerDrunk");
                        float wanted;
                        if (words.Length < 2 || adjusted == null || level == null || !float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out wanted))
                        { SpikeLog.Write("cmd: drunk <level 0..8>, in game"); break; }
                        wanted = Mathf.Clamp(wanted, 0f, 8f);
                        adjusted.Value = Mathf.Clamp(wanted * (weight != null && weight.Value > 0f ? weight.Value / 83f : 1f), 0f, 8f);
                        level.Value = wanted;
                        SpikeLog.Write($"cmd: drunk {wanted}, PlayerDrunkAdjusted = {adjusted.Value}");
                        break;
                    case "spawn":
                        if (words.Length < 2) { SpikeLog.Write("cmd: spawn <product> [count]"); break; }
                        Spawn(words[1], words.Length > 2 ? int.Parse(words[2]) : 1);
                        break;
                    case "snapshot":
                        if (words.Length < 3 || PerkeleRim.Game.SaveSync.Instance == null) { SpikeLog.Write("cmd: snapshot save|load <name>, in a real build"); break; }
                        var folder = Path.Combine(Path.Combine(SpikeLog.GameDir, "PerkeleRim_snapshots"), words[2]);
                        if (words[1].ToLower() == "save") PerkeleRim.Game.SaveSync.Instance.Save(1, folder);
                        else PerkeleRim.Game.SaveSync.Instance.Load(1, folder);
                        break;
                    case "pick":  // what PlayMaker's MousePickEvent sees: Camera.main's ray through the mouse
                        var cam = Camera.main;
                        SpikeLog.Write($"cmd: Camera.main {(cam != null ? cam.name + " enabled " + cam.enabled + " depth " + cam.depth + " at " + cam.transform.position : "none")}, mouse {Input.mousePosition}, screen {Screen.width}x{Screen.height}");
                        foreach (Camera c in Camera.allCameras)
                            SpikeLog.Write($"cmd: camera {c.name} tag {c.tag} depth {c.depth} mask {c.cullingMask:X8}");
                        if (cam != null)
                            foreach (var hit in Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition), 3f))
                                SpikeLog.Write($"cmd: hit {hit.collider.name} ({(hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "")}) layer {hit.collider.gameObject.layer} at {hit.distance:0.00} m trigger {hit.collider.isTrigger}");
                        break;
                    case "death":  // what a fatal crash sends; caught by DeathGuard while handed over
                        PlayMakerFSM.BroadcastEvent("DEATH");
                        break;
                    case "sink":  // "sink 3" pushes the player 3 m down, through the ground (PlayerLink catches the fall)
                        var sunk = GameObject.Find("PLAYER");
                        if (sunk != null) sunk.transform.position += Vector3.down * (words.Length > 1 ? float.Parse(words[1], CultureInfo.InvariantCulture) : 3f);
                        SpikeLog.Write(sunk != null ? $"cmd: player now at {sunk.transform.position}" : "cmd: no PLAYER");
                        break;
                    case "time":  // "time 6" moves MSC's sun, clock and day 6 hours on, the needs untouched
                        float hours;
                        if (words.Length > 1 && float.TryParse(words[1], NumberStyles.Float, CultureInfo.InvariantCulture, out hours) && hours > 0f && hours <= 48f)
                            SpikeLog.Write("cmd: time" + PerkeleRim.Game.RestLink.ClockMove(hours));
                        else
                            SpikeLog.Write("cmd: time <hours, 0..48>");
                        break;
                    case "aim":  // "aim HAYOSIKO door": the player in front of the nearest control surface ("Use" FSM) whose path has those words, looking at it
                        Aim(lines[0].Trim().Substring(3).Trim());
                        break;
                    case "fetch":  // "fetch wheel_regula": the nearest loose part of that name put down by the player; "fetch door left eyes": held up in front of his eyes for a click
                        var fetchWhat = lines[0].Trim().Substring(5).Trim();
                        if (fetchWhat.EndsWith(" eyes")) StartCoroutine(FetchToEyes(fetchWhat.Substring(0, fetchWhat.Length - 5).Trim()));
                        else Fetch(fetchWhat);
                        break;
                    case "fit":  // "fit trigger_discbrake_fl": that part put on through MSC's own Assembly, wherever it lies
                        var fitter = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (fitter != null && words.Length > 1) fitter.FitParts(lines[0].Trim().Substring(3).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                        else SpikeLog.Write("cmd: fit <trigger names>, with PlayerLink");
                        break;
                    case "freeze":  // "freeze SATSUMA on": that vehicle's body held still (kinematic), as on a stand; "off" lets it go
                        float lift = 0f;
                        bool letGo = words.Length > 2 && words[2] == "off";
                        if (words.Length > 2 && !letGo) float.TryParse(words[2], NumberStyles.Float, CultureInfo.InvariantCulture, out lift);
                        Freeze(words.Length > 1 ? words[1] : "", !letGo, lift);
                        break;
                    case "tighten":  // "tighten spindle": every bolt of that part turned all the way in by MSC's own TIGHTEN, for setups
                        StartCoroutine(Tighten(lines[0].Trim().Substring(7).Trim()));
                        break;
                    case "offer":  // "offer trigger_door_left": the part in MSC's hand counted as come into its place on the car
                        Offer(lines[0].Trim().Substring(5).Trim());
                        break;
                    case "spanner":  // "spanner door left(Clone)": the player at the spanner in the open spanner set that fits that part's bolts, looking at it
                        Spanner(lines[0].Trim().Substring(7).Trim());
                        break;
                    case "wrench":  // "wrench door left(Clone)": MSC's wrench the size of that part's nearest bolt, as if picked from the spanner set
                        Wrench(lines[0].Trim().Substring(6).Trim());
                        break;
                    case "look":  // "look trigger_door_left": the player at the nearest collider with those words in its path, looking at it
                        Look(lines[0].Trim().Substring(4).Trim());
                        break;
                    case "goto":  // "goto chair_pub 2" puts the player 1 m above the first object with that name (inactive ones too)
                        Goto(lines[0].Trim().Substring(4).Trim());
                        break;
                    case "drive":  // "drive HAYOSIKO": the player into that vehicle's driving trigger (Return then gets in, as a player would)
                        Drive(words.Length > 1 ? words[1] : "");
                        break;
                    case "pocket":  // "pocket Y", "pocket 1"
                        var pocketLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (pocketLink == null || words.Length < 2) { SpikeLog.Write("cmd: pocket <Y|3..8>, with PlayerLink"); break; }
                        pocketLink.PressPocketKey(char.IsDigit(words[1][0]) ? (int)KeyCode.Alpha0 + (words[1][0] - (char)48) : (int)KeyCode.Y);
                        break;
                    case "hold":  // "hold 0": a one-bottle beer case in the hand
                        var holder = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (holder != null && words.Length > 1) holder.HoldPocketKind(int.Parse(words[1]));
                        break;
                    case "inspect":  // "inspect empty bottle" lists objects with that name, inactive ones too
                        Inspect(lines[0].Trim().Substring(7).Trim());
                        break;
                    case "particles":  // "particles" lists every particle system, "particles HAYOSIKO" those under the van
                        Particles(lines[0].Trim().Substring(9).Trim());
                        break;
                    case "smoke":  // "smoke off|on" stops or resumes sending smoke to Skyrim; "smoke puff 30" bursts every mirrored system; "smoke" lists them
                        var smokeLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (words.Length > 2 && words[1] == "sheet") PerkeleRim.Game.ParticleLink.Sheets = words[2] != "off";
                        else if (words.Length > 1 && words[1] == "puff") { if (smokeLink != null) smokeLink.PuffParticles(words.Length > 2 ? int.Parse(words[2]) : 30); }
                        else if (words.Length > 1) PerkeleRim.Game.ParticleLink.Enabled = words[1] != "off";
                        else if (smokeLink != null) smokeLink.DumpParticles();
                        SpikeLog.Write("cmd: smoke " + (PerkeleRim.Game.ParticleLink.Enabled ? "on" : "off"));
                        break;
                    case "lightson":  // "lightson 1" forces the van's beams, rear and cabin lights on (their FSMs normally do), "lightson 0" off
                    {
                        bool onOff = words.Length < 2 || words[1] != "0";  // "lightson 2": the high beam too
                        foreach (var rootName in new[] { "HAYOSIKO(1500kg, 250)", "JONNEZ ES(Clone)", "KEKMET(350-400psi)" })
                        {
                        var carObj = GameObject.Find(rootName);
                        if (carObj == null) continue;
                        foreach (var t in carObj.GetComponentsInChildren<Transform>(true))
                        {
                            var n = t.name;
                            bool lamp = n == "BeamsShort" || (n == "BeamsLong" && words.Length > 1 && words[1] == "2") || n == "Brake" || n == "Reverse" || n == "InteriorLight" || (n == "Light" && t.parent != null && t.parent.name == "InteriorLight");
                            if (!lamp) continue;
                            if (onOff) for (var p = t; p != null && p != carObj.transform; p = p.parent) p.gameObject.SetActive(true);
                            else t.gameObject.SetActive(false);
                            foreach (var l in t.GetComponentsInChildren<Light>(true)) l.enabled = true;
                        }
                        }
                        SpikeLog.Write($"cmd: lightson {onOff}");
                        break;
                    }
                    case "carlights":  // every Light below the vehicles, and the objects named like lamps, with their state
                        CarLights();
                        break;
                    case "island":  // "island show", "island hide", "island steps 8", "island ramps on|off"
                        var islandLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        string islandMode = words.Length > 1 ? words[1].ToLower() : "show";
                        if (islandLink == null) SpikeLog.Write("cmd: no PlayerLink");
                        else if (islandMode == "why") islandLink.LogWhy(words.Length > 2 ? float.Parse(words[2], CultureInfo.InvariantCulture) : 2f);
                        else if (islandMode == "ramps") islandLink.SetRoadRamps(words.Length < 3 || words[2].ToLower() != "off");
                        else if (islandMode == "steps")islandLink.LogSteps(words.Length > 2 ? float.Parse(words[2], CultureInfo.InvariantCulture) : 8f);
                        else islandLink.SetIslandVisible(islandMode != "hide");
                        break;
                    case "voices":  // "voices" lists every MasterAudio group, "voices hurt" only those (and clips) named like it
                        Voices(lines[0].Trim().Substring(6).Trim());
                        break;
                    case "grunt":  // what Skyrim's PlayerDeath plays
                        PerkeleRim.Game.DeathGuard.PlayDeathSound();
                        break;
                    case "shout":  // "shout 13E07 2": what Skyrim's PlayerShout plays for that shout (form id, hex) and word count
                        var shoutLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (shoutLink == null) SpikeLog.Write("cmd: no PlayerLink");
                        else shoutLink.Yell(words.Length > 1 ? uint.Parse(words[1], System.Globalization.NumberStyles.HexNumber) : 0x13E07u,
                            words.Length > 2 ? uint.Parse(words[2]) : 3u);
                        break;
                    case "intro":  // "intro 1": what Skyrim's IntroCue plays as a new game opens, "intro 2" after Ralof's line, "intro 3" the shack
                        var introLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (introLink == null) SpikeLog.Write("cmd: no PlayerLink");
                        else introLink.Intro(words.Length > 1 ? uint.Parse(words[1]) : 1u);
                        break;
                    case "hurt":  // "hurt 0": what Skyrim's PlayerHurt plays for a hit (1 blocked, 2 fall, 3 crash, 4 low health), "hurt 2 0.3" a bad fall
                        var hurtLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (hurtLink == null) SpikeLog.Write("cmd: no PlayerLink");
                        else hurtLink.Hurt(words.Length > 1 ? uint.Parse(words[1]) : 0u, words.Length > 2 ? float.Parse(words[2], CultureInfo.InvariantCulture) : 0f);
                        break;
                    default:
                        SpikeLog.Write("cmd: unknown command");
                        break;
                }
            }
            catch (Exception e) { SpikeLog.Write("cmd: FAILED " + e); }
        }

        void StartScript(string[] lines)
        {
            var steps = new List<Step>();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(';');
                if (parts.Length == 0 || parts[0].Trim().Length == 0 || parts[0].Trim().StartsWith("#")) continue;
                var step = new Step { Seconds = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture), Source = lines[i].Trim() };
                for (int j = 1; j < parts.Length; j++)
                {
                    string tok = parts[j].Trim();
                    if (tok.Length == 0) continue;
                    int eq = tok.IndexOf('=');
                    if (eq > 0) step.Axes[tok.Substring(0, eq).Trim()] = float.Parse(tok.Substring(eq + 1).Trim(), CultureInfo.InvariantCulture);
                    else if (tok.StartsWith("button:")) step.Buttons.Add(tok.Substring(7));
                    else step.Keys.Add((KeyCode)Enum.Parse(typeof(KeyCode), tok, true));
                }
                steps.Add(step);
            }
            script = steps;
            stepIndex = -1;
            stepEnds = 0;
            InputShim.Active = InputShim.Manual = true;
            SpikeLog.Write($"script: {steps.Count} steps");
        }

        void RunScript()
        {
            if (script == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < stepEnds) return;

            stepIndex++;
            InputShim.Clear();
            if (stepIndex >= script.Count)
            {
                script = null;
                InputShim.Active = InputShim.Manual = false;
                SpikeLog.Write("script: done");
                LogProbe();
                return;
            }
            var step = script[stepIndex];
            foreach (var k in step.Keys) InputShim.Keys.Add(k);
            foreach (var b in step.Buttons) InputShim.Buttons.Add(b);
            foreach (var a in step.Axes) InputShim.Axes[a.Key] = a.Value;
            stepEnds = now + step.Seconds;
            SpikeLog.Write($"script: step {stepIndex + 1}/{script.Count} '{step.Source}'");
            LogProbe();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsIconic(IntPtr hwnd);
    }
}
