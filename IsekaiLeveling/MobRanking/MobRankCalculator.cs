using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Calculates threat scores for mobs based on their stats and abilities
    /// </summary>
    public static class MobRankCalculator
    {
        /// <summary>
        /// Calculate the overall threat score for a pawn
        /// Higher score = more dangerous = higher rank
        /// </summary>
        public static float CalculateThreatScore(Pawn pawn)
        {
            if (pawn?.RaceProps == null) return 0f;

            float score = 0f;

            // Base health contribution
            score += CalculateHealthScore(pawn);

            // Combat ability contribution
            score += CalculateCombatScore(pawn);

            // Movement and size contribution
            score += CalculateMobilityScore(pawn);

            // Special abilities and traits
            score += CalculateSpecialScore(pawn);

            // Anomaly bonus (if from Anomaly DLC)
            score += CalculateAnomalyBonus(pawn);

            // --- Modded creature safety nets ---
            // Many mods define combatPower but don't set body/health proportionally.
            // combatPower is what RimWorld uses for raid point calculations, making
            // it the most reliable mod-friendly metric for creature strength.
            float combatPower = pawn.kindDef?.combatPower ?? 0f;
            if (combatPower > 0f)
            {
                // Scale: chicken ~15CP→F, wolf ~50CP→D, thrumbo ~500CP→A-S
                float combatPowerFloor = combatPower * 2.5f;
                score = Mathf.Max(score, combatPowerFloor);
            }

            // Market value fallback - for creatures with no/low combatPower
            // but high market value (which indicates rarity/power).
            // Read from def.statBases to avoid stat calculation recursion.
            float marketValue = GetBaseStatValue(pawn.def, StatDefOf.MarketValue);
            if (marketValue > 500f)
            {
                float marketFloor = marketValue * 0.3f;
                score = Mathf.Max(score, marketFloor);
            }

            return Mathf.Max(1f, score);
        }

        /// <summary>
        /// Safely read a stat's base value from a ThingDef's statBases list.
        /// Returns 0 if not found. Does NOT trigger pawn stat calculations.
        /// </summary>
        private static float GetBaseStatValue(ThingDef def, StatDef stat)
        {
            if (def?.statBases == null) return 0f;
            foreach (var mod in def.statBases)
            {
                if (mod.stat == stat) return mod.value;
            }
            return 0f;
        }

        /// <summary>
        /// Health-based threat contribution
        /// Reduced multipliers to get more sensible base scores
        /// </summary>
        private static float CalculateHealthScore(Pawn pawn)
        {
            float baseHealth = pawn.RaceProps.baseHealthScale;
            float bodySize = pawn.RaceProps.baseBodySize;

            // Much lower multiplier - a goose (bodySize ~0.3, health ~0.8) should score ~5-10
            // A thrumbo (bodySize ~4, health ~3) should score ~200+
            return baseHealth * bodySize * 15f;
        }

        /// <summary>
        /// Combat ability contribution (melee + ranged potential)
        /// Reduced multipliers for more sensible scoring
        /// </summary>
        private static float CalculateCombatScore(Pawn pawn)
        {
            float score = 0f;

            // Melee DPS from race - reduced multiplier
            float meleeDps = 0f;
            if (pawn.RaceProps.Humanlike)
            {
                // For humanlike, estimate based on body size
                meleeDps = pawn.RaceProps.baseBodySize * 3f;
            }
            else
            {
                // For animals, calculate from their verbs/attacks
                meleeDps = CalculateMeleeDPS(pawn);
            }
            score += meleeDps * 3f; // Reduced from 10f

            // Armor contribution - use race defaults instead of stats to avoid recursion
            // During rank calculation we can't access pawn stats safely
            float armorBlunt = 0f;
            float armorSharp = 0f;
            
            // Use def's base stats if available, otherwise skip
            if (pawn.def.statBases != null)
            {
                foreach (var statMod in pawn.def.statBases)
                {
                    if (statMod.stat == StatDefOf.ArmorRating_Blunt)
                        armorBlunt = statMod.value;
                    else if (statMod.stat == StatDefOf.ArmorRating_Sharp)
                        armorSharp = statMod.value;
                }
            }

            score += (armorBlunt + armorSharp) * 20f; // Reduced from 50f

            // Predator bonus
            if (pawn.RaceProps.predator)
            {
                score *= 1.5f; // Predators are significantly more dangerous
            }

            // Pack animal penalty (usually less aggressive)
            if (pawn.RaceProps.packAnimal)
            {
                score *= 0.7f;
            }

            return score;
        }

        /// <summary>
        /// Calculate melee DPS from pawn's natural attacks.
        /// Uses VerbTracker first, falls back to ThingDef.tools if verbs aren't
        /// initialized (common for modded creatures during PostSpawnSetup).
        /// </summary>
        private static float CalculateMeleeDPS(Pawn pawn)
        {
            float totalDps = 0f;

            // First try: VerbTracker (accurate, uses adjusted values)
            try
            {
                var verbs = pawn.VerbTracker?.AllVerbs;
                if (verbs != null)
                {
                    foreach (var verb in verbs)
                    {
                        if (verb.IsMeleeAttack && verb.verbProps != null)
                        {
                            float damage = verb.verbProps.AdjustedMeleeDamageAmount(verb, pawn);
                            float cooldown = verb.verbProps.AdjustedCooldown(verb, pawn);
                            if (cooldown > 0)
                            {
                                totalDps += damage / cooldown;
                            }
                        }
                    }
                }
            }
            catch { /* VerbTracker may not be ready */ }

            // Second try: ThingDef.tools (always available, doesn't need pawn init)
            // This catches modded creatures whose VerbTracker isn't set up yet
            if (totalDps <= 0f)
            {
                totalDps = CalculateToolDPS(pawn.def);
            }

            return Mathf.Max(totalDps, pawn.RaceProps.baseBodySize * 1.5f);
        }

        /// <summary>
        /// Calculate melee DPS directly from a ThingDef's tools list.
        /// This is always available and doesn't depend on pawn initialization.
        /// </summary>
        private static float CalculateToolDPS(ThingDef def)
        {
            if (def?.tools == null || def.tools.Count == 0) return 0f;

            float totalDps = 0f;
            foreach (var tool in def.tools)
            {
                float damage = tool.power;
                float cooldown = tool.cooldownTime > 0f ? tool.cooldownTime : 2f;
                totalDps += damage / cooldown;
            }
            return totalDps;
        }

        /// <summary>
        /// Mobility and size contribution - reduced for better scaling
        /// </summary>
        private static float CalculateMobilityScore(Pawn pawn)
        {
            float score = 0f;

            // Movement speed - use race base instead of stat to avoid recursion
            // pawn.GetStatValue can trigger consciousness calculations which cause infinite loops
            float moveSpeed = 4.6f; // Default human move speed
            if (pawn.def.statBases != null)
            {
                foreach (var statMod in pawn.def.statBases)
                {
                    if (statMod.stat == StatDefOf.MoveSpeed)
                    {
                        moveSpeed = statMod.value;
                        break;
                    }
                }
            }
            else
            {
                // Fallback estimate based on body size
                moveSpeed = pawn.RaceProps.baseBodySize > 1f ? 3f : 4f;
            }

            score += moveSpeed * 2f; // Reduced from 5f

            // Body size multiplier - reduced bonuses
            float bodySize = pawn.RaceProps.baseBodySize;
            if (bodySize >= 4f) score += 150f;       // Thrumbo-sized
            else if (bodySize >= 3f) score += 80f;   // Massive creatures
            else if (bodySize >= 2f) score += 40f;   // Large creatures (elephants)
            else if (bodySize >= 1.5f) score += 20f; // Medium-large (bears)
            else if (bodySize >= 1f) score += 10f;   // Human-sized
            else if (bodySize >= 0.5f) score += 3f;  // Small (dogs, foxes)
            else score += 1f;                         // Tiny creatures

            return score;
        }

        /// <summary>
        /// Special abilities and traits
        /// </summary>
        private static float CalculateSpecialScore(Pawn pawn)
        {
            float score = 0f;

            // Manhunter tendency
            if (pawn.RaceProps.manhunterOnTameFailChance > 0)
            {
                score += pawn.RaceProps.manhunterOnTameFailChance * 50f;
            }

            if (pawn.RaceProps.manhunterOnDamageChance > 0)
            {
                score += pawn.RaceProps.manhunterOnDamageChance * 30f;
            }

            // Venomous/toxic attacks
            if (pawn.RaceProps.Insect)
            {
                score += 30f; // Insects are dangerous in groups
            }

            // Non-trainable animals are usually wilder/more dangerous
            if (!pawn.RaceProps.trainability?.defName?.Contains("Advanced") ?? true)
            {
                score += 15f;
            }

            // Combat power from race - additive contribution for granularity within ranks.
            // The floor in CalculateThreatScore handles major outliers.
            float combatPower = pawn.kindDef?.combatPower ?? 0f;
            score += combatPower * 0.5f;

            return score;
        }

        /// <summary>
        /// Bonus for Anomaly DLC creatures
        /// </summary>
        private static float CalculateAnomalyBonus(Pawn pawn)
        {
            float bonus = 0f;

            try
            {
                // Check if this is an anomaly-related entity
                // These are typically much more dangerous
                string defName = pawn.def.defName.ToLower();
                
                // Known dangerous anomaly creatures get significant bonuses
                if (defName.Contains("sightsteal") || defName.Contains("revenant"))
                    bonus += 800f;
                else if (defName.Contains("shambler") || defName.Contains("gorehulk"))
                    bonus += 400f;
                else if (defName.Contains("fleshmass") || defName.Contains("chimera"))
                    bonus += 300f;
                else if (defName.Contains("entity") || defName.Contains("horror"))
                    bonus += 200f;
                else if (defName.Contains("void") || defName.Contains("dark"))
                    bonus += 150f;

                // Check for "Anomaly" in the mod content source or tags
                if (pawn.def.modContentPack?.Name?.Contains("Anomaly") == true)
                {
                    bonus += 100f;
                }
            }
            catch { /* Ignore errors in anomaly detection */ }

            return bonus;
        }

        /// <summary>
        /// Get a cached or calculated threat score for a pawn def
        /// Useful for pre-calculating ranks at game load
        /// </summary>
        public static float EstimateThreatScoreFromDef(ThingDef def)
        {
            if (def?.race == null) return 0f;

            float score = 0f;
            var race = def.race;

            // Base health
            score += race.baseHealthScale * 100f * race.baseBodySize * 0.5f;

            // Body size
            if (race.baseBodySize >= 3f) score += 200f;
            else if (race.baseBodySize >= 2f) score += 100f;
            else if (race.baseBodySize >= 1f) score += 40f;
            else score += 10f;

            // Predator bonus
            if (race.predator) score *= 1.4f;

            // Insect
            if (race.Insect) score += 30f;

            // Manhunter
            score += race.manhunterOnDamageChance * 40f;
            score += race.manhunterOnTameFailChance * 60f;

            // Tool-based DPS from def (melee attacks)
            float toolDps = CalculateToolDPS(def);
            score += toolDps * 3f;

            // Combat power floor from any PawnKindDef that uses this race.
            // Many modded creatures define their power via combatPower on the kindDef.
            float maxCombatPower = 0f;
            try
            {
                foreach (var kindDef in DefDatabase<PawnKindDef>.AllDefsListForReading)
                {
                    if (kindDef.race == def && kindDef.combatPower > maxCombatPower)
                    {
                        maxCombatPower = kindDef.combatPower;
                    }
                }
            }
            catch { /* DefDatabase might not be ready */ }

            if (maxCombatPower > 0f)
            {
                score = Mathf.Max(score, maxCombatPower * 2.5f);
            }

            // Market value floor
            float marketValue = GetBaseStatValue(def, StatDefOf.MarketValue);
            if (marketValue > 500f)
            {
                score = Mathf.Max(score, marketValue * 0.3f);
            }

            return Mathf.Max(1f, score);
        }
    }
}
