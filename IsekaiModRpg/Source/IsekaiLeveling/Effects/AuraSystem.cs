using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using IsekaiLeveling.MobRanking;

namespace IsekaiLeveling.Effects
{
    /// <summary>
    /// Manages DBZ-style aura visual effects for drafted/combat pawns.
    /// Vertical energy flames + glow.
    ///
    /// Rank gates *whether* the aura shows and drives base size/intensity.
    /// Constellation drives palette + flame behavior + a unique secondary effect.
    ///
    /// RANK THRESHOLDS:
    /// F-D: No aura
    /// C: No aura
    /// B: White/Silver aura (rank fallback)
    /// A: Blue aura (rank fallback)
    /// S: Gold aura (rank fallback)
    /// SS: Orange/Red aura (rank fallback)
    /// SSS: Violet/Purple aura (rank fallback)
    ///
    /// When a pawn has a constellation, palette comes from AuraStyleResolver and the
    /// rank palette tints the accent color so high ranks shimmer regardless of class.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AuraSystem
    {
        // Textures
        private static readonly Texture2D GlowTexture;
        private static readonly Texture2D MoteTexture;     // small radial dot — sparks, orbs, runes
        private static readonly Texture2D LeafTexture;     // stretched teardrop — leaves, ash
        private static readonly Texture2D RayTextureFallback; // procedural shaft, used if sunray.png missing
        private static Texture2D _sunrayTex;               // lazily-loaded Auras/sunray.png
        private static bool _sunrayTexInit;

        // One flame texture per shape (procedural, 64x128 alpha mask).
        // Mirrored variant exists for asymmetric shapes — used instead of negative scale.x
        // to avoid backface-culling issues when scale flips triangle winding.
        private static readonly Dictionary<AuraFlameShape, Texture2D> FlameTextures
            = new Dictionary<AuraFlameShape, Texture2D>();
        private static readonly Dictionary<AuraFlameShape, Texture2D> FlameTexturesMirrored
            = new Dictionary<AuraFlameShape, Texture2D>();

        // Material caches (one per (color, alpha quantum) per texture)
        // FlameMaterials is keyed by (shape, color) so each silhouette gets its own material set.
        private static readonly Dictionary<AuraFlameShape, Dictionary<Color, Material>> FlameMaterialsByShape
            = new Dictionary<AuraFlameShape, Dictionary<Color, Material>>();
        private static readonly Dictionary<AuraFlameShape, Dictionary<Color, Material>> FlameMaterialsByShapeMirrored
            = new Dictionary<AuraFlameShape, Dictionary<Color, Material>>();

        // Custom (PNG-loaded) flame textures and their material caches, keyed by texture path.
        // Loaded lazily on first request so missing textures don't crash startup.
        private static readonly Dictionary<string, Texture2D> CustomFlameTextures
            = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> CustomFlameTexturesMirrored
            = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Dictionary<Color, Material>> CustomFlameMaterials
            = new Dictionary<string, Dictionary<Color, Material>>();
        private static readonly Dictionary<string, Dictionary<Color, Material>> CustomFlameMaterialsMirrored
            = new Dictionary<string, Dictionary<Color, Material>>();

        private static readonly Dictionary<Color, Material> GlowMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> MoteMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> LeafMaterials = new Dictionary<Color, Material>();

        // Armor-glow material cache, keyed by (apparel texture, tint color). Lets us re-draw
        // a pawn's worn apparel as an additive glow without allocating a Material per frame.
        private static readonly Dictionary<Texture, Dictionary<Color, Material>> ArmorGlowMaterials
            = new Dictionary<Texture, Dictionary<Color, Material>>();


        // Per-pawn animation state
        private static readonly Dictionary<int, AuraState> PawnAuraStates = new Dictionary<int, AuraState>();

        // ── Electric VFX (frame-animated clips, extracted from the .mp4 sources) ──
        private static readonly string[] ElectricClipPaths =
        {
            "Auras/Electric/electric1",
            "Auras/Electric/electric2",
            "Auras/Electric/electric3",
            "Auras/Electric/electric4",
        };
        // Loaded lazily on first use: clip index → ordered list of frame textures.
        private static readonly Dictionary<int, List<Texture2D>> ElectricFrames
            = new Dictionary<int, List<Texture2D>>();
        private const float ELECTRIC_FPS = 30f;          // matches extraction fps

        // Constants
        private const float BASE_FLAME_HEIGHT = 2.0f;
        private const float FLAME_WIDTH = 0.5f;
        private const int MAX_PARTICLES_PER_PAWN = 32;
        // North-south foreshortening for anything drawn flat on the ground. Matches the
        // 0.55 ellipse factor the flame ring uses for its circular layout, so ground
        // circles read as lying on the same perspective plane.
        private const float GROUND_PERSPECTIVE = 0.55f;

        private struct Particle
        {
            public Vector3 pos;
            public Vector3 vel;
            public float life;      // remaining seconds
            public float maxLife;   // initial seconds (for alpha curve)
            public float size;
            public byte colorIndex; // 0 = primary, 1 = accent
            // Stable per-particle rotation phase set on spawn. Decouples rotation from
            // loop-index so culling/repacking other particles doesn't make this one
            // jump. Used for leaf/ash drift.
            public float rotSeed;
            public float rotSpeed;  // degrees per second
        }

        private struct LightRay
        {
            public bool active;
            public float xOff;     // horizontal offset from pawn (world X)
            public float start;    // spawn time
            public float duration; // total lifetime
            public float height;   // shaft height
            public float width;    // shaft width
            public float tilt;     // slight lean (degrees)
        }

        private class AuraState
        {
            public float[] FlamePhases;
            public Particle[] Particles = new Particle[MAX_PARTICLES_PER_PAWN];
            public int ParticleCount = 0;
            public float LastSpawnTime;
            public float LastSlashTime;

            // Electric-arc VFX state (frame animation). -1 = inactive.
            public int ElectricClip = -1;
            public float ElectricStart;
            public Vector3 ElectricOffset;
            public float ElectricScale;
            public byte ElectricColorIdx;

            // God-ray light shafts (allocated lazily — only styles with lightRays use it).
            public LightRay[] Rays;
            public float LastRaySpawn;

            public AuraState(int flameCount)
            {
                FlamePhases = new float[Mathf.Max(1, flameCount)];
                for (int i = 0; i < FlamePhases.Length; i++)
                {
                    FlamePhases[i] = Rand.Range(0f, Mathf.PI * 2f);
                }
            }

            public void EnsureFlameCount(int flameCount)
            {
                int n = Mathf.Max(1, flameCount);
                if (FlamePhases.Length == n) return;
                FlamePhases = new float[n];
                for (int i = 0; i < n; i++)
                    FlamePhases[i] = Rand.Range(0f, Mathf.PI * 2f);
            }
        }

        static AuraSystem()
        {
            GlowTexture = GenerateGlowTexture(128);
            MoteTexture = GenerateMoteTexture(16);
            LeafTexture = GenerateLeafTexture(32, 16);
            RayTextureFallback = GenerateRayTexture(48, 160);

            // Generate one alpha-mask texture per flame shape (and a mirrored variant for asymmetric ones).
            foreach (AuraFlameShape shape in System.Enum.GetValues(typeof(AuraFlameShape)))
            {
                FlameTextures[shape] = GenerateShapeTexture(64, 128, shape, mirror: false);
                FlameMaterialsByShape[shape] = new Dictionary<Color, Material>();
                if (IsAsymmetricShape(shape))
                {
                    FlameTexturesMirrored[shape] = GenerateShapeTexture(64, 128, shape, mirror: true);
                    FlameMaterialsByShapeMirrored[shape] = new Dictionary<Color, Material>();
                }
            }

            Log.Message("[Isekai Leveling] DBZ-style aura system initialized.");
        }

        /// <summary>
        /// Generate an alpha-mask texture for a flame shape. Texture coords:
        ///   ny in [0,1]: 0 = bottom of flame, 1 = top
        ///   nx in [-1,+1]: horizontal position relative to center
        /// Each shape function returns 0..1 alpha for that (nx, ny).
        /// </summary>
        private static Texture2D GenerateShapeTexture(int width, int height, AuraFlameShape shape, bool mirror)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float halfW = width * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float ny = (float)y / (height - 1);
                for (int x = 0; x < width; x++)
                {
                    float nx = (x - halfW) / halfW;
                    if (mirror) nx = -nx;
                    float a = ShapeAlpha(shape, nx, ny);
                    if (a < 0f) a = 0f;
                    else if (a > 1f) a = 1f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private static bool IsAsymmetricShape(AuraFlameShape shape)
        {
            return shape == AuraFlameShape.Banner || shape == AuraFlameShape.Claw;
        }

        private static float ShapeAlpha(AuraFlameShape shape, float nx, float ny)
        {
            switch (shape)
            {
                case AuraFlameShape.Wavy: return WavyAlpha(nx, ny);
                case AuraFlameShape.Shield: return ShieldAlpha(nx, ny);
                case AuraFlameShape.SharpTip: return SharpTipAlpha(nx, ny);
                case AuraFlameShape.Arrow: return ArrowAlpha(nx, ny);
                case AuraFlameShape.Blade: return BladeAlpha(nx, ny);
                case AuraFlameShape.Anvil: return AnvilAlpha(nx, ny);
                case AuraFlameShape.Cross: return CrossAlpha(nx, ny);
                case AuraFlameShape.Lotus: return LotusAlpha(nx, ny);
                case AuraFlameShape.Banner: return BannerAlpha(nx, ny);
                case AuraFlameShape.Ragged: return RaggedAlpha(nx, ny);
                case AuraFlameShape.Lightning: return LightningAlpha(nx, ny);
                case AuraFlameShape.Flask: return FlaskAlpha(nx, ny);
                case AuraFlameShape.Claw: return ClawAlpha(nx, ny);
                default: return WavyAlpha(nx, ny);
            }
        }

        // ───────────────────────── Shape alpha functions ─────────────────────────
        // All take nx in [-1,+1], ny in [0,1] (0 = bottom, 1 = top of flame).

        // Original flame: wavy teardrop, used as Wavy fallback for unclassed pawns.
        private static float WavyAlpha(float nx, float ny)
        {
            float halfW = (1f - ny * ny) * 0.9f;
            float waveOffset = Mathf.Sin(ny * Mathf.PI * 4f) * 0.16f;
            float dist = Mathf.Abs(nx - waveOffset);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edgeFade = 1f - (dist / halfW);
            float topFade = 1f - (ny * ny * 0.8f);
            return edgeFade * edgeFade * topFade;
        }

        // Knight — broad rounded dome (rectangle base + half-ellipse top)
        private static float ShieldAlpha(float nx, float ny)
        {
            float halfW;
            if (ny < 0.4f)
            {
                halfW = 0.85f;
            }
            else
            {
                float t = (ny - 0.4f) / 0.6f;
                halfW = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) * 0.85f;
            }
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            return edge * edge;
        }

        // Mage — wizard hat: wide base, sharp pointed tip
        private static float SharpTipAlpha(float nx, float ny)
        {
            float halfW = Mathf.Pow(1f - ny, 1.4f) * 0.7f;
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            return edge;
        }

        // Ranger — long thin tapered both ends, like an arrow/leaf-blade
        private static float ArrowAlpha(float nx, float ny)
        {
            float halfW = Mathf.Sin(ny * Mathf.PI) * 0.40f;
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            return edge;
        }

        // Duelist — sword blade: gradual taper to ~0.85, then sharp tip
        private static float BladeAlpha(float nx, float ny)
        {
            float halfW;
            if (ny < 0.85f)
            {
                halfW = 0.32f * (1f - ny / 0.85f * 0.55f);
            }
            else
            {
                halfW = 0.32f * 0.45f * (1f - (ny - 0.85f) / 0.15f);
            }
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            return Mathf.Pow(edge, 0.7f);
        }

        // Crafter — anvil silhouette: wide flat top slab overhanging a chunky rectangular
        // body. The overhang under the face is the key feature — without it, the shape
        // reads as a house. With a narrow waist, it reads as a wine glass. Body must be
        // chunky to avoid both.
        private static float AnvilAlpha(float nx, float ny)
        {
            float halfW;
            if (ny >= 0.85f)
            {
                // Top face — wide thin slab (the work surface)
                halfW = 0.95f;
            }
            else if (ny >= 0.75f)
            {
                // Sharp shoulder under the face — creates the visible overhang
                float t = (ny - 0.75f) / 0.10f; // 0..1
                halfW = Mathf.Lerp(0.55f, 0.95f, t);
            }
            else if (ny >= 0.08f)
            {
                // Chunky trapezoidal body — slightly wider at bottom, NOT a thin waist
                float t = (ny - 0.08f) / 0.67f; // 0..1 going up the body
                halfW = Mathf.Lerp(0.65f, 0.55f, t);
            }
            else
            {
                // Small foot flare at the very bottom
                halfW = 0.65f;
            }
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            // 0.6 multiplier: anvil silhouette is solid/chunky enough that full alpha
            // reads as a heavy pillar. Knocking it down lets it read as flame-shaped.
            return Mathf.Pow(edge, 0.55f) * 0.6f;
        }

        // Paladin — vertical beam + horizontal crossbar (~upper third)
        private static float CrossAlpha(float nx, float ny)
        {
            float vBeam = 0f;
            if (Mathf.Abs(nx) < 0.18f)
                vBeam = 1f - Mathf.Abs(nx) / 0.18f;

            float hBeam = 0f;
            if (ny > 0.55f && ny < 0.78f && Mathf.Abs(nx) < 0.7f)
            {
                float yFade = 1f - Mathf.Abs(ny - 0.665f) / 0.115f;
                float xFade = 1f - Mathf.Abs(nx) / 0.7f;
                hBeam = yFade * xFade;
            }
            return Mathf.Max(vBeam, hBeam);
        }

        // Sage — soft lotus petal: pointed tip, rounded body
        private static float LotusAlpha(float nx, float ny)
        {
            float halfW = Mathf.Sin(Mathf.Pow(ny, 0.6f) * Mathf.PI) * 0.55f;
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            return Mathf.Sqrt(edge);
        }

        // Leader — pennant flag: vertical pole on left, triangular flag tapering to a point on right.
        // Asymmetric — "outward" is texture +X (right). Mirrored for left-half flames.
        private static float BannerAlpha(float nx, float ny)
        {
            const float poleX = -0.55f;

            // Pole: full-height vertical line, slightly thicker for visibility
            float poleAlpha = 0f;
            float poleDist = Mathf.Abs(nx - poleX);
            if (poleDist < 0.10f)
                poleAlpha = 1f - poleDist / 0.10f;

            // Flag: a triangle attached to the pole, occupying the upper ~75% of height.
            // - Bottom edge: flat horizontal at ny = 0.20
            // - Top edge: flat horizontal at ny = 0.95
            // - Right edge: tapers from far-right at the bottom to nearly the pole at the top,
            //   forming a downward-pointing pennant silhouette.
            float flagAlpha = 0f;
            if (ny > 0.20f && ny < 0.95f)
            {
                float ft = (ny - 0.20f) / 0.75f;        // 0..1 within flag region
                float rightEdge = Mathf.Lerp(0.85f, -0.30f, ft); // wide at bottom → near pole at top
                if (nx > poleX + 0.05f && nx < rightEdge)
                {
                    float spanW = Mathf.Max(0.01f, rightEdge - (poleX + 0.05f));
                    float along = (nx - (poleX + 0.05f)) / spanW;
                    // Brighter near pole, fading toward fly-end. Capped so the flag
                    // reads as cloth (translucent) rather than competing with the pole.
                    flagAlpha = (1f - along * 0.4f) * 0.55f;
                }
            }
            return Mathf.Max(poleAlpha, flagAlpha);
        }

        // Survivor — ragged uneven torch with multi-frequency noise
        private static float RaggedAlpha(float nx, float ny)
        {
            float baseW = (1f - ny * ny) * 0.7f;
            float noise = (Mathf.Sin(ny * 17f + 1.3f) * 0.6f
                          + Mathf.Cos(ny * 11f + 2.1f) * 0.4f) * 0.13f;
            float halfW = baseW + noise;
            if (halfW < 0.01f) return 0f;
            float wave = Mathf.Sin(ny * 19f) * 0.09f;
            float dist = Mathf.Abs(nx - wave);
            if (dist >= halfW) return 0f;
            float edge = 1f - (dist / halfW);
            return edge * edge;
        }

        // Berserker — clean angular lightning bolt as a 6-vertex polygon:
        //
        //                 P1 (top-right corner)
        //                / |
        //               /  |
        //              /   |
        //             P8   P2  (upper segment ends, top-left of the jog)
        //              ↘   ↘
        //               ↘   ↘     ← horizontal step connects the two segments
        //                P7   P3  (top-right of the lower segment)
        //                |   /
        //                |  /
        //                | /
        //                P4 (bottom-left corner)
        //
        // Each ny is between two parallel-ish edges (left + right). The middle band
        // (jog region) is the slanted strip that connects the upper and lower bars
        // — without it, the two bars look disconnected.
        private static float LightningAlpha(float nx, float ny)
        {
            // Polygon corners (centerline thickness 0.18 perpendicular ≈ ±0.18 horizontal):
            //   P1 = (+0.48, 1.00) top-right (top edge runs flat to P8)
            //   P8 = (+0.12, 1.00) top-left
            //   P2 = (+0.03, 0.55) upper segment's right-end at the jog
            //   P7 = (-0.33, 0.55) upper segment's left-end at the jog
            //   P3 = (+0.33, 0.45) lower segment's right-start at the jog
            //   P6 = (-0.03, 0.45) lower segment's left-start at the jog
            //   P4 = (-0.12, 0.00) bottom-right
            //   P5 = (-0.48, 0.00) bottom-left

            float leftEdge, rightEdge;

            if (ny >= 0.55f)
            {
                // Upper segment: edges go from P2/P7 (at ny=0.55) up to P1/P8 (at ny=1.00)
                float t = (ny - 0.55f) / 0.45f;
                rightEdge = Mathf.Lerp(0.03f, 0.48f, t);
                leftEdge = Mathf.Lerp(-0.33f, 0.12f, t);
            }
            else if (ny >= 0.45f)
            {
                // Jog region: horizontal step linking upper and lower bars.
                // At ny=0.55 (top of jog): right=0.03, left=-0.33  (= P2, P7)
                // At ny=0.45 (bottom of jog): right=0.33, left=-0.03 (= P3, P6)
                float t = (ny - 0.45f) / 0.10f; // 0 at bottom, 1 at top
                rightEdge = Mathf.Lerp(0.33f, 0.03f, t);
                leftEdge = Mathf.Lerp(-0.03f, -0.33f, t);
            }
            else
            {
                // Lower segment: edges go from P4/P5 (at ny=0.00) up to P3/P6 (at ny=0.45)
                float t = ny / 0.45f;
                rightEdge = Mathf.Lerp(-0.12f, 0.33f, t);
                leftEdge = Mathf.Lerp(-0.48f, -0.03f, t);
            }

            if (nx < leftEdge || nx > rightEdge) return 0f;

            float halfW = (rightEdge - leftEdge) * 0.5f;
            if (halfW < 0.001f) return 0f;
            float center = (rightEdge + leftEdge) * 0.5f;
            float dist = Mathf.Abs(nx - center);
            float edge = 1f - dist / halfW;
            return Mathf.Pow(edge, 0.6f);
        }

        // Alchemist — round bulb + thin neck + small lip flare
        private static float FlaskAlpha(float nx, float ny)
        {
            float halfW;
            if (ny < 0.55f)
            {
                float t = (ny - 0.275f) / 0.275f; // -1..+1
                halfW = Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)) * 0.78f;
            }
            else if (ny < 0.88f)
            {
                halfW = 0.22f;
            }
            else
            {
                halfW = 0.22f + (ny - 0.88f) * 1.5f;
                halfW = Mathf.Min(halfW, 0.45f);
            }
            float dist = Mathf.Abs(nx);
            if (dist >= halfW || halfW < 0.001f) return 0f;
            float edge = 1f - (dist / halfW);
            return edge;
        }

        // Beastmaster — curved sickle/claw, asymmetric (curves to texture +X side)
        private static float ClawAlpha(float nx, float ny)
        {
            float center = Mathf.Sin(ny * Mathf.PI * 0.5f) * 0.45f;
            float halfW = (1f - Mathf.Pow(ny, 1.4f)) * 0.32f;
            if (halfW < 0.01f) return 0f;
            float dist = Mathf.Abs(nx - center);
            if (dist >= halfW) return 0f;
            float edge = 1f - (dist / halfW);
            return Mathf.Pow(edge, 0.85f);
        }

        private static Texture2D GenerateGlowTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float maxRadius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float normalizedDist = distance / maxRadius;

                    float alpha = 0f;
                    if (normalizedDist < 1f)
                    {
                        alpha = 1f - normalizedDist;
                        alpha = alpha * alpha * alpha; // Cubic falloff for soft glow
                    }

                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Small radial dot. Used for sparks, orbs, glyph/rune motes, pawprint motes.
        /// Slightly sharper falloff than the glow texture.
        /// </summary>
        private static Texture2D GenerateMoteTexture(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float maxRadius = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float normalizedDist = distance / maxRadius;

                    float alpha = 0f;
                    if (normalizedDist < 1f)
                    {
                        alpha = 1f - normalizedDist;
                        alpha = alpha * alpha; // Quadratic falloff — punchier than glow
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        /// <summary>
        /// Stretched teardrop. Used for leaves and ash drifts.
        /// </summary>
        private static Texture2D GenerateLeafTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float centerY = height / 2f;

            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width; // 0..1 along length
                // teardrop width profile: thin at both ends, fattest ~25% from one tip
                float profile = Mathf.Sin(Mathf.Pow(nx, 0.6f) * Mathf.PI);
                float halfWidth = profile * (height * 0.45f);

                for (int y = 0; y < height; y++)
                {
                    float dy = Mathf.Abs(y - centerY);
                    float alpha = 0f;
                    if (dy < halfWidth && halfWidth > 0.001f)
                    {
                        float edge = 1f - (dy / halfWidth);
                        alpha = edge * edge;
                    }
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        /// <summary>
        /// Soft vertical light shaft for god rays. Narrow-ish soft sides, brightest in the body,
        /// fading toward the top (the "source" dissipates) and tapering at the very bottom.
        /// y=0 is the bottom (ground end), y=height-1 is the top.
        /// </summary>
        private static Texture2D GenerateRayTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float cx = width / 2f;

            for (int y = 0; y < height; y++)
            {
                float vy = (float)y / (height - 1); // 0 bottom → 1 top
                // Vertical envelope: quick fade-in at the bottom, gentle fade-out toward top.
                float vfade;
                if (vy < 0.12f) vfade = vy / 0.12f;
                else vfade = Mathf.Pow(1f - (vy - 0.12f) / 0.88f, 1.3f);

                for (int x = 0; x < width; x++)
                {
                    float hx = (x - cx) / cx; // -1..1
                    float dist = Mathf.Abs(hx);
                    float h = dist < 1f ? (1f - dist) : 0f;
                    h = h * h; // soft edges
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, h * vfade));
                }
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        /// <summary>
        /// Get aura color based on rank (used for fallback path AND for tinting accent on classed pawns)
        /// </summary>
        public static Color GetAuraColorForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.SSS: return new Color(0.75f, 0.35f, 1f, 1f);
                case MobRankTier.SS: return new Color(1f, 0.45f, 0.2f, 1f);
                case MobRankTier.S: return new Color(1f, 0.85f, 0.25f, 1f);
                case MobRankTier.A: return new Color(0.35f, 0.65f, 1f, 1f);
                case MobRankTier.B: return new Color(0.95f, 0.95f, 1f, 1f);
                default: return Color.clear;
            }
        }

        public static Color GetAuraColor(int level)
        {
            return GetAuraColorForRank(GetRankFromLevel(level));
        }

        private static MobRankTier GetRankFromLevel(int level)
        {
            if (level >= 401) return MobRankTier.SSS;
            if (level >= 201) return MobRankTier.SS;
            if (level >= 101) return MobRankTier.S;
            if (level >= 51) return MobRankTier.A;
            if (level >= 26) return MobRankTier.B;
            if (level >= 18) return MobRankTier.C;
            if (level >= 11) return MobRankTier.D;
            if (level >= 6) return MobRankTier.E;
            return MobRankTier.F;
        }

        public static Color GetInnerColorForRank(MobRankTier rank)
        {
            return Color.Lerp(GetAuraColorForRank(rank), Color.white, 0.6f);
        }

        public static Color GetInnerColor(int level)
        {
            return GetInnerColorForRank(GetRankFromLevel(level));
        }

        public static int GetFlameCountForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.SSS: return 16;
                case MobRankTier.SS: return 14;
                case MobRankTier.S: return 12;
                case MobRankTier.A: return 10;
                case MobRankTier.B: return 8;
                case MobRankTier.C: return 6;
                case MobRankTier.D: return 4;
                default: return 0;
            }
        }

        public static int GetFlameCount(int level) => GetFlameCountForRank(GetRankFromLevel(level));

        public static float GetFlameHeightForRank(MobRankTier rank, int level)
        {
            float height;
            switch (rank)
            {
                case MobRankTier.SSS: height = BASE_FLAME_HEIGHT * (4.5f + (level - 401) * 0.015f); break;
                case MobRankTier.SS: height = BASE_FLAME_HEIGHT * (3.2f + (level - 201) * 0.008f); break;
                case MobRankTier.S: height = BASE_FLAME_HEIGHT * (2.4f + (level - 101) * 0.01f); break;
                case MobRankTier.A: height = BASE_FLAME_HEIGHT * (1.7f + (level - 51) * 0.014f); break;
                case MobRankTier.B: height = BASE_FLAME_HEIGHT * (1.1f + (level - 26) * 0.025f); break;
                case MobRankTier.C: height = BASE_FLAME_HEIGHT * (1.0f + (level - 18) * 0.012f); break;
                case MobRankTier.D: height = BASE_FLAME_HEIGHT * (0.85f + (level - 11) * 0.012f); break;
                default: return 0f;
            }
            return Mathf.Min(height, 15f);
        }

        public static float GetFlameHeight(int level) => GetFlameHeightForRank(GetRankFromLevel(level), level);

        public static float GetAuraRadiusForRank(MobRankTier rank, int level)
        {
            float radius;
            switch (rank)
            {
                case MobRankTier.SSS: radius = 1.2f + (level - 401) * 0.004f; break;
                case MobRankTier.SS: radius = 0.9f + (level - 201) * 0.0015f; break;
                case MobRankTier.S: radius = 0.7f + (level - 101) * 0.002f; break;
                case MobRankTier.A: radius = 0.55f + (level - 51) * 0.003f; break;
                case MobRankTier.B: radius = 0.4f + (level - 26) * 0.006f; break;
                case MobRankTier.C: radius = 0.36f + (level - 18) * 0.006f; break;
                case MobRankTier.D: radius = 0.32f + (level - 11) * 0.006f; break;
                default: return 0f;
            }
            return Mathf.Min(radius, 4f);
        }

        public static float GetAuraRadius(int level) => GetAuraRadiusForRank(GetRankFromLevel(level), level);

        public static float GetFlameWidthForRank(MobRankTier rank, int level)
        {
            float width;
            switch (rank)
            {
                case MobRankTier.SSS: width = FLAME_WIDTH * (1.6f + (level - 401) * 0.002f); break;
                case MobRankTier.SS: width = FLAME_WIDTH * (1.4f + (level - 201) * 0.001f); break;
                case MobRankTier.S: width = FLAME_WIDTH * (1.2f + (level - 101) * 0.002f); break;
                case MobRankTier.A: width = FLAME_WIDTH * (1.0f + (level - 51) * 0.004f); break;
                case MobRankTier.B: width = FLAME_WIDTH * (0.7f + (level - 26) * 0.012f); break;
                case MobRankTier.C: width = FLAME_WIDTH * (0.62f + (level - 18) * 0.008f); break;
                case MobRankTier.D: width = FLAME_WIDTH * (0.55f + (level - 11) * 0.008f); break;
                default: return FLAME_WIDTH;
            }
            return Mathf.Min(width, 3f);
        }

        public static float GetFlameWidth(int level) => GetFlameWidthForRank(GetRankFromLevel(level), level);

        /// <summary>
        /// Quantize alpha to 20 steps to keep material caches bounded.
        /// </summary>
        private static float QuantizeAlpha(float alpha) => Mathf.Round(alpha * 20f) / 20f;

        /// <summary>
        /// Backwards-compat: defaults to the wavy shape. New callers should use the shape overload.
        /// </summary>
        public static Material GetFlameMaterial(Color color, float alpha)
            => GetFlameMaterial(AuraFlameShape.Wavy, false, color, alpha);

        public static Material GetFlameMaterial(AuraFlameShape shape, Color color, float alpha)
            => GetFlameMaterial(shape, false, color, alpha);

        public static Material GetFlameMaterial(AuraFlameShape shape, bool mirrored, Color color, float alpha)
        {
            // Fall back to non-mirrored if mirror not generated for this (symmetric) shape.
            var cacheDict = mirrored ? FlameMaterialsByShapeMirrored : FlameMaterialsByShape;
            var texDict = mirrored ? FlameTexturesMirrored : FlameTextures;
            if (!cacheDict.TryGetValue(shape, out var cache))
            {
                cacheDict = FlameMaterialsByShape;
                texDict = FlameTextures;
                if (!cacheDict.TryGetValue(shape, out cache))
                {
                    cache = new Dictionary<Color, Material>();
                    cacheDict[shape] = cache;
                }
            }
            if (!texDict.TryGetValue(shape, out var tex) || tex == null)
            {
                tex = FlameTextures[AuraFlameShape.Wavy];
            }
            return GetCachedMaterial(cache, tex, color, alpha);
        }

        /// <summary>
        /// Gets a material for a custom PNG flame texture. Loads the texture lazily on first
        /// request via <see cref="ContentFinder{T}"/> and creates a mirrored variant via
        /// <see cref="Graphics.Blit"/> (since loaded textures aren't read/write enabled).
        /// Falls back to the procedural Wavy shape if the texture is missing or fails to load.
        /// </summary>
        public static Material GetCustomFlameMaterial(string texturePath, bool mirrored, Color color, float alpha)
        {
            if (string.IsNullOrEmpty(texturePath))
                return GetFlameMaterial(AuraFlameShape.Wavy, false, color, alpha);

            // Lazy-load the source texture once
            if (!CustomFlameTextures.TryGetValue(texturePath, out var srcTex) || srcTex == null)
            {
                srcTex = ContentFinder<Texture2D>.Get(texturePath, reportFailure: false);
                if (srcTex == null)
                {
                    Log.WarningOnce($"[Isekai] Custom aura texture not found: '{texturePath}' — falling back to Wavy procedural.", texturePath.GetHashCode());
                    return GetFlameMaterial(AuraFlameShape.Wavy, false, color, alpha);
                }
                CustomFlameTextures[texturePath] = srcTex;
                CustomFlameMaterials[texturePath] = new Dictionary<Color, Material>();
            }

            // Lazy-build the mirrored variant on first mirrored request
            Texture2D tex = srcTex;
            Dictionary<Color, Material> cache = CustomFlameMaterials[texturePath];
            if (mirrored)
            {
                if (!CustomFlameTexturesMirrored.TryGetValue(texturePath, out var mirrorTex) || mirrorTex == null)
                {
                    mirrorTex = BuildMirroredTexture(srcTex);
                    CustomFlameTexturesMirrored[texturePath] = mirrorTex;
                    CustomFlameMaterialsMirrored[texturePath] = new Dictionary<Color, Material>();
                }
                tex = mirrorTex;
                cache = CustomFlameMaterialsMirrored[texturePath];
            }
            return GetCachedMaterial(cache, tex, color, alpha);
        }

        /// <summary>
        /// Creates a horizontally-flipped copy of a texture using a RenderTexture pipeline.
        /// Works on textures that aren't read/write enabled (which most PNGs loaded via
        /// ContentFinder are not).
        /// </summary>
        private static Texture2D BuildMirroredTexture(Texture2D src)
        {
            int w = src.width;
            int h = src.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                // Blit with negative X scale → flips horizontally
                Graphics.Blit(src, rt, new Vector2(-1f, 1f), new Vector2(1f, 0f));
                RenderTexture.active = rt;
                Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply();
                dst.wrapMode = TextureWrapMode.Clamp;
                return dst;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static Material GetGlowMaterial(Color color, float alpha)
            => GetCachedMaterial(GlowMaterials, GlowTexture, color, alpha);

        private static Material GetMoteMaterial(Color color, float alpha)
            => GetCachedMaterial(MoteMaterials, MoteTexture, color, alpha);

        private static Material GetLeafMaterial(Color color, float alpha)
            => GetCachedMaterial(LeafMaterials, LeafTexture, color, alpha);

        private static Material GetCachedMaterial(Dictionary<Color, Material> cache, Texture2D tex, Color color, float alpha)
        {
            float qAlpha = QuantizeAlpha(alpha);
            Color keyColor = new Color(color.r, color.g, color.b, qAlpha);
            if (!cache.TryGetValue(keyColor, out Material mat) || mat == null)
            {
                mat = new Material(ShaderDatabase.MoteGlow);
                mat.mainTexture = tex;
                mat.color = keyColor;
                cache[keyColor] = mat;
            }
            return mat;
        }

        private static readonly List<int> _evictKeys = new List<int>();

        private static AuraState GetAuraState(int pawnId, int flameCount)
        {
            // Evict entries for despawned/dead pawns instead of nuking everything
            if (PawnAuraStates.Count > 200)
            {
                _evictKeys.Clear();
                foreach (var kvp in PawnAuraStates)
                {
                    var thing = Find.CurrentMap?.listerThings.AllThings
                        .FirstOrDefault(t => t.thingIDNumber == kvp.Key);
                    if (thing == null || thing.Destroyed)
                        _evictKeys.Add(kvp.Key);
                    if (_evictKeys.Count >= 100) break;
                }
                for (int i = 0; i < _evictKeys.Count; i++)
                    PawnAuraStates.Remove(_evictKeys[i]);
                if (PawnAuraStates.Count > 200)
                    PawnAuraStates.Clear();
            }

            if (!PawnAuraStates.TryGetValue(pawnId, out AuraState state))
            {
                state = new AuraState(flameCount);
                PawnAuraStates[pawnId] = state;
            }
            else
            {
                state.EnsureFlameCount(flameCount);
            }
            return state;
        }

        public static bool ShouldShowAura(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return false;

            if (pawn.Faction != null && pawn.Faction.IsPlayer)
                return pawn.Drafted;

            if (pawn.HostileTo(Faction.OfPlayer))
                return true;

            if (pawn.InMentalState || pawn.CurJob?.def == JobDefOf.AttackMelee || pawn.CurJob?.def == JobDefOf.AttackStatic)
                return true;

            if (pawn.Faction != null && !pawn.Faction.HostileTo(Faction.OfPlayer) && pawn.mindState?.enemyTarget != null)
                return true;

            return false;
        }

        /// <summary>
        /// Main aura draw entry point. Resolves per-constellation style if available,
        /// otherwise falls back to the rank-based palette/parameters.
        /// </summary>
        public static void DrawAura(Pawn pawn, IsekaiComponent comp, Vector3 drawPos)
        {
            try
            {
                if (pawn == null || comp == null) return;
                if (!IsekaiLevelingSettings.enableDraftedAura) return;
                if (!ShouldShowAura(pawn)) return;

                int level = comp.Level;
                MobRankTier rank = comp.GetRank();

                // ── Per-pawn aura display preference (set from the status tab) ──
                AuraDisplayMode displayMode = comp.auraDisplayMode;
                if (displayMode == AuraDisplayMode.None) return;

                // ── Resolve constellation style (null → rank-only flame fallback) ──
                // "DefaultFlame" forces the rank-only path by leaving style null.
                AuraStyle style = null;
                if (displayMode == AuraDisplayMode.Constellation && IsekaiLevelingSettings.enableConstellationAuraStyle)
                {
                    style = AuraStyleResolver.Resolve(pawn);
                }

                // Threshold is per-style: styles with custom textures show from D rank,
                // procedural-only styles (and the no-constellation fallback) keep the B threshold.
                MobRankTier minRank = style?.MinAuraRank ?? MobRankTier.B;
                if (rank < minRank) return;

                // ── Compute primary/accent colors ──
                Color rankColor = GetAuraColorForRank(rank);
                Color primary, accent;

                if (style != null)
                {
                    primary = style.primary;
                    // Accent = style accent tinted toward rank color so high ranks shimmer.
                    // Blend strength scales 0.25 (B) → 0.55 (SSS).
                    float rankBlend = Mathf.Lerp(0.25f, 0.55f, RankToFloat01(rank));
                    accent = Color.Lerp(style.accent, rankColor, rankBlend);
                }
                else
                {
                    primary = rankColor;
                    accent = GetInnerColorForRank(rank);
                }

                // Favorite color overrides primary if enabled (per locked design decision).
                // Constellation behavior + secondary effect still apply.
                if (IsekaiLevelingSettings.auraUseFavoriteColor && pawn.story?.favoriteColor != null)
                {
                    Color fav = pawn.story.favoriteColor.color;
                    primary = new Color(fav.r, fav.g, fav.b, 1f);
                }

                if (primary.a <= 0) return;

                // ── Compute flame parameters with style multipliers ──
                int baseFlameCount = GetFlameCountForRank(rank);
                float baseFlameHeight = GetFlameHeightForRank(rank, level);
                float baseFlameWidth = GetFlameWidthForRank(rank, level);
                float radius = GetAuraRadiusForRank(rank, level);

                int flameCount = baseFlameCount;
                float flameHeight = baseFlameHeight;
                float flameWidth = baseFlameWidth;
                float jitterMul = 1f;
                float pulseSpeedMul = 1f;
                float swirlSpeedDeg = 0f;
                bool noFlames = false;
                AuraFlameShape flameShape = AuraFlameShape.Wavy;
                bool mirrorOutward = false;
                string customTexturePath = null;
                bool groundCircle = false;
                float groundCircleSizeMul = 1f;
                AuraSecondaryEffect secondary = AuraSecondaryEffect.None;

                if (style != null)
                {
                    flameCount = Mathf.Max(0, Mathf.RoundToInt(baseFlameCount * style.flameCountMul));
                    flameHeight = baseFlameHeight * style.flameHeightMul;
                    flameWidth = baseFlameWidth * style.flameWidthMul;
                    jitterMul = style.jitterMul;
                    pulseSpeedMul = style.pulseSpeedMul;
                    swirlSpeedDeg = style.swirlSpeedDeg;
                    noFlames = style.noFlames;
                    flameShape = style.flameShape;
                    mirrorOutward = style.flameMirrorOutward;
                    customTexturePath = style.GetTexturePathForRank(rank);
                    groundCircle = style.groundCircle;
                    groundCircleSizeMul = style.groundCircleSizeMul;
                    secondary = style.secondary;
                }

                float baseAlpha = IsekaiLevelingSettings.auraOpacity;
                float sizeMultiplier = IsekaiLevelingSettings.auraSizeMultiplier;
                flameHeight *= sizeMultiplier;
                flameWidth *= sizeMultiplier;
                radius *= sizeMultiplier;

                AuraState state = GetAuraState(pawn.thingIDNumber, Mathf.Max(1, flameCount));
                float time = Time.time;

                // Pulse
                float pulse = 1f;
                if (IsekaiLevelingSettings.enableAuraPulse)
                {
                    float pulseSpeed = IsekaiLevelingSettings.auraPulseSpeed * pulseSpeedMul;
                    pulse = 1f + Mathf.Sin(time * 3f * pulseSpeed) * 0.12f;
                }

                // ── DRAW GLOW UNDERNEATH ──
                // PNG silhouettes don't have the soft alpha falloff procedural shapes have,
                // so the central glow contributes more of the "light pool" effect for them.
                // Boost size and brightness slightly when a custom texture is active.
                bool customActive = !string.IsNullOrEmpty(customTexturePath);
                float glowSize = flameHeight * 1.2f + radius * 0.5f;
                float glowAlpha = baseAlpha * 0.5f * pulse;
                if (customActive)
                {
                    glowSize *= 1.25f;
                    glowAlpha *= 1.5f;
                }
                bool showGlowDisc = style == null || style.showGlowDisc;
                if (glowSize > 0.05f && showGlowDisc)
                    DrawGlow(drawPos, primary, glowAlpha, glowSize);

                // ── DRAW MAIN AURA BODY ──
                if (groundCircle && customActive)
                {
                    // Flat spinning magic circle on the ground beneath the pawn.
                    // Sized from the aura radius only (NOT flameHeight — that vertical-flame
                    // metric is tiny at low ranks and huge at high ranks, which made the D-rank
                    // circle abnormally small). A generous base keeps the circle encircling the
                    // pawn at every rank, with a gentle growth and a sane cap.
                    float circleSize = (2.0f + radius * 2.6f) * groundCircleSizeMul;
                    circleSize = Mathf.Min(circleSize, 6f * groundCircleSizeMul);
                    // Shared south offset (computed from the MAIN circle) so every concentric
                    // circle uses the same center and they stay aligned (no gap).
                    float circleSouthOffset = circleSize * GROUND_PERSPECTIVE * 0.2f;
                    DrawGroundCircle(drawPos, customTexturePath, primary, baseAlpha, circleSize,
                        time, pulse, swirlSpeedDeg, circleSouthOffset);

                    // Accent inner circle at S+ — counter-rotating, smaller, for a layered look.
                    if (rank >= MobRankTier.S)
                    {
                        DrawGroundCircle(drawPos, customTexturePath, accent, baseAlpha * 0.7f,
                            circleSize * 0.6f, time, pulse, -swirlSpeedDeg * 1.6f, circleSouthOffset);
                    }
                }
                else if (!noFlames && flameCount > 0)
                {
                    // Vertical flame ring (default).
                    DrawFlameRing(drawPos, state, flameCount, flameHeight, radius, primary, baseAlpha,
                        time, pulse, flameWidth, jitterMul, swirlSpeedDeg, flameShape, mirrorOutward, customTexturePath);

                    // Inner ring for S+ rank (kept from original)
                    if (rank >= MobRankTier.S)
                    {
                        int innerCount = Mathf.Max(1, flameCount / 2);
                        float innerRadius = radius * 0.5f;
                        float innerHeight = flameHeight * 0.7f;
                        float innerWidth = flameWidth * 0.7f;
                        DrawFlameRing(drawPos, state, innerCount, innerHeight, innerRadius, accent,
                            baseAlpha * 0.9f, time + 0.5f, pulse, innerWidth, jitterMul, swirlSpeedDeg * 1.5f,
                            flameShape, mirrorOutward, customTexturePath);
                    }
                }

                // ── DRAW SECONDARY EFFECT ──
                if (secondary != AuraSecondaryEffect.None)
                {
                    DrawSecondaryEffect(secondary, drawPos, state, primary, accent, baseAlpha,
                        radius, flameHeight, time, pulse, rank);
                }

                // ── DRAW GLOWING ARMOR OVERLAY (on top of everything) ──
                if (style != null && style.armorGlow)
                {
                    int armorTier = rank >= MobRankTier.S ? 3 : (rank >= MobRankTier.B ? 2 : 1);
                    DrawArmorGlow(pawn, drawPos, primary, baseAlpha, pulse, armorTier,
                        style.armorGlowIntensityMul, style.armorGlowTexturePath);
                }

                // ── DRAW HOLY LIGHT RAYS (downward god-ray shafts, fading in/out) ──
                if (style != null && style.lightRays)
                {
                    DrawLightRays(state, drawPos, primary, baseAlpha, radius, flameHeight, time);
                }

                // ── DRAW RANDOM ELECTRIC VFX (frame-animated, on top) ──
                if (style != null && style.electricVfx)
                {
                    int elecTier = rank >= MobRankTier.S ? 3 : (rank >= MobRankTier.B ? 2 : 1);
                    DrawElectricVfx(state, drawPos, primary, accent, radius, time, elecTier);
                }
            }
            catch { /* Silently ignore aura rendering errors */ }
        }

        /// <summary>0 for B, 1 for SSS, smooth ramp in between.</summary>
        private static float RankToFloat01(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.SSS: return 1.0f;
                case MobRankTier.SS: return 0.75f;
                case MobRankTier.S: return 0.5f;
                case MobRankTier.A: return 0.25f;
                case MobRankTier.B: return 0f;
                default: return 0f;
            }
        }

        private static void DrawGlow(Vector3 drawPos, Color color, float alpha, float size)
        {
            Vector3 glowPos = drawPos;
            // Glow disc overlaps the pawn (drawn at MoteOverhead altitude).
            glowPos.y = AltitudeLayer.MoteOverhead.AltitudeFor() - 0.05f;

            Material glowMat = GetGlowMaterial(color, alpha);
            Matrix4x4 matrix = Matrix4x4.TRS(
                glowPos,
                Quaternion.identity,
                new Vector3(size * 1.8f, 1f, size * 1.8f)
            );
            Graphics.DrawMesh(MeshPool.plane10, matrix, glowMat, 0);
        }

        private static void DrawFlameRing(Vector3 drawPos, AuraState state, int flameCount, float flameHeight,
                                          float radius, Color color, float baseAlpha, float time, float pulse,
                                          float flameWidth, float jitterMul, float swirlSpeedDeg,
                                          AuraFlameShape shape, bool mirrorOutward, string customTexturePath)
        {
            float swirlOffsetRad = swirlSpeedDeg * Mathf.Deg2Rad * time;
            bool isCustom = !string.IsNullOrEmpty(customTexturePath);

            // PNG silhouettes have hard alpha edges and don't absorb width wobble the way
            // procedural soft-edged flames do, so the width animation looks janky. Damp it.
            float widthJitterScale = isCustom ? 0.3f : 1.0f;
            // Same idea for height jitter — keep it but lighter on PNGs so the silhouette
            // doesn't visibly squash frame-to-frame.
            float heightJitterScale = isCustom ? 0.5f : 1.0f;
            // Main-pass alpha multiplier. PNGs read as solid flat stickers at full alpha;
            // knock them WAY back so they feel like faint translucent energy. Procedural
            // shapes also get a modest reduction so the whole effect reads as a soft glow
            // rather than bold opaque drawings.
            float mainAlphaMul = isCustom ? 0.35f : 0.7f;

            // Half-altitude offset for the halo pass — keeps it slightly below the main flame
            // so blending stacks (halo as backdrop, silhouette in front).
            float layingY = AltitudeLayer.LayingPawn.AltitudeFor();

            for (int i = 0; i < flameCount; i++)
            {
                float angle = (i / (float)flameCount) * 360f;
                float radians = angle * Mathf.Deg2Rad + swirlOffsetRad;

                float phaseOffset = state.FlamePhases[i % state.FlamePhases.Length];
                float jitterAmp = 0.25f * jitterMul * heightJitterScale;
                float animatedHeight = flameHeight * (0.75f + Mathf.Sin(time * 5f + phaseOffset) * jitterAmp) * pulse;
                float sway = Mathf.Sin(time * 4f + phaseOffset) * 0.04f * jitterMul;

                float cos = Mathf.Cos(radians);
                float sin = Mathf.Sin(radians);
                float x = cos * radius + sway;
                float z = sin * radius * 0.55f;

                Vector3 flamePos = drawPos + new Vector3(x, 0, z);
                flamePos.y = layingY;

                float alphaJitter = 0.35f * jitterMul;
                float flameAlpha = baseAlpha * (0.65f + Mathf.Sin(time * 6f + phaseOffset * 1.5f) * alphaJitter);
                float widthJitter = 0.15f * jitterMul * widthJitterScale;
                float animatedWidth = flameWidth * (0.85f + Mathf.Sin(time * 7f + phaseOffset) * widthJitter);

                bool useMirrored = mirrorOutward && cos < 0f;

                // ── Custom textures: draw a halo bloom behind the silhouette ──
                // The halo is the same texture scaled up and dimmed, painting a soft
                // light pool around the hard-edged PNG. Two passes are cheap — same
                // material cache, same mesh — and add the "fire/glow" feel PNGs lack.
                if (isCustom)
                {
                    Vector3 haloPos = flamePos + new Vector3(0, animatedHeight * 0.5f, 0);
                    // Outer soft halo — very faint, just a light bleed
                    Material haloOuter = GetCustomFlameMaterial(customTexturePath, useMirrored, color, flameAlpha * 0.08f);
                    Matrix4x4 haloOuterMatrix = Matrix4x4.TRS(
                        haloPos,
                        Quaternion.identity,
                        new Vector3(animatedWidth * 1.9f, animatedHeight * 1.55f, 1f)
                    );
                    Graphics.DrawMesh(MeshPool.plane10, haloOuterMatrix, haloOuter, 0);

                    // Inner halo — tighter, slightly brighter
                    Material haloInner = GetCustomFlameMaterial(customTexturePath, useMirrored, color, flameAlpha * 0.15f);
                    Matrix4x4 haloInnerMatrix = Matrix4x4.TRS(
                        haloPos,
                        Quaternion.identity,
                        new Vector3(animatedWidth * 1.35f, animatedHeight * 1.2f, 1f)
                    );
                    Graphics.DrawMesh(MeshPool.plane10, haloInnerMatrix, haloInner, 0);
                }

                Material flameMat = isCustom
                    ? GetCustomFlameMaterial(customTexturePath, useMirrored, color, flameAlpha * mainAlphaMul)
                    : GetFlameMaterial(shape, useMirrored, color, flameAlpha * mainAlphaMul);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    flamePos + new Vector3(0, animatedHeight * 0.5f, 0),
                    Quaternion.identity,
                    new Vector3(animatedWidth, animatedHeight, 1f)
                );
                Graphics.DrawMesh(MeshPool.plane10, matrix, flameMat, 0);
            }
        }

        /// <summary>
        /// Draws a custom texture as a flat "magic circle" lying on the ground beneath the
        /// pawn. The circle spins around the vertical axis and is foreshortened in the
        /// north-south direction so it reads as lying on the ground in RimWorld's perspective.
        ///
        /// Matrix is built manually so the perspective squash is applied AFTER the spin —
        /// the round artwork spins as a circle, then the whole thing is compressed into an
        /// ellipse. (Squashing before the spin would make the ellipse wobble as it turns.)
        /// </summary>
        private static void DrawGroundCircle(Vector3 drawPos, string texturePath, Color color,
            float baseAlpha, float size, float time, float pulse, float spinSpeedDeg, float southOffset)
        {
            Vector3 pos = drawPos;
            // Below the pawn so the pawn renders standing on top of the circle.
            pos.y = AltitudeLayer.LayingPawn.AltitudeFor();
            // Shift the circle south (−Z renders downward on screen) so the pawn sits at the
            // visual center. Offset is shared across concentric circles (passed in) so they
            // stay aligned with no gap between rings.
            pos.z -= southOffset;

            float spin = spinSpeedDeg * time; // degrees
            float pulsedSize = size * pulse;

            // T * perspectiveSquash(Z) * spin(Y) * uniformSize
            Matrix4x4 m =
                Matrix4x4.Translate(pos)
                * Matrix4x4.Scale(new Vector3(1f, 1f, GROUND_PERSPECTIVE))
                * Matrix4x4.Rotate(Quaternion.Euler(0f, spin, 0f))
                * Matrix4x4.Scale(new Vector3(pulsedSize, 1f, pulsedSize));

            // Faint outer bloom + main pass for a glow feel (PNGs lack soft falloff).
            Matrix4x4 mHalo =
                Matrix4x4.Translate(pos)
                * Matrix4x4.Scale(new Vector3(1f, 1f, GROUND_PERSPECTIVE))
                * Matrix4x4.Rotate(Quaternion.Euler(0f, spin, 0f))
                * Matrix4x4.Scale(new Vector3(pulsedSize * 1.12f, 1f, pulsedSize * 1.12f));

            Material haloMat = GetCustomFlameMaterial(texturePath, false, color, baseAlpha * 0.25f);
            Graphics.DrawMesh(MeshPool.plane10, mHalo, haloMat, 0);

            Material mainMat = GetCustomFlameMaterial(texturePath, false, color, baseAlpha * 0.7f);
            Graphics.DrawMesh(MeshPool.plane10, m, mainMat, 0);
        }

        // ───────────────────────── Holy light rays ─────────────────────────

        /// <summary>Lazily loads Auras/sunray.png; falls back to the procedural shaft if absent.</summary>
        private static Texture2D GetRayTexture()
        {
            if (!_sunrayTexInit)
            {
                _sunrayTex = ContentFinder<Texture2D>.Get("Auras/sunray", reportFailure: false);
                _sunrayTexInit = true;
            }
            return _sunrayTex ?? RayTextureFallback;
        }

        /// <summary>
        /// Draws downward "god ray" light shafts that appear at random horizontal positions
        /// around the pawn and fade in then out over their lifetime. Vertical beams (drawn like
        /// flames), additive, tinted to the aura color.
        /// </summary>
        private static void DrawLightRays(AuraState state, Vector3 drawPos, Color color,
            float baseAlpha, float radius, float flameHeight, float time)
        {
            const int MAX_RAYS = 6;
            if (state.Rays == null) state.Rays = new LightRay[MAX_RAYS];

            // Spawn pacing — aim for a new ray roughly every ~0.45s, into a free slot.
            float dt = Mathf.Min(0.1f, Time.deltaTime);
            if (time - state.LastRaySpawn > 0.1f && Rand.Value < dt / 0.45f)
            {
                for (int i = 0; i < state.Rays.Length; i++)
                {
                    if (state.Rays[i].active) continue;
                    float spread = Mathf.Max(0.6f, radius * 1.8f);
                    state.Rays[i] = new LightRay
                    {
                        active = true,
                        xOff = Rand.Range(-spread, spread),
                        start = time,
                        duration = Rand.Range(1.1f, 2.0f),
                        height = (flameHeight * 2.0f + 1.5f) * Rand.Range(0.85f, 1.25f),
                        width = Rand.Range(0.35f, 0.6f),
                        tilt = Rand.Range(-10f, 10f),
                    };
                    state.LastRaySpawn = time;
                    break;
                }
            }

            // Update + draw active rays.
            float baseY = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.03f;
            for (int i = 0; i < state.Rays.Length; i++)
            {
                if (!state.Rays[i].active) continue;
                LightRay r = state.Rays[i];
                float t = (time - r.start) / r.duration;
                if (t >= 1f) { state.Rays[i].active = false; continue; }

                float env = Mathf.Sin(t * Mathf.PI); // 0 → 1 → 0 fade in/out
                float alpha = baseAlpha * 1.6f * env;

                Vector3 pos = new Vector3(drawPos.x + r.xOff, baseY, drawPos.z);
                Material mat = GetArmorGlowMaterial(GetRayTexture(), color, alpha);
                // Vertical shaft: same orientation as flames (X = width, Y = height), base at the
                // pawn, extending upward, with a slight lean.
                Matrix4x4 m = Matrix4x4.TRS(
                    pos + new Vector3(0f, r.height * 0.5f, 0f),
                    Quaternion.Euler(0f, 0f, r.tilt),
                    new Vector3(r.width, r.height, 1f));
                Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
            }
        }

        // ───────────────────────── Electric VFX ─────────────────────────

        /// <summary>
        /// Randomly triggers and plays a short electric-arc clip (frame animation) near the
        /// pawn. Drawn additively (MoteGlow) so the clips' black backgrounds vanish and the
        /// arcs take the aura's tint. One flash plays at a time; a new one starts at random.
        /// </summary>
        private static void DrawElectricVfx(AuraState state, Vector3 drawPos, Color primary, Color accent,
            float radius, float time, int tier)
        {
            // Start a new flash at random when idle. Higher tier → shorter average interval
            // → electric arcs fire more often.
            if (state.ElectricClip < 0)
            {
                float interval = tier >= 3 ? 0.55f : (tier >= 2 ? 1.0f : 1.6f);
                float dt = Mathf.Min(0.1f, Time.deltaTime);
                if (Rand.Value < dt / interval)
                {
                    state.ElectricClip = Rand.RangeInclusive(0, ElectricClipPaths.Length - 1);
                    state.ElectricStart = time;
                    state.ElectricOffset = Vector3.zero; // always centered on the pawn
                    state.ElectricScale = Rand.Range(1.3f, 2.1f);
                    state.ElectricColorIdx = (byte)(Rand.Bool ? 0 : 1);
                }
                if (state.ElectricClip < 0) return;
            }

            List<Texture2D> frames = GetElectricFrames(state.ElectricClip);
            if (frames == null || frames.Count == 0) { state.ElectricClip = -1; return; }

            int idx = Mathf.FloorToInt((time - state.ElectricStart) * ELECTRIC_FPS);
            if (idx < 0 || idx >= frames.Count) { state.ElectricClip = -1; return; }

            Texture2D tex = frames[idx];
            if (tex == null) return;
            Color c = state.ElectricColorIdx == 0 ? primary : accent;

            Vector3 pos = drawPos + state.ElectricOffset;
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.06f; // above the rest of the aura
            float sz = state.ElectricScale;

            // Additive MoteGlow material (reuses the armor-glow cache): black bg → transparent,
            // texture tinted to the aura color.
            Material mat = GetArmorGlowMaterial(tex, c, 0.85f);
            Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(sz, 1f, sz));
            Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
        }

        private static List<Texture2D> GetElectricFrames(int clip)
        {
            if (clip < 0 || clip >= ElectricClipPaths.Length) return null;
            if (ElectricFrames.TryGetValue(clip, out var list)) return list;

            list = new List<Texture2D>();
            string basePath = ElectricClipPaths[clip];
            for (int i = 1; i <= 99; i++)
            {
                var t = ContentFinder<Texture2D>.Get($"{basePath}_{i:00}", reportFailure: false);
                if (t == null) break;
                list.Add(t);
            }
            if (list.Count == 0)
                Log.WarningOnce($"[Isekai] No electric VFX frames found for '{basePath}_NN'.", basePath.GetHashCode());
            ElectricFrames[clip] = list;
            return list;
        }

        // ───────────────────────── Glowing armor overlay ─────────────────────────

        /// <summary>
        /// Re-draws the pawn's worn torso apparel ON TOP of the pawn with an additive glow
        /// tint, matching the pawn's facing automatically (uses the apparel's already-resolved
        /// directional graphics). Higher rank tiers add brighter + larger bloom passes.
        /// </summary>
        // Glow mesh scale-up per rank tier. The pawn body mesh is ~1.5 tiles, so these
        // multipliers give an on-screen halo of roughly:
        //   tier 1 (D/C) → ~2.4 tiles, tier 2 (B/A) → ~3.0 tiles, tier 3 (S+) → ~4.0 tiles.
        private static float ArmorGlowScaleForTier(int tier)
        {
            switch (tier)
            {
                case 3: return 2.0f;
                case 2: return 1.6f;
                default: return 1.3f;
            }
        }

        /// <summary>
        /// On-screen height (world tiles) of the custom armor overlay texture per tier.
        /// A bit larger than the body-silhouette glow used previously.
        /// </summary>
        private static float ArmorTextureHeightForTier(int tier)
        {
            switch (tier)
            {
                case 3: return 2.0f;
                case 2: return 1.7f;
                default: return 1.4f;
            }
        }

        private static void DrawArmorGlow(Pawn pawn, Vector3 drawPos, Color color, float baseAlpha,
            float pulse, int tier, float intensityMul, string customArmorPath)
        {
            if (pawn == null) return;

            try
            {
                float y = AltitudeLayer.MoteOverhead.AltitudeFor();
                // Alpha is independent of the (low) auraOpacity setting so the glow is clearly
                // visible. Tier scales it up: t1 ≈ 0.85×, t2 ≈ 1.0×, t3 ≈ 1.15× of the base.
                float mainAlpha = 0.4f * Mathf.Max(0f, intensityMul) * (0.7f + 0.15f * tier) * pulse;

                // ── Custom armor overlay texture (single billboard image over the pawn) ──
                if (!string.IsNullOrEmpty(customArmorPath))
                {
                    Texture2D armorTex = ContentFinder<Texture2D>.Get(customArmorPath, reportFailure: false);
                    if (armorTex == null)
                    {
                        Log.WarningOnce($"[Isekai] Armor overlay texture not found: '{customArmorPath}'.", customArmorPath.GetHashCode());
                        return;
                    }
                    float h = ArmorTextureHeightForTier(tier);
                    float w = h * (armorTex.width / (float)armorTex.height); // preserve aspect
                    Vector3 apos = new Vector3(drawPos.x, y, drawPos.z);

                    Material amain = GetArmorGlowMaterial(armorTex, color, mainAlpha);
                    Graphics.DrawMesh(MeshPool.plane10,
                        Matrix4x4.TRS(apos, Quaternion.identity, new Vector3(w, 1f, h)), amain, 0);
                    if (tier >= 2)
                    {
                        Material ab = GetArmorGlowMaterial(armorTex, color, mainAlpha * 0.4f);
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(apos, Quaternion.identity, new Vector3(w * 1.12f, 1f, h * 1.12f)), ab, 0);
                    }
                    if (tier >= 3)
                    {
                        Material ab2 = GetArmorGlowMaterial(armorTex, color, mainAlpha * 0.25f);
                        Graphics.DrawMesh(MeshPool.plane10,
                            Matrix4x4.TRS(apos, Quaternion.identity, new Vector3(w * 1.28f, 1f, h * 1.28f)), ab2, 0);
                    }
                    return;
                }

                // ── Fallback: glow the pawn's body/head/worn apparel silhouette ──
                var renderer = pawn.Drawer?.renderer;
                if (renderer == null) return;
                renderer.EnsureGraphicsInitialized();

                Rot4 rot = pawn.Rotation;

                // Body position = pawn draw loc; head position = body + head offset.
                Vector3 bodyPos = new Vector3(drawPos.x, y, drawPos.z);
                Vector3 headOffset = renderer.BaseHeadOffsetAt(rot);
                Vector3 headPos = new Vector3(drawPos.x + headOffset.x, y, drawPos.z + headOffset.z);

                // 1) Glow the BODY silhouette (always present).
                Graphic body = renderer.BodyGraphic;
                if (body != null)
                    GlowGraphic(body, rot, bodyPos, color, mainAlpha, tier);

                // 2) Glow the HEAD silhouette so the head/helmet area is covered too.
                Graphic head = renderer.HeadGraphic;
                if (head != null)
                    GlowGraphic(head, rot, headPos, color, mainAlpha, tier);

                // 3) Glow worn apparel — body apparel at the body, headgear at the head.
                if (pawn.apparel?.WornApparel != null)
                {
                    BodyTypeDef bodyType = pawn.story?.bodyType ?? BodyTypeDefOf.Male;
                    var worn = pawn.apparel.WornApparel;
                    for (int i = 0; i < worn.Count; i++)
                    {
                        Apparel ap = worn[i];
                        if (ap?.def?.apparel == null) continue;
                        if (!ApparelGraphicRecordGetter.TryGetGraphicApparel(ap, bodyType, false, out ApparelGraphicRecord rec))
                            continue;
                        if (rec.graphic == null) continue;

                        var lastLayer = ap.def.apparel.LastLayer;
                        bool isHeadgear = lastLayer == ApparelLayerDefOf.Overhead || lastLayer == ApparelLayerDefOf.EyeCover;
                        GlowGraphic(rec.graphic, rot, isHeadgear ? headPos : bodyPos, color, mainAlpha, tier);
                    }
                }
            }
            catch { /* version drift / render not ready — skip armor glow */ }
        }

        /// <summary>Draws a single graphic as an additive glow (scaled-up main pass + tier bloom passes).</summary>
        private static void GlowGraphic(Graphic g, Rot4 rot, Vector3 pos, Color color, float mainAlpha, int tier)
        {
            Material src = g.MatAt(rot, null);
            Texture tex = src?.mainTexture;
            if (tex == null) return;
            Mesh mesh = g.MeshAt(rot); // handles per-direction flip + draw size
            if (mesh == null) return;

            float s = ArmorGlowScaleForTier(tier);
            Material main = GetArmorGlowMaterial(tex, color, mainAlpha);
            Matrix4x4 mm = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1f, s));
            Graphics.DrawMesh(mesh, mm, main, 0);

            if (tier >= 2)
            {
                Material b = GetArmorGlowMaterial(tex, color, mainAlpha * 0.4f);
                Matrix4x4 bm = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s * 1.18f, 1f, s * 1.18f));
                Graphics.DrawMesh(mesh, bm, b, 0);
            }
            if (tier >= 3)
            {
                Material b2 = GetArmorGlowMaterial(tex, color, mainAlpha * 0.25f);
                Matrix4x4 bm2 = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s * 1.38f, 1f, s * 1.38f));
                Graphics.DrawMesh(mesh, bm2, b2, 0);
            }
        }

        private static Material GetArmorGlowMaterial(Texture tex, Color color, float alpha)
        {
            float qAlpha = QuantizeAlpha(alpha);
            Color keyColor = new Color(color.r, color.g, color.b, qAlpha);
            if (!ArmorGlowMaterials.TryGetValue(tex, out var cache))
            {
                cache = new Dictionary<Color, Material>();
                ArmorGlowMaterials[tex] = cache;
            }
            if (!cache.TryGetValue(keyColor, out Material mat) || mat == null)
            {
                mat = new Material(ShaderDatabase.MoteGlow);
                mat.mainTexture = tex;
                mat.color = keyColor;
                cache[keyColor] = mat;
            }
            return mat;
        }

        // ───────────────────────── Secondary effects ─────────────────────────

        private static void DrawSecondaryEffect(AuraSecondaryEffect kind, Vector3 drawPos, AuraState state,
            Color primary, Color accent, float baseAlpha, float radius, float flameHeight, float time, float pulse,
            MobRankTier rank)
        {
            switch (kind)
            {
                case AuraSecondaryEffect.EmberSparks:
                    UpdateAndDrawDriftParticles(state, drawPos, primary, accent, baseAlpha,
                        radius, time, spawnRate: 6f, isMote: true,
                        velocity: new Vector3(0f, 0f, 0f),
                        upwardBias: 0.6f, lifetime: 1.4f, spreadRadius: radius * 0.9f, sizeBase: 0.18f);
                    break;

                case AuraSecondaryEffect.ForgeSparks:
                    UpdateAndDrawDriftParticles(state, drawPos, primary, accent, baseAlpha,
                        radius, time, spawnRate: 14f, isMote: true,
                        velocity: Vector3.zero,
                        upwardBias: 1.1f, lifetime: 0.9f, spreadRadius: radius * 1.1f, sizeBase: 0.13f);
                    break;

                case AuraSecondaryEffect.AshDrift:
                    UpdateAndDrawDriftParticles(state, drawPos, accent, primary, baseAlpha * 0.85f,
                        radius, time, spawnRate: 4f, isMote: false,
                        velocity: new Vector3(0.05f, 0f, 0.02f),
                        upwardBias: 0.25f, lifetime: 2.2f, spreadRadius: radius * 1.3f, sizeBase: 0.22f);
                    break;

                case AuraSecondaryEffect.LeafDrift:
                    UpdateAndDrawDriftParticles(state, drawPos, primary, accent, baseAlpha,
                        radius, time, spawnRate: 5f, isMote: false,
                        velocity: new Vector3(0.15f, 0f, 0.05f),
                        upwardBias: 0.15f, lifetime: 2.0f, spreadRadius: radius * 1.4f, sizeBase: 0.24f);
                    break;

                case AuraSecondaryEffect.OrbsRising:
                    UpdateAndDrawDriftParticles(state, drawPos, primary, accent, baseAlpha,
                        radius, time, spawnRate: 5f, isMote: true,
                        velocity: Vector3.zero,
                        upwardBias: 0.5f, lifetime: 1.8f, spreadRadius: radius * 0.7f, sizeBase: 0.28f);
                    break;

                case AuraSecondaryEffect.GlyphRing:
                    DrawOrbitingMotes(drawPos, primary, accent, baseAlpha, radius * 1.8f,
                        height: flameHeight * 0.45f, count: 6, time: time, rotSpeedDeg: 35f, size: 0.22f);
                    break;

                case AuraSecondaryEffect.RuneRing:
                    DrawOrbitingMotes(drawPos, primary, accent, baseAlpha, radius * 1.5f,
                        height: 0.05f, count: 8, time: time, rotSpeedDeg: 22f, size: 0.20f);
                    break;

                case AuraSecondaryEffect.BeamPillar:
                    DrawBeamPillar(drawPos, primary, accent, baseAlpha, flameHeight, pulse);
                    DrawHaloRing(drawPos, accent, baseAlpha * 0.7f, radius * 1.2f, flameHeight * 0.95f);
                    break;

                case AuraSecondaryEffect.Slashes:
                    DrawSlashes(state, drawPos, primary, baseAlpha, radius, flameHeight, time);
                    break;

                case AuraSecondaryEffect.DarkCrackle:
                    // Small, brief dark inner flames jittering at the pawn's feet.
                    if (Mathf.Sin(time * 11f) > 0.4f)
                    {
                        Color dark = new Color(accent.r * 0.4f, accent.g * 0.4f, accent.b * 0.4f, 1f);
                        DrawBeamPillar(drawPos, dark, dark, baseAlpha * 0.6f, flameHeight * 0.55f, pulse);
                    }
                    break;

                case AuraSecondaryEffect.SwirlMotion:
                    // Swirl handled in DrawFlameRing via swirlSpeedDeg; add pawprint motes too.
                    UpdateAndDrawDriftParticles(state, drawPos, accent, primary, baseAlpha * 0.9f,
                        radius, time, spawnRate: 3f, isMote: true,
                        velocity: new Vector3(0.2f, 0f, 0.1f),
                        upwardBias: 0.0f, lifetime: 1.6f, spreadRadius: radius * 1.5f, sizeBase: 0.22f);
                    break;

                case AuraSecondaryEffect.BannerFlames:
                default:
                    // Banner relies purely on flame-tuning multipliers; nothing extra to draw.
                    break;
            }
        }

        /// <summary>
        /// Generic drift particle pump: spawns at spawnRate/sec, integrates velocity + upward bias,
        /// fades out by life, draws each as either a mote or a leaf quad.
        /// Particles alternate between primary and accent color.
        /// </summary>
        private static void UpdateAndDrawDriftParticles(AuraState state, Vector3 drawPos,
            Color primary, Color accent, float baseAlpha, float radius, float time,
            float spawnRate, bool isMote, Vector3 velocity, float upwardBias, float lifetime,
            float spreadRadius, float sizeBase)
        {
            // Spawn pacing
            float dt = Mathf.Min(0.1f, time - state.LastSpawnTime);
            if (dt < 0f) dt = 0f;
            float toSpawn = spawnRate * dt;
            int spawnCount = Mathf.FloorToInt(toSpawn);
            // probabilistic remainder
            if (Rand.Range(0f, 1f) < (toSpawn - spawnCount)) spawnCount++;
            state.LastSpawnTime = time;

            for (int s = 0; s < spawnCount && state.ParticleCount < state.Particles.Length; s++)
            {
                float ang = Rand.Range(0f, Mathf.PI * 2f);
                float r = Rand.Range(0.1f, spreadRadius);
                Vector3 spawnOffset = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r * 0.55f);
                Vector3 vel = velocity + new Vector3(
                    Rand.Range(-0.05f, 0.05f),
                    upwardBias + Rand.Range(-0.1f, 0.1f),
                    Rand.Range(-0.05f, 0.05f));
                state.Particles[state.ParticleCount++] = new Particle
                {
                    pos = spawnOffset,
                    vel = vel,
                    life = lifetime,
                    maxLife = lifetime,
                    size = sizeBase * Rand.Range(0.7f, 1.3f),
                    colorIndex = (byte)(Rand.Bool ? 0 : 1),
                    rotSeed = Rand.Range(0f, 360f),
                    rotSpeed = Rand.Range(40f, 80f) * (Rand.Bool ? 1f : -1f),
                };
            }

            // Integrate + draw + cull
            float frameDt = Mathf.Min(0.05f, Time.deltaTime);
            int write = 0;
            for (int i = 0; i < state.ParticleCount; i++)
            {
                Particle p = state.Particles[i];
                p.life -= frameDt;
                if (p.life <= 0f) continue;
                p.pos += p.vel * frameDt;
                state.Particles[write++] = p;

                // Draw
                float lifeFrac = p.life / p.maxLife;
                float alpha = baseAlpha * Mathf.Clamp01(lifeFrac * 1.4f);
                Color c = p.colorIndex == 0 ? primary : accent;
                Vector3 worldPos = drawPos + p.pos;
                worldPos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.01f;

                Material mat = isMote ? GetMoteMaterial(c, alpha) : GetLeafMaterial(c, alpha);
                // Use the particle's stable rotSeed + rotSpeed so culling/repacking other
                // particles doesn't make this one's rotation jump frame-to-frame.
                Quaternion rot = isMote ? Quaternion.identity
                                        : Quaternion.Euler(0f, p.rotSeed + time * p.rotSpeed, 0f);
                float drawSize = p.size * Mathf.Lerp(0.7f, 1f, lifeFrac);
                Matrix4x4 matrix = Matrix4x4.TRS(
                    worldPos,
                    rot,
                    new Vector3(drawSize * (isMote ? 1f : 1.6f), 1f, drawSize));
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
            }
            state.ParticleCount = write;
        }

        /// <summary>
        /// Ring of motes orbiting at fixed radius and height (Mage glyphs / Sage runes).
        /// </summary>
        private static void DrawOrbitingMotes(Vector3 drawPos, Color primary, Color accent,
            float baseAlpha, float radius, float height, int count, float time, float rotSpeedDeg, float size)
        {
            float baseAng = time * rotSpeedDeg * Mathf.Deg2Rad;
            for (int i = 0; i < count; i++)
            {
                float a = baseAng + (i / (float)count) * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * radius;
                float z = Mathf.Sin(a) * radius * 0.55f;

                // Twinkle alpha
                float twinkle = 0.7f + Mathf.Sin(time * 4f + i * 1.3f) * 0.3f;
                Color c = (i % 2 == 0) ? primary : accent;
                Material mat = GetMoteMaterial(c, baseAlpha * 1.4f * twinkle);

                Vector3 pos = drawPos + new Vector3(x, 0f, z);
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f + height * 0.001f;

                Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size));
                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
            }
        }

        /// <summary>
        /// Single tall narrow flame at center (Paladin pillar, Berserker dark crackle).
        /// </summary>
        private static void DrawBeamPillar(Vector3 drawPos, Color primary, Color accent,
            float baseAlpha, float flameHeight, float pulse)
        {
            float beamHeight = flameHeight * 1.8f * pulse;
            float beamWidth = 0.45f;

            // Outer soft beam (primary)
            Vector3 pos = drawPos;
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
            Material outer = GetFlameMaterial(primary, baseAlpha * 0.9f);
            Matrix4x4 m1 = Matrix4x4.TRS(
                pos + new Vector3(0f, beamHeight * 0.5f, 0f),
                Quaternion.identity,
                new Vector3(beamWidth * 1.4f, beamHeight, 1f));
            Graphics.DrawMesh(MeshPool.plane10, m1, outer, 0);

            // Inner bright core (accent)
            Material inner = GetFlameMaterial(accent, baseAlpha * 1.1f);
            Matrix4x4 m2 = Matrix4x4.TRS(
                pos + new Vector3(0f, beamHeight * 0.5f, 0f),
                Quaternion.identity,
                new Vector3(beamWidth * 0.5f, beamHeight * 0.95f, 1f));
            Graphics.DrawMesh(MeshPool.plane10, m2, inner, 0);
        }

        /// <summary>
        /// Thin horizontal halo above the pawn (Paladin).
        /// </summary>
        private static void DrawHaloRing(Vector3 drawPos, Color color, float baseAlpha, float ringRadius, float height)
        {
            Vector3 pos = drawPos + new Vector3(0f, 0f, 0f);
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.04f;
            Material mat = GetGlowMaterial(color, baseAlpha);
            Matrix4x4 m = Matrix4x4.TRS(
                pos + new Vector3(0f, height, 0f),
                Quaternion.identity,
                new Vector3(ringRadius * 2.4f, 1f, ringRadius * 1.2f));
            Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
        }

        /// <summary>
        /// Brief horizontal slash flickers (Duelist). Triggered every ~0.4s.
        /// </summary>
        private static void DrawSlashes(AuraState state, Vector3 drawPos, Color color,
            float baseAlpha, float radius, float flameHeight, float time)
        {
            // Slash ~every 0.4-0.6s. We don't store slash positions — just flash one.
            float interval = 0.45f;
            if (time - state.LastSlashTime > interval)
            {
                state.LastSlashTime = time;
            }
            float slashAge = time - state.LastSlashTime;
            if (slashAge > 0.18f) return; // short flash

            float slashAlpha = baseAlpha * 1.6f * (1f - slashAge / 0.18f);
            // Pseudo-random slash angle/position from time
            float seed = state.LastSlashTime;
            float ang = (seed * 13.37f) % (Mathf.PI * 2f);
            float dist = radius * 0.9f;
            float x = Mathf.Cos(ang) * dist;
            float z = Mathf.Sin(ang) * dist * 0.55f;
            Vector3 pos = drawPos + new Vector3(x, 0f, z);
            pos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.03f;

            Material mat = GetFlameMaterial(color, slashAlpha);
            Quaternion rot = Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 90f, 0f);
            Matrix4x4 m = Matrix4x4.TRS(
                pos + new Vector3(0f, flameHeight * 0.3f, 0f),
                rot,
                new Vector3(flameHeight * 0.8f, 0.18f, 1f));
            Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
        }

        public static void CleanupPawn(int pawnId)
        {
            PawnAuraStates.Remove(pawnId);
        }

        /// <summary>
        /// Clear all caches. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearCaches()
        {
            foreach (var cache in FlameMaterialsByShape.Values)
                cache.Clear();
            foreach (var cache in FlameMaterialsByShapeMirrored.Values)
                cache.Clear();
            foreach (var cache in CustomFlameMaterials.Values)
                cache.Clear();
            foreach (var cache in CustomFlameMaterialsMirrored.Values)
                cache.Clear();
            foreach (var cache in ArmorGlowMaterials.Values)
                cache.Clear();
            ArmorGlowMaterials.Clear();
            GlowMaterials.Clear();
            MoteMaterials.Clear();
            LeafMaterials.Clear();
            PawnAuraStates.Clear();
        }
    }

    /// <summary>
    /// Harmony patch to draw auras when pawns are drawn.
    /// </summary>
    [HarmonyPatch]
    public static class Pawn_DrawAt_AuraPatch
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(Pawn), "DrawAt");
            if (method == null)
            {
                Log.Warning("[Isekai] Pawn.DrawAt not found — aura rendering patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Pawn), "DrawAt");
        }

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, Vector3 drawLoc)
        {
            if (__instance == null) return;
            if (!__instance.Spawned) return;
            if (!__instance.RaceProps.Humanlike) return;

            if (!AuraSystem.ShouldShowAura(__instance)) return;

            var comp = IsekaiComponent.GetCached(__instance);
            if (comp == null) return;

            AuraSystem.DrawAura(__instance, comp, drawLoc);
        }
    }
}
