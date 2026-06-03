using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace IsekaiLeveling.WorldBoss
{
    /// <summary>
    /// Shared utility methods for the World Boss system.
    /// </summary>
    public static class WorldBossUtility
    {
        /// <summary>
        /// Makes a creature hostile to the player using the best available method.
        /// Mechanoids → Mechanoid faction + assault Lord, already hostile → leave as-is,
        /// animals → Manhunter mental state, fallback → AncientsHostile faction.
        /// </summary>
        public static void MakeCreatureHostile(Pawn creature)
        {
            if (creature.RaceProps.IsMechanoid)
            {
                creature.SetFaction(Faction.OfMechanoids, null);
                // Mechs don't use mental states — they need an explicit assault Lord
                // (the same machinery vanilla mech raids use) or they just stand at
                // spawn. Without this, mech world bosses never chase colonists down.
                // canTimeoutOrFlee=false so the world boss fights to the death rather
                // than fleeing at low HP — observed with an SSS Conflagrator running
                // off-map after taking ~5% damage.
                if (creature.Map != null)
                {
                    LordMaker.MakeNewLord(
                        Faction.OfMechanoids,
                        new LordJob_AssaultColony(Faction.OfMechanoids,
                            canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                            useAvoidGridSmart: false, canSteal: false),
                        creature.Map,
                        new List<Pawn> { creature });
                }
            }
            else if (creature.Faction != null && creature.Faction.HostileTo(Faction.OfPlayer))
            {
                // Already hostile faction — leave as-is
            }
            else
            {
                // Try to assign to a hostile faction first for consistent behavior
                var hostileFaction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile)
                    ?? Faction.OfInsects;
                if (hostileFaction != null)
                {
                    creature.SetFaction(hostileFaction, null);
                }
            }

            // Always try to apply manhunter on top — this ensures the creature
            // actively hunts and attacks pawns rather than just being "hostile".
            // No-op for mechs (mindState is null), which is why the mech branch
            // above gets its own Lord assignment instead.
            if (creature.mindState?.mentalStateHandler != null)
            {
                creature.mindState.mentalStateHandler.TryStartMentalState(
                    MentalStateDefOf.ManhunterPermanent, forced: true);
            }
        }
    }
}
