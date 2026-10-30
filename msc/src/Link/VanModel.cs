using System;
using PerkeleRim.Protocol;
using System.Text.RegularExpressions;

namespace PerkeleRim
{
    // What goes into the van's model for Skyrim (one ModelChunk model named "Hayosiko", read by
    // Game/VehicleModel): every MSC object of the van with a mesh becomes a node, posed from
    // VehicleParts every frame and shown only while MSC shows it, so whatever MSC moves or switches
    // (doors, wheels, the fuel cap, lights, a broken windshield) does the same in Skyrim. Only what
    // MSC never draws is left out. Unity-free, so the tests cover it.
    public static class VanModel
    {
        public const string Name = "Hayosiko";

        // The intensity MSC gives the van's beams (the moped's follows its RPM): full brightness in Skyrim.
        public const float LowBeamIntensity = 3f;
        public const float HighBeamIntensity = 4f;

        // Paths below the van's root left out: colliders and the dead body.
        public static readonly string[] Skip = { "(^|/)coll", "^DeadBody" };

        // The lights (switched-on lamps, gauge lights, dashboard indicators): drawn glowing. The Gifu's
        // lit head and tail lamps are one mesh beside its PowerON, not in it; its beacons' lit domes and
        // its work lights' lit quads are shown only while on.
        public static readonly string[] Lights = { "Electricity/PowerON", "Electricity/Driving/DrivingLights", "GaugesLights", "Dashboard/Indicators/",
            "/LightDomeON$", "work_lights/WorkLight" };

        public static readonly string[] SkipMaterials = { "Default-Material" };

        // Glass the van's NIF blended by name, whatever MSC's shader says (tools/nif/hayosiko.json).
        public static readonly string[] AlphaBlendMaterials = { "WINDOWS", "glass", "_alpha$" };

        // How much brighter than its texture a switched-on light is drawn (build_nif.py GLOW_MULT).
        public const float Glow = 1.5f;

        static readonly Regex[] skip = Array.ConvertAll(Skip, p => new Regex(p));
        static readonly Regex[] lights = Array.ConvertAll(Lights, p => new Regex(p));
        static readonly Regex[] blend = Array.ConvertAll(AlphaBlendMaterials, p => new Regex(p, RegexOptions.IgnoreCase));

        public static bool Skipped(string path) => Any(skip, path);
        public static bool IsLight(string path) => Any(lights, path);

        // What a Unity Light below the vehicle is for (Wire.Light*), 0 = Skyrim doesn't draw it (the
        // blinkers, the moped's dummy). Paths are the van's, the moped's and the Kekmet's: beams are
        // spots below BeamsShort / BeamsLong, the van's tail lights sit below both of them. The Gifu's
        // work lights and beacons go by their objects' names, whatever their type.
        public static uint LightRole(string path, bool spot)
        {
            if (path.Contains("work_lights/")) return Wire.LightWork;
            if (path.Contains("/Beacon/")) return Wire.LightBeacon;
            if (path.Contains("Indicator") || path.Contains("Blinkers")) return 0;
            if (spot && path.Contains("BeamsLong")) return Wire.LightHigh;
            if (spot && path.Contains("BeamsShort")) return Wire.LightLow;
            if (spot) return 0;
            if (path.Contains("CarRearLight")) return Wire.LightTail;
            if (path.Contains("Brake")) return Wire.LightBrake;
            if (path.Contains("Reverse")) return Wire.LightReverse;
            if (path.Contains("InteriorLight")) return Wire.LightInterior;
            return 0;
        }

        static bool Any(Regex[] patterns, string s)
        {
            foreach (var p in patterns)
                if (p.IsMatch(s)) return true;
            return false;
        }

        // Unity names a material it copied at runtime "<name> (Instance)".
        static string MaterialName(string name) => name != null && name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;

        public static bool SkipMaterial(string name) => Array.IndexOf(SkipMaterials, MaterialName(name)) >= 0;

        public static bool BlendMaterial(string name) => Any(blend, MaterialName(name) ?? "");

        // A light's material: glows over its own lighting.
        public static ModelMaterialData Lit(ModelMaterialData m)
        {
            return new ModelMaterialData
            {
                Flags = m.Flags,
                AlphaCutoff = m.AlphaCutoff,
                Specular = (float[])m.Specular.Clone(),
                Glossiness = m.Glossiness,
                Emissive = new[] { Glow, Glow, Glow },
            };
        }
    }
}
