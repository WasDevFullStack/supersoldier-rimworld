using RimWorld;
using UnityEngine;
using Verse;
using System;
using IsekaiLeveling.SkillTree;

namespace IsekaiLeveling.Stats
{
    /// <summary>
    /// Base class for Isekai stat parts that modify RimWorld stats based on Isekai attributes.
    /// Uses cached component lookup (O(1)) instead of GetComp linear scan on this hot path.
    /// TransformValue / ExplanationPart are sealed with try-catch wrappers to prevent
    /// uncaught exceptions from crashing RimHUD and other UI mods that query stat values.
    /// Subclasses override TransformValueCore / ExplanationPartCore instead.
    /// </summary>
    public abstract class StatPart_IsekaiBase : StatPart
    {
        // ── Safe wrappers — prevent stat query exceptions from crashing UI mods (RimHUD) ──

        public sealed override void TransformValue(StatRequest req, ref float val)
        {
            try { TransformValueCore(req, ref val); } catch { }
        }

        public sealed override string ExplanationPart(StatRequest req)
        {
            try { return ExplanationPartCore(req); } catch { return null; }
        }

        protected virtual void TransformValueCore(StatRequest req, ref float val) { }
        protected virtual string ExplanationPartCore(StatRequest req) { return null; }

        protected IsekaiComponent GetComp(StatRequest req)
        {
            try
            {
                if (!req.HasThing) return null;
                if (!(req.Thing is Pawn pawn)) return null;
                if (pawn.Dead || pawn.Destroyed) return null;
                // O(1) cached lookup instead of O(n) linear scan
                return IsekaiComponent.GetCached(pawn);
            }
            catch
            {
                return null;
            }
        }

        protected float GetStatValue(IsekaiComponent comp, string statName)
        {
            try
            {
                if (comp?.stats == null) return 5f; // Base value
                
                float raw;
                IsekaiStatType statType;
                switch (statName)
                {
                    case "STR": raw = comp.stats.strength; statType = IsekaiStatType.Strength; break;
                    case "DEX": raw = comp.stats.dexterity; statType = IsekaiStatType.Dexterity; break;
                    case "VIT": raw = comp.stats.vitality; statType = IsekaiStatType.Vitality; break;
                    case "INT": raw = comp.stats.intelligence; statType = IsekaiStatType.Intelligence; break;
                    case "WIS": raw = comp.stats.wisdom; statType = IsekaiStatType.Wisdom; break;
                    case "CHA": raw = comp.stats.charisma; statType = IsekaiStatType.Charisma; break;
                    default: return 5f;
                }
                
                // Apply trait effectiveness (e.g. Mighty gives 1.5x STR effectiveness)
                Pawn pawn = comp.parent as Pawn;
                if (pawn != null)
                {
                    float traitMult = IsekaiTraitHelper.GetStatEffectiveness(pawn, statType, comp.currentLevel);
                    if (traitMult != 1f)
                        raw = 5f + (raw - 5f) * traitMult;
                }
                
                return raw;
            }
            catch
            {
                return 5f;
            }
        }

        /// <summary>
        /// Convert stat value to a multiplier. Base (5) = 1.0x, each point above/below shifts by 2%
        /// So 100 stat = 1.0 + (100-5)*0.02 = 1.0 + 1.9 = 2.9x multiplier
        /// And 0 stat = 1.0 + (0-5)*0.02 = 0.9x multiplier
        /// </summary>
        protected float StatToMultiplier(float statValue, float multiplierPerPoint = 0.02f)
        {
            float result = 1f + (statValue - 5f) * multiplierPerPoint;
            // Ensure we never return NaN, Infinity, or extreme values
            if (float.IsNaN(result) || float.IsInfinity(result)) return 1f;
            return result;
        }

        /// <summary>
        /// Apply trait-based stat effectiveness to a multiplier.
        /// Should be called after StatToMultiplier to scale the result by the trait modifier.
        /// </summary>
        protected float ApplyTraitEffectiveness(StatRequest req, float multiplier, IsekaiStatType statType)
        {
            if (!req.HasThing) return multiplier;
            if (!(req.Thing is Pawn pawn)) return multiplier;
            var comp = GetComp(req);
            if (comp == null) return multiplier;
            
            float traitMult = IsekaiTraitHelper.GetStatEffectiveness(pawn, statType, comp.currentLevel);
            if (traitMult != 1f)
            {
                // Apply effectiveness to the bonus portion only (above 1.0)
                // So if multiplier is 1.5 (50% bonus) and traitMult is 1.5, result is 1.0 + 0.5*1.5 = 1.75
                float bonus = multiplier - 1f;
                multiplier = 1f + bonus * traitMult;
            }
            return multiplier;
        }

        /// <summary>
        /// Convert stat value to a flat offset. Each point = offset amount
        /// </summary>
        protected float StatToOffset(float statValue, float offsetPerPoint = 0.01f)
        {
            float result = (statValue - 5f) * offsetPerPoint;
            // Ensure we never return NaN, Infinity, or extreme values
            if (float.IsNaN(result) || float.IsInfinity(result)) return 0f;
            return result;
        }

        // ── Passive Skill Tree Bonus Helpers ──

        /// <summary>Get the total passive tree bonus for a specific type</summary>
        protected float GetPassiveBonus(IsekaiComponent comp, PassiveBonusType bonusType)
        {
            try
            {
                if (comp?.passiveTree == null) return 0f;
                return comp.passiveTree.GetTotalBonus(bonusType);
            }
            catch { return 0f; }
        }

        /// <summary>Apply passive bonus as a multiplier: val *= (1 + bonus)</summary>
        protected void ApplyPassiveMultiplier(IsekaiComponent comp, ref float val, PassiveBonusType bonusType)
        {
            float pb = GetPassiveBonus(comp, bonusType);
            if (pb != 0f) val *= (1f + pb);
        }

        /// <summary>Apply passive bonus as a flat offset: val += bonus</summary>
        protected void ApplyPassiveOffset(IsekaiComponent comp, ref float val, PassiveBonusType bonusType)
        {
            float pb = GetPassiveBonus(comp, bonusType);
            if (pb != 0f) val += pb;
        }

        /// <summary>Get explanation text for a passive bonus (multiplier style)</summary>
        protected string PassiveMultExplanation(IsekaiComponent comp, PassiveBonusType bonusType)
        {
            float pb = GetPassiveBonus(comp, bonusType);
            if (pb == 0f) return "";
            return $"\n  Isekai Passive: x{(1f + pb):F2}";
        }

        /// <summary>Get explanation text for a passive bonus (offset style)</summary>
        protected string PassiveOffsetExplanation(IsekaiComponent comp, PassiveBonusType bonusType)
        {
            float pb = GetPassiveBonus(comp, bonusType);
            if (pb == 0f) return "";
            return $"\n  Isekai Passive: {pb:+0.0%;-0.0%}";
        }
    }

    // ==================== STRENGTH STAT PARTS ====================
    
    /// <summary>
    /// STR affects melee damage - SSS rank hits like a truck
    /// </summary>
    public class StatPart_IsekaiMeleeDamage : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_MeleeDamage);
            val *= Mathf.Clamp(multiplier, 0.5f, 10f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.MeleeDamage);

            // Warrior class gimmick: Wrath of the Fallen (tier-based)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.WrathOfTheFallen) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.WrathOfTheFallen);
                float wrathBonus = PassiveTreeTracker.CalcWrathOfTheFallen(req.Thing as Pawn, tier);
                if (wrathBonus > 0f)
                    val *= (1f + wrathBonus);
            }

            // Paladin class gimmick: Divine Retribution (stored charge burst)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.DivineRetribution) == true)
            {
                float retributionBonus = comp.passiveTree.CalcDivineRetribution();
                if (retributionBonus > 0f)
                    val *= (1f + retributionBonus);
            }

            // Duelist class gimmick: Counter Strike (dodge-charged melee burst)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.CounterStrike) == true)
            {
                float counterBonus = comp.passiveTree.CalcCounterStrike();
                if (counterBonus > 0f)
                    val *= (1f + counterBonus);
            }

            // Berserker class gimmick: Blood Frenzy (kill-stacking melee boost)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
            {
                float frenzyBonus = comp.passiveTree.CalcBloodFrenzy();
                if (frenzyBonus > 0f)
                    val *= (1f + frenzyBonus);
            }

            // BerserkerBlood trait: HP-threshold melee scaling
            // Below 50% HP: +30% melee damage
            // Below 25% HP: +60% melee damage
            if (IsekaiTraitHelper.HasTrait(req.Thing as Pawn, IsekaiTraitHelper.BerserkerBlood))
            {
                float hpPct = (req.Thing as Pawn)?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                if (hpPct < 0.25f)
                    val *= 1.60f;
                else if (hpPct < 0.50f)
                    val *= 1.30f;
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_MeleeDamage);
            string result = $"Isekai STR ({str:F0}): x{Mathf.Clamp(multiplier, 0.5f, 10f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.MeleeDamage);

            // Warrior class gimmick: Wrath of the Fallen (tier-based)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.WrathOfTheFallen) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.WrathOfTheFallen);
                if (tier > 0)
                {
                    float threshold, maxBonus;
                    switch (tier)
                    {
                        case 1: threshold = 0.50f; maxBonus = 0.25f; break;
                        case 2: threshold = 0.50f; maxBonus = 0.35f; break;
                        case 3: threshold = 0.50f; maxBonus = 0.50f; break;
                        default: threshold = 0.50f; maxBonus = 0.75f; break;
                    }
                    float wrathBonus = PassiveTreeTracker.CalcWrathOfTheFallen(req.Thing as Pawn, tier);
                    result += $"\n  Wrath of the Fallen (Tier {tier}/4): triggers below {threshold:P0} HP, max +{maxBonus:P0}";
                    if (wrathBonus > 0f)
                        result += $" [ACTIVE: x{(1f + wrathBonus):F2}]";
                    else
                    {
                        var pawnHP = req.Thing as Pawn;
                        float hpPct = pawnHP?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                        result += $" [Inactive: HP {hpPct:P0}]";
                    }
                }
            }

            // Paladin class gimmick: Divine Retribution (stored charge burst)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.DivineRetribution) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.DivineRetribution);
                if (tier > 0)
                {
                    int cap;
                    float maxBonus;
                    switch (tier)
                    {
                        case 1: cap = 50;  maxBonus = 0.15f; break;
                        case 2: cap = 75;  maxBonus = 0.30f; break;
                        case 3: cap = 100; maxBonus = 0.45f; break;
                        default: cap = 150; maxBonus = 0.60f; break;
                    }
                    int stored = comp.passiveTree.retributionStoredDamage;
                    float retributionBonus = comp.passiveTree.CalcDivineRetribution();
                    result += $"\n  Divine Retribution (Tier {tier}/4): charge {stored}/{cap}, max +{maxBonus:P0}";
                    if (retributionBonus > 0f)
                        result += $" [CHARGED: x{(1f + retributionBonus):F2}]";
                    else
                        result += " [No charge]";
                }
            }

            // Duelist class gimmick: Counter Strike
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.CounterStrike) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.CounterStrike);
                if (tier > 0)
                {
                    int maxCharges;
                    float perCharge;
                    switch (tier)
                    {
                        case 1: maxCharges = 3; perCharge = 0.07f; break;
                        case 2: maxCharges = 5; perCharge = 0.08f; break;
                        case 3: maxCharges = 7; perCharge = 0.09f; break;
                        default: maxCharges = 10; perCharge = 0.10f; break;
                    }
                    int charges = comp.passiveTree.counterStrikeCharges;
                    float counterBonus = comp.passiveTree.CalcCounterStrike();
                    result += $"\n  Counter Strike (Tier {tier}/4): {charges}/{maxCharges} charges, +{perCharge:P0}/charge";
                    if (counterBonus > 0f)
                        result += $" [CHARGED: x{(1f + counterBonus):F2}]";
                    else
                        result += " [No charges]";
                }
            }

            // Berserker class gimmick: Blood Frenzy
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.BloodFrenzy);
                if (tier > 0)
                {
                    int maxStacks;
                    float perStack;
                    switch (tier)
                    {
                        case 1: maxStacks = 3; perStack = 0.05f; break;
                        case 2: maxStacks = 5; perStack = 0.06f; break;
                        case 3: maxStacks = 7; perStack = 0.07f; break;
                        default: maxStacks = 10; perStack = 0.08f; break;
                    }
                    int stacks = comp.passiveTree.frenzyStacks;
                    float frenzyBonus = comp.passiveTree.CalcBloodFrenzy();
                    result += $"\n  Blood Frenzy (Tier {tier}/4): {stacks}/{maxStacks} stacks, +{perStack:P0}/stack";
                    if (frenzyBonus > 0f)
                        result += $" [FRENZIED: x{(1f + frenzyBonus):F2}]";
                    else
                        result += " [No frenzy — kill to stack]";
                }
            }

            // BerserkerBlood trait: HP-threshold melee scaling
            if (IsekaiTraitHelper.HasTrait(req.Thing as Pawn, IsekaiTraitHelper.BerserkerBlood))
            {
                float hpPct = (req.Thing as Pawn)?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                if (hpPct < 0.25f)
                    result += "\n  Berserker Blood (<25% HP): x1.60";
                else if (hpPct < 0.50f)
                    result += "\n  Berserker Blood (<50% HP): x1.30";
                else
                    result += $"\n  Berserker Blood: [Inactive: HP {hpPct:P0}]";
            }

            return result;
        }
    }

    /// <summary>
    /// STR affects carry capacity - SSS rank can carry an army's worth of gear
    /// </summary>
    public class StatPart_IsekaiCarryCapacity : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_CarryCapacity);
            val *= Mathf.Clamp(multiplier, 0.5f, 10f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.CarryCapacity);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_CarryCapacity);
            return $"Isekai STR ({str:F0}): x{Mathf.Clamp(multiplier, 0.5f, 10f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.CarryCapacity);
        }
    }

    /// <summary>
    /// STR affects mining speed - stronger pawns break rock faster
    /// </summary>
    public class StatPart_IsekaiMiningSpeed : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_MiningSpeed);
            val *= Mathf.Clamp(multiplier, 0.5f, 10f);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_MiningSpeed);
            return $"Isekai STR ({str:F0}): x{Mathf.Clamp(multiplier, 0.5f, 10f):F2}";
        }
    }

    // ==================== DEXTERITY STAT PARTS ====================

    /// <summary>
    /// DEX affects movement speed - configurable per point bonus
    /// </summary>
    public class StatPart_IsekaiMoveSpeed : StatPart_IsekaiBase
    {
        private const float LevelBonusPerLevel = 0.005f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float dex = GetStatValue(comp, "DEX");
            float multiplier = StatToMultiplier(dex, IsekaiLevelingSettings.DEX_MoveSpeed);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.MoveSpeed);

            // Per-level flat bonus (formerly in Patch_StatBonuses Harmony postfix)
            val += LevelBonusPerLevel * comp.currentLevel;

            // Berserker class gimmick: Blood Frenzy (kill-stacking speed boost)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
            {
                float frenzyBonus = comp.passiveTree.CalcBloodFrenzy();
                if (frenzyBonus > 0f)
                    val *= (1f + frenzyBonus);
            }

            // BerserkerBlood trait: HP-threshold move speed scaling
            // Below 50% HP: +15% move speed
            // Below 25% HP: +30% move speed
            if (IsekaiTraitHelper.HasTrait(req.Thing as Pawn, IsekaiTraitHelper.BerserkerBlood))
            {
                float hpPct = (req.Thing as Pawn)?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                if (hpPct < 0.25f)
                    val *= 1.30f;
                else if (hpPct < 0.50f)
                    val *= 1.15f;
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float dex = GetStatValue(comp, "DEX");
            float multiplier = StatToMultiplier(dex, IsekaiLevelingSettings.DEX_MoveSpeed);
            string result = $"Isekai DEX ({dex:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.MoveSpeed);

            float levelBonus = LevelBonusPerLevel * comp.currentLevel;
            if (levelBonus > 0f)
                result += $"\n  Isekai Level {comp.currentLevel}: +{levelBonus:F2}";

            // Berserker class gimmick: Blood Frenzy
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.BloodFrenzy);
                if (tier > 0)
                {
                    float frenzyBonus = comp.passiveTree.CalcBloodFrenzy();
                    if (frenzyBonus > 0f)
                        result += $"\n  Blood Frenzy (MoveSpeed, Tier {tier}/4): x{(1f + frenzyBonus):F2}";
                }
            }

            // BerserkerBlood trait: HP-threshold move speed scaling
            if (IsekaiTraitHelper.HasTrait(req.Thing as Pawn, IsekaiTraitHelper.BerserkerBlood))
            {
                float hpPct = (req.Thing as Pawn)?.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                if (hpPct < 0.25f)
                    result += "\n  Berserker Blood (<25% HP): x1.30";
                else if (hpPct < 0.50f)
                    result += "\n  Berserker Blood (<50% HP): x1.15";
                else
                    result += $"\n  Berserker Blood: [Inactive: HP {hpPct:P0}]";
            }

            return result;
        }
    }

    /// <summary>
    /// DEX affects melee dodge chance
    /// </summary>
    public class StatPart_IsekaiMeleeDodge : StatPart_IsekaiBase
    {
        private const float LevelBonusPerLevel = 0.005f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float dex = GetStatValue(comp, "DEX");
            float bonus = StatToOffset(dex, IsekaiLevelingSettings.DEX_MeleeDodge);
            val += Mathf.Clamp(bonus, -0.1f, 0.95f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.MeleeDodge);

            // Per-level flat bonus (formerly in Patch_StatBonuses Harmony postfix)
            val += LevelBonusPerLevel * comp.currentLevel;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float dex = GetStatValue(comp, "DEX");
            float bonus = StatToOffset(dex, IsekaiLevelingSettings.DEX_MeleeDodge);
            string result = $"Isekai DEX ({dex:F0}): {Mathf.Clamp(bonus, -0.1f, 0.95f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.MeleeDodge);

            float levelBonus = LevelBonusPerLevel * comp.currentLevel;
            if (levelBonus > 0f)
                result += $"\n  Isekai Level {comp.currentLevel}: +{levelBonus:F3}";

            return result;
        }
    }

    /// <summary>
    /// DEX affects ranged accuracy - dedicated shooting accuracy setting
    /// </summary>
    public class StatPart_IsekaiShootingAccuracy : StatPart_IsekaiBase
    {
        private const float LevelBonusPerLevel = 0.005f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            float dex = GetStatValue(comp, "DEX");
            float bonus = StatToOffset(dex, IsekaiLevelingSettings.DEX_ShootingAccuracy);
            val += Mathf.Clamp(bonus, -0.1f, 0.95f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.ShootingAccuracy);

            // Per-level flat bonus (formerly in Patch_StatBonuses Harmony postfix)
            val += LevelBonusPerLevel * comp.currentLevel;

            // Ranger class gimmick: Predator Focus (stacking accuracy from ranged hits)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PredatorFocus) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PredatorFocus);
                if (tier > 0)
                {
                    float focusBonus = comp.passiveTree.CalcPredatorFocus();
                    if (focusBonus > 0f)
                        val += focusBonus;
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float dex = GetStatValue(comp, "DEX");
            float bonus = StatToOffset(dex, IsekaiLevelingSettings.DEX_ShootingAccuracy);
            string result = $"Isekai DEX ({dex:F0}): {Mathf.Clamp(bonus, -0.1f, 0.95f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.ShootingAccuracy);

            float levelBonus = LevelBonusPerLevel * comp.currentLevel;
            if (levelBonus > 0f)
                result += $"\n  Isekai Level {comp.currentLevel}: +{levelBonus:F3}";

            // Ranger class gimmick: Predator Focus
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PredatorFocus) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PredatorFocus);
                if (tier > 0)
                {
                    int maxStacks;
                    float perStack;
                    switch (tier)
                    {
                        case 1: maxStacks = 3; perStack = 0.04f; break;
                        case 2: maxStacks = 4; perStack = 0.05f; break;
                        case 3: maxStacks = 5; perStack = 0.06f; break;
                        default: maxStacks = 7; perStack = 0.07f; break;
                    }
                    int stacks = comp.passiveTree.huntMarkStacks;
                    float focusBonus = comp.passiveTree.CalcPredatorFocus();
                    result += $"\n  Predator Focus (Tier {tier}/4): {stacks}/{maxStacks} stacks, +{perStack:P0}/stack";
                    if (focusBonus > 0f)
                        result += $" [LOCKED ON: +{focusBonus:P0}]";
                    else
                        result += " [No target]";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// DEX affects melee hit chance - SSS rank almost never misses
    /// </summary>
    public class StatPart_IsekaiMeleeHitChance : StatPart_IsekaiBase
    {
        private const float LevelBonusPerLevel = 0.005f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float dex = GetStatValue(comp, "DEX");
            float bonus = StatToOffset(dex, IsekaiLevelingSettings.DEX_MeleeHitChance);
            val += Mathf.Clamp(bonus, -0.1f, 0.95f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.MeleeHitChance);

            // Per-level flat bonus (formerly in Patch_StatBonuses Harmony postfix)
            val += LevelBonusPerLevel * comp.currentLevel;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float dex = GetStatValue(comp, "DEX");
            float bonus = StatToOffset(dex, IsekaiLevelingSettings.DEX_MeleeHitChance);
            string result = $"Isekai DEX ({dex:F0}): {Mathf.Clamp(bonus, -0.1f, 0.95f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.MeleeHitChance);

            float levelBonus = LevelBonusPerLevel * comp.currentLevel;
            if (levelBonus > 0f)
                result += $"\n  Isekai Level {comp.currentLevel}: +{levelBonus:F3}";

            return result;
        }
    }

    /// <summary>
    /// DEX reduces aiming delay - SSS rank aims almost instantly (lower is better)
    /// </summary>
    public class StatPart_IsekaiAimingDelay : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float dex = GetStatValue(comp, "DEX");
            // Reduction per point from settings
            float multiplier = 1f - (dex - 5f) * IsekaiLevelingSettings.DEX_AimingTime;
            val *= Mathf.Clamp(multiplier, 0.05f, 1.5f);
            // Passive AimingDelay: positive XML values = reduction (good), so negate
            float pb = GetPassiveBonus(comp, PassiveBonusType.AimingDelay);
            if (pb != 0f) val *= (1f - pb);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float dex = GetStatValue(comp, "DEX");
            float multiplier = 1f - (dex - 5f) * IsekaiLevelingSettings.DEX_AimingTime;
            float pb = GetPassiveBonus(comp, PassiveBonusType.AimingDelay);
            string passiveStr = pb != 0f ? $"\n  Isekai Passive: x{(1f - pb):F2}" : "";
            return $"Isekai DEX ({dex:F0}): x{Mathf.Clamp(multiplier, 0.05f, 1.5f):F2}" + passiveStr;
        }
    }

    /// <summary>
    /// INT affects work speed - smart pawns work more efficiently
    /// </summary>
    public class StatPart_IsekaiWorkSpeed : StatPart_IsekaiBase
    {
        private const float LevelBonusPerLevel = 0.01f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, IsekaiLevelingSettings.INT_WorkSpeed);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.WorkSpeed);

            // Per-level flat bonus (formerly in Patch_StatBonuses Harmony postfix)
            val += LevelBonusPerLevel * comp.currentLevel;

            // Crafter class gimmick: Masterwork Insight (full strength on work speed)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.MasterworkInsight) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.MasterworkInsight);
                if (tier > 0)
                {
                    float insightBonus = PassiveTreeTracker.CalcMasterworkInsight(req.Thing as Pawn, tier);
                    if (insightBonus > 0f)
                        val *= (1f + insightBonus);
                }
            }

            // Alchemist class gimmick: Eureka Synthesis (half strength on work speed)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.EurekaSynthesis) == true)
            {
                float eurekaBonus = comp.passiveTree.CalcEurekaSynthesis();
                if (eurekaBonus > 0f)
                    val *= (1f + eurekaBonus * 0.5f);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, IsekaiLevelingSettings.INT_WorkSpeed);
            string result = $"Isekai INT ({intel:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.WorkSpeed);

            float levelBonus = LevelBonusPerLevel * comp.currentLevel;
            if (levelBonus > 0f)
                result += $"\n  Isekai Level {comp.currentLevel}: +{levelBonus:F2}";

            // Crafter class gimmick: Masterwork Insight
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.MasterworkInsight) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.MasterworkInsight);
                if (tier > 0)
                {
                    float insightBonus = PassiveTreeTracker.CalcMasterworkInsight(req.Thing as Pawn, tier);
                    if (insightBonus > 0f)
                        result += $"\n  Masterwork Insight (Tier {tier}/4): x{(1f + insightBonus):F2}";
                }
            }

            // Alchemist class gimmick: Eureka Synthesis (half strength)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.EurekaSynthesis) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.EurekaSynthesis);
                if (tier > 0)
                {
                    float eurekaBonus = comp.passiveTree.CalcEurekaSynthesis();
                    if (eurekaBonus > 0f)
                        result += $"\n  Eureka Synthesis (WorkSpeed, Tier {tier}/4, x0.5): x{(1f + eurekaBonus * 0.5f):F2}";
                }
            }

            return result;
        }
    }

    // ==================== VITALITY STAT PARTS ======================================

    /// <summary>
    /// VIT affects health regen (injury healing factor)
    /// </summary>
    public class StatPart_IsekaiHealthRegen : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            float multiplier = StatToMultiplier(vit, IsekaiLevelingSettings.VIT_HealthRegen);
            val *= Mathf.Clamp(multiplier, 0.5f, 10f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.HealthRegen);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float multiplier = StatToMultiplier(vit, IsekaiLevelingSettings.VIT_HealthRegen);
            return $"Isekai VIT ({vit:F0}): x{Mathf.Clamp(multiplier, 0.5f, 10f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.HealthRegen);
        }
    }

    /// <summary>
    /// VIT affects toxic resistance
    /// </summary>
    public class StatPart_IsekaiToxicResist : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            float bonus = StatToOffset(vit, IsekaiLevelingSettings.VIT_ToxicResist);
            val += Mathf.Clamp(bonus, -0.1f, 0.95f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.ToxicResist);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float bonus = StatToOffset(vit, IsekaiLevelingSettings.VIT_ToxicResist);
            return $"Isekai VIT ({vit:F0}): {Mathf.Clamp(bonus, -0.1f, 0.95f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.ToxicResist);
        }
    }

    /// <summary>
    /// VIT affects immunity gain speed (how fast pawns fight off diseases like plague, flu, malaria)
    /// </summary>
    public class StatPart_IsekaiImmunityGain : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            float multiplier = StatToMultiplier(vit, IsekaiLevelingSettings.VIT_ImmunityGain);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.ImmunityGain);

            // Survivor class gimmick: Unyielding Spirit (full strength on immunity gain)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.UnyieldingSpirit) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.UnyieldingSpirit);
                if (tier > 0)
                {
                    float spiritBonus = PassiveTreeTracker.CalcUnyieldingSpirit(req.Thing as Pawn, tier);
                    if (spiritBonus > 0f)
                        val *= (1f + spiritBonus);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float multiplier = StatToMultiplier(vit, IsekaiLevelingSettings.VIT_ImmunityGain);
            string result = $"Isekai VIT ({vit:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.ImmunityGain);

            // Survivor class gimmick: Unyielding Spirit
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.UnyieldingSpirit) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.UnyieldingSpirit);
                if (tier > 0)
                {
                    float spiritBonus = PassiveTreeTracker.CalcUnyieldingSpirit(req.Thing as Pawn, tier);
                    if (spiritBonus > 0f)
                        result += $"\n  Unyielding Spirit (Tier {tier}/4): x{(1f + spiritBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// VIT reduces incoming damage (lower = better, so high VIT = lower value)
    /// Uses diminishing returns formula: dmgMult = 1 / (1 + effectiveVIT * rate)
    /// </summary>
    public class StatPart_IsekaiIncomingDamage : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            // Diminishing returns: each point still helps, but stacking gives less and less
            // VIT 30 → ~20% reduction, VIT 50 → ~31%, VIT 100 → ~49%, VIT 200 → ~66%
            float effectiveVIT = Mathf.Max(0f, vit - 5f);
            float rate = IsekaiLevelingSettings.VIT_DamageReduction;
            float damageMultiplier = 1f / (1f + effectiveVIT * rate);
            val *= Mathf.Clamp(damageMultiplier, 0.05f, 1.1f); // Minimum 5% damage taken (max 95% reduction, but requires extreme VIT)
            // Passive tree DamageReduction — diminishing returns (same formula as VIT)
            // MaxHealth passive now applied to actual HealthScale, not here
            float passiveDR = GetPassiveBonus(comp, PassiveBonusType.DamageReduction);
            if (passiveDR != 0f)
            {
                // Scale factor of 2 so +50% DR passive → x0.50 damage, +100% → x0.33
                float passiveMult = 1f / (1f + Mathf.Max(0f, passiveDR) * 2f);
                val *= Mathf.Clamp(passiveMult, 0.05f, 2f);
                // Negative DR (tradeoffs) increases damage taken
                if (passiveDR < 0f) val *= (1f - passiveDR);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float effectiveVIT = Mathf.Max(0f, vit - 5f);
            float rate = IsekaiLevelingSettings.VIT_DamageReduction;
            float damageMultiplier = 1f / (1f + effectiveVIT * rate);
            string result = $"Isekai VIT ({vit:F0}): x{Mathf.Clamp(damageMultiplier, 0.05f, 1.1f):F2}";
            float passiveDR = GetPassiveBonus(comp, PassiveBonusType.DamageReduction);
            if (passiveDR > 0f)
            {
                float passiveMult = 1f / (1f + passiveDR * 2f);
                result += $"\n  Isekai Passive: x{Mathf.Clamp(passiveMult, 0.05f, 2f):F2}";
            }
            else if (passiveDR < 0f)
            {
                result += $"\n  Isekai Passive: x{(1f - passiveDR):F2}";
            }
            return result;
        }
    }

    /// <summary>
    /// VIT provides natural sharp armor - SSS rank gets massive armor
    /// </summary>
    public class StatPart_IsekaiArmorSharp : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            // Each VIT point = +0.8% natural sharp armor
            // 100 VIT = (100-5)*0.008 = 76% natural sharp armor!
            float bonus = StatToOffset(vit, 0.008f);
            val += Mathf.Clamp(bonus, 0f, 0.80f); // Max 80% natural armor
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.SharpArmor);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float bonus = StatToOffset(vit, 0.008f);
            return $"Isekai VIT ({vit:F0}): {Mathf.Clamp(bonus, 0f, 0.80f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.SharpArmor);
        }
    }

    /// <summary>
    /// VIT provides natural blunt armor - SSS rank is nearly immune
    /// </summary>
    public class StatPart_IsekaiArmorBlunt : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            // Each VIT point = +1% natural blunt armor (blunt is easier to resist)
            // 100 VIT = (100-5)*0.01 = 95% natural blunt armor!
            float bonus = StatToOffset(vit, 0.01f);
            val += Mathf.Clamp(bonus, 0f, 0.95f); // Max 95% natural armor
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.BluntArmor);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float bonus = StatToOffset(vit, 0.01f);
            return $"Isekai VIT ({vit:F0}): {Mathf.Clamp(bonus, 0f, 0.95f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.BluntArmor);
        }
    }

    /// <summary>
    /// VIT provides natural heat armor - SSS rank shrugs off fire
    /// </summary>
    public class StatPart_IsekaiArmorHeat : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            // Each VIT point = +0.7% natural heat armor
            // 100 VIT = (100-5)*0.007 = 66.5% natural heat armor
            float bonus = StatToOffset(vit, 0.007f);
            val += Mathf.Clamp(bonus, 0f, 0.70f); // Max 70% natural heat armor
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.HeatArmor);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float bonus = StatToOffset(vit, 0.007f);
            return $"Isekai VIT ({vit:F0}): {Mathf.Clamp(bonus, 0f, 0.70f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.HeatArmor);
        }
    }

    /// <summary>
    /// VIT increases pain shock threshold proportional to health multiplier.
    /// This ensures pawns go down from pain at roughly the same %HP as a base pawn.
    /// Without this, high-VIT pawns faint with most of their HP remaining because
    /// pain scales with absolute injury severity, not relative to max HP.
    /// Formula: bonus = (healthMult - 1) * baseThreshold (0.8)
    /// VIT 30 → 2.0x HP → +0.80 threshold (1.60)
    /// VIT 50 → 2.8x HP → +1.44 threshold (2.24)
    /// VIT 100 → 4.8x HP → +3.04 threshold (3.84)
    /// </summary>
    public class StatPart_IsekaiPainShock : StatPart_IsekaiBase
    {
        private const float BASE_THRESHOLD = 0.80f;

        /// <summary>
        /// Compute the VIT+passive health multiplier (mirrors Patch_HealthBonus logic).
        /// Uses raw VIT (not trait-adjusted) to match actual HP from Patch_HealthBonus.
        /// </summary>
        private float GetHealthMultiplier(IsekaiComponent comp)
        {
            float vit = comp.stats.vitality;
            float multiplier = 1f;
            if (vit > 5f)
                multiplier += (vit - 5f) * IsekaiLevelingSettings.VIT_MaxHealth;
            if (comp.passiveTree != null)
            {
                float passiveMaxHP = comp.passiveTree.GetTotalBonus(PassiveBonusType.MaxHealth);
                if (passiveMaxHP != 0f) multiplier += passiveMaxHP;
            }
            return Mathf.Max(multiplier, 1f);
        }

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            float healthMult = GetHealthMultiplier(comp);
            // Scale pain threshold proportionally to HP so pawns go down
            // at approximately the same %HP as an unbuffed pawn.
            float bonus = (healthMult - 1f) * BASE_THRESHOLD;
            val += Mathf.Max(bonus, 0f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.PainThreshold);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float healthMult = GetHealthMultiplier(comp);
            float bonus = (healthMult - 1f) * BASE_THRESHOLD;
            bonus = Mathf.Max(bonus, 0f);
            return $"Isekai VIT (x{healthMult:F1} HP): {bonus:+0.00;-0.00}" + PassiveOffsetExplanation(comp, PassiveBonusType.PainThreshold);
        }
    }

    /// <summary>
    /// VIT improves rest rate - SSS rank sleeps more efficiently (needs less sleep)
    /// </summary>
    public class StatPart_IsekaiRestRate : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            // Each VIT point = +1.5% rest rate
            // 100 VIT = 1.0 + 95*0.015 = x2.43 rest rate (sleeps twice as fast)
            float multiplier = StatToMultiplier(vit, 0.015f);
            val *= Mathf.Clamp(multiplier, 0.5f, 3f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.RestRate);

            // Survivor class gimmick: Unyielding Spirit (full strength on rest rate)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.UnyieldingSpirit) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.UnyieldingSpirit);
                if (tier > 0)
                {
                    float spiritBonus = PassiveTreeTracker.CalcUnyieldingSpirit(req.Thing as Pawn, tier);
                    if (spiritBonus > 0f)
                        val *= (1f + spiritBonus);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float multiplier = StatToMultiplier(vit, 0.015f);
            string result = $"Isekai VIT ({vit:F0}): x{Mathf.Clamp(multiplier, 0.5f, 3f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.RestRate);

            // Survivor class gimmick: Unyielding Spirit
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.UnyieldingSpirit) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.UnyieldingSpirit);
                if (tier > 0)
                {
                    float spiritBonus = PassiveTreeTracker.CalcUnyieldingSpirit(req.Thing as Pawn, tier);
                    if (spiritBonus > 0f)
                        result += $"\n  Unyielding Spirit (Tier {tier}/4): x{(1f + spiritBonus):F2}";
                }
            }

            return result;
        }
    }

    // ==================== INTELLIGENCE STAT PARTS ======================================

    /// <summary>
    /// INT affects research speed
    /// </summary>
    public class StatPart_IsekaiResearchSpeed : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, IsekaiLevelingSettings.INT_ResearchSpeed);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.ResearchSpeed);

            // Mage class gimmick: Arcane Overflow (half strength on research speed)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.ArcaneOverflow) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.ArcaneOverflow);
                if (tier > 0)
                {
                    float arcaneBonus = PassiveTreeTracker.CalcArcaneOverflow(req.Thing as Pawn, tier);
                    if (arcaneBonus > 0f)
                        val *= (1f + arcaneBonus * 0.5f);
                }
            }

            // Sage class gimmick: Inner Calm (half strength on research speed)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                if (tier > 0)
                {
                    float calmBonus = PassiveTreeTracker.CalcInnerCalm(comp.passiveTree.lastHitTick, tier);
                    if (calmBonus > 0f)
                        val *= (1f + calmBonus * 0.5f);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, IsekaiLevelingSettings.INT_ResearchSpeed);
            string result = $"Isekai INT ({intel:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.ResearchSpeed);

            // Mage class gimmick: Arcane Overflow (half strength on research)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.ArcaneOverflow) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.ArcaneOverflow);
                if (tier > 0)
                {
                    float arcaneBonus = PassiveTreeTracker.CalcArcaneOverflow(req.Thing as Pawn, tier);
                    if (arcaneBonus > 0f)
                        result += $"\n  Arcane Overflow (Research, Tier {tier}/4): x{(1f + arcaneBonus * 0.5f):F2}";
                }
            }

            // Sage class gimmick: Inner Calm (half strength on research)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                if (tier > 0)
                {
                    float calmBonus = PassiveTreeTracker.CalcInnerCalm(comp.passiveTree.lastHitTick, tier);
                    if (calmBonus > 0f)
                        result += $"\n  Inner Calm (Research, Tier {tier}/4): x{(1f + calmBonus * 0.5f):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// INT affects learning speed
    /// </summary>
    public class StatPart_IsekaiLearningSpeed : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, IsekaiLevelingSettings.INT_LearningSpeed);
            val *= Mathf.Clamp(multiplier, 0.5f, 100f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.LearningSpeed);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, IsekaiLevelingSettings.INT_LearningSpeed);
            return $"Isekai INT ({intel:F0}): x{Mathf.Clamp(multiplier, 0.5f, 100f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.LearningSpeed);
        }
    }

    /// <summary>
    /// INT affects hacking speed - smart pawns hack faster
    /// </summary>
    public class StatPart_IsekaiHackingSpeed : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, 0.02f);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float intel = GetStatValue(comp, "INT");
            float multiplier = StatToMultiplier(intel, 0.02f);
            return $"Isekai INT ({intel:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}";
        }
    }

    // ==================== WISDOM STAT PARTS ====================

    /// <summary>
    /// WIS affects mental break threshold
    /// </summary>
    public class StatPart_IsekaiMentalBreakThreshold : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            // High WIS = lower mental break threshold (more stable)
            float reduction = StatToOffset(wis, IsekaiLevelingSettings.WIS_MentalBreak);
            val -= Mathf.Clamp(reduction, -0.1f, 0.25f);
            // Passive: positive MentalBreakThreshold = lowers threshold (more stable)
            float passive = GetPassiveBonus(comp, PassiveBonusType.MentalBreakThreshold);
            if (passive != 0f) val -= passive;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float wis = GetStatValue(comp, "WIS");
            float reduction = StatToOffset(wis, IsekaiLevelingSettings.WIS_MentalBreak);
            string result = $"Isekai WIS ({wis:F0}): {-Mathf.Clamp(reduction, -0.1f, 0.25f):+0.0%;-0.0%}";
            float passive = GetPassiveBonus(comp, PassiveBonusType.MentalBreakThreshold);
            if (passive != 0f) result += $"\n  Isekai Passive: {-passive:+0.0%;-0.0%}";
            return result;
        }
    }

    /// <summary>
    /// WIS affects meditation focus gain
    /// </summary>
    public class StatPart_IsekaiMeditationFocus : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            float bonus = StatToOffset(wis, IsekaiLevelingSettings.WIS_MeditationFocus);
            val += Mathf.Clamp(bonus, -0.1f, 1.5f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.MeditationFocus);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float wis = GetStatValue(comp, "WIS");
            float bonus = StatToOffset(wis, IsekaiLevelingSettings.WIS_MeditationFocus);
            return $"Isekai WIS ({wis:F0}): {Mathf.Clamp(bonus, -0.1f, 1.5f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.MeditationFocus);
        }
    }

    // ==================== WISDOM NON-PSYCHIC STAT PARTS ======================================

    /// <summary>
    /// WIS affects medical tend quality - wise pawns are more perceptive healers
    /// </summary>
    public class StatPart_IsekaiMedicalTendQuality : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            float bonus = StatToOffset(wis, IsekaiLevelingSettings.WIS_MedicalTendQuality);
            val += Mathf.Clamp(bonus, -0.1f, 0.5f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.TendQuality);

            // Sage class gimmick: Inner Calm (full strength on tend quality)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                if (tier > 0)
                {
                    float calmBonus = PassiveTreeTracker.CalcInnerCalm(comp.passiveTree.lastHitTick, tier);
                    if (calmBonus > 0f)
                        val *= (1f + calmBonus);
                }
            }

            // Alchemist class gimmick: Eureka Synthesis (full strength on tend quality)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.EurekaSynthesis) == true)
            {
                float eurekaBonus = comp.passiveTree.CalcEurekaSynthesis();
                if (eurekaBonus > 0f)
                    val *= (1f + eurekaBonus);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float wis = GetStatValue(comp, "WIS");
            float bonus = StatToOffset(wis, IsekaiLevelingSettings.WIS_MedicalTendQuality);
            string result = $"Isekai WIS ({wis:F0}): {Mathf.Clamp(bonus, -0.1f, 0.5f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.TendQuality);

            // Sage class gimmick: Inner Calm (full strength on tend quality)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                if (tier > 0)
                {
                    float calmBonus = PassiveTreeTracker.CalcInnerCalm(comp.passiveTree.lastHitTick, tier);
                    if (calmBonus > 0f)
                        result += $"\n  Inner Calm (TendQ, Tier {tier}/4): x{(1f + calmBonus):F2}";
                }
            }

            // Alchemist class gimmick: Eureka Synthesis
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.EurekaSynthesis) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.EurekaSynthesis);
                if (tier > 0)
                {
                    float eurekaBonus = comp.passiveTree.CalcEurekaSynthesis();
                    if (eurekaBonus > 0f)
                        result += $"\n  Eureka Synthesis (TendQ, Tier {tier}/4): x{(1f + eurekaBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// WIS affects medical surgery success - perceptive pawns make fewer mistakes
    /// </summary>
    public class StatPart_IsekaiSurgerySuccess : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_SurgerySuccess);
            val *= Mathf.Clamp(multiplier, 0.5f, 4f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.SurgerySuccess);

            // Sage class gimmick: Inner Calm (full strength on surgery success)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                if (tier > 0)
                {
                    float calmBonus = PassiveTreeTracker.CalcInnerCalm(comp.passiveTree.lastHitTick, tier);
                    if (calmBonus > 0f)
                        val *= (1f + calmBonus);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_SurgerySuccess);
            string result = $"Isekai WIS ({wis:F0}): x{Mathf.Clamp(multiplier, 0.5f, 4f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.SurgerySuccess);

            // Sage class gimmick: Inner Calm (full strength on surgery success)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                if (tier > 0)
                {
                    float calmBonus = PassiveTreeTracker.CalcInnerCalm(comp.passiveTree.lastHitTick, tier);
                    if (calmBonus > 0f)
                        result += $"\n  Inner Calm (Surgery, Tier {tier}/4): x{(1f + calmBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// WIS affects train animal chance - patient, perceptive pawns train animals better
    /// </summary>
    public class StatPart_IsekaiTrainAnimal : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_TrainAnimal);
            val *= Mathf.Clamp(multiplier, 0.5f, 3f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.TrainAnimal);

            // Beastmaster class gimmick: Pack Alpha (bonded animal bonus)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                if (packBonus > 0f)
                    val *= (1f + packBonus);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_TrainAnimal);
            string result = $"Isekai WIS ({wis:F0}): x{Mathf.Clamp(multiplier, 0.5f, 3f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.TrainAnimal);

            // Beastmaster class gimmick: Pack Alpha
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PackAlpha);
                if (tier > 0)
                {
                    float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                    if (packBonus > 0f)
                        result += $"\n  Pack Alpha (TrainAnimal, Tier {tier}/4): x{(1f + packBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// WIS affects animal gather yield - gentle, attentive handling produces more resources
    /// </summary>
    public class StatPart_IsekaiAnimalGatherYield : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_AnimalGatherYield);
            val *= Mathf.Clamp(multiplier, 0.5f, 3f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.GatherYield);

            // Beastmaster class gimmick: Pack Alpha (bonded animal bonus)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                if (packBonus > 0f)
                    val *= (1f + packBonus);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_AnimalGatherYield);
            string result = $"Isekai WIS ({wis:F0}): x{Mathf.Clamp(multiplier, 0.5f, 3f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.GatherYield);

            // Beastmaster class gimmick: Pack Alpha
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PackAlpha);
                if (tier > 0)
                {
                    float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                    if (packBonus > 0f)
                        result += $"\n  Pack Alpha (GatherYield, Tier {tier}/4): x{(1f + packBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// GatherYield constellation bonus also affects plant harvest yield.
    /// No base-stat (WIS) scaling — purely passive-tree driven.
    /// </summary>
    public class StatPart_IsekaiPlantHarvestYield : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.GatherYield);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            string passive = PassiveMultExplanation(comp, PassiveBonusType.GatherYield);
            return string.IsNullOrEmpty(passive) ? null : passive;
        }
    }

    // ==================== CHARISMA STAT PARTS ======================================

    /// <summary>
    /// CHA affects social impact
    /// </summary>
    public class StatPart_IsekaiSocialImpact : StatPart_IsekaiBase
    {
        private const float LevelBonusPerLevel = 0.005f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, IsekaiLevelingSettings.CHA_SocialImpact);
            val *= Mathf.Clamp(multiplier, 0.5f, 4f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.SocialImpact);

            // Per-level flat bonus (formerly in Patch_StatBonuses Harmony postfix)
            val += LevelBonusPerLevel * comp.currentLevel;

            // Leader class gimmick: Rallying Presence (full strength on social impact)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                if (tier > 0)
                {
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(req.Thing as Pawn, tier);
                    if (rallyBonus > 0f)
                        val *= (1f + rallyBonus);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, IsekaiLevelingSettings.CHA_SocialImpact);
            string result = $"Isekai CHA ({cha:F0}): x{Mathf.Clamp(multiplier, 0.5f, 4f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.SocialImpact);

            float levelBonus = LevelBonusPerLevel * comp.currentLevel;
            if (levelBonus > 0f)
                result += $"\n  Isekai Level {comp.currentLevel}: +{levelBonus:F3}";

            // Leader class gimmick: Rallying Presence
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                if (tier > 0)
                {
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(req.Thing as Pawn, tier);
                    if (rallyBonus > 0f)
                        result += $"\n  Rallying Presence (Tier {tier}/4): x{(1f + rallyBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// CHA affects negotiation ability
    /// </summary>
    public class StatPart_IsekaiNegotiation : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, IsekaiLevelingSettings.CHA_NegotiationAbility);
            val *= Mathf.Clamp(multiplier, 0.5f, 4f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.Negotiation);

            // Leader class gimmick: Rallying Presence (full strength on negotiation)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                if (tier > 0)
                {
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(req.Thing as Pawn, tier);
                    if (rallyBonus > 0f)
                        val *= (1f + rallyBonus);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, IsekaiLevelingSettings.CHA_NegotiationAbility);
            string result = $"Isekai CHA ({cha:F0}): x{Mathf.Clamp(multiplier, 0.5f, 4f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.Negotiation);

            // Leader class gimmick: Rallying Presence
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                if (tier > 0)
                {
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(req.Thing as Pawn, tier);
                    if (rallyBonus > 0f)
                        result += $"\n  Rallying Presence (Tier {tier}/4): x{(1f + rallyBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// CHA affects trade price improvement
    /// </summary>
    public class StatPart_IsekaiTradePrices : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float cha = GetStatValue(comp, "CHA");
            float bonus = StatToOffset(cha, IsekaiLevelingSettings.CHA_TradePrice);
            val += Mathf.Clamp(bonus, -0.05f, 0.3f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.TradePrice);

            // Leader class gimmick: Rallying Presence (half strength on trade prices, offset style)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                if (tier > 0)
                {
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(req.Thing as Pawn, tier);
                    if (rallyBonus > 0f)
                        val += rallyBonus * 0.5f;
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float cha = GetStatValue(comp, "CHA");
            float bonus = StatToOffset(cha, IsekaiLevelingSettings.CHA_TradePrice);
            string result = $"Isekai CHA ({cha:F0}): {Mathf.Clamp(bonus, -0.05f, 0.3f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.TradePrice);

            // Leader class gimmick: Rallying Presence (half strength)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                if (tier > 0)
                {
                    float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(req.Thing as Pawn, tier);
                    if (rallyBonus > 0f)
                        result += $"\n  Rallying Presence (Tier {tier}/4): +{(rallyBonus * 0.5f):P0}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// CHA affects animal taming
    /// </summary>
    public class StatPart_IsekaiTameAnimal : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, 0.015f);
            val *= Mathf.Clamp(multiplier, 0.5f, 3f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.Taming);

            // Beastmaster class gimmick: Pack Alpha (bonded animal bonus)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                if (packBonus > 0f)
                    val *= (1f + packBonus);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, 0.015f);
            string result = $"Isekai CHA ({cha:F0}): x{Mathf.Clamp(multiplier, 0.5f, 3f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.Taming);

            // Beastmaster class gimmick: Pack Alpha
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PackAlpha);
                if (tier > 0)
                {
                    float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                    if (packBonus > 0f)
                        result += $"\n  Pack Alpha (Taming, Tier {tier}/4): x{(1f + packBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// CHA + Beastmaster tree affects bond animal chance factor.
    /// Base CHA gives a small bonus; Beastmaster constellation nodes add more via BondChance passive.
    /// </summary>
    public class StatPart_IsekaiBondAnimal : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, 0.01f);
            val *= Mathf.Clamp(multiplier, 0.5f, 3f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.BondChance);

            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                if (packBonus > 0f)
                    val *= (1f + packBonus);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float cha = GetStatValue(comp, "CHA");
            float multiplier = StatToMultiplier(cha, 0.01f);
            string result = $"Isekai CHA ({cha:F0}): x{Mathf.Clamp(multiplier, 0.5f, 3f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.BondChance);

            if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PackAlpha);
                if (tier > 0)
                {
                    float packBonus = comp.passiveTree.CalcPackAlpha(req.Thing as Pawn);
                    if (packBonus > 0f)
                        result += $"\n  Pack Alpha (Bond, Tier {tier}/4): x{(1f + packBonus):F2}";
                }
            }

            return result;
        }
    }

    /// <summary>
    /// CHA affects arrest success
    /// </summary>
    public class StatPart_IsekaiArrest : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float cha = GetStatValue(comp, "CHA");
            float bonus = StatToOffset(cha, IsekaiLevelingSettings.CHA_ArrestSuccess);
            val += Mathf.Clamp(bonus, -0.1f, 0.5f);
            ApplyPassiveOffset(comp, ref val, PassiveBonusType.ArrestSuccess);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float cha = GetStatValue(comp, "CHA");
            float bonus = StatToOffset(cha, IsekaiLevelingSettings.CHA_ArrestSuccess);
            return $"Isekai CHA ({cha:F0}): {Mathf.Clamp(bonus, -0.1f, 0.5f):+0.0%;-0.0%}" + PassiveOffsetExplanation(comp, PassiveBonusType.ArrestSuccess);
        }
    }

    // ==================== PSYCAST STAT PARTS ====================
    
    /// <summary>
    /// INT + WIS affects Psychic Sensitivity
    /// Higher sensitivity = stronger psychic effects (both positive and negative)
    /// </summary>
    public class StatPart_IsekaiPsychicSensitivity : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;

            float intel = GetStatValue(comp, "INT");
            float wis = GetStatValue(comp, "WIS");
            float combined = (intel + wis) / 2f;
            float multiplier = StatToMultiplier(combined, IsekaiLevelingSettings.WIS_PsychicSensitivity);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);

            // Mage class gimmick: Arcane Overflow (primary effect)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.ArcaneOverflow) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.ArcaneOverflow);
                if (tier > 0)
                {
                    float arcaneBonus = PassiveTreeTracker.CalcArcaneOverflow(req.Thing as Pawn, tier);
                    if (arcaneBonus > 0f)
                        val *= (1f + arcaneBonus);
                }
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;

            float intel = GetStatValue(comp, "INT");
            float wis = GetStatValue(comp, "WIS");
            float combined = (intel + wis) / 2f;
            float multiplier = StatToMultiplier(combined, IsekaiLevelingSettings.WIS_PsychicSensitivity);
            string result = $"Isekai INT+WIS ({combined:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}";

            // Mage class gimmick: Arcane Overflow (primary effect)
            if (comp.passiveTree?.HasGimmick(ClassGimmickType.ArcaneOverflow) == true)
            {
                int tier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.ArcaneOverflow);
                if (tier > 0)
                {
                    float threshold, maxBonus;
                    switch (tier)
                    {
                        case 1: threshold = 0.50f; maxBonus = 0.20f; break;
                        case 2: threshold = 0.40f; maxBonus = 0.35f; break;
                        case 3: threshold = 0.30f; maxBonus = 0.55f; break;
                        default: threshold = 0.20f; maxBonus = 0.80f; break;
                    }
                    float arcaneBonus = PassiveTreeTracker.CalcArcaneOverflow(req.Thing as Pawn, tier);
                    result += $"\n  Arcane Overflow (Tier {tier}/4): triggers above {threshold:P0} psyfocus, max +{maxBonus:P0}";
                    if (arcaneBonus > 0f)
                        result += $" [ACTIVE: x{(1f + arcaneBonus):F2}]";
                    else
                    {
                        float focus = (req.Thing as Pawn)?.psychicEntropy?.CurrentPsyfocus ?? 0f;
                        result += $" [Inactive: psyfocus {focus:P0}]";
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// VIT + WIS affects Neural Heat Limit (Psychic Entropy Max)
    /// Higher limit = can cast more psycasts before overheating
    /// </summary>
    public class StatPart_IsekaiPsychicEntropyMax : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float vit = GetStatValue(comp, "VIT");
            float wis = GetStatValue(comp, "WIS");
            float combined = (vit + wis) / 2f; // Average of VIT and WIS
            float bonus = StatToOffset(combined, IsekaiLevelingSettings.WIS_NeuralHeatLimit);
            val += Mathf.Clamp(bonus, -10f, 150f);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float vit = GetStatValue(comp, "VIT");
            float wis = GetStatValue(comp, "WIS");
            float combined = (vit + wis) / 2f;
            float bonus = StatToOffset(combined, IsekaiLevelingSettings.WIS_NeuralHeatLimit);
            return $"Isekai VIT+WIS ({combined:F0}): {Mathf.Clamp(bonus, -10f, 150f):+0;-0}";
        }
    }

    /// <summary>
    /// WIS affects Neural Heat Recovery (Psychic Entropy Recovery Rate)
    /// Higher recovery = neural heat dissipates faster
    /// </summary>
    public class StatPart_IsekaiPsychicEntropyRecovery : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_NeuralHeatRecovery);
            val *= Mathf.Clamp(multiplier, 0.5f, 5f);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = StatToMultiplier(wis, IsekaiLevelingSettings.WIS_NeuralHeatRecovery);
            return $"Isekai WIS ({wis:F0}): x{Mathf.Clamp(multiplier, 0.5f, 5f):F2}";
        }
    }

    /// <summary>
    /// WIS reduces psyfocus cost. Applied via:
    /// 1. Harmony patch on Ability.FinalPsyfocusCost (vanilla Royalty)
    /// 2. XML injection into VPE_PsyfocusCostFactor StatDef (Vanilla Psycasts Expanded)
    /// </summary>
    public class StatPart_IsekaiPsyfocusCost : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float wis = GetStatValue(comp, "WIS");
            // Reduce cost for high WIS (multiplier less than 1)
            float multiplier = 1f - StatToOffset(wis, IsekaiLevelingSettings.WIS_PsyfocusCost); // configurable reduction per point above base
            val *= Mathf.Clamp(multiplier, 0.3f, 1.2f);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float wis = GetStatValue(comp, "WIS");
            float multiplier = 1f - StatToOffset(wis, IsekaiLevelingSettings.WIS_PsyfocusCost);
            return $"Isekai WIS ({wis:F0}): x{Mathf.Clamp(multiplier, 0.3f, 1.2f):F2}";
        }
    }

    // ==================== MARKET VALUE STAT PART ====================
    
    /// <summary>
    /// Pawn Level and Stats affect Market Value
    /// High level pawns are worth millions of silver
    /// </summary>
    public class StatPart_IsekaiMarketValue : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float multiplier = CalculateValueMultiplier(comp);
            val *= multiplier;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float multiplier = CalculateValueMultiplier(comp);
            if (multiplier == 1f) return null;
            
            return $"Isekai Level {comp.Level}: x{multiplier:F1}";
        }
        
        /// <summary>
        /// Calculate market value multiplier based on level and total stats
        /// Reduced caps since this multiplies with rank multipliers from MobRank system
        /// Level 1-10: 1.0x (baseline)
        /// Level 11-25: 1.0x -> 1.5x (slight boost)
        /// Level 26-50: 1.5x -> 2.5x (moderate boost)
        /// Level 51-100: 2.5x -> 4.0x (high level)
        /// Level 101+: 4.0x -> 5.0x (capped to prevent excessive accumulation)
        /// </summary>
        private float CalculateValueMultiplier(IsekaiComponent comp)
        {
            int level = comp.Level;
            
            // Reduced multiplier to avoid excessive accumulation with rank multipliers
            float levelMult;
            if (level <= 10)
                levelMult = 1.0f;                            // Baseline
            else if (level <= 25)
                levelMult = 1.0f + (level - 10) * 0.033f;    // 1.0 -> 1.5
            else if (level <= 50)
                levelMult = 1.5f + (level - 25) * 0.04f;     // 1.5 -> 2.5
            else if (level <= 100)
                levelMult = 2.5f + (level - 50) * 0.03f;     // 2.5 -> 4.0
            else
                levelMult = Mathf.Min(5.0f, 4.0f + (level - 100) * 0.01f); // 4.0 -> 5.0 (capped)
            
            // Bonus from total allocated stat points (reduced impact)
            int totalStats = comp.stats.strength + comp.stats.dexterity + 
                            comp.stats.vitality + comp.stats.intelligence + 
                            comp.stats.wisdom + comp.stats.charisma;
            int baseTotal = 30; // 6 stats * 5 base each
            int bonusPoints = Mathf.Max(0, totalStats - baseTotal);
            
            // Each bonus stat point adds 0.5% value (reduced from 2%)
            float statMult = 1f + bonusPoints * 0.005f;
            
            float baseMult = levelMult * statMult;
            
            // Apply global multiplier from settings (scales the deviation from 1.0)
            // At 0x: always 1.0 (no effect)
            // At 1x: default values
            // At 2x: doubled effect
            float settingsMult = IsekaiMod.Settings?.PawnLevelValueMultiplier ?? 1.0f;
            return 1.0f + (baseMult - 1.0f) * settingsMult;
        }
    }

    // ==================== VEF COMPATIBILITY STAT PARTS ====================
    
    /// <summary>
    /// STR affects VEF Mass Carry Capacity (caravan carry weight from Vanilla Expanded Framework)
    /// Only active when VEF is loaded — patched via VEFCompatibility.xml
    /// Uses the same STR_CarryCapacity setting as regular carry capacity
    /// </summary>
    public class StatPart_IsekaiCarryMass : StatPart_IsekaiBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            var comp = GetComp(req);
            if (comp == null) return;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_CarryCapacity);
            val *= Mathf.Clamp(multiplier, 0.5f, 10f);
            ApplyPassiveMultiplier(comp, ref val, PassiveBonusType.CarryCapacity);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            var comp = GetComp(req);
            if (comp == null) return null;
            
            float str = GetStatValue(comp, "STR");
            float multiplier = StatToMultiplier(str, IsekaiLevelingSettings.STR_CarryCapacity);
            return $"Isekai STR ({str:F0}): x{Mathf.Clamp(multiplier, 0.5f, 10f):F2}" + PassiveMultExplanation(comp, PassiveBonusType.CarryCapacity);
        }
    }
}
