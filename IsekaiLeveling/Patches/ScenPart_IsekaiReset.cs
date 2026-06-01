using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Scenario part that resets all starting player pawns to level 1 with base stats
    /// and grants the Protagonist trait. Used by the Protagonist scenario.
    /// </summary>
    public class ScenPart_IsekaiReset : ScenPart
    {
        public override void PostGameStart()
        {
            base.PostGameStart();

            Map map = Find.CurrentMap;
            if (map == null) return;

            TraitDef protagonistTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Isekai_Protagonist");

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                // Grant Protagonist trait if not already present
                if (protagonistTrait != null && pawn.story?.traits != null
                    && !pawn.story.traits.HasTrait(protagonistTrait))
                {
                    pawn.story.traits.GainTrait(new Trait(protagonistTrait, 0));
                }

                IsekaiComponent comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) continue;

                PawnStatGenerator.ResetToNewborn(pawn, comp);
                comp.statsInitialized = true;
            }
        }
    }
}
