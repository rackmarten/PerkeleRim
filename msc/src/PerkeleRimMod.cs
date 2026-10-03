using MSCLoader;

namespace PerkeleRim
{
    public class PerkeleRimMod : Mod
    {
        public override string ID => "PerkeleRim";
        public override string Name => "PerkeleRim";
        public override string Author => "Marek Uncik";
        public override string Version => "0.1.0";
        public override string Description => "Couples My Summer Car with Skyrim.";

        public override void ModSetup()
        {
            SetupFunction(Setup.OnMenuLoad, Mod_OnMenuLoad);
        }

        private void Mod_OnMenuLoad()
        {
            ModConsole.Print("PerkeleRim loaded");
#if PERKELERIM_SPIKE
            Spike.SpikeHost.Ensure();
#endif
        }
    }
}
