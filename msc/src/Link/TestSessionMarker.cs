using System;

namespace PerkeleRim
{
    // <MSC>/PerkeleRim_test_session.txt says this start is a test session's partner (scripts/launch-msc.ps1
    // -Hidden): MSC runs minimized behind a hidden Skyrim while someone may be using the desktop, so it keeps
    // quiet (no sound unless the session asked for it) and never locks the cursor (a minimized window's center
    // is off screen, and Windows pins the cursor to the screen's top left corner). The mod reads it once, as MSC
    // starts, and deletes it; one older than MaxAge is a leftover of a start that never got that far and is
    // ignored, so a player's own start is never a test session. Unity-free.
    public static class TestSessionMarker
    {
        public const string FileName = "PerkeleRim_test_session.txt";

        // launch-msc.ps1 writes this line, then Sound on the next when the session keeps its sound (-Sound).
        public const string Header = "Written by scripts/launch-msc.ps1 for a test session.";
        public const string SoundLine = "Sound";

        public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        // Whether the marker (its text, when it was written) makes this start a test session; sound: whether it
        // keeps its sound.
        public static bool Read(string content, DateTime writtenUtc, DateTime nowUtc, out bool sound)
        {
            sound = false;
            if (content == null) return false;
            var age = nowUtc - writtenUtc;
            if (age > MaxAge || age < -MaxAge) return false;
            var lines = content.TrimStart('﻿').Split('\n');
            if (lines[0].Trim() != Header) return false;
            for (int i = 1; i < lines.Length; i++)
                if (lines[i].Trim() == SoundLine) sound = true;
            return true;
        }
    }
}
