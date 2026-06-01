using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;
using IsekaiLeveling.UI;

namespace IsekaiLeveling.WorldBoss
{
    /// <summary>
    /// MapComponent that tracks the world boss fight.
    /// Monitors boss HP and triggers S-ranked wave spawns at 50% health.
    /// </summary>
    public class WorldBossMapComponent : MapComponent
    {
        // Boss reference
        private Pawn bossCreature;
        private PawnKindDef bossKind;

        // Wave spawning state
        private bool wavesTriggered;
        private bool bossDefeated;
        private int lastWaveSpawnTick = -1;
        private int wavesSpawned;
        private int nextWaveInterval;
        private List<Pawn> spawnedWaveMobs = new List<Pawn>();

        // Faction reinforcement state
        private int lastReinforcementTick = -1;
        private int reinforcementsSpawned;
        private QuestPart_WorldBoss questPart;

        // Constants
        private const int WAVE_MIN_INTERVAL_TICKS = 1800;  // 30 seconds
        private const int WAVE_MAX_INTERVAL_TICKS = 3600;  // 60 seconds
        private const int WAVE_MOB_COUNT_MIN = 2;
        private const int WAVE_MOB_COUNT_MAX = 3;
        private const float WAVE_TRIGGER_HP_PCT = 0.50f;
        private const int MAX_WAVES = 8; // Cap total waves to prevent infinite spawning
        private const int ALLY_RETARGET_INTERVAL = 180; // Re-prime allies every 3 seconds
        private const int REINFORCEMENT_INTERVAL = 60000; // 24 in-game hours (full day)
        private const int MAX_REINFORCEMENTS = 4; // Cap total reinforcement waves

        public WorldBossMapComponent(Map map) : base(map) { }

        /// <summary>
        /// Called from Game.FinalizeInit AFTER ClearAll wipes the static registries.
        /// Re-registers wave mobs in the boss-team HashSet so HostileTo patch works.
        /// </summary>
        public void ReRegisterWaveMobsAfterLoad()
        {
            if (bossCreature != null && !bossCreature.Dead)
            {
                WorldBossSizePatch.RegisterWorldBoss(bossCreature);
            }
            
            if (spawnedWaveMobs != null)
            {
                int reregistered = 0;
                foreach (var mob in spawnedWaveMobs)
                {
                    if (mob != null && !mob.Dead)
                    {
                        WorldBossSizePatch.RegisterWaveMob(mob);
                        reregistered++;
                    }
                }
                if (reregistered > 0)
                    Log.Message($"[Isekai WorldBoss] Re-registered {reregistered} wave mobs after load");
            }
        }

        /// <summary>
        /// Register the boss creature for HP tracking.
        /// Called by QuestPart_WorldBoss when the boss spawns.
        /// </summary>
        public void RegisterBoss(Pawn boss, PawnKindDef kind)
        {
            bossCreature = boss;
            bossKind = kind;
            wavesTriggered = false;
            bossDefeated = false;
            wavesSpawned = 0;
            lastWaveSpawnTick = -1;
            spawnedWaveMobs = new List<Pawn>();
            nextWaveInterval = Rand.Range(WAVE_MIN_INTERVAL_TICKS, WAVE_MAX_INTERVAL_TICKS);
            lastReinforcementTick = Find.TickManager.TicksGame;
            reinforcementsSpawned = 0;
        }

        /// <summary>
        /// Link the QuestPart so we can call SpawnReinforcementFaction.
        /// </summary>
        public void SetQuestPart(QuestPart_WorldBoss qp)
        {
            questPart = qp;
        }

        /// <summary>
        /// Called when the boss is killed.
        /// </summary>
        public void OnBossDefeated()
        {
            bossDefeated = true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (bossDefeated || bossCreature == null || bossCreature.Dead) return;

            // Re-prime allied faction pawns to target the boss every few seconds.
            // This keeps allies actively hunting the boss instead of wandering idle.
            if (Find.TickManager.TicksGame % ALLY_RETARGET_INTERVAL == 0)
            {
                RetargetAlliedPawns();
            }

            // Spawn faction reinforcements every 12 in-game hours
            if (reinforcementsSpawned < MAX_REINFORCEMENTS && questPart != null)
            {
                int ticksSinceLastReinforcement = Find.TickManager.TicksGame - lastReinforcementTick;
                if (ticksSinceLastReinforcement >= REINFORCEMENT_INTERVAL)
                {
                    lastReinforcementTick = Find.TickManager.TicksGame;
                    reinforcementsSpawned++;
                    questPart.SpawnReinforcementFaction(map);
                    Log.Message($"[Isekai WorldBoss] Reinforcement #{reinforcementsSpawned} arrived");
                }
            }

            // Only check wave spawning every 60 ticks for performance
            if (Find.TickManager.TicksGame % 60 != 0) return;

            // Check if boss has dropped below the wave trigger threshold
            float hpPct = bossCreature.health.summaryHealth.SummaryHealthPercent;

            if (!wavesTriggered && hpPct <= WAVE_TRIGGER_HP_PCT)
            {
                wavesTriggered = true;
                lastWaveSpawnTick = Find.TickManager.TicksGame;

                // Spawn first wave immediately
                SpawnWave();

                Log.Message($"[Isekai WorldBoss] Wave spawning triggered! Boss at {hpPct * 100f:F0}% HP");
            }

            // Spawn subsequent waves on timer
            if (wavesTriggered && !bossDefeated && wavesSpawned < MAX_WAVES)
            {
                int ticksSinceLastWave = Find.TickManager.TicksGame - lastWaveSpawnTick;
                if (ticksSinceLastWave >= nextWaveInterval)
                {
                    SpawnWave();
                    lastWaveSpawnTick = Find.TickManager.TicksGame;
                    nextWaveInterval = Rand.Range(WAVE_MIN_INTERVAL_TICKS, WAVE_MAX_INTERVAL_TICKS);
                }
            }
        }

        /// <summary>
        /// Spawns a wave of S-ranked copies of the boss creature at normal size near the boss.
        /// Wave mobs are naturally normal-sized because we NEVER mutate the shared PawnKindDef.
        /// Visual scaling is purely per-pawn via GraphicFor postfix on world boss IDs only.
        /// ManhunterPermanent provides aggression; HostileTo patch prevents infighting.
        /// </summary>
        private void SpawnWave()
        {
            if (bossCreature == null || bossCreature.Dead || bossCreature.Map == null) return;
            if (bossKind == null) return;

            Map bossMap = bossCreature.Map;
            int mobCount = Rand.RangeInclusive(WAVE_MOB_COUNT_MIN, WAVE_MOB_COUNT_MAX);
            wavesSpawned++;

            // Determine hostile faction for wave mobs
            Faction hostileFaction = bossCreature.Faction
                ?? Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile)
                ?? Faction.OfInsects;

            List<Pawn> waveCreatures = new List<Pawn>();

            for (int i = 0; i < mobCount; i++)
            {
                // Spawn near the boss, not at map edges
                IntVec3 spawnLoc = bossCreature.Position;
                CellFinder.TryFindRandomCellNear(bossCreature.Position, bossMap, 12,
                    c => c.Standable(bossMap) && !c.Fogged(bossMap), out spawnLoc);

                var waveReq = new PawnGenerationRequest(
                    bossKind,
                    faction: hostileFaction,
                    PawnGenerationContext.NonPlayer,
                    bossMap.Tile,
                    forceGenerateNewPawn: true,
                    allowDowned: false,
                    allowDead: false
                );
                // Wave mobs are ephemeral — no auto-generated parent/sibling relations.
                waveReq.RelationWithExtraPawnChanceFactor = 0f;
                waveReq.ColonistRelationChanceFactor = 0f;
                Pawn waveMob = PawnGenerator.GeneratePawn(waveReq);

                if (waveMob == null) continue;

                GenSpawn.Spawn(waveMob, spawnLoc, bossMap, Rot4.Random);

                // Set S-rank (not Nation — these are normal-sized minions)
                var mobRankComp = waveMob.TryGetComp<MobRankComponent>();
                if (mobRankComp != null)
                {
                    mobRankComp.SetRankOverride(MobRankTier.S);
                    mobRankComp.SetEliteOverride(true);
                }

                // Full health, combat-ready
                waveMob.health.Reset();
                IncidentWorker_IsekaiHunt.RemoveIncapacitatingConditions(waveMob);

                // Ensure hostile faction
                if (waveMob.Faction == null || !waveMob.Faction.HostileTo(Faction.OfPlayer))
                {
                    waveMob.SetFaction(hostileFaction, null);
                }

                // Apply manhunter for active aggression (necessary for animals).
                // HostileTo patch prevents boss-team infighting.
                if (waveMob.mindState?.mentalStateHandler != null)
                {
                    waveMob.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent, forced: true);
                }

                // Register as boss-team for HostileTo patch
                WorldBossSizePatch.RegisterWaveMob(waveMob);

                waveCreatures.Add(waveMob);
                spawnedWaveMobs.Add(waveMob);
            }

            if (waveCreatures.Any())
            {
                // === DRAMATIC SUMMONING VFX ===
                IsekaiAnimations.PlayWorldBossSummonEffect(bossCreature, waveCreatures);

                // Wave notification
                Messages.Message(
                    "<color=#FFD700>" + "Isekai_WorldBoss_WaveSpawn".Translate(
                        waveCreatures.Count, bossKind.LabelCap, wavesSpawned
                    ) + "</color>",
                    new LookTargets(waveCreatures),
                    MessageTypeDefOf.ThreatBig
                );

                Log.Message($"[Isekai WorldBoss] Wave {wavesSpawned}: Spawned {waveCreatures.Count}x S-rank {bossKind.LabelCap} (normal size, manhunter + hostility override)");
            }
        }

        /// <summary>
        /// Finds all non-player faction pawns that are allied to the player and
        /// re-sets their enemyTarget to the boss. This ensures they keep fighting
        /// the boss instead of standing around after their initial target dies or
        /// their current combat job completes.
        /// </summary>
        private void RetargetAlliedPawns()
        {
            if (bossCreature == null || bossCreature.Dead || !bossCreature.Spawned) return;

            var playerFaction = Faction.OfPlayer;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.Downed) continue;
                if (pawn.Faction == null || pawn.Faction == playerFaction) continue;
                if (pawn.Faction.HostileTo(playerFaction)) continue;

                // This is an allied faction pawn — ensure they're targeting the boss
                if (pawn.mindState != null)
                {
                    // Only retarget if they have no current enemy or their target is dead
                    var currentTarget = pawn.mindState.enemyTarget;
                    if (currentTarget == null || (currentTarget is Pawn targetPawn && (targetPawn.Dead || targetPawn.Destroyed)))
                    {
                        pawn.mindState.enemyTarget = bossCreature;
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref bossCreature, "bossCreature");
            Scribe_Defs.Look(ref bossKind, "bossKind");
            Scribe_Values.Look(ref wavesTriggered, "wavesTriggered", false);
            Scribe_Values.Look(ref bossDefeated, "bossDefeated", false);
            Scribe_Values.Look(ref lastWaveSpawnTick, "lastWaveSpawnTick", -1);
            Scribe_Values.Look(ref wavesSpawned, "wavesSpawned", 0);
            Scribe_Values.Look(ref nextWaveInterval, "nextWaveInterval", WAVE_MAX_INTERVAL_TICKS);
            Scribe_Values.Look(ref lastReinforcementTick, "lastReinforcementTick", -1);
            Scribe_Values.Look(ref reinforcementsSpawned, "reinforcementsSpawned", 0);
            Scribe_Collections.Look(ref spawnedWaveMobs, "spawnedWaveMobs", LookMode.Reference);

            if (spawnedWaveMobs == null)
                spawnedWaveMobs = new List<Pawn>();

            // NOTE: Visual/team re-registration now happens in ReRegisterAfterLoad(),
            // called from Game.FinalizeInit AFTER ClearAll. This prevents the old
            // PostLoadInit registrations from being wiped by Game.FinalizeInit.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Just log — actual re-registration deferred to Game.FinalizeInit
                Log.Message($"[Isekai WorldBoss] MapComponent loaded. Boss: {bossCreature?.LabelCap ?? "none"}, waves: {spawnedWaveMobs?.Count ?? 0}");
            }
        }
    }
}
