using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling
{
    /// <summary>
    /// CompProperties for Star Fragment use effect.
    /// When consumed, grants starPoints Star Points (passive/constellation points) to the pawn.
    /// </summary>
    public class CompProperties_UseEffectStarFragment : CompProperties_UseEffect
    {
        public int starPoints = 1;

        public CompProperties_UseEffectStarFragment()
        {
            compClass = typeof(CompUseEffect_StarFragment);
        }
    }

    /// <summary>
    /// Use effect that grants Star Points (passive tree points) when a pawn absorbs
    /// a Star Fragment.
    /// </summary>
    public class CompUseEffect_StarFragment : CompUseEffect
    {
        public CompProperties_UseEffectStarFragment Props => (CompProperties_UseEffectStarFragment)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            var comp = usedBy.GetComp<IsekaiComponent>();
            if (comp == null) return;

            int pts = Mathf.Max(1, Props.starPoints);
            comp.passiveTree.availablePoints += pts;
            comp.passiveTree.starFragmentsAbsorbed++;

            // Visual feedback
            if (usedBy.Spawned)
            {
                FleckMaker.Static(usedBy.DrawPos, usedBy.Map, FleckDefOf.PsycastAreaEffect, 0.5f);
                MoteMaker.ThrowText(
                    usedBy.DrawPos + new Vector3(0f, 0f, 0.6f),
                    usedBy.Map,
                    $"+{pts} Star Point" + (pts > 1 ? "s" : "") + " + Multiclass Unlock",
                    new Color(0.85f, 0.75f, 1f),
                    3.5f);
            }

            // Consume one from the stack
            if (parent.stackCount > 1)
                parent.stackCount--;
            else
                parent.Destroy();
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p.GetComp<IsekaiComponent>() == null)
                return "Can only be absorbed by an Isekai adventurer.";
            return true;
        }
    }
}
