using System;
using RimWorld;
using UnityEngine;
using Verse;
using IsekaiLeveling.UI;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Component that stores and displays a mob's rank
    /// Attached to all non-player humanlike pawns (animals, insects, anomalies, etc.)
    /// Each individual creature gets a stable random variation within their species' rank range.
    /// Player-faction creatures also gain XP and level up using the same allocation system as colonists.
    /// </summary>
    public class MobRankComponent : ThingComp
    {
        // Cached rank data — rolled ONCE per creature via PawnStatGenerator.RollStandardRank()
        // and then persisted forever. See RecalculateRank + PostExposeData.
        private MobRankTier cachedRank = MobRankTier.F;
        private MobRankTier cachedBaseRank = MobRankTier.F;
        private bool cachedIsElite = false;
        private bool isInitialized = false;

        // ── Leveling state (player-faction creatures) ──────────────────────────
        public int currentLevel = 1;
        public int currentXP = 0;
        public IsekaiStatAllocation stats = new IsekaiStatAllocation();
        public bool statsInitialized = false;
        private int lastNotifiedLevel = 0;
        private int lastLoggedLevel = 0;

        // ── Rank override (for quest-spawned bosses / forced-rank targets) ─────
        // When set, RecalculateRank skips threat-score logic and uses these values.
        private bool hasRankOverride = false;
        private MobRankTier overriddenRank = MobRankTier.F;
        private bool overriddenElite = false;

        /// <summary>
        /// True once RecalculateRank has populated the cached rank/stats at least once.
        /// Read-only view of the private flag for external callers (StatWorkers, UI, patches).
        /// </summary>
        public bool IsInitialized => isInitialized;

        public MobRankTier Rank
        {
            get
            {
                EnsureInitialized();
                return cachedRank;
            }
        }
        
        public MobRankTier BaseRank
        {
            get
            {
                EnsureInitialized();
                return cachedBaseRank;
            }
        }

        /// <summary>
        /// Returns true if this creature is elite (separate from rank, 10% chance for C+ ranks)
        /// Elite creatures get bonus stats on top of their rank bonuses
        /// </summary>
        public bool IsElite
        {
            get
            {
                EnsureInitialized();
                return cachedIsElite;
            }
        }
        
        /// <summary>
        /// How many tiers above base this creature rolled
        /// </summary>
        public int RankBonus => (int)Rank - (int)BaseRank;

        public string RankString => MobRankUtility.GetRankString(Rank);
        public Color RankColor => MobRankUtility.GetRankColor(Rank);
        public string RankTitle => MobRankUtility.GetRankTitle(Rank);

        public Pawn Pawn => parent as Pawn;

        /// <summary>
        /// Triggers the one-time rank roll if this creature hasn't been initialized yet.
        /// After the first call (or after a save load), this is a no-op.
        /// </summary>
        private void EnsureInitialized()
        {
            if (isInitialized) return;
            RecalculateRank();
        }

        /// <summary>
        /// Random-roll-per-creature: each non-overridden creature picks its rank ONCE
        /// from PawnStatGenerator.RollStandardRank(), then derives stats from that rank.
        /// Subsequent calls are no-ops because <see cref="statsInitialized"/> latches to true.
        /// World bosses with <see cref="hasRankOverride"/> bypass the roll entirely.
        /// </summary>
        public void RecalculateRank()
        {
            if (Pawn == null) return;

            // Honor mod-settings opt-outs. Quest-spawned bosses with hasRankOverride
            // bypass the exclude check on purpose — the player explicitly summoned them.
            if (!hasRankOverride && MobRankInjector.IsExcludedFromRanking(Pawn))
            {
                // Mark "evaluated" so we don't re-enter every stat query, but leave
                // cachedRank/stats at their defaults so this creature contributes
                // nothing to the leveling system. Re-enabling the setting later will
                // start fresh on the next load (statsInitialized stays false).
                isInitialized = true;
                return;
            }

            MobRankStatPatches.SetCalculatingRank(true);
            try
            {
                if (hasRankOverride)
                {
                    cachedRank = overriddenRank;
                    cachedBaseRank = overriddenRank;
                    cachedIsElite = overriddenElite;
                    EnsureStatsInitialized();
                }
                else if (!statsInitialized)
                {
                    // First-time roll for this creature.
                    string rolledRank = PawnStatGenerator.RollStandardRank();
                    cachedRank = RankTierFromString(rolledRank);
                    cachedBaseRank = cachedRank;
                    cachedIsElite = false;

                    float eliteChance = GetEliteChance(cachedRank);
                    if (eliteChance > 0f && Rand.Chance(eliteChance))
                    {
                        cachedIsElite = true;
                        EnsureEliteName();
                    }

                    EnsureStatsInitialized(rolledRank);
                }
                // else: rank already loaded from save; nothing to (re-)compute.

                isInitialized = true;
            }
            finally
            {
                MobRankStatPatches.SetCalculatingRank(false);
            }
        }

        /// <summary>
        /// Per-rank elite probability table. Mirrors the originally shipped distribution:
        /// commoner ranks have a higher elite chance than the rare top tiers, since the
        /// top-tier creatures are already rare and don't need to compound their rarity.
        /// </summary>
        private static float GetEliteChance(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.C:      return 0.05f;
                case MobRankTier.B:      return 0.06f;
                case MobRankTier.A:      return 0.07f;
                case MobRankTier.S:      return 0.05f;
                case MobRankTier.SS:     return 0.04f;
                case MobRankTier.SSS:    return 0.03f;
                case MobRankTier.Nation: return 0.03f;
                default:                 return 0f;
            }
        }

        /// <summary>
        /// Generates the per-creature stat allocation and starting level the first
        /// time we know this creature's rank. Used by Window_CreatureStats and GainXP.
        /// </summary>
        /// <param name="rankStringOverride">Pass the rank string already produced by
        /// the roller to skip redundant enum→string conversion; otherwise derived from
        /// <see cref="cachedRank"/>.</param>
        private void EnsureStatsInitialized(string rankStringOverride = null)
        {
            if (statsInitialized) return;
            if (stats == null) stats = new IsekaiStatAllocation();

            string rankString = rankStringOverride ?? MobRankUtility.GetRankString(cachedRank);
            try
            {
                PawnStatGenerator.GenerateStatsForRank(rankString, stats);
                currentLevel = PawnStatGenerator.CalculateLevelFromStats(stats);
                int minLevel = PawnStatGenerator.GetMinLevelForRank(rankString);
                if (currentLevel < minLevel) currentLevel = minLevel;
                stats.availableStatPoints = 0;
            }
            catch (Exception ex)
            {
                if (Prefs.DevMode)
                    Log.Warning($"[Isekai] EnsureStatsInitialized failed for {Pawn?.LabelShort}: {ex.Message}");
                currentLevel = 1;
            }
            statsInitialized = true;
        }

        /// <summary>
        /// Force this creature to a specific rank (used by world boss / quest bosses).
        /// Persists across saves and re-rolls of the threat-score path.
        /// </summary>
        public void SetRankOverride(MobRankTier forcedRank)
        {
            hasRankOverride = true;
            overriddenRank = forcedRank;
            cachedRank = forcedRank;
            cachedBaseRank = forcedRank;

            // Regenerate stats/level so the boss isn't trapped at level 1.
            statsInitialized = false;
            EnsureStatsInitialized();
            isInitialized = true;
        }

        /// <summary>
        /// Force the elite flag on/off; persists alongside SetRankOverride.
        /// </summary>
        public void SetEliteOverride(bool isElite)
        {
            cachedIsElite = isElite;
            overriddenElite = isElite;
            if (isElite) EnsureEliteName();
        }

        /// <summary>
        /// Gives the creature a flavorful elite name, but only for non-colonist
        /// faction creatures that don't already have a custom name.
        /// </summary>
        private void EnsureEliteName()
        {
            Pawn p = Pawn;
            if (p == null || p.RaceProps == null || p.RaceProps.Humanlike) return;
            if (p.Faction != null && p.Faction == Faction.OfPlayer) return;

            // Don't overwrite a name the player has already set
            if (p.Name is NameSingle ns)
            {
                string kindLabel = p.kindDef?.LabelCap ?? p.def?.LabelCap;
                if (!string.IsNullOrWhiteSpace(ns.Name) && ns.Name != kindLabel) return;
            }
            else if (p.Name != null)
            {
                // Pawn has a triple/composite name (rare for animals) — leave it alone
                return;
            }

            p.Name = new NameSingle(MobRankUtility.GenerateEliteName(p, cachedRank), false);
        }

        /// <summary>
        /// Force a fresh rank roll (e.g. dev-mode reroll button). Wipes both the
        /// initialization and the stat seed so RecalculateRank rolls anew.
        /// </summary>
        public void ForceRecalculate()
        {
            isInitialized = false;
            statsInitialized = false;
            RecalculateRank();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            RecalculateRank();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();

            // Random-rolled rank, overrides, and per-creature XP/stats all persist.
            // Without these, every load would reroll the rank to a different tier.
            Scribe_Values.Look(ref hasRankOverride, "hasRankOverride", false);
            Scribe_Values.Look(ref overriddenRank, "overriddenRank", MobRankTier.F);
            Scribe_Values.Look(ref overriddenElite, "overriddenElite", false);
            Scribe_Values.Look(ref cachedRank, "mobRank", MobRankTier.F);
            Scribe_Values.Look(ref cachedBaseRank, "mobBaseRank", MobRankTier.F);
            Scribe_Values.Look(ref cachedIsElite, "mobIsElite", false);
            Scribe_Values.Look(ref currentLevel, "mobLevel", 1);
            Scribe_Values.Look(ref currentXP, "mobXP", 0);
            Scribe_Values.Look(ref statsInitialized, "mobStatsInit", false);
            Scribe_Deep.Look(ref stats, "mobStats");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (stats == null) stats = new IsekaiStatAllocation();
                if (hasRankOverride)
                {
                    // Override re-applies on every load so a hot-swapped boss def can't drift.
                    cachedRank = overriddenRank;
                    cachedBaseRank = overriddenRank;
                    cachedIsElite = overriddenElite;
                }
                if (statsInitialized)
                {
                    // Skip the random-roll path next time RecalculateRank fires.
                    isInitialized = true;
                }
            }
        }

        // ── Leveling API ──────────────────────────────────────────────────────

        /// <summary>
        /// XP threshold for the next level. Curve mirrors the colonist leveling system.
        /// </summary>
        public int XPToNextLevel
        {
            get
            {
                const float baseXP = 100f;
                const float exponent = 1.5f;
                return Mathf.Max(100, Mathf.RoundToInt(baseXP * Mathf.Pow(currentLevel, exponent)));
            }
        }

        public float LevelProgress => (float)currentXP / Mathf.Max(1, XPToNextLevel);

        /// <summary>
        /// Award XP to this creature. Only player-faction (and optionally mech) creatures level up.
        /// Wisdom multiplies XP gained, mirroring the colonist progression.
        /// </summary>
        public void GainXP(int amount, string source = null)
        {
            Pawn p = Pawn;
            if (p?.Faction == null || !p.Faction.IsPlayer) return;
            if (p.RaceProps.IsMechanoid && !(IsekaiMod.Settings?.EnableMechCreatureXP ?? true)) return;
            // Excluded creature types don't level up — no XP, no notifications, no FX.
            if (MobRankInjector.IsExcludedFromRanking(p)) return;

            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            if (currentLevel >= maxLevel) return;

            float xpMult = IsekaiMod.Settings?.XPMultiplier ?? 3f;
            int gained = Mathf.RoundToInt(amount * xpMult);

            if (statsInitialized && stats != null && stats.wisdom > 0f)
            {
                gained = Mathf.RoundToInt(gained * (1f + stats.wisdom * 0.02f));
            }

            if (amount > 0 && gained <= 0) gained = 1;
            currentXP += gained;

            bool notify = IsekaiMod.Settings?.EnableXPNotifications ?? true;
            if (notify && p.Spawned)
            {
                if (gained >= 15) IsekaiAnimations.PlayXPGainEffect(p, gained, source);
                else if (gained >= 5) IsekaiAnimations.PlaySmallXPEffect(p, gained);
            }

            // Cap recursion in case of degenerate XP awards
            int safety = 0;
            while (currentXP >= XPToNextLevel && currentLevel < maxLevel && safety < 50)
            {
                LevelUp();
                safety++;
            }
        }

        /// <summary>
        /// Drain any banked XP that's already past the next level threshold.
        /// Called from CompTickRare so deferred XP from offline awards still levels up.
        /// </summary>
        public void ProcessDeferredLevelUps()
        {
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            int safety = 0;
            while (currentXP >= XPToNextLevel && currentLevel < maxLevel && safety < 50)
            {
                LevelUp();
                safety++;
            }
        }

        private void LevelUp()
        {
            string oldRank = GetRankFromLevel();
            currentXP -= XPToNextLevel;
            currentLevel++;
            stats?.OnLevelUp();

            string newRank = GetRankFromLevel();
            if (oldRank != newRank && !hasRankOverride)
            {
                // Promote the cached rank only when the player earned it.
                // Bosses with overrides keep their forced rank.
                cachedRank = RankTierFromString(newRank);
            }

            Pawn p = Pawn;
            bool announce = IsekaiMod.Settings != null
                && IsekaiMod.Settings.EnableLevelUpNotifications
                && (currentLevel <= 10 || currentLevel % 10 == 0 || currentLevel - lastNotifiedLevel >= 10);
            if (announce && p != null)
            {
                lastNotifiedLevel = currentLevel;
                string title = p.LabelShort + " Leveled Up!";
                string body = $"{p.LabelShort} has reached level {currentLevel}!\n\nThey have {stats?.availableStatPoints ?? 0} stat point(s) to spend.";
                if (IsekaiMod.Settings.UseLevelUpNotice)
                    Messages.Message($"{title} (Lv{currentLevel})", p, MessageTypeDefOf.PositiveEvent, false);
                else
                    Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.PositiveEvent, p);
            }

            if (p != null && p.Spawned && (currentLevel <= 10 || currentLevel % 5 == 0))
            {
                IsekaiAnimations.PlayLevelUpEffect(p, currentLevel);
            }

            if (currentLevel <= 10 || currentLevel % 25 == 0 || currentLevel - lastLoggedLevel >= 25)
            {
                lastLoggedLevel = currentLevel;
                Log.Message($"[Isekai Leveling] {p?.LabelShort} (creature) reached level {currentLevel}");
            }
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            Pawn p = Pawn;
            int maxLevel = IsekaiMod.Settings?.MaxLevel ?? 9999;
            if (p != null && !p.Dead && !p.Destroyed
                && p.Faction != null && p.Faction.IsPlayer
                && currentXP >= XPToNextLevel && currentLevel < maxLevel)
            {
                ProcessDeferredLevelUps();
            }
        }

        /// <summary>
        /// Maps the current level to a rank string (used by Window_CreatureStats).
        /// Mirrors the colonist progression breakpoints.
        /// </summary>
        public string GetRankFromLevel()
        {
            if (currentLevel >= 401) return "SSS";
            if (currentLevel >= 201) return "SS";
            if (currentLevel >= 101) return "S";
            if (currentLevel >= 51)  return "A";
            if (currentLevel >= 26)  return "B";
            if (currentLevel >= 18)  return "C";
            if (currentLevel >= 11)  return "D";
            if (currentLevel >= 6)   return "E";
            return "F";
        }

        public static MobRankTier RankTierFromString(string rank)
        {
            switch (rank)
            {
                case "Nation": return MobRankTier.Nation;
                case "SSS": return MobRankTier.SSS;
                case "SS": return MobRankTier.SS;
                case "S": return MobRankTier.S;
                case "A": return MobRankTier.A;
                case "B": return MobRankTier.B;
                case "C": return MobRankTier.C;
                case "D": return MobRankTier.D;
                case "E": return MobRankTier.E;
                default: return MobRankTier.F;
            }
        }

        /// <summary>
        /// Add rank info to the inspect string with stat bonuses
        /// </summary>
        public override string CompInspectStringExtra()
        {
            if (!IsekaiLevelingSettings.showMobRanks) return null;
            if (!MobRankInjector.ShouldShowMobRank(Pawn)) return null;
            
            string eliteMarker = IsElite ? " ★" : "";
            string eliteLabel = IsElite ? " (Elite)" : "";
            
            // Get stat multipliers
            float healthMult = MobRankUtility.GetRankHealthMultiplier(Rank);
            float damageMult = MobRankUtility.GetRankDamageMultiplier(Rank);
            float speedMult = MobRankUtility.GetRankSpeedMultiplier(Rank);
            float armorBonus = MobRankUtility.GetRankArmorBonus(Rank);
            
            // Elite bonus
            if (IsElite)
            {
                healthMult *= 1.20f;
                damageMult *= 1.15f;
                speedMult *= 1.15f;
                armorBonus += 0.10f;
            }
            
            // Build stat string
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"Rank: {RankString}{eliteMarker} - {RankTitle}{eliteLabel}");
            
            // Only show non-baseline stats
            if (healthMult != 1f || damageMult != 1f || speedMult != 1f || armorBonus != 0f)
            {
                sb.Append("  ");
                bool first = true;
                
                if (healthMult != 1f)
                {
                    if (!first) sb.Append(" | ");
                    sb.Append($"HP: {FormatPercent(healthMult)}");
                    first = false;
                }
                
                if (damageMult != 1f)
                {
                    if (!first) sb.Append(" | ");
                    sb.Append($"DMG: {FormatPercent(damageMult)}");
                    first = false;
                }
                
                if (speedMult != 1f)
                {
                    if (!first) sb.Append(" | ");
                    sb.Append($"SPD: {FormatPercent(speedMult)}");
                    first = false;
                }
                
                if (armorBonus != 0f)
                {
                    if (!first) sb.Append(" | ");
                    string sign = armorBonus >= 0 ? "+" : "";
                    sb.Append($"ARM: {sign}{(int)(armorBonus * 100)}%");
                }
            }
            
            return sb.ToString().TrimEnd('\r', '\n');
        }
        
        /// <summary>
        /// Format a multiplier as a percentage with sign
        /// </summary>
        private string FormatPercent(float mult)
        {
            int percent = (int)((mult - 1f) * 100);
            if (percent >= 0)
                return $"+{percent}%";
            else
                return $"{percent}%";
        }
    }

    /// <summary>
    /// Component properties for XML definition
    /// </summary>
    public class CompProperties_MobRank : CompProperties
    {
        public CompProperties_MobRank()
        {
            compClass = typeof(MobRankComponent);
        }
    }
}
