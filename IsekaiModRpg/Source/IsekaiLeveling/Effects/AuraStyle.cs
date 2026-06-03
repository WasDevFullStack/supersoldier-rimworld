using System.Collections.Generic;
using IsekaiLeveling.MobRanking;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Effects
{
    /// <summary>
    /// Per-pawn choice of which aura to display, set from the status tab.
    /// </summary>
    public enum AuraDisplayMode
    {
        Constellation, // default — the pawn's constellation-specific aura
        DefaultFlame,  // the original rank-based flame aura (ignores constellation)
        None,          // no aura at all
    }

    /// <summary>
    /// Secondary visual effect layered on top of (or replacing) the standard flame ring.
    /// Each constellation maps to one of these for its signature look.
    /// </summary>
    public enum AuraSecondaryEffect
    {
        None,
        EmberSparks,    // Knight — slow rising sparks
        GlyphRing,      // Mage — orbiting glyph motes at chest height
        LeafDrift,      // Ranger — leaves drifting outward
        Slashes,        // Duelist — brief horizontal slash flickers
        ForgeSparks,    // Crafter — continuous spark stream
        BeamPillar,     // Paladin — vertical light pillar + halo
        RuneRing,       // Sage — slow ankle-height rune ring
        BannerFlames,   // Leader — handled via flame tuning, no extra particles
        AshDrift,       // Survivor — ash motes drifting
        DarkCrackle,    // Berserker — brief inner dark flame crackles
        OrbsRising,     // Alchemist — bubbling colored orbs rising
        SwirlMotion,    // Beastmaster — flame ring rotates + pawprint motes
    }

    /// <summary>
    /// Per-constellation flame silhouette. Each value is a procedurally-generated
    /// alpha mask in AuraSystem (see GenerateShapeTexture).
    /// </summary>
    public enum AuraFlameShape
    {
        Wavy,       // default — original wavy teardrop (used for unclassed pawns)
        Shield,     // Knight — broad rounded dome
        SharpTip,   // Mage — wizard-hat point
        Arrow,      // Ranger — long thin tapered both ends
        Blade,      // Duelist — sword silhouette, sharp tip
        Anvil,      // Crafter — squat blocky base
        Cross,      // Paladin — vertical beam + horizontal crossbar
        Lotus,      // Sage — soft lotus petal
        Banner,     // Leader — triangular pennant (asymmetric: pole on one side)
        Ragged,     // Survivor — uneven jagged edges
        Lightning,  // Berserker — two-segment angular bolt (clean lightning silhouette)
        Flask,      // Alchemist — bulb + neck
        Claw,       // Beastmaster — curved sickle (asymmetric)
    }

    /// <summary>
    /// Per-constellation aura style definition.
    /// Multipliers are applied on top of the rank-based base values in AuraSystem.
    /// </summary>
    public sealed class AuraStyle
    {
        public Color primary;
        public Color accent;
        public float flameCountMul = 1f;
        public float flameHeightMul = 1f;
        public float flameWidthMul = 1f;
        public float jitterMul = 1f;       // amplifies sin-wave variance on flame phase
        public float pulseSpeedMul = 1f;
        /// <summary>
        /// Degrees-per-second the entire flame ring orbits around the pawn. 0 = no orbit
        /// (default). Used independently of secondary effect — Beastmaster and Leader both
        /// use this for "marching parade" / "pack circling" motion.
        /// </summary>
        public float swirlSpeedDeg = 0f;
        public bool noFlames = false;
        public AuraFlameShape flameShape = AuraFlameShape.Wavy;

        /// <summary>
        /// Tiered custom PNG textures (relative to Textures/, no extension). Allow a
        /// constellation to show a progressively more impressive silhouette as the pawn
        /// climbs rank. When ANY tier is set, the aura starts appearing from rank D
        /// instead of the global B threshold.
        ///
        /// Tier selection by rank:
        ///   D, C        → tier 1 (early silhouette — first appearance)
        ///   B, A        → tier 2 (mid-rank upgrade; falls back to tier 1 if not set)
        ///   S, SS, SSS  → tier 3 (final form; falls back to tier 2, then tier 1)
        ///
        /// Setting all three to the same path = single texture used at every rank.
        /// Setting only tier 1 = same texture used at every rank from D up.
        /// </summary>
        public string customTexturePathTier1 = null;
        public string customTexturePathTier2 = null;
        public string customTexturePathTier3 = null;

        /// <summary>
        /// If true, asymmetric flame shapes are mirrored for flames on the pawn's left
        /// half so the shape's "outward" side faces away from the pawn on both halves
        /// of the ring. Affects asymmetric procedural shapes (Banner, Claw) and any
        /// custom textures with left/right asymmetry.
        /// </summary>
        public bool flameMirrorOutward = false;

        /// <summary>
        /// When true, the custom texture is drawn as a single flat "magic circle" lying on
        /// the ground beneath the pawn (perspective-foreshortened + spinning) instead of a
        /// vertical flame ring. Spin speed uses <see cref="swirlSpeedDeg"/>. Secondary
        /// effects and the glow disc still draw on top. Requires a custom texture tier set.
        /// </summary>
        public bool groundCircle = false;
        /// <summary>Size multiplier for the ground circle (relative to the rank-scaled aura radius).</summary>
        public float groundCircleSizeMul = 1f;

        /// <summary>
        /// When true, the pawn's worn torso armor/clothing is re-drawn ON TOP of the pawn with
        /// an additive glow tint (the same MoteGlow effect the other auras use), matching the
        /// pawn's facing automatically. Layered on top of all other aura VFX. Glow intensity
        /// scales by rank tier (D/C → 1, B/A → 2, S+ → 3). Best for melee constellations.
        /// </summary>
        public bool armorGlow = false;
        /// <summary>Per-style intensity multiplier for the armor glow.</summary>
        public float armorGlowIntensityMul = 1f;

        /// <summary>
        /// Optional custom armor overlay texture (relative to Textures/, no extension). When set,
        /// this single image is drawn additively over the pawn (tinted + sized/faded by tier)
        /// INSTEAD of glowing the pawn's body/head/worn apparel. Requires <see cref="armorGlow"/>.
        /// </summary>
        public string armorGlowTexturePath = null;

        /// <summary>When false, the soft ground glow disc beneath the pawn is not drawn.</summary>
        public bool showGlowDisc = true;

        /// <summary>
        /// When true, short electric-arc VFX (frame-animated from the Electric clips) randomly
        /// flash centered on the pawn, tinted with the aura color via the additive shader (which
        /// also drops the clips' black backgrounds). Spawn frequency scales with rank tier.
        /// Enabled for all constellations by default; set false per-style to opt out.
        /// </summary>
        public bool electricVfx = true;

        /// <summary>
        /// When true, a slowly-rotating sunburst of radial "holy light" rays is drawn around the
        /// pawn, each ray independently fading in and out, tinted with the aura color. Adds
        /// movement to otherwise-static auras (used by Paladin).
        /// </summary>
        public bool lightRays = false;

        public AuraSecondaryEffect secondary = AuraSecondaryEffect.None;

        /// <summary>
        /// True if any tier has a custom texture path set.
        /// </summary>
        public bool HasAnyCustomTexture =>
            !string.IsNullOrEmpty(customTexturePathTier1) ||
            !string.IsNullOrEmpty(customTexturePathTier2) ||
            !string.IsNullOrEmpty(customTexturePathTier3);

        /// <summary>
        /// Minimum rank at which this style's aura should appear. Every chosen constellation
        /// shows from D rank for consistency (the no-constellation rank-only fallback keeps
        /// its B threshold via the caller). Custom-texture tier resolution still walks down
        /// tiers, and procedural shapes simply render at their rank-scaled size.
        /// </summary>
        public MobRankTier MinAuraRank => MobRankTier.D;

        /// <summary>
        /// Resolves the best custom texture path for a given rank, walking down tiers
        /// when the matching tier is null. Returns null if no custom textures are set.
        /// </summary>
        public string GetTexturePathForRank(MobRankTier rank)
        {
            // S+ : prefer tier 3, fall through to 2 then 1
            if (rank >= MobRankTier.S)
            {
                if (!string.IsNullOrEmpty(customTexturePathTier3)) return customTexturePathTier3;
                if (!string.IsNullOrEmpty(customTexturePathTier2)) return customTexturePathTier2;
                if (!string.IsNullOrEmpty(customTexturePathTier1)) return customTexturePathTier1;
                return null;
            }
            // B, A : prefer tier 2, fall through to tier 1
            if (rank >= MobRankTier.B)
            {
                if (!string.IsNullOrEmpty(customTexturePathTier2)) return customTexturePathTier2;
                if (!string.IsNullOrEmpty(customTexturePathTier1)) return customTexturePathTier1;
                return null;
            }
            // D, C : tier 1 only
            if (rank >= MobRankTier.D)
            {
                return customTexturePathTier1;
            }
            return null;
        }
    }

    /// <summary>
    /// Resolves a pawn's constellation to its aura style.
    /// Returns null if the pawn has no constellation assigned (caller should fall back to rank-only path).
    /// </summary>
    public static class AuraStyleResolver
    {
        // Keyed by treeClass / migrated assignedTree name (e.g. "Knight", "Mage", ...)
        private static readonly Dictionary<string, AuraStyle> StyleByTree = BuildStyles();

        public static AuraStyle Resolve(Pawn pawn)
        {
            if (pawn == null) return null;
            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null) return null;

            // Multiclass: honor the player's chosen constellation if they've entered that tree;
            // otherwise fall back to the primary assigned tree.
            string tree = comp.auraConstellation;
            if (!string.IsNullOrEmpty(tree) && comp.passiveTree?.HasEnteredTreeByName(tree) != true)
                tree = null;
            if (string.IsNullOrEmpty(tree))
                tree = comp.passiveTree?.assignedTree;

            if (string.IsNullOrEmpty(tree)) return null;
            StyleByTree.TryGetValue(tree, out var style);
            return style;
        }

        public static AuraStyle ResolveByName(string treeName)
        {
            if (string.IsNullOrEmpty(treeName)) return null;
            StyleByTree.TryGetValue(treeName, out var style);
            return style;
        }

        private static Dictionary<string, AuraStyle> BuildStyles()
        {
            var d = new Dictionary<string, AuraStyle>();

            // Knight — golden melee aura: the classic DBZ-style Wavy flame ring + glow disc
            // + ember sparks, PLUS a glowing-armor overlay (gold glow over the pawn's body,
            // head and worn armor) scaling with rank tier.
            d["Knight"] = new AuraStyle
            {
                primary = new Color(1.00f, 0.85f, 0.40f, 1f),
                accent = new Color(1.00f, 0.95f, 0.70f, 1f),
                flameCountMul = 1.0f,
                flameWidthMul = 1.0f,
                flameHeightMul = 1.0f,
                jitterMul = 1.0f,
                pulseSpeedMul = 1.0f,
                flameShape = AuraFlameShape.Wavy,   // the original "usual" flames
                showGlowDisc = true,
                armorGlow = true,
                armorGlowIntensityMul = 1.4f,
                armorGlowTexturePath = "Auras/knight_armor",
                secondary = AuraSecondaryEffect.EmberSparks,
            };

            // Mage — deep violet core, cyan accent. Spinning magic circle on the ground
            // (3-tier custom PNGs) + the orbiting glyph ring floating above it.
            d["Mage"] = new AuraStyle
            {
                primary = new Color(0.55f, 0.20f, 0.95f, 1f),
                accent = new Color(0.40f, 0.85f, 1.00f, 1f),
                pulseSpeedMul = 1.0f,
                swirlSpeedDeg = 18f,    // slow mystical rotation of the circle
                groundCircle = true,
                groundCircleSizeMul = 1.0f,
                customTexturePathTier1 = "Auras/mage_tier1",
                customTexturePathTier2 = "Auras/mage_tier2",
                customTexturePathTier3 = "Auras/mage_tier3",
                secondary = AuraSecondaryEffect.GlyphRing,
            };

            // Ranger — forest green, slim arrow/leaf-blades, leaf drift
            d["Ranger"] = new AuraStyle
            {
                primary = new Color(0.30f, 0.75f, 0.35f, 1f),
                accent = new Color(0.95f, 0.95f, 0.55f, 1f),
                flameCountMul = 1.1f,
                flameWidthMul = 0.85f,
                flameHeightMul = 0.85f,
                jitterMul = 1.4f,
                pulseSpeedMul = 1.3f,
                flameShape = AuraFlameShape.Arrow,
                secondary = AuraSecondaryEffect.LeafDrift,
            };

            // Duelist — crimson, sword-blade silhouettes spinning around the pawn.
            // Fast orbital rotation matches the duelist's quick agile combat theme.
            d["Duelist"] = new AuraStyle
            {
                primary = new Color(0.90f, 0.20f, 0.25f, 1f),
                accent = new Color(0.95f, 0.95f, 0.95f, 1f),
                flameCountMul = 1.0f,
                flameWidthMul = 0.85f,
                flameHeightMul = 1.15f,
                jitterMul = 1.8f,
                pulseSpeedMul = 1.6f,
                swirlSpeedDeg = 80f,    // fast spin — quick-blades whirling
                customTexturePathTier1 = "Auras/duelist_tier1",
                customTexturePathTier2 = "Auras/duelist_tier2",
                customTexturePathTier3 = "Auras/duelist_tier3",
                secondary = AuraSecondaryEffect.None,
            };

            // Crafter — copper-orange, I-beam anvil silhouettes, forge sparks
            d["Crafter"] = new AuraStyle
            {
                primary = new Color(0.95f, 0.55f, 0.20f, 1f),
                accent = new Color(1.00f, 0.85f, 0.45f, 1f),
                flameCountMul = 0.9f,
                flameWidthMul = 1.0f,
                flameHeightMul = 0.85f,
                jitterMul = 0.7f,
                pulseSpeedMul = 1.0f,
                customTexturePathTier1 = "Auras/crafter_tier1",
                customTexturePathTier2 = "Auras/crafter_tier2",
                customTexturePathTier3 = "Auras/crafter_tier3",
                secondary = AuraSecondaryEffect.ForgeSparks,
            };

            // Paladin — holy gold + white, 3-tier custom PNGs, beam pillar + halo
            d["Paladin"] = new AuraStyle
            {
                primary = new Color(1.00f, 0.90f, 0.45f, 1f),
                accent = new Color(1.00f, 1.00f, 0.95f, 1f),
                flameCountMul = 0.6f,
                flameWidthMul = 1.1f,
                flameHeightMul = 1.0f,
                jitterMul = 0.4f,
                pulseSpeedMul = 0.9f,
                customTexturePathTier1 = "Auras/paladin_tier1",
                customTexturePathTier2 = "Auras/paladin_tier2",
                customTexturePathTier3 = "Auras/paladin_tier3",
                secondary = AuraSecondaryEffect.BeamPillar,
            };

            // Sage — pale teal, soft lotus-petal flames + rune ring at ankle
            // Sage — pale teal + soft white. Slow spinning magic circle on the ground
            // (3-tier custom PNGs) + the ankle-height rune ring floating above it.
            d["Sage"] = new AuraStyle
            {
                primary = new Color(0.45f, 0.90f, 0.85f, 1f),
                accent = new Color(0.95f, 1.00f, 0.95f, 1f),
                pulseSpeedMul = 0.6f,
                swirlSpeedDeg = 12f,    // very slow, serene rotation
                groundCircle = true,
                groundCircleSizeMul = 1.0f,
                customTexturePathTier1 = "Auras/sage_tier1",
                customTexturePathTier2 = "Auras/sage_tier2",
                customTexturePathTier3 = "Auras/sage_tier3",
                secondary = AuraSecondaryEffect.RuneRing,
            };

            // Leader — royal purple + gold, triangular pennant flags marching around the pawn.
            // Asymmetric (pole on one side), mirrored for the pawn's left half. Calm internal
            // animation but the whole ring orbits, like a banner-bearing parade circling.
            // Leader — royal purple + gold, 3-tier custom PNG banners marching around the pawn.
            // Few, wide, tall flame slots; the whole ring slowly orbits. Mirrored so an
            // asymmetric banner faces consistently on both halves of the ring.
            d["Leader"] = new AuraStyle
            {
                primary = new Color(0.55f, 0.30f, 0.80f, 1f),
                accent = new Color(1.00f, 0.85f, 0.35f, 1f),
                flameCountMul = 0.5f,
                flameWidthMul = 1.7f,
                flameHeightMul = 1.4f,
                jitterMul = 0.5f,
                pulseSpeedMul = 0.7f,
                swirlSpeedDeg = 25f,     // slow orbit — the ring rotates around the pawn
                customTexturePathTier1 = "Auras/leader_tier1",
                customTexturePathTier2 = "Auras/leader_tier2",
                customTexturePathTier3 = "Auras/leader_tier3",
                flameMirrorOutward = true,
                secondary = AuraSecondaryEffect.BannerFlames,
            };

            // Survivor — earthy brown / ash, ragged uneven torch flames, ash drift
            d["Survivor"] = new AuraStyle
            {
                primary = new Color(0.55f, 0.40f, 0.30f, 1f),
                accent = new Color(0.70f, 0.70f, 0.70f, 1f),
                flameCountMul = 1.2f,
                flameWidthMul = 0.9f,
                flameHeightMul = 0.7f,
                jitterMul = 1.7f,
                pulseSpeedMul = 0.8f,
                customTexturePathTier1 = "Auras/survivor_tier1",
                customTexturePathTier2 = "Auras/survivor_tier2",
                customTexturePathTier3 = "Auras/survivor_tier3",
                secondary = AuraSecondaryEffect.AshDrift,
            };

            // Berserker — blood red + black, 3-tier custom PNGs (128x256 portrait) + dark crackle.
            // D rank shows tier 1, B rank upgrades to tier 2, S rank graduates to tier 3.
            d["Berserker"] = new AuraStyle
            {
                primary = new Color(0.85f, 0.10f, 0.10f, 1f),
                accent = new Color(0.20f, 0.05f, 0.05f, 1f),
                flameCountMul = 1.2f,
                flameWidthMul = 0.85f,
                flameHeightMul = 1.3f,
                jitterMul = 2.5f,
                pulseSpeedMul = 1.8f,
                swirlSpeedDeg = 55f,    // textures orbit around the pawn (like Beastmaster)
                customTexturePathTier1 = "Auras/berserker_tier1",
                customTexturePathTier2 = "Auras/berserker_tier2",
                customTexturePathTier3 = "Auras/berserker_tier3",
                secondary = AuraSecondaryEffect.DarkCrackle,
            };

            // Alchemist — acid green + violet, flask-shaped flames (bulb + neck), rising orbs
            d["Alchemist"] = new AuraStyle
            {
                primary = new Color(0.55f, 0.95f, 0.30f, 1f),
                accent = new Color(0.80f, 0.40f, 0.95f, 1f),
                flameCountMul = 1.0f,
                flameWidthMul = 1.05f,
                flameHeightMul = 0.85f,
                jitterMul = 1.2f,
                pulseSpeedMul = 1.2f,
                customTexturePathTier1 = "Auras/alchemist_tier1",
                customTexturePathTier2 = "Auras/alchemist_tier2",
                customTexturePathTier3 = "Auras/alchemist_tier3",
                secondary = AuraSecondaryEffect.OrbsRising,
            };

            // Beastmaster — amber + teal, 3-tier custom PNGs (128x256 portrait, matches engine
            // aspect so no width/height compensation needed). D rank shows tier 1, B rank
            // upgrades to tier 2, S rank graduates to the final tier 3 silhouette.
            d["Beastmaster"] = new AuraStyle
            {
                primary = new Color(1.00f, 0.65f, 0.20f, 1f),
                accent = new Color(0.30f, 0.85f, 0.85f, 1f),
                flameCountMul = 1.0f,
                flameWidthMul = 1.0f,
                flameHeightMul = 1.0f,
                jitterMul = 1.0f,
                pulseSpeedMul = 1.2f,
                swirlSpeedDeg = 60f,
                customTexturePathTier1 = "Auras/beastmaster_tier1",
                customTexturePathTier2 = "Auras/beastmaster_tier2",
                customTexturePathTier3 = "Auras/beastmaster_tier3",
                flameMirrorOutward = false,    // PNGs assumed symmetric — flip on if asymmetry shows
                secondary = AuraSecondaryEffect.SwirlMotion,
            };

            return d;
        }
    }
}
