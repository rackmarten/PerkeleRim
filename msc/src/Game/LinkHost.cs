using System;
using System.Diagnostics;
using MSCLoader;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Keeps the link to the Skyrim plugin alive from MSC's game loop, on a DontDestroyOnLoad object
    // created at the main menu. Ticking from Update (not a thread) means the heartbeat stops when
    // MSC's main loop stalls, which is exactly what the Skyrim side should notice.
    public class LinkHost : MonoBehaviour
    {
        static LinkHost instance;

        readonly Stopwatch clock = Stopwatch.StartNew();
        Link link;

        public static Link Current => instance != null ? instance.link : null;

        // Every line goes to output_log.txt; MSCLoader's console (which writes there too) only gets
        // the ones a player should see (ConsoleLine).
        public static void Log(string line)
        {
            if (ConsoleLine.IsForPlayer(line)) ModConsole.Print("PerkeleRim: " + line);
            else UnityEngine.Debug.Log("PerkeleRim: " + line);
        }

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("PerkeleRim.Link");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<LinkHost>();
        }

        void Awake()
        {
            // MSC runs hidden behind Skyrim; without this Unity pauses it as soon as it loses focus.
            Application.runInBackground = true;
            Action<string> log = Log;
            link = new Link(Side.Msc, log);
            // Redirect the game's Input calls to InputShim before the game scene loads.
            try { InputRedirect.Apply(log); }
            catch (Exception e) { log("input redirect FAILED: " + e); }
            gameObject.AddComponent<PlayerLink>().Init(log);
        }

        void Update() => link.Tick((ulong)clock.ElapsedMilliseconds);

        void OnApplicationQuit() => link.Dispose();

        void OnDestroy() => link.Dispose();
    }
}
