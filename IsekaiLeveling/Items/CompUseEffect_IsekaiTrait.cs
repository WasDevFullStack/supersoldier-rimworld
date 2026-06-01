using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling
{
    /// <summary>
    /// CompProperties for Isekai Trait granting items.
    /// When used, grants the specified Isekai trait to the pawn.
    /// </summary>
    public class CompProperties_UseEffectIsekaiTrait : CompProperties_UseEffect
    {
        /// <summary>The defName of the Isekai trait to grant (e.g. "Isekai_Protagonist").</summary>
        public string traitDefName;

        public CompProperties_UseEffectIsekaiTrait()
        {
            compClass = typeof(CompUseEffect_IsekaiTrait);
        }
    }

    /// <summary>
    /// Use effect that grants an Isekai trait when a pawn uses the item.
    /// Handles conflict checking, one-time effects, and visual feedback.
    /// </summary>
    public class CompUseEffect_IsekaiTrait : CompUseEffect
    {
        public CompProperties_UseEffectIsekaiTrait Props => (CompProperties_UseEffectIsekaiTrait)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            if (string.IsNullOrEmpty(Props.traitDefName))
            {
                Log.Error("[Isekai Leveling] CompUseEffect_IsekaiTrait: traitDefName is null or empty.");
                return;
            }

            bool success = IsekaiTraitHelper.AddIsekaiTrait(usedBy, Props.traitDefName);

            if (success)
            {
                // Get the trait label for display
                TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(Props.traitDefName);
                string traitLabel = traitDef?.degreeDatas?[0]?.label ?? Props.traitDefName;

                // Visual feedback
                if (usedBy.Spawned)
                {
                    FleckMaker.Static(usedBy.DrawPos, usedBy.Map, FleckDefOf.PsycastAreaEffect, 1.0f);
                    MoteMaker.ThrowText(
                        usedBy.DrawPos + new Vector3(0f, 0f, 0.6f),
                        usedBy.Map,
                        $"Awakened: {traitLabel.CapitalizeFirst()}!",
                        new Color(1f, 0.84f, 0f), // Gold
                        4f);
                }

                // Notification letter
                string label = $"{usedBy.LabelShort} — {traitLabel.CapitalizeFirst()}!";
                string text = $"{usedBy.LabelShort} has awakened as a {traitLabel}!\n\n" +
                              traitDef?.degreeDatas?[0]?.description;
                Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, usedBy);

                // Consume the item
                if (parent.stackCount > 1)
                    parent.stackCount--;
                else
                    parent.Destroy();
            }
            else
            {
                // Failed — show reason
                Messages.Message(
                    $"{usedBy.LabelShort} cannot use this — they already have a conflicting trait.",
                    usedBy, MessageTypeDefOf.RejectInput, false);
            }
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p == null) return false;

            // Must have IsekaiComponent
            var comp = IsekaiComponent.GetCached(p);
            if (comp == null)
                return "This pawn has no Isekai progression.";

            // Check if pawn already has this trait
            if (IsekaiTraitHelper.HasTrait(p, Props.traitDefName))
                return "Already has this trait.";

            // Check for conflicts
            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(Props.traitDefName);
            if (traitDef?.conflictingTraits != null)
            {
                foreach (var conflict in traitDef.conflictingTraits)
                {
                    if (IsekaiTraitHelper.HasTrait(p, conflict.defName))
                        return $"Conflicts with existing trait: {conflict.degreeDatas?[0]?.label ?? conflict.defName}";
                }
            }

            return true;
        }
    }
}
