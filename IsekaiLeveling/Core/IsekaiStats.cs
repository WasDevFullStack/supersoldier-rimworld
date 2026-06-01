using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Represents the core RPG stats for the Isekai system
    /// </summary>
    public enum IsekaiStatType
    {
        Strength,       // Melee damage, carry capacity, mining speed
        Dexterity,      // Move speed, attack speed, dodge
        Vitality,       // Health, resistance
        Intelligence,   // Work speed, research speed
        Wisdom,         // XP gain, skill learning
        Charisma        // Social impact, trade prices
    }

    /// <summary>
    /// Manages the stat allocation system for a pawn
    /// </summary>
    public class IsekaiStatAllocation : IExposable
    {
        // Base stats (allocated by player)
        public int strength = 5;
        public int dexterity = 5;
        public int vitality = 5;
        public int intelligence = 5;
        public int wisdom = 5;
        public int charisma = 5;

        // Points available to allocate
        public int availableStatPoints = 0;

        // Constants
        public const int BASE_STAT_VALUE = 5;
        public const int MAX_STAT_VALUE = 9999; // Effectively unlimited
        public const int STAT_POINTS_PER_LEVEL = 3;
        
        /// <summary>
        /// Get the effective max stat value based on settings
        /// </summary>
        public static int GetEffectiveMaxStat()
        {
            if (IsekaiMod.Settings == null) return MAX_STAT_VALUE;
            return IsekaiMod.Settings.MaxStatCap > 0 ? IsekaiMod.Settings.MaxStatCap : MAX_STAT_VALUE;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref strength, "strength", BASE_STAT_VALUE);
            Scribe_Values.Look(ref dexterity, "dexterity", BASE_STAT_VALUE);
            Scribe_Values.Look(ref vitality, "vitality", BASE_STAT_VALUE);
            Scribe_Values.Look(ref intelligence, "intelligence", BASE_STAT_VALUE);
            Scribe_Values.Look(ref wisdom, "wisdom", BASE_STAT_VALUE);
            Scribe_Values.Look(ref charisma, "charisma", BASE_STAT_VALUE);
            Scribe_Values.Look(ref availableStatPoints, "availableStatPoints", 0);
        }

        public int GetStat(IsekaiStatType stat)
        {
            switch (stat)
            {
                case IsekaiStatType.Strength: return strength;
                case IsekaiStatType.Dexterity: return dexterity;
                case IsekaiStatType.Vitality: return vitality;
                case IsekaiStatType.Intelligence: return intelligence;
                case IsekaiStatType.Wisdom: return wisdom;
                case IsekaiStatType.Charisma: return charisma;
                default: return BASE_STAT_VALUE;
            }
        }

        public void SetStat(IsekaiStatType stat, int value)
        {
            int maxStat = GetEffectiveMaxStat();
            value = System.Math.Max(1, System.Math.Min(maxStat, value));
            switch (stat)
            {
                case IsekaiStatType.Strength: strength = value; break;
                case IsekaiStatType.Dexterity: dexterity = value; break;
                case IsekaiStatType.Vitality: vitality = value; break;
                case IsekaiStatType.Intelligence: intelligence = value; break;
                case IsekaiStatType.Wisdom: wisdom = value; break;
                case IsekaiStatType.Charisma: charisma = value; break;
            }
        }

        public bool AllocatePoint(IsekaiStatType stat)
        {
            if (availableStatPoints <= 0) return false;
            if (GetStat(stat) >= GetEffectiveMaxStat()) return false;

            SetStat(stat, GetStat(stat) + 1);
            availableStatPoints--;
            return true;
        }

        /// <summary>
        /// Allocate multiple points to a stat at once.
        /// Returns the number of points actually allocated.
        /// </summary>
        public int AllocatePoints(IsekaiStatType stat, int amount)
        {
            int allocated = 0;
            int max = GetEffectiveMaxStat();
            for (int i = 0; i < amount; i++)
            {
                if (availableStatPoints <= 0) break;
                if (GetStat(stat) >= max) break;
                SetStat(stat, GetStat(stat) + 1);
                availableStatPoints--;
                allocated++;
            }
            return allocated;
        }

        /// <summary>
        /// Get the number of points to allocate based on held modifier keys.
        /// Normal click = 1, Shift = 5, Ctrl = 20, Ctrl+Shift = 100
        /// </summary>
        public static int GetBulkAmount()
        {
            bool shift = Event.current.shift;
            bool ctrl = Event.current.control;
            if (ctrl && shift) return 100;
            if (ctrl) return 20;
            if (shift) return 5;
            return 1;
        }

        public void OnLevelUp()
        {
            availableStatPoints += IsekaiLevelingSettings.skillPointsPerLevel;
        }

        public int TotalAllocatedPoints => 
            (strength - BASE_STAT_VALUE) + 
            (dexterity - BASE_STAT_VALUE) + 
            (vitality - BASE_STAT_VALUE) + 
            (intelligence - BASE_STAT_VALUE) + 
            (wisdom - BASE_STAT_VALUE) + 
            (charisma - BASE_STAT_VALUE);

        /// <summary>
        /// Get the stat modifier as a multiplier (e.g., 1.0 = 100%, 1.5 = 150%)
        /// Base stat of 5 = 1.0x, each point above/below changes by 2%
        /// </summary>
        public float GetStatMultiplier(IsekaiStatType stat)
        {
            int value = GetStat(stat);
            // 5 = 1.0, 10 = 1.1, 50 = 1.9, 100 = 2.9
            return 1f + (value - BASE_STAT_VALUE) * 0.02f;
        }

        /// <summary>
        /// Get stat bonus as a flat value for additive stats
        /// </summary>
        public float GetStatBonus(IsekaiStatType stat, float perPointBonus)
        {
            int value = GetStat(stat);
            return (value - BASE_STAT_VALUE) * perPointBonus;
        }
    }

    /// <summary>
    /// Static helper to get stat descriptions and icons
    /// </summary>
    public static class IsekaiStatInfo
    {
        public static string GetStatName(IsekaiStatType stat)
        {
            switch (stat)
            {
                case IsekaiStatType.Strength: return "Strength";
                case IsekaiStatType.Dexterity: return "Dexterity";
                case IsekaiStatType.Vitality: return "Vitality";
                case IsekaiStatType.Intelligence: return "Intelligence";
                case IsekaiStatType.Wisdom: return "Wisdom";
                case IsekaiStatType.Charisma: return "Charisma";
                default: return "Unknown";
            }
        }

        public static string GetStatAbbreviation(IsekaiStatType stat)
        {
            switch (stat)
            {
                case IsekaiStatType.Strength: return "STR";
                case IsekaiStatType.Dexterity: return "DEX";
                case IsekaiStatType.Vitality: return "VIT";
                case IsekaiStatType.Intelligence: return "INT";
                case IsekaiStatType.Wisdom: return "WIS";
                case IsekaiStatType.Charisma: return "CHA";
                default: return "???";
            }
        }

        public static string GetStatDescription(IsekaiStatType stat)
        {
            switch (stat)
            {
                case IsekaiStatType.Strength: 
                    return "Increases melee damage and carry capacity.";
                case IsekaiStatType.Dexterity: 
                    return "Improves movement speed, melee dodge, shooting accuracy, melee hit chance, and aiming speed.";
                case IsekaiStatType.Vitality: 
                    return "Boosts injury healing, toxic resistance, damage reduction, natural armor, pain shock threshold, and rest efficiency.";
                case IsekaiStatType.Intelligence: 
                    return "Enhances work speed, research speed, learning rate, and hacking speed. Affects psychic sensitivity with WIS.";
                case IsekaiStatType.Wisdom: 
                    return "Improves mental stability, meditation focus, neural heat recovery, and psyfocus efficiency. Affects psychic sensitivity with INT and neural heat limit with VIT.";
                case IsekaiStatType.Charisma: 
                    return "Improves social impact, negotiation ability, trade prices, animal taming, and arrest success.";
                default: 
                    return "Unknown stat.";
            }
        }

        public static string GetStatEffects(IsekaiStatType stat, int value)
        {
            float mult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.02f;
            string percent = $"{(mult - 1f) * 100f:+0;-0}%";
            float offset = (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.01f;
            string offsetPercent = $"{offset * 100f:+0;-0}%";
            
            switch (stat)
            {
                case IsekaiStatType.Strength:
                    return $"Melee Damage {percent}\nCarry Capacity {percent}";
                case IsekaiStatType.Dexterity:
                {
                    float moveMult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * IsekaiLevelingSettings.DEX_MoveSpeed;
                    string movePercent = $"{(moveMult - 1f) * 100f:+0;-0}%";
                    float aimMult = 1f - (value - 5f) * IsekaiLevelingSettings.DEX_AimingTime;
                    string aimPercent = $"x{Mathf.Clamp(aimMult, 0.05f, 1.5f):F2}";
                    return $"Move Speed {movePercent}\nDodge {offsetPercent}\nShooting Accuracy {offsetPercent}\nMelee Hit {offsetPercent}\nAiming Delay {aimPercent}";
                }
                case IsekaiStatType.Vitality:
                {
                    float regenMult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * IsekaiLevelingSettings.VIT_HealthRegen;
                    string regenPercent = $"{(regenMult - 1f) * 100f:+0;-0}%";
                    float dmgRed = 1f / (1f + Mathf.Max(0f, value - 5f) * IsekaiLevelingSettings.VIT_DamageReduction);
                    string dmgRedPercent = $"x{Mathf.Clamp(dmgRed, 0.05f, 1.1f):F2}";
                    float restMult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.015f;
                    string restPercent = $"{(restMult - 1f) * 100f:+0;-0}%";
                    // Pain threshold & bleed rate scale with VIT health multiplier
                    float vitHealthMult = 1f + Mathf.Max(0f, value - 5f) * IsekaiLevelingSettings.VIT_MaxHealth;
                    float painBonus = (vitHealthMult - 1f) * 0.8f;
                    string painPercent = $"{painBonus:+0.00}";
                    string bleedPercent = vitHealthMult > 1f ? $"x{(1f / vitHealthMult):F2}" : "x1.00";
                    return $"Health Regen {regenPercent}\nToxic Resist {offsetPercent}\nDamage Taken {dmgRedPercent}\nNatural Armor {offsetPercent}\nPain Threshold {painPercent}\nBleed Rate {bleedPercent}\nRest Rate {restPercent}";
                }
                case IsekaiStatType.Intelligence:
                {
                    float workMult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * IsekaiLevelingSettings.INT_WorkSpeed;
                    string workPercent = $"{(workMult - 1f) * 100f:+0;-0}%";
                    float learnMult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * IsekaiLevelingSettings.INT_LearningSpeed;
                    string learnPercent = $"{(learnMult - 1f) * 100f:+0;-0}%";
                    return $"Work Speed {workPercent}\nResearch {percent}\nLearning {learnPercent}\nHacking {percent}";
                }
                case IsekaiStatType.Wisdom:
                {
                    float mentalOffset = (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.003f;
                    string mentalPercent = $"{-mentalOffset * 100f:+0;-0}%";
                    float medOffset = (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.005f;
                    string medPercent = $"{medOffset * 100f:+0;-0}%";
                    float entropyRecov = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.015f;
                    string entropyPercent = $"{(entropyRecov - 1f) * 100f:+0;-0}%";
                    float psyCost = 1f - (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.008f;
                    string psyCostStr = $"x{Mathf.Clamp(psyCost, 0.3f, 1.2f):F2}";
                    return $"Mental Break {mentalPercent}\nMeditation {medPercent}\nNeural Recovery {entropyPercent}\nPsyfocus Cost {psyCostStr}";
                }
                case IsekaiStatType.Charisma:
                {
                    float tradeOffset = (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.003f;
                    string tradePercent = $"{tradeOffset * 100f:+0;-0}%";
                    float tameMult = 1f + (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.015f;
                    string tamePercent = $"{(tameMult - 1f) * 100f:+0;-0}%";
                    float arrestOffset = (value - IsekaiStatAllocation.BASE_STAT_VALUE) * 0.005f;
                    string arrestPercent = $"{arrestOffset * 100f:+0;-0}%";
                    return $"Social {percent}\nNegotiation {percent}\nTrade Price {tradePercent}\nTaming {tamePercent}\nArrest {arrestPercent}";
                }
                default:
                    return "";
            }
        }

        // ==================== CREATURE-SPECIFIC VERSIONS ====================
        // Animals don't research, meditate, negotiate, etc. Show only applicable bonuses.

        public static string GetCreatureStatDescription(IsekaiStatType stat)
        {
            switch (stat)
            {
                case IsekaiStatType.Strength: 
                    return "Increases melee damage and carry capacity.";
                case IsekaiStatType.Dexterity: 
                    return "Improves movement speed, melee dodge chance, and melee hit chance.";
                case IsekaiStatType.Vitality: 
                    return "Boosts max health, injury healing, natural armor, damage reduction, pain threshold, toxic resistance, immunity gain, rest efficiency, and bleed rate.";
                case IsekaiStatType.Intelligence: 
                    return "Currently provides no direct bonus for creatures. Reserved for future use.";
                case IsekaiStatType.Wisdom: 
                    return "Increases XP gain speed. Higher WIS means faster leveling.";
                case IsekaiStatType.Charisma: 
                    return "Currently provides no direct bonus for creatures. Reserved for future use.";
                default: 
                    return "Unknown stat.";
            }
        }

        public static string GetCreatureStatEffects(IsekaiStatType stat, int value, float tamedRetention = 1f)
        {
            // Creatures use the full stat value (not stat-5 like pawns)
            // so base 5 already gives meaningful bonuses.
            float effF = Mathf.Max(0, value);
            
            switch (stat)
            {
                case IsekaiStatType.Strength:
                {
                    float dmgMult = 1f + effF * IsekaiLevelingSettings.STR_MeleeDamage;
                    float scaledDmg = 1f + (dmgMult - 1f) * tamedRetention;
                    string dmgPercent = $"{(scaledDmg - 1f) * 100f:+0;-0}%";
                    float carryMult = 1f + effF * IsekaiLevelingSettings.STR_CarryCapacity;
                    float scaledCarry = 1f + (carryMult - 1f) * tamedRetention;
                    string carryPercent = $"{(scaledCarry - 1f) * 100f:+0;-0}%";
                    float mineMult = 1f + effF * IsekaiLevelingSettings.STR_MiningSpeed;
                    float scaledMine = 1f + (mineMult - 1f) * tamedRetention;
                    string minePercent = $"{(scaledMine - 1f) * 100f:+0;-0}%";
                    return $"Melee Damage {dmgPercent}\nCarry Capacity {carryPercent}\nMining Speed {minePercent}";
                }
                case IsekaiStatType.Dexterity:
                {
                    float moveMult = 1f + effF * IsekaiLevelingSettings.DEX_MoveSpeed;
                    float scaledMove = 1f + (moveMult - 1f) * tamedRetention;
                    string movePercent = $"{(scaledMove - 1f) * 100f:+0;-0}%";
                    float dodge = effF * IsekaiLevelingSettings.DEX_MeleeDodge * tamedRetention;
                    string dodgePercent = $"{dodge * 100f:+0;-0}%";
                    float hit = effF * IsekaiLevelingSettings.DEX_MeleeHitChance * tamedRetention;
                    string hitPercent = $"{hit * 100f:+0;-0}%";
                    return $"Move Speed {movePercent}\nMelee Dodge {dodgePercent}\nMelee Hit {hitPercent}";
                }
                case IsekaiStatType.Vitality:
                {
                    float healthMult = 1f + effF * IsekaiLevelingSettings.VIT_MaxHealth;
                    float scaledHealth = 1f + (healthMult - 1f) * tamedRetention;
                    string healthPercent = $"{(scaledHealth - 1f) * 100f:+0;-0}%";
                    float regenMult = 1f + effF * IsekaiLevelingSettings.VIT_HealthRegen;
                    float scaledRegen = 1f + (regenMult - 1f) * tamedRetention;
                    string regenPercent = $"{(scaledRegen - 1f) * 100f:+0;-0}%";
                    float sharpArmor = effF * 0.008f * tamedRetention;
                    string sharpPercent = $"{sharpArmor * 100f:+0;-0}%";
                    float bluntArmor = effF * 0.01f * tamedRetention;
                    string bluntPercent = $"{bluntArmor * 100f:+0;-0}%";
                    float dmgRed = 1f / (1f + effF * IsekaiLevelingSettings.VIT_DamageReduction);
                    float scaledDR = 1f + (dmgRed - 1f) * tamedRetention;
                    string dmgRedPercent = $"x{Mathf.Clamp(scaledDR, 0.05f, 1.1f):F2}";
                    float painBonus = (healthMult - 1f) * 0.8f * tamedRetention;
                    string painStr = $"{painBonus:+0.00}";
                    float toxicBonus = effF * IsekaiLevelingSettings.VIT_ToxicResist * tamedRetention;
                    string toxicPercent = $"{toxicBonus * 100f:+0;-0}%";
                    float immunityMult = 1f + effF * IsekaiLevelingSettings.VIT_ImmunityGain;
                    float scaledImmunity = 1f + (immunityMult - 1f) * tamedRetention;
                    string immunityPercent = $"{(scaledImmunity - 1f) * 100f:+0;-0}%";
                    float restMult = 1f + effF * 0.015f;
                    float scaledRest = 1f + (restMult - 1f) * tamedRetention;
                    string restPercent = $"{(scaledRest - 1f) * 100f:+0;-0}%";
                    string bleedStr = scaledHealth > 1f ? $"x{(1f / scaledHealth):F2}" : "x1.00";
                    // Lifespan: aging proceeds at 1/factor speed (not affected by tamed retention)
                    float lifeFactor = 1f + effF * IsekaiLevelingSettings.VIT_LifespanFactor;
                    string lifePercent = $"{(lifeFactor - 1f) * 100f:+0;-0}%";
                    return $"Max Health {healthPercent}\nHealth Regen {regenPercent}\nSharp Armor {sharpPercent}\nBlunt Armor {bluntPercent}\nDamage Taken {dmgRedPercent}\nPain Threshold {painStr}\nToxic Resist {toxicPercent}\nImmunity Gain {immunityPercent}\nRest Rate {restPercent}\nBleed Rate {bleedStr}\nLifespan {lifePercent}";
                }
                case IsekaiStatType.Intelligence:
                {
                    float psychMult = 1f + effF * 0.02f;
                    float scaledPsych = 1f + (psychMult - 1f) * tamedRetention;
                    string psychPercent = $"{(scaledPsych - 1f) * 100f:+0;-0}%";
                    float mbOffset = effF * 0.01f * tamedRetention;
                    string mbPercent = $"-{mbOffset * 100f:F0}%";
                    float trainWild = effF * 0.015f;
                    float scaledTrain = 1f - (1f - Mathf.Max(0.05f, 1f - trainWild)) * tamedRetention;
                    string trainPercent = $"-{(1f - Mathf.Max(0.05f, 1f - effF * 0.015f)) * tamedRetention * 100f:F1}%";
                    return $"Psychic Sensitivity {psychPercent}\nMental Break Threshold {mbPercent}\nWildness (Training) {trainPercent}";
                }
                case IsekaiStatType.Wisdom:
                {
                    // WIS XP bonus is not affected by tamed retention
                    float wisMult = 1f + effF * 0.02f;
                    string wisPercent = $"{(wisMult - 1f) * 100f:+0;-0}%";
                    return $"XP Gain {wisPercent}";
                }
                case IsekaiStatType.Charisma:
                {
                    float mktMult = 1f + effF * 0.03f;
                    float scaledMkt = 1f + (mktMult - 1f) * tamedRetention;
                    string mktPercent = $"{(scaledMkt - 1f) * 100f:+0;-0}%";
                    float tameWild = effF * 0.02f;
                    string tamePercent = $"-{(1f - Mathf.Max(0.05f, 1f - tameWild)) * tamedRetention * 100f:F1}%";
                    float nuzzleMult = 1f + effF * 0.015f;
                    float scaledNuzzle = 1f + (nuzzleMult - 1f) * tamedRetention;
                    string nuzzlePercent = $"{(scaledNuzzle - 1f) * 100f:+0;-0}%";
                    return $"Market Value {mktPercent}\nWildness (Taming) {tamePercent}\nNuzzle Chance {nuzzlePercent}";
                }
                default:
                    return "";
            }
        }
    }
}
