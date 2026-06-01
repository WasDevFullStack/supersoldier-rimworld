using System;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Handles rewards when ranked mobs are killed:
    /// - XP rewards to the killer
    /// - Mana Core drops based on rank
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MobRankRewards
    {
        // Base XP for killing a D-rank creature
        private const float BASE_KILL_XP = 15f;
        
        // Mana Core ThingDefs (loaded at runtime)
        private static ThingDef smallManaCoreeDef;
        private static ThingDef manaCoreDef;
        private static ThingDef bigManaCoreDef;
        private static ThingDef hugeManaCoreDef;
        
        static MobRankRewards()
        {
            // Load mana core defs after game starts
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                smallManaCoreeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_SmallManaCore");
                manaCoreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ManaCore");
                bigManaCoreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_BigManaCore");
                hugeManaCoreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_HugeManaCore");
                
                if (smallManaCoreeDef == null)
                    Log.Warning("[IsekaiLeveling] Could not find Isekai_SmallManaCore def");
            });
        }
        
        /// <summary>
        /// Called when a ranked mob is killed to handle rewards.
        /// </summary>
        /// <param name="capturedMap">
        /// The map captured by a Prefix before <see cref="Pawn.Kill"/> ran.
        /// Required because Kill() despawns the pawn — by the time this Postfix
        /// fires, <c>victim.Map</c> is null and any drop spawn would silently
        /// no-op. v1.1.5's bundled rewrite dropped this parameter, which is why
        /// no mana cores have been dropping since.
        /// </param>
        public static void OnMobKilled(Pawn victim, DamageInfo? dinfo, Map capturedMap)
        {
            if (victim == null) return;

            // Get the mob's rank component
            var rankComp = victim.TryGetComp<MobRankComponent>();
            if (rankComp == null) return;

            // Don't give rewards for player pawns
            if (victim.Faction != null && victim.Faction.IsPlayer) return;

            // Handle XP reward
            if (dinfo.HasValue && dinfo.Value.Instigator is Pawn killer)
            {
                GiveKillXP(killer, rankComp);
            }

            // Handle mana core drops on the captured map (victim.Map is null post-kill)
            SpawnManaCoreDrops(victim, rankComp, capturedMap);
        }
        
        /// <summary>
        /// Give XP to the killer based on the victim's rank
        /// </summary>
        private static void GiveKillXP(Pawn killer, MobRankComponent rankComp)
        {
            // Only give XP to player pawns
            if (killer.Faction == null || !killer.Faction.IsPlayer) return;
            
            // Get the killer's Isekai component
            var isekaiComp = killer.GetComp<IsekaiComponent>();
            if (isekaiComp == null) return;
            
            // Calculate XP based on rank
            float xpMultiplier = MobRankUtility.GetRankXPRewardMultiplier(rankComp.Rank);
            
            // Elite bonus
            if (rankComp.IsElite)
            {
                xpMultiplier *= 1.5f;
            }
            
            int xpGained = (int)(BASE_KILL_XP * xpMultiplier);
            
            // Add XP (method takes int, not float)
            string eliteText = rankComp.IsElite ? " ★" : "";
            isekaiComp.GainXP(xpGained, $"Rank {rankComp.RankString}{eliteText} Kill");
        }
        
        /// <summary>
        /// Spawn mana core drops based on the creature's rank.
        /// </summary>
        private static void SpawnManaCoreDrops(Pawn victim, MobRankComponent rankComp, Map map)
        {
            if (map == null) return; // captured pre-kill; victim.Map is null at this point
            if (smallManaCoreeDef == null) return; // Defs not loaded yet
            
            // Determine which mana core to drop and chance based on rank
            ThingDef coreDef = null;
            float dropChance = 0f;
            int maxCount = 1;
            
            switch (rankComp.Rank)
            {
                case MobRankTier.F:
                    coreDef = smallManaCoreeDef;
                    dropChance = 0.15f; // 15% chance
                    maxCount = 1;
                    break;
                    
                case MobRankTier.E:
                    coreDef = smallManaCoreeDef;
                    dropChance = 0.30f; // 30% chance
                    maxCount = 2;
                    break;
                    
                case MobRankTier.D:
                    coreDef = manaCoreDef;
                    dropChance = 0.25f; // 25% chance
                    maxCount = 1;
                    break;
                    
                case MobRankTier.C:
                    coreDef = manaCoreDef;
                    dropChance = 0.50f; // 50% chance
                    maxCount = 2;
                    break;
                    
                case MobRankTier.B:
                    coreDef = bigManaCoreDef;
                    dropChance = 0.40f; // 40% chance
                    maxCount = 1;
                    break;
                    
                case MobRankTier.A:
                    coreDef = bigManaCoreDef;
                    dropChance = 0.70f; // 70% chance
                    maxCount = 2;
                    break;
                    
                case MobRankTier.S:
                    coreDef = hugeManaCoreDef;
                    dropChance = 0.50f; // 50% chance
                    maxCount = 1;
                    break;
                    
                case MobRankTier.SS:
                    coreDef = hugeManaCoreDef;
                    dropChance = 0.80f; // 80% chance
                    maxCount = 2;
                    break;
                    
                case MobRankTier.SSS:
                    coreDef = hugeManaCoreDef;
                    dropChance = 1.0f; // 100% chance
                    maxCount = 3;
                    break;
            }
            
            if (coreDef == null) return;
            
            // Elite creatures have higher drop chance and can drop more
            if (rankComp.IsElite)
            {
                dropChance = Mathf.Min(1f, dropChance * 1.5f);
                maxCount += 1;
            }
            
            // Roll for drop
            if (Rand.Value > dropChance) return;
            
            // Determine count (1 to maxCount)
            int count = Rand.RangeInclusive(1, maxCount);
            
            // Spawn the mana core
            Thing manaCore = ThingMaker.MakeThing(coreDef);
            manaCore.stackCount = count;
            
            // Find a valid spawn position near the corpse on the CAPTURED map
            // (victim.PositionHeld still works for the corpse but is not used here
            // because the captured map is the authoritative reference).
            IntVec3 spawnPos = victim.PositionHeld.IsValid ? victim.PositionHeld : victim.Position;
            if (!spawnPos.IsValid || !spawnPos.InBounds(map) || !spawnPos.Standable(map))
            {
                spawnPos = CellFinder.RandomClosewalkCellNear(spawnPos, map, 2);
            }

            GenSpawn.Spawn(manaCore, spawnPos, map);

            // Optional: Visual effect (use the captured map — victim.DrawPos still
            // works because the corpse holds the position).
            FleckMaker.ThrowLightningGlow(spawnPos.ToVector3Shifted(), map, 0.5f);
        }
    }

    /// <summary>
    /// Harmony patch to trigger rewards when a pawn dies.
    /// Uses Prefix to capture the map BEFORE the kill — vanilla Kill() calls
    /// DeSpawn() during execution, so by the time the Postfix runs the pawn has
    /// no Map and any drop spawn would silently no-op.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Pawn_Kill_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, out Map __state)
        {
            __state = __instance?.Map;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo, Map __state)
        {
            try
            {
                MobRankRewards.OnMobKilled(__instance, dinfo, __state);
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiLeveling] Error in mob kill reward: {ex}");
            }
        }
    }
}
