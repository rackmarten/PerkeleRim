using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class ConsoleLineTests
    {
        [Test]
        public void TheLinkComingAndGoingIsForThePlayer()
        {
            Assert.That(ConsoleLine.IsForPlayer("link up: Skyrim pid 1234, protocol v40"), Is.True);
            Assert.That(ConsoleLine.IsForPlayer("link down: no heartbeat from the Skyrim side for 3000 ms"), Is.True);
            Assert.That(ConsoleLine.IsForPlayer("link disabled: protocol mismatch"), Is.True);
        }

        [Test]
        public void FailuresAreForThePlayer()
        {
            Assert.That(ConsoleLine.IsForPlayer("input redirect FAILED: System.Exception"), Is.True);
            Assert.That(ConsoleLine.IsForPlayer("death: patching Fsm.ProcessEvent FAILED: gone"), Is.True);
            Assert.That(ConsoleLine.IsForPlayer("cannot map shared memory (error 5), retrying"), Is.True);
        }

        [Test]
        public void RoutineLinesStayInTheLog()
        {
            Assert.That(ConsoleLine.IsForPlayer("redirect: scanned 8768 methods, patched 90, failed 0"), Is.False);
            Assert.That(ConsoleLine.IsForPlayer("hand layer: 640x480 copies=60 msc frame=7.10 ms layer cpu=120 us"), Is.False);
            Assert.That(ConsoleLine.IsForPlayer("created shared memory, waiting for peer"), Is.False);
            Assert.That(ConsoleLine.IsForPlayer("beer: 3 from Skyrim, 3 to hand out"), Is.False);
            Assert.That(ConsoleLine.IsForPlayer(""), Is.False);
            Assert.That(ConsoleLine.IsForPlayer(null), Is.False);
        }
    }
}
