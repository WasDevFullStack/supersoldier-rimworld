using RimWorld;
using UnityEngine;
using Verse;
using System;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// StatPart for Vanilla Psycasts Expanded - Meditation Focus Gain
    /// WIS stat increases meditation effectiveness
    /// Includes safety wrappers to prevent exceptions from crashing UI mods like RimHUD
    /// </summary>
    public class StatPart_VPE_MeditationFocus : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.VanillaPsycastsExpandedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                // Only apply to psycasters
                if (!VPECompatibility.HasPsylink(pawn)) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = VPECompatibility.GetMeditationMultiplier(comp);
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
                if (!ModCompatibility.VanillaPsycastsExpandedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                if (!VPECompatibility.HasPsylink(pawn)) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = VPECompatibility.GetMeditationMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai WIS ({comp.stats.wisdom}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Vanilla Psycasts Expanded - Neural Heat Recovery
    /// INT stat increases how fast neural heat dissipates
    /// </summary>
    public class StatPart_VPE_NeuralHeatRecovery : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.VanillaPsycastsExpandedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                if (!VPECompatibility.HasPsylink(pawn)) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = VPECompatibility.GetNeuralHeatRecoveryMultiplier(comp);
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
                if (!ModCompatibility.VanillaPsycastsExpandedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                if (!VPECompatibility.HasPsylink(pawn)) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = VPECompatibility.GetNeuralHeatRecoveryMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai INT ({comp.stats.intelligence}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
    
    /// <summary>
    /// StatPart for Vanilla Psycasts Expanded - Psyfocus Sensitivity  
    /// WIS stat affects psyfocus gains from meditation and other sources
    /// </summary>
    public class StatPart_VPE_PsyfocusSensitivity : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.VanillaPsycastsExpandedActive) return;
                if (!req.HasThing) return;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return;
                
                if (!VPECompatibility.HasPsylink(pawn)) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return;
                
                float multiplier = VPECompatibility.GetPsyfocusSensitivityMultiplier(comp);
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
                if (!ModCompatibility.VanillaPsycastsExpandedActive) return null;
                if (!req.HasThing) return null;
                
                Pawn pawn = req.Thing as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed) return null;
                
                if (!VPECompatibility.HasPsylink(pawn)) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp?.stats == null) return null;
                
                float multiplier = VPECompatibility.GetPsyfocusSensitivityMultiplier(comp);
                if (Mathf.Approximately(multiplier, 1f)) return null;
                
                float percent = (multiplier - 1f) * 100f;
                string sign = percent >= 0 ? "+" : "";
                return $"Isekai WIS ({comp.stats.wisdom}): {sign}{percent:F0}%";
            }
            catch (Exception) { return null; }
        }
    }
}
