using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class VanModelTests
    {
        [Test]
        public void OnlyWhatMscNeverDrawsIsLeftOut()
        {
            Assert.That(VanModel.Skipped("coll"));
            Assert.That(VanModel.Skipped("LOD/collider_dash"));
            Assert.That(VanModel.Skipped("DeadBody/head"));
            Assert.That(VanModel.Skipped("LOD/FuelFiller/Pivot/cap"), Is.False);
            Assert.That(VanModel.Skipped("LOD/Electricity/PowerON/Brake/brake"), Is.False, "lights are shown while on");
            Assert.That(VanModel.Skipped("broken windshield"), Is.False, "shown when MSC breaks it");
        }

        [Test]
        public void UnityLightsGetARole()
        {
            Assert.That(VanModel.LightRole("HAYOSIKO/LOD/Electricity/PowerON/Driving/BeamsShort/BeamShortLeft", true), Is.EqualTo(Wire.LightLow));
            Assert.That(VanModel.LightRole("KEKMET/LOD/Electrics/BeamsLong/BeamLongRight", true), Is.EqualTo(Wire.LightHigh));
            Assert.That(VanModel.LightRole("HAYOSIKO/LOD/Electricity/PowerON/Driving/BeamsLong/CarRearLight", false), Is.EqualTo(Wire.LightTail));
            Assert.That(VanModel.LightRole("HAYOSIKO/LOD/Electricity/PowerON/Brake/CarBrakeLight", false), Is.EqualTo(Wire.LightBrake));
            Assert.That(VanModel.LightRole("HAYOSIKO/LOD/Electricity/PowerON/Reverse/Reverse", false), Is.EqualTo(Wire.LightReverse));
            Assert.That(VanModel.LightRole("KEKMET/LOD/InteriorLight/Light", false), Is.EqualTo(Wire.LightInterior));
            Assert.That(VanModel.LightRole("HAYOSIKO/LOD/Electricity/PowerON/Blinkers/Left/FrontIndicator", false), Is.EqualTo(0u));
            Assert.That(VanModel.LightRole("JONNEZ ES(Clone)/LOD/Suspension/Steering/SteeringPivot/Headlight/Light", false), Is.EqualTo(0u));
        }

        [Test]
        public void LightsGlow()
        {
            Assert.That(VanModel.IsLight("LOD/Electricity/PowerON/Brake/brake"));
            Assert.That(VanModel.IsLight("LOD/Dashboard/Gauges/GaugesLights"));
            Assert.That(VanModel.IsLight("LOD/Dashboard/Indicators/indicator_oil"));
            Assert.That(VanModel.IsLight("LOD/Dashboard/Gauges/NeedleFuel/Needle"), Is.False);
            var lit = VanModel.Lit(new ModelMaterialData { Flags = Wire.MaterialBlend, Glossiness = 0.4f });
            Assert.That(lit.Emissive, Is.EqualTo(new[] { VanModel.Glow, VanModel.Glow, VanModel.Glow }));
            Assert.That(lit.Flags, Is.EqualTo(Wire.MaterialBlend));
            Assert.That(lit.Glossiness, Is.EqualTo(0.4f));
        }

        [Test]
        public void MaterialsGoByTheirNameAlsoAsRuntimeCopies()
        {
            Assert.That(VanModel.SkipMaterial("Default-Material"));
            Assert.That(VanModel.SkipMaterial("Default-Material (Instance)"));
            Assert.That(VanModel.SkipMaterial("van_body"), Is.False);
            Assert.That(VanModel.BlendMaterial("WINDOWS"));
            Assert.That(VanModel.BlendMaterial("Glass (Instance)"));
            Assert.That(VanModel.BlendMaterial("lights_alpha"));
            Assert.That(VanModel.BlendMaterial("alpha_lights"), Is.False);
        }

        [Test]
        public void GlassIsBlendedLikeTheOptionalNif()
        {
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.RepoRoot, "tools", "nif", "hayosiko.json")));
            var blend = json.RootElement.GetProperty("alpha_blend_materials").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.That(VanModel.AlphaBlendMaterials, Is.EqualTo(blend));
        }
    }
}
