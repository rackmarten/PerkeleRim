using System;
using System.IO;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Spike output goes to <MSC>/PerkeleRim_spike.log so it can be read while the game is hidden.
    public static class SpikeLog
    {
        public static readonly string GameDir = Path.GetDirectoryName(Application.dataPath);
        static StreamWriter writer;

        public static void Write(string line)
        {
            if (writer == null)
            {
                writer = new StreamWriter(Path.Combine(GameDir, "PerkeleRim_spike.log"), false) { AutoFlush = true };
            }
            writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
    }
}
