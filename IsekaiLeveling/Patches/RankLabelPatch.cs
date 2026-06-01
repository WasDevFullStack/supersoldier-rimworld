using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Patches TraitDegreeData.GetLabelCapFor to preserve uppercase rank letters.
    /// RimWorld's GenText.CapitalizeFirst lowercases everything then capitalizes 
    /// only the first character, turning "Rank F" into "Rank f".
    /// This postfix re-uppercases the rank letter for our rank traits.
    /// </summary>
    [HarmonyPatch]
    public static class TraitDegreeData_GetLabelCapFor_Patch
    {
        private static readonly string RankPrefix = "Rank ";

        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(TraitDegreeData), nameof(TraitDegreeData.GetLabelCapFor), new[] { typeof(Pawn) });
        }

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (__result == null || !__result.StartsWith(RankPrefix))
                return;

            // "Rank f" → "Rank F", "Rank ss" → "Rank SS", etc.
            string rankPart = __result.Substring(RankPrefix.Length);
            string upper = rankPart.ToUpperInvariant();
            if (rankPart != upper)
            {
                __result = RankPrefix + upper;
            }
        }
    }
}
