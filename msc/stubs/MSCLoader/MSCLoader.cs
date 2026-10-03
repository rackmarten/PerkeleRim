// Compile-only stubs mirroring MSCLoader's public API. Bodies are empty or throw;
// this assembly is never loaded by the game.
using System;

namespace MSCLoader
{
    public enum Game
    {
        MySummerCar = 1,
        MyWinterCar = 2,
    }

    public abstract partial class Mod
    {
        public enum Setup
        {
            OnNewGame,
            OnMenuLoad,
            PreLoad,
            OnLoad,
            PostLoad,
            OnSave,
            OnGUI,
            Update,
            FixedUpdate,
            OnModEnabled,
            OnModDisabled,
            ModSettingsLoaded,
            ModSettings,
        }

        public abstract string ID { get; }
        public virtual string Name => ID;
        public abstract string Version { get; }
        public abstract string Author { get; }
        public virtual string Description { get; set; }
        public virtual Game SupportedGames => Game.MySummerCar;

        public virtual void ModSetup() { }

        public void SetupFunction(Setup functionType, Action function) { }
    }

    public class ModConsole
    {
        public static void Print(string str) { }
        public static void Print(object obj) { }
    }
}
