using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;
using IsekaiLeveling.UI;
using IsekaiLeveling.SkillTree;
using IsekaiLeveling.Effects;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Patches;

namespace IsekaiLeveling
{
    /// <summary>
    /// Component attached to pawns to track their Isekai progression
    /// Simplified: Level, XP, Stats, and Titles only
    /// </summary>
    public class IsekaiComponent : ThingComp
    {
        // ── Fast Component Lookup Cache ──
        // Avoids O(n) GetComp<T>() linear scans on hot paths like stat calculation.
        // Registered in PostSpawnSetup, removed in PostDeSpawn, cleared on Game.FinalizeInit.
        private static readonly Dictionary<int, IsekaiComponent> compCache = new Dictionary<int, IsekaiComponent>();

        /// <summary>
        /// O(1) cached lookup. Falls back to GetComp for edge cases (caravan/world pawns).
        /// Use this on hot paths instead of pawn.GetComp&lt;IsekaiComponent&gt;().
        /// </summary>
        public static IsekaiComponent GetCached(Pawn pawn)
        {
            if (pawn == null) return null;
            if (compCache.TryGetValue(pawn.thingIDNumber, out var comp))
                return comp;
            // Fallback: pawn not in cache (caravan, world pawn, newly created, etc.)
            // Slow path but self-healing — caches for next call
            comp = pawn.GetComp<IsekaiComponent>();
            if (comp != null)
                compCache[pawn.thingIDNumber] = comp;
            return comp;
        }

        /// <summary>Clear the component cache. Called from Game.FinalizeInit.</summary>
        public static void ClearCompCache()
        {
            compCache.Clear();
        }

        /// <summary>
        /// Get all humanlike player-faction pawns with IsekaiComponent on a map.
        /// Unlike FreeColonists, this includes ghouls and other Anomaly DLC pawns
        /// that are part of the player faction but filtered out by FreeColonists.
        /// </summary>
        public static List<Pawn> GetIsekaiPawnsOnMap(Map map)
        {
            if (map == null) return new List<Pawn>();
            return map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                .Where(p => p.RaceProps.Humanlike && !p.IsPrisoner && GetCached(p) != null)
                .ToList();
        }

        /// <summary>
        /// Get all humanlike player-faction pawns with IsekaiComponent across all maps.
        /// Unlike FreeColonists, this includes ghouls and other Anomaly DLC pawns.
        /// </summary>
        public static List<Pawn> GetIsekaiPawnsAllMaps()
        {
            return Find.Maps
                .SelectMany(m => m.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                .Where(p => p.RaceProps.Humanlike && !p.IsPrisoner && GetCached(p) != null)
                .ToList();
        }

        /// <summary>
        /// Get all humanlike player-faction pawns with IsekaiComponent in caravans.
        /// </summary>
        public static List<Pawn> GetIsekaiPawnsInCaravans()
        {
            return Find.WorldObjects.Caravans
                .Where(c => c.IsPlayerControlled)
                .SelectMany(c => c.PawnsListForReading)
                .Where(p => p.RaceProps.Humanlike && GetCached(p) != null)
                .ToList();
        }

        // Core leveling data
        public int currentLevel = 1;
        public int currentXP = 0;

        // Whether stats have been initialized (prevents re-randomization on save/load)
        public bool statsInitialized = false;

        // Whether Isekai traits have been rolled for this pawn (prevents re-rolling on every load)
        public bool traitsRolled = false;

        // RPG Stats allocation
        public IsekaiStatAllocation stats = new IsekaiStatAllocation();

        // Title system
        public IsekaiTitleTracker titles = new IsekaiTitleTracker();
        
        // Passive skill tree
        public PassiveTreeTracker passiveTree = new PassiveTreeTracker();
        
        // Weapon mastery (per-weapon-type combat proficiency)
        public Forge.WeaponMasteryTracker weaponMastery = new Forge.WeaponMasteryTracker();
        
        // ── Trait mechanic timers ──
        // EchoOfDefeat: temporary x2 XP bonus after being downed (tick when it expires)
        public int echoOfDefeatActiveUntilTick = 0;
        // Undying: cheat-death cooldown (tick when cooldown expires, 0 = never triggered)
        public int undyingCooldownTick = 0;
        
        // Auto-distribute stat points on level-up (player toggle)
        public bool autoDistributeStats = false;

        // ── XP batching (VBE performance fix) ──
        // Small, frequent XP gains (e.g. per-hit combat XP) are accumulated and processed
        // in batches to avoid per-hit trait scanning, animation spam, and stat cache
        // invalidation that causes lag with mods like Vanilla Books Expanded.
        private int pendingXP = 0;
        private int lastXPFlushTick = -999;
        private const int XP_BATCH_INTERVAL = 60;      // ticks between flushes (~1 second)
        private const int XP_IMMEDIATE_THRESHOLD = 20;  // raw XP >= this bypasses batching

        // Cached stat bonuses
        private Dictionary<StatDef, float> cachedStatBonuses = new Dictionary<StatDef, float>();
        private bool statBonusesDirty = true;

        public Pawn Pawn => parent as Pawn;

        /// <summary>Marks stat bonuses for recalculation (e.g. after AC2 stack transfer).</summary>
        public void InvalidateStatCache() => statBonusesDirty = true;
        
        public int Level => currentLevel;
        
        /// <summary>
        /// XP required for next level using polynomial scaling formula
        /// Uses: base * level^exponent for smooth, achievable progression
        /// Level 1→2: 100 XP, Level 50→51: ~35K XP, Level 100→101: 100K XP
        /// </summary>
        public int XPToNextLevel
        {
            get
            {
                // Polynomial scaling: 100 * level^1.5
                // This grows much slower than exponential and stays achievable
                float baseXP = 100f;
                float exponent = 1.5f;
                return Mathf.Max(100, Mathf.RoundToInt(baseXP * Mathf.Pow(currentLevel, exponent)));
            }
        }
        
        public float LevelProgress => (float)currentXP / Mathf.Max(1, XPToNextLevel);

        public override void PostExposeData()
        {
            base.PostExposeData();
            
            Scribe_Values.Look(ref currentLevel, "currentLevel", 1);
            Scribe_Values.Look(ref currentXP, "currentXP", 0);
            Scribe_Values.Look(ref statsInitialized, "statsInitialized", false);
            Scribe_Values.Look(ref traitsRolled, "traitsRolled", false);

            // Trait mechanic timers
            Scribe_Values.Look(ref echoOfDefeatActiveUntilTick, "echoOfDefeatActiveUntilTick", 0);
            Scribe_Values.Look(ref undyingCooldownTick, "undyingCooldownTick", 0);
            Scribe_Values.Look(ref autoDistributeStats, "autoDistributeStats", false);

            // Save/load RPG stats
            Scribe_Deep.Look(ref stats, "isekaiStats");
            if (stats == null)
                stats = new IsekaiStatAllocation();
            
            // Save/load titles
            Scribe_Deep.Look(ref titles, "isekaiTitles");
            if (titles == null)
                titles = new IsekaiTitleTracker();
            
            // Save/load passive tree
            Scribe_Deep.Look(ref passiveTree, "passiveTree");
            if (passiveTree == null)
                passiveTree = new PassiveTreeTracker();
            
            // Save/load weapon mastery
            Scribe_Deep.Look(ref weaponMastery, "weaponMastery");
            if (weaponMastery == null)
                weaponMastery = new Forge.WeaponMasteryTracker();
                
            statBonusesDirty = true;
        }

        /// <summary>
        /// Called when the pawn spawns on a map.
        /// Handles mid-save mod addition: if the mod was added to an existing save,
        /// PostExposeData finds no saved Isekai data and leaves defaults. We detect this
        /// and initialize appropriate stats here (using wealth-based scaling if applicable).
        /// Also handles saves from before the statsInitialized flag was added.
        /// </summary>
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // Register in the fast-lookup cache
            compCache[parent.thingIDNumber] = this;

            // Migration: strip the removed Isekai_SystemGlitch trait from loaded pawns.
            // This trait was removed in v1.1.5 — old saves may still have it until cleaned up.
            try
            {
                if (Pawn?.story?.traits != null)
                {
                    var allTraits = Pawn.story.traits.allTraits;
                    for (int i = allTraits.Count - 1; i >= 0; i--)
                    {
                        if (allTraits[i]?.def?.defName == "Isekai_SystemGlitch")
                        {
                            Pawn.story.traits.RemoveTrait(allTraits[i]);
                        }
                    }
                }
            }
            catch { /* silently ignore if trait system is unavailable */ }

            try
            {
                if (!statsInitialized)
                {
                    // If the pawn already has non-default data, this is an old save from
                    // before the statsInitialized flag was added. Data was correctly restored
                    // by PostExposeData - just set the flag.
                    bool hasExistingData = currentLevel > 1 || stats.TotalAllocatedPoints > 0;

                    if (!hasExistingData)
                    {
                        // Check resurrection cache before generating fresh stats.
                        // If this pawn died and was resurrected, restore their original progression.
                        if (!Patches.ResurrectionPreserver.TryRestore(Pawn, this))
                        {
                            // No cached data: mid-save mod addition or genuinely new pawn. Generate stats.
                            PawnStatGenerator.InitializePawnStats(Pawn, this);
                        }
                    }
                    statsInitialized = true;
                }
                
                // Check resurrection cache even when statsInitialized is true.
                // This handles cases where the comp was re-created with fresh random stats
                // (InitializeComps patch set statsInitialized = true) but the original pawn
                // had higher progression before death. Only runs on actual spawns, not save/load.
                if (!respawningAfterLoad)
                {
                    Patches.ResurrectionPreserver.TryRestore(Pawn, this);
                }
                
                // Retroactive trait roll for existing saves upgrading to v1.1.2
                // Runs once per pawn, then traitsRolled is set to true and saved.
                if (!traitsRolled)
                {
                    IsekaiTraitHelper.RollRandomTraits(Pawn);
                    traitsRolled = true;
                }

                // Retroactive passive point grant for existing saves upgrading to v1.1.1
                if (passiveTree != null)
                    passiveTree.RetroactiveGrant(currentLevel);

                // Auto-assign class tree and unlock nodes proportional to level
                // Player colonists choose their own class via the constellation UI — only NPCs get auto-assigned.
                if (currentLevel >= 11)
                {
                    bool isPlayerPawn = Pawn?.Faction != null && Pawn.Faction.IsPlayer;
                    if (!isPlayerPawn)
                    {
                        if (string.IsNullOrEmpty(passiveTree?.assignedTree))
                            TreeAutoAssigner.AssignTreeProgression(passiveTree, currentLevel, stats);
                        else
                            TreeAutoAssigner.AutoSpendAccumulated(passiveTree);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] PostSpawnSetup error for {Pawn?.LabelShort}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean up per-pawn static cache entries when a pawn is de-spawned.
        /// Prevents memory leaks from accumulating entries for dead/gone pawns.
        /// </summary>
        public override void PostDeSpawn(Map map, DestroyMode mode)
        {
            base.PostDeSpawn(map, mode);
            try
            {
                int id = parent.thingIDNumber;
                compCache.Remove(id);
                AuraSystem.CleanupPawn(id);
                Patch_LifespanBonus.CleanupPawn(id);
                Patch_WorkXP.CleanupPawn(id);
                Patch_MeditationXP.CleanupPawn(id);
                Patch_AbilityXP.CleanupPawn(id);
                Patch_AbilityXP_World.CleanupPawn(id);
                Patch_PsychicEntropyXP.CleanupPawn(id);
            }
            catch { /* Silently ignore cleanup errors */ }
        }

        /// <summary>
        /// Periodic tick (every 250 ticks) for trait mechanic timers.
        /// Handles SystemGlitch stat shuffling and batched XP flush.
        /// </summary>
        public override void CompTickRare()
        {
            base.CompTickRare();
            try
            {
                if (Pawn == null || Pawn.Dead) return;

                // Flush any batched XP that hasn't been processed yet
                FlushPendingXP();

                // Process deferred level-ups: if currentXP exceeds XPToNextLevel
                // (e.g. from a large quest reward that hit the 50-level cap),
                // continue leveling up so XP doesn't sit dormant in the bar.
                if (currentXP >= XPToNextLevel && currentLevel < (IsekaiMod.Settings?.MaxLevel ?? 9999))
                {
                    ProcessDeferredLevelUps();
                }

                // Update cached colonist count for Rallying Presence (safe context)
                if (Pawn.Map != null)
                    SkillTree.PassiveTreeTracker.UpdateColonistCountCache(Pawn.Map);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] CompTickRare error for {Pawn?.LabelShort}: {ex.Message}");
            }
        }

        /// <summary>
        /// Auto-distribute all available stat points weighted towards the pawn's class affinity.
        /// Primary stat gets 3 shares, secondary 2 shares, others 1 each.
        /// If no class is assigned, distributes evenly across all stats.
        /// </summary>
        public void AutoDistributeByClass()
        {
            if (stats.availableStatPoints <= 0) return;

            // Build weight table based on assigned class
            int[] weights = { 1, 1, 1, 1, 1, 1 }; // STR, DEX, VIT, INT, WIS, CHA

            string className = passiveTree?.assignedTree;
            if (!string.IsNullOrEmpty(className))
            {
                var affinity = GetClassStatAffinity(className);
                if (affinity.HasValue)
                {
                    weights[(int)affinity.Value.primary] += 2;   // Primary: total 3
                    weights[(int)affinity.Value.secondary] += 1; // Secondary: total 2 (or 4 if same)
                }
            }

            // Build weighted cycle list
            var cycle = new System.Collections.Generic.List<IsekaiStatType>();
            for (int s = 0; s < 6; s++)
                for (int w = 0; w < weights[s]; w++)
                    cycle.Add((IsekaiStatType)s);

            int maxStat = IsekaiStatAllocation.GetEffectiveMaxStat();
            int idx = 0;
            int safety = stats.availableStatPoints * cycle.Count;
            while (stats.availableStatPoints > 0 && safety > 0)
            {
                IsekaiStatType stat = cycle[idx % cycle.Count];
                if (stats.GetStat(stat) < maxStat)
                {
                    stats.SetStat(stat, stats.GetStat(stat) + 1);
                    stats.availableStatPoints--;
                }
                idx++;
                safety--;
            }
            statBonusesDirty = true;
        }

        private static (IsekaiStatType primary, IsekaiStatType secondary)? GetClassStatAffinity(string className)
        {
            switch (className)
            {
                case "Knight":      return (IsekaiStatType.Strength,     IsekaiStatType.Vitality);
                case "Berserker":   return (IsekaiStatType.Strength,     IsekaiStatType.Strength);
                case "Duelist":     return (IsekaiStatType.Dexterity,    IsekaiStatType.Strength);
                case "Ranger":      return (IsekaiStatType.Dexterity,    IsekaiStatType.Wisdom);
                case "Paladin":     return (IsekaiStatType.Vitality,     IsekaiStatType.Charisma);
                case "Survivor":    return (IsekaiStatType.Vitality,     IsekaiStatType.Dexterity);
                case "Mage":        return (IsekaiStatType.Intelligence, IsekaiStatType.Wisdom);
                case "Alchemist":   return (IsekaiStatType.Intelligence, IsekaiStatType.Wisdom);
                case "Crafter":     return (IsekaiStatType.Intelligence, IsekaiStatType.Dexterity);
                case "Sage":        return (IsekaiStatType.Wisdom,       IsekaiStatType.Intelligence);
                case "Beastmaster": return (IsekaiStatType.Wisdom,       IsekaiStatType.Charisma);
                case "Leader":      return (IsekaiStatType.Charisma,     IsekaiStatType.Wisdom);
                default:            return null;
            }
        }

        /// <summary>
        /// Award XP to this pawn.
        /// Small, frequent gains (combat hits) are batched to reduce per-hit overhead
        /// from trait scanning, animations, and stat-cache invalidation that causes
        /// lag with mods like Vanilla Books Expanded.
        /// </summary>
        public void GainXP(int amount, string source = null)
        {
            // Only player faction pawns should gain XP to prevent enemy runaway leveling
            if (Pawn?.Faction == null || !Pawn.Faction.IsPlayer)
                return;
            if (currentLevel >= (IsekaiMod.Settings?.MaxLevel ?? 9999))
                return;

            int currentTick = Find.TickManager.TicksGame;
            bool shouldBatch = amount < XP_IMMEDIATE_THRESHOLD
                               && (currentTick - lastXPFlushTick) < XP_BATCH_INTERVAL;

            if (shouldBatch)
            {
                // Accumulate raw XP; skip trait checks, animations, level-up until flush
                pendingXP += amount;
                return;
            }

            // Flush: merge any pending XP into this call
            int totalRaw = amount + pendingXP;
            pendingXP = 0;
            lastXPFlushTick = currentTick;

            ProcessXPGain(totalRaw, source);
        }

        /// <summary>
        /// Force-flush any accumulated XP (called from CompTickRare as a safety net).
        /// </summary>
        public void FlushPendingXP()
        {
            if (pendingXP <= 0) return;
            int raw = pendingXP;
            pendingXP = 0;
            lastXPFlushTick = Find.TickManager.TicksGame;
            ProcessXPGain(raw, "Combat");
        }

        /// <summary>
        /// Process deferred level-ups when excess XP is sitting in currentXP.
        /// Called from CompTickRare to drain XP that exceeded the per-call level cap.
        /// </summary>
        private void ProcessDeferredLevelUps()
        {
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            bool showXPNotifs = IsekaiMod.Settings?.EnableXPNotifications ?? true;
            int levelsGained = 0;
            const int MAX_LEVELS_PER_TICK = 50;
            while (currentXP >= XPToNextLevel && currentLevel < maxLevel && levelsGained < MAX_LEVELS_PER_TICK)
            {
                LevelUp();
                levelsGained++;
            }
        }

        /// <summary>
        /// Core XP processing: applies multipliers, animations, level-ups, bond sharing.
        /// </summary>
        private void ProcessXPGain(int rawAmount, string source)
        {
            if (rawAmount <= 0) return;

            // Use null-safe settings access
            float xpMult = IsekaiMod.Settings?.XPMultiplier ?? 3f;
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            bool showXPNotifs = IsekaiMod.Settings?.EnableXPNotifications ?? true;

            if (currentLevel >= maxLevel) return;
            
            // Apply global XP multiplier
            int modifiedAmount = Mathf.RoundToInt(rawAmount * xpMult);
            
            // Apply trait-based XP multiplier (most expensive per-call cost)
            float traitXPMult = IsekaiTraitHelper.GetXPMultiplier(Pawn, currentLevel, source);
            if (traitXPMult != 1f)
                modifiedAmount = Mathf.RoundToInt(modifiedAmount * traitXPMult);
            
            // Ensure at least 1 XP if original amount was positive
            if (rawAmount > 0 && modifiedAmount <= 0)
                modifiedAmount = 1;
                
            currentXP += modifiedAmount;
            
            // Show golden XP animation if enabled
            if (showXPNotifs && Pawn.Spawned)
            {
                // Use smaller effect for minor XP gains, larger for significant ones
                if (modifiedAmount >= 15)
                {
                    IsekaiAnimations.PlayXPGainEffect(Pawn, modifiedAmount, source);
                }
                else if (modifiedAmount >= 5)
                {
                    IsekaiAnimations.PlaySmallXPEffect(Pawn, modifiedAmount);
                }
                // Very small gains (1-4 XP) don't show animation to avoid spam
            }
            
            // Check for level up — safety cap prevents infinite loops from bugs,
            // but excess XP is KEPT (never discarded) so it carries to the next tick.
            int levelsGained = 0;
            const int MAX_LEVELS_PER_CALL = 50;
            while (currentXP >= XPToNextLevel && currentLevel < maxLevel && levelsGained < MAX_LEVELS_PER_CALL)
            {
                LevelUp();
                levelsGained++;
            }
            // Excess XP carries over naturally — no discard

            // Bond XP sharing: bonded creatures receive 30% of raw XP
            if (!isSharingBondXP && rawAmount > 0 && Pawn?.relations != null)
            {
                int sharedXP = Mathf.RoundToInt(rawAmount * 0.3f);
                if (sharedXP > 0)
                {
                    isSharingBondXP = true;
                    try
                    {
                        foreach (var rel in Pawn.relations.DirectRelations)
                        {
                            if (rel.def == PawnRelationDefOf.Bond && rel.otherPawn != null
                                && !rel.otherPawn.Dead && !rel.otherPawn.Destroyed)
                            {
                                var rankComp = rel.otherPawn.TryGetComp<MobRankComponent>();
                                if (rankComp != null)
                                {
                                    rankComp.GainXP(sharedXP, "Bond");
                                }
                            }
                        }

                        // Golem XP sharing (RoM): golems can't be bonded like animals,
                        // so we route a bond-equivalent share through the golem's
                        // CompGolem.pawnMaster link when this pawn is the master.
                        if (IsekaiLeveling.Compatibility.ModCompatibility.RimWorldOfMagicActive && Pawn != null)
                        {
                            foreach (var golem in IsekaiLeveling.Compatibility.RoMCompatibility.GetGolemsOwnedBy(Pawn))
                            {
                                var rankComp = golem.TryGetComp<MobRankComponent>();
                                if (rankComp != null)
                                {
                                    rankComp.GainXP(sharedXP, "Golem");
                                }
                            }
                        }
                    }
                    finally
                    {
                        isSharingBondXP = false;
                    }
                }
            }
        }

        // Recursion guard for bond XP sharing
        private static bool isSharingBondXP = false;

        // Track last notification to throttle spam
        private int lastNotifiedLevel = 0;
        private int lastLoggedLevel = 0;
        
        /// <summary>
        /// Process a level up
        /// </summary>
        private void LevelUp()
        {
            int previousLevel = currentLevel;
            currentXP -= XPToNextLevel;
            currentLevel++;
            stats.OnLevelUp(); // Grant stat points
            
            // Grant bonus stat points from traits
            int traitBonusPoints = IsekaiTraitHelper.GetBonusStatPoints(Pawn, currentLevel);
            if (traitBonusPoints > 0)
                stats.availableStatPoints += traitBonusPoints;
            
            passiveTree.OnLevelUp(currentLevel, Pawn); // Grant star points (trait-aware)
            statBonusesDirty = true;

            // Player toggle: auto-distribute stat points by class weights
            if (autoDistributeStats && stats.availableStatPoints > 0)
            {
                AutoDistributeByClass();
            }

            // Auto-assign class tree when reaching D rank (level 11) for the first time
            // Player colonists choose their own class via the constellation UI — only NPCs get auto-assigned.
            if (currentLevel >= 11)
            {
                bool isPlayerPawn = Pawn?.Faction != null && Pawn.Faction.IsPlayer;
                if (!isPlayerPawn)
                {
                    if (string.IsNullOrEmpty(passiveTree.assignedTree))
                        TreeAutoAssigner.AssignTreeProgression(passiveTree, currentLevel, stats);
                    else
                        TreeAutoAssigner.AutoSpendAccumulated(passiveTree);
                }
            }
            
            // Update rank trait if we crossed a rank threshold
            if (GetRankFromLevel(previousLevel) != GetRankFromLevel(currentLevel))
            {
                PawnStatGenerator.UpdateRankTraitFromStats(Pawn, this);
            }
            
            // Notify the player - throttle to every 10 levels to prevent letter spam
            if (IsekaiMod.Settings.EnableLevelUpNotifications && 
                (currentLevel <= 10 || currentLevel % 10 == 0 || currentLevel - lastNotifiedLevel >= 10))
            {
                lastNotifiedLevel = currentLevel;
                string label = $"{Pawn.LabelShort} Leveled Up!";
                string text = $"{Pawn.LabelShort} has reached level {currentLevel}!\n\n" +
                             $"They have {stats.availableStatPoints} stat point(s) to spend.";
                if (IsekaiMod.Settings.UseLevelUpNotice)
                    Messages.Message(label + " (Lv" + currentLevel + ")", Pawn, MessageTypeDefOf.PositiveEvent, false);
                else
                    Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.PositiveEvent, Pawn);
            }
            
            // Golden level up visual effect - only every 5 levels after 10 to reduce spam
            if (Pawn.Spawned && (currentLevel <= 10 || currentLevel % 5 == 0))
            {
                IsekaiAnimations.PlayLevelUpEffect(Pawn, currentLevel);
            }
            
            // Log only significant milestones to prevent log spam
            if (currentLevel <= 10 || currentLevel % 25 == 0 || currentLevel - lastLoggedLevel >= 25)
            {
                lastLoggedLevel = currentLevel;
                Log.Message($"[Isekai Leveling] {Pawn.LabelShort} reached level {currentLevel}");
            }
        }

        /// <summary>
        /// Dev mode function to add levels directly (skips XP requirements)
        /// </summary>
        public void DevAddLevel(int levels)
        {
            int previousLevel = currentLevel;
            
            for (int i = 0; i < levels; i++)
            {
                if (currentLevel >= IsekaiMod.Settings.MaxLevel) break;
                
                currentLevel++;
                stats.OnLevelUp(); // Grant stat points
                int devTraitBonus = IsekaiTraitHelper.GetBonusStatPoints(Pawn, currentLevel);
                if (devTraitBonus > 0) stats.availableStatPoints += devTraitBonus;
                passiveTree.OnLevelUp(currentLevel, Pawn); // Grant star points (trait-aware)
            }

            currentXP = 0;
            statBonusesDirty = true;

            // Player toggle: auto-distribute stat points by class weights
            if (autoDistributeStats && stats.availableStatPoints > 0)
            {
                AutoDistributeByClass();
            }

            // Auto-assign class if crossed D-rank threshold (player colonists choose manually)
            if (currentLevel >= 11)
            {
                bool isPlayerPawn = Pawn?.Faction != null && Pawn.Faction.IsPlayer;
                if (!isPlayerPawn)
                {
                    if (string.IsNullOrEmpty(passiveTree?.assignedTree))
                        TreeAutoAssigner.AssignTreeProgression(passiveTree, currentLevel, stats);
                    else
                        TreeAutoAssigner.AutoSpendAccumulated(passiveTree);
                }
            }
            
            // Update rank trait if we crossed a rank threshold
            if (GetRankFromLevel(previousLevel) != GetRankFromLevel(currentLevel))
            {
                PawnStatGenerator.UpdateRankTraitFromStats(Pawn, this);
            }
            
            if (Pawn.Spawned)
            {
                MoteMaker.ThrowText(Pawn.DrawPos, Pawn.Map, $"DEV: Level {currentLevel}", Color.green);
            }
            
            Log.Message($"[Isekai Leveling] DEV: {Pawn.LabelShort} set to level {currentLevel}");
        }

        /// <summary>
        /// Get the total stat bonus from level and stats
        /// </summary>
        public float GetStatBonus(StatDef stat)
        {
            if (statBonusesDirty)
            {
                RecalculateStatBonuses();
            }
            
            return cachedStatBonuses.TryGetValue(stat, out float bonus) ? bonus : 0f;
        }

        private void RecalculateStatBonuses()
        {
            cachedStatBonuses.Clear();
            // Stats are applied directly through Harmony patches in StatPatches.cs
            statBonusesDirty = false;
        }
        
        /// <summary>
        /// Get the rank string based on level
        /// </summary>
        public string GetRankString()
        {
            return GetRankFromLevel(currentLevel);
        }
        
        /// <summary>
        /// Get rank string from a specific level value
        /// </summary>
        private static string GetRankFromLevel(int level)
        {
            if (level >= 401) return "SSS";
            if (level >= 201) return "SS";
            if (level >= 101) return "S";
            if (level >= 51) return "A";
            if (level >= 26) return "B";
            if (level >= 18) return "C";
            if (level >= 11) return "D";
            if (level >= 6) return "E";
            return "F";
        }
        
        /// <summary>
        /// Get rank as enum for aura system comparison
        /// </summary>
        public MobRanking.MobRankTier GetRank()
        {
            if (currentLevel >= 401) return MobRanking.MobRankTier.SSS;
            if (currentLevel >= 201) return MobRanking.MobRankTier.SS;
            if (currentLevel >= 101) return MobRanking.MobRankTier.S;
            if (currentLevel >= 51) return MobRanking.MobRankTier.A;
            if (currentLevel >= 26) return MobRanking.MobRankTier.B;
            if (currentLevel >= 18) return MobRanking.MobRankTier.C;
            if (currentLevel >= 11) return MobRanking.MobRankTier.D;
            if (currentLevel >= 6) return MobRanking.MobRankTier.E;
            return MobRanking.MobRankTier.F;
        }
        
        /// <summary>
        /// Add rank info to the inspect string for player pawns
        /// </summary>
        public override string CompInspectStringExtra()
        {
            if (!IsekaiLevelingSettings.showMobRanks) return null;
            
            // Show rank info: "Rank: A - Level 55"
            return $"Rank: {GetRankString()} - Level {currentLevel}";
        }
    }
    
    /// <summary>
    /// Properties for the Isekai component
    /// </summary>
    public class IsekaiComponentProperties : CompProperties
    {
        public IsekaiComponentProperties()
        {
            compClass = typeof(IsekaiComponent);
        }
    }
}
