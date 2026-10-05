using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    // The van's part list lives in three places: the C++ header (source of truth), its C# mirror and
    // tools/nif/hayosiko.json, which builds one NIF node per part. All three must agree in order.
    public class PartsTests
    {
        static string[] HeaderParts()
        {
            var header = File.ReadAllText(Path.Combine(Paths.ProtocolDir, "perkelerim_protocol.h"));
            var body = Regex.Match(header, @"kHayosikoParts\[\] = \{(.*?)\};", RegexOptions.Singleline);
            Assert.That(body.Success, "kHayosikoParts not found in the header");
            return Regex.Matches(body.Groups[1].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
        }

        [Test]
        public void MirrorMatchesTheHeader()
        {
            var header = HeaderParts();
            Assert.That(header.Length, Is.EqualTo(Wire.HayosikoPartCount));
            Assert.That(Wire.HayosikoParts, Is.EqualTo(header));
        }

        [Test]
        public void NifSettingsListTheSameParts()
        {
            var path = Path.Combine(Paths.RepoRoot, "tools", "nif", "hayosiko.json");
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            var parts = json.RootElement.GetProperty("parts").EnumerateArray().Select(p => p.GetProperty("path").GetString()).ToArray();
            Assert.That(parts, Is.EqualTo(HeaderParts()));
        }
    }
}
