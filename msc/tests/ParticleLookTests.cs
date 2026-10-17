using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ParticleLookTests
    {
        [Test]
        public void DefaultTintKeepsTheParticleColor()
        {
            Assert.That(ParticleLook.Channel(ParticleLook.DefaultTint, 1f), Is.EqualTo(255));
            Assert.That(ParticleLook.Channel(ParticleLook.DefaultTint, 0.5f), Is.EqualTo(128));
            Assert.That(ParticleLook.Channel(ParticleLook.DefaultTint, 0f), Is.EqualTo(0));
        }

        [Test]
        public void TintScalesAndClamps()
        {
            Assert.That(ParticleLook.Channel(0.25f, 1f), Is.EqualTo(128));
            Assert.That(ParticleLook.Channel(1f, 1f), Is.EqualTo(255));  // twice as bright, clamped
            Assert.That(ParticleLook.Channel(-1f, 1f), Is.EqualTo(0));
        }

        [Test]
        public void FrameStepsThroughTheSheetOverTheLife()
        {
            Assert.That(ParticleLook.Frame(2f, 2f, 1f, 4, 4), Is.EqualTo(0));     // just born
            Assert.That(ParticleLook.Frame(1f, 2f, 1f, 4, 4), Is.EqualTo(8));     // half way
            Assert.That(ParticleLook.Frame(0.01f, 2f, 1f, 4, 4), Is.EqualTo(15)); // about to die
            Assert.That(ParticleLook.Frame(0f, 2f, 1f, 4, 4), Is.EqualTo(0));     // dead: wraps, never past the last
            Assert.That(ParticleLook.Frame(1f, 2f, 2f, 4, 4), Is.EqualTo(0));     // two cycles: half way starts over
        }

        [Test]
        public void HazeIsClearedAndTheRestStretched()
        {
            var rgba = new byte[] { 255, 255, 255, 0, 255, 255, 255, 5, 255, 255, 255, 10, 255, 255, 255, 11, 255, 255, 255, 255 };
            Assert.That(ParticleLook.ClearHaze(rgba, 10), Is.EqualTo(2));
            Assert.That(new[] { rgba[3], rgba[7], rgba[11], rgba[15], rgba[19] }, Is.EqualTo(new byte[] { 0, 0, 0, 1, 255 }));
            Assert.That(rgba[0], Is.EqualTo(255));  // color untouched
        }

        [Test]
        public void FrameEdgesFadeOutAndCentersStay()
        {
            const int size = 16;  // 2 x 2 frames of 8 x 8
            var rgba = new byte[size * size * 4];
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 200;
            ParticleLook.FadeFrameEdges(rgba, size, size, 2, 2);
            byte At(int x, int y) => rgba[(y * size + x) * 4 + 3];
            Assert.That(At(3, 3), Is.EqualTo(200));  // a frame's center
            Assert.That(At(12, 12), Is.EqualTo(200));
            Assert.That(At(0, 0), Is.EqualTo(0));    // a frame's corner
            Assert.That(At(8, 8), Is.EqualTo(0));    // next frame's corner, at the boundary
            Assert.That(At(0, 3), Is.LessThan(200)); // a frame's edge is faded
        }

        [Test]
        public void DieselIsDarkCigaretteIsNot()
        {
            Assert.That(ParticleLook.IsDark(0.213f, 0.213f, 0.213f), Is.True);   // MSC's SmokeDiesel tint
            Assert.That(ParticleLook.IsDark(0.772f, 0.772f, 0.772f), Is.False);  // SmokeCigarette
        }

        [Test]
        public void BlackenKeepsAlpha()
        {
            var rgba = new byte[] { 255, 128, 7, 90 };
            ParticleLook.Blacken(rgba);
            Assert.That(rgba, Is.EqualTo(new byte[] { 0, 0, 0, 90 }));
        }

        [Test]
        public void DarkerColorsDimMore()
        {
            Assert.That(ParticleLook.DarkenAlpha(200, 0, 0, 0), Is.EqualTo(200));
            Assert.That(ParticleLook.DarkenAlpha(200, 255, 255, 255), Is.EqualTo(0));
            Assert.That(ParticleLook.DarkenAlpha(200, 90, 90, 90), Is.EqualTo(129));
        }

        [Test]
        public void SinglePictureHasNoFrames()
        {
            Assert.That(ParticleLook.Frame(1f, 2f, 1f, 1, 1), Is.EqualTo(0));
            Assert.That(ParticleLook.Frame(1f, 0f, 1f, 4, 4), Is.EqualTo(0));
        }
    }
}
