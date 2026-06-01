using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Injects the MobRankComponent into all applicable pawn defs at startup
    /// This runs during game initialization to add ranks to all creatures
    /// </summary>
    [StaticConstructorOnStartup]
    public static class MobRankInjector
    {
        private static int injectedCount = 0;
        private static int skippedCount = 0;

        static MobRankInjector()
        {
            InjectMobRankComponents();
            Log.Message($"[Isekai Leveling] Mob Rank system initialized: {injectedCount} creatures ranked, {skippedCount} skipped.");
        }

        /// <summary>
        /// Inject MobRankComponent into all eligible pawn defs
        /// </summary>
        private static void InjectMobRankComponents()
        {
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!ShouldHaveMobRank(def))
                {
                    continue;
                }

                // Check if already has the component
                if (def.comps == null)
                {
                    def.comps = new List<CompProperties>();
                }

                bool alreadyHas = def.comps.Any(c => c is CompProperties_MobRank);
                if (alreadyHas)
                {
                    skippedCount++;
                    continue;
                }

                // Add the component
                def.comps.Add(new CompProperties_MobRank());
                
                // Inject ITab_CreatureStats so tamed/allied creatures show a Status tab
                InjectCreatureITab(def);
                
                injectedCount++;
            }
        }

        /// <summary>
        /// Inject the creature stats ITab into a ThingDef so the Status tab appears
        /// when selecting tamed/allied creatures. Safe to call multiple times.
        /// </summary>
        private static void InjectCreatureITab(ThingDef def)
        {
            var tabType = typeof(UI.ITab_CreatureStats);
            
            if (def.inspectorTabs == null)
                def.inspectorTabs = new List<System.Type>();
            if (def.inspectorTabsResolved == null)
                def.inspectorTabsResolved = new List<InspectTabBase>();
            
            if (!def.inspectorTabs.Contains(tabType))
            {
                def.inspectorTabs.Add(tabType);
                def.inspectorTabsResolved.Add(InspectTabManager.GetSharedInstance(tabType));
            }
        }

        /// <summary>
        /// Determine if a ThingDef should have mob ranking
        /// Only non-humanlike creatures get mob ranks (animals, insects, mechanoids, anomalies)
        /// All humanlike pawns (colonists, raiders, traders) use the Isekai stat system instead
        /// </summary>
        private static bool ShouldHaveMobRank(ThingDef def)
        {
            // Must be a pawn
            if (def.race == null) return false;
            if (def.category != ThingCategory.Pawn) return false;

            // EXCLUDE all humanlike pawns - they use Isekai leveling system instead
            // This includes colonists, raiders, traders, prisoners, etc.
            if (def.race.Humanlike) return false;

            // EXCLUDE vehicles (Vanilla Vehicles Expanded / Vehicle Framework / SRTS)
            // Vehicles are inert non-combatant entities that shouldn't be ranked
            string className = def.thingClass?.FullName ?? "";
            if (className.IndexOf("Vehicle", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (className.StartsWith("SRTS", System.StringComparison.OrdinalIgnoreCase)) return false;
            string defName = def.defName ?? "";
            if (defName.StartsWith("VVE_", System.StringComparison.OrdinalIgnoreCase) ||
                defName.IndexOf("Vehicle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.StartsWith("DVVE_", System.StringComparison.OrdinalIgnoreCase) ||
                defName.StartsWith("SRTS_", System.StringComparison.OrdinalIgnoreCase)) return false;
            // Check comps for vehicle-related components
            if (def.comps != null)
            {
                foreach (var comp in def.comps)
                {
                    string compClass = comp?.compClass?.FullName ?? "";
                    if (compClass.IndexOf("Vehicle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        compClass.StartsWith("SRTS", System.StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            // Animals - include
            if (def.race.Animal) return true;

            // Insects - include
            if (def.race.Insect) return true;

            // Mechanoids - include
            if (def.race.IsMechanoid) return true;

            // Anything else with a race (anomaly entities, etc.) - include
            return true;
        }

        /// <summary>
        /// Get the MobRankComponent from a pawn, if it has one
        /// </summary>
        public static MobRankComponent GetMobRank(this Pawn pawn)
        {
            if (pawn == null) return null;
            return pawn.GetComp<MobRankComponent>();
        }

        /// <summary>
        /// Returns true when this pawn's creature category has been opted out of the
        /// mob ranking system in mod settings. This is the single source of truth for
        /// "should we touch this creature's rank/stats/XP at all" — used by the rank
        /// roller, the XP awarder, and the Status tab so all three agree.
        /// </summary>
        public static bool IsExcludedFromRanking(Pawn pawn)
        {
            if (pawn == null) return true;
            // Humanlikes never get mob ranks — they use the IsekaiComponent path.
            if (pawn.RaceProps.Humanlike) return true;
            if (IsekaiLevelingSettings.excludeAnimalsFromRanking && pawn.RaceProps.Animal) return true;
            if (IsekaiLevelingSettings.excludeMechsFromRanking && pawn.RaceProps.IsMechanoid) return true;
            if (IsekaiLevelingSettings.excludeEntitiesFromRanking && IsAnomalyEntity(pawn)) return true;
            return false;
        }

        /// <summary>
        /// Check if a pawn should display mob rank in its inspect string.
        /// </summary>
        public static bool ShouldShowMobRank(Pawn pawn)
        {
            return !IsExcludedFromRanking(pawn);
        }

        /// <summary>
        /// Check if a pawn is an Anomaly DLC entity (not a regular animal or mechanoid).
        /// Detects creatures from the Anomaly DLC by checking the mod content pack
        /// and race type — entities are non-animal, non-mechanoid, non-humanlike creatures.
        /// </summary>
        public static bool IsAnomalyEntity(Pawn pawn)
        {
            if (pawn == null) return false;
            if (pawn.RaceProps.Humanlike) return false;
            if (pawn.RaceProps.Animal) return false;
            if (pawn.RaceProps.IsMechanoid) return false;
            
            // Check mod content pack — Anomaly DLC entities come from ludeon.rimworld.anomaly
            string packageId = pawn.def.modContentPack?.PackageId;
            if (packageId != null && packageId.IndexOf("anomaly", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            
            // Fallback: check defName for known anomaly creature patterns
            string defName = pawn.def.defName.ToLower();
            if (defName.Contains("sightsteal") || defName.Contains("revenant") || 
                defName.Contains("shambler") || defName.Contains("gorehulk") ||
                defName.Contains("fleshmass") || defName.Contains("chimera") ||
                defName.Contains("horror") || defName.Contains("noctol") ||
                defName.Contains("metalhorror") || defName.Contains("devourer"))
                return true;
            
            return false;
        }
    }
}
