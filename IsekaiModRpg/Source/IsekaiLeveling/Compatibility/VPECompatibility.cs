using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Compatibility patches for Vanilla Psycasts Expanded.
    /// Affects: Psyfocus, Meditation, Psycast abilities
    /// 
    /// Stat Mappings:
    /// - WIS → +Psyfocus gain rate (meditation effectiveness)
    /// - INT → +Psycast neural heat recovery (faster cooldown)
    /// - CHA → +Social psycast effectiveness (word of inspiration, etc.)
    /// </summary>
    public static class VPECompatibility
    {
        private static bool initialized = false;
        
        // Cached stat definitions (for future use if needed)
        #pragma warning disable CS0169
        private static StatDef meditationFocusGain;
        #pragma warning restore CS0169
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                // VPE uses vanilla psycast system, so we patch those stats
                // These are base game stats that VPE expands upon
                meditationFocusGain = StatDefOf.MeditationFocusGain;
                
                // Apply harmony patches
                ApplyHarmonyPatches();
                
                initialized = true;
                Log.Message("[Isekai Leveling] Vanilla Psycasts Expanded compatibility initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Isekai Leveling] Error initializing VPE compatibility: {ex}");
                throw;
            }
        }
        
        private static void ApplyHarmonyPatches()
        {
            // Stat bonuses are applied via StatParts registered in XML
            // This allows for clean integration without direct patches
        }
        
        /// <summary>
        /// Apply stat effects to a pawn's psycast abilities.
        /// Called when Isekai stats change.
        /// </summary>
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            // Force psycast stat recalculation
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
            
            // If pawn has a psylink, notify it of stat changes
            if (pawn.psychicEntropy != null)
            {
                // Recalculate neural heat limits
            }
        }
        
        /// <summary>
        /// Calculate meditation focus gain multiplier based on WIS stat.
        /// Each WIS above 5 adds 1% meditation effectiveness.
        /// At 100 WIS = 1.0 + (95 * 0.01) = 1.95x meditation focus gain
        /// </summary>
        public static float GetMeditationMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float bonus = (wisStat - 5) * 0.01f;
            return 1f + Mathf.Clamp(bonus, -0.04f, 2.0f); // Min 0.96x, Max 3.0x
        }
        
        /// <summary>
        /// Calculate neural heat recovery multiplier based on INT stat.
        /// Each INT above 5 adds 1.5% heat recovery.
        /// At 100 INT = 1.0 + (95 * 0.015) = 2.425x recovery rate
        /// </summary>
        public static float GetNeuralHeatRecoveryMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int intStat = comp.stats.intelligence;
            float bonus = (intStat - 5) * 0.015f;
            return 1f + Mathf.Clamp(bonus, -0.06f, 1.5f);
        }
        
        /// <summary>
        /// Calculate psyfocus sensitivity multiplier based on WIS.
        /// This affects how much psyfocus is gained/lost from various sources.
        /// </summary>
        public static float GetPsyfocusSensitivityMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float bonus = (wisStat - 5) * 0.008f;
            return 1f + Mathf.Clamp(bonus, -0.032f, 1.5f); // Max 2.5x
        }
        
        /// <summary>
        /// Check if a pawn has psylink abilities.
        /// </summary>
        public static bool HasPsylink(Pawn pawn)
        {
            return pawn?.psychicEntropy != null && pawn.GetPsylinkLevel() > 0;
        }
        
        /// <summary>
        /// Get the current psyfocus percentage (0-1).
        /// </summary>
        public static float GetPsyfocusPercent(Pawn pawn)
        {
            if (pawn?.psychicEntropy == null) return 0f;
            return pawn.psychicEntropy.CurrentPsyfocus;
        }
    }
}
