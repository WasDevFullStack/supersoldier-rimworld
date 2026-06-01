using RimWorld;
using UnityEngine;
using Verse;
using System;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// StatPart for Hospitality - Recruitment Chance (applied to NegotiationAbility)
    /// CHA stat improves recruitment
    /// Includes safety wrappers to prevent exceptions from crashing UI mods like RimHUD
    /// </summary>
    public class StatPart_Hospitality_Recruitment : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.HospitalityActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = HospitalityCompatibility.GetRecruitmentMultiplier(comp);
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
                if (!ModCompatibility.HospitalityActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = HospitalityCompatibility.GetRecruitmentMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai CHA ({comp.stats.charisma}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for better social interactions (applies to SocialImpact)
    /// CHA stat improves relationship building
    /// </summary>
    public class StatPart_Hospitality_SocialImpact : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.HospitalityActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = HospitalityCompatibility.GetRelationshipGainMultiplier(comp);
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
                if (!ModCompatibility.HospitalityActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = HospitalityCompatibility.GetRelationshipGainMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai CHA ({comp.stats.charisma}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
}
