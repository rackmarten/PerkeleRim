namespace PerkeleRim
{
    // Which of PerkeleRim's log lines a player sees in MSCLoader's console (toggled with ~): the link
    // coming up, going down or giving up, and anything that failed. Every other line goes to
    // output_log.txt only, so the console stays quiet during play. Unity-free.
    public static class ConsoleLine
    {
        static readonly string[] PlayerPrefixes = { "link up", "link down", "link disabled" };
        static readonly string[] PlayerWords = { "FAILED", "cannot " };

        public static bool IsForPlayer(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            foreach (var prefix in PlayerPrefixes)
                if (line.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
            foreach (var word in PlayerWords)
                if (line.IndexOf(word, System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
