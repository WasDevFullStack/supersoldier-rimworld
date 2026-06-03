using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Shared state and utilities for raid rank processing.
    /// </summary>
    public static class RaidRankPatches
    {
        // Track raid points for use in pawn processing
        public static float lastRaidPoints = 0f;
        public static Map lastRaidMap = null;
        
        // Track processed lords to avoid double-processing
        private static HashSet<int> processedLords = new HashSet<int>();

        /// <summary>
        /// Check if a LordJob is a raid-type job (hostile or allied).
        /// </summary>
        public static bool IsRaidLordJob(LordJob lordJob)
        {
            if (lordJob == null) return false;

            string jobTypeName = lordJob.GetType().Name;

            // Hostile raid lord job types
            bool isHostileRaid = jobTypeName.Contains("AssaultColony") ||
                   jobTypeName.Contains("Raid") ||
                   jobTypeName.Contains("Siege") ||
                   jobTypeName.Contains("Sapper") ||
                   jobTypeName.Contains("Assault") ||
                   jobTypeName.Contains("Kidnap") ||
                   jobTypeName.Contains("Steal") ||
                   jobTypeName.Contains("Besiege");
            
            // Allied reinforcement lord job types
            bool isAlliedRaid = jobTypeName.Contains("DefendPoint") ||
                   jobTypeName.Contains("AssistColony") ||
                   jobTypeName.Contains("Defend") ||
                   jobTypeName.Contains("MilitaryAid") ||
                   jobTypeName.Contains("Reinforce");
            
            return isHostileRaid || isAlliedRaid;
        }
        
        /// <summary>
        /// Check if a LordJob is specifically an allied assistance job.
        /// </summary>
        public static bool IsAlliedLordJob(LordJob lordJob)
        {
            if (lordJob == null) return false;

            string jobTypeName = lordJob.GetType().Name;

            return jobTypeName.Contains("DefendPoint") ||
                   jobTypeName.Contains("AssistColony") ||
                   jobTypeName.Contains("Defend") ||
                   jobTypeName.Contains("MilitaryAid") ||
                   jobTypeName.Contains("Reinforce");
        }

        /// <summary>
        /// Estimate raid points from the pawns' combat power if we don't have recorded points.
        /// This is a fallback - we prefer actual raid points from the incident.
        /// For early game, we apply additional reduction to prevent overwhelming weak colonies.
        /// </summary>
        public static float EstimateRaidPoints(List<Pawn> pawns)
        {
            float total = 0f;
            foreach (var pawn in pawns)
            {
                total += pawn.kindDef?.combatPower ?? 50f;
            }
            
            // When estimating, apply a reduction factor since combat power 
            // doesn't directly map to raid points (raid points are usually lower)
            // This prevents estimated raids from getting too high ranks
            float estimatedPoints = total * 0.6f;
            
            if (Prefs.DevMode)
            {
                Log.Warning($"[Isekai Raid] Using ESTIMATED points (fallback): {estimatedPoints:F0} from {pawns.Count} pawns");
            }
            
            return estimatedPoints;
        }

        /// <summary>
        /// Check if this lord was already processed.
        /// </summary>
        public static bool WasLordProcessed(Lord lord)
        {
            if (lord == null) return true;
            return processedLords.Contains(lord.GetHashCode());
        }

        /// <summary>
        /// Mark a lord as processed.
        /// </summary>
        public static void MarkLordProcessed(Lord lord)
        {
            if (lord == null) return;
            processedLords.Add(lord.GetHashCode());
            
            // Clean up old entries periodically
            if (processedLords.Count > 100)
            {
                processedLords.Clear();
            }
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            processedLords.Clear();
            lastRaidPoints = 0f;
            lastRaidMap = null;
        }
    }

    /// <summary>
    /// Captures raid points when a raid incident is about to execute.
    /// Guarded: uses string method name for stability across versions.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_IncidentWorker_TryExecute
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(IncidentWorker), "TryExecute");
            if (method == null)
            {
                Log.Warning("[Isekai] IncidentWorker.TryExecute not found \u2014 raid point tracking patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(IncidentWorker), "TryExecute");
        }

        [HarmonyPrefix]
        public static void Prefix(IncidentWorker __instance, IncidentParms parms)
        {
            // Capture points for raid-type incidents (hostile) and reinforcement incidents (allied)
            if (__instance == null || parms == null) return;
            
            // Check for hostile raids
            bool isHostileRaid = __instance is IncidentWorker_Raid;
            
            // Check for allied reinforcement incidents
            string incidentTypeName = __instance.GetType().Name;
            bool isAlliedReinforcement = incidentTypeName.Contains("RaidFriendly") ||
                                         incidentTypeName.Contains("MilitaryAid") ||
                                         incidentTypeName.Contains("AllyAid") ||
                                         incidentTypeName.Contains("Reinforcement") ||
                                         incidentTypeName.Contains("TraderCaravan");
            
            if (!isHostileRaid && !isAlliedReinforcement) return;

            RaidRankPatches.lastRaidPoints = parms.points;
            RaidRankPatches.lastRaidMap = parms.target as Map;

            if (Prefs.DevMode)
            {
                string raidType = isHostileRaid ? "Hostile" : "Allied";
                Log.Message($"[Isekai Raid] {raidType} group incoming! Points: {parms.points:F0}, " +
                           $"Faction: {parms.faction?.Name ?? "Unknown"}, " +
                           $"Type: {(parms.raidStrategy?.defName ?? incidentTypeName)}");
            }
        }
    }

    /// <summary>
    /// Hook into Lord creation for raids.
    /// Lords coordinate group AI, and raids always create a Lord for the attackers.
    /// This is the most reliable way to catch raid pawns after they spawn.
    /// Guarded: uses string method name for stability across versions.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_LordMaker_MakeNewLord
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(LordMaker), "MakeNewLord");
            if (method == null)
            {
                Log.Warning("[Isekai] LordMaker.MakeNewLord not found \u2014 raid rank patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(LordMaker), "MakeNewLord");
        }

        [HarmonyPostfix]
        public static void Postfix(Lord __result, Faction faction, LordJob lordJob, Map map, IEnumerable<Pawn> startingPawns)
        {
            try
            {
                // Process both hostile and allied raid lords
                if (__result == null) return;
                if (faction == null) return;
                if (map == null) return;
                
                // Check if this faction is relevant (hostile OR allied)
                bool isHostile = faction.HostileTo(Faction.OfPlayer);
                bool isAllied = !isHostile && (faction.AllyOrNeutralTo(Faction.OfPlayer) || faction == Faction.OfPlayer);
                
                // For hostile factions, check for raid lord jobs
                // For allied factions, check for assistance lord jobs
                bool isRelevantJob = false;
                if (isHostile)
                {
                    isRelevantJob = RaidRankPatches.IsRaidLordJob(lordJob);
                }
                else if (isAllied)
                {
                    isRelevantJob = RaidRankPatches.IsAlliedLordJob(lordJob) || RaidRankPatches.IsRaidLordJob(lordJob);
                }
                
                if (!isRelevantJob) return;

                // Avoid double-processing
                if (RaidRankPatches.WasLordProcessed(__result)) return;
                RaidRankPatches.MarkLordProcessed(__result);

                // Get the pawns from the lord or starting pawns
                List<Pawn> lordPawns = null;
                
                if (__result.ownedPawns != null && __result.ownedPawns.Count > 0)
                {
                    lordPawns = __result.ownedPawns.Where(p => 
                        p != null && !p.Dead && p.RaceProps.Humanlike).ToList();
                }
                
                if ((lordPawns == null || lordPawns.Count == 0) && startingPawns != null)
                {
                    lordPawns = startingPawns.Where(p => 
                        p != null && !p.Dead && p.RaceProps.Humanlike).ToList();
                }

                if (lordPawns == null || lordPawns.Count == 0) return;

                // Use the last captured raid points if available, otherwise estimate
                float raidPoints = RaidRankPatches.lastRaidPoints > 0 
                    ? RaidRankPatches.lastRaidPoints 
                    : RaidRankPatches.EstimateRaidPoints(lordPawns);

                // Process the pawns
                RaidRankSystem.ProcessRaidPawns(lordPawns, raidPoints, map);

                if (Prefs.DevMode)
                {
                    string groupType = isHostile ? "hostile" : "allied";
                    Log.Message($"[Isekai Raid] Processed {lordPawns.Count} {groupType} pawns, {raidPoints:F0} points, " +
                               $"Lord: {lordJob?.GetType().Name ?? "Unknown"}");
                }

                // Reset tracking after processing
                RaidRankPatches.lastRaidPoints = 0f;
                RaidRankPatches.lastRaidMap = null;
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Raid] Error processing raid pawns: {ex.Message}");
            }
        }
    }
}
