using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using IsekaiLeveling.Forge;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Applies forge enhancements (refinement + runes) across the whole game:
    /// 1. Enemy/NPC pawns get enhanced gear on spawn based on their rank
    /// 2. Trader stock has a chance for enhanced weapons/armor
    /// 3. Generic quest/loot rewards can spawn enhanced
    /// </summary>

    // ══════════════════════════════════════════
    //  1. PAWN EQUIPMENT ENHANCEMENT ON SPAWN
    // ══════════════════════════════════════════

    /// <summary>
    /// After PawnGenerator.GeneratePawn completes, enhance the pawn's equipped
    /// weapon and apparel based on their Isekai rank. Applies to all humanlike
    /// pawns (enemies, allies, visitors) — not just player pawns.
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    public static class Patch_EnhancePawnEquipmentOnGeneration
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)] // Run after Patch_AssignRankTraitAfterGeneration
        public static void Postfix(Pawn __result)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (__result == null) return;
            if (!__result.RaceProps.Humanlike) return;

            // Determine the pawn's rank from their Isekai component
            string rank = GetPawnRank(__result);
            if (rank == null)
            {
                if (Prefs.DevMode)
                    Log.Message($"[Isekai Forge] Skipping {__result.LabelCap}: no rank found (comp={__result.GetComp<IsekaiComponent>() != null}, level={__result.GetComp<IsekaiComponent>()?.currentLevel ?? -1})");
                return;
            }

            // Get enhancement parameters based on rank
            if (!GetEnhancementParams(rank, out int maxRef, out float runeChance, out int maxRuneRank, out float equipChance))
            {
                if (Prefs.DevMode)
                    Log.Message($"[Isekai Forge] Skipping {__result.LabelCap}: rank {rank} has no enhancement params (F/E rank)");
                return;
            }

            // Roll once to see if this pawn gets enhanced gear at all
            if (!Rand.Chance(equipChance))
            {
                if (Prefs.DevMode)
                    Log.Message($"[Isekai Forge] Skipping {__result.LabelCap}: failed equipChance roll ({equipChance:P0}) for rank {rank}");
                return;
            }

            if (Prefs.DevMode)
                Log.Message($"[Isekai Forge] Enhancing {__result.LabelCap} (rank {rank}): maxRef={maxRef}, runeChance={runeChance:P0}, maxRuneRank={maxRuneRank}");

            // Enhance primary weapon
            if (__result.equipment?.Primary != null)
            {
                ForgeItemGenerator.TryApplyRandomEnhancement(__result.equipment.Primary, maxRef, runeChance, maxRuneRank);
            }

            // Enhance worn apparel
            if (__result.apparel?.WornApparel != null)
            {
                foreach (var apparel in __result.apparel.WornApparel)
                {
                    // Each piece rolls independently for enhancement
                    if (Rand.Chance(equipChance))
                    {
                        ForgeItemGenerator.TryApplyRandomEnhancement(apparel, maxRef, runeChance, maxRuneRank);
                    }
                }
            }
        }

        private static string GetPawnRank(Pawn pawn)
        {
            // Try IsekaiComponent first
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp != null && comp.currentLevel > 0)
            {
                return GetRankFromLevel(comp.currentLevel);
            }

            // Fallback: check rank traits directly
            if (pawn.story?.traits?.allTraits != null)
            {
                string[] ranks = { "SSS", "SS", "S", "A", "B", "C", "D", "E", "F" };
                foreach (var trait in pawn.story.traits.allTraits)
                {
                    if (trait?.def?.defName == null) continue;
                    foreach (var r in ranks)
                    {
                        if (trait.def.defName == $"Isekai_Rank_{r}")
                            return r;
                    }
                }
            }
            return null;
        }

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
        /// Get enhancement parameters. Lower ranks have low chance and weak enhancements.
        /// </summary>
        private static bool GetEnhancementParams(string rank, out int maxRef, out float runeChance, out int maxRuneRank, out float equipChance)
        {
            switch (rank)
            {
                case "F":   maxRef = 1; runeChance = 0.05f; maxRuneRank = 1; equipChance = 0.10f; return true;
                case "E":   maxRef = 1; runeChance = 0.10f; maxRuneRank = 1; equipChance = 0.20f; return true;
                case "D":   maxRef = 2; runeChance = 0.20f; maxRuneRank = 1; equipChance = 0.40f; return true;
                case "C":   maxRef = 3; runeChance = 0.35f; maxRuneRank = 1; equipChance = 0.55f; return true;
                case "B":   maxRef = 4; runeChance = 0.50f; maxRuneRank = 2; equipChance = 0.70f; return true;
                case "A":   maxRef = 6; runeChance = 0.65f; maxRuneRank = 3; equipChance = 0.85f; return true;
                case "S":   maxRef = 8; runeChance = 0.75f; maxRuneRank = 3; equipChance = 0.95f; return true;
                case "SS":  maxRef = 9; runeChance = 0.85f; maxRuneRank = 4; equipChance = 1.00f; return true;
                case "SSS": maxRef = 10; runeChance = 1.00f; maxRuneRank = 5; equipChance = 1.00f; return true;
                default:    maxRef = 0; runeChance = 0f;    maxRuneRank = 0; equipChance = 0f;    return false;
            }
        }
    }

    // ══════════════════════════════════════════
    //  2. TRADER STOCK ENHANCEMENT
    // ══════════════════════════════════════════

    /// <summary>
    /// After a trader's stock is generated, enhance weapons/armor.
    /// Patches ThingSetMaker.Generate which covers quest rewards, item stashes,
    /// reward drop pods, and ancient dangers.
    /// </summary>
    [HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate), typeof(ThingSetMakerParams))]
    public static class Patch_EnhanceGeneratedThingSets
    {
        [HarmonyPostfix]
        public static void Postfix(List<Thing> __result)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (__result == null) return;

            foreach (Thing thing in __result)
            {
                if (thing == null) continue;
                if (!thing.def.IsWeapon && !thing.def.IsApparel) continue;

                // 30% base chance for any generated weapon/armor to be enhanced
                if (!Rand.Chance(0.30f)) continue;

                // Random enhancement: +1 to +6, good rune chance
                ForgeItemGenerator.TryApplyRandomEnhancement(thing, 6, 0.40f, 3);
            }
        }
    }

    /// <summary>
    /// Registers a manual Harmony postfix on the internal trader stock generation method.
    /// This enhances weapons/armor in trader inventories when they spawn.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Patch_EnhanceTraderStock
    {
        static Patch_EnhanceTraderStock()
        {
            try
            {
                var harmony = new Harmony("IsekaiLeveling.ForgeWorldPatches.TraderStock");

                // Patch ThingSetMaker_MarketValue.Generate — used by most trader stock generators
                var targetType = AccessTools.TypeByName("RimWorld.ThingSetMaker_MarketValue");
                if (targetType != null)
                {
                    var targetMethod = AccessTools.Method(targetType, "Generate", new[] { typeof(ThingSetMakerParams), typeof(List<Thing>) });
                    if (targetMethod != null)
                    {
                        harmony.Patch(targetMethod,
                            postfix: new HarmonyMethod(typeof(Patch_EnhanceTraderStock), nameof(TraderStockPostfix)));
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[IsekaiLeveling] Could not patch trader stock generation: {ex.Message}");
            }
        }

        public static void TraderStockPostfix(List<Thing> outThings)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (outThings == null) return;

            foreach (Thing thing in outThings)
            {
                if (thing == null) continue;
                if (!thing.def.IsWeapon && !thing.def.IsApparel) continue;

                // 35% chance for trader weapons/armor to be enhanced
                if (!Rand.Chance(0.35f)) continue;

                ForgeItemGenerator.TryApplyRandomEnhancement(thing, 6, 0.40f, 3);
            }
        }
    }
}
