using UnityEngine;
using Verse;

namespace IsekaiLeveling.MobRanking
{
    /// <summary>
    /// Represents a mob's rank tier from F (weakest) to SSS (strongest)
    /// </summary>
    public enum MobRankTier
    {
        F = 0,
        E = 1,
        D = 2,
        C = 3,
        B = 4,
        A = 5,
        S = 6,
        SS = 7,
        SSS = 8,
        // Nation is not naturally rolled — only assigned via SetRankOverride
        // (used for world bosses and faction-tier quest targets).
        Nation = 9
    }

    /// <summary>
    /// Static utility class for mob rank calculations and display
    /// </summary>
    public static class MobRankUtility
    {
        /// <summary>
        /// Get the display string for a rank tier
        /// </summary>
        public static string GetRankString(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return "Nation";
                case MobRankTier.SSS: return "SSS";
                case MobRankTier.SS: return "SS";
                case MobRankTier.S: return "S";
                case MobRankTier.A: return "A";
                case MobRankTier.B: return "B";
                case MobRankTier.C: return "C";
                case MobRankTier.D: return "D";
                case MobRankTier.E: return "E";
                case MobRankTier.F: return "F";
                default: return "?";
            }
        }

        /// <summary>
        /// Get the color associated with a rank tier
        /// </summary>
        public static Color GetRankColor(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return new Color(1f, 0.2f, 0.2f);   // Crimson — faction-tier menace
                case MobRankTier.SSS: return new Color(1f, 0.9f, 0.3f);      // Brilliant Gold
                case MobRankTier.SS: return new Color(1f, 0.75f, 0.35f);     // Orange Gold
                case MobRankTier.S: return new Color(0.85f, 0.45f, 0.9f);    // Purple
                case MobRankTier.A: return new Color(0.4f, 0.9f, 0.55f);     // Green
                case MobRankTier.B: return new Color(0.45f, 0.7f, 1f);       // Blue
                case MobRankTier.C: return new Color(0.6f, 0.85f, 1f);       // Light Blue
                case MobRankTier.D: return new Color(0.75f, 0.75f, 0.78f);   // Silver
                case MobRankTier.E: return new Color(0.6f, 0.55f, 0.5f);     // Bronze
                case MobRankTier.F: return new Color(0.5f, 0.45f, 0.4f);     // Dark Bronze
                default: return Color.gray;
            }
        }

        /// <summary>
        /// Get a descriptive title for a rank tier
        /// </summary>
        public static string GetRankTitle(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return "World Threat";
                case MobRankTier.SSS: return "Apex Predator";
                case MobRankTier.SS: return "Alpha Beast";
                case MobRankTier.S: return "Legendary";
                case MobRankTier.A: return "Deadly";
                case MobRankTier.B: return "Dangerous";
                case MobRankTier.C: return "Threatening";
                case MobRankTier.D: return "Common";
                case MobRankTier.E: return "Weak";
                case MobRankTier.F: return "Harmless";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Calculate the BASE rank tier from threat score (before random variation)
        /// This determines the creature's rank range
        /// </summary>
        public static MobRankTier CalculateBaseRankFromThreatScore(float threatScore)
        {
            // Recalibrated thresholds - much stricter now
            // These represent the BASE rank, individual variation adds ±0-2 tiers
            // Camels/horses should be D-E, wolves C-D, bears B-C, thrumbos S+
            if (threatScore >= 1500f) return MobRankTier.S;      // Thrumbos, Mega creatures (can roll S-SSS)
            if (threatScore >= 800f) return MobRankTier.A;       // Elephants, Rhinoceros (can roll A-SS)
            if (threatScore >= 400f) return MobRankTier.B;       // Bears, Panthers, Large predators (can roll B-S)
            if (threatScore >= 200f) return MobRankTier.C;       // Wolves, Cougars (can roll C-A)
            if (threatScore >= 100f) return MobRankTier.D;       // Camels, Horses, Boars (can roll D-B)
            if (threatScore >= 40f) return MobRankTier.E;        // Dogs, Deer, medium animals (can roll E-C)
            if (threatScore >= 15f) return MobRankTier.F;        // Small animals like geese (can roll F-D)
            return MobRankTier.F;                                // Tiny creatures (F-E only)
        }

        /// <summary>
        /// Apply random variation to a base rank using a stable seed
        /// Higher tier upgrades are progressively rarer
        /// </summary>
        public static MobRankTier ApplyRandomVariation(MobRankTier baseRank, int seed, float threatScore)
        {
            // Use stable random based on pawn's unique ID
            System.Random rand = new System.Random(seed);
            float roll = (float)rand.NextDouble();
            
            // Determine max possible upgrade based on threat score
            int maxUpgrade = GetMaxUpgrade(threatScore);
            
            // Weighted random - higher upgrades are rarer
            // 60% stay at base, 25% +1, 10% +2, 5% +3 (if allowed)
            int upgrade = 0;
            if (roll > 0.95f && maxUpgrade >= 3)
                upgrade = 3;
            else if (roll > 0.85f && maxUpgrade >= 2)
                upgrade = 2;
            else if (roll > 0.60f && maxUpgrade >= 1)
                upgrade = 1;
            
            // Apply upgrade, capped at SSS
            int finalRank = Mathf.Min((int)baseRank + upgrade, (int)MobRankTier.SSS);
            
            return (MobRankTier)finalRank;
        }

        /// <summary>
        /// Determine maximum possible rank upgrade based on creature's threat level
        /// </summary>
        private static int GetMaxUpgrade(float threatScore)
        {
            // Bigger/stronger creatures have wider rank ranges
            if (threatScore >= 1500f) return 2;   // S base -> can reach SSS
            if (threatScore >= 800f) return 2;    // A base -> can reach S/SS
            if (threatScore >= 400f) return 2;    // B base -> can reach A/S
            if (threatScore >= 200f) return 2;    // C base -> can reach B/A
            if (threatScore >= 100f) return 2;    // D base -> can reach C/B
            if (threatScore >= 40f) return 2;     // E base -> can reach D/C
            if (threatScore >= 15f) return 2;     // F base -> can reach E/D
            return 1;                              // Tiny creatures: F base -> can reach E only
        }
        
        /// <summary>
        /// Get overall stat multiplier for a rank
        /// This significantly buffs higher rank creatures
        /// </summary>
        public static float GetRankStatMultiplier(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return 4.0f; // 4x stats - faction-tier
                case MobRankTier.SSS: return 3.0f;   // 3x stats - terrifying
                case MobRankTier.SS: return 2.5f;    // 2.5x stats
                case MobRankTier.S: return 2.0f;     // 2x stats
                case MobRankTier.A: return 1.6f;     // 60% stronger
                case MobRankTier.B: return 1.35f;    // 35% stronger
                case MobRankTier.C: return 1.15f;    // 15% stronger
                case MobRankTier.D: return 1.0f;     // Baseline
                case MobRankTier.E: return 0.9f;     // 10% weaker
                case MobRankTier.F: return 0.75f;    // 25% weaker
                default: return 1.0f;
            }
        }
        
        /// <summary>
        /// Get damage multiplier for a rank (affects melee and ranged damage)
        /// </summary>
        public static float GetRankDamageMultiplier(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return 3.0f;
                case MobRankTier.SSS: return 2.5f;
                case MobRankTier.SS: return 2.0f;
                case MobRankTier.S: return 1.7f;
                case MobRankTier.A: return 1.4f;
                case MobRankTier.B: return 1.25f;
                case MobRankTier.C: return 1.1f;
                case MobRankTier.D: return 1.0f;
                case MobRankTier.E: return 0.85f;
                case MobRankTier.F: return 0.7f;
                default: return 1.0f;
            }
        }
        
        /// <summary>
        /// Get health multiplier for a rank
        /// </summary>
        public static float GetRankHealthMultiplier(MobRankTier tier)
        {
            switch (tier)
            {
                // Bumped from 5.0 → 12.0: world bosses were folding to two factions
                // dogpiling them (e.g. Bulbfreak vs player + raid). Combined with the
                // BodySize x2.5 patch in WorldBossSizePatch this puts a Nation boss at
                // ~30x baseline body part HP, which holds up against 2 simultaneous
                // attacking factions while still being killable in a sustained fight.
                case MobRankTier.Nation: return 12.0f;
                case MobRankTier.SSS: return 3.5f;   // Extremely tanky
                case MobRankTier.SS: return 2.8f;
                case MobRankTier.S: return 2.2f;
                case MobRankTier.A: return 1.7f;
                case MobRankTier.B: return 1.4f;
                case MobRankTier.C: return 1.2f;
                case MobRankTier.D: return 1.0f;
                case MobRankTier.E: return 0.85f;
                case MobRankTier.F: return 0.7f;
                default: return 1.0f;
            }
        }
        
        /// <summary>
        /// Get armor bonus for a rank (added to existing armor)
        /// </summary>
        public static float GetRankArmorBonus(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return 0.65f;
                case MobRankTier.SSS: return 0.50f;  // +50% armor
                case MobRankTier.SS: return 0.40f;
                case MobRankTier.S: return 0.30f;
                case MobRankTier.A: return 0.20f;
                case MobRankTier.B: return 0.12f;
                case MobRankTier.C: return 0.05f;
                case MobRankTier.D: return 0f;
                case MobRankTier.E: return -0.05f;
                case MobRankTier.F: return -0.10f;
                default: return 0f;
            }
        }
        
        /// <summary>
        /// Get move speed multiplier for a rank
        /// </summary>
        public static float GetRankSpeedMultiplier(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return 1.5f;
                case MobRankTier.SSS: return 1.4f;
                case MobRankTier.SS: return 1.3f;
                case MobRankTier.S: return 1.2f;
                case MobRankTier.A: return 1.15f;
                case MobRankTier.B: return 1.08f;
                case MobRankTier.C: return 1.03f;
                case MobRankTier.D: return 1.0f;
                case MobRankTier.E: return 0.95f;
                case MobRankTier.F: return 0.9f;
                default: return 1.0f;
            }
        }
        
        /// <summary>
        /// Get XP reward multiplier for killing this rank creature
        /// </summary>
        /// <summary>
        /// Builds a flavorful name for an elite creature ("Bonecrusher the Bear", etc.).
        /// Used by MobRankComponent when an animal rolls or is forced into the elite state.
        /// Falls back to the kind label if no flavor adjective fits the rank.
        /// </summary>
        public static string GenerateEliteName(Pawn pawn, MobRankTier rank)
        {
            if (pawn == null) return "Unknown";

            string kindLabel = pawn.kindDef?.LabelCap.ToString()
                               ?? pawn.def?.LabelCap.ToString()
                               ?? "Beast";

            // Stable seed so the same pawn keeps the same elite name across saves
            System.Random rng = new System.Random(pawn.thingIDNumber + 31337);
            string[] adjectives = GetEliteAdjectivesForRank(rank);
            string adjective = adjectives[rng.Next(adjectives.Length)];

            return $"{adjective} the {kindLabel}";
        }

        private static string[] GetEliteAdjectivesForRank(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation:
                case MobRankTier.SSS:
                    return new[] { "Worldrender", "Skyeater", "Doomspeaker", "Voidmaw", "Ashking" };
                case MobRankTier.SS:
                    return new[] { "Bonecrusher", "Stormfang", "Direhorn", "Nightreaver" };
                case MobRankTier.S:
                    return new[] { "Ironhide", "Bloodmane", "Frostclaw", "Shadowstalker" };
                case MobRankTier.A:
                    return new[] { "Grimjaw", "Sharpfang", "Wildhowl", "Brokentusk" };
                case MobRankTier.B:
                    return new[] { "Scarred", "Cunning", "Restless", "Old" };
                case MobRankTier.C:
                    return new[] { "Mean", "Stubborn", "Wary", "Limping" };
                default:
                    return new[] { "Strange", "Quiet", "Lone" };
            }
        }

        public static float GetRankXPRewardMultiplier(MobRankTier tier)
        {
            switch (tier)
            {
                case MobRankTier.Nation: return 50.0f; // 750 XP — once-in-a-game payout
                case MobRankTier.SSS: return 25.0f;  // 375 XP
                case MobRankTier.SS: return 15.0f;   // 225 XP
                case MobRankTier.S: return 10.0f;    // 150 XP
                case MobRankTier.A: return 6.0f;     // 90 XP
                case MobRankTier.B: return 4.0f;     // 60 XP
                case MobRankTier.C: return 2.5f;     // 37 XP
                case MobRankTier.D: return 1.5f;     // 22 XP
                case MobRankTier.E: return 1.0f;     // 15 XP
                case MobRankTier.F: return 0.6f;     // 9 XP
                default: return 1.0f;
            }
        }
    }
}
