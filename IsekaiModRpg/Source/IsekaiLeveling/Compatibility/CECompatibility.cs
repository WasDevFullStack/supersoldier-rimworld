using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Compatibility patches for Combat Extended.
    /// Affects: Accuracy, Recoil, Suppression, Reload Speed
    /// 
    /// Stat Mappings:
    /// - DEX → Aiming accuracy, weapon sway reduction, faster aim time
    /// - STR → Recoil control, melee penetration
    /// - VIT → Suppression resistance
    /// - INT → Faster reload speed (technique/muscle memory)
    /// </summary>
    public static class CECompatibility
    {
        private static bool initialized = false;
        
        // Combat Extended stat defs (found via reflection)
        private static StatDef aimingAccuracy;
        private static StatDef recoil;
        private static StatDef suppressionResistance;
        private static StatDef reloadSpeed;
        private static StatDef meleePenetration;
        private static StatDef aimingTime;
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                // CE stats are typically prefixed with CE_
                aimingAccuracy = DefDatabase<StatDef>.GetNamedSilentFail("AimingAccuracy");
                recoil = DefDatabase<StatDef>.GetNamedSilentFail("CE_Recoil");
                suppressionResistance = DefDatabase<StatDef>.GetNamedSilentFail("CE_SuppressionResistance");
                reloadSpeed = DefDatabase<StatDef>.GetNamedSilentFail("ReloadSpeed");
                meleePenetration = DefDatabase<StatDef>.GetNamedSilentFail("MeleePenetrationFactor");
                aimingTime = DefDatabase<StatDef>.GetNamedSilentFail("AimingDelayFactor");
                
                initialized = true;
                Log.Message("[Isekai Leveling] Combat Extended compatibility initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Isekai Leveling] Error initializing CE compatibility: {ex}");
                throw;
            }
        }
        
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
        }
        
        /// <summary>
        /// DEX improves aiming accuracy.
        /// Each DEX above 5 = +0.8% accuracy
        /// At 100 DEX = +76% accuracy bonus
        /// </summary>
        public static float GetAimingAccuracyMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int dexStat = comp.stats.dexterity;
            float bonus = (dexStat - 5) * 0.008f;
            return 1f + Mathf.Clamp(bonus, -0.04f, 0.8f);
        }
        
        /// <summary>
        /// DEX reduces aim time (faster targeting).
        /// Each DEX above 5 = -0.5% aim time
        /// At 100 DEX = -47.5% aim time
        /// </summary>
        public static float GetAimingTimeMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int dexStat = comp.stats.dexterity;
            float reduction = (dexStat - 5) * 0.005f;
            return 1f - Mathf.Clamp(reduction, -0.02f, 0.5f); // Lower is better
        }
        
        /// <summary>
        /// STR reduces recoil.
        /// Each STR above 5 = -1.5% recoil
        /// At 100 STR = -95% recoil (capped)
        /// </summary>
        public static float GetRecoilMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int strStat = comp.stats.strength;
            float reduction = (strStat - 5) * 0.015f;
            return 1f - Mathf.Clamp(reduction, -0.04f, 0.95f); // Lower is better
        }
        
        /// <summary>
        /// VIT reduces suppressability (makes pawn harder to suppress).
        /// Each VIT above 5 = -1.5% suppressability
        /// At 100 VIT = -142% suppressability (clamped to minimum)
        /// Note: Lower suppressability = harder to suppress (good)
        /// </summary>
        public static float GetSuppressabilityMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int vitStat = comp.stats.vitality;
            float reduction = (vitStat - 5) * 0.015f;
            return 1f - Mathf.Clamp(reduction, -0.08f, 0.95f); // Lower is better (harder to suppress)
        }
        
        /// <summary>
        /// INT improves reload speed.
        /// Each INT above 5 = +1.5% reload speed
        /// At 100 INT = +142% faster reload
        /// </summary>
        public static float GetReloadSpeedMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int intStat = comp.stats.intelligence;
            float bonus = (intStat - 5) * 0.015f;
            return 1f + Mathf.Clamp(bonus, -0.06f, 1.5f);
        }
        
        /// <summary>
        /// STR improves melee armor penetration.
        /// Each STR above 5 = +3% penetration
        /// At 100 STR = +285% melee penetration - cuts through armor like butter
        /// </summary>
        public static float GetMeleePenetrationMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int strStat = comp.stats.strength;
            float bonus = (strStat - 5) * 0.03f;
            return 1f + Mathf.Clamp(bonus, -0.1f, 3.0f);
        }
        
        /// <summary>
        /// STR improves melee parry chance.
        /// Each STR above 5 = +1% parry chance (additive)
        /// At 100 STR = +95% parry chance
        /// </summary>
        public static float GetMeleeParryBonus(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 0f;
            int strStat = comp.stats.strength;
            float bonus = (strStat - 5) * 0.01f;
            return Mathf.Clamp(bonus, -0.04f, 0.95f);
        }
        
        /// <summary>
        /// STR improves counter-parry damage.
        /// Each STR above 5 = +2% counter damage
        /// At 100 STR = +190% counter-attack damage
        /// </summary>
        public static float GetMeleeCounterParryMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int strStat = comp.stats.strength;
            float bonus = (strStat - 5) * 0.02f;
            return 1f + Mathf.Clamp(bonus, -0.08f, 2.0f);
        }
        
        /// <summary>
        /// STR improves melee crit chance.
        /// Each STR above 5 = +0.5% crit chance (additive)
        /// At 100 STR = +47.5% crit chance
        /// </summary>
        public static float GetMeleeCritBonus(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 0f;
            int strStat = comp.stats.strength;
            float bonus = (strStat - 5) * 0.005f;
            return Mathf.Clamp(bonus, -0.02f, 0.5f);
        }
        
        /// <summary>
        /// STR improves base melee damage (CE version).
        /// Each STR above 5 = +5% melee damage
        /// At 100 STR = +475% melee damage (x5.75)
        /// This stacks with vanilla MeleeDamageFactor!
        /// </summary>
        public static float GetMeleeDamageMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int strStat = comp.stats.strength;
            float bonus = (strStat - 5) * 0.05f;
            return 1f + Mathf.Clamp(bonus, -0.2f, 5.0f);
        }
        
        /// <summary>
        /// VIT improves tough/bulk factor for reduced incoming damage.
        /// Each VIT above 5 = +1% toughness
        /// At 100 VIT = +95% toughness
        /// </summary>
        public static float GetToughnessMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int vitStat = comp.stats.vitality;
            float bonus = (vitStat - 5) * 0.01f;
            return 1f + Mathf.Clamp(bonus, -0.04f, 1.0f);
        }
    }
}
