using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Harmony postfix patches for RimWorld of Magic ability system.
    /// All patches are applied manually via reflection in RoMCompatibility.ApplyHarmonyPatches().
    /// These methods are static postfixes — Harmony injects __instance automatically.
    /// 
    /// Patch summary:
    /// 1. MagicAbility.PostAbilityAttempt → INT reduces magic cooldown
    /// 2. MightAbility.PostAbilityAttempt → DEX reduces might cooldown
    /// 3. ActualManaCost → WIS reduces mana cost
    /// 4. ActualStaminaCost → DEX reduces stamina cost
    /// 5. HediffComp_Chi.CompPostTick → VIT increases chi max
    /// 6. HediffComp_Psionic.CompPostTick → INT increases psionic max
    /// 7. CompSummoned.PostSpawnSetup → CHA increases summon duration
    /// 8. CompAbilityUserMagic.CompTick → INT/WIS scale comp.maxMP &amp; mpRegenRate directly
    ///    (cascades automatically through Need_Mana display, CurLevel clamp, and GainNeed regen)
    /// 9. CompAbilityUserMight.CompTick → VIT/DEX scale comp.maxSP &amp; spRegenRate directly
    /// </summary>
    public static class RoMHarmonyPatches
    {
        // ══════════════════ 1. Magic Cooldown (INT) ══════════════════
        
        /// <summary>
        /// After MagicAbility.PostAbilityAttempt sets CooldownTicksLeft,
        /// multiply it by INT-based cooldown reduction.
        /// </summary>
        public static void MagicAbility_PostAbilityAttempt_Postfix(object __instance)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                
                Pawn pawn = RoMCompatibility.GetPawnFromAbility(__instance);
                if (pawn == null) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float cdMult = RoMCompatibility.GetMagicCooldownMultiplier(comp);
                if (Mathf.Approximately(cdMult, 1f)) return;
                
                int currentCD = RoMCompatibility.GetCooldownTicksLeft(__instance);
                if (currentCD <= 0) return;
                
                int newCD = Mathf.Max(1, Mathf.RoundToInt(currentCD * cdMult));
                RoMCompatibility.SetCooldownTicksLeft(__instance, newCD);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM MagicAbility cooldown patch error: {ex.Message}", 
                    "IsekaiRoMMagicCD".GetHashCode());
            }
        }
        
        // ══════════════════ 2. Might Cooldown (DEX) ══════════════════
        
        /// <summary>
        /// After MightAbility.PostAbilityAttempt sets CooldownTicksLeft,
        /// multiply it by DEX-based cooldown reduction.
        /// </summary>
        public static void MightAbility_PostAbilityAttempt_Postfix(object __instance)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                
                Pawn pawn = RoMCompatibility.GetPawnFromAbility(__instance);
                if (pawn == null) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float cdMult = RoMCompatibility.GetMightCooldownMultiplier(comp);
                if (Mathf.Approximately(cdMult, 1f)) return;
                
                int currentCD = RoMCompatibility.GetCooldownTicksLeft(__instance);
                if (currentCD <= 0) return;
                
                int newCD = Mathf.Max(1, Mathf.RoundToInt(currentCD * cdMult));
                RoMCompatibility.SetCooldownTicksLeft(__instance, newCD);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM MightAbility cooldown patch error: {ex.Message}",
                    "IsekaiRoMMightCD".GetHashCode());
            }
        }
        
        // ══════════════════ 3. Mana Cost (WIS) ══════════════════
        
        /// <summary>
        /// After CompAbilityUserMagic.ActualManaCost returns the cost,
        /// multiply it by WIS-based cost reduction.
        /// </summary>
        public static void ActualManaCost_Postfix(object __instance, ref float __result)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                
                // __instance is CompAbilityUserMagic — get pawn via parent
                var thingComp = __instance as ThingComp;
                Pawn pawn = thingComp?.parent as Pawn;
                if (pawn == null) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float costMult = RoMCompatibility.GetManaCostMultiplier(comp);
                if (!Mathf.Approximately(costMult, 1f))
                    __result *= costMult;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM ManaCost patch error: {ex.Message}",
                    "IsekaiRoMManaCost".GetHashCode());
            }
        }
        
        // ══════════════════ 4. Stamina Cost (DEX) ══════════════════
        
        /// <summary>
        /// After CompAbilityUserMight.ActualStaminaCost returns the cost,
        /// multiply it by DEX-based cost reduction.
        /// </summary>
        public static void ActualStaminaCost_Postfix(object __instance, ref float __result)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                
                var thingComp = __instance as ThingComp;
                Pawn pawn = thingComp?.parent as Pawn;
                if (pawn == null) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float costMult = RoMCompatibility.GetStaminaCostMultiplier(comp);
                if (!Mathf.Approximately(costMult, 1f))
                    __result *= costMult;
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM StaminaCost patch error: {ex.Message}",
                    "IsekaiRoMStaminaCost".GetHashCode());
            }
        }
        
        // ══════════════════ 5. Chi Max (VIT) ══════════════════
        
        /// <summary>
        /// After HediffComp_Chi.CompPostTick runs (which clamps severity to maxSev),
        /// boost the maxSev based on VIT and re-clamp if we increased the cap.
        /// We track the base maxSev per pawn to avoid compounding the multiplier each tick.
        /// </summary>
        public static void Chi_CompPostTick_Postfix(HediffComp __instance, ref float severityAdjustment)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                
                Pawn pawn = RoMCompatibility.GetPawnFromHediffComp(__instance);
                if (pawn == null) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float boostedMax = RoMCompatibility.GetBoostedChiMax(__instance, comp);
                
                // Write the boosted max back to the field
                var maxField = __instance.GetType().GetField("maxSev", 
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (maxField != null)
                    maxField.SetValue(__instance, boostedMax);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM Chi max patch error: {ex.Message}",
                    "IsekaiRoMChiMax".GetHashCode());
            }
        }
        
        // ══════════════════ 6. Psionic Max (INT) ══════════════════

        /// <summary>
        /// HediffComp_Psionic.CompPostTick has a HARDCODED local <c>float num = 100f;</c>
        /// that gets passed as the upper bound to <c>Mathf.Clamp(Severity, 0f, num)</c>.
        /// A postfix can't undo that clamp — by the time it runs, severity is already
        /// pegged at 100 and any "raise the ceiling" logic is a no-op.
        ///
        /// Solution: a transpiler that swaps the literal <c>100f</c> for a call to
        /// <see cref="GetPsionicMaxForComp"/>, which returns the per-pawn boosted cap
        /// based on INT. Vanilla then naturally clamps to that boosted value, regen
        /// fills past 100, energy spending decrements normally — no state tracking
        /// required, no fight against vanilla logic.
        /// </summary>
        public static IEnumerable<CodeInstruction> Psionic_CompPostTick_Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo helper = AccessTools.Method(typeof(RoMHarmonyPatches), nameof(GetPsionicMaxForComp));
            bool replaced = false;
            int seenLdc100 = 0;

            foreach (CodeInstruction inst in instructions)
            {
                bool isLdc100 = !replaced
                    && inst.opcode == OpCodes.Ldc_R4
                    && inst.operand is float f
                    && Mathf.Approximately(f, 100f);

                if (isLdc100)
                {
                    seenLdc100++;
                    // Replace with: ldarg.0 (this); call GetPsionicMaxForComp(HediffComp)
                    // Preserve any branch labels / exception block markers from the original
                    // ldc.r4 instruction by attaching them to the first replacement instruction.
                    var loadThis = new CodeInstruction(OpCodes.Ldarg_0)
                    {
                        labels = inst.labels,
                        blocks = inst.blocks,
                    };
                    yield return loadThis;
                    yield return new CodeInstruction(OpCodes.Call, helper);
                    replaced = true;
                }
                else
                {
                    yield return inst;
                }
            }

            if (!replaced)
            {
                Log.Warning("[Isekai Leveling] RoM Psionic transpiler: did not find ldc.r4 100 in HediffComp_Psionic.CompPostTick — INT will not raise psionic max. The mod may have updated; this needs investigation.");
            }
        }

        /// <summary>
        /// Helper invoked by the transpiler in place of the hardcoded <c>100f</c> max.
        /// Returns the per-pawn boosted psionic cap (INT-scaled) or 100f as a safe default.
        /// </summary>
        public static float GetPsionicMaxForComp(HediffComp comp)
        {
            try
            {
                if (comp?.parent == null) return 100f;
                Pawn pawn = RoMCompatibility.GetPawnFromHediffComp(comp);
                if (pawn == null) return 100f;
                var isekaiComp = IsekaiComponent.GetCached(pawn);
                if (isekaiComp?.stats == null) return 100f;
                float boosted = RoMCompatibility.GetBoostedPsionicMax(isekaiComp);
                // Never go below the vanilla cap — a negative INT shouldn't shrink the pool.
                return boosted >= 100f ? boosted : 100f;
            }
            catch
            {
                return 100f;
            }
        }
        
        // ══════════════════ 7. Summon Duration (CHA) + Level Scaling ══════════════════
        
        /// <summary>
        /// After CompSummoned.PostSpawnSetup:
        ///   1. Multiply ticksToDestroy by CHA-based duration bonus.
        ///   2. Scale the summoned creature's MobRank/level to the caster's level
        ///      (~80% for timed summons, ~110% + elite for permanent/ultimate summons like
        ///      the Shaman's Guardian Spirit).
        /// Runs once on summon creation, not per-tick.
        /// </summary>
        public static void CompSummoned_PostSpawnSetup_Postfix(ThingComp __instance)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;

                // Patch is applied to ThingComp.PostSpawnSetup (base virtual) — filter to
                // only act on TorannMagic.CompSummoned instances.
                var summonedType = RoMCompatibility.compSummonedType;
                if (summonedType == null || !summonedType.IsInstanceOfType(__instance)) return;

                Pawn summoner = RoMCompatibility.GetSummonerPawn(__instance);
                if (summoner == null) return;

                var iComp = IsekaiComponent.GetCached(summoner);
                if (iComp?.stats == null) return;

                // ─── (1) Duration scaling ───
                float durationMult = RoMCompatibility.GetSummonDurationMultiplier(iComp);
                if (!Mathf.Approximately(durationMult, 1f))
                {
                    var ticksField = __instance.GetType().GetField("ticksToDestroy",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (ticksField != null)
                    {
                        int currentTicks = (int)ticksField.GetValue(__instance);
                        if (currentTicks > 0)
                        {
                            int newTicks = Mathf.RoundToInt(currentTicks * durationMult);
                            ticksField.SetValue(__instance, newTicks);
                        }
                    }
                }

                // ─── (2) Level scaling of the summoned creature ───
                // Only scale player-side summons to avoid buffing enemy conjurers.
                var creature = __instance.parent as Pawn;
                if (creature == null || creature.RaceProps == null || creature.RaceProps.Humanlike) return;
                if (creature.Faction == null || !creature.Faction.IsPlayer) return;

                bool permanent = RoMCompatibility.IsPermanentSummon(__instance);
                ApplySummonLevelScaling(creature, iComp.currentLevel, permanent);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM summon postfix error: {ex.Message}",
                    "IsekaiRoMSummonPostfix".GetHashCode());
            }
        }

        /// <summary>
        /// Apply rank + level override to a summoned creature's MobRankComponent so it
        /// roughly matches the caster's power. Timed summons run at ~80% of caster
        /// level; permanent/sustained summons (e.g. Guardian Spirit) run at ~110% and
        /// are flagged elite for an extra buff.
        /// </summary>
        private static void ApplySummonLevelScaling(Pawn creature, int casterLevel, bool permanent)
        {
            if (creature == null || casterLevel <= 0) return;

            var rankComp = creature.TryGetComp<IsekaiLeveling.MobRanking.MobRankComponent>();
            if (rankComp == null) return;

            // Permanent/ultimate summons are a bit stronger than the caster; timed
            // summons a bit weaker so they feel expendable.
            float factor = permanent ? 1.10f : 0.80f;
            int summonLevel = Mathf.Max(1, Mathf.RoundToInt(casterLevel * factor));

            var tier = LevelToMobRankTier(summonLevel);
            rankComp.SetRankOverride(tier);
            rankComp.currentLevel = summonLevel;
            if (permanent)
            {
                rankComp.SetEliteOverride(true);
            }
        }

        private static IsekaiLeveling.MobRanking.MobRankTier LevelToMobRankTier(int level)
        {
            if (level >= 401) return IsekaiLeveling.MobRanking.MobRankTier.SSS;
            if (level >= 201) return IsekaiLeveling.MobRanking.MobRankTier.SS;
            if (level >= 101) return IsekaiLeveling.MobRanking.MobRankTier.S;
            if (level >= 51)  return IsekaiLeveling.MobRanking.MobRankTier.A;
            if (level >= 26)  return IsekaiLeveling.MobRanking.MobRankTier.B;
            if (level >= 18)  return IsekaiLeveling.MobRanking.MobRankTier.C;
            if (level >= 11)  return IsekaiLeveling.MobRanking.MobRankTier.D;
            if (level >= 6)   return IsekaiLeveling.MobRanking.MobRankTier.E;
            return IsekaiLeveling.MobRanking.MobRankTier.F;
        }
        
        // ══════════════════ 8. CompAbilityUserMagic.CompTick ══════════════════
        //   Directly multiply comp.maxMP and comp.mpRegenRate so RoM's internal Need_Mana
        //   logic naturally uses boosted values for MaxLevel, CurLevel clamp (2*maxMP),
        //   and GainNeed regen (which uses comp.mpRegenRate * 0.0012f).
        //   Uses non-compounding tracking: if RoM modified the field (e.g. on level-up),
        //   adopt the new value as the base and re-apply our multiplier.
        
        public static void CompMagic_CompTick_Postfix(ThingComp __instance)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                if (RoMCompatibility.field_maxMP == null || RoMCompatibility.field_mpRegenRate == null) return;

                // NOTE: Must run every tick — RoM's CompAbilityUserMagic.CompTick recalculates
                // maxMP from the base formula each tick. Throttling causes Need_Mana.MaxLevel
                // to oscillate between the multiplied value and RoM's recalculated base, which
                // shows up as a flickering mana bar (reported by users as max value jumping
                // every ~1 second). The ApplyTrackedMultiplier helper below already prevents
                // compounding by detecting whether RoM or this patch wrote the field last.

                Pawn pawn = __instance.parent as Pawn;
                if (pawn == null) return;
                
                var iComp = IsekaiComponent.GetCached(pawn);
                if (iComp?.stats == null) return;
                
                int pawnId = pawn.thingIDNumber;
                
                // ─── maxMP ───
                float maxMult = RoMCompatibility.GetMaxManaMultiplier(iComp);
                ApplyTrackedMultiplier(__instance, RoMCompatibility.field_maxMP,
                    RoMCompatibility.magicMaxMPTracker, pawnId, maxMult);
                
                // ─── mpRegenRate ───
                float regenMult = RoMCompatibility.GetManaRegenMultiplier(iComp);
                ApplyTrackedMultiplier(__instance, RoMCompatibility.field_mpRegenRate,
                    RoMCompatibility.magicRegenTracker, pawnId, regenMult);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM CompMagic.CompTick patch error: {ex.Message}",
                    "IsekaiRoMCompMagicTick".GetHashCode());
            }
        }
        
        // ══════════════════ 9. CompAbilityUserMight.CompTick ══════════════════
        public static void CompMight_CompTick_Postfix(ThingComp __instance)
        {
            try
            {
                if (!ModCompatibility.RimWorldOfMagicActive) return;
                if (RoMCompatibility.field_maxSP == null || RoMCompatibility.field_spRegenRate == null) return;

                // NOTE: Must run every tick — same reason as CompMagic_CompTick_Postfix above.
                // RoM recalculates maxSP each tick; throttling causes the stamina bar to flicker.

                Pawn pawn = __instance.parent as Pawn;
                if (pawn == null) return;
                
                var iComp = IsekaiComponent.GetCached(pawn);
                if (iComp?.stats == null) return;
                
                int pawnId = pawn.thingIDNumber;
                
                float maxMult = RoMCompatibility.GetMaxStaminaMultiplier(iComp);
                ApplyTrackedMultiplier(__instance, RoMCompatibility.field_maxSP,
                    RoMCompatibility.mightMaxSPTracker, pawnId, maxMult);
                
                float regenMult = RoMCompatibility.GetStaminaRegenMultiplier(iComp);
                ApplyTrackedMultiplier(__instance, RoMCompatibility.field_spRegenRate,
                    RoMCompatibility.mightRegenTracker, pawnId, regenMult);
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[Isekai Leveling] RoM CompMight.CompTick patch error: {ex.Message}",
                    "IsekaiRoMCompMightTick".GetHashCode());
            }
        }
        
        /// <summary>
        /// Non-compounding multiplier application:
        /// - If field == lastApplied, RoM hasn't touched the field; just re-apply (no-op effectively).
        /// - If field != lastApplied, RoM updated it (level-up, init, enchant, etc.) → adopt as new base.
        /// - Compute target = base * mult, write to field, store as lastApplied.
        /// Uses small epsilon to detect changes (float comparison).
        /// </summary>
        private static void ApplyTrackedMultiplier(
            ThingComp comp, FieldInfo field,
            System.Collections.Generic.Dictionary<int, (float baseVal, float lastApplied)> tracker,
            int pawnId, float mult)
        {
            float current = (float)field.GetValue(comp);
            
            // Skip if multiplier ~1 and we have no cached state — leave RoM's value untouched
            if (Mathf.Approximately(mult, 1f) && !tracker.ContainsKey(pawnId))
                return;
            
            float baseVal;
            if (tracker.TryGetValue(pawnId, out var entry))
            {
                // Detect if RoM changed the field since our last write
                if (Mathf.Abs(current - entry.lastApplied) > 0.0001f)
                {
                    // RoM updated it — treat current as new base
                    baseVal = current;
                }
                else
                {
                    baseVal = entry.baseVal;
                }
            }
            else
            {
                // First sighting — current is the base
                baseVal = current;
            }
            
            float target = baseVal * mult;
            
            // Avoid pointless writes if nothing changed
            if (Mathf.Abs(current - target) > 0.0001f)
                field.SetValue(comp, target);
            
            tracker[pawnId] = (baseVal, target);
        }
    }
}
