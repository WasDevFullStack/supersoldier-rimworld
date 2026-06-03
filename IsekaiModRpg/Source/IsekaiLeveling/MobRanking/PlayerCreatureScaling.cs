using System;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Applies Isekai stat bonuses to PLAYER-faction non-mech creatures (tamed animals,
    /// insects, etc.) based on their OWN MobRankComponent stats and level.
    ///
    /// This fills a gap that previously left player animals with no bonuses at all:
    ///   • Wild / enemy creatures  → MobRankStatPatches (flat rank multipliers; skips player pawns)
    ///   • Player mechs            → MechScalingSystem (scales off the mechanitor)
    ///   • Player animals          → (nothing — this system)
    ///
    /// Formulas mirror IsekaiStatInfo.GetCreatureStatEffects EXACTLY (full stat value, not
    /// value-5, scaled by the tamed-retention setting) so the values shown in the creature
    /// stat window match what is actually applied. Reported bugs fixed: tamed animals not
    /// retaining stats, animals not benefiting from leveling/VIT, decimal body-part health.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PlayerCreatureScaling
    {
        /// <summary>
        /// A player-faction, non-humanlike, NON-mech creature with an initialized rank
        /// component. Mechs are handled by MechScalingSystem; humanlikes by IsekaiComponent.
        /// </summary>
        public static bool IsPlayerScalableCreature(Pawn pawn)
        {
            if (pawn == null || pawn.Dead) return false;
            if (pawn.Faction == null || !pawn.Faction.IsPlayer) return false;
            if (pawn.RaceProps == null) return false;
            if (pawn.RaceProps.Humanlike) return false;
            if (pawn.RaceProps.IsMechanoid) return false; // mechs scale off their mechanitor
            // Respect the "exclude from ranking" mod settings — excluded creatures keep
            // default stats and must receive no bonuses.
            if (MobRankInjector.IsExcludedFromRanking(pawn)) return false;
            return true;
        }

        /// <summary>Returns the creature's initialized rank component, or null.</summary>
        public static MobRankComponent GetComp(Pawn pawn)
        {
            var comp = pawn.GetComp<MobRankComponent>();
            if (comp == null || !comp.IsInitialized || comp.stats == null) return null;
            return comp;
        }

        /// <summary>
        /// Fraction of stat bonuses a tamed creature keeps (mod setting). Mirrors the
        /// tamedRetention parameter used by GetCreatureStatEffects so display == applied.
        /// </summary>
        public static float Retention =>
            Mathf.Clamp(IsekaiLevelingSettings.TamedAnimalBonusRetention, 0f, 1f);

        // ── Multiplier helpers (full stat value × per-point, then retention-scaled) ──

        private static float ScaledMult(float statValue, float perPoint)
        {
            float mult = 1f + Mathf.Max(0f, statValue) * perPoint;
            return 1f + (mult - 1f) * Retention;
        }

        private static float ScaledOffset(float statValue, float perPoint)
        {
            return Mathf.Max(0f, statValue) * perPoint * Retention;
        }

        public static float GetMeleeDamageMult(MobRankComponent c) =>
            ScaledMult(c.stats.strength, IsekaiLevelingSettings.STR_MeleeDamage);

        public static float GetCarryCapacityMult(MobRankComponent c) =>
            ScaledMult(c.stats.strength, IsekaiLevelingSettings.STR_CarryCapacity);

        public static float GetMoveSpeedMult(MobRankComponent c) =>
            ScaledMult(c.stats.dexterity, IsekaiLevelingSettings.DEX_MoveSpeed);

        public static float GetMeleeDodgeOffset(MobRankComponent c) =>
            ScaledOffset(c.stats.dexterity, IsekaiLevelingSettings.DEX_MeleeDodge);

        public static float GetMeleeHitOffset(MobRankComponent c) =>
            ScaledOffset(c.stats.dexterity, IsekaiLevelingSettings.DEX_MeleeHitChance);

        public static float GetHealthRegenMult(MobRankComponent c) =>
            ScaledMult(c.stats.vitality, IsekaiLevelingSettings.VIT_HealthRegen);

        public static float GetImmunityGainMult(MobRankComponent c) =>
            ScaledMult(c.stats.vitality, IsekaiLevelingSettings.VIT_ImmunityGain);

        public static float GetRestRateMult(MobRankComponent c) =>
            ScaledMult(c.stats.vitality, IsekaiLevelingSettings.VIT_RestRate);

        public static float GetSharpArmorOffset(MobRankComponent c) =>
            ScaledOffset(c.stats.vitality, 0.008f);

        public static float GetBluntArmorOffset(MobRankComponent c) =>
            ScaledOffset(c.stats.vitality, 0.01f);

        public static float GetHeatArmorOffset(MobRankComponent c) =>
            ScaledOffset(c.stats.vitality, 0.007f);

        public static float GetToxicResistOffset(MobRankComponent c) =>
            ScaledOffset(c.stats.vitality, IsekaiLevelingSettings.VIT_ToxicResist);

        /// <summary>Health multiplier from VIT (full value), retention-scaled. Matches display.</summary>
        public static float GetHealthMult(MobRankComponent c) =>
            ScaledMult(c.stats.vitality, IsekaiLevelingSettings.VIT_MaxHealth);
    }

    /// <summary>
    /// Applies player-creature stat scaling to combat/utility stats. Runs after the mod's
    /// base stat parts. Mutually exclusive with MechScalingSystem (mechs excluded) and
    /// MobRankStatPatches (which skips player pawns), so no double application.
    /// </summary>
    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    [HarmonyAfter("JellyCreative.IsekaiLeveling")]
    public static class PlayerCreatureStatScaling_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, StatRequest req, StatDef ___stat)
        {
            try
            {
                // Guard against recursion during rank calc (shared flag with the old system).
                if (MobRankStatPatches.IsCalculatingRank) return;
                if (!req.HasThing) return;

                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || !PlayerCreatureScaling.IsPlayerScalableCreature(pawn)) return;

                var c = PlayerCreatureScaling.GetComp(pawn);
                if (c == null) return;

                if (___stat == StatDefOf.MeleeDamageFactor)
                    __result *= PlayerCreatureScaling.GetMeleeDamageMult(c);
                else if (___stat == StatDefOf.MeleeHitChance)
                    __result += PlayerCreatureScaling.GetMeleeHitOffset(c);
                else if (___stat == StatDefOf.MeleeDodgeChance)
                    __result += PlayerCreatureScaling.GetMeleeDodgeOffset(c);
                else if (___stat == StatDefOf.MoveSpeed)
                    __result *= PlayerCreatureScaling.GetMoveSpeedMult(c);
                else if (___stat == StatDefOf.CarryingCapacity)
                    __result *= PlayerCreatureScaling.GetCarryCapacityMult(c);
                else if (___stat == StatDefOf.ArmorRating_Sharp)
                    __result += PlayerCreatureScaling.GetSharpArmorOffset(c);
                else if (___stat == StatDefOf.ArmorRating_Blunt)
                    __result += PlayerCreatureScaling.GetBluntArmorOffset(c);
                else if (___stat == StatDefOf.ArmorRating_Heat)
                    __result += PlayerCreatureScaling.GetHeatArmorOffset(c);
                else if (___stat == StatDefOf.ToxicResistance)
                    __result += PlayerCreatureScaling.GetToxicResistOffset(c);
                else if (___stat == StatDefOf.ImmunityGainSpeed)
                    __result *= PlayerCreatureScaling.GetImmunityGainMult(c);
                else if (___stat == StatDefOf.InjuryHealingFactor)
                    __result *= PlayerCreatureScaling.GetHealthRegenMult(c);
                else if (___stat == StatDefOf.RestRateMultiplier)
                    __result *= PlayerCreatureScaling.GetRestRateMult(c);
            }
            catch { /* never throw out of a stat query — protects RimHUD etc. */ }
        }
    }

    /// <summary>
    /// Applies VIT-based max-health scaling to player creatures and ROUNDS the result to a
    /// whole number. Rounding fixes the reported bug where VIT-boosted animals had fractional
    /// body-part HP, which corrupted derived movement/manipulation capacity calculations.
    /// </summary>
    [HarmonyPatch(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth))]
    [HarmonyAfter("JellyCreative.IsekaiLeveling")]
    public static class PlayerCreatureHealthScaling_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ref float __result, Pawn pawn)
        {
            try
            {
                if (MobRankStatPatches.IsCalculatingRank) return;
                if (pawn == null || !PlayerCreatureScaling.IsPlayerScalableCreature(pawn)) return;

                var c = PlayerCreatureScaling.GetComp(pawn);
                if (c == null) return;

                __result *= PlayerCreatureScaling.GetHealthMult(c);
                // Whole-number body part HP — fractional HP breaks capacity math (#335).
                __result = Mathf.Max(1f, Mathf.Round(__result));
            }
            catch { }
        }
    }
}
