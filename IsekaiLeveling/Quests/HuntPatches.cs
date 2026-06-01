using HarmonyLib;
using RimWorld;
using Verse;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Harmony patch to detect when hunt target creatures are killed
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_HuntTracker
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance == null) return;
            if (Current.Game == null) return;
            
            // Guard against map-cleanup kills (gravship/SRTS departure destroys map → kills pawns)
            if (!IncidentWorker_IsekaiHunt.IsLegitimateQuestKill(__instance, dinfo))
                return;
            
            // Get the hunt tracker
            var tracker = Current.Game.GetComponent<IsekaiHuntTracker>();
            if (tracker == null) return;
            
            // Try to find who killed it
            Pawn killer = null;
            if (dinfo.HasValue && dinfo.Value.Instigator is Pawn instigator)
            {
                killer = instigator;
            }
            
            // Notify tracker
            tracker.OnCreatureKilled(__instance, killer);
        }
    }
}
