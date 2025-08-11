using HarmonyLib;
using MySuperStrengthMod;
using RimWorld;
using Verse;

[StaticConstructorOnStartup]
public static class SuperStrengthPatch
{
    static SuperStrengthPatch()
    {
        var harmony = new Harmony("SuperSoldier.RimWorld");
        //harmony.Patch(AccessTools.Property(typeof(Pawn), "CarryingCapacity").GetGetMethod(),
        harmony.Patch(AccessTools.PropertyGetter(typeof(PawnCapacityDef), "CarryingCapacity"),
        postfix: new HarmonyMethod(typeof(SuperStrengthPatch), nameof(Postfix_CarryingCapacity)));
    }

    public static void Postfix_CarryingCapacity(Pawn __instance, StatDef stat, bool applyPostProcess, ref float __result)
    {
        if (stat == StatDefOf.CarryingCapacity)
        {
            var comp = __instance.TryGetComp<CompSuperStrength>();
            if (comp != null && comp.StrengthLevel > 0)
            {
                __result += comp.StrengthLevel * 10f; // ex.: +10 kg por nível
            }
        }
    }
}
