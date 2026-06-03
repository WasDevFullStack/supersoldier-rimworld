using RimWorld;
using Verse;
using System;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// StatPart for Hygiene Need Rate (WIS reduces need rate)
    /// Includes safety wrappers to prevent exceptions from crashing UI mods like RimHUD
    /// </summary>
    public class StatPart_IsekaiHygieneRate : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.DubsBadHygieneActive) return;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return;
                if (pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;
                
                float mult = DBHCompatibility.GetHygieneNeedMultiplier(comp);
                if (!float.IsNaN(mult) && !float.IsInfinity(mult))
                {
                    val *= mult;
                }
            }
            catch (Exception) { }
        }

        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.DubsBadHygieneActive) return null;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return null;
                if (pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return null;
                
                float mult = DBHCompatibility.GetHygieneNeedMultiplier(comp);
                if (mult == 1f) return null;
                
                float pct = (mult - 1f) * 100f;
                return $"Isekai WIS ({comp.stats.wisdom}): {pct:+0;-0}%";
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// StatPart for Bladder Need Rate (WIS reduces need rate)
    /// </summary>
    public class StatPart_IsekaiBladderRate : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.DubsBadHygieneActive) return;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return;
                if (pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;
                
                // Use the same multiplier for bladder rate
                float mult = DBHCompatibility.GetHygieneNeedMultiplier(comp);
                if (!float.IsNaN(mult) && !float.IsInfinity(mult))
                {
                    val *= mult;
                }
            }
            catch (Exception) { }
        }

        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!ModCompatibility.DubsBadHygieneActive) return null;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return null;
                if (pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return null;
                
                float mult = DBHCompatibility.GetHygieneNeedMultiplier(comp);
                if (mult == 1f) return null;
                
                float pct = (mult - 1f) * 100f;
                return $"Isekai WIS ({comp.stats.wisdom}): {pct:+0;-0}%";
            }
            catch (Exception) { return null; }
        }
    }
}
