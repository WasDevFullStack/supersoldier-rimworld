using HarmonyLib;
using RimWorld;
using Verse;
using System.Collections.Generic;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Harmony patches that apply stat bonuses/penalties based on mob rank
    /// This makes higher ranked creatures actually more dangerous
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MobRankStatPatches
    {
        // Stats that should be affected by rank
        private static readonly HashSet<StatDef> DamageStats = new HashSet<StatDef>();
        private static readonly HashSet<StatDef> ArmorStats = new HashSet<StatDef>();
        private static readonly HashSet<StatDef> SpeedStats = new HashSet<StatDef>();
        
        // Recursion guard to prevent stack overflow during rank calculation
        [System.ThreadStatic]
        private static bool isCalculatingRank;
        
        /// <summary>
        /// Check if we're currently calculating rank (to prevent recursion)
        /// </summary>
        public static bool IsCalculatingRank => isCalculatingRank;
        
        /// <summary>
        /// Set the calculating rank flag
        /// </summary>
        public static void SetCalculatingRank(bool value) => isCalculatingRank = value;
        
        static MobRankStatPatches()
        {
            // Initialize stat sets after defs are loaded
            if (StatDefOf.MeleeDamageFactor != null)
                DamageStats.Add(StatDefOf.MeleeDamageFactor);
            if (StatDefOf.MeleeHitChance != null)
                DamageStats.Add(StatDefOf.MeleeHitChance);
            if (StatDefOf.MeleeDodgeChance != null)
                DamageStats.Add(StatDefOf.MeleeDodgeChance);
                
            if (StatDefOf.ArmorRating_Sharp != null)
                ArmorStats.Add(StatDefOf.ArmorRating_Sharp);
            if (StatDefOf.ArmorRating_Blunt != null)
                ArmorStats.Add(StatDefOf.ArmorRating_Blunt);
            if (StatDefOf.ArmorRating_Heat != null)
                ArmorStats.Add(StatDefOf.ArmorRating_Heat);
                
            if (StatDefOf.MoveSpeed != null)
                SpeedStats.Add(StatDefOf.MoveSpeed);
        }
        
        /// <summary>
        /// Get the MobRankComponent from a pawn if they have one
        /// </summary>
        public static MobRankComponent GetMobRankComp(Pawn pawn)
        {
            if (pawn == null) return null;
            return pawn.TryGetComp<MobRankComponent>();
        }
        
        /// <summary>
        /// Calculate the stat offset for a pawn based on their rank
        /// </summary>
        public static float GetRankStatOffset(Pawn pawn, StatDef stat)
        {
            var rankComp = GetMobRankComp(pawn);
            if (rankComp == null) return 0f;
            
            // Armor gets additive bonus
            if (ArmorStats.Contains(stat))
            {
                float armorBonus = MobRankUtility.GetRankArmorBonus(rankComp.Rank);
                // Elite creatures get +10% more armor
                if (rankComp.IsElite) armorBonus += 0.10f;
                return armorBonus;
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Calculate the stat factor (multiplier) for a pawn based on their rank
        /// </summary>
        public static float GetRankStatFactor(Pawn pawn, StatDef stat)
        {
            var rankComp = GetMobRankComp(pawn);
            if (rankComp == null) return 1f;
            
            float factor = 1f;
            
            // Damage stats use damage multiplier
            if (DamageStats.Contains(stat))
            {
                factor = MobRankUtility.GetRankDamageMultiplier(rankComp.Rank);
            }
            // Speed stats use speed multiplier
            else if (SpeedStats.Contains(stat))
            {
                factor = MobRankUtility.GetRankSpeedMultiplier(rankComp.Rank);
            }
            
            // Elite creatures get +15% more on all multiplied stats
            if (rankComp.IsElite && factor != 1f)
            {
                factor *= 1.15f;
            }
            
            return factor;
        }
    }
    
    /// <summary>
    /// Patch StatWorker.GetValueUnfinalized to apply rank-based stat modifications
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class StatWorker_GetValueUnfinalized_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, StatRequest req, StatDef ___stat)
        {
            // Prevent recursion during rank calculation
            if (MobRankStatPatches.IsCalculatingRank) return;
            
            if (!req.HasThing) return;
            
            Pawn pawn = req.Thing as Pawn;
            if (pawn == null) return;
            
            // Don't modify player colonist stats
            if (pawn.Faction != null && pawn.Faction.IsPlayer) return;
            
            // Get rank component
            var rankComp = MobRankStatPatches.GetMobRankComp(pawn);
            if (rankComp == null) return;
            
            // Apply stat offset (additive)
            float offset = MobRankStatPatches.GetRankStatOffset(pawn, ___stat);
            if (offset != 0f)
            {
                __result += offset;
            }
            
            // Apply stat factor (multiplicative)
            float factor = MobRankStatPatches.GetRankStatFactor(pawn, ___stat);
            if (factor != 1f)
            {
                __result *= factor;
            }
        }
    }
    
    /// <summary>
    /// Patch to scale body part max health based on rank
    /// This makes higher rank creatures much tankier
    /// </summary>
    [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
    public static class BodyPartDef_GetMaxHealth_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, Pawn pawn)
        {
            // Prevent recursion during rank calculation
            if (MobRankStatPatches.IsCalculatingRank) return;
            
            if (pawn == null) return;
            
            // Don't modify player colonist health
            if (pawn.Faction != null && pawn.Faction.IsPlayer) return;
            
            var rankComp = MobRankStatPatches.GetMobRankComp(pawn);
            if (rankComp == null) return;
            
            float healthMult = MobRankUtility.GetRankHealthMultiplier(rankComp.Rank);

            // Elite creatures get +20% more health
            if (rankComp.IsElite)
            {
                healthMult *= 1.20f;
            }

            // Registered world bosses get an extra HP multiplier on top of the
            // rank+elite scaling. Without this, the rank multiplier alone
            // (Nation 12× × elite 1.2 = 14.4×) is too thin to survive a player
            // + hostile-faction dogpile — observed with the Bulbfreak boss.
            // The flag is set by WorldBossSizePatch.RegisterWorldBoss and only
            // fires for bosses spawned via QuestPart_WorldBoss; wave mobs and
            // regular Nation-rank pawns are unaffected.
            if (WorldBoss.WorldBossSizePatch.IsWorldBoss(pawn))
            {
                healthMult *= WorldBoss.WorldBossSizePatch.BOSS_HEALTH_BOOST;
            }

            __result *= healthMult;
        }
    }
    
    /// <summary>
    /// Patch DamageInfo to scale damage dealt by ranked creatures
    /// This catches all damage types (melee, ranged, etc.)
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Thing_TakeDamage_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ref DamageInfo dinfo)
        {
            // Prevent recursion during rank calculation
            if (MobRankStatPatches.IsCalculatingRank) return;
            
            if (dinfo.Instigator == null) return;
            
            Pawn attackerPawn = dinfo.Instigator as Pawn;
            if (attackerPawn == null) return;
            
            // Don't modify player colonist damage
            if (attackerPawn.Faction != null && attackerPawn.Faction.IsPlayer) return;
            
            var rankComp = MobRankStatPatches.GetMobRankComp(attackerPawn);
            if (rankComp == null) return;
            
            float damageMult = MobRankUtility.GetRankDamageMultiplier(rankComp.Rank);
            
            // Elite creatures get +15% more damage
            if (rankComp.IsElite)
            {
                damageMult *= 1.15f;
            }
            
            if (damageMult != 1f)
            {
                dinfo.SetAmount(dinfo.Amount * damageMult);
            }
        }
    }
}
