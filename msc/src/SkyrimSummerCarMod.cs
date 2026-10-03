using MSCLoader;

namespace SkyrimSummerCar
{
    public class SkyrimSummerCarMod : Mod
    {
        public override string ID => "SkyrimSummerCar";
        public override string Name => "SkyrimSummerCar";
        public override string Author => "Marek Uncik";
        public override string Version => "0.1.0";
        public override string Description => "Couples My Summer Car with Skyrim.";

        public override void ModSetup()
        {
            SetupFunction(Setup.OnMenuLoad, Mod_OnMenuLoad);
        }

        private void Mod_OnMenuLoad()
        {
            ModConsole.Print("SkyrimSummerCar loaded");
        }
    }
}
