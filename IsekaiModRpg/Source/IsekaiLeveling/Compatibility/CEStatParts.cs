using RimWorld;
using UnityEngine;
using Verse;
using System;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// StatPart for Combat Extended - Aiming Accuracy
    /// DEX stat improves accuracy
    /// Includes safety wrappers to prevent exceptions from crashing UI mods like RimHUD
    /// </summary>
    public class StatPart_CE_AimingAccuracy : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetAimingAccuracyMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetAimingAccuracyMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai DEX ({comp.stats.dexterity}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Aiming Time
    /// DEX stat reduces aiming delay
    /// </summary>
    public class StatPart_CE_AimingTime : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetAimingTimeMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetAimingTimeMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                return $"Isekai DEX ({comp.stats.dexterity}): {percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Recoil
    /// STR stat reduces recoil
    /// </summary>
    public class StatPart_CE_Recoil : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetRecoilMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetRecoilMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                return $"Isekai STR ({comp.stats.strength}): {percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Suppressability
    /// VIT stat reduces suppressability (makes pawn harder to suppress)
    /// </summary>
    public class StatPart_CE_SuppressionResistance : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetSuppressabilityMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetSuppressabilityMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai VIT ({comp.stats.vitality}): {sign}{percent:F0}% suppressability";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Reload Speed
    /// INT stat increases reload speed
    /// </summary>
    public class StatPart_CE_ReloadSpeed : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetReloadSpeedMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetReloadSpeedMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai INT ({comp.stats.intelligence}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Melee Penetration
    /// STR stat increases armor penetration in melee
    /// </summary>
    public class StatPart_CE_MeleePenetration : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetMeleePenetrationMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetMeleePenetrationMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({comp.stats.strength}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Melee Parry Chance
    /// STR stat increases parry chance
    /// </summary>
    public class StatPart_CE_MeleeParry : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float bonus = CECompatibility.GetMeleeParryBonus(comp);
                if (!float.IsNaN(bonus) && !float.IsInfinity(bonus))
                {
                    val += bonus;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float bonus = CECompatibility.GetMeleeParryBonus(comp);
                if (Mathf.Approximately(bonus, 0f)) return null;
                
                float percent = bonus * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({comp.stats.strength}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Counter-Parry Damage Bonus
    /// STR stat increases counter-attack damage
    /// </summary>
    public class StatPart_CE_MeleeCounterParry : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetMeleeCounterParryMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetMeleeCounterParryMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({comp.stats.strength}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Melee Crit Chance
    /// STR stat increases critical hit chance
    /// </summary>
    public class StatPart_CE_MeleeCrit : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float bonus = CECompatibility.GetMeleeCritBonus(comp);
                if (!float.IsNaN(bonus) && !float.IsInfinity(bonus))
                {
                    val += bonus;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float bonus = CECompatibility.GetMeleeCritBonus(comp);
                if (Mathf.Approximately(bonus, 0f)) return null;
                
                float percent = bonus * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({comp.stats.strength}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Melee Damage
    /// STR stat directly increases melee damage output
    /// </summary>
    public class StatPart_CE_MeleeDamage : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetMeleeDamageMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetMeleeDamageMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({comp.stats.strength}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - Toughness (damage reduction)
    /// VIT stat increases damage resistance
    /// </summary>
    public class StatPart_CE_Toughness : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = CECompatibility.GetToughnessMultiplier(comp);
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = CECompatibility.GetToughnessMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai VIT ({comp.stats.vitality}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - CarryBulk (inventory capacity)
    /// STR stat increases how much can be carried in inventory
    /// </summary>
    public class StatPart_CE_CarryBulk : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                // Use same multiplier as vanilla carry capacity
                int strStat = comp.stats.strength;
                float bonus = (strStat - 5) * IsekaiLevelingSettings.STR_CarryCapacity;
                float multiplier = 1f + Mathf.Clamp(bonus, -0.2f, 5.0f);
                
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                int strStat = comp.stats.strength;
                float bonus = (strStat - 5) * IsekaiLevelingSettings.STR_CarryCapacity;
                float multiplier = 1f + Mathf.Clamp(bonus, -0.2f, 5.0f);
                
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({strStat}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Combat Extended - CarryWeight (mass capacity in kg)
    /// STR stat increases how much weight can be carried
    /// </summary>
    public class StatPart_CE_CarryWeight : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                // Use same multiplier as vanilla carry capacity
                int strStat = comp.stats.strength;
                float bonus = (strStat - 5) * IsekaiLevelingSettings.STR_CarryCapacity;
                float multiplier = 1f + Mathf.Clamp(bonus, -0.2f, 5.0f);
                
                if (!float.IsNaN(multiplier) && !float.IsInfinity(multiplier))
                {
                    val *= multiplier;
                }
            }
            catch (Exception) { }
        }
        
        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.CombatExtendedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                int strStat = comp.stats.strength;
                float bonus = (strStat - 5) * IsekaiLevelingSettings.STR_CarryCapacity;
                float multiplier = 1f + Mathf.Clamp(bonus, -0.2f, 5.0f);
                
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai STR ({strStat}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
}
