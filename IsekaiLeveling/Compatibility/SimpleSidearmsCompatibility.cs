using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Compatibility patches for Simple Sidearms.
    /// Affects: Weapon swap speed, sidearm capacity
    /// 
    /// Stat Mappings:
    /// - DEX → Faster weapon switching
    /// - STR → Can carry more sidearms (via mass capacity)
    /// </summary>
    public static class SimpleSidearmsCompatibility
    {
        private static bool initialized = false;
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                initialized = true;
                Log.Message("[Isekai Leveling] Simple Sidearms compatibility initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Isekai Leveling] Error initializing Simple Sidearms compatibility: {ex}");
                throw;
            }
        }
        
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
        }
        
        /// <summary>
        /// DEX improves weapon swap speed.
        /// Each DEX above 5 = +1% faster swap
        /// At 100 DEX = +95% faster weapon switching
        /// </summary>
        public static float GetWeaponSwapSpeedMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int dexStat = comp.stats.dexterity;
            float bonus = (dexStat - 5) * 0.01f;
            return 1f + Mathf.Clamp(bonus, -0.04f, 1.0f);
        }
    }
}
