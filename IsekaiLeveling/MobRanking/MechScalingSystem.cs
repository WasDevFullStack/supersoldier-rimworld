using System;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Scales player-created mechs (Biotech DLC) based on their mechanitor's Isekai stats.
    /// Without this, player mechs have no rank bonuses and "fold" in combat.
    /// 
    /// Approach: Player mechs inherit a percentage of their mechanitor's stat bonuses
    /// as flat health/damage/armor scaling. This makes investing in a mechanitor's
    /// stats also strengthen their mech army.
    /// 
    /// Scaling formula: mechBonus = mechanitorLevel * scaleFactor
    /// - Health: +1% per mechanitor level
    /// - Damage: +0.5% per mechanitor level  
    /// - Armor: +0.3% per mechanitor level (additive)
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MechScalingSystem
    {
        /// <summary>
        /// Check if a pawn is a player-owned mech that should receive scaling
        /// </summary>
        public static bool IsPlayerMech(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!ModsConfig.BiotechActive) return false;
            if (!pawn.RaceProps.IsMechanoid) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            return pawn.GetOverseer() != null;
        }

        /// <summary>
        /// Get the mechanitor's IsekaiComponent for a player mech
        /// </summary>
        public static IsekaiComponent GetMechanitorComp(Pawn mech)
        {
            if (mech == null) return null;
            if (!ModsConfig.BiotechActive) return null;
            
            Pawn overseer = mech.GetOverseer();
            if (overseer == null) return null;
            
            return overseer.GetComp<IsekaiComponent>();
        }

        /// <summary>
        /// Get health multiplier for a player mech based on mechanitor's level and VIT
        /// </summary>
        public static float GetMechHealthMultiplier(Pawn mech)
        {
            var mechComp = GetMechanitorComp(mech);
            if (mechComp?.stats == null) return 1f;
            
            int level = mechComp.currentLevel;
            int vit = mechComp.stats.vitality;
            
            // Base: +1% per mechanitor level
            // VIT bonus: +0.5% per VIT point above 5
            float levelBonus = level * 0.01f;
            float vitBonus = Math.Max(0, vit - 5) * 0.005f;
            
            return 1f + levelBonus + vitBonus;
        }

        /// <summary>
        /// Get damage multiplier for a player mech based on mechanitor's level and INT
        /// </summary>
        public static float GetMechDamageMultiplier(Pawn mech)
        {
            var mechComp = GetMechanitorComp(mech);
            if (mechComp?.stats == null) return 1f;
            
            int level = mechComp.currentLevel;
            int intelligence = mechComp.stats.intelligence;
            
            // Base: +0.5% per mechanitor level
            // INT bonus: +0.3% per INT point above 5 (smarter = better mech control)
            float levelBonus = level * 0.005f;
            float intBonus = Math.Max(0, intelligence - 5) * 0.003f;
            
            return 1f + levelBonus + intBonus;
        }

        /// <summary>
        /// Get armor offset for a player mech based on mechanitor's level
        /// </summary>
        public static float GetMechArmorOffset(Pawn mech)
        {
            var mechComp = GetMechanitorComp(mech);
            if (mechComp?.stats == null) return 0f;
            
            int level = mechComp.currentLevel;
            int intelligence = mechComp.stats.intelligence;
            
            // +0.3% armor per mechanitor level + 0.2% per INT above 5
            float levelBonus = level * 0.003f;
            float intBonus = Math.Max(0, intelligence - 5) * 0.002f;
            
            return levelBonus + intBonus;
        }

        /// <summary>
        /// Get speed multiplier for a player mech based on mechanitor's DEX
        /// </summary>
        public static float GetMechSpeedMultiplier(Pawn mech)
        {
            var mechComp = GetMechanitorComp(mech);
            if (mechComp?.stats == null) return 1f;
            
            int dex = mechComp.stats.dexterity;
            
            // +0.5% speed per DEX point above 5
            float dexBonus = Math.Max(0, dex - 5) * 0.005f;
            
            return 1f + dexBonus;
        }
    }

    /// <summary>
    /// Patch stat calculations to apply mech scaling for player mechs
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    [HarmonyAfter("JellyCreative.IsekaiLeveling")] // Run after base stat patches
    public static class MechStatScaling_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, StatRequest req, StatDef ___stat)
        {
            try
            {
                if (!req.HasThing) return;
                if (!IsekaiLevelingSettings.EnableMechanitorXP) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null) return;
                if (!MechScalingSystem.IsPlayerMech(pawn)) return;

                // Apply damage scaling
                if (___stat == StatDefOf.MeleeDamageFactor || ___stat == StatDefOf.MeleeHitChance)
                {
                    float mult = MechScalingSystem.GetMechDamageMultiplier(pawn);
                    __result *= mult;
                }
                // Apply speed scaling
                else if (___stat == StatDefOf.MoveSpeed)
                {
                    float mult = MechScalingSystem.GetMechSpeedMultiplier(pawn);
                    __result *= mult;
                }
                // Apply armor scaling
                else if (___stat == StatDefOf.ArmorRating_Sharp || 
                         ___stat == StatDefOf.ArmorRating_Blunt || 
                         ___stat == StatDefOf.ArmorRating_Heat)
                {
                    float offset = MechScalingSystem.GetMechArmorOffset(pawn);
                    __result += offset;
                }
            }
            catch { /* Silently ignore */ }
        }
    }

    /// <summary>
    /// Patch body part health for player mechs
    /// </summary>
    [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
    [HarmonyAfter("JellyCreative.IsekaiLeveling")]
    public static class MechHealthScaling_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, Pawn pawn)
        {
            try
            {
                if (pawn == null) return;
                if (!IsekaiLevelingSettings.EnableMechanitorXP) return;
                if (!MechScalingSystem.IsPlayerMech(pawn)) return;
                
                float mult = MechScalingSystem.GetMechHealthMultiplier(pawn);
                __result *= mult;
            }
            catch { /* Silently ignore */ }
        }
    }
}
