using System;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    // Same vectors as the C++ tests (protocol/tests/coords_vectors.csv), so both sides agree.
    public class CoordsTests
    {
        [Test]
        public void ConversionMatchesTheSharedVectors()
        {
            var rows = File.ReadAllLines(Path.Combine(Paths.ProtocolDir, "tests", "coords_vectors.csv"))
                .Where(l => l.Length > 0 && l[0] != '#')
                .Select(l => l.Split(','))
                .ToList();
            Assert.That(rows.Count, Is.GreaterThanOrEqualTo(10));

            foreach (var row in rows)
            {
                var v = row.Skip(1).Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                switch (row[0])
                {
                    case "pos":
                        var unity = Coords.SkyrimToUnity(new Vec3(v[0], v[1], v[2]));
                        AssertClose(unity, new Vec3(v[3], v[4], v[5]), string.Join(",", row));
                        var skyrim = Coords.UnityToSkyrim(new Vec3(v[3], v[4], v[5]));
                        AssertClose(skyrim, new Vec3(v[0], v[1], v[2]), string.Join(",", row));
                        break;
                    case "angle":
                    case "angle_wrap":
                        Assert.That(Coords.SkyrimAngleToUnity(v[0]), Is.EqualTo(v[1]).Within(1e-3), string.Join(",", row));
                        if (row[0] == "angle")
                            Assert.That(Coords.UnityAngleToSkyrim(v[1]), Is.EqualTo(v[0]).Within(1e-5), string.Join(",", row));
                        break;
                    case "rot":
                        var label = string.Join(",", row);
                        AssertClose(Coords.UnityToSkyrim(new Quat(v[0], v[1], v[2], v[3])), new Quat(v[4], v[5], v[6], v[7]), label);
                        AssertClose(Coords.SkyrimToUnity(new Quat(v[4], v[5], v[6], v[7])), new Quat(v[0], v[1], v[2], v[3]), label);
                        break;
                    default:
                        Assert.Fail("unknown row kind " + row[0]);
                        break;
                }
            }
        }

        [Test]
        public void OneMeterIsAboutSeventyUnits() =>
            Assert.That(Coords.UnitsPerMeter, Is.EqualTo(69.99125).Within(1e-4));

        static void AssertClose(Quat actual, Quat expected, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-6), label + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-6), label + " y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-6), label + " z");
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(1e-6), label + " w");
        }

        static void AssertClose(Vec3 actual, Vec3 expected, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5 * Math.Max(1, Math.Abs(expected.x))), label + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5 * Math.Max(1, Math.Abs(expected.y))), label + " y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5 * Math.Max(1, Math.Abs(expected.z))), label + " z");
        }
    }
}
