using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Static utility methods for forge system calculations:
    /// refinement costs, success/failure rates, and stat bonus formulas.
    /// </summary>
    public static class ForgeUtility
    {
        // ── Refinement cost item defNames ──
        private static ThingDef _smallManaCore;
        private static ThingDef _manaCore;
        private static ThingDef _bigManaCore;
        private static ThingDef _hugeManaCore;
        private static ThingDef _reinforcementCore;
        private static ThingDef _steel;
        private static ThingDef _component;
        private static bool _defsResolved;

        /// <summary>
        /// Lazily resolve ThingDefs. Called once on first use.
        /// </summary>
        private static void ResolveDefsIfNeeded()
        {
            if (_defsResolved) return;
            _smallManaCore = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_SmallManaCore");
            _manaCore = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ManaCore");
            _bigManaCore = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_BigManaCore");
            _hugeManaCore = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_HugeManaCore");
            _reinforcementCore = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ReinforcementCore");
            _steel = ThingDefOf.Steel;
            _component = ThingDefOf.ComponentIndustrial;
            _defsResolved = true;
        }

        // ══════════════════════════════════════════
        //  REFINEMENT SUCCESS / FAILURE RATES
        // ══════════════════════════════════════════

        /// <summary>
        /// Base success chance to reach the given target level.
        /// </summary>
        public static float GetBaseSuccessChance(int targetLevel)
        {
            switch (targetLevel)
            {
                case 1:  return 1.00f;
                case 2:  return 1.00f;
                case 3:  return 0.95f;
                case 4:  return 0.90f;
                case 5:  return 0.80f;
                case 6:  return 0.65f;
                case 7:  return 0.55f;
                case 8:  return 0.45f;
                case 9:  return 0.35f;
                case 10: return 0.25f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Adjusted success chance factoring in crafter skill.
        /// +1% per Crafting skill level above 10 (max +10% at Crafting 20).
        /// </summary>
        public static float GetSuccessChance(int targetLevel, Pawn crafter)
        {
            float baseChance = GetBaseSuccessChance(targetLevel);
            float multiplier = IsekaiLevelingSettings.RefinementSuccessMultiplier;

            if (crafter != null)
            {
                int crafting = crafter.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                int bonusLevels = Mathf.Max(0, crafting - 10);
                baseChance += bonusLevels * 0.01f;
            }

            return Mathf.Clamp01(baseChance * multiplier);
        }

        /// <summary>
        /// Chance of equipment being destroyed on failure (only applies above +5).
        /// </summary>
        public static float GetDestroyChance(int targetLevel)
        {
            switch (targetLevel)
            {
                case 6:  return 0.10f;
                case 7:  return 0.20f;
                case 8:  return 0.30f;
                case 9:  return 0.35f;
                case 10: return 0.45f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Chance of downgrade on failure (remainder after success and destroy).
        /// </summary>
        public static float GetDowngradeChance(int targetLevel)
        {
            float success = GetBaseSuccessChance(targetLevel);
            float destroy = GetDestroyChance(targetLevel);
            return Mathf.Max(0f, 1f - success - destroy);
        }

        // ══════════════════════════════════════════
        //  REFINEMENT COST QUERIES
        // ══════════════════════════════════════════

        /// <summary>
        /// Cost structure for a single refinement attempt.
        /// </summary>
        public struct RefineCost
        {
            public ThingDef coreDef;
            public int coreCount;
            public ThingDef secondaryCoreDef;
            public int secondaryCoreCount;
            public int steel;
            public int components;

            public bool RequiresReinforcementCore => coreDef == _reinforcementCore || secondaryCoreDef == _reinforcementCore;
        }

        /// <summary>
        /// Get material cost to refine from (targetLevel-1) to targetLevel.
        /// </summary>
        public static RefineCost GetRefineCost(int targetLevel)
        {
            ResolveDefsIfNeeded();

            switch (targetLevel)
            {
                case 1: return new RefineCost { coreDef = _smallManaCore, coreCount = 2, steel = 50 };
                case 2: return new RefineCost { coreDef = _smallManaCore, coreCount = 3, steel = 75, components = 1 };
                case 3: return new RefineCost { coreDef = _manaCore, coreCount = 1, steel = 100, components = 2 };
                case 4: return new RefineCost { coreDef = _manaCore, coreCount = 2, steel = 150, components = 3 };
                case 5: return new RefineCost { coreDef = _bigManaCore, coreCount = 1, steel = 200, components = 5 };
                case 6: return new RefineCost
                {
                    coreDef = _reinforcementCore, coreCount = 1,
                    secondaryCoreDef = _bigManaCore, secondaryCoreCount = 1,
                    steel = 250
                };
                case 7: return new RefineCost
                {
                    coreDef = _reinforcementCore, coreCount = 1,
                    secondaryCoreDef = _bigManaCore, secondaryCoreCount = 2,
                    steel = 300
                };
                case 8: return new RefineCost
                {
                    coreDef = _reinforcementCore, coreCount = 2,
                    secondaryCoreDef = _hugeManaCore, secondaryCoreCount = 1,
                    steel = 400
                };
                case 9: return new RefineCost
                {
                    coreDef = _reinforcementCore, coreCount = 2,
                    secondaryCoreDef = _hugeManaCore, secondaryCoreCount = 2,
                    steel = 500
                };
                case 10: return new RefineCost
                {
                    coreDef = _reinforcementCore, coreCount = 3,
                    secondaryCoreDef = _hugeManaCore, secondaryCoreCount = 3,
                    steel = 750
                };
                default: return default;
            }
        }

        /// <summary>
        /// Check if the colony (map) has enough materials for a refinement.
        /// </summary>
        public static bool HasMaterials(Map map, RefineCost cost)
        {
            if (map == null) return false;

            if (cost.coreDef != null && cost.coreCount > 0)
            {
                if (CountOnMap(map, cost.coreDef) < cost.coreCount) return false;
            }
            if (cost.secondaryCoreDef != null && cost.secondaryCoreCount > 0)
            {
                if (CountOnMap(map, cost.secondaryCoreDef) < cost.secondaryCoreCount) return false;
            }
            if (cost.steel > 0 && CountOnMap(map, _steel) < cost.steel) return false;
            if (cost.components > 0 && CountOnMap(map, _component) < cost.components) return false;

            return true;
        }

        /// <summary>
        /// Consume materials from the map for a refinement attempt.
        /// </summary>
        public static void ConsumeMaterials(Map map, RefineCost cost)
        {
            if (map == null) return;

            if (cost.coreDef != null && cost.coreCount > 0)
                ConsumeFromMap(map, cost.coreDef, cost.coreCount);
            if (cost.secondaryCoreDef != null && cost.secondaryCoreCount > 0)
                ConsumeFromMap(map, cost.secondaryCoreDef, cost.secondaryCoreCount);
            if (cost.steel > 0)
                ConsumeFromMap(map, _steel, cost.steel);
            if (cost.components > 0)
                ConsumeFromMap(map, _component, cost.components);
        }

        internal static int CountOnMap(Map map, ThingDef def)
        {
            if (def == null || map == null) return 0;
            int total = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                // Only count items stored in a stockpile / storage zone
                if (!thing.IsInValidStorage()) continue;
                total += thing.stackCount;
            }
            return total;
        }

        private static void ConsumeFromMap(Map map, ThingDef def, int count)
        {
            if (def == null || count <= 0) return;

            int remaining = count;
            // Snapshot to avoid collection-modified exception when destroying items
            var things = map.listerThings.ThingsOfDef(def).ToList();
            foreach (Thing thing in things)
            {
                if (remaining <= 0) break;
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                if (!thing.IsInValidStorage()) continue;

                int take = Mathf.Min(thing.stackCount, remaining);
                thing.SplitOff(take).Destroy();
                remaining -= take;
            }
        }

        // ══════════════════════════════════════════
        //  REFINEMENT STAT BONUSES
        // ══════════════════════════════════════════

        /// <summary>
        /// Melee damage multiplier bonus for a refinement level.
        /// +8% per level → +80% at +10.
        /// </summary>
        public static float GetMeleeDamageBonus(int level)
        {
            return level * 0.08f;
        }

        /// <summary>
        /// Melee attack speed bonus (cooldown reduction).
        /// +3% per level → +30% at +10.
        /// </summary>
        public static float GetMeleeSpeedBonus(int level)
        {
            return level * 0.03f;
        }

        /// <summary>
        /// Weapon mass reduction (melee weapons get lighter with refinement).
        /// -3% per level → -30% at +10.
        /// </summary>
        public static float GetWeaponMassReduction(int level)
        {
            return level * 0.03f;
        }

        /// <summary>
        /// Ranged weapon damage bonus.
        /// +5% per level → +50% at +10.
        /// </summary>
        public static float GetRangedDamageBonus(int level)
        {
            return level * 0.05f;
        }

        /// <summary>
        /// Ranged cooldown reduction factor for a refinement level.
        /// -5% per level → -50% at +10. Applied as multiplier reduction.
        /// </summary>
        public static float GetRangedCooldownReduction(int level)
        {
            return level * 0.05f;
        }

        /// <summary>
        /// Ranged accuracy bonus.
        /// +3% per level → +30% at +10.
        /// </summary>
        public static float GetRangedAccuracyBonus(int level)
        {
            return level * 0.03f;
        }

        /// <summary>
        /// Armor rating bonus for a refinement level.
        /// +6% per level → +60% at +10.
        /// </summary>
        public static float GetArmorBonus(int level)
        {
            return level * 0.06f;
        }

        /// <summary>
        /// Armor move speed bonus.
        /// +2.5% per level → +25% at +10.
        /// </summary>
        public static float GetArmorMoveSpeedBonus(int level)
        {
            return level * 0.025f;
        }

        /// <summary>
        /// Armor mass reduction.
        /// -3% per level → -30% at +10.
        /// </summary>
        public static float GetArmorMassReduction(int level)
        {
            return level * 0.03f;
        }

        /// <summary>
        /// Armor/apparel MaxHitPoints bonus from refinement.
        /// +10% per level → +100% at +10.
        /// Compensates for higher DPS from the mod causing gear to break too quickly.
        /// </summary>
        public static float GetArmorHPBonus(int level)
        {
            return level * 0.10f;
        }

        // ══════════════════════════════════════════
        //  REFINEMENT EXECUTION
        // ══════════════════════════════════════════

        /// <summary>
        /// Result of a refinement attempt.
        /// </summary>
        public enum RefineResult
        {
            Success,
            Downgrade,
            Destroyed
        }

        /// <summary>
        /// Attempt to refine an item. Consumes materials and rolls for success/failure.
        /// Returns the result. The caller is responsible for applying the outcome.
        /// </summary>
        public static RefineResult AttemptRefinement(Thing item, Map map, Pawn crafter)
        {
            var comp = item?.TryGetComp<CompForgeEnhancement>();
            if (comp == null || !comp.CanRefine()) return RefineResult.Downgrade;

            int targetLevel = comp.refinementLevel + 1;
            RefineCost cost = GetRefineCost(targetLevel);

            // God mode: skip material check and consumption
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            if (!godMode)
            {
                if (!HasMaterials(map, cost)) return RefineResult.Downgrade;
                ConsumeMaterials(map, cost);
            }

            // Roll for success
            float successChance = GetSuccessChance(targetLevel, crafter);
            float roll = Rand.Value;

            if (roll < successChance)
            {
                comp.refinementLevel = targetLevel;
                return RefineResult.Success;
            }

            // Failed — check for destruction (only above +5)
            float destroyChance = GetDestroyChance(targetLevel);
            if (destroyChance > 0f && Rand.Chance(destroyChance / (1f - successChance)))
            {
                return RefineResult.Destroyed;
            }

            // Downgrade by 1 level
            if (comp.refinementLevel > 0)
                comp.refinementLevel--;

            return RefineResult.Downgrade;
        }

        // ══════════════════════════════════════════
        //  REPAIR
        // ══════════════════════════════════════════

        private static ThingDef _manaEssence;

        private static ThingDef ManaEssenceDef
        {
            get
            {
                if (_manaEssence == null)
                    _manaEssence = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ManaEssence");
                return _manaEssence;
            }
        }

        /// <summary>
        /// Calculates how much Mana Essence is needed to fully repair an item.
        /// 1 essence per 20% HP missing, minimum 1 if damaged at all.
        /// </summary>
        public static int GetRepairEssenceCost(Thing item)
        {
            if (item == null || item.HitPoints >= item.MaxHitPoints) return 0;
            float missingFraction = 1f - (float)item.HitPoints / item.MaxHitPoints;
            return Mathf.Max(1, Mathf.CeilToInt(missingFraction * 5f));
        }

        /// <summary>
        /// Returns true if the item is damaged and can be repaired.
        /// </summary>
        public static bool NeedsRepair(Thing item)
        {
            return item != null && !item.Destroyed && item.HitPoints < item.MaxHitPoints;
        }

        /// <summary>
        /// Repairs the item to full HP, consuming Mana Essence from the map.
        /// Returns true if repair succeeded.
        /// </summary>
        public static bool RepairItem(Thing item, Map map)
        {
            if (!NeedsRepair(item)) return false;
            if (ManaEssenceDef == null) return false;

            int cost = GetRepairEssenceCost(item);
            bool godMode = Prefs.DevMode && DebugSettings.godMode;

            if (!godMode)
            {
                if (CountOnMap(map, ManaEssenceDef) < cost) return false;
                ConsumeFromMap(map, ManaEssenceDef, cost);
            }

            item.HitPoints = item.MaxHitPoints;
            return true;
        }
    }
}
