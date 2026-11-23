using System;

namespace PerkeleRim
{
    // MSC's frame times over a stretch of play (ModelSender watches the models' link-up with it):
    // how many frames, the worst, and how many went over a hitch a player feels. Unity-free: the
    // caller times its frames.
    public sealed class FrameWatch
    {
        public const double HitchMs = 200;   // a frame this long is a freeze a player feels, and a key pressed in it is lost
        public const double SlowMs = 100;

        public int Frames { get; private set; }
        public double WorstMs { get; private set; }
        public double TotalMs { get; private set; }
        public int Hitches { get; private set; }  // over HitchMs
        public int Slow { get; private set; }     // over SlowMs

        public void Add(double ms)
        {
            if (ms < 0 || double.IsNaN(ms)) return;
            Frames++;
            TotalMs += ms;
            WorstMs = Math.Max(WorstMs, ms);
            if (ms > HitchMs) Hitches++;
            if (ms > SlowMs) Slow++;
        }

        public string Summary() =>
            $"{Frames} frames, worst {WorstMs:0} ms, {Hitches} over {HitchMs:0} ms, {Slow} over {SlowMs:0} ms, average {(Frames > 0 ? TotalMs / Frames : 0):0.0} ms";
    }
}
