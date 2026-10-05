using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    // The C++ header asserts every size and offset; these tests read those asserts and check the
    // C# mirror against them, so the two layouts can't drift apart silently.
    public class LayoutTests
    {
        static string HeaderText => File.ReadAllText(Path.Combine(Paths.ProtocolDir, "perkelerim_protocol.h"));

        static Type MirrorType(string name)
        {
            var type = typeof(Wire).Assembly.GetType("PerkeleRim.Protocol." + name);
            Assert.That(type, Is.Not.Null, $"no C# mirror for {name}");
            return type;
        }

        // An inline array of structs (LinkHeader.peers) is mirrored as numbered fields: peers0, peers1.
        static string MirrorField(Type type, string name) =>
            type.GetField(name) != null ? name : type.GetField(name + "0") != null ? name + "0" : name;

        [Test]
        public void SizesMatchTheHeader()
        {
            var asserts = Regex.Matches(HeaderText, @"static_assert\(sizeof\((\w+)\) == (\d+)\);");
            Assert.That(asserts.Count, Is.GreaterThanOrEqualTo(10));
            foreach (Match m in asserts)
                Assert.That(Marshal.SizeOf(MirrorType(m.Groups[1].Value)), Is.EqualTo(int.Parse(m.Groups[2].Value)), $"sizeof({m.Groups[1].Value})");
        }

        [Test]
        public void OffsetsMatchTheHeader()
        {
            var asserts = Regex.Matches(HeaderText, @"static_assert\(offsetof\((\w+), (\w+)\) == (\d+)\);");
            Assert.That(asserts.Count, Is.GreaterThanOrEqualTo(20));
            foreach (Match m in asserts)
            {
                var type = MirrorType(m.Groups[1].Value);
                var field = MirrorField(type, m.Groups[2].Value);
                Assert.That((int)Marshal.OffsetOf(type, field), Is.EqualTo(int.Parse(m.Groups[3].Value)), $"offsetof({type.Name}, {field})");
            }
        }

        [Test]
        public void ConstantsMatchTheHeader()
        {
            var constants = Regex.Matches(HeaderText, @"inline constexpr std::uint32_t k(\w+) = (0x[0-9A-Fa-f]+|\d+);");
            Assert.That(constants.Count, Is.GreaterThanOrEqualTo(10));
            foreach (Match m in constants)
            {
                var field = typeof(Wire).GetField(m.Groups[1].Value, BindingFlags.Public | BindingFlags.Static);
                Assert.That(field, Is.Not.Null, $"Wire.{m.Groups[1].Value} missing");
                string text = m.Groups[2].Value;
                uint expected = text.StartsWith("0x") ? Convert.ToUInt32(text.Substring(2), 16) : uint.Parse(text);
                Assert.That(field.GetValue(null), Is.EqualTo(expected), $"Wire.{field.Name}");
            }

            var name = Regex.Match(HeaderText, @"kMappingName = L""(.+?)"";");
            Assert.That(Wire.MappingName, Is.EqualTo(name.Groups[1].Value.Replace(@"\\", @"\")));
        }

        [Test]
        public void EnumsMatchTheHeader()
        {
            foreach (var e in new[] { typeof(Side), typeof(MessageType) })
            {
                var body = Regex.Match(HeaderText, @"enum class " + e.Name + @"[^{]*\{([^}]*)\}").Groups[1].Value;
                var members = Regex.Matches(body, @"(\w+) = (\d+)").Cast<Match>().ToList();
                Assert.That(members, Is.Not.Empty, e.Name);
                Assert.That(Enum.GetNames(e), Is.EquivalentTo(members.Select(m => m.Groups[1].Value)), e.Name);
                foreach (var m in members)
                    Assert.That(Convert.ToInt64(Enum.Parse(e, m.Groups[1].Value)), Is.EqualTo(long.Parse(m.Groups[2].Value)), $"{e.Name}.{m.Groups[1].Value}");
            }
        }
    }

    static class Paths
    {
        // msc/tests/bin/<config>/net8.0 -> repo root
        public static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "protocol"))) dir = dir.Parent;
                Assert.That(dir, Is.Not.Null, "repo root not found");
                return dir.FullName;
            }
        }

        public static string ProtocolDir => Path.Combine(RepoRoot, "protocol");
    }
}
