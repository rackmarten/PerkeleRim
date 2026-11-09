using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class AutoloadMarkerTests
    {
        [Test]
        public void TheLaunchersMarkerIsUsedOnce()
        {
            Assert.That(AutoloadMarker.IsOneShot("Written by the PerkeleRim launcher."), Is.True);
            // Set-Content adds a line break, and a BOM on some systems.
            Assert.That(AutoloadMarker.IsOneShot("Written by the PerkeleRim launcher.\r\n"), Is.True);
            Assert.That(AutoloadMarker.IsOneShot("﻿Written by the PerkeleRim launcher.\r\n"), Is.True);
        }

        [Test]
        public void AnEmptyOrHandMadeMarkerStays()
        {
            Assert.That(AutoloadMarker.IsOneShot(""), Is.False);
            Assert.That(AutoloadMarker.IsOneShot(null), Is.False);
            Assert.That(AutoloadMarker.IsOneShot("keep loading my save"), Is.False);
        }
    }
}
