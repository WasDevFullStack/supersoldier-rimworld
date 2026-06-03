using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// System for assigning ranks to raid enemies based on raid points.
    /// Takes into account colony strength while accounting for player pawn ranks inflating wealth.
    /// Caps regular raids at S rank - SS and SSS are reserved for special events/quests.
    /// </summary>
    public static class RaidRankSystem
    {
        private static readonly System.Random random = new System.Random();

        // Points thresholds for each base rank in raids
        // These are "effective points" after adjusting for player pawn power inflation
        // Raised thresholds so high ranks require significantly more raid strength
        private static readonly (MobRankTier rank, float minPoints)[] RankThresholds = new[]
        {
            (MobRankTier.S, 25000f),  // Extreme late game only
            (MobRankTier.A, 14000f),  // Very late game
            (MobRankTier.B, 7000f),   // Late game
            (MobRankTier.C, 3500f),   // Late-mid game
            (MobRankTier.D, 1200f),   // Mid game
            (MobRankTier.E, 400f),    // Early-mid game
            (MobRankTier.F, 0f),      // Baseline
        };

        // Variance weights: probability of getting each rank offset
        // Negative = weaker, positive = stronger
        // Balanced variance - heavily favors base rank or lower to keep raids fair
        private static readonly (int offset, float weight)[] BalancedVarianceWeights = new[]
        {
            (-2, 15f),   // 15% chance to be 2 ranks weaker
            (-1, 35f),   // 35% chance to be 1 rank weaker
            (0, 44f),    // 44% chance to be at base rank
            (+1, 5f),    // 5% chance to be 1 rank stronger
            (+2, 0.9f),  // 0.9% chance to be 2 ranks stronger (very rare)
            (+3, 0.1f),  // 0.1% chance to be 3 ranks stronger (nearly impossible)
        };
        
        // Classic RNG variance - more chaotic, can result in stronger enemies
        private static readonly (int offset, float weight)[] ClassicVarianceWeights = new[]
        {
            (-2, 10f),   // 10% chance to be 2 ranks weaker
            (-1, 25f),   // 25% chance to be 1 rank weaker
            (0, 45f),    // 45% chance to be at base rank
            (+1, 13f),   // 13% chance to be 1 rank stronger
            (+2, 5f),    // 5% chance to be 2 ranks stronger
            (+3, 2f),    // 2% chance to be 3 ranks stronger
        };

        /// <summary>
        /// Calculate effective raid points by adjusting for player pawn power inflation.
        /// High-rank player pawns contribute more to wealth, so we reduce the effective points
        /// to prevent raids from scaling too aggressively against strong colonies.
        /// Also applies protection for low-wealth colonies to prevent overwhelming early game.
        /// </summary>
        public static float GetEffectiveRaidPoints(float rawPoints, Map map)
        {
            if (map == null) return rawPoints;

            float totalPlayerPowerInflation = 0f;
            int playerPawnCount = 0;
            int maxPlayerLevel = 0;
            float colonyWealth = map.wealthWatcher?.WealthTotal ?? 0f;

            // Calculate how much player pawn ranks inflate colony wealth
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
            {
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) continue;

                playerPawnCount++;
                maxPlayerLevel = Math.Max(maxPlayerLevel, comp.currentLevel);
                
                // Higher level pawns contribute more to perceived wealth
                // This estimates how much their gear/capabilities add to raid calculations
                float pawnInflation = GetPowerInflationForLevel(comp.currentLevel);
                totalPlayerPowerInflation += pawnInflation;
            }

            // If no player pawns or minimal inflation, use raw points
            if (playerPawnCount == 0 || totalPlayerPowerInflation < 100f)
                return rawPoints;

            // Reduce effective points based on player power inflation
            // This prevents raids from spiraling out of control when players have high-rank pawns
            // We use a diminishing formula: effective = raw / (1 + inflationFactor)
            float inflationFactor = totalPlayerPowerInflation / (playerPawnCount * 2000f);
            inflationFactor = Math.Min(inflationFactor, 0.5f); // Cap at 50% reduction

            float effectivePoints = rawPoints / (1f + inflationFactor);
            
            // Additional protection for early game (low wealth colonies)
            // Prevents situations where a small raid gets inflated ranks
            // Only applies when not using Classic RNG mode
            bool useClassicRNG = IsekaiMod.Settings?.ClassicRNGRaids == true;
            if (!useClassicRNG && colonyWealth < 20000f)
            {
                // Scale down effective points for low-wealth colonies
                // At 4000 wealth = 20% of points, at 10000 = 50%, at 20000 = 100%
                float wealthFactor = Mathf.Clamp01(colonyWealth / 20000f);
                wealthFactor = Mathf.Max(0.2f, wealthFactor); // Minimum 20%
                effectivePoints *= wealthFactor;
                
                if (Prefs.DevMode)
                {
                    Log.Message($"[Isekai Raid] Low-wealth protection applied: wealth={colonyWealth:F0}, factor={wealthFactor:P0}");
                }
            }

            // Apply settings multiplier
            if (IsekaiMod.Settings != null)
            {
                effectivePoints *= IsekaiMod.Settings.RaidRankPointsMultiplier;
            }

            if (Prefs.DevMode)
            {
                Log.Message($"[Isekai Raid] Raw points: {rawPoints:F0}, Player inflation: {totalPlayerPowerInflation:F0}, " +
                           $"Factor: {inflationFactor:P0}, Effective: {effectivePoints:F0}, " +
                           $"Max player level: {maxPlayerLevel}, Wealth: {colonyWealth:F0}");
            }

            return effectivePoints;
        }

        /// <summary>
        /// Estimate how much a player pawn's level contributes to wealth inflation.
        /// Higher level pawns have better stats = better work output = more wealth.
        /// </summary>
        private static float GetPowerInflationForLevel(int level)
        {
            // Levels 1-10: minimal inflation (100-500)
            // Levels 11-25: moderate inflation (500-1500)
            // Levels 26-50: significant inflation (1500-4000)
            // Levels 51+: major inflation (4000+)

            if (level <= 10) return 100f + (level * 40f);
            if (level <= 25) return 500f + ((level - 10) * 70f);
            if (level <= 50) return 1500f + ((level - 25) * 100f);
            if (level <= 100) return 4000f + ((level - 50) * 80f);
            return 8000f + ((level - 100) * 50f);
        }

        /// <summary>
        /// Determine the base rank for a raid based on effective points.
        /// </summary>
        public static MobRankTier GetBaseRankForPoints(float effectivePoints)
        {
            foreach (var (rank, minPoints) in RankThresholds)
            {
                if (effectivePoints >= minPoints)
                    return rank;
            }
            return MobRankTier.F;
        }

        /// <summary>
        /// Apply variance to a base rank, adding randomness while staying within bounds.
        /// Uses weighted random to determine rank offset.
        /// Caps at the MaxRaidRank setting (0=F through 6=S, default 4=B).
        /// </summary>
        public static MobRankTier ApplyRankVariance(MobRankTier baseRank, bool isLeader = false, bool allowHighTier = false)
        {
            // Roll for variance offset
            int offset = RollVarianceOffset();

            // Leaders get +1 bonus rank (the strongest raider stands out)
            if (isLeader)
            {
                offset += 1;
            }

            // Calculate new rank
            int newRankValue = (int)baseRank + offset;

            // Apply caps based on MaxRaidRank setting
            // MaxRaidRank: 0=F, 1=E, 2=D, 3=C, 4=B, 5=A, 6=S
            int settingsCap = IsekaiMod.Settings?.MaxRaidRank ?? 6; // Default S
            MobRankTier maxFromSettings = (MobRankTier)settingsCap;
            
            // allowHighTier lets quests/special events bypass the cap
            int maxRank = allowHighTier ? (int)MobRankTier.SSS : (int)maxFromSettings;
            int minRank = (int)MobRankTier.F;

            newRankValue = Math.Max(minRank, Math.Min(maxRank, newRankValue));

            return (MobRankTier)newRankValue;
        }

        /// <summary>
        /// Roll a rank variance offset using weighted probabilities.
        /// Uses balanced or classic weights based on settings.
        /// </summary>
        private static int RollVarianceOffset()
        {
            // Choose variance weights based on setting
            var varianceWeights = IsekaiMod.Settings?.ClassicRNGRaids == true 
                ? ClassicVarianceWeights 
                : BalancedVarianceWeights;
            
            float totalWeight = 0f;
            foreach (var (_, weight) in varianceWeights)
                totalWeight += weight;

            float roll = (float)random.NextDouble() * totalWeight;
            float cumulative = 0f;

            foreach (var (offset, weight) in varianceWeights)
            {
                cumulative += weight;
                if (roll < cumulative)
                    return offset;
            }

            return 0; // Fallback
        }

        /// <summary>
        /// Get the highest rank among all player colonists on the map.
        /// Used by Adaptive Raid Ranks to clamp enemy ranks relative to colony strength.
        /// </summary>
        public static MobRankTier GetColonyBestRank(Map map)
        {
            MobRankTier best = MobRankTier.F;
            if (map == null) return best;
            
            foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
            {
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) continue;
                
                MobRankTier pawnRank = comp.GetRank();
                if ((int)pawnRank > (int)best)
                    best = pawnRank;
            }
            return best;
        }

        /// <summary>
        /// Assign rank-based stats to a raid pawn.
        /// Called after raid pawns are spawned.
        /// </summary>
        public static void AssignRaidPawnRank(Pawn pawn, float raidPoints, Map map, bool isLeader = false)
        {
            if (pawn == null) return;

            // Safety: never re-roll a pawn that was (or is) a player colonist.
            // Covers Anomaly obelisks, berserk, mind control turning colonists hostile.
            if (pawn.playerSettings != null) return;
            
            // Skip bounty quest pawns — their rank was explicitly forced by the hunt system.
            // Without this check, the raid rank postfix on LordMaker.MakeNewLord would
            // see the bounty pawn's LordJob_AssaultColony and overwrite the forced C-Rank
            // with a wealth-based F-Rank.
            if (Quests.IncidentWorker_IsekaiHunt.IsBountyPawn(pawn)) return;

            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;

            // Calculate effective points (adjusted for player power)
            float effectivePoints = GetEffectiveRaidPoints(raidPoints, map);

            // Determine base rank from points
            MobRankTier baseRank = GetBaseRankForPoints(effectivePoints);

            // Apply variance (capped by MaxRaidRank setting)
            bool allowHighTier = false; // Normal raids always respect the cap
            MobRankTier finalRank = ApplyRankVariance(baseRank, isLeader, allowHighTier);

            // Adaptive Raid Ranks: clamp enemy rank to within ±1 of the colony's best pawn rank
            if (IsekaiMod.Settings?.AdaptiveRaidRanks == true)
            {
                MobRankTier colonyBest = GetColonyBestRank(map);
                int colonyRankInt = (int)colonyBest;
                int adaptiveMin = Math.Max((int)MobRankTier.F, colonyRankInt - 1);
                int adaptiveMax = Math.Min((int)MobRankTier.S, colonyRankInt + 1); // Still respects S cap for raids
                
                // Also respect the MaxRaidRank setting
                int settingsCap = IsekaiMod.Settings?.MaxRaidRank ?? 6;
                adaptiveMax = Math.Min(adaptiveMax, settingsCap);
                
                int clampedRank = Math.Max(adaptiveMin, Math.Min(adaptiveMax, (int)finalRank));
                
                if (Prefs.DevMode && clampedRank != (int)finalRank)
                {
                    Log.Message($"[Isekai Raid] Adaptive clamp: {MobRankUtility.GetRankString(finalRank)} -> " +
                               $"{MobRankUtility.GetRankString((MobRankTier)clampedRank)} " +
                               $"(colony best: {MobRankUtility.GetRankString(colonyBest)}, range: ±1)");
                }
                
                finalRank = (MobRankTier)clampedRank;
            }

            // Convert rank to level
            int level = GetLevelForRank(finalRank);

            // Add some level variance within the rank
            int levelVariance = GetLevelVarianceForRank(finalRank);
            level += random.Next(-levelVariance, levelVariance + 1);
            level = Math.Max(1, level);

            // Regenerate stats for the new level
            string rankString = MobRankUtility.GetRankString(finalRank);
            PawnStatGenerator.GenerateStatsForRank(rankString, comp.stats);
            comp.currentLevel = level;
            comp.stats.availableStatPoints = 0;

            // Update the rank trait
            PawnStatGenerator.UpdateRankTraitFromStats(pawn, comp);

            if (Prefs.DevMode)
            {
                Log.Message($"[Isekai Raid] {pawn.LabelShort}: Base {MobRankUtility.GetRankString(baseRank)} -> " +
                           $"Final {rankString} (Lv{level}){(isLeader ? " [LEADER]" : "")}");
            }
        }

        /// <summary>
        /// Get the target level for a specific rank tier.
        /// </summary>
        private static int GetLevelForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.F: return 3;
                case MobRankTier.E: return 8;
                case MobRankTier.D: return 14;
                case MobRankTier.C: return 22;
                case MobRankTier.B: return 38;
                case MobRankTier.A: return 70;
                case MobRankTier.S: return 130;
                case MobRankTier.SS: return 280;
                case MobRankTier.SSS: return 450;
                default: return 1;
            }
        }

        /// <summary>
        /// Get level variance allowed within a rank tier.
        /// Higher ranks have more variance for interesting encounters.
        /// </summary>
        private static int GetLevelVarianceForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.F: return 1;
                case MobRankTier.E: return 2;
                case MobRankTier.D: return 3;
                case MobRankTier.C: return 4;
                case MobRankTier.B: return 6;
                case MobRankTier.A: return 10;
                case MobRankTier.S: return 15;
                case MobRankTier.SS: return 25;
                case MobRankTier.SSS: return 30;
                default: return 1;
            }
        }

        /// <summary>
        /// Check if a pawn is a raid leader (has certain titles, is faction leader, etc.)
        /// </summary>
        public static bool IsRaidLeader(Pawn pawn, List<Pawn> allRaidPawns)
        {
            if (pawn == null) return false;

            // Faction leader is always a raid leader
            if (pawn.Faction?.leader == pawn)
                return true;

            // If this is the most combat-capable pawn in the raid (by combat power), they're the leader
            if (allRaidPawns != null && allRaidPawns.Count > 0)
            {
                float highestPower = 0f;
                Pawn strongest = null;

                foreach (var p in allRaidPawns)
                {
                    float power = p.kindDef?.combatPower ?? 0f;
                    if (power > highestPower)
                    {
                        highestPower = power;
                        strongest = p;
                    }
                }

                if (strongest == pawn)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Process all pawns in a raid and assign appropriate ranks.
        /// </summary>
        public static void ProcessRaidPawns(List<Pawn> raidPawns, float raidPoints, Map map)
        {
            // Check if raid ranking is enabled in settings
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableRaidRanking)
                return;

            if (raidPawns == null || raidPawns.Count == 0) return;

            foreach (var pawn in raidPawns)
            {
                if (pawn == null || pawn.Dead) continue;
                if (!pawn.RaceProps.Humanlike) continue;

                // Skip pawns with established Isekai progression.
                // This prevents colonists temporarily turned hostile (Anomaly obelisks,
                // mind control, berserk effects, etc.) from having their stats re-rolled
                // as low-level raid enemies, destroying their progression.
                if (IsEstablishedPawn(pawn)) continue;

                bool isLeader = IsRaidLeader(pawn, raidPawns);
                AssignRaidPawnRank(pawn, raidPoints, map, isLeader);
            }
        }

        /// <summary>
        /// Check if a pawn already has established Isekai progression that should not be
        /// overwritten by the raid rank system. Returns true for colonists that were
        /// temporarily turned hostile (Anomaly obelisks, berserk, mind control, etc.).
        /// </summary>
        private static bool IsEstablishedPawn(Pawn pawn)
        {
            // playerSettings is only non-null for pawns that have been player-controlled.
            // Genuine raid pawns never have playerSettings.
            // This catches colonists temporarily turned hostile by Anomaly obelisks,
            // berserk, mind control, possession, etc.
            if (pawn.playerSettings != null)
                return true;

            return false;
        }
    }
}
