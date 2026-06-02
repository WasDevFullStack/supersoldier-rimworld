using HarmonyLib;
using RimWorld;
using Verse;

namespace EnhancedCarryingCapacity
{
    [HarmonyPatch(typeof(MassUtility), nameof(MassUtility.Capacity))]
    internal static class MassUtility_CapacityPatch
    {
        static bool Prepare()
        {
            Log.Message("[EnhancedCarryingCapacity] Combat Extended active: " + EnhancedCarryingCapacity.Instance.CombatExtendedActive);
            return !EnhancedCarryingCapacity.Instance.CombatExtendedActive;
        }

        static void Postfix(ref float __result, Pawn p)
        {
            var new_cap = p.GetStatValue(StatDefOf.CarryingCapacity);
            if (new_cap > __result) __result = new_cap;
        }
    }
}