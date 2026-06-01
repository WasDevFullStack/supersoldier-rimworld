using HarmonyLib;
using Verse;

namespace SuperSoldier
{
    /// <summary>
    /// Main mod class for the SuperSoldier mod.
    /// Handles Harmony patches for stat modifications.
    /// </summary>
    public class SuperSoldierMod : Mod
    {
        public SuperSoldierMod(ModContentPack content) : base(content)
        {
            // Apply Harmony patches
            var harmony = new Harmony("SuperSoldier.Mods");
            harmony.PatchAll();
        }
    }
}
