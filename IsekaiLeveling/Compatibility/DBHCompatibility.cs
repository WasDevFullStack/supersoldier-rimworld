using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Compatibility patches for Dubs Bad Hygiene.
    /// Affects: Disease resistance from hygiene, bladder/hygiene need rates
    /// 
    /// Stat Mappings:
    /// - VIT → Disease/infection resistance from poor hygiene
    /// - WIS → Slower hygiene need degradation (remembers to stay clean)
    /// </summary>
    public static class DBHCompatibility
    {
        private static bool initialized = false;
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                initialized = true;
                Log.Message("[Isekai Leveling] Dubs Bad Hygiene compatibility initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Isekai Leveling] Error initializing DBH compatibility: {ex}");
                throw;
            }
        }
        
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
        }
        
        /// <summary>
        /// VIT reduces disease chance from poor hygiene.
        /// Each VIT above 5 = +1.5% resistance
        /// At 100 VIT = +142% disease resistance
        /// </summary>
        public static float GetHygieneDiseaseResistance(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int vitStat = comp.stats.vitality;
            float bonus = (vitStat - 5) * 0.015f;
            return 1f + Mathf.Clamp(bonus, -0.06f, 1.5f);
        }
        
        /// <summary>
        /// WIS slows hygiene need degradation.
        /// Each WIS above 5 = -0.5% need fall rate
        /// At 100 WIS = -47.5% slower hygiene need
        /// </summary>
        public static float GetHygieneNeedMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float reduction = (wisStat - 5) * 0.005f;
            return 1f - Mathf.Clamp(reduction, -0.02f, 0.5f); // Lower is better
        }
    }
}
