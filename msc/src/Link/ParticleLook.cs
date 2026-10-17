using System;

namespace PerkeleRim.Protocol
{
    // ParticlePose's look from a Unity legacy particle.
    public static class ParticleLook
    {
        public const float DefaultTint = 0.5f;  // the shader's _TintColor default: the particle color as is

        // One ParticlePose.color channel: Particles/Alpha Blended draws 2 x _TintColor x the
        // particle's color x the texture, so the first two go to Skyrim multiplied together
        // (255 = the texture as is, brighter is clamped).
        public static byte Channel(float tint, float particle) =>
            (byte)Math.Round(Math.Max(0f, Math.Min(1f, 2f * tint * particle)) * 255f);

        // Particle textures carry a faint alpha haze around their puffs (a few /255; carsmoke has
        // it on about a fifth of its pixels): invisible on one particle, it adds up over dozens of
        // stacked squares. Alpha at or below floor becomes 0 and the rest is stretched back to
        // 0..255. Returns how many pixels were cleared (for the log).
        public const byte HazeFloor = 10;

        public static int ClearHaze(byte[] rgba, byte floor = HazeFloor)
        {
            int cleared = 0;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                int a = rgba[i];
                if (a == 0) continue;
                if (a <= floor) { rgba[i] = 0; cleared++; continue; }
                rgba[i] = (byte)Math.Round((a - floor) * 255.0 / (255 - floor));
            }
            return cleared;
        }

        // The puffs' faint haze fills each frame of a sheet right up to its edges, which can show as
        // a straight edge where squares stack or the light differs from MSC's. Each frame's alpha fades out from Inner to the edge of its inscribed circle (smoothstep),
        // corners outside it are cleared. rgba is width x height, columns x rows frames.
        public const float Inner = 0.6f;

        public static void FadeFrameEdges(byte[] rgba, int width, int height, int columns, int rows)
        {
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);
            float cw = (float)width / columns, ch = (float)height / rows;
            for (int y = 0; y < height; y++)
            {
                float fy = ((y % ch) + 0.5f) / ch * 2f - 1f;  // -1..1 across its frame
                for (int x = 0; x < width; x++)
                {
                    float fx = ((x % cw) + 0.5f) / cw * 2f - 1f;
                    float r = (float)Math.Sqrt(fx * fx + fy * fy);
                    float t = Math.Max(0f, Math.Min(1f, (1f - r) / (1f - Inner)));
                    float keep = t * t * (3f - 2f * t);
                    int i = (y * width + x) * 4 + 3;
                    rgba[i] = (byte)Math.Round(rgba[i] * keep);
                }
            }
        }

        // Dark smoke (diesel exhaust) is drawn in Skyrim by only darkening what's behind it, since
        // its lighting shader adds the scene's light and turned it pale. A material is dark when its
        // doubled tint is darker than this.
        public const float DarkTint = 0.5f;

        public static bool IsDark(float r, float g, float b) => 2f * (r + g + b) / 3f < DarkTint;

        // A dark smoke texture's colors go black, keeping its alpha: Skyrim's light times black is
        // still black, so the squares only dim what's behind them (normal alpha blending).
        public static void Blacken(byte[] rgba)
        {
            for (int i = 0; i < rgba.Length; i += 4) rgba[i] = rgba[i + 1] = rgba[i + 2] = 0;
        }

        // A darkening particle's alpha: blending only dims the background by alpha, so the lighter
        // its color, the less it dims (black dims fully, white not at all).
        public static byte DarkenAlpha(byte alpha, byte r, byte g, byte b) =>
            (byte)Math.Round(alpha * (1.0 - (r + g + b) / (3.0 * 255.0)));

        // ParticlePose.sheet's frame: ParticleRenderer's UV animation steps through the grid
        // cycles times over a particle's life, left to right from the top row.
        public static byte Frame(float energy, float startEnergy, float cycles, int columns, int rows)
        {
            int frames = columns * rows;
            if (frames <= 1 || startEnergy <= 0f) return 0;
            float age = Math.Max(0f, Math.Min(1f, 1f - energy / startEnergy));
            int frame = (int)Math.Floor(age * Math.Max(cycles, 0f) * frames);
            return (byte)Math.Min(255, ((frame % frames) + frames) % frames);
        }
    }
}
