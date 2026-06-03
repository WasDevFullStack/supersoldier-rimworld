using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Forge
{
    // ══════════════════════════════════════════
    //  Base class for forge stat parts with try-catch safety
    // ══════════════════════════════════════════

    public abstract partial class StatPart_ForgeBase : StatPart
    {
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
    }

    // ══════════════════════════════════════════
    //  REFINEMENT STAT PARTS
    // ══════════════════════════════════════════

    /// <summary>
    /// Applies melee damage bonus from weapon refinement level.
    /// +5% per refinement level.
    /// </summary>
    public class StatPart_RefineMeleeDamage : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedMeleeWeapon(req))
            {
                val *= 1f + ForgeUtility.GetMeleeDamageBonus(comp.refinementLevel);
                return;
            }
            if (req.HasThing && req.Thing.def.IsMeleeWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                    val *= 1f + ForgeUtility.GetMeleeDamageBonus(itemComp.refinementLevel);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedMeleeWeapon(req))
            {
                float bonus = ForgeUtility.GetMeleeDamageBonus(comp.refinementLevel) * 100f;
                return "Isekai_StatExp_RefineDmg".Translate(comp.refinementLevel.ToString(), bonus.ToString("F0"));
            }
            if (req.HasThing && req.Thing.def.IsMeleeWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                {
                    float bonus = ForgeUtility.GetMeleeDamageBonus(itemComp.refinementLevel) * 100f;
                    return "Isekai_StatExp_RefineDmg".Translate(itemComp.refinementLevel.ToString(), bonus.ToString("F0"));
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Applies ranged cooldown reduction from weapon refinement level.
    /// -4% per level (multiplied against cooldown, so lower is better).
    /// </summary>
    public class StatPart_RefineRangedCooldown : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedRangedWeapon(req))
            {
                val *= 1f - ForgeUtility.GetRangedCooldownReduction(comp.refinementLevel);
                return;
            }
            if (req.HasThing && req.Thing.def.IsRangedWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                    val *= 1f - ForgeUtility.GetRangedCooldownReduction(itemComp.refinementLevel);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedRangedWeapon(req))
            {
                float reduction = ForgeUtility.GetRangedCooldownReduction(comp.refinementLevel) * 100f;
                return "Isekai_StatExp_RefineCooldown".Translate(comp.refinementLevel.ToString(), reduction.ToString("F0"));
            }
            if (req.HasThing && req.Thing.def.IsRangedWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                {
                    float reduction = ForgeUtility.GetRangedCooldownReduction(itemComp.refinementLevel) * 100f;
                    return "Isekai_StatExp_RefineCooldown".Translate(itemComp.refinementLevel.ToString(), reduction.ToString("F0"));
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Applies armor rating bonus from apparel refinement level.
    /// +4% per level to Sharp, Blunt, and Heat armor.
    /// </summary>
    public class StatPart_RefineArmor : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (!req.HasThing) return;
            if (req.Thing is Pawn pawn)
            {
                float totalBonus = GetTotalArmorBonus(pawn);
                if (totalBonus > 0f) val += totalBonus;
                return;
            }
            if (req.Thing.def.IsApparel)
            {
                var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (comp != null && comp.refinementLevel > 0)
                    val += ForgeUtility.GetArmorBonus(comp.refinementLevel);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            if (!req.HasThing) return null;
            if (req.Thing is Pawn pawn)
            {
                float totalBonus = GetTotalArmorBonus(pawn);
                if (totalBonus > 0f) return "Isekai_StatExp_RefineArmor".Translate((totalBonus * 100f).ToString("F0"));
                return null;
            }
            if (req.Thing.def.IsApparel)
            {
                var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (comp != null && comp.refinementLevel > 0)
                {
                    float bonus = ForgeUtility.GetArmorBonus(comp.refinementLevel) * 100f;
                    return "Isekai_StatExp_RefineArmorItem".Translate(comp.refinementLevel.ToString(), bonus.ToString("F0"));
                }
            }
            return null;
        }

        private float GetTotalArmorBonus(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return 0f;
            float total = 0f;
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                var comp = apparel.TryGetComp<CompForgeEnhancement>();
                if (comp != null && comp.refinementLevel > 0)
                    total += ForgeUtility.GetArmorBonus(comp.refinementLevel);
            }
            return total;
        }
    }

    /// <summary>
    /// Applies melee attack speed bonus from weapon refinement.
    /// -2% cooldown per level.
    /// </summary>
    public class StatPart_RefineMeleeSpeed : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedMeleeWeapon(req))
            {
                val *= 1f - ForgeUtility.GetMeleeSpeedBonus(comp.refinementLevel);
                return;
            }
            if (req.HasThing && req.Thing.def.IsMeleeWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                    val *= 1f - ForgeUtility.GetMeleeSpeedBonus(itemComp.refinementLevel);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedMeleeWeapon(req))
            {
                float bonus = ForgeUtility.GetMeleeSpeedBonus(comp.refinementLevel) * 100f;
                return "Isekai_StatExp_RefineCooldown".Translate(comp.refinementLevel.ToString(), bonus.ToString("F0"));
            }
            if (req.HasThing && req.Thing.def.IsMeleeWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                {
                    float bonus = ForgeUtility.GetMeleeSpeedBonus(itemComp.refinementLevel) * 100f;
                    return "Isekai_StatExp_RefineCooldown".Translate(itemComp.refinementLevel.ToString(), bonus.ToString("F0"));
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Applies ranged damage bonus from weapon refinement.
    /// +3% per level.
    /// </summary>
    public class StatPart_RefineRangedDamage : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedRangedWeapon(req))
            {
                val *= 1f + ForgeUtility.GetRangedDamageBonus(comp.refinementLevel);
                return;
            }
            if (req.HasThing && req.Thing.def.IsRangedWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                    val *= 1f + ForgeUtility.GetRangedDamageBonus(itemComp.refinementLevel);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedRangedWeapon(req))
            {
                float bonus = ForgeUtility.GetRangedDamageBonus(comp.refinementLevel) * 100f;
                return "Isekai_StatExp_RefineRangedDmg".Translate(comp.refinementLevel.ToString(), bonus.ToString("F0"));
            }
            if (req.HasThing && req.Thing.def.IsRangedWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                {
                    float bonus = ForgeUtility.GetRangedDamageBonus(itemComp.refinementLevel) * 100f;
                    return "Isekai_StatExp_RefineRangedDmg".Translate(itemComp.refinementLevel.ToString(), bonus.ToString("F0"));
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Applies ranged accuracy bonus from weapon refinement.
    /// +2% per level.
    /// </summary>
    public class StatPart_RefineRangedAccuracy : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedRangedWeapon(req))
            {
                val += ForgeUtility.GetRangedAccuracyBonus(comp.refinementLevel);
                return;
            }
            if (req.HasThing && req.Thing.def.IsRangedWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                    val += ForgeUtility.GetRangedAccuracyBonus(itemComp.refinementLevel);
            }
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            var comp = GetEquippedWeaponComp(req);
            if (comp != null && comp.refinementLevel > 0 && IsEquippedRangedWeapon(req))
            {
                float bonus = ForgeUtility.GetRangedAccuracyBonus(comp.refinementLevel) * 100f;
                return "Isekai_StatExp_RefineAccuracy".Translate(comp.refinementLevel.ToString(), bonus.ToString("F0"));
            }
            if (req.HasThing && req.Thing.def.IsRangedWeapon)
            {
                var itemComp = req.Thing.TryGetComp<CompForgeEnhancement>();
                if (itemComp != null && itemComp.refinementLevel > 0)
                {
                    float bonus = ForgeUtility.GetRangedAccuracyBonus(itemComp.refinementLevel) * 100f;
                    return "Isekai_StatExp_RefineAccuracy".Translate(itemComp.refinementLevel.ToString(), bonus.ToString("F0"));
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Applies weapon mass reduction from refinement.
    /// -1.5% per level. Works on the weapon Thing directly.
    /// </summary>
    public class StatPart_RefineWeaponMass : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (!req.HasThing) return;
            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null || comp.refinementLevel <= 0) return;
            if (!req.Thing.def.IsWeapon) return;
            val *= 1f - ForgeUtility.GetWeaponMassReduction(comp.refinementLevel);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            if (!req.HasThing) return null;
            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null || comp.refinementLevel <= 0) return null;
            if (!req.Thing.def.IsWeapon) return null;
            float reduction = ForgeUtility.GetWeaponMassReduction(comp.refinementLevel) * 100f;
            return "Isekai_StatExp_RefineMass".Translate(comp.refinementLevel.ToString(), reduction.ToString("F0"));
        }
    }

    /// <summary>
    /// Applies move speed bonus from refined armor.
    /// +1.5% per level, summed across all worn apparel.
    /// </summary>
    public class StatPart_RefineArmorMoveSpeed : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return;
            float bonus = GetTotalMoveSpeedBonus(pawn);
            if (bonus <= 0f) return;
            val *= 1f + bonus;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return null;
            float bonus = GetTotalMoveSpeedBonus(pawn);
            if (bonus <= 0f) return null;
            return "Isekai_StatExp_RefineMoveSpeed".Translate((bonus * 100f).ToString("F0"));
        }

        private float GetTotalMoveSpeedBonus(Pawn pawn)
        {
            if (pawn.apparel?.WornApparel == null) return 0f;
            float total = 0f;
            foreach (var apparel in pawn.apparel.WornApparel)
            {
                var comp = apparel.TryGetComp<CompForgeEnhancement>();
                if (comp != null && comp.refinementLevel > 0)
                    total += ForgeUtility.GetArmorMoveSpeedBonus(comp.refinementLevel);
            }
            return total;
        }
    }

    /// <summary>
    /// Applies MaxHitPoints bonus to refined armor/apparel.
    /// +10% per level → +100% at +10. Counteracts high DPS causing fast gear breakage.
    /// </summary>
    public class StatPart_RefineArmorHP : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (!req.HasThing) return;
            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null || comp.refinementLevel <= 0) return;
            if (!req.Thing.def.IsApparel && !req.Thing.def.IsWeapon) return;
            val *= 1f + ForgeUtility.GetArmorHPBonus(comp.refinementLevel);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            if (!req.HasThing) return null;
            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null || comp.refinementLevel <= 0) return null;
            if (!req.Thing.def.IsApparel && !req.Thing.def.IsWeapon) return null;
            float bonus = ForgeUtility.GetArmorHPBonus(comp.refinementLevel) * 100f;
            return "Isekai_StatExp_RefineDurability".Translate(comp.refinementLevel.ToString(), bonus.ToString("F0"));
        }
    }

    /// <summary>
    /// Applies mass reduction from refined armor.
    /// -2% per level. Works on the apparel Thing directly.
    /// </summary>
    public class StatPart_RefineArmorMass : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (!req.HasThing) return;
            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null || comp.refinementLevel <= 0) return;
            if (!req.Thing.def.IsApparel) return;
            val *= 1f - ForgeUtility.GetArmorMassReduction(comp.refinementLevel);
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            if (!req.HasThing) return null;
            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null || comp.refinementLevel <= 0) return null;
            if (!req.Thing.def.IsApparel) return null;
            float reduction = ForgeUtility.GetArmorMassReduction(comp.refinementLevel) * 100f;
            return "Isekai_StatExp_RefineMass".Translate(comp.refinementLevel.ToString(), reduction.ToString("F0"));
        }
    }

    // ══════════════════════════════════════════
    //  WEAPON MASTERY STAT PARTS
    // ══════════════════════════════════════════

    /// <summary>
    /// Applies melee hit chance bonus from weapon mastery.
    /// Only active when wielding the mastered weapon type.
    /// </summary>
    public class StatPart_MasteryMeleeHitChance : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return;
            float bonus = GetMasteryHitChance(req);
            if (bonus <= 0f) return;
            val += bonus;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return null;
            float bonus = GetMasteryHitChance(req);
            if (bonus <= 0f) return null;
            return "Isekai_StatExp_Mastery".Translate($"+{bonus * 100f:F0}%");
        }

        private float GetMasteryHitChance(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return 0f;
            string weaponDef = GetEquippedWeaponDefName(pawn);
            if (weaponDef == null) return 0f;
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp?.weaponMastery == null) return 0f;
            comp.weaponMastery.GetMasteryBonuses(weaponDef, out float hitChance, out _, out _);
            return hitChance;
        }
    }

    /// <summary>
    /// Applies melee damage bonus from weapon mastery.
    /// </summary>
    public class StatPart_MasteryMeleeDamage : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return;
            float bonus = GetMasteryDamage(req);
            if (bonus <= 0f) return;
            val *= 1f + bonus;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return null;
            float bonus = GetMasteryDamage(req);
            if (bonus <= 0f) return null;
            return "Isekai_StatExp_Mastery".Translate($"+{bonus * 100f:F0}%");
        }

        private float GetMasteryDamage(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return 0f;
            string weaponDef = GetEquippedWeaponDefName(pawn);
            if (weaponDef == null) return 0f;
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp?.weaponMastery == null) return 0f;
            comp.weaponMastery.GetMasteryBonuses(weaponDef, out _, out _, out float damage);
            return damage;
        }
    }

    /// <summary>
    /// Applies shooting accuracy bonus from weapon mastery (ranged weapons).
    /// </summary>
    public class StatPart_MasteryShootingAccuracy : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return;
            float bonus = GetMasteryAccuracy(req);
            if (bonus <= 0f) return;
            val += bonus;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return null;
            float bonus = GetMasteryAccuracy(req);
            if (bonus <= 0f) return null;
            return "Isekai_StatExp_Mastery".Translate($"+{bonus * 100f:F0}%");
        }

        private float GetMasteryAccuracy(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return 0f;
            string weaponDef = GetEquippedWeaponDefName(pawn);
            if (weaponDef == null) return 0f;
            // Only for ranged weapons
            if (pawn.equipment?.Primary == null || !pawn.equipment.Primary.def.IsRangedWeapon) return 0f;
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp?.weaponMastery == null) return 0f;
            comp.weaponMastery.GetMasteryBonuses(weaponDef, out float hitChance, out _, out _);
            return hitChance;
        }
    }

    /// <summary>
    /// Applies attack speed bonus from weapon mastery (reduces aiming delay / melee cooldown).
    /// </summary>
    public class StatPart_MasteryAttackSpeed : StatPart_ForgeBase
    {
        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return;
            float bonus = GetMasterySpeed(req);
            if (bonus <= 0f) return;
            val *= 1f - bonus; // Reduces cooldown/aiming delay
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableWeaponMastery) return null;
            float bonus = GetMasterySpeed(req);
            if (bonus <= 0f) return null;
            return "Isekai_StatExp_Mastery".Translate($"-{bonus * 100f:F0}%");
        }

        private float GetMasterySpeed(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return 0f;
            string weaponDef = GetEquippedWeaponDefName(pawn);
            if (weaponDef == null) return 0f;
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp?.weaponMastery == null) return 0f;
            comp.weaponMastery.GetMasteryBonuses(weaponDef, out _, out float attackSpeed, out _);
            return attackSpeed;
        }
    }

    // ══════════════════════════════════════════
    //  RUNE STAT PARTS
    // ══════════════════════════════════════════

    /// <summary>
    /// Generic rune stat part that checks all equipped gear for a specific RuneEffectType
    /// and applies the cumulative magnitude as a stat bonus.
    /// </summary>
    public class StatPart_RuneEffect : StatPart_ForgeBase
    {
        public RuneEffectType targetEffect;
        public bool isMultiplier = false; // true = multiply val, false = add to val
        public bool isReduction = false;  // true = subtract/reduce (e.g. cooldown reduction)

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            float bonus = GetTotalRuneBonus(req);
            if (bonus == 0f) return;
            if (isMultiplier)
                val *= isReduction ? (1f - bonus) : (1f + bonus);
            else
                val += isReduction ? -bonus : bonus;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            float bonus = GetTotalRuneBonus(req);
            if (bonus == 0f) return null;
            if (isReduction)
                return "Isekai_StatExp_RuneEnchant".Translate($"-{bonus * 100f:F0}%");
            string sign = bonus > 0 ? "+" : "";
            return "Isekai_StatExp_RuneEnchant".Translate($"{sign}{bonus * 100f:F0}%");
        }

        private float GetTotalRuneBonus(StatRequest req)
        {
            if (!req.HasThing) return 0f;

            // Pawn path: sum rune bonuses from all equipped gear
            if (req.Thing is Pawn pawn)
            {
                float total = 0f;
                if (pawn.equipment?.Primary != null)
                    total += GetRuneBonusFromThing(pawn.equipment.Primary);
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                        total += GetRuneBonusFromThing(apparel);
                }
                return total;
            }

            // Item path: check the item itself for rune bonuses
            return GetRuneBonusFromThing(req.Thing);
        }

        private float GetRuneBonusFromThing(Thing thing)
        {
            var comp = thing?.TryGetComp<CompForgeEnhancement>();
            if (comp == null) return 0f;

            float total = 0f;
            var runesWithRanks = comp.GetAppliedRunesWithRanks();
            foreach (var (rune, rank) in runesWithRanks)
            {
                if (rune.effectType == targetEffect)
                    total += rune.GetMagnitudeForRank(rank);
            }
            return total;
        }
    }

    // ══════════════════════════════════════════
    //  MARKET VALUE STAT PART
    // ══════════════════════════════════════════

    /// <summary>
    /// Increases item MarketValue based on refinement level and applied runes.
    /// Refinement: +25% per level (e.g. +10 = +250%).
    /// Runes: +40% per rune rank applied (e.g. a Rank III rune adds +120%).
    /// </summary>
    public class StatPart_ForgeMarketValue : StatPart_ForgeBase
    {
        private const float RefinementValuePerLevel = 0.25f;
        private const float RuneValuePerRank = 0.40f;

        protected override void TransformValueCore(StatRequest req, ref float val)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return;
            if (!req.HasThing) return;

            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null) return;

            float multiplier = GetValueMultiplier(comp);
            if (multiplier != 1f)
                val *= multiplier;
        }

        protected override string ExplanationPartCore(StatRequest req)
        {
            if (!IsekaiLevelingSettings.EnableForgeSystem) return null;
            if (!req.HasThing) return null;

            var comp = req.Thing.TryGetComp<CompForgeEnhancement>();
            if (comp == null) return null;

            float refineBonus = comp.refinementLevel * RefinementValuePerLevel;
            float runeBonus = 0f;
            var runesWithRanks = comp.GetAppliedRunesWithRanks();
            foreach (var (rune, rank) in runesWithRanks)
                runeBonus += rank * RuneValuePerRank;

            if (refineBonus == 0f && runeBonus == 0f) return null;

            string result = "";
            if (refineBonus > 0f)
                result += "Isekai_StatExp_RefineMarket".Translate(comp.refinementLevel.ToString(), (refineBonus * 100f).ToString("F0")) + "\n";
            if (runeBonus > 0f)
                result += "Isekai_StatExp_RuneMarket".Translate((runeBonus * 100f).ToString("F0"));
            return result.TrimEnd();
        }

        private float GetValueMultiplier(CompForgeEnhancement comp)
        {
            float bonus = comp.refinementLevel * RefinementValuePerLevel;
            var runesWithRanks = comp.GetAppliedRunesWithRanks();
            foreach (var (rune, rank) in runesWithRanks)
                bonus += rank * RuneValuePerRank;
            return 1f + bonus;
        }
    }

    // ══════════════════════════════════════════
    //  HELPER METHODS (shared by all forge stat parts)
    // ══════════════════════════════════════════

    // Extension to StatPart_ForgeBase
    public partial class StatPart_ForgeBase
    {
        /// <summary>Get CompForgeEnhancement from the pawn's equipped primary weapon.</summary>
        protected static CompForgeEnhancement GetEquippedWeaponComp(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return null;
            return pawn.equipment?.Primary?.TryGetComp<CompForgeEnhancement>();
        }

        /// <summary>Check if the pawn's equipped primary weapon is a melee weapon.</summary>
        protected static bool IsEquippedMeleeWeapon(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return false;
            var primary = pawn.equipment?.Primary;
            return primary != null && primary.def.IsMeleeWeapon;
        }

        /// <summary>Check if the pawn's equipped primary weapon is a ranged weapon.</summary>
        protected static bool IsEquippedRangedWeapon(StatRequest req)
        {
            if (!req.HasThing || !(req.Thing is Pawn pawn)) return false;
            var primary = pawn.equipment?.Primary;
            return primary != null && primary.def.IsRangedWeapon;
        }

        /// <summary>Get the defName of the pawn's equipped primary weapon.</summary>
        protected static string GetEquippedWeaponDefName(Pawn pawn)
        {
            return pawn.equipment?.Primary?.def?.defName;
        }
    }
}
