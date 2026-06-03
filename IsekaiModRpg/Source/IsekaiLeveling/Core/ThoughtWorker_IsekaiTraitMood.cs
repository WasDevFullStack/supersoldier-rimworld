using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Generic ThoughtWorker for Isekai trait-based mood effects.
    /// Maps ThoughtDef defNames to required traits. Active when pawn has the trait.
    /// Special case: TouchedByDeath only activates after Undying has triggered (cooldown active).
    /// </summary>
    public class ThoughtWorker_IsekaiTraitMood : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || p.story?.traits == null) return ThoughtState.Inactive;

            string defName = def.defName;

            switch (defName)
            {
                case "Isekai_SummonedHeroDisplacement":
                    if (IsekaiTraitHelper.HasTrait(p, IsekaiTraitHelper.SummonedHero))
                        return ThoughtState.ActiveAtStage(0);
                    break;

                case "Isekai_LuckyMood":
                    if (IsekaiTraitHelper.HasTrait(p, IsekaiTraitHelper.Lucky))
                        return ThoughtState.ActiveAtStage(0);
                    break;

                case "Isekai_CursedMood":
                    if (IsekaiTraitHelper.HasTrait(p, IsekaiTraitHelper.CursedLuck))
                        return ThoughtState.ActiveAtStage(0);
                    break;

                case "Isekai_TouchedByDeath":
                    // Only active when pawn has Undying AND has cheated death (cooldown is running)
                    if (IsekaiTraitHelper.HasTrait(p, IsekaiTraitHelper.Undying))
                    {
                        var comp = IsekaiComponent.GetCached(p);
                        if (comp != null && comp.undyingCooldownTick > 0)
                            return ThoughtState.ActiveAtStage(0);
                    }
                    break;
            }

            return ThoughtState.Inactive;
        }
    }
}
