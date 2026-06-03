using UnityEngine;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Whether a rune targets weapons or armor.
    /// </summary>
    public enum RuneCategory
    {
        Weapon,
        Armor
    }

    /// <summary>
    /// The specific stat or proc effect a rune provides.
    /// </summary>
    public enum RuneEffectType
    {
        // ── Weapon runes ──
        MeleeAttackSpeed,   // Fury
        RangedAccuracy,     // Precision
        Lifesteal,          // Vampirism (proc)
        StunChance,         // Thunder (proc)
        FireDamage,         // Flame (proc)
        SlowChance,         // Frost (proc)
        ArmorPenetration,   // Sharpness
        AoESplash,          // Havoc (proc, melee only)
        RangedDamage,       // Devastation

        // ── Armor runes ──
        MaxHealth,          // Fortitude
        DodgeChance,        // Evasion
        Resistance,         // Resistance (toxic/heat/cold)
        HealRate,           // Regeneration
        MentalBreakReduce,  // Warding
        ImmunityGain,       // Vitality
        MoveSpeed,          // Swiftness
        DamageReduction     // Bulwark
    }

    /// <summary>
    /// Custom Def for rune types. Defined in XML under Defs/RuneDefs/.
    /// Each RuneDef represents a type of rune that can be crafted and socketed into equipment.
    /// Runes can be applied at ranks I-V, scaling their effect magnitude.
    /// </summary>
    public class RuneDef : Def
    {
        /// <summary>Whether this rune goes in weapons or armor.</summary>
        public RuneCategory category;

        /// <summary>The stat or proc effect this rune provides.</summary>
        public RuneEffectType effectType;

        /// <summary>The base magnitude of the effect at rank I (e.g. 0.12 for +12% attack speed).</summary>
        public float magnitude;

        /// <summary>Maximum rank this rune can be applied at (default 5).</summary>
        public int maxRank = 5;

        /// <summary>Display color for the rune in UI.</summary>
        public Color runeColor = Color.white;

        /// <summary>Human-readable stat description shown in tooltips (e.g. "+12% melee attack speed").</summary>
        public string statDescription;

        /// <summary>Whether this is a proc effect (triggers on hit) rather than a flat stat bonus.</summary>
        public bool IsProcEffect
        {
            get
            {
                switch (effectType)
                {
                    case RuneEffectType.Lifesteal:
                    case RuneEffectType.StunChance:
                    case RuneEffectType.FireDamage:
                    case RuneEffectType.SlowChance:
                    case RuneEffectType.AoESplash:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Get the effective magnitude for a given rank.
        /// Rank I = 1.0× base, II = 1.5×, III = 2.0×, IV = 2.5×, V = 3.0×.
        /// </summary>
        public float GetMagnitudeForRank(int rank)
        {
            if (rank <= 1) return magnitude;
            return magnitude * (1f + (rank - 1) * 0.5f);
        }

        /// <summary>
        /// Get the number of rune items required to apply at a given rank.
        /// Rank I = 1, II = 2, III = 3, IV = 4, V = 5.
        /// </summary>
        public static int GetItemCostForRank(int rank)
        {
            return Mathf.Clamp(rank, 1, 5);
        }

        /// <summary>
        /// Convert a rank number to Roman numeral string.
        /// </summary>
        public static string GetRomanNumeral(int rank)
        {
            switch (rank)
            {
                case 1: return "I";
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                case 5: return "V";
                default: return rank.ToString();
            }
        }

        /// <summary>
        /// Get the stat description with rank-scaled values.
        /// </summary>
        public string GetStatDescriptionForRank(int rank)
        {
            if (rank <= 1) return statDescription;
            float scaledMag = GetMagnitudeForRank(rank) * 100f;
            string sign;
            if (effectType == RuneEffectType.MentalBreakReduce) sign = "-";
            else sign = IsProcEffect ? "" : "+";
            return "Isekai_RuneDesc_Format".Translate(sign, scaledMag.ToString("F0"), GetEffectShortName());
        }

        private string GetEffectShortName()
        {
            switch (effectType)
            {
                case RuneEffectType.MeleeAttackSpeed: return "Isekai_EffectShort_MeleeAttackSpeed".Translate();
                case RuneEffectType.RangedAccuracy: return "Isekai_EffectShort_RangedAccuracy".Translate();
                case RuneEffectType.Lifesteal: return "Isekai_EffectShort_Lifesteal".Translate();
                case RuneEffectType.StunChance: return "Isekai_EffectShort_StunChance".Translate();
                case RuneEffectType.FireDamage: return "Isekai_EffectShort_FireDamage".Translate();
                case RuneEffectType.SlowChance: return "Isekai_EffectShort_SlowChance".Translate();
                case RuneEffectType.ArmorPenetration: return "Isekai_EffectShort_ArmorPenetration".Translate();
                case RuneEffectType.AoESplash: return "Isekai_EffectShort_AoESplash".Translate();
                case RuneEffectType.RangedDamage: return "Isekai_EffectShort_RangedDamage".Translate();
                case RuneEffectType.MaxHealth: return "Isekai_EffectShort_MaxHealth".Translate();
                case RuneEffectType.DodgeChance: return "Isekai_EffectShort_DodgeChance".Translate();
                case RuneEffectType.Resistance: return "Isekai_EffectShort_Resistance".Translate();
                case RuneEffectType.HealRate: return "Isekai_EffectShort_HealRate".Translate();
                case RuneEffectType.MentalBreakReduce: return "Isekai_EffectShort_MentalBreakReduce".Translate();
                case RuneEffectType.ImmunityGain: return "Isekai_EffectShort_ImmunityGain".Translate();
                case RuneEffectType.MoveSpeed: return "Isekai_EffectShort_MoveSpeedEffect".Translate();
                case RuneEffectType.DamageReduction: return "Isekai_EffectShort_DamageReduction".Translate();
                default: return effectType.ToString();
            }
        }
    }
}
