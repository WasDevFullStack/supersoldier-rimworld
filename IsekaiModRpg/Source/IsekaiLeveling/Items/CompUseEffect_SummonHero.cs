using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling
{
    /// <summary>
    /// CompProperties for the Summoning Circle Scroll.
    /// When used, spawns a new random adult colonist with the Summoned Hero trait.
    /// </summary>
    public class CompProperties_UseEffectSummonHero : CompProperties_UseEffect
    {
        public CompProperties_UseEffectSummonHero()
        {
            compClass = typeof(CompUseEffect_SummonHero);
        }
    }

    /// <summary>
    /// Use effect that summons a new colonist with the Summoned Hero trait.
    /// The item does NOT affect the user — it creates a brand new pawn.
    /// </summary>
    public class CompUseEffect_SummonHero : CompUseEffect
    {
        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);

            if (usedBy?.Map == null)
            {
                Log.Error("[Isekai Leveling] CompUseEffect_SummonHero: usedBy has no map.");
                return;
            }

            Map map = usedBy.Map;

            // Generate a random adult colonist for the player faction
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind: PawnKindDefOf.Colonist,
                faction: Faction.OfPlayer,
                context: PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true,
                allowDead: false,
                allowDowned: false
            );

            Pawn summonedPawn = PawnGenerator.GeneratePawn(request);
            if (summonedPawn == null)
            {
                Log.Error("[Isekai Leveling] CompUseEffect_SummonHero: Failed to generate pawn.");
                return;
            }

            // Find a spawn position near the user
            IntVec3 spawnPos = usedBy.Position;
            if (!CellFinder.TryFindRandomCellNear(usedBy.Position, map, 3, 
                    (IntVec3 c) => c.Standable(map) && !c.Fogged(map), out IntVec3 nearPos))
            {
                nearPos = spawnPos;
            }

            // Spawn the pawn
            GenSpawn.Spawn(summonedPawn, nearPos, map);

            // Grant the Summoned Hero trait
            IsekaiTraitHelper.AddIsekaiTrait(summonedPawn, IsekaiTraitHelper.SummonedHero);

            // Visual feedback
            FleckMaker.Static(nearPos.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, 2.0f);
            MoteMaker.ThrowText(
                nearPos.ToVector3Shifted() + new Vector3(0f, 0f, 0.6f),
                map,
                "A Hero Has Been Summoned!",
                new Color(1f, 0.84f, 0f), // Gold
                6f);

            // Get trait label for letter
            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(IsekaiTraitHelper.SummonedHero);
            string traitLabel = traitDef?.degreeDatas?[0]?.label ?? "Summoned Hero";

            // Notification letter
            string label = $"A Hero Arrives — {summonedPawn.LabelShort}!";
            string text = $"A summoning circle has torn open a rift between worlds!\n\n" +
                          $"{summonedPawn.LabelShort} has been pulled from another dimension and joins your colony as a {traitLabel}.\n\n" +
                          $"• x3 XP multiplier\n" +
                          $"• +1 bonus stat point per level\n" +
                          $"• Highly resistant to mental breaks\n" +
                          $"• +25% social impact";
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, summonedPawn);

            // Consume the item
            if (parent.stackCount > 1)
                parent.stackCount--;
            else
                parent.Destroy();
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (p == null) return false;

            // Just need to be spawned on a map
            if (!p.Spawned || p.Map == null)
                return "Must be used on a map.";

            return true;
        }
    }
}
