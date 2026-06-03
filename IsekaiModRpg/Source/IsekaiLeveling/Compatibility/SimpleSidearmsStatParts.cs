using RimWorld;
using Verse;
using System;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// StatPart for Weapon Swap Speed (DEX increases swap speed)
    /// Includes safety wrappers to prevent exceptions from crashing UI mods like RimHUD
    /// </summary>
    public class StatPart_IsekaiWeaponSwapSpeed : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!ModCompatibility.SimpleSidearmsActive) return;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return;
                if (pawn.Dead || pawn.Destroyed) return;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;
                
                float mult = SimpleSidearmsCompatibility.GetWeaponSwapSpeedMultiplier(comp);
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
                if (!ModCompatibility.SimpleSidearmsActive) return null;
                if (!req.HasThing || !(req.Thing is Pawn pawn)) return null;
                if (pawn.Dead || pawn.Destroyed) return null;
                
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return null;
                
                float mult = SimpleSidearmsCompatibility.GetWeaponSwapSpeedMultiplier(comp);
                if (mult == 1f) return null;
                
                float pct = (mult - 1f) * 100f;
                return $"Isekai DEX ({comp.stats.dexterity}): {pct:+0;-0}%";
            }
            catch (Exception) { return null; }
        }
    }
}
