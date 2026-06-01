using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using System;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.SkillTree;

namespace IsekaiLeveling.Patches
{
    // NOTE: Patch_StatBonuses was removed. Level-based bonuses (MoveSpeed, MeleeHitChance,
    // MeleeDodgeChance, ShootingAccuracyPawn, WorkSpeedGlobal, SocialImpact) are now delivered
    // through the existing StatPart classes in IsekaiStatParts.cs, wired via IsekaiStatPatches.xml.
    // This eliminates a Harmony postfix on StatWorker.GetValueUnfinalized — one of the most
    // frequently called methods in RimWorld — that ran on every stat query for every pawn.
    // Credit: Chiseled Cactus (performance analysis with Dubs Performance Analyzer)

    /// <summary>
    /// Apply health bonuses from Vitality stat.
    /// Performance-critical: Pawn.HealthScale getter is called very frequently.
    /// Uses cached component lookup to avoid O(n) GetComp scan.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.HealthScale), MethodType.Getter)]
    public static class Patch_HealthBonus
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref float __result)
        {
            try
            {
                if (__instance == null || __instance.Dead || __instance.Destroyed) return;
                
                // O(1) cached lookup
                var comp = IsekaiComponent.GetCached(__instance);
                if (comp?.stats == null) return;
                
                // Vitality stat bonus to health
                int vit = comp.stats.vitality;
                float multiplier = 1f;
                
                if (vit > 5)
                {
                    float vitBonus = (vit - 5) * IsekaiLevelingSettings.VIT_MaxHealth;
                    multiplier += vitBonus;
                }
                
                // Passive tree MaxHealth bonus (applied to actual HP, not as DR)
                if (comp.passiveTree != null)
                {
                    float passiveMaxHP = comp.passiveTree.GetTotalBonus(PassiveBonusType.MaxHealth);
                    if (passiveMaxHP != 0f) multiplier += passiveMaxHP;
                }
                
                if (multiplier == 1f) return; // No change
                
                float newResult = __result * multiplier;
                
                if (!float.IsNaN(newResult) && !float.IsInfinity(newResult) && newResult > 0f)
                {
                    __result = newResult;
                }
            }
            catch (Exception)
            {
                // Silently ignore exceptions to prevent crashing RimHUD and other UI mods
            }
        }
    }

    /// <summary>
    /// Round body part max HP to the nearest integer for pawns with VIT health bonus.
    /// Uses cached component lookup on this hot path.
    /// </summary>
    [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
    public static class Patch_RoundBodyPartHealth
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.RaceProps.Humanlike) return;

                // O(1) cached lookup
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;

                __result = Mathf.Max(Mathf.RoundToInt(__result), 1f);
            }
            catch { /* Silently ignore to protect UI mods */ }
        }
    }

    /// <summary>
    /// Apply Vitality-based bleed reduction to HediffSet.BleedRateTotal.
    /// Mirrors the formula advertised in Window_StatsAttribution: every point of
    /// vitality above the baseline divides bleed rate by (1 + diff * VIT_MaxHealth).
    /// Verified by IsekaiMod.VerifyCriticalPatches; missing this patch logs a CRITICAL.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_BleedRateReduction
    {
        // AccessTools resolves the property getter at runtime so we don't fight RimWorld
        // version drift; the verifier targets the same PropertyGetter to confirm it landed.
        public static MethodBase TargetMethod() =>
            AccessTools.PropertyGetter(typeof(HediffSet), nameof(HediffSet.BleedRateTotal));

        [HarmonyPostfix]
        public static void Postfix(HediffSet __instance, ref float __result)
        {
            try
            {
                if (__result <= 0f) return;

                Pawn pawn = __instance?.pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                if (!pawn.RaceProps.Humanlike) return;

                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;

                int vit = comp.stats.vitality;
                if (vit <= 5) return;

                float bonus = (vit - 5) * IsekaiLevelingSettings.VIT_MaxHealth;
                if (bonus <= 0f) return;

                float reduced = __result / (1f + bonus);
                if (!float.IsNaN(reduced) && !float.IsInfinity(reduced) && reduced >= 0f)
                {
                    __result = reduced;
                }
            }
            catch
            {
                // Bleed-rate getter is called on every pawn tick; never throw out of here.
            }
        }
    }

    /// <summary>
    /// Apply Wisdom bonus to XP gain
    /// </summary>
    [HarmonyPatch(typeof(IsekaiComponent), nameof(IsekaiComponent.GainXP))]
    public static class Patch_XPGain
    {
        [HarmonyPrefix]
        public static void Prefix(IsekaiComponent __instance, ref int amount)
        {
            try
            {
                if (__instance?.stats == null) return;
                
                // Apply Wisdom multiplier to XP gain
                float wisdomMult = __instance.stats.GetStatMultiplier(IsekaiStatType.Wisdom);
                if (!float.IsNaN(wisdomMult) && !float.IsInfinity(wisdomMult))
                {
                    amount = Mathf.RoundToInt(amount * wisdomMult);
                }
            }
            catch (Exception)
            {
                // Silently ignore
            }
        }
    }

    /// <summary>
    /// VIT slows biological aging — high-VIT pawns live longer.
    /// Accumulates fractional tick debt and periodically subtracts whole ticks
    /// from biological age to avoid floating-point drift.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_AgeTracker), "TickBiologicalAge")]
    public static class Patch_LifespanBonus
    {
        // Cached reflection for private fields
        private static readonly FieldInfo ageBioField =
            AccessTools.Field(typeof(Pawn_AgeTracker), "ageBiologicalTicksInt");
        private static readonly FieldInfo pawnField =
            AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");

        // Fractional tick debt per pawn (keyed by thingIDNumber)
        private static readonly Dictionary<int, float> agingDebt = new Dictionary<int, float>();

        [HarmonyPostfix]
        public static void Postfix(Pawn_AgeTracker __instance, int interval)
        {
            try
            {
                if (ageBioField == null || pawnField == null) return;

                float rate = IsekaiLevelingSettings.VIT_LifespanFactor;
                if (rate <= 0f) return;

                Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;

                // Skip if pawn has Ageless gene or any gene that zeros biological aging.
                // Without this check, VIT subtracts ticks on top of vanilla's 0-tick increment,
                // causing pawns to age backwards.
                if (ModsConfig.BiotechActive && pawn.genes != null)
                {
                    var geneList = pawn.genes.GenesListForReading;
                    for (int g = 0; g < geneList.Count; g++)
                    {
                        var gene = geneList[g];
                        if (gene == null || gene.def == null || !gene.Active) continue;
                        // Gene_Ageless class name check (compat-safe across modded ageless variants)
                        string typeName = gene.GetType().Name;
                        if (typeName == "Gene_Ageless" || typeName.Contains("Ageless")) return;
                    }
                }

                float effectiveVit = 0f;

                // O(1) cached lookup for humanlike pawns
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats != null)
                {
                    int vit = comp.stats.vitality;
                    if (vit <= 5) return; // No bonus at base VIT
                    effectiveVit = vit - 5;
                }
                else
                {
                    // Creature lifespan: try MobRankComponent (animals, mechs, etc.)
                    var rankComp = pawn.TryGetComp<MobRankComponent>();
                    if (rankComp != null && rankComp.statsInitialized && rankComp.stats != null)
                    {
                        // Creatures use the full VIT value (not VIT-5 like pawns)
                        effectiveVit = rankComp.stats.vitality;
                    }
                }

                if (effectiveVit <= 0f) return;

                // lifespanFactor = 1 + effectiveVit * rate
                // Aging should proceed at 1/factor speed.
                // Vanilla added `interval` ticks; we undo (1 - 1/factor) of that.
                float factor = 1f + effectiveVit * rate;
                float reduction = (1f - (1f / factor)) * interval; // fraction of ticks to undo

                int id = pawn.thingIDNumber;
                agingDebt.TryGetValue(id, out float debt);
                debt += reduction;

                if (debt >= 1f)
                {
                    int whole = (int)debt;
                    long current = (long)ageBioField.GetValue(__instance);
                    ageBioField.SetValue(__instance, current - whole);
                    debt -= whole;
                }

                agingDebt[id] = debt;
            }
            catch (Exception)
            {
                // Silently ignore — aging continues normally if anything goes wrong
            }
        }

        /// <summary>
        /// Clean up debt tracking when a pawn is destroyed.
        /// Called from IsekaiComponent.PostDeSpawn.
        /// </summary>
        public static void CleanupPawn(int pawnId)
        {
            agingDebt.Remove(pawnId);
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            agingDebt.Clear();
        }
    }

    /// <summary>
    /// WIS reduces psyfocus cost when casting psycasts.
    /// Patches Ability.FinalPsyfocusCost to apply the actual cost reduction
    /// through the standard cost pipeline.
    /// Works with both vanilla Royalty and Vanilla Psycasts Expanded (VPE extends Ability).
    /// CONDITIONAL: Only patches if Royalty DLC is loaded (psyfocus requires Royalty).
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.FinalPsyfocusCost))]
    public static class Patch_PsyfocusCostReduction
    {
        static bool Prepare() => ModLister.RoyaltyInstalled;

        /// <summary>
        /// Compute the WIS psyfocus cost multiplier for a pawn.
        /// Returns 1.0 if no reduction applies.
        /// Shared by the FinalPsyfocusCost postfix and the tooltip patch.
        /// </summary>
        public static float GetWisMultiplier(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed) return 1f;
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp?.stats == null) return 1f;
            float wis = comp.stats.wisdom;
            // Configurable cost reduction per WIS point above base (5)
            float multiplier = 1f - (wis - 5) * IsekaiLevelingSettings.WIS_PsyfocusCost;
            return Mathf.Clamp(multiplier, 0.3f, 1.2f);
        }

        [HarmonyPostfix]
        public static void Postfix(Ability __instance, ref float __result)
        {
            try
            {
                if (__result <= 0f) return; // No cost to reduce
                __result *= GetWisMultiplier(__instance.pawn);
            }
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Patches the ability tooltip to display the WIS-modified psyfocus cost
    /// instead of the raw base cost. Vanilla tooltips read from
    /// AbilityDef.PsyfocusCost (raw statBases) and never call FinalPsyfocusCost,
    /// so the cost reduction from WIS would otherwise be invisible to the player.
    /// </summary>
    [HarmonyPatch(typeof(Ability), "get_Tooltip")]
    public static class Patch_PsyfocusCostTooltip
    {
        static bool Prepare() => ModLister.RoyaltyInstalled;

        [HarmonyPostfix]
        public static void Postfix(Ability __instance, ref string __result)
        {
            try
            {
                float baseCost = __instance.def.PsyfocusCost;
                if (baseCost <= 0f) return;

                float multiplier = Patch_PsyfocusCostReduction.GetWisMultiplier(__instance.pawn);
                if (Mathf.Approximately(multiplier, 1f)) return;

                float modifiedCost = baseCost * multiplier;
                
                // Always try to replace the displayed cost with the reduced value
                string oldCostStr = baseCost.ToStringPercent();
                string newCostStr = modifiedCost.ToStringPercent();
                
                string label = "AbilityPsyfocusCost".Translate();
                
                // Replace the base cost line with the modified cost (even if rounded values match,
                // the appended WIS line below provides visibility)
                if (oldCostStr != newCostStr)
                {
                    string oldLine = label + ": " + oldCostStr;
                    string newLine = label + ": " + newCostStr;
                    if (__result.Contains(oldLine))
                    {
                        __result = __result.Replace(oldLine, newLine);
                    }
                }
                
                // Always append a WIS reduction line so the player sees the effect,
                // even when rounding hides the change (e.g. 2% → 1.76% both display "2%")
                float reductionPct = (1f - multiplier) * 100f;
                __result += "\n" + "Isekai_WIS_PsyfocusCostReduction".Translate(reductionPct.ToString("F1"));
            }
            catch (Exception) { }
        }
    }
}
