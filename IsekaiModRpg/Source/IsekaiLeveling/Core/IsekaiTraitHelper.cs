using System.Collections.Generic;
using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Centralized helper for all Isekai trait effects.
    /// Every trait mechanic routes through here so hooks in GainXP, LevelUp,
    /// OnLevelUp (star points) and stat parts can query a single source of truth.
    /// </summary>
    public static class IsekaiTraitHelper
    {
        // ── Trait DefName constants ──
        // Archetype
        public const string Protagonist     = "Isekai_Protagonist";
        public const string Antagonist      = "Isekai_Antagonist";
        public const string Reincarnator    = "Isekai_Reincarnated";
        public const string Regressor       = "Isekai_Regressor";
        public const string SummonedHero    = "Isekai_SummonedHero";

        // Growth
        public const string NaturalTalent      = "Isekai_NaturalTalent";
        public const string Prodigy            = "Isekai_Prodigy";
        public const string LateBloomer        = "Isekai_LateBloomer";
        public const string QuickLearner       = "Isekai_QuickLearner";
        public const string SlowGrind          = "Isekai_SlowGrind";
        public const string PowerSpike         = "Isekai_PowerSpike";
        public const string AwakenedPotential  = "Isekai_AwakenedPotential";
        public const string Genius             = "Isekai_Genius";
        public const string BattleManiac       = "Isekai_BattleManiac";

        // Combat
        public const string BerserkerBlood    = "Isekai_BerserkerBlood";
        public const string IronWill          = "Isekai_IronWill";
        public const string GlassCannon       = "Isekai_GlassCannon";
        public const string Fortress          = "Isekai_Fortress";
        public const string ShadowStep        = "Isekai_ShadowStep";
        public const string PredatorInstinct  = "Isekai_PredatorInstinct";
        public const string Undying           = "Isekai_Undying";

        // Stat Affinity
        public const string Mighty       = "Isekai_Mighty";
        public const string Agile        = "Isekai_Agile";
        public const string Resilient    = "Isekai_Resilient";
        public const string Brilliant    = "Isekai_Brilliant";
        public const string Enlightened  = "Isekai_Enlightened";
        public const string SilverTongue = "Isekai_SilverTongue";

        // Utility
        public const string MerchantEye    = "Isekai_MerchantEye";
        public const string CraftsmanSoul  = "Isekai_CraftsmanSoul";
        public const string BeastWhisperer = "Isekai_BeastWhisperer";
        public const string HealerTouch    = "Isekai_HealerTouch";
        public const string Lucky          = "Isekai_Lucky";
        public const string CursedLuck     = "Isekai_CursedLuck";

        // Negative / Curse
        // SystemGlitch removed in v1.1.5 — const kept only for save migration reference.
        public const string SystemGlitch   = "Isekai_SystemGlitch";
        public const string HollowCore     = "Isekai_HollowCore";
        public const string FragileVessel  = "Isekai_FragileVessel";
        public const string EchoOfDefeat   = "Isekai_EchoOfDefeat";
        public const string SealedPower    = "Isekai_SealedPower";

        // All isekai trait defNames for iteration / cleanup
        private static readonly HashSet<string> AllIsekaiTraitDefNames = new HashSet<string>
        {
            Protagonist, Antagonist, Reincarnator, Regressor, SummonedHero,
            NaturalTalent, Prodigy, LateBloomer, QuickLearner, SlowGrind,
            PowerSpike, AwakenedPotential, Genius, BattleManiac,
            BerserkerBlood, IronWill, GlassCannon, Fortress, ShadowStep,
            PredatorInstinct, Undying,
            Mighty, Agile, Resilient, Brilliant, Enlightened, SilverTongue,
            MerchantEye, CraftsmanSoul, BeastWhisperer, HealerTouch, Lucky, CursedLuck,
            HollowCore, FragileVessel, EchoOfDefeat, SealedPower
        };

        // ── Quick trait check ──

        /// <summary>Check if a pawn has a specific Isekai trait by defName.</summary>
        public static bool HasTrait(Pawn pawn, string traitDefName)
        {
            if (pawn?.story?.traits == null) return false;
            var allTraits = pawn.story.traits.allTraits;
            for (int i = 0; i < allTraits.Count; i++)
            {
                if (allTraits[i].def.defName == traitDefName)
                    return true;
            }
            return false;
        }

        /// <summary>Check if a pawn is an Isekai trait.</summary>
        public static bool IsIsekaiTrait(string defName)
        {
            return AllIsekaiTraitDefNames.Contains(defName);
        }

        /// <summary>Get all Isekai traits a pawn has.</summary>
        public static List<string> GetIsekaiTraits(Pawn pawn)
        {
            var result = new List<string>();
            if (pawn?.story?.traits == null) return result;
            var allTraits = pawn.story.traits.allTraits;
            for (int i = 0; i < allTraits.Count; i++)
            {
                if (AllIsekaiTraitDefNames.Contains(allTraits[i].def.defName))
                    result.Add(allTraits[i].def.defName);
            }
            return result;
        }

        // ═══════════════════════════════════════════
        //  XP MULTIPLIER — called from GainXP()
        // ═══════════════════════════════════════════

        /// <summary>
        /// Get the total XP multiplier from all Isekai traits on a pawn.
        /// Multiplicative stacking. Returns 1.0 if no trait modifiers.
        /// </summary>
        public static float GetXPMultiplier(Pawn pawn, int currentLevel, string xpSource = null)
        {
            if (pawn?.story?.traits == null) return 1f;

            float mult = 1f;

            // Archetype traits (only one can be active due to conflicts)
            if (HasTrait(pawn, Protagonist))     mult *= 10f;
            else if (HasTrait(pawn, Antagonist))  mult *= 8f;
            else if (HasTrait(pawn, Reincarnator)) mult *= 5f;
            else if (HasTrait(pawn, Regressor))   mult *= 4f;
            else if (HasTrait(pawn, SummonedHero)) mult *= 3f;

            // Growth traits
            if (HasTrait(pawn, Prodigy))       mult *= 2f;
            if (HasTrait(pawn, QuickLearner))   mult *= 1.5f;
            if (HasTrait(pawn, SlowGrind))      mult *= 0.5f;
            if (HasTrait(pawn, CursedLuck))     mult *= 2f;
            if (HasTrait(pawn, EchoOfDefeat))   mult *= 1.1f; // permanent 10%

            // EchoOfDefeat temporary x2 XP buff after being downed (3 days)
            if (HasTrait(pawn, EchoOfDefeat))
            {
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp != null && comp.echoOfDefeatActiveUntilTick > Find.TickManager.TicksGame)
                    mult *= 2f;
            }

            // Late Bloomer: scaling by level
            if (HasTrait(pawn, LateBloomer))
            {
                if (currentLevel >= 101)     mult *= 3.0f;
                else if (currentLevel >= 51) mult *= 2.0f;
                else if (currentLevel >= 26) mult *= 1.5f;
                else if (currentLevel >= 11) mult *= 1.0f;
                else                         mult *= 0.5f;
            }

            // Battle Maniac: different multiplier based on XP source
            if (HasTrait(pawn, BattleManiac))
            {
                bool isCombat = xpSource == "Combat" || xpSource == "Kill";
                if (isCombat)
                    mult *= 3f;
                else
                    mult *= 0.1f;
            }

            // Curse traits
            if (HasTrait(pawn, HollowCore))     mult *= 3f;
            if (HasTrait(pawn, FragileVessel))  mult *= 2.5f;

            return mult;
        }

        // ═══════════════════════════════════════════
        //  BONUS STAT POINTS — called from LevelUp()
        // ═══════════════════════════════════════════

        /// <summary>
        /// Get bonus stat points granted on level up from traits.
        /// Returns the number of EXTRA points beyond the normal grant.
        /// </summary>
        public static int GetBonusStatPoints(Pawn pawn, int newLevel)
        {
            if (pawn?.story?.traits == null) return 0;

            int bonus = 0;

            // Protagonist: +2 per level
            if (HasTrait(pawn, Protagonist)) bonus += 2;

            // Summoned Hero: +1 per level
            if (HasTrait(pawn, SummonedHero)) bonus += 1;

            // Slow Grind: +2 per level
            if (HasTrait(pawn, SlowGrind)) bonus += 2;

            // Power Spike: +5 every 10 levels
            if (HasTrait(pawn, PowerSpike) && newLevel % 10 == 0) bonus += 5;

            // Sealed Power: after level 25, bonus stat points (capped at base rate to stay balanced at low pts/lvl settings)
            if (HasTrait(pawn, SealedPower) && newLevel > 25)
                bonus += System.Math.Min(2, IsekaiLevelingSettings.skillPointsPerLevel);

            return bonus;
        }

        // ═══════════════════════════════════════════
        //  STAR POINTS — called from PassiveTreeTracker.OnLevelUp()
        // ═══════════════════════════════════════════

        /// <summary>
        /// Get the total star points to award for this level (replaces default logic).
        /// Default is 1 point every 5 levels. Traits modify this.
        /// Returns -1 to indicate "no constellation access" (Hollow Core).
        /// </summary>
        public static int GetStarPointsForLevel(Pawn pawn, int newLevel)
        {
            if (pawn?.story?.traits == null)
            {
                // Default: 1 point every 5 levels
                return (newLevel % 5 == 0) ? 1 : 0;
            }

            // Hollow Core: no star points ever
            if (HasTrait(pawn, HollowCore)) return 0;

            // Sealed Power before level 25: no star points
            if (HasTrait(pawn, SealedPower) && newLevel < 25) return 0;

            int points = 0;

            // Reincarnator: star point every 3 levels INSTEAD OF base every 5
            bool isReincarnator = HasTrait(pawn, Reincarnator);
            if (isReincarnator)
            {
                if (newLevel % 3 == 0) points += 1;
            }
            else
            {
                // Base: 1 point every 5 levels
                if (newLevel % 5 == 0) points += 1;
            }

            // Natural Talent: +1 bonus star point every 5 levels
            if (HasTrait(pawn, NaturalTalent) && newLevel % 5 == 0) points += 1;

            // Sealed Power after level 25: +2 per 5 levels (total 3)
            if (HasTrait(pawn, SealedPower) && newLevel > 25 && newLevel % 5 == 0) points += 2;

            return points;
        }

        // ═══════════════════════════════════════════
        //  STAT EFFECTIVENESS — called from stat parts
        // ═══════════════════════════════════════════

        /// <summary>
        /// Get the stat effectiveness multiplier for a specific Isekai stat type.
        /// Applied in stat part calculations to scale how much each point of a stat matters.
        /// </summary>
        public static float GetStatEffectiveness(Pawn pawn, IsekaiStatType statType, int level)
        {
            if (pawn?.story?.traits == null) return 1f;

            float mult = 1f;

            // Stat Affinity traits — paired oppositions:
            // STR ↔ DEX (Mighty / Agile)
            // VIT ↔ WIS (Resilient / Enlightened)
            // INT ↔ CHA (Brilliant / Silver Tongue)
            switch (statType)
            {
                case IsekaiStatType.Strength:
                    if (HasTrait(pawn, Mighty))    mult *= 1.5f;
                    if (HasTrait(pawn, Agile))     mult *= 0.75f;
                    if (HasTrait(pawn, Genius))    mult *= 0.5f;
                    break;
                case IsekaiStatType.Dexterity:
                    if (HasTrait(pawn, Agile))     mult *= 1.5f;
                    if (HasTrait(pawn, Mighty))    mult *= 0.75f;
                    break;
                case IsekaiStatType.Vitality:
                    if (HasTrait(pawn, Resilient))    mult *= 1.5f;
                    if (HasTrait(pawn, Enlightened))  mult *= 0.75f;
                    if (HasTrait(pawn, Genius))       mult *= 0.5f;
                    if (HasTrait(pawn, FragileVessel)) mult *= 0.5f;
                    if (HasTrait(pawn, GlassCannon))  mult *= 0.5f;
                    break;
                case IsekaiStatType.Intelligence:
                    if (HasTrait(pawn, Brilliant))    mult *= 1.5f;
                    if (HasTrait(pawn, SilverTongue)) mult *= 0.75f;
                    if (HasTrait(pawn, Genius))       mult *= 2.0f;
                    break;
                case IsekaiStatType.Wisdom:
                    if (HasTrait(pawn, Enlightened)) mult *= 1.5f;
                    if (HasTrait(pawn, Resilient))   mult *= 0.75f;
                    if (HasTrait(pawn, Genius))      mult *= 2.0f;
                    break;
                case IsekaiStatType.Charisma:
                    if (HasTrait(pawn, SilverTongue)) mult *= 1.5f;
                    if (HasTrait(pawn, Brilliant))     mult *= 0.75f;
                    break;
            }

            // Awakened Potential
            if (HasTrait(pawn, AwakenedPotential))
            {
                if (level >= 25)
                    mult *= 1.5f;
                else
                    mult *= 0.9f;
            }

            // Sealed Power: before level 25, -30% all stats
            if (HasTrait(pawn, SealedPower) && level < 25)
            {
                mult *= 0.7f;
            }

            return mult;
        }


        // ═══════════════════════════════════════════
        //  ONE-TIME GRANTS — called when trait is first applied
        // ═══════════════════════════════════════════

        /// <summary>
        /// Apply one-time effects when an Isekai trait is granted.
        /// Called from AddIsekaiTrait().
        /// </summary>
        public static void ApplyOneTimeEffects(Pawn pawn, string traitDefName)
        {
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return;

            switch (traitDefName)
            {
                case Reincarnator:
                    // Reincarnation: Reset level, XP, and stats (New Game+ — keep constellation tree)
                    comp.currentLevel = 1;
                    comp.currentXP = 0;
                    comp.stats.strength = IsekaiStatAllocation.BASE_STAT_VALUE;
                    comp.stats.dexterity = IsekaiStatAllocation.BASE_STAT_VALUE;
                    comp.stats.vitality = IsekaiStatAllocation.BASE_STAT_VALUE;
                    comp.stats.intelligence = IsekaiStatAllocation.BASE_STAT_VALUE;
                    comp.stats.wisdom = IsekaiStatAllocation.BASE_STAT_VALUE;
                    comp.stats.charisma = IsekaiStatAllocation.BASE_STAT_VALUE;
                    comp.stats.availableStatPoints = 0;
                    // +10 bonus star points on top of the reset
                    if (comp.passiveTree != null)
                        comp.passiveTree.availablePoints += 10;
                    break;

                case Regressor:
                    // No drawback — stats remain at default (no penalty applied)
                    break;
            }
        }

        // ═══════════════════════════════════════════
        //  TRAIT ASSIGNMENT
        // ═══════════════════════════════════════════

        /// <summary>
        /// Add an Isekai trait to a pawn, handling conflicts and one-time effects.
        /// Returns true if the trait was successfully added.
        /// </summary>
        public static bool AddIsekaiTrait(Pawn pawn, string traitDefName)
        {
            if (pawn?.story?.traits == null) return false;

            TraitDef traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitDefName);
            if (traitDef == null)
            {
                Log.Warning($"[Isekai Leveling] Trait def not found: {traitDefName}");
                return false;
            }

            // Check if pawn already has this trait
            if (HasTrait(pawn, traitDefName)) return false;

            // Check for conflicts
            if (traitDef.conflictingTraits != null)
            {
                foreach (var conflict in traitDef.conflictingTraits)
                {
                    if (HasTrait(pawn, conflict.defName))
                        return false;
                }
            }
            // Check reverse conflicts: existing traits that list this trait as conflicting
            var allTraits = pawn.story.traits.allTraits;
            for (int i = 0; i < allTraits.Count; i++)
            {
                if (allTraits[i].def.conflictingTraits != null && allTraits[i].def.conflictingTraits.Contains(traitDef))
                    return false;
            }
            // Add the trait
            Trait newTrait = new Trait(traitDef, 0, true);
            pawn.story.traits.GainTrait(newTrait);

            // Apply one-time effects
            ApplyOneTimeEffects(pawn, traitDefName);

            return true;
        }

        /// <summary>
        /// Remove an Isekai trait from a pawn by defName.
        /// </summary>
        public static bool RemoveIsekaiTrait(Pawn pawn, string traitDefName)
        {
            if (pawn?.story?.traits == null) return false;

            var allTraits = pawn.story.traits.allTraits;
            for (int i = allTraits.Count - 1; i >= 0; i--)
            {
                if (allTraits[i].def.defName == traitDefName)
                {
                    pawn.story.traits.RemoveTrait(allTraits[i]);
                    return true;
                }
            }
            return false;
        }

        // ═══════════════════════════════════════════
        //  RANDOM TRAIT ROLLING — for NPC generation
        // ═══════════════════════════════════════════

        // Rollable traits with their commonality weights
        private static readonly List<string> RollableTraits = new List<string>
        {
            Protagonist, Antagonist,
            NaturalTalent, Prodigy, LateBloomer, QuickLearner, SlowGrind,
            PowerSpike, AwakenedPotential, Genius, BattleManiac,
            BerserkerBlood, IronWill, GlassCannon, Fortress, ShadowStep,
            PredatorInstinct, Undying,
            Mighty, Agile, Resilient, Brilliant, Enlightened, SilverTongue,
            MerchantEye, CraftsmanSoul, BeastWhisperer, HealerTouch, Lucky, CursedLuck,
            HollowCore, FragileVessel, EchoOfDefeat, SealedPower
        };

        // Local commonality map (mirrors XML commonality values)
        private static readonly Dictionary<string, float> TraitCommonality = new Dictionary<string, float>
        {
            { Protagonist, 0.01f }, { Antagonist, 0.01f },
            { NaturalTalent, 0.8f }, { Prodigy, 0.6f }, { LateBloomer, 0.5f },
            { QuickLearner, 1.2f }, { SlowGrind, 0.7f }, { PowerSpike, 0.6f },
            { AwakenedPotential, 0.4f }, { Genius, 0.5f }, { BattleManiac, 0.5f },
            { BerserkerBlood, 0.6f }, { IronWill, 0.8f }, { GlassCannon, 0.7f },
            { Fortress, 0.6f }, { ShadowStep, 0.5f }, { PredatorInstinct, 0.6f },
            { Undying, 0.3f },
            { Mighty, 1.5f }, { Agile, 1.5f }, { Resilient, 1.5f },
            { Brilliant, 1.5f }, { Enlightened, 1.5f }, { SilverTongue, 1.5f },
            { MerchantEye, 1.0f }, { CraftsmanSoul, 1.0f }, { BeastWhisperer, 1.0f },
            { HealerTouch, 0.8f }, { Lucky, 0.6f }, { CursedLuck, 0.5f },
            { HollowCore, 0.3f }, { FragileVessel, 0.4f },
            { EchoOfDefeat, 0.5f }, { SealedPower, 0.3f }
        };

        /// <summary>
        /// Roll random Isekai traits for a pawn during NPC generation.
        /// Typically assigns 0-2 traits based on weighted probability.
        /// </summary>
        public static void RollRandomTraits(Pawn pawn)
        {
            if (pawn?.story?.traits == null) return;

            // Determine how many traits to assign (0-2)
            // 80% chance of 0, 15% chance of 1, 5% chance of 2
            float roll = Rand.Value;
            int traitCount;
            if (roll < 0.80f)      traitCount = 0;
            else if (roll < 0.95f) traitCount = 1;
            else                   traitCount = 2;

            if (traitCount == 0) return;

            // Build weighted candidate list, filtering out traits that conflict
            var candidates = new List<(string defName, float weight)>();

            foreach (string defName in RollableTraits)
            {
                TraitDef def = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
                if (def == null) continue;

                // Check for conflicts with existing traits
                bool hasConflict = false;
                if (def.conflictingTraits != null)
                {
                    foreach (var conflict in def.conflictingTraits)
                    {
                        if (HasTrait(pawn, conflict.defName))
                        {
                            hasConflict = true;
                            break;
                        }
                    }
                }
                if (hasConflict) continue;

                // Already has this trait
                if (HasTrait(pawn, defName)) continue;

                float weight = TraitCommonality.TryGetValue(defName, out float w) ? w : 0.5f;
                candidates.Add((defName, weight));
            }

            // Roll traits from weighted list
            for (int i = 0; i < traitCount && candidates.Count > 0; i++)
            {
                // Weighted random selection
                float totalWeight = 0f;
                foreach (var c in candidates) totalWeight += c.weight;

                float target = Rand.Value * totalWeight;
                float cumulative = 0f;
                string chosen = null;

                for (int j = 0; j < candidates.Count; j++)
                {
                    cumulative += candidates[j].weight;
                    if (target <= cumulative)
                    {
                        chosen = candidates[j].defName;
                        break;
                    }
                }

                if (chosen == null) chosen = candidates[candidates.Count - 1].defName;

                // Add the trait
                if (AddIsekaiTrait(pawn, chosen))
                {
                    // Remove chosen + any now-conflicting traits from candidates
                    TraitDef chosenDef = DefDatabase<TraitDef>.GetNamedSilentFail(chosen);
                    var conflicts = new HashSet<string>();
                    conflicts.Add(chosen);
                    if (chosenDef?.conflictingTraits != null)
                    {
                        foreach (var c in chosenDef.conflictingTraits)
                            conflicts.Add(c.defName);
                    }

                    candidates.RemoveAll(c => conflicts.Contains(c.defName));
                }
            }
        }
    }
}
