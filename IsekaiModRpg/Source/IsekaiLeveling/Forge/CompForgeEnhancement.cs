using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// CompProperties for forge enhancement. Defines max rune slots for weapons vs armor.
    /// </summary>
    public class CompProperties_ForgeEnhancement : CompProperties
    {
        public int maxWeaponRuneSlots = 2;
        public int maxArmorRuneSlots = 3;

        public CompProperties_ForgeEnhancement()
        {
            compClass = typeof(CompForgeEnhancement);
        }
    }

    /// <summary>
    /// ThingComp attached to all weapons and armor at startup.
    /// Stores refinement level (+0 to +10) and applied rune slots with ranks.
    /// </summary>
    public class CompForgeEnhancement : ThingComp
    {
        public int refinementLevel = 0;
        public List<string> appliedRuneDefNames = new List<string>();
        public List<int> appliedRuneRanks = new List<int>();

        public CompProperties_ForgeEnhancement Props => (CompProperties_ForgeEnhancement)props;

        /// <summary>
        /// Max rune slots depends on whether this is a weapon or armor.
        /// </summary>
        public int MaxRuneSlots
        {
            get
            {
                if (parent == null || Props == null) return 0;
                if (parent.def.IsWeapon) return Props.maxWeaponRuneSlots;
                if (parent.def.IsApparel) return Props.maxArmorRuneSlots;
                return 0;
            }
        }

        public int UsedRuneSlots => appliedRuneDefNames?.Count ?? 0;
        public int FreeRuneSlots => MaxRuneSlots - UsedRuneSlots;

        public bool CanRefine() => refinementLevel < 10;

        public bool CanAddRune() => FreeRuneSlots > 0;

        /// <summary>
        /// Get the refinement stat bonus multiplier.
        /// Weapons (melee): +5% per level. Weapons (ranged): +4% per level. Armor: +4% per level.
        /// </summary>
        public float GetRefinementBonus()
        {
            if (refinementLevel <= 0) return 0f;
            if (parent.def.IsWeapon)
            {
                bool isRanged = parent.def.IsRangedWeapon;
                float perLevel = isRanged ? 0.04f : 0.05f;
                return refinementLevel * perLevel;
            }
            if (parent.def.IsApparel)
            {
                return refinementLevel * 0.04f;
            }
            return 0f;
        }

        /// <summary>
        /// Get all applied RuneDefs resolved from their defNames.
        /// </summary>
        public List<RuneDef> GetAppliedRunes()
        {
            var result = new List<RuneDef>();
            if (appliedRuneDefNames == null) return result;
            foreach (string defName in appliedRuneDefNames)
            {
                var rune = DefDatabase<RuneDef>.GetNamedSilentFail(defName);
                if (rune != null)
                    result.Add(rune);
            }
            return result;
        }

        /// <summary>
        /// Get the rank of the applied rune at the given index.
        /// Returns 1 if ranks list is missing or out of range (backward compat).
        /// </summary>
        public int GetRuneRank(int index)
        {
            if (appliedRuneRanks == null || index < 0 || index >= appliedRuneRanks.Count)
                return 1;
            return appliedRuneRanks[index];
        }

        /// <summary>
        /// Get all applied runes with their ranks as pairs.
        /// </summary>
        public List<(RuneDef rune, int rank)> GetAppliedRunesWithRanks()
        {
            var result = new List<(RuneDef, int)>();
            if (appliedRuneDefNames == null) return result;
            for (int i = 0; i < appliedRuneDefNames.Count; i++)
            {
                var rune = DefDatabase<RuneDef>.GetNamedSilentFail(appliedRuneDefNames[i]);
                if (rune != null)
                    result.Add((rune, GetRuneRank(i)));
            }
            return result;
        }

        /// <summary>
        /// Add a rune to this equipment at a given rank. Returns true if successful.
        /// </summary>
        public bool TryAddRune(RuneDef rune, int rank = 1)
        {
            if (rune == null || !CanAddRune()) return false;

            // Validate category matches equipment type
            if (rune.category == RuneCategory.Weapon && !parent.def.IsWeapon) return false;
            if (rune.category == RuneCategory.Armor && !parent.def.IsApparel) return false;

            rank = UnityEngine.Mathf.Clamp(rank, 1, rune.maxRank);
            appliedRuneDefNames.Add(rune.defName);
            appliedRuneRanks.Add(rank);
            return true;
        }

        /// <summary>
        /// Remove a rune at the given slot index. The rune is destroyed (not returned).
        /// </summary>
        public bool RemoveRuneAt(int index)
        {
            if (appliedRuneDefNames == null || index < 0 || index >= appliedRuneDefNames.Count)
                return false;
            appliedRuneDefNames.RemoveAt(index);
            if (appliedRuneRanks != null && index < appliedRuneRanks.Count)
                appliedRuneRanks.RemoveAt(index);
            return true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref refinementLevel, "refinementLevel", 0);
            Scribe_Collections.Look(ref appliedRuneDefNames, "appliedRuneDefNames", LookMode.Value);
            Scribe_Collections.Look(ref appliedRuneRanks, "appliedRuneRanks", LookMode.Value);
            if (appliedRuneDefNames == null)
                appliedRuneDefNames = new List<string>();
            if (appliedRuneRanks == null)
                appliedRuneRanks = new List<int>();
            // Backward compat: fill missing ranks with 1
            while (appliedRuneRanks.Count < appliedRuneDefNames.Count)
                appliedRuneRanks.Add(1);
        }

        /// <summary>
        /// Append "+N" to the item label when refined.
        /// </summary>
        public override string TransformLabel(string label)
        {
            if (refinementLevel > 0)
                return label + " +" + refinementLevel;
            return label;
        }

        /// <summary>
        /// Show refinement and rune info in the inspect pane.
        /// </summary>
        public override string CompInspectStringExtra()
        {
            if (refinementLevel <= 0 && UsedRuneSlots <= 0)
                return null;

            var sb = new StringBuilder();

            if (refinementLevel > 0)
            {
                string bonusStr;
                if (parent.def.IsMeleeWeapon)
                    bonusStr = "Isekai_Inspect_MeleeBonus".Translate(
                        (ForgeUtility.GetMeleeDamageBonus(refinementLevel) * 100f).ToString("F0"),
                        (ForgeUtility.GetMeleeSpeedBonus(refinementLevel) * 100f).ToString("F0"),
                        (ForgeUtility.GetWeaponMassReduction(refinementLevel) * 100f).ToString("F0"));
                else if (parent.def.IsRangedWeapon)
                    bonusStr = "Isekai_Inspect_RangedBonus".Translate(
                        (ForgeUtility.GetRangedDamageBonus(refinementLevel) * 100f).ToString("F0"),
                        (ForgeUtility.GetRangedCooldownReduction(refinementLevel) * 100f).ToString("F0"),
                        (ForgeUtility.GetRangedAccuracyBonus(refinementLevel) * 100f).ToString("F0"));
                else if (parent.def.IsApparel)
                    bonusStr = "Isekai_Inspect_ArmorBonus".Translate(
                        (ForgeUtility.GetArmorBonus(refinementLevel) * 100f).ToString("F0"),
                        (ForgeUtility.GetArmorMoveSpeedBonus(refinementLevel) * 100f).ToString("F1"),
                        (ForgeUtility.GetArmorMassReduction(refinementLevel) * 100f).ToString("F0"));
                else
                    bonusStr = "";
                sb.AppendLine("Isekai_Inspect_RefineInfo".Translate(refinementLevel.ToString(), bonusStr));
            }

            if (UsedRuneSlots > 0)
            {
                var runesWithRanks = GetAppliedRunesWithRanks();
                string runeList = string.Join(", ", runesWithRanks.Select(r =>
                    (r.rune.label ?? r.rune.defName) + " " + RuneDef.GetRomanNumeral(r.rank)));
                sb.AppendLine("Isekai_Inspect_Runes".Translate(UsedRuneSlots.ToString(), MaxRuneSlots.ToString(), runeList));
            }
            else if (MaxRuneSlots > 0 && refinementLevel > 0)
            {
                sb.AppendLine("Isekai_Inspect_RuneSlots".Translate(UsedRuneSlots.ToString(), MaxRuneSlots.ToString()));
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Show refinement and rune details in the item's stat info window ("i" button).
        /// </summary>
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            var baseStats = base.SpecialDisplayStats();
            if (baseStats != null)
            {
                foreach (var entry in baseStats)
                    yield return entry;
            }

            if (parent?.def == null)
                yield break;

            if (refinementLevel > 0)
            {
                var descSb = new StringBuilder();
                descSb.AppendLine("Isekai_Stat_RefinedDesc".Translate(refinementLevel.ToString()) + "\n");

                if (parent.def.IsMeleeWeapon)
                {
                    descSb.AppendLine("Isekai_Stat_MeleeDmg".Translate((ForgeUtility.GetMeleeDamageBonus(refinementLevel) * 100f).ToString("F0")));
                    descSb.AppendLine("Isekai_Stat_AttackCooldown".Translate((ForgeUtility.GetMeleeSpeedBonus(refinementLevel) * 100f).ToString("F0")));
                    descSb.AppendLine("Isekai_Stat_WeaponMass".Translate((ForgeUtility.GetWeaponMassReduction(refinementLevel) * 100f).ToString("F1")));
                }
                else if (parent.def.IsRangedWeapon)
                {
                    descSb.AppendLine("Isekai_Stat_RangedDmg".Translate((ForgeUtility.GetRangedDamageBonus(refinementLevel) * 100f).ToString("F0")));
                    descSb.AppendLine("Isekai_Stat_Cooldown".Translate((ForgeUtility.GetRangedCooldownReduction(refinementLevel) * 100f).ToString("F0")));
                    descSb.AppendLine("Isekai_Stat_AccuracyStat".Translate((ForgeUtility.GetRangedAccuracyBonus(refinementLevel) * 100f).ToString("F0")));
                    descSb.AppendLine("Isekai_Stat_WeaponMass".Translate((ForgeUtility.GetWeaponMassReduction(refinementLevel) * 100f).ToString("F1")));
                }
                else if (parent.def.IsApparel)
                {
                    descSb.AppendLine("Isekai_Stat_ArmorRating".Translate((ForgeUtility.GetArmorBonus(refinementLevel) * 100f).ToString("F0")));
                    descSb.AppendLine("Isekai_Stat_MoveSpeedStat".Translate((ForgeUtility.GetArmorMoveSpeedBonus(refinementLevel) * 100f).ToString("F1")));
                    descSb.AppendLine("Isekai_Stat_ArmorMass".Translate((ForgeUtility.GetArmorMassReduction(refinementLevel) * 100f).ToString("F0")));
                }

                descSb.AppendLine();
                descSb.Append("Isekai_Stat_RefinementMaxHint".Translate());

                string valueText = $"+{refinementLevel}";

                yield return new StatDrawEntry(
                    StatCategoryDefOf.Basics,
                    "Isekai_Stat_ForgeRefinement".Translate(),
                    valueText,
                    descSb.ToString(),
                    1000);
            }

            if (MaxRuneSlots > 0)
            {
                var runesWithRanks = GetAppliedRunesWithRanks();
                string runeValue = $"{UsedRuneSlots} / {MaxRuneSlots}";
                var sb = new StringBuilder();
                sb.AppendLine("Isekai_Stat_RuneSlotsDesc".Translate(MaxRuneSlots.ToString(), UsedRuneSlots.ToString()));
                if (runesWithRanks.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Isekai_Stat_AppliedRunes".Translate());
                    foreach (var (rune, rank) in runesWithRanks)
                    {
                        string desc = rune.GetStatDescriptionForRank(rank);
                        sb.AppendLine($"  • {rune.LabelCap} {RuneDef.GetRomanNumeral(rank)}: {desc}");
                    }
                }
                sb.AppendLine();
                sb.Append("Isekai_Stat_RuneHint".Translate());

                yield return new StatDrawEntry(
                    StatCategoryDefOf.Basics,
                    "Isekai_Rune_Slots".Translate(),
                    runeValue,
                    sb.ToString(),
                    999);

                // Individual stat entries per rune effect type for visibility in item info
                if (runesWithRanks.Count > 0)
                {
                    var effectTotals = new Dictionary<RuneEffectType, float>();
                    var effectSources = new Dictionary<RuneEffectType, List<string>>();
                    foreach (var (rune, rank) in runesWithRanks)
                    {
                        float mag = rune.GetMagnitudeForRank(rank);
                        if (!effectTotals.ContainsKey(rune.effectType))
                        {
                            effectTotals[rune.effectType] = 0f;
                            effectSources[rune.effectType] = new List<string>();
                        }
                        effectTotals[rune.effectType] += mag;
                        effectSources[rune.effectType].Add($"{rune.LabelCap} {RuneDef.GetRomanNumeral(rank)}: {rune.GetStatDescriptionForRank(rank)}");
                    }

                    int priority = 998;
                    foreach (var kvp in effectTotals)
                    {
                        var effectType = kvp.Key;
                        float total = kvp.Value;
                        string effectName = GetEffectDisplayName(effectType);
                        bool isProc = effectType == RuneEffectType.Lifesteal || effectType == RuneEffectType.StunChance ||
                                      effectType == RuneEffectType.FireDamage || effectType == RuneEffectType.SlowChance ||
                                      effectType == RuneEffectType.AoESplash;
                        bool isReduction = effectType == RuneEffectType.MentalBreakReduce;
                        string valueStr = isReduction ? $"-{total * 100f:F0}%" :
                                          isProc ? $"{total * 100f:F0}%" :
                                          $"+{total * 100f:F0}%";

                        var detailSb = new StringBuilder();
                        detailSb.AppendLine("Isekai_Stat_TotalEffect".Translate(effectName, valueStr));
                        detailSb.AppendLine();
                        detailSb.AppendLine("Isekai_Stat_Sources".Translate());
                        foreach (string source in effectSources[effectType])
                            detailSb.AppendLine($"  • {source}");

                        yield return new StatDrawEntry(
                            StatCategoryDefOf.Basics,
                            "Isekai_Stat_RuneLabel".Translate(effectName),
                            valueStr,
                            detailSb.ToString().TrimEnd(),
                            priority--);
                    }
                }
            }
        }

        private static string GetEffectDisplayName(RuneEffectType effectType)
        {
            switch (effectType)
            {
                case RuneEffectType.MeleeAttackSpeed: return "Isekai_Effect_MeleeAttackSpeed".Translate();
                case RuneEffectType.RangedAccuracy: return "Isekai_Effect_RangedAccuracy".Translate();
                case RuneEffectType.Lifesteal: return "Isekai_Effect_Lifesteal".Translate();
                case RuneEffectType.StunChance: return "Isekai_Effect_StunChance".Translate();
                case RuneEffectType.FireDamage: return "Isekai_Effect_FireDamage".Translate();
                case RuneEffectType.SlowChance: return "Isekai_Effect_SlowChance".Translate();
                case RuneEffectType.ArmorPenetration: return "Isekai_Effect_ArmorPenetration".Translate();
                case RuneEffectType.AoESplash: return "Isekai_Effect_AoESplash".Translate();
                case RuneEffectType.RangedDamage: return "Isekai_Effect_RangedDamage".Translate();
                case RuneEffectType.MaxHealth: return "Isekai_Effect_MaxHealth".Translate();
                case RuneEffectType.DodgeChance: return "Isekai_Effect_DodgeChance".Translate();
                case RuneEffectType.Resistance: return "Isekai_Effect_Resistance".Translate();
                case RuneEffectType.HealRate: return "Isekai_Effect_HealRate".Translate();
                case RuneEffectType.MentalBreakReduce: return "Isekai_Effect_MentalBreakReduce".Translate();
                case RuneEffectType.ImmunityGain: return "Isekai_Effect_ImmunityGain".Translate();
                case RuneEffectType.MoveSpeed: return "Isekai_Effect_MoveSpeedEffect".Translate();
                case RuneEffectType.DamageReduction: return "Isekai_Effect_DamageReduction".Translate();
                default: return effectType.ToString();
            }
        }
    }
}
