using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace IsekaiLeveling
{
    /// <summary>
    /// CompProperties for mana core use effect.
    /// xpAmount is the flat amount of XP granted when a pawn absorbs a core.
    /// </summary>
    public class CompProperties_UseEffectManaCore : CompProperties_UseEffect
    {
        public int xpAmount = 1000;

        public CompProperties_UseEffectManaCore()
        {
            compClass = typeof(CompUseEffect_ManaCore);
        }
    }

    /// <summary>
    /// Use effect that grants Isekai XP when a pawn absorbs a mana core.
    /// Paired with CompUsable in XML to provide the right-click "Absorb" option.
    /// Supports bulk absorption: right-clicking a stack shows "Absorb all" in addition to the single-core option.
    /// </summary>
    public class CompUseEffect_ManaCore : CompUseEffect
    {
        /// <summary>
        /// When > 0, the next DoEffect call absorbs this many cores instead of 1.
        /// Set by the "Absorb all" float menu option. Reset on use or when the menu reopens.
        /// </summary>
        private int pendingBulkAbsorb;

        public CompProperties_UseEffectManaCore Props => (CompProperties_UseEffectManaCore)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            var comp = usedBy.GetComp<IsekaiComponent>();
            if (comp == null) return;

            // Determine how many cores to absorb (clamped to actual stack)
            int count = pendingBulkAbsorb > 0 ? Math.Min(pendingBulkAbsorb, parent.stackCount) : 1;
            pendingBulkAbsorb = 0;

            int xpPerCore = Props.xpAmount;
            if (xpPerCore <= 0) xpPerCore = 1;
            int totalXp = xpPerCore * count;

            comp.GainXP(totalXp, "Isekai_ManaCore_Source".Translate());

            // Notify player for bulk absorption
            if (count > 1)
            {
                Messages.Message(
                    "Isekai_ManaCore_BulkAbsorb".Translate(usedBy.LabelShort, count, totalXp.ToString("N0")),
                    usedBy, MessageTypeDefOf.PositiveEvent, false);
            }

            // Play visual effect
            if (usedBy.Spawned)
            {
                FleckMaker.Static(usedBy.DrawPos, usedBy.Map, FleckDefOf.PsycastAreaEffect, 0.6f);
            }

            // Consume cores from the stack
            if (parent.stackCount > count)
                parent.stackCount -= count;
            else
                parent.Destroy();
        }

        /// <summary>
        /// Adds "Absorb all mana cores (X)" option when the stack has 2+ cores.
        /// Resets any stale pending bulk count whenever the menu opens.
        /// </summary>
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            // Reset any stale pending bulk absorb whenever the menu is opened
            pendingBulkAbsorb = 0;
            return GetBulkAbsorbOptions(selPawn);
        }

        private IEnumerable<FloatMenuOption> GetBulkAbsorbOptions(Pawn selPawn)
        {
            if (parent.stackCount <= 1) yield break;

            AcceptanceReport report = CanBeUsedBy(selPawn);
            int count = parent.stackCount;
            int totalXp = Props.xpAmount * count;
            string label = "Isekai_ManaCore_AbsorbAll".Translate(count, totalXp.ToString("N0"));

            if (!report.Accepted)
            {
                yield return new FloatMenuOption(label + " (" + report.Reason + ")", null);
                yield break;
            }

            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + " (" + "NoPath".Translate() + ")", null);
                yield break;
            }

            yield return new FloatMenuOption(label, () =>
            {
                pendingBulkAbsorb = parent.stackCount;
                selPawn.jobs.TryTakeOrderedJob(
                    JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("UseItem"), parent),
                    JobTag.Misc);
            });
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p.GetComp<IsekaiComponent>() == null)
                return "Isekai_ManaCore_NoPawn".Translate();
            return true;
        }
    }
}
