using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class IslandTests
    {
        static void AssertClose(Vec3 actual, Vec3 expected, double tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }

        [Test]
        public void AnchorSitsAtTheIslandBase()
        {
            var island = new Island();
            island.SetAnchor(1, 0x3C, 0, new Vec3(21500f, -8900f, -3200f), 1.5f);
            AssertClose(island.SkyrimToIsland(island.Anchor), Island.Base, 1e-3);
            AssertClose(island.IslandToSkyrim(Island.Base), island.Anchor, 1e-2);
        }

        [Test]
        public void OffsetsFollowTheCoordinateConventions()
        {
            var island = new Island();
            island.SetAnchor(1, 0x3C, 0, new Vec3(21500f, -8900f, -3200f), 0f);
            // 70 units north, 140 east and 35 up from the anchor.
            var unity = island.SkyrimToIsland(new Vec3(21500f + 140f, -8900f + 70f, -3200f + 35f));
            AssertClose(unity, new Vec3(Island.Base.x + 2.00025f, Island.Base.y + 0.5000625f, Island.Base.z + 1.000125f), 1e-3);
        }

        [Test]
        public void RoundTripKeepsPrecisionFarFromTheOrigin()
        {
            var island = new Island();
            island.SetAnchor(1, 0x3C, 0, new Vec3(150000f, -120000f, 9000f), 0f);
            var p = new Vec3(150123.5f, -119876.25f, 9012.75f);
            AssertClose(island.IslandToSkyrim(island.SkyrimToIsland(p)), p, 0.1);  // under 1.5 mm
        }

        [Test]
        public void SetAnchorReplacesTheOldOne()
        {
            var island = new Island();
            Assert.That(island.HasAnchor, Is.False);
            island.SetAnchor(1, 0x3C, 0, new Vec3(1, 2, 3), 0f);
            island.SetAnchor(2, 0x3C, 0x1234, new Vec3(4, 5, 6), 2f);
            Assert.That(island.HasAnchor, Is.True);
            Assert.That(island.Generation, Is.EqualTo(2u));
            Assert.That(island.Cell, Is.EqualTo(0x1234u));
            Assert.That(island.AnchorHeading, Is.EqualTo(2f));
            AssertClose(island.Anchor, new Vec3(4, 5, 6), 0);
        }

        [TestCase("Wait key", Stance.Standing)]
        [TestCase("Move up", Stance.Standing)]
        [TestCase("Move down 1", Stance.Crouching)]
        [TestCase("Crouched 1", Stance.Crouching)]
        [TestCase("Move down 2", Stance.Prone)]
        [TestCase("Crouched 2", Stance.Prone)]
        [TestCase("Incar", Stance.Standing)]
        [TestCase(null, Stance.Standing)]
        public void StanceFromCrouchFsm(string state, byte expected)
        {
            Assert.That(Stance.FromCrouchState(state), Is.EqualTo(expected));
        }
    }

    public class InputFrameTests
    {
        static InputState State(double mouseX, double mouseY, bool owns = true)
        {
            var s = new InputState { mouseX = mouseX, mouseY = mouseY, flags = owns ? Wire.InputOwnsPlayer : 0 };
            return s;
        }

        [Test]
        public void FirstStateOnlySeedsTheMouse()
        {
            var f = new InputFrame();
            var s = State(500.0, -20.0);
            f.Update(ref s);
            Assert.That(f.MouseX, Is.EqualTo(0f));
            Assert.That(f.MouseY, Is.EqualTo(0f));
            Assert.That(f.OwnsPlayer, Is.True);
        }

        [Test]
        public void MouseAxesAreDeltasOfTheTotals()
        {
            var f = new InputFrame();
            var s = State(500.0, -20.0);
            f.Update(ref s);
            s = State(501.5, -19.0);
            f.Update(ref s);
            Assert.That(f.MouseX, Is.EqualTo(1.5f).Within(1e-6));
            Assert.That(f.MouseY, Is.EqualTo(1.0f).Within(1e-6));
            f.Update(ref s);  // nothing new from Skyrim this frame
            Assert.That(f.MouseX, Is.EqualTo(0f));
        }

        [Test]
        public void WheelIsADeltaOfTheTotal()
        {
            var f = new InputFrame();
            var s = State(0.0, 0.0);
            s.mouseWheel = 3.0;
            f.Update(ref s);
            Assert.That(f.MouseWheel, Is.EqualTo(0f));  // first state only seeds
            s.mouseWheel = 2.9;
            f.Update(ref s);
            Assert.That(f.MouseWheel, Is.EqualTo(-0.1f).Within(1e-6));
            f.Update(ref s);
            Assert.That(f.MouseWheel, Is.EqualTo(0f));
        }

        [Test]
        public void ResetSeedsAgain()
        {
            var f = new InputFrame();
            var s = State(0, 0);
            f.Update(ref s);
            f.Reset();
            Assert.That(f.OwnsPlayer, Is.False);
            s = State(300, 0);
            f.Update(ref s);
            Assert.That(f.MouseX, Is.EqualTo(0f));
        }

        [Test]
        public void MscMenuCarriesSkyrimsCursor()
        {
            var f = new InputFrame();
            var s = State(0, 0);
            s.flags |= Wire.InputMscMenu;
            s.cursorX = 0.25f;
            s.cursorY = 1.5f;  // off the screen: clamped
            f.Update(ref s);
            Assert.That(f.MscMenu, Is.True);
            Assert.That(f.CursorX, Is.EqualTo(0.25f));
            Assert.That(f.CursorY, Is.EqualTo(1f));

            s.flags = Wire.InputMscMenu;  // the menu flag means nothing without the player
            f.Update(ref s);
            Assert.That(f.MscMenu, Is.False);
            Assert.That(f.CursorX, Is.EqualTo(0.5f));

            s.flags = Wire.InputOwnsPlayer | Wire.InputMscMenu;
            s.cursorX = float.NaN;
            f.Update(ref s);
            Assert.That(f.CursorX, Is.EqualTo(0f));
            f.Reset();
            Assert.That(f.MscMenu, Is.False);
        }

        [Test]
        public void CursorPixelsCountFromTheBottomLeft()
        {
            float x, y;
            InputFrame.CursorPixels(0.25f, 0.1f, 1920, 1080, out x, out y);
            Assert.That(x, Is.EqualTo(480f).Within(1e-3));
            Assert.That(y, Is.EqualTo(972f).Within(1e-3));
            InputFrame.CursorPixels(0f, 0f, 800, 600, out x, out y);
            Assert.That(y, Is.EqualTo(600f));
        }

        [Test]
        public void HandoverSeedsAgain()
        {
            var f = new InputFrame();
            var s = State(0, 0);
            f.Update(ref s);
            s = State(10, 0, owns: false);  // Skyrim took the player back
            f.Update(ref s);
            Assert.That(f.MouseX, Is.EqualTo(0f));
            s = State(900, 0);  // handed over again after a lot of Skyrim mouse movement
            f.Update(ref s);
            Assert.That(f.MouseX, Is.EqualTo(0f));
            s = State(902, 0);
            f.Update(ref s);
            Assert.That(f.MouseX, Is.EqualTo(2f).Within(1e-6));
        }

        [Test]
        public void HeldKeysAndAxesComeThrough()
        {
            var f = new InputFrame();
            var s = State(0, 0);
            s.SetKey(119, true);  // W
            s.SetKey(304, true);  // LeftShift
            s.SetKey(323, true);  // Mouse0
            s.vertical = 1f;
            s.horizontal = -1f;
            f.Update(ref s);
            Assert.That(f.Keys, Is.EqualTo(new[] { 119, 304, 323 }));
            Assert.That(f.Vertical, Is.EqualTo(1f));
            Assert.That(f.Horizontal, Is.EqualTo(-1f));

            s.SetKey(119, false);
            f.Update(ref s);
            Assert.That(f.Keys, Is.EqualTo(new[] { 304, 323 }));
        }
    }
}
