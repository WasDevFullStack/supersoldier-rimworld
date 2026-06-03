using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Generates random stats for NPC pawns based on weighted rank distribution
    /// </summary>
    public static class PawnStatGenerator
    {
        // Rank probability weights (sum to 100%)
        private static readonly Dictionary<string, float> RankProbabilities = new Dictionary<string, float>
        {
            { "F", 35f },    // Very common - weak pawns
            { "E", 28.5f },  // Common - average pawns
            { "D", 18f },    // Uncommon - above average
            { "C", 10f },    // Rare - competent
            { "B", 5f },     // Very rare - skilled
            { "A", 2.5f },   // Extremely rare - exceptional
            { "S", 0.8f },   // Ultra rare - elite
            { "SS", 0.18f }, // Near impossible - legendary
            { "SSS", 0.02f } // Mythical - godlike
        };

        // Level ranges per rank — used by GenerateStatsForRank to compute correct
        // stat budgets regardless of SkillPointsPerLevel setting.
        // Must match DetermineRankFromLevel / GetMinLevelForRank / GetMaxLevelForRank.
        private static readonly Dictionary<string, (int minLevel, int maxLevel)> RankLevelRanges = new Dictionary<string, (int minLevel, int maxLevel)>
        {
            { "F",   (1, 5) },
            { "E",   (6, 10) },
            { "D",   (11, 17) },
            { "C",   (18, 25) },
            { "B",   (26, 50) },
            { "A",   (51, 100) },
            { "S",   (101, 200) },
            { "SS",  (201, 400) },
            { "SSS", (401, 9999) },
            { "Nation", (500, 9999) }
        };

        // Factions that can have SSS leaders
        private static readonly HashSet<string> StrongFactionDefNames = new HashSet<string>
        {
            "Empire",
            "OutlanderCivil",   // Outlander Union leaders
            "OutlanderRough",   // Rough Outlander leaders
            "Mechanoid"         // Mechanoid leaders (if applicable)
        };

        private static System.Random random = new System.Random();

        // Cache for character creation stats, keyed by pawn full name.
        // Used to preserve stats when EdB Prepare Carefully (or similar mods)
        // recreate pawn objects on game start, which would otherwise lose the
        // stats the player set during character creation.
        private static Dictionary<string, CachedCharacterStats> characterCreationCache = new Dictionary<string, CachedCharacterStats>();

        private struct CachedCharacterStats
        {
            public int level;
            public int xp;
            public int strength, dexterity, vitality, intelligence, wisdom, charisma;
            public int availableStatPoints;
        }

        /// <summary>
        /// Check if a pawn should have generated stats (excludes only newborns)
        /// All pawns including player colonists get pre-generated stats based on rank
        /// </summary>
        public static bool ShouldGenerateStats(Pawn pawn)
        {
            if (pawn == null) return false;
            
            // Skip newborns / very young pawns (age less than 3 years),
            // unless the player enabled "Newborns Use Rank Rolling" in settings.
            // Also skip if ageTracker is null — this means the pawn is still being
            // constructed (e.g. a baby being born) and we shouldn't roll stats yet.
            if (!IsekaiLevelingSettings.NewbornsUseRankRolling)
            {
                if (pawn.ageTracker == null) return false;
                if (pawn.ageTracker.AgeBiologicalYears < 3) return false;
            }
            
            return true;
        }

        /// <summary>
        /// Check if a pawn is a baby or child too young for Isekai stats.
        /// Used after pawn generation is complete (when ageTracker is available).
        /// </summary>
        public static bool IsNewborn(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.ageTracker == null) return true; // No age tracker = treat as newborn
            return pawn.ageTracker.AgeBiologicalYears < 3;
        }

        /// <summary>
        /// Reset a pawn to level 1 / F rank with base stats and no available points.
        /// Used for newborns that were incorrectly assigned stats during generation.
        /// </summary>
        public static void ResetToNewborn(Pawn pawn, IsekaiComponent comp)
        {
            comp.currentLevel = 1;
            comp.currentXP = 0;
            comp.stats.strength = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.dexterity = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.vitality = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.intelligence = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.wisdom = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.charisma = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.availableStatPoints = 0;
            comp.statsInitialized = true;
            AssignRankTrait(pawn, "F");
        }

        /// <summary>
        /// Roll a rank for the pawn based on weighted probability
        /// Faction leaders get boosted chances for higher ranks
        /// </summary>
        public static string RollRankForPawn(Pawn pawn)
        {
            bool isFactionLeader = IsFactionLeader(pawn);
            bool isStrongFaction = IsFromStrongFaction(pawn);
            
            // Strong faction leaders (Empire, Pirates, Tribes, etc.) get top tier ranks
            if (isFactionLeader && isStrongFaction)
            {
                // S:50%, SS:30%, SSS:10%, fallback A:10%
                float roll = (float)random.NextDouble() * 100f;
                if (roll < 50f) return "S";
                if (roll < 80f) return "SS";
                if (roll < 90f) return "SSS";
                return "A";
            }
            
            // Regular faction leaders get mid-high tier ranks
            if (isFactionLeader)
            {
                // C:60% B:30% A:9% S:1%
                float roll = (float)random.NextDouble() * 100f;
                if (roll < 60f) return "C";
                if (roll < 90f) return "B";
                if (roll < 99f) return "A";
                return "S";
            }
            
            // All other pawns use standard distribution (no faction boost)
            return RollStandardRank();
        }

        /// <summary>
        /// Roll rank using standard probability distribution.
        /// Public so creatures can reuse the same RNG distribution.
        /// </summary>
        public static string RollStandardRank()
        {
            float roll = (float)random.NextDouble() * 100f;
            float cumulative = 0f;
            
            foreach (var kvp in RankProbabilities)
            {
                cumulative += kvp.Value;
                if (roll < cumulative)
                {
                    return kvp.Key;
                }
            }
            
            return "E"; // Fallback to most common rank
        }

        /// <summary>
        /// Roll rank with a boost (higher ranks more likely)
        /// </summary>
        private static string RollRankWithBoost(int boostTiers)
        {
            string baseRank = RollStandardRank();
            
            // Boost the rank up by boostTiers levels
            string[] rankOrder = { "F", "E", "D", "C", "B", "A", "S", "SS", "SSS" };
            int currentIndex = Array.IndexOf(rankOrder, baseRank);
            int boostedIndex = Math.Min(currentIndex + boostTiers, rankOrder.Length - 1);
            
            return rankOrder[boostedIndex];
        }

        /// <summary>
        /// Generate random stats for a given rank.
        /// Picks a random target level within the rank's level range, then computes
        /// total stat points from that level using SkillPointsPerLevel. This ensures
        /// the resulting level (and therefore the display rank) always matches the
        /// originally rolled rank, regardless of the SkillPointsPerLevel setting.
        /// </summary>
        public static void GenerateStatsForRank(string rank, IsekaiStatAllocation stats)
        {
            // Get the level range for this rank
            if (!RankLevelRanges.TryGetValue(rank, out var levelRange))
            {
                levelRange = RankLevelRanges["E"]; // Default to E rank
            }
            
            // Cap to settings MaxLevel
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            int effectiveMaxLevel = Math.Min(levelRange.maxLevel, maxLevel);
            int effectiveMinLevel = Math.Min(levelRange.minLevel, effectiveMaxLevel);
            
            // Pick a random target level within the rank's range
            int targetLevel = random.Next(effectiveMinLevel, effectiveMaxLevel + 1);
            
            // Calculate total stat points from level: each level after 1 grants SkillPointsPerLevel
            int skillPointsPerLevel = IsekaiMod.Settings?.SkillPointsPerLevel ?? 1;
            int totalPointsToAllocate = (targetLevel - 1) * skillPointsPerLevel;
            
            // Get effective per-stat cap from settings
            int maxStatValue = IsekaiStatAllocation.GetEffectiveMaxStat();
            
            // Reset stats to base
            stats.strength = IsekaiStatAllocation.BASE_STAT_VALUE;
            stats.dexterity = IsekaiStatAllocation.BASE_STAT_VALUE;
            stats.vitality = IsekaiStatAllocation.BASE_STAT_VALUE;
            stats.intelligence = IsekaiStatAllocation.BASE_STAT_VALUE;
            stats.wisdom = IsekaiStatAllocation.BASE_STAT_VALUE;
            stats.charisma = IsekaiStatAllocation.BASE_STAT_VALUE;
            
            if (totalPointsToAllocate <= 0) return;
            
            // Choose distribution style randomly
            int distributionStyle = random.Next(100);
            
            if (distributionStyle < 20)
            {
                // 20% chance: Focused build (1-2 primary stats)
                DistributeFocused(stats, totalPointsToAllocate, maxStatValue);
            }
            else if (distributionStyle < 50)
            {
                // 30% chance: Semi-focused (2-3 stats emphasized)
                DistributeSemiFocused(stats, totalPointsToAllocate, maxStatValue);
            }
            else
            {
                // 50% chance: Random distribution
                DistributeRandom(stats, totalPointsToAllocate, maxStatValue);
            }
        }

        /// <summary>
        /// Focused distribution - most points in 1-2 stats
        /// </summary>
        private static void DistributeFocused(IsekaiStatAllocation stats, int totalPoints, int maxStatValue)
        {
            int[] statValues = { stats.strength, stats.dexterity, stats.vitality, 
                                 stats.intelligence, stats.wisdom, stats.charisma };
            
            // Pick 1-2 primary stats
            int primaryCount = random.Next(1, 3);
            List<int> primaryIndices = new List<int>();
            
            while (primaryIndices.Count < primaryCount)
            {
                int idx = random.Next(6);
                if (!primaryIndices.Contains(idx))
                    primaryIndices.Add(idx);
            }
            
            // Allocate 70-80% to primary stats
            int primaryPoints = (int)(totalPoints * (0.7f + (float)random.NextDouble() * 0.1f));
            int remainingPoints = totalPoints - primaryPoints;
            
            // Distribute among primary stats, tracking overflow from integer division and stat cap
            int primaryActuallyAllocated = 0;
            int pointsPerPrimary = primaryPoints / primaryIndices.Count;
            foreach (int idx in primaryIndices)
            {
                int before = statValues[idx];
                statValues[idx] = Math.Min(statValues[idx] + pointsPerPrimary, maxStatValue);
                primaryActuallyAllocated += statValues[idx] - before;
            }
            // Recover any points lost to integer division or stat cap
            remainingPoints += (primaryPoints - primaryActuallyAllocated);
            
            // Distribute remaining points randomly among all stats
            while (remainingPoints > 0)
            {
                int idx = random.Next(6);
                if (statValues[idx] < maxStatValue)
                {
                    statValues[idx]++;
                    remainingPoints--;
                }
                else
                {
                    // Find a stat that can still receive points
                    bool anyCanReceive = false;
                    for (int i = 0; i < 6; i++)
                    {
                        if (statValues[i] < maxStatValue)
                        {
                            anyCanReceive = true;
                            break;
                        }
                    }
                    if (!anyCanReceive) break;
                }
            }
            
            // Apply back to stats
            stats.strength = statValues[0];
            stats.dexterity = statValues[1];
            stats.vitality = statValues[2];
            stats.intelligence = statValues[3];
            stats.wisdom = statValues[4];
            stats.charisma = statValues[5];
        }

        /// <summary>
        /// Semi-focused distribution - 2-3 emphasized stats
        /// </summary>
        private static void DistributeSemiFocused(IsekaiStatAllocation stats, int totalPoints, int maxStatValue)
        {
            int[] statValues = { stats.strength, stats.dexterity, stats.vitality, 
                                 stats.intelligence, stats.wisdom, stats.charisma };
            
            // Pick 2-3 primary stats
            int primaryCount = random.Next(2, 4);
            List<int> primaryIndices = new List<int>();
            
            while (primaryIndices.Count < primaryCount)
            {
                int idx = random.Next(6);
                if (!primaryIndices.Contains(idx))
                    primaryIndices.Add(idx);
            }
            
            // Allocate 50-65% to primary stats
            int primaryPoints = (int)(totalPoints * (0.5f + (float)random.NextDouble() * 0.15f));
            int remainingPoints = totalPoints - primaryPoints;
            
            // Distribute among primary stats, tracking overflow from integer division and stat cap
            int primaryActuallyAllocated = 0;
            int pointsPerPrimary = primaryPoints / primaryIndices.Count;
            foreach (int idx in primaryIndices)
            {
                int before = statValues[idx];
                statValues[idx] = Math.Min(statValues[idx] + pointsPerPrimary, maxStatValue);
                primaryActuallyAllocated += statValues[idx] - before;
            }
            // Recover any points lost to integer division or stat cap
            remainingPoints += (primaryPoints - primaryActuallyAllocated);
            
            // Distribute remaining points randomly
            while (remainingPoints > 0)
            {
                int idx = random.Next(6);
                if (statValues[idx] < maxStatValue)
                {
                    statValues[idx]++;
                    remainingPoints--;
                }
                else
                {
                    bool anyCanReceive = false;
                    for (int i = 0; i < 6; i++)
                    {
                        if (statValues[i] < maxStatValue)
                        {
                            anyCanReceive = true;
                            break;
                        }
                    }
                    if (!anyCanReceive) break;
                }
            }
            
            stats.strength = statValues[0];
            stats.dexterity = statValues[1];
            stats.vitality = statValues[2];
            stats.intelligence = statValues[3];
            stats.wisdom = statValues[4];
            stats.charisma = statValues[5];
        }

        /// <summary>
        /// Random distribution - points allocated randomly across all stats
        /// </summary>
        private static void DistributeRandom(IsekaiStatAllocation stats, int totalPoints, int maxStatValue)
        {
            int[] statValues = { stats.strength, stats.dexterity, stats.vitality, 
                                 stats.intelligence, stats.wisdom, stats.charisma };
            
            int remaining = totalPoints;
            while (remaining > 0)
            {
                int idx = random.Next(6);
                if (statValues[idx] < maxStatValue)
                {
                    statValues[idx]++;
                    remaining--;
                }
                else
                {
                    bool anyCanReceive = false;
                    for (int i = 0; i < 6; i++)
                    {
                        if (statValues[i] < maxStatValue)
                        {
                            anyCanReceive = true;
                            break;
                        }
                    }
                    if (!anyCanReceive) break;
                }
            }
            
            stats.strength = statValues[0];
            stats.dexterity = statValues[1];
            stats.vitality = statValues[2];
            stats.intelligence = statValues[3];
            stats.wisdom = statValues[4];
            stats.charisma = statValues[5];
        }

        /// <summary>
        /// Calculate appropriate level based on allocated stat points.
        /// Uses SkillPointsPerLevel from settings and caps to MaxLevel.
        /// </summary>
        public static int CalculateLevelFromStats(IsekaiStatAllocation stats)
        {
            int totalAllocated = stats.TotalAllocatedPoints;
            
            // Use settings SkillPointsPerLevel (not hardcoded constant)
            int skillPointsPerLevel = IsekaiMod.Settings?.SkillPointsPerLevel ?? 1;
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            
            // Level = 1 + (total allocated points / points per level)
            int level = 1 + (totalAllocated / Math.Max(1, skillPointsPerLevel));
            
            // Cap to MaxLevel
            return Math.Max(1, Math.Min(level, maxLevel));
        }

        /// <summary>
        /// Enforce config caps on a component's stats and level.
        /// Caps each stat to MaxStatCap, level to MaxLevel.
        /// Safety net called after all stat generation paths.
        /// </summary>
        private static void EnforceConfigCaps(IsekaiComponent comp)
        {
            int maxStatCap = IsekaiStatAllocation.GetEffectiveMaxStat();
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            
            // Cap individual stats
            comp.stats.strength = Math.Min(comp.stats.strength, maxStatCap);
            comp.stats.dexterity = Math.Min(comp.stats.dexterity, maxStatCap);
            comp.stats.vitality = Math.Min(comp.stats.vitality, maxStatCap);
            comp.stats.intelligence = Math.Min(comp.stats.intelligence, maxStatCap);
            comp.stats.wisdom = Math.Min(comp.stats.wisdom, maxStatCap);
            comp.stats.charisma = Math.Min(comp.stats.charisma, maxStatCap);
            
            // Cap level
            comp.currentLevel = Math.Min(comp.currentLevel, maxLevel);
        }

        /// <summary>
        /// Check if pawn is a faction leader
        /// </summary>
        private static bool IsFactionLeader(Pawn pawn)
        {
            if (pawn?.Faction == null) return false;
            
            return pawn.Faction.leader == pawn;
        }

        /// <summary>
        /// Check if pawn is from a strong faction
        /// </summary>
        private static bool IsFromStrongFaction(Pawn pawn)
        {
            if (pawn?.Faction?.def == null) return false;
            
            return StrongFactionDefNames.Contains(pawn.Faction.def.defName);
        }

        /// <summary>
        /// Initialize a pawn with generated stats based on rank probability.
        /// Returns the power rank for use by magic generator.
        /// Player colonists can use the StartingPawnLevel setting.
        /// </summary>
        public static string InitializePawnStats(Pawn pawn, IsekaiComponent comp)
        {
            if (!ShouldGenerateStats(pawn)) return "F";
            
            // Check if this is a player colonist and we have a custom starting level
            bool useStartingLevel = ShouldUseStartingLevel(pawn);
            int startingLevel = IsekaiMod.Settings?.StartingPawnLevel ?? 0;

            string rank;

            if (useStartingLevel && startingLevel > 0)
            {
                // Use custom starting level for player colonists
                InitializeWithStartingLevel(comp, startingLevel);
                rank = DetermineRankFromLevel(startingLevel);
            }
            else if (useStartingLevel && startingLevel == 0 && IsMidSaveAddition())
            {
                // Mid-save mod addition: scale colonist level to colony wealth
                // so they don't all start at F-rank against A/S-rank raid enemies
                int wealthLevel = CalculateWealthBasedLevel();
                InitializeWithStartingLevel(comp, wealthLevel);
                rank = DetermineRankFromLevel(wealthLevel);
            }
            else
            {
                // Roll for rank normally
                rank = RollRankForPawn(pawn);

                // Generate stats for that rank (respects MaxLevel, MaxStatCap, SkillPointsPerLevel)
                GenerateStatsForRank(rank, comp.stats);

                // Calculate and set level (respects settings)
                comp.currentLevel = CalculateLevelFromStats(comp.stats);
                
                // Re-derive rank from actual capped level so trait matches power
                rank = DetermineRankFromLevel(comp.currentLevel);
            }
            
            // Safety net: enforce config caps on all paths
            EnforceConfigCaps(comp);
            
            // Clear any available stat points (they were pre-allocated)
            comp.stats.availableStatPoints = 0;
            
            // Assign the rank trait to the pawn (uses rank derived from capped level)
            AssignRankTrait(pawn, rank);
            
            // Roll random Isekai traits (0-2 traits based on weighted probability)
            IsekaiTraitHelper.RollRandomTraits(pawn);

            // Mark traits as rolled so retroactive logic doesn't re-roll
            comp.traitsRolled = true;
            
            return rank;
        }

        /// <summary>
        /// Determine if a pawn should use the Starting Level setting
        /// Returns true for player faction colonists
        /// </summary>
        private static bool ShouldUseStartingLevel(Pawn pawn)
        {
            if (pawn == null) return false;
            
            // Check if pawn belongs to player faction.
            // Faction.OfPlayer throws during world generation (factions not created yet),
            // so use Find.FactionManager?.OfPlayer which returns null safely.
            if (pawn.Faction != null)
            {
                var playerFaction = Find.FactionManager?.OfPlayer;
                if (playerFaction != null && pawn.Faction == playerFaction)
                    return true;
            }
            
            // Also check if pawn is being generated FOR the player faction (scenario starting pawns)
            // During generation, Faction might not be set yet, so check if they're in the world map
            if (!pawn.Spawned && pawn.Faction == null)
            {
                // This might be a starting pawn during scenario setup
                // Check if we're in game startup
                if (Current.Game != null && !Current.Game.Maps.Any())
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Detect if the mod is being added to an existing save (mid-save addition).
        /// True ONLY when the mod was just installed into a running save:
        /// game is active with maps, but NO existing colonist has an initialized IsekaiComponent.
        /// Once any colonist has been initialized, this returns false — subsequent new pawns
        /// (recruits, resurrections, xenotype abilities) use normal rank rolling instead
        /// of wealth-based catch-up scaling.
        /// </summary>
        private static bool IsMidSaveAddition()
        {
            try
            {
                if (Current.Game == null) return false;
                if (!Current.Game.Maps.Any()) return false;
                // Must have been running for a bit (not brand new game)
                if (Current.Game.tickManager.TicksGame <= 1000) return false;
                
                // Check if ANY existing colonist already has an initialized IsekaiComponent.
                // If so, the mod has been running — this is a normal new pawn, not a first-time mod addition.
                foreach (var map in Current.Game.Maps)
                {
                    if (map?.mapPawns?.FreeColonists == null) continue;
                    foreach (var colonist in map.mapPawns.FreeColonists)
                    {
                        var existingComp = colonist.GetComp<IsekaiComponent>();
                        if (existingComp != null && existingComp.statsInitialized)
                        {
                            // Another colonist already has the mod running — not a mid-save addition
                            return false;
                        }
                    }
                }
                
                // No colonists have initialized IsekaiComponents — mod was just added
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Calculate an appropriate starting level based on colony wealth.
        /// Wealth thresholds are aligned with raid rank scaling so colonists
        /// start at a similar power level to the enemies they'll face.
        /// </summary>
        private static int CalculateWealthBasedLevel()
        {
            try
            {
                Map map = Find.Maps?.FirstOrDefault(m => m.IsPlayerHome);
                if (map == null) return 1;

                float wealth = map.wealthWatcher?.WealthTotal ?? 0f;

                // Thresholds aligned with RaidRankSystem point scaling
                if (wealth >= 1000000f) return random.Next(76, 101);   // S-rank territory
                if (wealth >= 500000f)  return random.Next(51, 76);    // A-rank
                if (wealth >= 250000f)  return random.Next(26, 51);    // B-rank
                if (wealth >= 100000f)  return random.Next(18, 26);    // C-rank
                if (wealth >= 40000f)   return random.Next(11, 18);    // D-rank
                if (wealth >= 15000f)   return random.Next(6, 11);     // E-rank
                return random.Next(1, 6);                               // F-rank
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// Initialize a pawn with a specific starting level, distributing stat points accordingly
        /// </summary>
        private static void InitializeWithStartingLevel(IsekaiComponent comp, int level)
        {
            // Reset to base stats
            comp.stats.strength = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.dexterity = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.vitality = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.intelligence = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.wisdom = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.charisma = IsekaiStatAllocation.BASE_STAT_VALUE;
            
            // Calculate total stat points from level
            // Each level after 1 grants SkillPointsPerLevel stat points
            int skillPointsPerLevel = IsekaiMod.Settings?.SkillPointsPerLevel ?? 1;
            int totalPoints = (level - 1) * skillPointsPerLevel;
            
            // Distribute points randomly (respects MaxStatCap)
            if (totalPoints > 0)
            {
                int maxStatValue = IsekaiStatAllocation.GetEffectiveMaxStat();
                DistributeRandom(comp.stats, totalPoints, maxStatValue);
            }
            
            // Set the level, capped to MaxLevel
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            comp.currentLevel = Math.Min(level, maxLevel);
            comp.currentXP = 0;
        }
        
        /// <summary>
        /// Sync a pawn's component level and stats to match their rank trait.
        /// Called when a pawn already has a rank trait (e.g. from Character Editor
        /// or Prepare Carefully) but their component level doesn't match.
        /// Works bidirectionally: scales UP if pawn is below the rank, scales DOWN
        /// if pawn is above the rank (e.g. player chose F-Rank but pawn rolled B-Rank).
        /// </summary>
        public static void SyncToRankTrait(Pawn pawn, IsekaiComponent comp, string rank)
        {
            if (pawn == null || comp == null || string.IsNullOrEmpty(rank)) return;
            
            int minLevelForRank = GetMinLevelForRank(rank);
            int maxLevelForRank = GetMaxLevelForRank(rank);
            
            // Check if the current level already falls within this rank's range
            string currentRank = DetermineRankFromLevel(comp.currentLevel);
            if (currentRank == rank) return;
            
            // Generate stats appropriate for this rank
            GenerateStatsForRank(rank, comp.stats);
            
            // Calculate level from the generated stats
            int calculatedLevel = CalculateLevelFromStats(comp.stats);
            
            // Clamp level to the rank's valid range and MaxLevel
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            comp.currentLevel = Math.Max(minLevelForRank, Math.Min(calculatedLevel, Math.Min(maxLevelForRank, maxLevel)));
            comp.currentXP = 0;
            comp.stats.availableStatPoints = 0;
            
            // Enforce stat caps
            EnforceConfigCaps(comp);
            
            if (Prefs.DevMode)
            {
                Log.Message($"[Isekai Leveling] Synced {pawn.LabelShort} to rank {rank} (level {comp.currentLevel})");
            }
        }
        
        /// <summary>
        /// Get the minimum level threshold for a given rank
        /// </summary>
        public static int GetMinLevelForRank(string rank)
        {
            switch (rank)
            {
                case "Nation": return 500;
                case "SSS": return 401;
                case "SS":  return 201;
                case "S":   return 101;
                case "A":   return 51;
                case "B":   return 26;
                case "C":   return 18;
                case "D":   return 11;
                case "E":   return 6;
                default:    return 1;
            }
        }

        /// <summary>
        /// Get the maximum level for a given rank (just below the next rank's threshold)
        /// </summary>
        private static int GetMaxLevelForRank(string rank)
        {
            switch (rank)
            {
                case "F":   return 5;
                case "E":   return 10;
                case "D":   return 17;
                case "C":   return 25;
                case "B":   return 50;
                case "A":   return 100;
                case "S":   return 200;
                case "SS":  return 400;
                default:    return 9999; // SSS has no upper cap
            }
        }

        /// <summary>
        /// Determine rank based on level (approximation for starting level pawns)
        /// </summary>
        private static string DetermineRankFromLevel(int level)
        {
            return GetRankFromLevel(level);
        }

        /// <summary>
        /// Assign the appropriate rank trait to a pawn based on their power rank
        /// </summary>
        public static void AssignRankTrait(Pawn pawn, string rank)
        {
            if (pawn?.story?.traits == null) return;
            
            // Get the trait def name for this rank
            string traitDefName = $"Isekai_Rank_{rank}";
            TraitDef rankTraitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitDefName);
            
            if (rankTraitDef == null)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[Isekai Leveling] Could not find rank trait: {traitDefName}");
                }
                return;
            }
            
            // Remove any existing rank traits first
            RemoveExistingRankTraits(pawn);
            
            // Add the new rank trait
            Trait rankTrait = new Trait(rankTraitDef, 0, true);
            pawn.story.traits.GainTrait(rankTrait);
        }

        /// <summary>
        /// Remove any existing Isekai rank traits from a pawn
        /// </summary>
        private static void RemoveExistingRankTraits(Pawn pawn)
        {
            if (pawn?.story?.traits == null) return;
            
            string[] rankSuffixes = { "F", "E", "D", "C", "B", "A", "S", "SS", "SSS" };
            List<Trait> traitsToRemove = new List<Trait>();
            
            foreach (var trait in pawn.story.traits.allTraits)
            {
                foreach (var suffix in rankSuffixes)
                {
                    if (trait.def.defName == $"Isekai_Rank_{suffix}")
                    {
                        traitsToRemove.Add(trait);
                        break;
                    }
                }
            }
            
            foreach (var trait in traitsToRemove)
            {
                pawn.story.traits.RemoveTrait(trait);
            }
        }

        /// <summary>
        /// Update a pawn's rank trait when their level changes
        /// Call this when stats are manually adjusted or after leveling
        /// </summary>
        public static void UpdateRankTraitFromStats(Pawn pawn, IsekaiComponent comp)
        {
            if (pawn?.story?.traits == null || comp == null) return;
            
            // Determine rank from LEVEL (not stats) - consistent with UI display
            string newRank = GetRankFromLevel(comp.currentLevel);
            
            // Assign the new rank trait
            AssignRankTrait(pawn, newRank);
        }

        /// <summary>
        /// Get power rank from level (consistent with UI display)
        /// </summary>
        private static string GetRankFromLevel(int level)
        {
            if (level >= 401) return "SSS";
            if (level >= 201) return "SS";
            if (level >= 101) return "S";
            if (level >= 51) return "A";
            if (level >= 26) return "B";
            if (level >= 18) return "C";
            if (level >= 11) return "D";
            if (level >= 6) return "E";
            return "F";
        }

        /// <summary>
        /// Get a readable description of the rank
        /// </summary>
        public static string GetRankDescription(string rank)
        {
            switch (rank)
            {
                case "F": return "Novice - barely trained in combat";
                case "E": return "Apprentice - has basic training";
                case "D": return "Journeyman - competent but unremarkable";
                case "C": return "Adventurer - skilled and capable";
                case "B": return "Veteran - experienced and dangerous";
                case "A": return "Elite - among the best fighters";
                case "S": return "Hero - legendary prowess";
                case "SS": return "Demigod - transcendent power";
                case "SSS": return "Godlike - absolute apex being";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Cache a pawn's Isekai stats during character creation.
        /// Called each frame from the character creation panel to capture the latest state.
        /// </summary>
        public static void CacheCharacterCreationStats(Pawn pawn, IsekaiComponent comp)
        {
            if (pawn?.Name == null || comp?.stats == null) return;

            string key = pawn.Name.ToStringFull;
            if (string.IsNullOrEmpty(key)) return;

            characterCreationCache[key] = new CachedCharacterStats
            {
                level = comp.currentLevel,
                xp = comp.currentXP,
                strength = comp.stats.strength,
                dexterity = comp.stats.dexterity,
                vitality = comp.stats.vitality,
                intelligence = comp.stats.intelligence,
                wisdom = comp.stats.wisdom,
                charisma = comp.stats.charisma,
                availableStatPoints = comp.stats.availableStatPoints
            };
        }

        /// <summary>
        /// Try to restore character creation stats for a pawn.
        /// Returns true if cached stats were found and restored.
        /// </summary>
        public static bool TryRestoreCharacterCreationStats(Pawn pawn, IsekaiComponent comp)
        {
            if (pawn?.Name == null || comp?.stats == null) return false;

            string key = pawn.Name.ToStringFull;
            if (string.IsNullOrEmpty(key)) return false;

            if (!characterCreationCache.TryGetValue(key, out var cached)) return false;

            // Restore all stats from cache
            comp.currentLevel = cached.level;
            comp.currentXP = cached.xp;
            comp.stats.strength = cached.strength;
            comp.stats.dexterity = cached.dexterity;
            comp.stats.vitality = cached.vitality;
            comp.stats.intelligence = cached.intelligence;
            comp.stats.wisdom = cached.wisdom;
            comp.stats.charisma = cached.charisma;
            comp.stats.availableStatPoints = cached.availableStatPoints;
            comp.statsInitialized = true;

            // Update rank trait to match restored stats
            UpdateRankTraitFromStats(pawn, comp);

            // Remove from cache (one-time restore)
            characterCreationCache.Remove(key);

            return true;
        }

        /// <summary>
        /// Clear the character creation cache.
        /// Called from Game.FinalizeInit to prevent stale entries.
        /// </summary>
        public static void ClearCharacterCreationCache()
        {
            characterCreationCache.Clear();
        }
    }
}
