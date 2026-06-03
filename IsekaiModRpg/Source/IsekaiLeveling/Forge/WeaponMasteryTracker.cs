using System.Collections.Generic;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Mastery tiers earned through combat use of a specific weapon type.
    /// </summary>
    public enum MasteryTier
    {
        Novice,       // 0 XP
        Apprentice,   // 100 XP
        Skilled,      // 300 XP
        Adept,        // 700 XP
        Expert,       // 1500 XP
        Master,       // 3000 XP
        Grandmaster   // 6000 XP
    }

    /// <summary>
    /// Tracks per-weapon-type mastery XP for a single pawn.
    /// Nested inside IsekaiComponent and saved via Scribe_Deep.
    /// Keyed by ThingDef.defName (e.g. "MeleeWeapon_Longsword").
    /// </summary>
    public class WeaponMasteryTracker : IExposable
    {
        public Dictionary<string, int> masteryXP = new Dictionary<string, int>();

        // ── Tier thresholds ──
        private static readonly int[] TierThresholds = { 0, 100, 300, 700, 1500, 3000, 6000 };

        // ── Tier bonuses: [hitChance, attackSpeed, damage] ──
        private static readonly float[][] TierBonuses =
        {
            new float[] { 0f,    0f,    0f    }, // Novice
            new float[] { 0.08f, 0.04f, 0.03f }, // Apprentice
            new float[] { 0.15f, 0.08f, 0.06f }, // Skilled
            new float[] { 0.22f, 0.12f, 0.10f }, // Adept
            new float[] { 0.30f, 0.18f, 0.15f }, // Expert
            new float[] { 0.40f, 0.25f, 0.22f }, // Master
            new float[] { 0.50f, 0.30f, 0.30f }, // Grandmaster
        };

        public void ExposeData()
        {
            Scribe_Collections.Look(ref masteryXP, "masteryXP", LookMode.Value, LookMode.Value);
            if (masteryXP == null)
                masteryXP = new Dictionary<string, int>();
        }

        /// <summary>
        /// Add mastery XP for a specific weapon defName.
        /// </summary>
        public void AddMasteryXP(string weaponDefName, int amount)
        {
            if (string.IsNullOrEmpty(weaponDefName) || amount <= 0) return;

            if (masteryXP.TryGetValue(weaponDefName, out int current))
                masteryXP[weaponDefName] = current + amount;
            else
                masteryXP[weaponDefName] = amount;
        }

        /// <summary>
        /// Get current mastery XP for a weapon type.
        /// </summary>
        public int GetXP(string weaponDefName)
        {
            if (string.IsNullOrEmpty(weaponDefName)) return 0;
            return masteryXP.TryGetValue(weaponDefName, out int xp) ? xp : 0;
        }

        /// <summary>
        /// Get the mastery tier for a specific weapon type.
        /// </summary>
        public MasteryTier GetMasteryTier(string weaponDefName)
        {
            int xp = GetXP(weaponDefName);
            MasteryTier tier = MasteryTier.Novice;
            for (int i = TierThresholds.Length - 1; i >= 0; i--)
            {
                if (xp >= TierThresholds[i])
                {
                    tier = (MasteryTier)i;
                    break;
                }
            }
            return tier;
        }

        /// <summary>
        /// Get XP required for the next tier. Returns -1 if already Grandmaster.
        /// </summary>
        public int GetXPForNextTier(string weaponDefName)
        {
            MasteryTier current = GetMasteryTier(weaponDefName);
            int nextIndex = (int)current + 1;
            if (nextIndex >= TierThresholds.Length) return -1;
            return TierThresholds[nextIndex];
        }

        /// <summary>
        /// Get mastery bonuses for a weapon as (hitChance, attackSpeed, damage).
        /// Only applies when the pawn is wielding the matching weapon type.
        /// </summary>
        public void GetMasteryBonuses(string weaponDefName, out float hitChance, out float attackSpeed, out float damage)
        {
            MasteryTier tier = GetMasteryTier(weaponDefName);
            int index = (int)tier;
            hitChance = TierBonuses[index][0];
            attackSpeed = TierBonuses[index][1];
            damage = TierBonuses[index][2];
        }

        /// <summary>
        /// Get a display-friendly name for a mastery tier.
        /// </summary>
        public static string GetTierLabel(MasteryTier tier)
        {
            switch (tier)
            {
                case MasteryTier.Novice: return "Novice";
                case MasteryTier.Apprentice: return "Apprentice";
                case MasteryTier.Skilled: return "Skilled";
                case MasteryTier.Adept: return "Adept";
                case MasteryTier.Expert: return "Expert";
                case MasteryTier.Master: return "Master";
                case MasteryTier.Grandmaster: return "Grandmaster";
                default: return "Unknown";
            }
        }
    }
}
