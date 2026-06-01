using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.SkillTree;
using RimWorld.Planet;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Helper class for getting mech overseer (mechanitor) - Biotech DLC
    /// </summary>
    public static class MechanitorHelper
    {
        /// <summary>
        /// Check if a pawn is a controlled mech (has a mechanitor)
        /// </summary>
        public static bool IsControlledMech(Pawn pawn)
        {
            if (pawn == null) return false;
            if (!ModsConfig.BiotechActive) return false;
            
            // Check if it's a mechanoid with an overseer
            return pawn.RaceProps.IsMechanoid && pawn.GetOverseer() != null;
        }
        
        /// <summary>
        /// Get the mechanitor controlling a mech, or null if not a controlled mech
        /// </summary>
        public static Pawn GetMechanitor(Pawn mech)
        {
            if (mech == null) return null;
            if (!ModsConfig.BiotechActive) return null;
            if (!mech.RaceProps.IsMechanoid) return null;
            
            return mech.GetOverseer();
        }
    }
    
    /// <summary>
    /// Tracks damage dealt to entities for shared kill XP distribution
    /// </summary>
    public static class DamageTracker
    {
        // Dictionary mapping target Thing ID to dictionary of attacker pawn ID -> damage dealt
        private static Dictionary<int, Dictionary<int, float>> damageRecords = new Dictionary<int, Dictionary<int, float>>();
        
        // Cleanup old records periodically (every 2500 ticks = ~42 seconds)
        private static int lastCleanupTick = 0;
        private const int CLEANUP_INTERVAL = 2500;
        private const int RECORD_EXPIRY_TICKS = 15000; // Records expire after ~4 minutes
        
        private static Dictionary<int, int> recordTimestamps = new Dictionary<int, int>();
        
        /// <summary>
        /// Register damage dealt by an attacker to a target.
        /// If the attacker is a controlled mech and MechanitorXP is enabled, also registers damage for the mechanitor.
        /// </summary>
        public static void RegisterDamage(Thing target, Pawn attacker, float damage)
        {
            if (target == null || attacker == null || damage <= 0) return;
            
            int targetId = target.thingIDNumber;
            int attackerId = attacker.thingIDNumber;
            
            // If attacker is a controlled mech and setting is enabled, also attribute damage to mechanitor
            if (IsekaiLevelingSettings.EnableMechanitorXP && MechanitorHelper.IsControlledMech(attacker))
            {
                Pawn mechanitor = MechanitorHelper.GetMechanitor(attacker);
                if (mechanitor != null)
                {
                    // Register damage for the mechanitor as well
                    RegisterDamageForPawn(targetId, mechanitor.thingIDNumber, damage);
                }
            }
            
            // Register damage for the actual attacker (mech or otherwise)
            RegisterDamageForPawn(targetId, attackerId, damage);
            
            recordTimestamps[targetId] = Find.TickManager?.TicksGame ?? 0;
            
            // Periodic cleanup
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick - lastCleanupTick > CLEANUP_INTERVAL)
            {
                CleanupOldRecords(currentTick);
                lastCleanupTick = currentTick;
            }
        }
        
        /// <summary>
        /// Internal helper to register damage for a specific pawn ID
        /// </summary>
        private static void RegisterDamageForPawn(int targetId, int attackerId, float damage)
        {
            if (!damageRecords.ContainsKey(targetId))
            {
                damageRecords[targetId] = new Dictionary<int, float>();
            }
            
            if (!damageRecords[targetId].ContainsKey(attackerId))
            {
                damageRecords[targetId][attackerId] = 0f;
            }
            
            damageRecords[targetId][attackerId] += damage;
        }
        
        /// <summary>
        /// Get all attackers who damaged a target, with their damage percentages
        /// </summary>
        public static Dictionary<Pawn, float> GetAttackers(Thing target)
        {
            var result = new Dictionary<Pawn, float>();
            
            if (target == null) return result;
            
            int targetId = target.thingIDNumber;
            if (!damageRecords.ContainsKey(targetId)) return result;
            
            var attackerDamage = damageRecords[targetId];
            float totalDamage = 0f;
            
            foreach (var kvp in attackerDamage)
            {
                totalDamage += kvp.Value;
            }
            
            if (totalDamage <= 0) return result;
            
            // Convert to pawn references with percentage contribution
            foreach (var kvp in attackerDamage)
            {
                Pawn attacker = Find.WorldPawns?.AllPawnsAlive?.FirstOrDefault(p => p.thingIDNumber == kvp.Key);
                if (attacker == null)
                {
                    // Try finding on current map
                    foreach (Map map in Find.Maps)
                    {
                        attacker = map.mapPawns.AllPawns.FirstOrDefault(p => p.thingIDNumber == kvp.Key);
                        if (attacker != null) break;
                    }
                }
                
                if (attacker != null)
                {
                    result[attacker] = kvp.Value / totalDamage;
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Clear damage records for a target (call after kill XP is distributed)
        /// </summary>
        public static void ClearRecords(Thing target)
        {
            if (target == null) return;
            int targetId = target.thingIDNumber;
            damageRecords.Remove(targetId);
            recordTimestamps.Remove(targetId);
        }
        
        private static void CleanupOldRecords(int currentTick)
        {
            var expiredTargets = new List<int>();
            
            foreach (var kvp in recordTimestamps)
            {
                if (currentTick - kvp.Value > RECORD_EXPIRY_TICKS)
                {
                    expiredTargets.Add(kvp.Key);
                }
            }
            
            foreach (int targetId in expiredTargets)
            {
                damageRecords.Remove(targetId);
                recordTimestamps.Remove(targetId);
            }
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            damageRecords.Clear();
            recordTimestamps.Clear();
            lastCleanupTick = 0;
        }
    }
    
    /// <summary>
    /// XP gain from combat - melee/ranged damage + track for shared kill XP
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_CombatXP_Melee
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref DamageInfo dinfo)
        {
            if (!(dinfo.Instigator is Pawn attacker)) return;
            
            // Skip non-combat damage types (firefoam, surgery, execution, etc.)
            // These should never award combat XP
            if (dinfo.Def == DamageDefOf.Extinguish ||
                dinfo.Def == DamageDefOf.SurgicalCut ||
                dinfo.Def == DamageDefOf.ExecutionCut ||
                dinfo.Def == DamageDefOf.Stun ||
                dinfo.Def == DamageDefOf.EMP)
                return;
            
            // Skip friendly fire — don't award XP for hitting allied/friendly pawns
            // This prevents exploits like AoE effects (firefoam, explosions) hitting friendlies for XP
            if (attacker.Faction != null && __instance.Faction != null && 
                !attacker.Faction.HostileTo(__instance.Faction))
                return;
            
            // Register damage for shared kill XP (handles mechanitor attribution internally)
            DamageTracker.RegisterDamage(__instance, attacker, dinfo.Amount);

            // ── Divine Retribution (Paladin gimmick) ───────────────────────────────────
            // When the VICTIM is a Paladin: accumulate incoming damage as retribution charge
            var victimComp = IsekaiComponent.GetCached(__instance);
            if (victimComp?.passiveTree?.HasGimmick(ClassGimmickType.DivineRetribution) == true)
            {
                victimComp.passiveTree.AccumulateRetribution(Mathf.Max(1, (int)dinfo.Amount));
            }
            // When the ATTACKER is a Paladin: they just landed a strike — consume and reset charge
            // (the melee damage stat was already read before PreApplyDamage fires, so the bonus already applied)
            var attackerRetComp = IsekaiComponent.GetCached(attacker);
            if (attackerRetComp?.passiveTree?.HasGimmick(ClassGimmickType.DivineRetribution) == true)
            {
                attackerRetComp.passiveTree.ConsumeRetribution();
            }
            // ──────────────────────────────────────────────────────────────────────────

            // ── Inner Calm (Sage gimmick) ─────────────────────────────────────────────
            // When the VICTIM is a Sage: any hit resets the calm timer to now
            if (victimComp?.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
            {
                victimComp.passiveTree.ResetCalmTimer();
            }
            // ──────────────────────────────────────────────────────────────────────────

            // ── Predator Focus (Ranger gimmick) ───────────────────────────────────────
            // When the ATTACKER is a Ranger with ranged weapon: stack focus marks on target
            if (attackerRetComp == null) attackerRetComp = IsekaiComponent.GetCached(attacker);
            if (attackerRetComp?.passiveTree?.HasGimmick(ClassGimmickType.PredatorFocus) == true)
            {
                // Detect ranged hit: weapon is not melee and no body part group (ranged projectile)
                bool isRanged = dinfo.Weapon != null && !dinfo.Weapon.IsMeleeWeapon && dinfo.WeaponBodyPartGroup == null;
                if (isRanged)
                {
                    attackerRetComp.passiveTree.StackPredatorFocus(__instance);
                }
            }
            // ──────────────────────────────────────────────────────────────────────────

            // ── Counter Strike (Duelist gimmick) ──────────────────────────────────────
            // When the ATTACKER is a Duelist and lands a melee hit: consume counter charges
            // (the melee damage stat was already read before PreApplyDamage fires, so the bonus already applied)
            if (attackerRetComp?.passiveTree?.HasGimmick(ClassGimmickType.CounterStrike) == true)
            {
                bool isMelee = dinfo.Weapon != null && dinfo.Weapon.IsMeleeWeapon;
                if (isMelee)
                {
                    attackerRetComp.passiveTree.ConsumeCounterStrike();
                }
            }
            // ──────────────────────────────────────────────────────────────────────────
            
            // Small XP for dealing damage (reduced since kill XP is now higher)
            int xpAmount = (int)(dinfo.Amount * 0.3f);
            if (xpAmount <= 0) return;
            
            // Try to give combat XP to the attacker
            var comp = IsekaiComponent.GetCached(attacker);
            if (comp != null)
            {
                comp.GainXP(xpAmount, "Combat");
            }
            // If attacker is a mech with a mechanitor, give XP to the mechanitor
            else if (IsekaiLevelingSettings.EnableMechanitorXP && MechanitorHelper.IsControlledMech(attacker))
            {
                Pawn mechanitor = MechanitorHelper.GetMechanitor(attacker);
                if (mechanitor != null)
                {
                    var mechComp = IsekaiComponent.GetCached(mechanitor);
                    if (mechComp != null)
                    {
                        mechComp.GainXP(xpAmount, "Combat");
                    }
                }
            }
            // Creature XP: non-humanlike player faction creatures with MobRankComponent
            // Standalone check (not else-if) so controlled mechs also gain creature XP
            if (comp == null && attacker.Faction != null && attacker.Faction.IsPlayer && !attacker.RaceProps.Humanlike)
            {
                var rankComp = attacker.TryGetComp<MobRankComponent>();
                if (rankComp != null)
                {
                    rankComp.GainXP(xpAmount, "Combat");
                }
            }
        }
    }

    /// <summary>
    /// XP gain from kills - distributed to all attackers who contributed
    /// XP scales based on mob rank (F to SSS)
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_KillXP
    {
        // Capture the victim's map before Kill() despawns them
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, out Map __state)
        {
            __state = __instance?.Map;
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo, Map __state)
        {
            // Skip reanimated/mutant pawns (Anomaly DLC) — no double XP for revived corpses
            if (__instance.IsMutant || __instance.IsShambler || __instance.IsGhoul) return;

            // Calculate kill XP based on mob rank
            int baseKillXP = CalculateKillXP(__instance);

            // Same-map filter: when enabled, only award kill XP to pawns on the same map
            // as the victim. Mechanitors are exempt (their mechs fight on their behalf).
            bool requireSameMap = !(IsekaiMod.Settings?.ShareKillXPAcrossMaps ?? false);
            
            // Get all attackers who contributed damage
            var attackers = DamageTracker.GetAttackers(__instance);
            
            if (attackers.Count > 0)
            {
                // Distribute XP among all attackers based on damage contribution
                // Minimum 20% XP even for small contributions
                foreach (var kvp in attackers)
                {
                    Pawn attacker = kvp.Key;
                    float contribution = kvp.Value;

                    // Same-map check: skip attacker if they're on a different map
                    if (requireSameMap && __state != null && attacker.Map != __state)
                    {
                        // Exempt mechanitors — they legitimately earn XP via their mechs on remote maps
                        if (IsekaiLevelingSettings.EnableMechanitorXP && ModsConfig.BiotechActive
                            && attacker.mechanitor != null)
                        {
                            // Mechanitor is allowed cross-map XP
                        }
                        else
                        {
                            continue;
                        }
                    }
                    
                    var comp = IsekaiComponent.GetCached(attacker);
                    if (comp != null)
                    {
                        // Apply rank difference bonus: more XP for killing higher-ranked targets
                        float rankBonus = CalculateRankDifferenceBonus(comp, __instance);
                        
                        // Scale XP by contribution, with minimum 20% for any participant
                        float xpMultiplier = Mathf.Max(0.2f, contribution);
                        int xpGained = (int)(baseKillXP * xpMultiplier * rankBonus);
                        
                        if (xpGained > 0)
                        {
                            comp.GainXP(xpGained, "Kill");
                        }

                        // Berserker gimmick: Blood Frenzy — stack on kill
                        if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
                        {
                            comp.passiveTree.AccumulateFrenzy();
                        }
                    }
                    // Creature kill XP: non-humanlike player faction creatures
                    else if (attacker.Faction != null && attacker.Faction.IsPlayer && !attacker.RaceProps.Humanlike)
                    {
                        var rankComp = attacker.TryGetComp<MobRankComponent>();
                        if (rankComp != null)
                        {
                            float xpMultiplier = Mathf.Max(0.2f, contribution);
                            int xpGained = (int)(baseKillXP * xpMultiplier);
                            if (xpGained > 0)
                            {
                                rankComp.GainXP(xpGained, "Kill");
                            }
                        }
                    }
                }
                
                // Clear damage records for this target
                DamageTracker.ClearRecords(__instance);
            }
            else if (dinfo.HasValue && dinfo.Value.Instigator is Pawn killer)
            {
                // Fallback: if no damage records, award to the killer only
                // But if killer is a mech, award to their mechanitor instead
                Pawn xpRecipient = killer;
                
                if (IsekaiLevelingSettings.EnableMechanitorXP && MechanitorHelper.IsControlledMech(killer))
                {
                    Pawn mechanitor = MechanitorHelper.GetMechanitor(killer);
                    if (mechanitor != null)
                    {
                        xpRecipient = mechanitor;
                    }
                }
                
                var comp = IsekaiComponent.GetCached(xpRecipient);
                if (comp != null)
                {
                    float rankBonus = CalculateRankDifferenceBonus(comp, __instance);
                    int xpGained = (int)(baseKillXP * rankBonus);
                    comp.GainXP(xpGained, "Kill");

                    // Berserker gimmick: Blood Frenzy — stack on kill
                    if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
                    {
                        comp.passiveTree.AccumulateFrenzy();
                    }
                }
                // Creature kill XP fallback: if original killer is a player creature
                // Uses killer (not xpRecipient) so mechs get creature XP even when mechanitor gets pawn XP
                if (killer.Faction != null && killer.Faction.IsPlayer && !killer.RaceProps.Humanlike)
                {
                    var rankComp = killer.TryGetComp<MobRankComponent>();
                    if (rankComp != null)
                    {
                        int xpGained = baseKillXP;
                        if (xpGained > 0)
                        {
                            rankComp.GainXP(xpGained, "Kill");
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Calculate XP reward for killing a pawn based on their rank
        /// </summary>
        private static int CalculateKillXP(Pawn target)
        {
            // Try to get mob rank component
            var mobRankComp = target.GetComp<MobRankComponent>();
            
            if (mobRankComp != null && mobRankComp.IsInitialized)
            {
                // Use rank-based XP scaling
                MobRankTier rank = mobRankComp.Rank;
                bool isElite = mobRankComp.IsElite;
                
                int xp = GetXPForRank(rank);
                
                // Elite mobs give 50% bonus XP
                if (isElite)
                {
                    xp = (int)(xp * 1.5f);
                }
                
                return xp;
            }
            
            // Fallback for pawns without rank component (player pawns, etc.)
            // Use the old type-based system
            if (target.RaceProps.Humanlike)
            {
                // Check if target has IsekaiComponent (player-like pawn)
                var isekaiComp = IsekaiComponent.GetCached(target);
                if (isekaiComp != null)
                {
                    // Scale by target's level
                    int level = isekaiComp.Level;
                    return 50 + (level * 10); // 50 base + 10 per level
                }
                return 300;
            }
            else if (target.RaceProps.IsMechanoid)
            {
                return 400;
            }
            else if (target.RaceProps.predator)
            {
                return 200;
            }
            else if (target.RaceProps.baseBodySize >= 2f)
            {
                return 250;
            }
            
            return 150; // Default
        }
        
        /// <summary>
        /// Get base XP reward for each rank tier
        /// </summary>
        private static int GetXPForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.SSS: return 2000;  // Apex creatures - massive reward
                case MobRankTier.SS:  return 1200;  // Alpha beasts
                case MobRankTier.S:   return 800;   // Legendary
                case MobRankTier.A:   return 500;   // Deadly
                case MobRankTier.B:   return 300;   // Dangerous
                case MobRankTier.C:   return 180;   // Threatening
                case MobRankTier.D:   return 100;   // Common
                case MobRankTier.E:   return 60;    // Weak
                case MobRankTier.F:   return 30;    // Very weak
                default:              return 50;
            }
        }
        
        /// <summary>
        /// Calculate XP bonus multiplier based on rank difference between attacker and target.
        /// Defeating creatures ranked higher than you gives bonus XP (up to 3x).
        /// Defeating creatures ranked lower gives slightly less XP (down to 0.5x).
        /// Each rank tier difference = +25% bonus (or -10% penalty).
        /// </summary>
        private static float CalculateRankDifferenceBonus(IsekaiComponent attackerComp, Pawn target)
        {
            if (attackerComp == null || target == null) return 1f;
            
            // Get attacker's rank tier as int
            int attackerRankValue = RankTierToInt(attackerComp.GetRank());
            
            // Get target's rank tier
            int targetRankValue = 0;
            var mobRankComp = target.GetComp<MobRankComponent>();
            if (mobRankComp != null && mobRankComp.IsInitialized)
            {
                targetRankValue = RankTierToInt(mobRankComp.Rank);
            }
            else
            {
                // For humanlike targets without mob rank, estimate from their level
                var targetComp = IsekaiComponent.GetCached(target);
                if (targetComp != null)
                {
                    targetRankValue = RankTierToInt(targetComp.GetRank());
                }
                else
                {
                    return 1f; // Can't determine rank, no bonus
                }
            }
            
            int rankDiff = targetRankValue - attackerRankValue;
            
            if (rankDiff > 0)
            {
                // Target is higher rank: +25% per rank tier difference, capped at 3x
                return Mathf.Min(3f, 1f + rankDiff * 0.25f);
            }
            else if (rankDiff < 0)
            {
                // Target is lower rank: -10% per rank tier difference, minimum 0.5x
                return Mathf.Max(0.5f, 1f + rankDiff * 0.1f);
            }
            
            return 1f; // Same rank
        }
        
        /// <summary>
        /// Convert MobRankTier enum to integer for comparison
        /// F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8
        /// </summary>
        private static int RankTierToInt(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.F:   return 0;
                case MobRankTier.E:   return 1;
                case MobRankTier.D:   return 2;
                case MobRankTier.C:   return 3;
                case MobRankTier.B:   return 4;
                case MobRankTier.A:   return 5;
                case MobRankTier.S:   return 6;
                case MobRankTier.SS:  return 7;
                case MobRankTier.SSS: return 8;
                default:              return 0;
            }
        }
    }

    /// <summary>
    /// XP gain from work completion
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    public static class Patch_WorkXP
    {
        // Per-pawn, per-job-type cooldown to prevent rapid-fire XP exploits
        // (e.g. prisoner cleaning with Cleaning Area mod causing instant job loops)
        private static Dictionary<int, Dictionary<string, int>> lastWorkXPTick = new Dictionary<int, Dictionary<string, int>>();
        private const int WORK_XP_COOLDOWN_TICKS = 60; // Minimum 1 second between same-job XP awards
        
        [HarmonyPostfix]
        public static void Postfix(Pawn_JobTracker __instance, JobCondition condition, Pawn ___pawn)
        {
            if (condition != JobCondition.Succeeded) return;
            
            Pawn pawn = ___pawn;
            if (pawn == null) return;
            
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;
            
            var job = __instance.curJob;
            if (job == null) return;
            
            // Check if this is a hauling job with equipment/apparel (weapon swapping exploit)
            if ((job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer) && 
                job.targetA.HasThing && 
                (job.targetA.Thing is ThingWithComps twc && 
                 (twc.def.IsWeapon || twc.def.IsApparel)))
            {
                // Don't award XP for equipping/dropping weapons/apparel
                return;
            }
            
            var (xpAmount, sourceName) = GetXPForJob(job.def);
            if (xpAmount > 0)
            {
                // Rate-limit: prevent same pawn from getting XP for the same job type too rapidly
                // This guards against mod interactions that cause jobs to instantly complete in loops
                int pawnId = pawn.thingIDNumber;
                string jobKey = job.def.defName;
                int currentTick = Find.TickManager?.TicksGame ?? 0;
                
                if (lastWorkXPTick.TryGetValue(pawnId, out var pawnJobs))
                {
                    if (pawnJobs.TryGetValue(jobKey, out int lastTick) && 
                        currentTick - lastTick < WORK_XP_COOLDOWN_TICKS)
                    {
                        return; // Too soon, skip XP
                    }
                    pawnJobs[jobKey] = currentTick;
                }
                else
                {
                    lastWorkXPTick[pawnId] = new Dictionary<string, int> { { jobKey, currentTick } };
                }
                
                comp.GainXP(xpAmount, sourceName);

                // Also give XP to the target animal for training and gathering jobs
                if (job.targetA.HasThing && job.targetA.Thing is Pawn targetAnimal
                    && targetAnimal.Faction != null && targetAnimal.Faction.IsPlayer
                    && !targetAnimal.RaceProps.Humanlike)
                {
                    var rankComp = targetAnimal.TryGetComp<MobRankComponent>();
                    if (rankComp != null)
                    {
                        if (job.def == JobDefOf.Train)
                        {
                            rankComp.GainXP(15, "Training");
                        }
                        else if (job.def.defName.Contains("Milk") || job.def.defName.Contains("Shear"))
                        {
                            rankComp.GainXP(5, "Gathering");
                        }
                    }
                }
            }
        }

        private static (int xp, string source) GetXPForJob(JobDef jobDef)
        {
            if (jobDef == null) return (0, null);
            
            string defName = jobDef.defName;
            
            // === CRAFTING (high XP for skilled work) ===
            if (jobDef == JobDefOf.DoBill)
            {
                // DoBill is generic - we check more in Bill patches
                return (15, "Crafting");
            }
            
            // === CONSTRUCTION (reduced XP) ===
            if (jobDef == JobDefOf.FinishFrame) return (8, "Building");
            if (jobDef == JobDefOf.Deconstruct) return (3, "Deconstruct");
            if (defName.Contains("Repair")) return (4, "Repair");
            if (defName.Contains("Smooth")) return (5, "Smoothing");
            
            // === RESEARCH ===
            if (jobDef == JobDefOf.Research) return (15, "Research");
            
            // === MEDICAL ===
            if (jobDef == JobDefOf.TendPatient) return (35, "Medical");
            if (jobDef == JobDefOf.FeedPatient) return (8, "Nursing");
            if (defName.Contains("Rescue")) return (20, "Rescue");
            if (defName.Contains("Surgery") || defName.Contains("Operation")) return (50, "Surgery");
            
            // === HAULING & CLEANING ===
            if (jobDef == JobDefOf.HaulToCell || jobDef == JobDefOf.HaulToContainer) return (3, null);
            if (jobDef == JobDefOf.Clean) return (2, null);
            if (defName.Contains("Refuel")) return (5, null);
            
            // === HUNTING & ANIMALS ===
            if (jobDef == JobDefOf.Hunt) return (15, "Hunting");
            if (jobDef == JobDefOf.Tame) return (30, "Taming");
            if (jobDef == JobDefOf.Train) return (20, "Training");
            if (defName.Contains("Slaughter")) return (12, "Slaughter");
            if (defName.Contains("Milk") || defName.Contains("Shear")) return (8, "Animals");
            
            // === MINING ===
            if (jobDef == JobDefOf.Mine) return (12, "Mining");
            if (defName.Contains("Mine") || defName.Contains("Dig")) return (10, "Mining");
            if (defName.Contains("Drill")) return (10, "Drilling");
            
            // === FARMING & PLANTS ===
            if (jobDef == JobDefOf.Harvest) return (8, "Harvest");
            if (jobDef == JobDefOf.HarvestDesignated) return (8, "Harvest");
            if (jobDef == JobDefOf.CutPlant) return (5, "Cutting");
            if (jobDef == JobDefOf.Sow) return (5, "Planting");
            if (defName.Contains("Harvest")) return (8, "Harvest");
            if (defName.Contains("CutPlant") || defName.Contains("Fell")) return (8, "Woodcutting");
            if (defName.Contains("CutTree") || defName == "CutPlantDesignated") return (10, "Woodcutting");
            
            // === COOKING ===
            if (defName.Contains("Cook")) return (15, "Cooking");
            if (defName.Contains("Butcher")) return (12, "Butchering");
            if (defName.Contains("Brew")) return (18, "Brewing");
            
            // === SMITHING & CRAFTING (by defName patterns) ===
            if (defName.Contains("Smith") || defName.Contains("Forge")) return (25, "Smithing");
            if (defName.Contains("Tailor") || defName.Contains("Sew")) return (18, "Tailoring");
            if (defName.Contains("Sculpt") || defName.Contains("Art")) return (22, "Art");
            if (defName.Contains("Craft") || defName.Contains("Make")) return (15, "Crafting");
            if (defName.Contains("Assemble")) return (20, "Assembly");
            
            // === WARDEN DUTIES ===
            if (defName.Contains("Warden") || defName.Contains("Prisoner")) return (10, "Warden");
            if (defName.Contains("Recruit")) return (25, "Recruiting");
            
            // === MISC ===
            if (defName.Contains("Pray") || defName.Contains("Meditat")) return (8, "Meditation");
            if (defName.Contains("Entertain")) return (12, "Entertain");
            if (defName.Contains("Study") || defName.Contains("Read")) return (10, "Study");
            
            return (0, null);
        }

        /// <summary>
        /// Clean up per-pawn work XP cooldown tracking.
        /// Called from IsekaiComponent.PostDeSpawn.
        /// </summary>
        public static void CleanupPawn(int pawnId)
        {
            lastWorkXPTick.Remove(pawnId);
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            lastWorkXPTick.Clear();
        }
    }
    
    /// <summary>
    /// XP gain when a mineable rock is destroyed (guaranteed mining XP)
    /// </summary>
    [HarmonyPatch(typeof(Mineable), nameof(Mineable.Destroy))]
    public static class Patch_MineableDestroyXP
    {
        [HarmonyPrefix]
        public static void Prefix(Mineable __instance, DestroyMode mode)
        {
            if (mode != DestroyMode.KillFinalize) return;
            
            // Find the pawn who mined this
            Map map = __instance.Map;
            if (map == null) return;
            
            // Check for nearby pawns doing mining jobs
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Position.DistanceTo(__instance.Position) <= 2f)
                {
                    var job = pawn.CurJob;
                    if (job != null && job.def == JobDefOf.Mine)
                    {
                        var comp = IsekaiComponent.GetCached(pawn);
                        if (comp != null)
                        {
                            // Bonus XP for valuable ores
                            int xp = 10;
                            string oreName = __instance.def.defName;
                            if (oreName.Contains("Steel") || oreName.Contains("Plasteel") || oreName.Contains("Gold") ||
                                oreName.Contains("Silver") || oreName.Contains("Uranium") || oreName.Contains("Jade"))
                            {
                                xp = 18;
                            }
                            comp.GainXP(xp, "Mining");
                        }
                        break;
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// XP gain when a plant is destroyed (harvesting trees, cutting plants)
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    public static class Patch_PlantDestroyXP
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, DestroyMode mode)
        {
            // Only handle plants
            if (!(__instance is Plant plant)) return;
            if (mode != DestroyMode.KillFinalize) return;
            
            Map map = plant.Map;
            if (map == null) return;
            
            // Check for nearby pawns doing plant work
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.Position.DistanceTo(plant.Position) <= 2f)
                {
                    var job = pawn.CurJob;
                    if (job != null)
                    {
                        bool isPlantJob = job.def == JobDefOf.CutPlant || 
                                         job.def == JobDefOf.Harvest ||
                                         job.def == JobDefOf.HarvestDesignated ||
                                         job.def.defName.Contains("CutPlant") ||
                                         job.def.defName.Contains("Harvest");
                        
                        if (isPlantJob)
                        {
                            var comp = IsekaiComponent.GetCached(pawn);
                            if (comp != null)
                            {
                                // More XP for trees
                                int xp = 5;
                                string source = "Harvest";
                                
                                if (plant.def.plant != null && plant.def.plant.IsTree)
                                {
                                    xp = 12;
                                    source = "Woodcutting";
                                }
                                else if (plant.def.plant?.harvestedThingDef != null)
                                {
                                    xp = 8;
                                    source = "Harvest";
                                }
                                
                                comp.GainXP(xp, source);
                            }
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// XP gain from social interactions
    /// </summary>
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    public static class Patch_SocialXP
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_InteractionsTracker __instance, bool __result, Pawn recipient, Pawn ___pawn)
        {
            if (!__result) return;
            
            Pawn pawn = ___pawn;
            if (pawn == null) return;
            
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;
            
            // Small XP for successful social interaction
            comp.GainXP(3, null);
        }
    }

    /// <summary>
    /// XP gain from successful trade
    /// </summary>
    [HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    public static class Patch_TradeXP
    {
        [HarmonyPostfix]
        public static void Postfix(TradeDeal __instance, bool __result)
        {
            if (!__result) return;
            
            // Award XP to the negotiator
            Pawn negotiator = TradeSession.playerNegotiator;
            if (negotiator == null) return;
            
            var comp = IsekaiComponent.GetCached(negotiator);
            if (comp == null) return;
            
            comp.GainXP(30, "Trading");
        }
    }
    
    /// <summary>
    /// XP gain from crafting based on item quality AND INT-based quality boost
    /// Guarded: GenerateQualityCreatedByPawn signature may differ between RimWorld versions
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CraftQualityXP
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn",
                new Type[] { typeof(Pawn), typeof(SkillDef), typeof(bool) });
            if (method == null)
            {
                Log.Warning("[Isekai] QualityUtility.GenerateQualityCreatedByPawn(Pawn, SkillDef, bool) not found — craft quality patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(QualityUtility), "GenerateQualityCreatedByPawn",
                new Type[] { typeof(Pawn), typeof(SkillDef), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix(ref QualityCategory __result, Pawn pawn, SkillDef relevantSkill, bool consumeInspiration)
        {
            if (pawn == null) return;
            
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;
            
            // INT-based quality boost: chance to upgrade quality tier by 1
            QualityCategory originalQuality = __result;
            __result = TryBoostQuality(comp, __result);
            
            // Bonus XP based on final quality achieved
            int qualityXP = GetXPForQuality(__result);
            if (qualityXP > 0)
            {
                string qualityName = __result.GetLabel();
                bool wasUpgraded = __result > originalQuality;
                if (wasUpgraded)
                {
                    comp.GainXP(qualityXP, $"{qualityName} Craft (INT Boost!)");
                }
                else
                {
                    comp.GainXP(qualityXP, $"{qualityName} Craft");
                }
            }
        }
        
        /// <summary>
        /// Based on INT stat, roll chance to boost quality by 1 tier
        /// Each INT point above 5 gives +X% chance (configurable)
        /// E.g., at 0.5% per point and 50 INT: (50-5) * 0.5% = 22.5% chance to boost
        /// </summary>
        private static QualityCategory TryBoostQuality(IsekaiComponent comp, QualityCategory currentQuality)
        {
            try
            {
                if (comp?.stats == null) return currentQuality;
                if (currentQuality >= QualityCategory.Legendary) return currentQuality; // Already max
                
                float multiplierPerPoint = IsekaiLevelingSettings.INT_CraftingQuality;
                if (multiplierPerPoint <= 0f) return currentQuality; // Disabled
                
                float intValue = comp.stats.intelligence;
                // Calculate boost chance: (INT - 5) * multiplier
                // Base 5 gives 0%, each point above/below shifts the chance
                float boostChance = (intValue - 5f) * multiplierPerPoint;
                boostChance = Mathf.Clamp(boostChance, 0f, 0.95f); // Cap at 95%
                
                if (boostChance > 0f && Rand.Value < boostChance)
                {
                    // Upgrade quality by 1 tier
                    return currentQuality + 1;
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce($"[IsekaiLeveling] Error in TryBoostQuality: {ex.Message}", 999887766);
            }
            
            return currentQuality;
        }
        
        private static int GetXPForQuality(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful: return 5;
                case QualityCategory.Poor: return 8;
                case QualityCategory.Normal: return 12;
                case QualityCategory.Good: return 20;
                case QualityCategory.Excellent: return 35;
                case QualityCategory.Masterwork: return 60;
                case QualityCategory.Legendary: return 150;
                default: return 0;
            }
        }
    }
    
    /// <summary>
    /// XP gain from successful surgery - using CheckSurgeryFail instead since ApplyOnPawn signature changed
    /// Guarded: CheckSurgeryFail signature may differ between RimWorld versions
    /// </summary>
    [HarmonyPatch]
    public static class Patch_SurgeryXP
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(Recipe_Surgery), "CheckSurgeryFail");
            if (method == null)
            {
                Log.Warning("[Isekai] Recipe_Surgery.CheckSurgeryFail not found — surgery XP patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Recipe_Surgery), "CheckSurgeryFail");
        }

        [HarmonyPostfix]
        public static void Postfix(bool __result, Pawn surgeon, Pawn patient, Bill_Medical bill)
        {
            // Only award XP if surgery succeeded (CheckSurgeryFail returned false)
            if (__result) return;  // Surgery failed, no XP
            if (surgeon == null) return;
            
            var comp = IsekaiComponent.GetCached(surgeon);
            if (comp == null) return;
            
            // XP for completing surgery successfully
            int surgeryXP = 40;
            
            // Bonus for difficult surgeries
            if (bill?.recipe != null)
            {
                if (bill.recipe.defName.Contains("Install"))
                    surgeryXP = 60;
                if (bill.recipe.defName.Contains("Bionic") || bill.recipe.defName.Contains("Archotech"))
                    surgeryXP = 80;
            }
            
            comp.GainXP(surgeryXP, "Surgery");
        }
    }
    
    /// <summary>
    /// XP gain from arresting/capturing
    /// </summary>
    [HarmonyPatch(typeof(Pawn_GuestTracker), nameof(Pawn_GuestTracker.SetGuestStatus))]
    public static class Patch_CaptureXP
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_GuestTracker __instance, Faction newHost, GuestStatus guestStatus, Pawn ___pawn)
        {
            if (guestStatus != GuestStatus.Prisoner) return;
            
            // Find who captured this pawn (last interactor)
            Pawn capturedPawn = ___pawn;
            if (capturedPawn == null) return;
            
            // Award XP to all colonists nearby who may have helped
            if (capturedPawn.Map != null)
            {
                foreach (Pawn colonist in capturedPawn.Map.mapPawns.FreeColonists)
                {
                    if (colonist.Position.DistanceTo(capturedPawn.Position) < 10f)
                    {
                        var comp = IsekaiComponent.GetCached(colonist);
                        if (comp != null)
                        {
                            comp.GainXP(25, "Capture");
                        }
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// XP gain from successful recruitment
    /// </summary>
    [HarmonyPatch(typeof(InteractionWorker_RecruitAttempt), nameof(InteractionWorker_RecruitAttempt.Interacted))]
    public static class Patch_RecruitXP
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn initiator, Pawn recipient)
        {
            if (initiator == null) return;
            
            // Check if recruitment was successful (recipient joined player faction)
            if (recipient.Faction == Faction.OfPlayer && recipient.guest?.Recruitable == false)
            {
                var comp = IsekaiComponent.GetCached(initiator);
                if (comp != null)
                {
                    comp.GainXP(75, "Recruitment");
                }
            }
        }
    }
    
    /// <summary>
    /// XP gain from art creation
    /// </summary>
    [HarmonyPatch(typeof(CompArt), nameof(CompArt.InitializeArt), new Type[] { typeof(ArtGenerationContext) })]
    public static class Patch_ArtXP
    {
        [HarmonyPostfix]
        public static void Postfix(CompArt __instance, ArtGenerationContext source)
        {
            // Get author from the CompArt - RW 1.6 uses AuthorName but we need the pawn
            // For now, we can try to get the creator from thing's maker if available
            if (__instance.parent == null) return;
            
            // Try to find the pawn who made this through the bills/work system
            // In 1.6, art author info is stored differently - skip XP for now if no author
            // The art XP will be granted through crafting XP instead
        }
    }
    
    // ========================================
    // PSYCAST & ABILITY XP PATCHES
    // ========================================
    
    /// <summary>
    /// XP gain from using abilities (psycasts, VPE abilities, etc.)
    /// Hooks into Ability.Activate which fires when the ability actually activates
    /// (after the casting job completes), not when it's queued via QueueCastingJob.
    /// Uses the 2-param overload: Activate(LocalTargetInfo target, LocalTargetInfo dest)
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new System.Type[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })]
    public static class Patch_AbilityXP
    {
        // Rate-limiter: prevents toggled/maintained abilities (e.g. VPE Skipbarrier)
        // from awarding XP every tick. Key = pawnId ^ abilityDef hash.
        private static Dictionary<long, int> lastAbilityXPTick = new Dictionary<long, int>();
        private const int MIN_ABILITY_XP_INTERVAL = 2500; // ~42 seconds

        [HarmonyPostfix]
        public static void Postfix(Ability __instance, bool __result)
        {
            // Only award XP if ability actually activated successfully
            if (!__result) return;
            if (__instance?.pawn == null || __instance.def == null) return;
            
            var comp = IsekaiComponent.GetCached(__instance.pawn);
            if (comp == null) return;

            // Rate-limit: use ability cooldown if available, otherwise minimum interval
            int cooldown = (int)__instance.def.cooldownTicksRange.TrueMax;
            int interval = cooldown > MIN_ABILITY_XP_INTERVAL ? cooldown : MIN_ABILITY_XP_INTERVAL;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long key = ((long)__instance.pawn.thingIDNumber << 32) ^ __instance.def.shortHash;
            if (lastAbilityXPTick.TryGetValue(key, out int lastTick) && currentTick - lastTick < interval)
                return;
            lastAbilityXPTick[key] = currentTick;
            
            // Calculate XP based on ability properties
            int xpAmount = CalculateAbilityXP(__instance);
            
            if (xpAmount > 0)
            {
                string abilityName = __instance.def?.label ?? "Ability";
                comp.GainXP(xpAmount, $"Cast: {abilityName}");
            }
        }

        /// <summary>
        /// Clean up per-pawn ability XP tracking.
        /// Called from IsekaiComponent.PostDeSpawn.
        /// </summary>
        public static void CleanupPawn(int pawnId)
        {
            long prefix = (long)pawnId << 32;
            var toRemove = lastAbilityXPTick.Keys.Where(k => (k >> 32) == pawnId).ToList();
            foreach (var k in toRemove)
                lastAbilityXPTick.Remove(k);
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            lastAbilityXPTick.Clear();
        }
        
        public static int CalculateAbilityXP(Ability ability)
        {
            if (ability?.def == null) return 5;
            
            int baseXP = 10;
            
            // Check entropy/psyfocus cost for psycasts
            var abComp = ability.def.comps;
            if (abComp != null)
            {
                foreach (var compProps in abComp)
                {
                    // Check for entropy cost (higher entropy = more XP)
                    if (compProps is CompProperties_AbilityGiveHediff)
                    {
                        baseXP += 5; // Buffs/debuffs get bonus XP
                    }
                }
            }
            
            // Scale by cooldown (longer cooldown = more powerful = more XP)
            if (ability.def.cooldownTicksRange.TrueMax > 0)
            {
                float cooldownDays = ability.def.cooldownTicksRange.TrueMax / 60000f;
                baseXP += Mathf.RoundToInt(cooldownDays * 20f); // +20 XP per day of cooldown
            }
            
            // Check for psychic entropy (vanilla psycasts)
            if (ability.def.statBases != null)
            {
                foreach (var stat in ability.def.statBases)
                {
                    if (stat.stat?.defName?.Contains("Entropy") == true || 
                        stat.stat?.defName?.Contains("Psyfocus") == true)
                    {
                        baseXP += Mathf.RoundToInt(stat.value * 2f);
                    }
                }
            }
            
            // Cap at reasonable values
            return Mathf.Clamp(baseXP, 5, 100);
        }
    }
    
    /// <summary>
    /// XP gain from using world-map abilities (GlobalTargetInfo overload).
    /// Catches abilities that target world tiles/caravans rather than local targets.
    /// </summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.Activate), new System.Type[] { typeof(GlobalTargetInfo) })]
    public static class Patch_AbilityXP_World
    {
        private static Dictionary<long, int> lastWorldAbilityXPTick = new Dictionary<long, int>();

        [HarmonyPostfix]
        public static void Postfix(Ability __instance, bool __result)
        {
            if (!__result) return;
            if (__instance?.pawn == null || __instance.def == null) return;
            
            var comp = IsekaiComponent.GetCached(__instance.pawn);
            if (comp == null) return;

            // Rate-limit: prevents toggled/maintained abilities from granting XP every tick
            int cooldown = (int)__instance.def.cooldownTicksRange.TrueMax;
            int interval = cooldown > 2500 ? cooldown : 2500;
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            long key = ((long)__instance.pawn.thingIDNumber << 32) ^ __instance.def.shortHash;
            if (lastWorldAbilityXPTick.TryGetValue(key, out int lastTick) && currentTick - lastTick < interval)
                return;
            lastWorldAbilityXPTick[key] = currentTick;
            
            int xpAmount = Patch_AbilityXP.CalculateAbilityXP(__instance);
            
            if (xpAmount > 0)
            {
                string abilityName = __instance.def?.label ?? "Ability";
                comp.GainXP(xpAmount, $"Cast: {abilityName}");
            }
        }

        public static void CleanupPawn(int pawnId)
        {
            var toRemove = lastWorldAbilityXPTick.Keys.Where(k => (k >> 32) == pawnId).ToList();
            foreach (var k in toRemove)
                lastWorldAbilityXPTick.Remove(k);
        }

        public static void ClearAll()
        {
            lastWorldAbilityXPTick.Clear();
        }
    }
    
    /// <summary>
    /// XP gain from gaining psychic entropy (using psycasts)
    /// Works with both vanilla and Vanilla Psycasts Expanded
    /// RimWorld 1.6 signature: TryAddEntropy(float value, Thing source, bool scale, bool overLimit)
    /// CONDITIONAL: Only patches if Royalty DLC is loaded
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PsychicEntropyXP
    {
        static bool Prepare()
        {
            var type = AccessTools.TypeByName("RimWorld.Pawn_PsychicEntropyTracker");
            if (type == null) return false;
            var method = AccessTools.Method(type, "TryAddEntropy");
            if (method == null)
            {
                Log.Warning("[Isekai] PsychicEntropyTracker type found but TryAddEntropy method not found — skipping patch");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.Pawn_PsychicEntropyTracker");
            return AccessTools.Method(type, "TryAddEntropy");
        }
        
        // Rate-limiter: prevents toggled/maintained abilities that add tiny entropy
        // each tick from awarding XP every tick.
        private static Dictionary<int, int> lastEntropyXPTick = new Dictionary<int, int>();
        private static Dictionary<int, float> accumulatedEntropy = new Dictionary<int, float>();
        private const int ENTROPY_XP_INTERVAL = 2500; // ~42 seconds

        [HarmonyPostfix]
        public static void Postfix(object __instance, bool __result, float value, Pawn ___pawn)
        {
            // Only award XP if entropy was successfully added
            if (!__result || value <= 0) return;
            if (___pawn == null) return;
            
            var comp = IsekaiComponent.GetCached(___pawn);
            if (comp == null) return;

            int pawnId = ___pawn.thingIDNumber;
            int currentTick = Find.TickManager?.TicksGame ?? 0;

            // Accumulate entropy between XP awards
            if (!accumulatedEntropy.ContainsKey(pawnId))
                accumulatedEntropy[pawnId] = 0f;
            accumulatedEntropy[pawnId] += value;

            // Rate-limit: only award XP once per interval
            if (lastEntropyXPTick.TryGetValue(pawnId, out int lastTick) && currentTick - lastTick < ENTROPY_XP_INTERVAL)
                return;
            lastEntropyXPTick[pawnId] = currentTick;

            // Scale XP by total accumulated entropy since last award
            float totalEntropy = accumulatedEntropy[pawnId];
            accumulatedEntropy[pawnId] = 0f;
            int xpAmount = Mathf.RoundToInt(totalEntropy * 0.5f);
            xpAmount = Mathf.Clamp(xpAmount, 3, 50);
            
            comp.GainXP(xpAmount, "Psycast");
        }

        /// <summary>
        /// Clean up per-pawn entropy XP tracking.
        /// Called from IsekaiComponent.PostDeSpawn.
        /// </summary>
        public static void CleanupPawn(int pawnId)
        {
            lastEntropyXPTick.Remove(pawnId);
            accumulatedEntropy.Remove(pawnId);
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            lastEntropyXPTick.Clear();
            accumulatedEntropy.Clear();
        }
    }
    
    /// <summary>
    /// XP gain from meditation (vanilla and VPE)
    /// Tracks meditation time and awards XP periodically
    /// CONDITIONAL: Only patches if Royalty DLC is loaded
    /// </summary>
    [HarmonyPatch]
    public static class Patch_MeditationXP
    {
        static bool Prepare()
        {
            var type = AccessTools.TypeByName("RimWorld.Pawn_PsychicEntropyTracker");
            if (type == null) return false;
            var method = AccessTools.Method(type, "GainPsyfocus");
            if (method == null)
            {
                Log.Warning("[Isekai] PsychicEntropyTracker type found but GainPsyfocus method not found — skipping patch");
                return false;
            }
            // Skip if the target is marked [Obsolete] — patching obsolete methods produces
            // HugsLib warnings and the method may be removed in future versions.
            if (System.Attribute.IsDefined(method, typeof(System.ObsoleteAttribute)))
                return false;
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.Pawn_PsychicEntropyTracker");
            if (type == null) return null;
            var method = AccessTools.Method(type, "GainPsyfocus");
            if (method == null || System.Attribute.IsDefined(method, typeof(System.ObsoleteAttribute)))
                return null;
            return method;
        }
        
        private static Dictionary<int, int> lastMeditationXPTick = new Dictionary<int, int>();
        private const int XP_INTERVAL_TICKS = 2500; // Award XP every ~42 seconds of meditation
        
        [HarmonyPostfix]
        public static void Postfix(object __instance, Pawn ___pawn)
        {
            if (___pawn == null) return;
            
            var comp = IsekaiComponent.GetCached(___pawn);
            if (comp == null) return;
            
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = ___pawn.thingIDNumber;
            
            if (!lastMeditationXPTick.TryGetValue(pawnId, out int lastTick) || 
                currentTick - lastTick >= XP_INTERVAL_TICKS)
            {
                comp.GainXP(8, "Meditation");
                lastMeditationXPTick[pawnId] = currentTick;
                
                // Cleanup old entries
                if (lastMeditationXPTick.Count > 50)
                {
                    var toRemove = lastMeditationXPTick
                        .Where(kvp => currentTick - kvp.Value > 60000)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    foreach (var key in toRemove)
                    {
                        lastMeditationXPTick.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Clean up per-pawn meditation XP tracking.
        /// Called from IsekaiComponent.PostDeSpawn.
        /// </summary>
        public static void CleanupPawn(int pawnId)
        {
            lastMeditationXPTick.Remove(pawnId);
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            lastMeditationXPTick.Clear();
        }
    }
    
    /// <summary>
    /// XP gain when psyfocus is spent (VPE compatibility)
    /// CONDITIONAL: Only patches if Royalty DLC is loaded
    /// </summary>
    [HarmonyPatch]
    public static class Patch_PsyfocusSpentXP
    {
        static bool Prepare()
        {
            var type = AccessTools.TypeByName("RimWorld.Pawn_PsychicEntropyTracker");
            if (type == null) return false;
            var method = AccessTools.Method(type, "OffsetPsyfocusDirectly");
            if (method == null)
            {
                Log.Warning("[Isekai] PsychicEntropyTracker type found but OffsetPsyfocusDirectly method not found — skipping patch");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.Pawn_PsychicEntropyTracker");
            return AccessTools.Method(type, "OffsetPsyfocusDirectly");
        }
        
        [HarmonyPostfix]
        public static void Postfix(object __instance, float offset, Pawn ___pawn)
        {
            // Only award XP when psyfocus is spent (negative offset)
            if (offset >= 0) return;
            if (___pawn == null) return;
            
            var comp = IsekaiComponent.GetCached(___pawn);
            if (comp == null) return;
            
            // Scale XP by psyfocus spent
            float psyfocusSpent = -offset; // Convert to positive
            int xpAmount = Mathf.RoundToInt(psyfocusSpent * 30f); // ~30 XP per 1.0 psyfocus spent
            xpAmount = Mathf.Clamp(xpAmount, 2, 40);
            
            comp.GainXP(xpAmount, "Psyfocus");
        }
    }

    /// <summary>
    /// Counter Strike (Duelist gimmick) — detect when a melee attack MISSES the Duelist.
    /// TryCastShot returns false when the melee attack misses (target dodged).
    /// When the target is a Duelist with Counter Strike active, each dodge stores a counter charge.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CounterStrikeDodge
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(Verb_MeleeAttackDamage), "TryCastShot");
            if (method == null)
            {
                Log.Warning("[Isekai] Verb_MeleeAttackDamage.TryCastShot not found — counter strike patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Verb_MeleeAttackDamage), "TryCastShot");
        }

        [HarmonyPostfix]
        public static void Postfix(Verb_MeleeAttackDamage __instance, bool __result)
        {
            // Only trigger on miss (dodge) — __result == true means the attack landed
            if (__result) return;

            Pawn attacker = __instance.CasterPawn;
            if (attacker == null || attacker.Dead || attacker.Downed) return;

            // Get the target — must be a living pawn
            if (!(__instance.CurrentTarget.Thing is Pawn target)) return;
            if (target.Dead || target.Downed) return;

            var comp = IsekaiComponent.GetCached(target);
            if (comp?.passiveTree?.HasGimmick(ClassGimmickType.CounterStrike) == true)
            {
                comp.passiveTree.AccumulateCounterCharge();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ▓▓  EUREKA SYNTHESIS PATCH  (Alchemist — tend completion)  ▓▓
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// When a pawn successfully tends a patient, accumulate Eureka insight
    /// if the doctor has the EurekaSynthesis gimmick active.
    /// Uses manual method resolution to avoid crashes if the signature differs.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_EurekaSynthesisTend
    {
        static bool Prepare()
        {
            var type = AccessTools.TypeByName("RimWorld.TendUtility");
            if (type == null) return false;
            var method = AccessTools.Method(type, "DoTend");
            if (method == null)
            {
                Log.Warning("[Isekai] TendUtility type found but DoTend method not found — skipping patch");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("RimWorld.TendUtility");
            return AccessTools.Method(type, "DoTend");
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn doctor, Pawn patient)
        {
            if (doctor == null || patient == null) return;
            if (doctor.Dead || doctor.Downed) return;

            var comp = IsekaiComponent.GetCached(doctor);
            if (comp?.passiveTree?.HasGimmick(ClassGimmickType.EurekaSynthesis) == true)
            {
                comp.passiveTree.AccumulateEurekaInsight();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ▓▓  ANIMAL EGG-LAYING XP PATCH                              ▓▓
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Give XP to player-faction animals when they lay an egg.
    /// Detects egg laying by checking if eggProgress was >= 1 before CompTick and reset after.
    /// </summary>
    [HarmonyPatch(typeof(CompEggLayer), nameof(CompEggLayer.CompTick))]
    public static class Patch_EggLayXP
    {
        [HarmonyPrefix]
        public static void Prefix(CompEggLayer __instance, out float __state)
        {
            // Record current egg progress before tick
            __state = Traverse.Create(__instance).Field("eggProgress").GetValue<float>();
        }

        [HarmonyPostfix]
        public static void Postfix(CompEggLayer __instance, float __state)
        {
            // If eggProgress was >= 1 and now reset, an egg was laid
            if (__state < 1f) return;
            float current = Traverse.Create(__instance).Field("eggProgress").GetValue<float>();
            if (current >= 1f) return; // Not reset, egg wasn't actually laid

            Pawn animal = __instance.parent as Pawn;
            if (animal == null || animal.Dead) return;
            if (animal.Faction == null || !animal.Faction.IsPlayer) return;

            var rankComp = animal.TryGetComp<MobRankComponent>();
            if (rankComp != null)
            {
                rankComp.GainXP(5, "EggLaying");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // ▓▓  SKILL LEARNING XP CATCH-ALL                             ▓▓
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Catch-all XP source: whenever vanilla SkillRecord.Learn fires, award
    /// a small amount of Isekai XP. This covers direct-control mods like
    /// Perspective Shift that bypass the job system but still trigger vanilla
    /// skill learning for combat, mining, construction, etc.
    /// Rate-limited per pawn (once per 120 ticks ≈ 2 seconds) to avoid spam.
    /// </summary>
    [HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
    public static class Patch_SkillLearnXP
    {
        private static Dictionary<int, int> lastAwardTick = new Dictionary<int, int>();
        private const int COOLDOWN_TICKS = 120; // ~2 seconds between awards per pawn
        private static readonly System.Reflection.FieldInfo pawnField = AccessTools.Field(typeof(SkillRecord), "pawn");

        [HarmonyPostfix]
        public static void Postfix(SkillRecord __instance, float xp, bool direct)
        {
            if (xp <= 0f) return;     // Only positive XP gains
            if (direct) return;       // Skip direct assignments (scenarios, dev mode)

            Pawn pawn = pawnField?.GetValue(__instance) as Pawn;
            if (pawn == null) return;

            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;

            // Per-pawn cooldown to avoid flooding from rapid skill ticks
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            int pawnId = pawn.thingIDNumber;
            if (lastAwardTick.TryGetValue(pawnId, out int last) && currentTick - last < COOLDOWN_TICKS)
                return;
            lastAwardTick[pawnId] = currentTick;

            // Convert vanilla skill XP to Isekai XP — small baseline
            int isekaiXP = Mathf.Max(1, (int)(xp * 0.15f));
            comp.GainXP(isekaiXP, "Skill");
        }

        /// <summary>Clear state on game load.</summary>
        public static void ClearAll()
        {
            lastAwardTick.Clear();
        }
    }
}
