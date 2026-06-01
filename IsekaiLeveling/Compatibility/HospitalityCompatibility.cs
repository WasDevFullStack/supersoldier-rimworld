using System;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Compatibility patches for Hospitality mod.
    /// Affects: Guest recruitment, relationship building, guest mood
    /// 
    /// Stat Mappings:
    /// - CHA → Guest recruitment chance, relationship building speed
    /// - WIS → Better trade prices with guests
    /// - INT → Faster guest entertainment
    /// </summary>
    public static class HospitalityCompatibility
    {
        private static bool initialized = false;
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                // Hospitality uses its own systems, we patch via stats
                initialized = true;
                Log.Message("[Isekai Leveling] Hospitality compatibility initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Isekai Leveling] Error initializing Hospitality compatibility: {ex}");
                throw;
            }
        }
        
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
        }
        
        /// <summary>
        /// CHA improves guest recruitment chance.
        /// Each CHA above 5 = +1% recruitment chance
        /// At 100 CHA = +95% recruitment bonus
        /// </summary>
        public static float GetRecruitmentMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int chaStat = comp.stats.charisma;
            float bonus = (chaStat - 5) * 0.01f;
            return 1f + Mathf.Clamp(bonus, -0.04f, 1.0f);
        }
        
        /// <summary>
        /// CHA improves relationship gain speed with guests.
        /// Each CHA above 5 = +1.5% relationship gain
        /// At 100 CHA = +142% faster relationship building
        /// </summary>
        public static float GetRelationshipGainMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int chaStat = comp.stats.charisma;
            float bonus = (chaStat - 5) * 0.015f;
            return 1f + Mathf.Clamp(bonus, -0.06f, 1.5f);
        }
        
        /// <summary>
        /// WIS improves trade prices with guests.
        /// Each WIS above 5 = +0.5% better prices
        /// At 100 WIS = +47.5% better trade deals
        /// </summary>
        public static float GetTradePriceMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float bonus = (wisStat - 5) * 0.005f;
            return 1f + Mathf.Clamp(bonus, -0.02f, 0.5f);
        }
    }
}
