using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using IsekaiLeveling.Effects;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Injects the IsekaiComponent into humanlike pawns
    /// </summary>
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.InitializeComps))]
    public static class Patch_InjectComponent
    {
        [HarmonyPostfix]
        public static void Postfix(ThingWithComps __instance)
        {
            if (__instance is Pawn pawn && pawn.RaceProps.Humanlike)
            {
                // Check if already has component
                if (pawn.GetComp<IsekaiComponent>() == null)
                {
                    var comp = new IsekaiComponent();
                    comp.parent = pawn;
                    pawn.AllComps.Add(comp);

                    // Only generate stats when NOT loading from a save file.
                    // During save/load, InitializeComps is called inside ExposeData
                    // while Scribe.mode == LoadingVars. PostExposeData will then
                    // restore the saved values, so generating here would be wasted work.
                    if (Scribe.mode != LoadSaveMode.LoadingVars)
                    {
                        // NOTE: During PawnGenerator.GeneratePawn, ageTracker may not
                        // exist yet (or may have age 0) at this point. If ShouldGenerateStats
                        // returns false, InitializePawnStats does nothing — do NOT mark
                        // statsInitialized so PostSpawnSetup / GeneratePawn postfix can retry.
                        if (PawnStatGenerator.ShouldGenerateStats(pawn))
                        {
                            PawnStatGenerator.InitializePawnStats(pawn, comp);
                            comp.statsInitialized = true;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Injects the Isekai ITab into the Human ThingDef at startup
    /// </summary>
    [HarmonyPatch(typeof(DefGenerator), nameof(DefGenerator.GenerateImpliedDefs_PostResolve))]
    public static class Patch_InjectITab
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Find all humanlike pawn ThingDefs and add our ITab
            foreach (var thingDef in DefDatabase<ThingDef>.AllDefs)
            {
                if (thingDef.race != null && thingDef.race.Humanlike)
                {
                    if (thingDef.inspectorTabs == null)
                    {
                        thingDef.inspectorTabs = new List<System.Type>();
                    }
                    
                    if (thingDef.inspectorTabsResolved == null)
                    {
                        thingDef.inspectorTabsResolved = new List<InspectTabBase>();
                    }
                    
                    var tabType = typeof(UI.ITab_IsekaiStats);
                    
                    // Add to the type list if not present
                    if (!thingDef.inspectorTabs.Contains(tabType))
                    {
                        thingDef.inspectorTabs.Add(tabType);
                    }
                    
                    // Add to the resolved list if not present
                    if (!thingDef.inspectorTabsResolved.Any(t => t is UI.ITab_IsekaiStats))
                    {
                        thingDef.inspectorTabsResolved.Add(new UI.ITab_IsekaiStats());
                    }
                }
            }
            
            // Tab injection verified silently
        }
    }

    /// <summary>
    /// Assigns rank traits after pawn generation is complete (when story/traits are available)
    /// </summary>
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), typeof(PawnGenerationRequest))]
    public static class Patch_AssignRankTraitAfterGeneration
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __result)
        {
            try
            {
                if (__result == null) return;
                if (__result.RaceProps == null || !__result.RaceProps.Humanlike) return;
                if (__result.story == null || __result.story.traits == null) return;
                if (__result.story.traits.allTraits == null) return;

                // Get the Isekai component
                var comp = __result.GetComp<IsekaiComponent>();
                if (comp == null || comp.stats == null) return;

                // Newborns / babies should always be level 1 / F rank,
                // unless the player enabled "Newborns Use Rank Rolling" in settings.
                // During InitializeComps, ageTracker may not exist yet so the baby
                // could have been rolled a random rank. Now that generation is complete
                // and ageTracker is available, fix it.
                if (PawnStatGenerator.IsNewborn(__result) && !IsekaiLevelingSettings.NewbornsUseRankRolling)
                {
                    PawnStatGenerator.ResetToNewborn(__result, comp);
                    return;
                }

                // Second-chance stat generation: if stats weren't generated during
                // InitializeComps (ageTracker was null or age was 0), generate now.
                // By this point the pawn is fully constructed with valid ageTracker.
                if (!comp.statsInitialized || (comp.currentLevel <= 1 && comp.stats.TotalAllocatedPoints <= 0))
                {
                    PawnStatGenerator.InitializePawnStats(__result, comp);
                    comp.statsInitialized = true;
                }

                // Try to restore character creation stats (EdB Prepare Carefully compat).
                // When EdB recreates pawn objects on game start, this restores
                // the stats the player set during character creation.
                if (PawnStatGenerator.TryRestoreCharacterCreationStats(__result, comp))
                {
                    return;
                }

                // Check if already has a rank trait (e.g. from Character Editor)
                string existingRank = null;
                string[] rankSuffixes = { "F", "E", "D", "C", "B", "A", "S", "SS", "SSS" };
                foreach (var trait in __result.story.traits.allTraits)
                {
                    if (trait?.def?.defName == null) continue;
                    foreach (var suffix in rankSuffixes)
                    {
                        if (trait.def.defName == $"Isekai_Rank_{suffix}")
                        {
                            existingRank = suffix;
                            break;
                        }
                    }
                    if (existingRank != null) break;
                }

                if (existingRank != null)
                {
                    // Pawn already has a rank trait — sync component level to match.
                    // This handles Character Editor assigning a rank trait that doesn't
                    // match the auto-generated component level.
                    PawnStatGenerator.SyncToRankTrait(__result, comp, existingRank);
                }
                else
                {
                    // No rank trait, assign one based on stats
                    PawnStatGenerator.UpdateRankTraitFromStats(__result, comp);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Error in rank trait assignment: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Detects when a map is finalized/entered, and spawns hunt creatures for active hunt quests.
    /// Also triggers world boss spawning when a player enters a boss site.
    /// This is needed because QuestPart doesn't have tick or map notification methods.
    /// </summary>
    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    public static class Patch_MapFinalizeInit_SpawnHuntCreature
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            try
            {
                // Check if any active hunt quest is for this map's parent (a Site)
                Quests.QuestPart_IsekaiWorldHunt.OnMapGenerated(__instance);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Error in hunt creature spawn check: {ex.Message}");
            }
            
            try
            {
                // Check if any active world boss quest is for this map
                WorldBoss.QuestPart_WorldBoss.OnMapGenerated(__instance);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Error in world boss spawn check: {ex.Message}");
            }
            
            // Add a safety-net MapComponent for Site maps to retry spawning
            // in case the above checks missed due to timing issues
            if (__instance.Parent is RimWorld.Planet.Site)
            {
                try
                {
                    if (__instance.GetComponent<Quests.IsekaiHuntSpawnChecker>() == null)
                    {
                        __instance.components.Add(new Quests.IsekaiHuntSpawnChecker(__instance));
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[Isekai Leveling] Error adding spawn checker: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Clears static caches and stale state after the game fully initializes.
    /// Prevents stale cache entries from interfering with subsequent save/load cycles.
    /// </summary>
    [HarmonyPatch(typeof(Game), nameof(Game.FinalizeInit))]
    public static class Patch_ClearCharacterCreationCache
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            PawnStatGenerator.ClearCharacterCreationCache();
            
            // Clear fast component lookup cache (prevents cross-save contamination)
            IsekaiComponent.ClearCompCache();
            
            // Clear stale static state FIRST (prevents cross-save contamination)
            WorldBoss.WorldBossSizePatch.ClearAll();
            WorldBoss.QuestPart_WorldBoss.ClearStaticState();
            Quests.QuestPart_IsekaiWorldHunt.ClearStaticState();
            Quests.QuestPart_IsekaiLocalHunt.ClearStaticState();
            
            // Clear static caches that accumulate per-pawn/per-entity data
            // These would otherwise leak across save/load boundaries
            Effects.AuraSystem.ClearCaches();
            Patch_LifespanBonus.ClearAll();
            DamageTracker.ClearAll();
            Patch_WorkXP.ClearAll();
            Patch_SkillLearnXP.ClearAll();
            Patch_MeditationXP.ClearAll();
            Patch_AbilityXP.ClearAll();
            Patch_AbilityXP_World.ClearAll();
            Patch_PsychicEntropyXP.ClearAll();
            Patch_EliteNameOverlay.ClearAll();
            MobRanking.RaidRankPatches.ClearAll();
            AlteredCarbonCompat.ClearAll();
            ResurrectionPreserver.ClearAll();
            
            // Re-register quest parts from the quest manager.
            // PostLoadInit deserialized everything but ClearStaticState just wiped the lists.
            // Now rebuild from the authoritative source (active quests).
            Quests.QuestPart_IsekaiWorldHunt.ReRegisterAfterLoad();
            Quests.QuestPart_IsekaiLocalHunt.ReRegisterAfterLoad();
            WorldBoss.QuestPart_WorldBoss.ReRegisterAfterLoad();
            
            // Re-register wave mobs from any active WorldBossMapComponents
            foreach (var map in Find.Maps)
            {
                var wbComp = map.GetComponent<WorldBoss.WorldBossMapComponent>();
                if (wbComp != null)
                {
                    wbComp.ReRegisterWaveMobsAfterLoad();
                }
            }
        }
    }
}
