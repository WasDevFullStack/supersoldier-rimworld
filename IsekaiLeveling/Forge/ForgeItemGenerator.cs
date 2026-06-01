using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Generates pre-reinforced weapons/armor with optional rune sockets for loot drops.
    /// Used by elite mob kills, world boss rewards, and quest rewards.
    /// </summary>
    public static class ForgeItemGenerator
    {
        /// <summary>
        /// Apply random refinement (and optionally runes) to an already-created weapon or armor Thing.
        /// Does nothing if the forge system is disabled or the item has no CompForgeEnhancement.
        /// </summary>
        /// <param name="item">The weapon or armor Thing to enhance.</param>
        /// <param name="maxRefinement">Maximum refinement level (capped at 10).</param>
        /// <param name="runeChance">Chance (0-1) to socket each available rune slot.</param>
        /// <param name="maxRuneRank">Maximum rune rank that can appear (1-5).</param>
        /// <param name="minRefinement">Minimum refinement level floor (default 1).</param>
        public static void TryApplyRandomEnhancement(Thing item, int maxRefinement, float runeChance, int maxRuneRank, int minRefinement = 1)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;

            var comp = item.TryGetComp<CompForgeEnhancement>();
            if (comp == null) return;

            maxRefinement = Mathf.Clamp(maxRefinement, 0, 10);
            minRefinement = Mathf.Clamp(minRefinement, 1, maxRefinement);
            maxRuneRank = Mathf.Clamp(maxRuneRank, 1, 5);

            // Apply refinement level (weighted toward lower end of the min-max range)
            if (maxRefinement > 0)
            {
                int range = maxRefinement - minRefinement + 1;
                int level = range <= 1 ? minRefinement : minRefinement + GetWeightedRefinementLevel(range) - 1;
                comp.refinementLevel = level;
            }

            // Apply runes
            if (runeChance > 0f && comp.MaxRuneSlots > 0)
            {
                bool isWeapon = item.def.IsWeapon;
                RuneCategory targetCategory = isWeapon ? RuneCategory.Weapon : RuneCategory.Armor;

                var availableRunes = DefDatabase<RuneDef>.AllDefsListForReading
                    .Where(r => r.category == targetCategory)
                    .ToList();

                if (availableRunes.Count > 0)
                {
                    int slots = comp.MaxRuneSlots;
                    for (int i = 0; i < slots; i++)
                    {
                        if (!Rand.Chance(runeChance)) continue;
                        if (!comp.CanAddRune()) break;

                        RuneDef rune = availableRunes.RandomElement();
                        int rank = GetWeightedRuneRank(maxRuneRank);
                        comp.TryAddRune(rune, rank);
                    }
                }
            }
        }

        /// <summary>
        /// Weighted random refinement level. Lower levels are more likely.
        /// Weight for level N = (maxLevel - N + 1), so +1 is most common, maxLevel is rarest.
        /// </summary>
        private static int GetWeightedRefinementLevel(int maxLevel)
        {
            // Build weighted table: level 1 has weight=maxLevel, level maxLevel has weight=1
            float totalWeight = 0f;
            for (int i = 1; i <= maxLevel; i++)
                totalWeight += maxLevel - i + 1;

            float roll = Rand.Value * totalWeight;
            float cumulative = 0f;
            for (int i = 1; i <= maxLevel; i++)
            {
                cumulative += maxLevel - i + 1;
                if (roll <= cumulative)
                    return i;
            }
            return 1;
        }

        /// <summary>
        /// Weighted random rune rank. Lower ranks are more likely.
        /// </summary>
        private static int GetWeightedRuneRank(int maxRank)
        {
            float totalWeight = 0f;
            for (int i = 1; i <= maxRank; i++)
                totalWeight += maxRank - i + 1;

            float roll = Rand.Value * totalWeight;
            float cumulative = 0f;
            for (int i = 1; i <= maxRank; i++)
            {
                cumulative += maxRank - i + 1;
                if (roll <= cumulative)
                    return i;
            }
            return 1;
        }
    }
}
