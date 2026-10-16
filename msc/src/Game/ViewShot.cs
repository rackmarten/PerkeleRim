using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PerkeleRim.Game
{
    // F11 in Skyrim (ScreenshotEvent): MSC's view as a PNG at the path Skyrim names, next to Skyrim's
    // own frame. Every camera that draws to the screen renders once more, by depth, into a texture at
    // Skyrim's screen size, so the shot is whole even with MSC's window minimized, where the screen
    // itself (Application.CaptureScreenshot) comes out black.
    public static class ViewShot
    {
        public static void Save(string path, int width, int height, Action<string> log)
        {
            if (width <= 0 || height <= 0) { width = Screen.width; height = Screen.height; }
            if (width <= 0 || height <= 0) { width = 1280; height = 720; }
            var cameras = new List<Camera>();
            foreach (var c in Camera.allCameras)
                if (c.enabled && c.targetTexture == null) cameras.Add(c);
            if (cameras.Count == 0) { log("shot: no camera draws MSC's view"); return; }
            cameras.Sort((a, b) => a.depth.CompareTo(b.depth));

            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                foreach (var c in cameras)
                {
                    c.targetTexture = target;
                    try { c.Render(); }
                    finally { c.targetTexture = null; }
                }
                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                RenderTexture.active = previous;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                log($"shot: MSC's view ({width}x{height}, {cameras.Count} cameras) to {path}");
            }
            catch (Exception e)
            {
                log($"shot: could not save {path}: {e.Message}");
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (pixels != null) UnityEngine.Object.Destroy(pixels);
            }
        }
    }
}
