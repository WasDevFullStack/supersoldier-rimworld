using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Social thought that makes all pawns dislike pawns with the Antagonist trait.
    /// The thought is nullified if the observer also has the Antagonist trait.
    /// </summary>
    public class ThoughtWorker_AntagonistOpinion : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            if (p == null || other == null) return false;
            if (!p.RaceProps.Humanlike || !other.RaceProps.Humanlike) return false;
            if (other.story?.traits == null) return false;

            // Check if the other pawn has the Antagonist trait
            if (!IsekaiTraitHelper.HasTrait(other, IsekaiTraitHelper.Antagonist))
                return false;

            // Nullify if the observer is also an Antagonist (villains respect each other)
            if (p.story?.traits != null && IsekaiTraitHelper.HasTrait(p, IsekaiTraitHelper.Antagonist))
                return false;

            return ThoughtState.ActiveAtStage(0);
        }

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            // Not a personal thought, only social
            return ThoughtState.Inactive;
        }
    }
}
