using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;

namespace IsekaiLeveling.WorldBoss
{
    /// <summary>
    /// Quest part for the World Boss encounter.
    /// Creates site on acceptance, spawns Nation-rank boss at 3x size,
    /// optionally spawns faction groups, and triggers mid-life wave spawning.
    /// </summary>
    public class QuestPart_WorldBoss : QuestPart
    {
        // Quest configuration (set before quest starts)
        public PawnKindDef bossKind;
        public int targetTile = -1;
        public float xpReward;
        public float silverReward;
        public List<Thing> lootRewards = new List<Thing>();
        public int factionGroupCount;
        public string inSignalEnable;

        // Runtime state
        private Site site;
        private Pawn bossCreature;
        private bool siteCreated;
        private bool bossSpawned;
        private bool questCompleted;
        private List<Pawn> factionPawns = new List<Pawn>();

        // Static registry for active world boss quests
        private static List<QuestPart_WorldBoss> activeWorldBosses = new List<QuestPart_WorldBoss>();

        /// <summary>
        /// Clears static state between save/load boundaries to prevent stale data.
        /// Called from Game.FinalizeInit patch.
        /// </summary>
        public static void ClearStaticState()
        {
            activeWorldBosses.Clear();
        }

        /// <summary>
        /// Called from Game.FinalizeInit AFTER ClearStaticState + ClearAll.
        /// Scans all active quests to rebuild the activeWorldBosses list,
        /// re-registers boss creatures in the size/team patches, and re-links
        /// WorldBossMapComponent → QuestPart references that aren't serialized.
        /// </summary>
        public static void ReRegisterAfterLoad()
        {
            // Rebuild activeWorldBosses from the quest manager
            if (Find.QuestManager != null)
            {
                foreach (var quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.State == QuestState.Ongoing || quest.State == QuestState.NotYetAccepted)
                    {
                        foreach (var part in quest.PartsListForReading)
                        {
                            if (part is QuestPart_WorldBoss wb && !activeWorldBosses.Contains(wb))
                            {
                                activeWorldBosses.Add(wb);
                            }
                        }
                    }
                }
            }
            
            // Now re-register boss creatures and re-link MapComponents
            foreach (var wb in activeWorldBosses)
            {
                if (wb == null) continue;
                if (wb.bossCreature != null && !wb.bossCreature.Dead)
                {
                    WorldBossSizePatch.RegisterWorldBoss(wb.bossCreature);
                    Log.Message($"[Isekai WorldBoss] Re-registered boss after load: {wb.bossCreature.LabelCap}");
                }

                // Re-link WorldBossMapComponent → QuestPart
                if (wb.site != null && wb.site.HasMap)
                {
                    var mapComp = wb.site.Map.GetComponent<WorldBossMapComponent>();
                    if (mapComp != null)
                    {
                        mapComp.SetQuestPart(wb);
                        Log.Message($"[Isekai WorldBoss] Re-linked MapComponent → QuestPart after load");
                    }
                }
            }
        }

        /// <summary>
        /// Called by HuntPatches.cs (Harmony patch on map generation) to check if any
        /// world boss quests need to spawn their boss on the newly generated map.
        /// </summary>
        public static void OnMapGenerated(Map map)
        {
            foreach (var wb in activeWorldBosses.ToList())
            {
                if (wb == null) continue;
                
                // Use map.Parent for robust matching — site.HasMap can be unreliable during FinalizeInit
                if (wb.siteCreated && !wb.bossSpawned &&
                    wb.site != null && map.Parent == wb.site)
                {
                    Log.Message($"[Isekai WorldBoss] Player entered world boss site, spawning {wb.bossKind?.LabelCap}");
                    wb.SpawnBossOnSite(map);
                }
            }
        }
        
        /// <summary>
        /// Called by IsekaiHuntSpawnChecker MapComponent as a tick-based safety net.
        /// Re-checks all active world bosses against a map that already exists.
        /// </summary>
        public static void TrySpawnOnExistingMap(Map map)
        {
            foreach (var wb in activeWorldBosses.ToList())
            {
                if (wb == null) continue;
                if (wb.siteCreated && !wb.bossSpawned &&
                    wb.site != null && map.Parent == wb.site)
                {
                    Log.Message($"[Isekai WorldBoss] Safety-net spawn triggered for {wb.bossKind?.LabelCap}");
                    wb.SpawnBossOnSite(map);
                }
            }
        }

        public override void PostQuestAdded()
        {
            base.PostQuestAdded();
            if (!activeWorldBosses.Contains(this))
                activeWorldBosses.Add(this);
        }

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);

            // When quest is accepted, create the site on the world map
            if (!siteCreated && signal.tag == quest.InitiateSignal && targetTile >= 0)
            {
                CreateSiteOnWorldMap();
            }

            // Check if player has entered the site
            if (siteCreated && !bossSpawned && site != null && site.HasMap)
            {
                SpawnBossOnSite(site.Map);
            }
        }

        // ─────────────────────────────────────────────
        //  Site Creation
        // ─────────────────────────────────────────────

        private void CreateSiteOnWorldMap()
        {
            siteCreated = true;

            SitePartDef partDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Isekai_WorldBossGrounds")
                ?? DefDatabase<SitePartDef>.GetNamedSilentFail("Isekai_HuntGrounds")
                ?? DefDatabase<SitePartDef>.GetNamedSilentFail("ItemStash");

            if (partDef == null)
            {
                Log.Error("[Isekai WorldBoss] No suitable SitePartDef found!");
                quest.End(QuestEndOutcome.Fail, sendLetter: false);
                return;
            }

            site = SiteMaker.MakeSite(
                sitePart: partDef,
                tile: targetTile,
                faction: null,
                threatPoints: 0f
            );

            if (site == null)
            {
                Log.Error("[Isekai WorldBoss] SiteMaker returned null!");
                quest.End(QuestEndOutcome.Fail, sendLetter: false);
                return;
            }

            site.customLabel = "Isekai_WorldBoss_SiteLabel".Translate(bossKind.LabelCap);
            Find.WorldObjects.Add(site);

            Messages.Message(
                "<color=#9933FF>" + "Isekai_WorldBoss_SiteRevealed".Translate(bossKind.LabelCap) + "</color>",
                new LookTargets(site),
                MessageTypeDefOf.ThreatBig
            );
        }

        // ─────────────────────────────────────────────
        //  Boss Spawning
        // ─────────────────────────────────────────────

        private void SpawnBossOnSite(Map map)
        {
            if (bossSpawned) return;
            bossSpawned = true;

            // 1) Spawn the boss creature - prefer outdoor cells, don't filter by fog (map may be fully fogged during FinalizeInit)
            IntVec3 bossSpawnLoc = map.Center;
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15, 
                c => c.Standable(map) && !c.Roofed(map), out bossSpawnLoc))
            {
                // Fallback: try larger radius still preferring outdoors
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 40, 
                    c => c.Standable(map) && !c.Roofed(map), out bossSpawnLoc))
                {
                    // Final fallback: any standable cell
                    CellFinder.TryFindRandomCellNear(map.Center, map, 15, c => c.Standable(map), out bossSpawnLoc);
                }
            }

            try
            {
                var bossReq = new PawnGenerationRequest(
                    bossKind,
                    faction: null,
                    PawnGenerationContext.NonPlayer,
                    map.Tile,
                    forceGenerateNewPawn: true,
                    allowDowned: false,
                    allowDead: false
                );
                // Suppress orphan parent/sibling relations — bosses are ephemeral.
                bossReq.RelationWithExtraPawnChanceFactor = 0f;
                bossReq.ColonistRelationChanceFactor = 0f;
                bossCreature = PawnGenerator.GeneratePawn(bossReq);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[Isekai WorldBoss] Failed to generate boss pawn of kind {bossKind?.defName ?? "null"}: {ex.Message}");
                bossCreature = null;
            }

            if (bossCreature == null)
            {
                Log.Error("[Isekai WorldBoss] Failed to generate boss pawn!");
                quest.End(QuestEndOutcome.Fail, sendLetter: false);
                return;
            }

            GenSpawn.Spawn(bossCreature, bossSpawnLoc, map, Rot4.Random);

            // Force Nation rank override BEFORE health reset
            var mobRankComp = bossCreature.TryGetComp<MobRankComponent>();
            if (mobRankComp != null)
            {
                mobRankComp.SetRankOverride(MobRankTier.Nation);
                mobRankComp.SetEliteOverride(true);
                Log.Message($"[Isekai WorldBoss] Set {bossCreature.LabelCap} to Nation rank (elite)");
            }
            else
            {
                Log.Error($"[Isekai WorldBoss] {bossCreature.LabelCap} has no MobRankComponent! Stats will not apply.");
            }

            // Register for visual scaling BEFORE health reset (so BodySize patch is active)
            WorldBossSizePatch.RegisterWorldBoss(bossCreature);

            // Full health with no debuffs — must happen AFTER rank override AND
            // RegisterWorldBoss so GetMaxHealth returns the fully-multiplied values
            // (Nation rank × elite × BOSS_HEALTH_BOOST × HEALTH_SCALE_BOOST ≈ 216×).
            bossCreature.health.Reset();
            IncidentWorker_IsekaiHunt.RemoveIncapacitatingConditions(bossCreature);

            // Dirty the health cache so the game recalculates with Nation-rank HP
            bossCreature.health.Notify_HediffChanged(null);

            // Log actual HP for debugging
            try
            {
                var torso = bossCreature.health.hediffSet.GetNotMissingParts()
                    .FirstOrDefault(p => p.def.defName == "Torso" || p.def.defName == "Body");
                if (torso != null)
                {
                    float maxHp = torso.def.GetMaxHealth(bossCreature);
                    // ~216× = MobRankUtility Nation(12) × elite(1.2) × BOSS_HEALTH_BOOST(5) × HEALTH_SCALE_BOOST(3).
                    Log.Message($"[Isekai WorldBoss] {bossCreature.LabelCap} torso max HP = {maxHp} (base would be ~{maxHp / 216f:F0})");
                }
            }
            catch { }

            // Make the boss hostile
            WorldBossUtility.MakeCreatureHostile(bossCreature);

            // 2) Spawn faction groups if any
            SpawnFactionGroups(map);

            // 4) Add map component for wave tracking
            var waveComp = map.GetComponent<WorldBossMapComponent>();
            if (waveComp == null)
            {
                waveComp = new WorldBossMapComponent(map);
                map.components.Add(waveComp);
            }
            waveComp.RegisterBoss(bossCreature, bossKind);
            waveComp.SetQuestPart(this);

            // 5) Dramatic notification
            Find.LetterStack.ReceiveLetter(
                "Isekai_WorldBoss_Arrived_Label".Translate(),
                "Isekai_WorldBoss_Arrived_Text".Translate(bossCreature.LabelCap),
                LetterDefOf.ThreatBig,
                bossCreature,
                relatedFaction: null,
                quest: quest
            );

            CameraJumper.TryJumpAndSelect(bossCreature);

            Log.Message($"[Isekai WorldBoss] Boss spawned: {bossCreature.LabelCap} at {bossSpawnLoc}");
        }

        // ─────────────────────────────────────────────
        //  Faction Groups
        // ─────────────────────────────────────────────

        private void SpawnFactionGroups(Map map)
        {
            if (factionGroupCount <= 0) return;

            // Select eligible factions (non-player, non-hidden, with pawns)
            var allEligible = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.Hidden && !f.defeated &&
                            f.def.humanlikeFaction && f.def.pawnGroupMakers != null &&
                            f.def.pawnGroupMakers.Any())
                .ToList();

            if (!allEligible.Any())
            {
                Log.Warning("[Isekai WorldBoss] No eligible factions for encounter groups");
                return;
            }

            // Separate into allied/neutral vs hostile — bias heavily toward allies
            var alliedFactions = allEligible
                .Where(f => !f.HostileTo(Faction.OfPlayer))
                .ToList();
            var hostileFactions = allEligible
                .Where(f => f.HostileTo(Faction.OfPlayer))
                .ToList();

            alliedFactions.Shuffle();
            hostileFactions.Shuffle();

            // Build the faction list: prioritize allies, then fill with hostile if needed
            // 70% chance each slot goes to an ally, 30% hostile
            List<Faction> selectedFactions = new List<Faction>();
            int alliedIdx = 0;
            int hostileIdx = 0;

            for (int g = 0; g < factionGroupCount; g++)
            {
                bool pickAlly = Rand.Value < 0.70f;

                if (pickAlly && alliedIdx < alliedFactions.Count)
                {
                    selectedFactions.Add(alliedFactions[alliedIdx++]);
                }
                else if (hostileIdx < hostileFactions.Count)
                {
                    selectedFactions.Add(hostileFactions[hostileIdx++]);
                }
                else if (alliedIdx < alliedFactions.Count)
                {
                    selectedFactions.Add(alliedFactions[alliedIdx++]);
                }
                // else no more factions available
            }

            foreach (var faction in selectedFactions)
            {
                SpawnFactionGroup(map, faction, spawnNearBoss: true);
            }
        }

        /// <summary>
        /// Spawns a single faction reinforcement at the map edges.
        /// Called by WorldBossMapComponent every 12 hours for ongoing reinforcements.
        /// </summary>
        public void SpawnReinforcementFaction(Map map)
        {
            if (bossCreature == null || bossCreature.Dead) return;

            var allEligible = Find.FactionManager.AllFactions
                .Where(f => !f.IsPlayer && !f.Hidden && !f.defeated &&
                            f.def.humanlikeFaction && f.def.pawnGroupMakers != null &&
                            f.def.pawnGroupMakers.Any())
                .ToList();

            if (!allEligible.Any()) return;

            // Bias toward allies (70/30)
            var allies = allEligible.Where(f => !f.HostileTo(Faction.OfPlayer)).ToList();
            var hostiles = allEligible.Where(f => f.HostileTo(Faction.OfPlayer)).ToList();

            Faction chosen = null;
            if (Rand.Value < 0.70f && allies.Any())
                chosen = allies.RandomElement();
            else if (hostiles.Any())
                chosen = hostiles.RandomElement();
            else if (allies.Any())
                chosen = allies.RandomElement();

            if (chosen == null) return;

            SpawnFactionGroup(map, chosen, spawnNearBoss: false);

            Find.LetterStack.ReceiveLetter(
                "Isekai_WorldBoss_Reinforcement_Label".Translate(),
                "Isekai_WorldBoss_Reinforcement_Text".Translate(chosen.Name, bossCreature.LabelCap),
                LetterDefOf.NeutralEvent,
                new LookTargets(bossCreature)
            );
        }

        private void SpawnFactionGroup(Map map, Faction faction, bool spawnNearBoss = false)
        {
            IntVec3 spawnLoc;

            if (spawnNearBoss && bossCreature != null && bossCreature.Spawned)
            {
                // Spawn near the boss so they immediately engage
                spawnLoc = bossCreature.Position;
                CellFinder.TryFindRandomCellNear(bossCreature.Position, map, 20,
                    c => c.Standable(map) && !c.Fogged(map), out spawnLoc);
            }
            else
            {
                // Spawn at map edges (reinforcements arriving later)
                if (!RCellFinder.TryFindRandomPawnEntryCell(out spawnLoc, map, CellFinder.EdgeRoadChance_Animal))
                {
                    CellFinder.TryFindRandomEdgeCellWith(
                        c => c.Standable(map) && !c.Fogged(map), map,
                        CellFinder.EdgeRoadChance_Ignore, out spawnLoc);
                }
            }

            // Generate a medium combat group — strong enough to help but not solo the boss
            int groupSize = Rand.RangeInclusive(8, 15);
            float combatPoints = 3000f + (groupSize * 150f);

            PawnGroupMakerParms groupParms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                faction = faction,
                points = combatPoints,
                generateFightersOnly = true
            };

            List<Pawn> groupPawns;
            try
            {
                groupPawns = PawnGroupMakerUtility.GeneratePawns(groupParms).Take(groupSize).ToList();
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai WorldBoss] Failed to generate faction group for {faction.Name}: {ex.Message}");
                return;
            }

            if (!groupPawns.Any()) return;

            // Determine relation to player
            bool hostileToPlayer = faction.HostileTo(Faction.OfPlayer);

            foreach (Pawn pawn in groupPawns)
            {
                IntVec3 loc = spawnLoc;
                CellFinder.TryFindRandomCellNear(spawnLoc, map, 8, c => c.Standable(map), out loc);
                GenSpawn.Spawn(pawn, loc, map, Rot4.Random);
                factionPawns.Add(pawn);
            }

            // Assign AI Lord so pawns actually fight instead of standing idle.
            // Both hostile and allied factions use AssaultColony — hostile ones attack
            // the player AND boss, allied ones are set to assault the boss's faction
            // and are individually primed to target the boss creature.
            try
            {
                if (groupPawns.Any())
                {
                    // Both hostile and allied factions get AssaultColony for aggressive behavior.
                    // AssaultColony makes pawns actively seek and attack enemy targets
                    // instead of passively standing at a defend point.
                    LordJob lordJob = new LordJob_AssaultColony(faction, canKidnap: false,
                        canTimeoutOrFlee: false, sappers: false, useAvoidGridSmart: false,
                        canSteal: false);

                    LordMaker.MakeNewLord(faction, lordJob, map, groupPawns);

                    // For allied factions: prime each pawn to target the boss directly.
                    // This ensures they charge at the boss rather than wandering the map.
                    if (!hostileToPlayer && bossCreature != null)
                    {
                        foreach (var pawn in groupPawns)
                        {
                            if (pawn.mindState != null)
                            {
                                pawn.mindState.enemyTarget = bossCreature;
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai WorldBoss] Failed to create Lord for {faction.Name}: {ex.Message}");
            }

            string relationStr = hostileToPlayer
                ? "Isekai_WorldBoss_FactionHostile".Translate(faction.Name)
                : "Isekai_WorldBoss_FactionAllied".Translate(faction.Name);

            Messages.Message(
                "<color=#FF8844>" + "Isekai_WorldBoss_FactionArrived".Translate(faction.Name, groupPawns.Count) + "</color>\n" + relationStr,
                new LookTargets(groupPawns.First()),
                MessageTypeDefOf.NeutralEvent
            );

            Log.Message($"[Isekai WorldBoss] Spawned {groupPawns.Count} pawns from {faction.Name} (hostile to player: {hostileToPlayer})");
        }

        // ─────────────────────────────────────────────
        //  Boss Death
        // ─────────────────────────────────────────────

        public override void Notify_PawnKilled(Pawn pawn, DamageInfo? dinfo)
        {
            if (pawn != bossCreature) return;
            if (questCompleted) return;
            
            // Guard against map-cleanup kills (gravship/SRTS departure destroys map → kills pawns)
            // Pass site?.Map as fallback since pawn.Map is null after despawn
            if (!IncidentWorker_IsekaiHunt.IsLegitimateQuestKill(pawn, dinfo, site?.Map))
            {
                Log.Message($"[Isekai WorldBoss] Ignoring map-cleanup kill of {pawn.LabelCap} (no colonists present)");
                return;
            }
            
            CompleteWorldBoss(dinfo?.Instigator as Pawn);
        }
        
        /// <summary>
        /// Safety net: called from IsekaiHuntTracker.GameComponentTick every 250 ticks.
        /// If the boss died but Notify_PawnKilled didn't complete the quest
        /// (e.g., edge case in notification dispatch), catch it here.
        /// </summary>
        public void CheckSafetyNet()
        {
            if (questCompleted) return;
            if (bossCreature == null || !bossCreature.Dead) return;
            if (quest == null || quest.State != QuestState.Ongoing) return;
            
            // Verify colonists are/were present on site (reject gravship cleanup deaths)
            if (site != null && site.HasMap && IncidentWorker_IsekaiHunt.AnyColonistsOnMap(site.Map))
            {
                Log.Message($"[Isekai WorldBoss] Safety net: completing world boss quest for dead {bossCreature.LabelCap}");
                CompleteWorldBoss(null);
            }
        }
        
        private void CompleteWorldBoss(Pawn killer)
        {
            if (questCompleted) return;
            questCompleted = true;

            // Unregister from size scaling
            WorldBossSizePatch.UnregisterWorldBoss(bossCreature);

            // Award rewards directly (bypasses tracker to avoid duplicate letters)
            AwardWorldBossXP(xpReward, killer, bossCreature, site?.Map);
            AwardWorldBossSilver(silverReward, killer ?? bossCreature, site?.Map);
            AwardWorldBossLoot(lootRewards, killer ?? bossCreature, site?.Map);

            // Stop wave spawning
            if (site != null && site.HasMap)
            {
                var waveComp = site.Map.GetComponent<WorldBossMapComponent>();
                waveComp?.OnBossDefeated();
            }

            // Send world-boss-specific victory letter
            Map letterMap = site?.Map ?? killer?.Map ?? bossCreature?.MapHeld ?? Find.AnyPlayerHomeMap;
            GlobalTargetInfo letterTarget = letterMap != null
                ? new GlobalTargetInfo(killer?.Position ?? IntVec3.Zero, letterMap)
                : GlobalTargetInfo.Invalid;

            Find.LetterStack.ReceiveLetter(
                "Isekai_WorldBoss_Victory_Label".Translate(bossCreature.LabelCap),
                "Isekai_WorldBoss_Victory_Text".Translate(
                    bossCreature.LabelCap,
                    NumberFormatting.FormatNum(xpReward),
                    NumberFormatting.FormatNum(silverReward)
                ),
                LetterDefOf.PositiveEvent,
                letterTarget
            );

            // End quest with delay to avoid NullRef during kill processing
            var questToEnd = quest;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (questToEnd != null && questToEnd.State == QuestState.Ongoing)
                    {
                        questToEnd.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Isekai WorldBoss] Quest end had minor issue: {ex.Message}");
                }
            });
        }

        // ─────────────────────────────────────────────
        //  Reward Distribution (self-contained, no tracker dependency)
        // ─────────────────────────────────────────────

        private void AwardWorldBossXP(float totalXP, Pawn killer, Pawn creature, Map questSiteMap = null)
        {
            // Priority 1: eligible pawns on the quest site map (includes ghouls)
            List<Pawn> colonists = null;
            Map questMap = questSiteMap ?? killer?.Map ?? creature?.MapHeld ?? creature?.Map;
            if (questMap != null)
            {
                colonists = IsekaiComponent.GetIsekaiPawnsOnMap(questMap);
            }

            // Priority 2: all maps
            if (colonists == null || !colonists.Any())
            {
                colonists = IsekaiComponent.GetIsekaiPawnsAllMaps();
            }

            // Priority 3: caravans
            if (!colonists.Any())
            {
                colonists = IsekaiComponent.GetIsekaiPawnsInCaravans();
            }

            if (!colonists.Any()) return;

            // Level-scaled XP: each pawn gets enough raw XP for ~15 levels at their
            // current level.  GainXP applies the global XP multiplier, so we divide
            // by it here to keep the effective gain at ~15 levels regardless of setting.
            float xpMult = IsekaiMod.Settings?.XPMultiplier ?? 3f;
            if (xpMult <= 0f) xpMult = 1f;

            int targetLevels = IncidentWorker_WorldBoss.WORLD_BOSS_TARGET_LEVELS;
            int totalAwarded = 0;

            foreach (var pawn in colonists)
            {
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) continue;

                // Sum XPToNextLevel for the next N levels from this pawn's current level
                float targetXP = 0f;
                for (int i = 0; i < targetLevels; i++)
                {
                    int futureLevel = comp.currentLevel + i;
                    targetXP += Mathf.Max(100f, 100f * Mathf.Pow(futureLevel, 1.5f));
                }

                // Divide by xpMult since GainXP will re-apply it
                int rawXP = Mathf.RoundToInt(targetXP / xpMult);
                comp.GainXP(rawXP, "WorldBossReward");
                totalAwarded += rawXP;
            }

            // Award XP to player-owned creatures (bonded pets/mechs) on the same map
            // Only bonded animals receive quest XP to prevent all tamed animals from leveling
            int creatureCount = 0;
            if (questMap != null)
            {
                foreach (var creature2 in questMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    if (creature2.RaceProps.Humanlike) continue;
                    // Combat pets only (bonded / attack-trained / master-assigned).
                    // Filters out chickens & cows but rewards combat pets.
                    if (creature2.RaceProps.Animal && !IsekaiHuntTracker.IsCombatPet(creature2)) continue;
                    var rankComp = creature2.TryGetComp<MobRankComponent>();
                    if (rankComp == null) continue;
                    // Creatures get the average per-colonist XP
                    int creatureXP = colonists.Count > 0 ? Mathf.RoundToInt((float)totalAwarded / colonists.Count) : 0;
                    if (creatureXP > 0)
                    {
                        rankComp.GainXP(creatureXP, "WorldBossReward");
                        creatureCount++;
                    }
                }
            }

            Log.Message($"[Isekai WorldBoss] Awarded ~{targetLevels} levels worth of XP to {colonists.Count} colonists and {creatureCount} creatures (total raw: {totalAwarded})");
        }

        private void AwardWorldBossSilver(float amount, Pawn nearPawn, Map questSiteMap = null)
        {
            Map map = questSiteMap ?? nearPawn?.MapHeld ?? nearPawn?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            IntVec3 dropLoc = IsekaiHuntTracker.SafeDropCell(map, nearPawn);
            if (!dropLoc.IsValid) return;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = Mathf.RoundToInt(amount);
            GenPlace.TryPlaceThing(silver, dropLoc, map, ThingPlaceMode.Near);
        }

        private void AwardWorldBossLoot(List<Thing> loot, Pawn nearPawn, Map questSiteMap = null)
        {
            if (loot == null || loot.Count == 0) return;

            Map map = questSiteMap ?? nearPawn?.MapHeld ?? nearPawn?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            IntVec3 dropLoc = IsekaiHuntTracker.SafeDropCell(map, nearPawn);
            if (!dropLoc.IsValid) return;
            foreach (var item in loot)
            {
                if (item == null) continue;
                // Silver in the loot list is purely a deep-save anchor for the quest's
                // Reward_Items display (which uses LookMode.Reference). Actual silver
                // payout is handled by AwardWorldBossSilver.
                if (item.def == ThingDefOf.Silver) continue;
                Thing spawnedItem = IsekaiLeveling.Quests.IncidentWorker_IsekaiHunt.CloneRewardItem(item);
                if (spawnedItem == null) continue;
                GenPlace.TryPlaceThing(spawnedItem, dropLoc, map, ThingPlaceMode.Near);
            }
        }

        // ─────────────────────────────────────────────
        //  UI / Description
        // ─────────────────────────────────────────────

        public override string DescriptionPart
        {
            get
            {
                if (!siteCreated)
                    return "Isekai_WorldBoss_Pending".Translate(bossKind.LabelCap);
                if (!bossSpawned)
                    return "Isekai_WorldBoss_Travel".Translate(site?.Label ?? "???");
                if (bossCreature == null || bossCreature.Dead)
                    return "Isekai_WorldBoss_Slain".Translate(bossKind.LabelCap);

                // Show boss health status
                try
                {
                    float hpPct = bossCreature.health.summaryHealth.SummaryHealthPercent;
                    return "Isekai_WorldBoss_Fighting".Translate(
                        bossCreature.LabelCap,
                        Mathf.RoundToInt(hpPct * 100f)
                    );
                }
                catch
                {
                    return "Isekai_WorldBoss_Fighting".Translate(bossCreature.LabelCap, "?");
                }
            }
        }

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (siteCreated && site != null && site.Spawned)
                    yield return site;
                if (bossCreature != null && !bossCreature.Dead && bossCreature.Spawned)
                    yield return bossCreature;
            }
        }

        // ─────────────────────────────────────────────
        //  Cleanup
        // ─────────────────────────────────────────────

        public override void Cleanup()
        {
            base.Cleanup();
            activeWorldBosses.Remove(this);

            if (bossCreature != null)
                WorldBossSizePatch.UnregisterWorldBoss(bossCreature);

            TryRemoveSite();
        }

        /// <summary>
        /// Removes the world boss site if no colonists are present.
        /// If colonists are still on the map, schedules a deferred retry.
        /// </summary>
        private void TryRemoveSite()
        {
            if (!siteCreated || site == null || !site.Spawned) return;

            if (site.HasMap && IncidentWorker_IsekaiHunt.AnyColonistsOnMap(site.Map))
            {
                // Colonists still present (or inside vehicles) — schedule deferred cleanup
                var siteRef = site;
                LongEventHandler.ExecuteWhenFinished(() =>
                {
                    // The map will be destroyed when all colonists leave (vanilla behavior).
                    // Once the map is gone, the site can be safely removed.
                    if (siteRef != null && siteRef.Spawned && (!siteRef.HasMap || !IncidentWorker_IsekaiHunt.AnyColonistsOnMap(siteRef.Map)))
                    {
                        Find.WorldObjects.Remove(siteRef);
                    }
                });
                return;
            }

            Find.WorldObjects.Remove(site);
        }

        // ─────────────────────────────────────────────
        //  Save/Load
        // ─────────────────────────────────────────────

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref bossKind, "bossKind");
            Scribe_Values.Look(ref targetTile, "targetTile", -1);
            Scribe_Values.Look(ref xpReward, "xpReward", 0f);
            Scribe_Values.Look(ref silverReward, "silverReward", 0f);
            Scribe_Collections.Look(ref lootRewards, "lootRewards", LookMode.Deep);
            Scribe_Values.Look(ref factionGroupCount, "factionGroupCount", 0);
            Scribe_Values.Look(ref inSignalEnable, "inSignalEnable");

            Scribe_References.Look(ref site, "site");
            Scribe_References.Look(ref bossCreature, "bossCreature");
            Scribe_Values.Look(ref siteCreated, "siteCreated", false);
            Scribe_Values.Look(ref bossSpawned, "bossSpawned", false);
            Scribe_Values.Look(ref questCompleted, "questCompleted", false);
            Scribe_Collections.Look(ref factionPawns, "factionPawns", LookMode.Reference);

            if (lootRewards == null) lootRewards = new List<Thing>();
            if (factionPawns == null) factionPawns = new List<Pawn>();

            // NOTE: Re-registration into activeWorldBosses and WorldBossSizePatch
            // is handled by ReRegisterAfterLoad(), called from Game.FinalizeInit
            // AFTER ClearStaticState/ClearAll. This ensures correct ordering.
        }
    }
}
