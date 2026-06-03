using RimWorld;
using Verse;
using UnityEngine;
using IsekaiLeveling.SkillTree;

namespace IsekaiLeveling
{
    /// <summary>
    /// Use effect for the Respec Orb item.
    /// When used by a pawn, resets all allocated stat points back to base values (5),
    /// returns all spent points, AND fully resets the constellation tree (class + all nodes).
    /// The pawn keeps their level and XP. No usage limit.
    /// </summary>
    public class CompUseEffect_Respec : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            
            var comp = usedBy.GetComp<IsekaiComponent>();
            if (comp == null)
            {
                Messages.Message("Isekai_Respec_NoPawn".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            
            // Calculate total spent stat points (everything above base 5)
            int spentPoints = 0;
            spentPoints += Mathf.Max(0, comp.stats.strength - IsekaiStatAllocation.BASE_STAT_VALUE);
            spentPoints += Mathf.Max(0, comp.stats.dexterity - IsekaiStatAllocation.BASE_STAT_VALUE);
            spentPoints += Mathf.Max(0, comp.stats.vitality - IsekaiStatAllocation.BASE_STAT_VALUE);
            spentPoints += Mathf.Max(0, comp.stats.intelligence - IsekaiStatAllocation.BASE_STAT_VALUE);
            spentPoints += Mathf.Max(0, comp.stats.wisdom - IsekaiStatAllocation.BASE_STAT_VALUE);
            spentPoints += Mathf.Max(0, comp.stats.charisma - IsekaiStatAllocation.BASE_STAT_VALUE);

            bool hasStatPoints = spentPoints > 0;
            bool hasTreeNodes = comp.passiveTree != null && comp.passiveTree.UnlockedCount > 0;

            if (!hasStatPoints && !hasTreeNodes)
            {
                Messages.Message("Isekai_Respec_NoPoints".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            
            // Reset all stats to base
            comp.stats.strength = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.dexterity = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.vitality = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.intelligence = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.wisdom = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.charisma = IsekaiStatAllocation.BASE_STAT_VALUE;
            
            // Return all spent stat points
            comp.stats.availableStatPoints += spentPoints;

            // Full constellation respec — resets class, all nodes, returns constellation points
            // Note: this does NOT count toward the skill tree respec limit
            int constellationRefunded = 0;
            if (comp.passiveTree != null)
            {
                constellationRefunded = comp.passiveTree.FullRespec();
            }
            
            // Update rank trait based on new stats
            PawnStatGenerator.UpdateRankTraitFromStats(usedBy, comp);
            
            // Visual effect
            if (usedBy.Spawned)
            {
                FleckMaker.ThrowLightningGlow(usedBy.DrawPos, usedBy.Map, 1.5f);
                MoteMaker.ThrowText(usedBy.DrawPos, usedBy.Map, 
                    "Isekai_Respec_Effect".Translate(spentPoints), Color.cyan);
            }
            
            // Notification
            Messages.Message(
                "Isekai_Respec_Success_Full".Translate(usedBy.LabelShort, spentPoints, constellationRefunded), 
                usedBy, MessageTypeDefOf.PositiveEvent, false);
            
            Log.Message($"[Isekai Leveling] {usedBy.LabelShort} used Respec Orb: {spentPoints} stat points + {constellationRefunded} constellation points returned.");
        }
        
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var comp = p.GetComp<IsekaiComponent>();
            if (comp == null)
            {
                return "Isekai_Respec_NoPawn".Translate();
            }
            
            // Check if there's anything to respec (stats or constellation)
            bool hasStatPoints = comp.stats.strength > IsekaiStatAllocation.BASE_STAT_VALUE ||
                comp.stats.dexterity > IsekaiStatAllocation.BASE_STAT_VALUE ||
                comp.stats.vitality > IsekaiStatAllocation.BASE_STAT_VALUE ||
                comp.stats.intelligence > IsekaiStatAllocation.BASE_STAT_VALUE ||
                comp.stats.wisdom > IsekaiStatAllocation.BASE_STAT_VALUE ||
                comp.stats.charisma > IsekaiStatAllocation.BASE_STAT_VALUE;
            bool hasTreeNodes = comp.passiveTree != null && comp.passiveTree.UnlockedCount > 0;

            if (!hasStatPoints && !hasTreeNodes)
            {
                return "Isekai_Respec_NoPoints".Translate();
            }
            
            return true;
        }
    }
}
