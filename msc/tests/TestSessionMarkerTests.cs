using System;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class TestSessionMarkerTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 23, 22, 0, 0, DateTimeKind.Utc);

        [Test]
        public void AFreshMarkerIsATestSessionWithoutSound()
        {
            Assert.That(TestSessionMarker.Read(TestSessionMarker.Header + "\n", Now.AddSeconds(-5), Now, out bool sound), Is.True);
            Assert.That(sound, Is.False);
            // Windows line endings and a BOM.
            Assert.That(TestSessionMarker.Read("﻿" + TestSessionMarker.Header + "\r\n", Now, Now, out sound), Is.True);
            Assert.That(sound, Is.False);
        }

        [Test]
        public void TheSoundLineKeepsTheSound()
        {
            Assert.That(TestSessionMarker.Read(TestSessionMarker.Header + "\r\nSound\r\n", Now.AddSeconds(-5), Now, out bool sound), Is.True);
            Assert.That(sound, Is.True);
        }

        [Test]
        public void AnOldOrForeignMarkerIsNoTestSession()
        {
            Assert.That(TestSessionMarker.Read(TestSessionMarker.Header, Now.AddMinutes(-11), Now, out bool sound), Is.False);
            Assert.That(TestSessionMarker.Read(TestSessionMarker.Header, Now.AddMinutes(11), Now, out sound), Is.False);
            Assert.That(TestSessionMarker.Read("", Now, Now, out sound), Is.False);
            Assert.That(TestSessionMarker.Read(null, Now, Now, out sound), Is.False);
            Assert.That(TestSessionMarker.Read("Written by the PerkeleRim launcher.", Now, Now, out sound), Is.False);
        }
    }
}
