using System.Collections.Generic;
using System.Linq;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Injects CompForgeEnhancement onto all weapon and apparel ThingDefs at startup.
    /// Follows the same pattern as MobRankInjector.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ForgeCompInjector
    {
        private static int injectedCount = 0;
        private static int skippedCount = 0;

        static ForgeCompInjector()
        {
            InjectForgeComponents();
            Log.Message($"[Isekai Leveling] Forge system initialized: {injectedCount} items enhanced, {skippedCount} skipped.");
        }

        private static void InjectForgeComponents()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!ShouldHaveForgeComp(def))
                    continue;

                if (def.comps == null)
                    def.comps = new List<CompProperties>();

                bool alreadyHas = def.comps.Any(c => c is CompProperties_ForgeEnhancement);
                if (alreadyHas)
                {
                    skippedCount++;
                    continue;
                }

                def.comps.Add(new CompProperties_ForgeEnhancement());
                injectedCount++;
            }
        }

        /// <summary>
        /// Determine if a ThingDef should receive forge enhancement.
        /// Includes craftable/spawnable weapons and apparel. Excludes frames, blueprints, etc.
        /// </summary>
        private static bool ShouldHaveForgeComp(ThingDef def)
        {
            if (def == null) return false;

            // Must be an item (not a building, mote, projectile, etc.)
            if (def.category != ThingCategory.Item) return false;

            // Must be a weapon or apparel
            if (!def.IsWeapon && !def.IsApparel) return false;

            // Skip unfinished things, corpses, etc.
            if (def.IsFrame || def.isUnfinishedThing) return false;

            // Skip "stuff" resources that vanilla also classifies as weapons
            // (WoodLog is the worst offender: 140k log piles each with a runic UI entry).
            // Real crafted weapons set IsStuff = false; only material resources have stuffProps.
            if (def.IsStuff) return false;

            return true;
        }
    }
}
