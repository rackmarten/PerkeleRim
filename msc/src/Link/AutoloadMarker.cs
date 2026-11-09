namespace PerkeleRim
{
    // <MSC>/PerkeleRim_autoload.txt makes the mod continue MSC's save from the main menu. The launcher
    // writes it with LauncherText on every start, so the mod deletes that one after using it: a player
    // who starts MSC by itself later gets the main menu. An empty marker (the dev test loops, or one made
    // by hand) stays until someone deletes it. Unity-free.
    public static class AutoloadMarker
    {
        public const string FileName = "PerkeleRim_autoload.txt";

        // launcher/PerkeleRim.ps1 writes exactly this.
        public const string LauncherText = "Written by the PerkeleRim launcher.";

        public static bool IsOneShot(string content)
        {
            if (content == null) return false;
            return content.TrimStart('﻿').Trim().StartsWith(LauncherText, System.StringComparison.Ordinal);
        }
    }
}
