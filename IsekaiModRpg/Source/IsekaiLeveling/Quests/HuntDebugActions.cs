using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;
using IsekaiLeveling.Compatibility;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Debug actions for testing the hunt system
    /// Accessible via Dev Mode > Debug Actions > Isekai
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HuntDebugActions
    {
        [DebugAction("Isekai", "Spawn F-Rank Hunt", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnFRankHunt()
        {
            SpawnHuntWithRank(QuestRank.F);
        }
        
        [DebugAction("Isekai", "Spawn D-Rank Hunt", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnDRankHunt()
        {
            SpawnHuntWithRank(QuestRank.D);
        }
        
        [DebugAction("Isekai", "Spawn C-Rank Hunt", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnCRankHunt()
        {
            SpawnHuntWithRank(QuestRank.C);
        }
        
        [DebugAction("Isekai", "Spawn B-Rank Hunt (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnBRankHunt()
        {
            SpawnHuntWithRank(QuestRank.B);
        }
        
        [DebugAction("Isekai", "Spawn A-Rank Hunt (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnARankHunt()
        {
            SpawnHuntWithRank(QuestRank.A);
        }
        
        [DebugAction("Isekai", "Spawn S-Rank Hunt (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnSRankHunt()
        {
            SpawnHuntWithRank(QuestRank.S);
        }
        
        [DebugAction("Isekai", "Spawn SSS-Rank Hunt (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnSSSRankHunt()
        {
            SpawnHuntWithRank(QuestRank.SSS);
        }
        
        [DebugAction("Isekai", "Trigger Daily Quest (Hunt/Bounty RNG)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void TriggerRandomHunt()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("No current map", MessageTypeDefOf.RejectInput);
                return;
            }
            
            var incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail("Isekai_HuntSpawn");
            if (incidentDef == null)
            {
                Messages.Message("Isekai_HuntSpawn incident not found", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Use proper storyteller parameters with threat points for proper difficulty scaling
            var parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
            parms.points = StorytellerUtility.DefaultThreatPointsNow(map);
            bool result = incidentDef.Worker.TryExecute(parms);
            
            if (!result)
                Messages.Message("[DEBUG] Quest generation failed — check log for details", MessageTypeDefOf.RejectInput);
        }
        
        [DebugAction("Isekai", "Spawn World Boss Quest", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnWorldBossQuest()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("No current map", MessageTypeDefOf.RejectInput);
                return;
            }
            
            var bossKind = WorldBoss.IncidentWorker_WorldBoss.SelectWorldBossCreature();
            if (bossKind == null)
            {
                Messages.Message("[DEBUG] No suitable creature found for World Boss", MessageTypeDefOf.RejectInput);
                return;
            }
            
            if (!WorldBoss.IncidentWorker_WorldBoss.TryFindBossTile(map.Tile, out int targetTile))
            {
                Messages.Message("[DEBUG] Could not find suitable tile for World Boss", MessageTypeDefOf.RejectInput);
                return;
            }
            
            float xpReward = WorldBoss.IncidentWorker_WorldBoss.CalculateWorldBossXP(bossKind.combatPower);
            float silverReward = WorldBoss.IncidentWorker_WorldBoss.CalculateWorldBossSilver(bossKind.combatPower);
            
            // Spawn with 2 faction groups for testing
            WorldBoss.IncidentWorker_WorldBoss.CreateWorldBossQuest(
                bossKind, targetTile, map.Tile, xpReward, silverReward, 2, map);
            
            Messages.Message($"[DEBUG] World Boss quest created: Nation-rank {bossKind.LabelCap}! Check your quest tab.", MessageTypeDefOf.NeutralEvent);
        }
        
        [DebugAction("Isekai", "Reset Selected Pawn Level", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ResetPawnLevel(Pawn pawn)
        {
            if (pawn == null || !pawn.RaceProps.Humanlike)
            {
                Messages.Message("Select a humanlike pawn", MessageTypeDefOf.RejectInput);
                return;
            }
            
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null)
            {
                Messages.Message($"{pawn.LabelShort} has no Isekai component", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Reset level and XP
            comp.currentLevel = 1;
            comp.currentXP = 0;
            
            // Reset stats to base values
            comp.stats.strength = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.dexterity = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.vitality = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.intelligence = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.wisdom = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.charisma = IsekaiStatAllocation.BASE_STAT_VALUE;
            comp.stats.availableStatPoints = 0;
            
            // Update rank trait to F (lowest)
            PawnStatGenerator.AssignRankTrait(pawn, "F");
            
            Messages.Message($"Reset {pawn.LabelShort}'s Isekai level to 1", MessageTypeDefOf.NeutralEvent);
            Log.Message($"[Isekai Leveling] DEV: Reset {pawn.LabelShort}'s level to 1 with base stats.");
        }
        
        [DebugAction("Isekai", "Reset to Level 0 (Full Wipe)", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ResetPawnToZero(Pawn pawn)
        {
            if (pawn == null || !pawn.RaceProps.Humanlike)
            {
                Messages.Message("Select a humanlike pawn", MessageTypeDefOf.RejectInput);
                return;
            }
            
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null)
            {
                Messages.Message($"{pawn.LabelShort} has no Isekai component", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Full zero-out: level 0, all stats 0, no points
            comp.currentLevel = 0;
            comp.currentXP = 0;
            
            comp.stats.strength = 0;
            comp.stats.dexterity = 0;
            comp.stats.vitality = 0;
            comp.stats.intelligence = 0;
            comp.stats.wisdom = 0;
            comp.stats.charisma = 0;
            comp.stats.availableStatPoints = 0;
            
            // Update rank trait to F (lowest)
            PawnStatGenerator.AssignRankTrait(pawn, "F");
            
            Messages.Message($"Reset {pawn.LabelShort} to Level 0 (naked brutality start)", MessageTypeDefOf.NeutralEvent);
            Log.Message($"[Isekai Leveling] DEV: Full wipe on {pawn.LabelShort} - Level 0, all stats 0.");
        }
        
        private static void SpawnHuntWithRank(QuestRank rank)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("No current map", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Use the same creature selection as the main system
            PawnKindDef creatureKind = IncidentWorker_IsekaiHunt.SelectCreatureForRank(rank);
            
            if (creatureKind == null)
            {
                Messages.Message("No suitable creature found", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Calculate rewards using the same formula as the main system
            float xpReward = IncidentWorker_IsekaiHunt.CalculateXPReward(rank, creatureKind.combatPower);
            float silverReward = IncidentWorker_IsekaiHunt.CalculateSilverReward(rank, creatureKind.combatPower);
            
            // Create hunt quest (appears in Available tab)
            IncidentWorker_IsekaiHunt.CreateHuntQuest(creatureKind, rank, xpReward, silverReward, map);
            
            Messages.Message($"[DEBUG] Created {rank}-Rank hunt quest for {creatureKind.LabelCap}", MessageTypeDefOf.NeutralEvent);
        }
        
        // === BOUNTY DEBUG ACTIONS ===
        
        [DebugAction("Isekai", "Spawn C-Rank Bounty", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnCRankBounty()
        {
            SpawnBountyWithRank(QuestRank.C);
        }
        
        [DebugAction("Isekai", "Spawn B-Rank Bounty (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnBRankBounty()
        {
            SpawnBountyWithRank(QuestRank.B);
        }
        
        [DebugAction("Isekai", "Spawn A-Rank Bounty (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnARankBounty()
        {
            SpawnBountyWithRank(QuestRank.A);
        }
        
        [DebugAction("Isekai", "Spawn S-Rank Bounty (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnSRankBounty()
        {
            SpawnBountyWithRank(QuestRank.S);
        }
        
        [DebugAction("Isekai", "Spawn SSS-Rank Bounty (World)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void SpawnSSSRankBounty()
        {
            SpawnBountyWithRank(QuestRank.SSS);
        }
        
        private static void SpawnBountyWithRank(QuestRank rank)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("No current map", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Use hostile pawn selection for bounty quests
            PawnKindDef bountyTarget = IncidentWorker_IsekaiHunt.SelectHostilePawnForRank(rank);
            
            if (bountyTarget == null)
            {
                Messages.Message($"No suitable hostile pawn found for {rank}-Rank bounty", MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Calculate rewards using the same formula as the main system
            float xpReward = IncidentWorker_IsekaiHunt.CalculateXPReward(rank, bountyTarget.combatPower);
            float silverReward = IncidentWorker_IsekaiHunt.CalculateSilverReward(rank, bountyTarget.combatPower);
            
            // Create bounty quest (appears in Available tab)
            IncidentWorker_IsekaiHunt.CreateHuntQuest(bountyTarget, rank, xpReward, silverReward, map, isBounty: true);
            
            Messages.Message($"[DEBUG] Created {rank}-Rank bounty quest for {bountyTarget.LabelCap}", MessageTypeDefOf.NeutralEvent);
        }
    }
}
