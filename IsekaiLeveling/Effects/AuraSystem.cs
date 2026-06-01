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
    /// Manages DBZ-style aura visual effects for drafted pawns
    /// Vertical energy flames + glow
    /// Only for B rank and above (based on pawn rank, not level)
    /// Size scales with level within each rank tier for more impressive high-level auras
    /// 
    /// RANK THRESHOLDS:
    /// F-D: No aura
    /// C: No aura  
    /// B: White/Silver aura
    /// A: Blue aura
    /// S: Gold aura (Super Saiyan!)
    /// SS: Orange/Red aura
    /// SSS: Violet/Purple aura
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AuraSystem
    {
        // Textures
        private static readonly Texture2D FlameTexture;
        private static readonly Texture2D GlowTexture;
        
        // Material caches
        private static readonly Dictionary<Color, Material> FlameMaterials = new Dictionary<Color, Material>();
        private static readonly Dictionary<Color, Material> GlowMaterials = new Dictionary<Color, Material>();
        
        // Per-pawn animation state
        private static readonly Dictionary<int, AuraState> PawnAuraStates = new Dictionary<int, AuraState>();
        
        // Constants - LARGER FLAMES for more impressive effect
        private const float BASE_FLAME_HEIGHT = 2.0f;
        private const float FLAME_WIDTH = 0.5f;
        
        private class AuraState
        {
            public float[] FlamePhases;
            
            public AuraState(int flameCount)
            {
                FlamePhases = new float[flameCount];
                for (int i = 0; i < flameCount; i++)
                {
                    FlamePhases[i] = Rand.Range(0f, Mathf.PI * 2f);
                }
            }
        }
        
        static AuraSystem()
        {
            FlameTexture = GenerateFlameTexture(64, 128);
            GlowTexture = GenerateGlowTexture(128);
            Log.Message("[Isekai Leveling] DBZ-style aura system initialized.");
        }
        
        private static Texture2D GenerateFlameTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            float centerX = width / 2f;
            
            for (int y = 0; y < height; y++)
            {
                float normalizedY = (float)y / height;
                float flameWidth = (1f - normalizedY * normalizedY) * (width * 0.45f);
                float waveOffset = Mathf.Sin(normalizedY * Mathf.PI * 4f) * (width * 0.08f);
                
                for (int x = 0; x < width; x++)
                {
                    float distFromCenter = Mathf.Abs(x - centerX - waveOffset);
                    float alpha = 0f;
                    
                    if (distFromCenter < flameWidth)
                    {
                        float edgeFade = 1f - (distFromCenter / flameWidth);
                        float topFade = 1f - (normalizedY * normalizedY * 0.8f);
                        alpha = edgeFade * edgeFade * topFade;
                    }
                    
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
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
        /// Get aura color based on rank
        /// </summary>
        public static Color GetAuraColorForRank(MobRankTier rank)
        {
            switch (rank)
            {
                case MobRankTier.SSS:  // Violet/Purple
                    return new Color(0.75f, 0.35f, 1f, 1f);
                case MobRankTier.SS:   // Orange/Red
                    return new Color(1f, 0.45f, 0.2f, 1f);
                case MobRankTier.S:    // Gold (Super Saiyan!)
                    return new Color(1f, 0.85f, 0.25f, 1f);
                case MobRankTier.A:    // Blue
                    return new Color(0.35f, 0.65f, 1f, 1f);
                case MobRankTier.B:    // White/Silver
                    return new Color(0.95f, 0.95f, 1f, 1f);
                default:           // F-D - No aura
                    return Color.clear;
            }
        }
        
        /// <summary>
        /// Get aura color based on level (legacy, maps to rank)
        /// </summary>
        public static Color GetAuraColor(int level)
        {
            MobRankTier rank = GetRankFromLevel(level);
            return GetAuraColorForRank(rank);
        }
        
        /// <summary>
        /// Helper to convert level to rank
        /// </summary>
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
        
        /// <summary>
        /// Get secondary/accent color for inner flames
        /// </summary>
        public static Color GetInnerColorForRank(MobRankTier rank)
        {
            Color main = GetAuraColorForRank(rank);
            return Color.Lerp(main, Color.white, 0.6f);
        }
        
        /// <summary>
        /// Get secondary/accent color for inner flames (legacy)
        /// </summary>
        public static Color GetInnerColor(int level)
        {
            Color main = GetAuraColor(level);
            return Color.Lerp(main, Color.white, 0.6f);
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
                default: return 0;
            }
        }
        
        public static int GetFlameCount(int level)
        {
            MobRankTier rank = GetRankFromLevel(level);
            return GetFlameCountForRank(rank);
        }
        
        /// <summary>
        /// Get flame height - scales with rank, with bonus scaling within rank tier
        /// BIGGER FLAMES for more impressive effect
        /// </summary>
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
                default: return 0f;
            }
            return Mathf.Min(height, 15f); // Cap at 15 tiles
        }
        
        /// <summary>
        /// Get flame height - scales dramatically with level within each rank tier
        /// </summary>
        public static float GetFlameHeight(int level)
        {
            MobRankTier rank = GetRankFromLevel(level);
            return GetFlameHeightForRank(rank, level);
        }
        
        /// <summary>
        /// Get aura radius - scales with rank
        /// WIDER AURA for more impressive effect
        /// </summary>
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
                default: return 0f;
            }
            return Mathf.Min(radius, 4f); // Cap at 4 tiles
        }
        
        /// <summary>
        /// Get aura radius - scales dramatically with level
        /// </summary>
        public static float GetAuraRadius(int level)
        {
            MobRankTier rank = GetRankFromLevel(level);
            return GetAuraRadiusForRank(rank, level);
        }
        
        /// <summary>
        /// Get flame width - scales with rank
        /// </summary>
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
                default: return FLAME_WIDTH;
            }
            return Mathf.Min(width, 3f); // Cap at 3 tiles
        }
        
        /// <summary>
        /// Get flame width - also scales with level for thicker flames at higher levels
        /// </summary>
        public static float GetFlameWidth(int level)
        {
            MobRankTier rank = GetRankFromLevel(level);
            return GetFlameWidthForRank(rank, level);
        }
        
        /// <summary>
        /// Quantize alpha to fixed steps to prevent unbounded Material cache growth.
        /// Sin-based alpha generates unique floats every frame — quantizing to 20 steps
        /// keeps the cache bounded to ~(numColors * 20) entries.
        /// </summary>
        private static float QuantizeAlpha(float alpha)
        {
            return Mathf.Round(alpha * 20f) / 20f;
        }

        public static Material GetFlameMaterial(Color color, float alpha)
        {
            // Quantize alpha to avoid creating a new Material every frame
            float qAlpha = QuantizeAlpha(alpha);
            Color keyColor = new Color(color.r, color.g, color.b, qAlpha);
            if (!FlameMaterials.TryGetValue(keyColor, out Material mat) || mat == null)
            {
                mat = new Material(ShaderDatabase.MoteGlow);
                mat.mainTexture = FlameTexture;
                mat.color = keyColor;
                FlameMaterials[keyColor] = mat;
            }
            return mat;
        }

        public static Material GetGlowMaterial(Color color, float alpha)
        {
            float qAlpha = QuantizeAlpha(alpha);
            Color keyColor = new Color(color.r, color.g, color.b, qAlpha);
            if (!GlowMaterials.TryGetValue(keyColor, out Material mat) || mat == null)
            {
                mat = new Material(ShaderDatabase.MoteGlow);
                mat.mainTexture = GlowTexture;
                mat.color = keyColor;
                GlowMaterials[keyColor] = mat;
            }
            return mat;
        }
        
        /// <summary>Reusable key list for eviction — avoids allocation each cleanup.</summary>
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
                // If still too large after eviction, clear all
                if (PawnAuraStates.Count > 200)
                    PawnAuraStates.Clear();
            }
            
            if (!PawnAuraStates.TryGetValue(pawnId, out AuraState state) || state.FlamePhases.Length != flameCount)
            {
                state = new AuraState(flameCount);
                PawnAuraStates[pawnId] = state;
            }
            return state;
        }
        
        /// <summary>
        /// Check if pawn should display aura
        /// - Player colonists: when drafted
        /// - Enemies: when hostile (raiders, mechs, etc.)
        /// - Allies: when in combat or hostile to enemies
        /// </summary>
        public static bool ShouldShowAura(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Downed) return false;
            
            // Player colonists: show when drafted
            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                return pawn.Drafted;
            }
            
            // Hostile pawns: always show aura when they're hostile to player
            if (pawn.HostileTo(Faction.OfPlayer))
            {
                return true;
            }
            
            // Allied/neutral pawns: show if they're in a combat-related mental state or attacking something
            if (pawn.InMentalState || pawn.CurJob?.def == JobDefOf.AttackMelee || pawn.CurJob?.def == JobDefOf.AttackStatic)
            {
                return true;
            }
            
            // Friendly faction pawns that are attacking
            if (pawn.Faction != null && !pawn.Faction.HostileTo(Faction.OfPlayer) && pawn.mindState?.enemyTarget != null)
            {
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Draw DBZ-style aura with flames and glow
        /// Uses rank-based logic for aura appearance
        /// Shows for: drafted colonists, hostile pawns, allied fighters
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
                
                // No aura for ranks below B
                if (rank < MobRankTier.B) return;
                
                Color auraColor = GetAuraColorForRank(rank);
                
                // Use pawn's favorite color if setting is enabled and pawn has one
                if (IsekaiLevelingSettings.auraUseFavoriteColor && pawn.story?.favoriteColor != null)
                {
                    Color favColor = pawn.story.favoriteColor.color;
                    auraColor = new Color(favColor.r, favColor.g, favColor.b, 1f);
                }
                
                if (auraColor.a <= 0) return;
                
                Color innerColor = GetInnerColorForRank(rank);
                int flameCount = GetFlameCountForRank(rank);
                float flameHeight = GetFlameHeightForRank(rank, level);
                float flameWidth = GetFlameWidthForRank(rank, level);
                float radius = GetAuraRadiusForRank(rank, level);
                float baseAlpha = IsekaiLevelingSettings.auraOpacity;
                float sizeMultiplier = IsekaiLevelingSettings.auraSizeMultiplier;
                
                flameHeight *= sizeMultiplier;
                flameWidth *= sizeMultiplier;
                radius *= sizeMultiplier;
                
                AuraState state = GetAuraState(pawn.thingIDNumber, flameCount);
                float time = Time.time;
                
                // Pulse effect
                float pulse = 1f;
                float pulseSpeed = IsekaiLevelingSettings.auraPulseSpeed;
                if (IsekaiLevelingSettings.enableAuraPulse)
                {
                    pulse = 1f + Mathf.Sin(time * 3f * pulseSpeed) * 0.12f;
                }
                
                // === DRAW GLOW UNDERNEATH ===
                float glowSize = flameHeight * 1.2f + radius * 0.5f;
                DrawGlow(drawPos, auraColor, baseAlpha * 0.5f * pulse, glowSize);
                
                // === DRAW OUTER FLAMES ===
                DrawFlameRing(drawPos, state, flameCount, flameHeight, radius, auraColor, baseAlpha, time, pulse, flameWidth);
                
                // === DRAW INNER FLAMES (for S rank and above) ===
                if (rank >= MobRankTier.S)
                {
                    int innerCount = flameCount / 2;
                    float innerRadius = radius * 0.5f;
                    float innerHeight = flameHeight * 0.7f;
                    float innerWidth = flameWidth * 0.7f;
                    DrawFlameRing(drawPos, state, innerCount, innerHeight, innerRadius, innerColor, baseAlpha * 0.9f, time + 0.5f, pulse, innerWidth);
                }
            }
            catch { /* Silently ignore aura rendering errors */ }
        }
        
        private static void DrawGlow(Vector3 drawPos, Color color, float alpha, float size)
        {
            Vector3 glowPos = drawPos;
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
                                          float radius, Color color, float baseAlpha, float time, float pulse, float flameWidth)
        {
            for (int i = 0; i < flameCount; i++)
            {
                float angle = (i / (float)flameCount) * 360f;
                float radians = angle * Mathf.Deg2Rad;
                
                float phaseOffset = state.FlamePhases[i % state.FlamePhases.Length];
                float animatedHeight = flameHeight * (0.75f + Mathf.Sin(time * 5f + phaseOffset) * 0.25f) * pulse;
                float sway = Mathf.Sin(time * 4f + phaseOffset) * 0.04f;
                
                float x = Mathf.Cos(radians) * radius + sway;
                float z = Mathf.Sin(radians) * radius * 0.55f;
                
                Vector3 flamePos = drawPos + new Vector3(x, 0, z);
                flamePos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                
                float flameAlpha = baseAlpha * (0.65f + Mathf.Sin(time * 6f + phaseOffset * 1.5f) * 0.35f);
                float animatedWidth = flameWidth * (0.85f + Mathf.Sin(time * 7f + phaseOffset) * 0.15f);
                
                Material flameMat = GetFlameMaterial(color, flameAlpha);
                
                Matrix4x4 matrix = Matrix4x4.TRS(
                    flamePos + new Vector3(0, animatedHeight * 0.5f, 0),
                    Quaternion.identity,
                    new Vector3(animatedWidth, animatedHeight, 1f)
                );
                
                Graphics.DrawMesh(MeshPool.plane10, matrix, flameMat, 0);
            }
        }
        
        public static void CleanupPawn(int pawnId)
        {
            PawnAuraStates.Remove(pawnId);
        }

        /// <summary>
        /// Clear all caches. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// Materials are GPU resources — clearing them allows GC + GPU reclamation.
        /// </summary>
        public static void ClearCaches()
        {
            FlameMaterials.Clear();
            GlowMaterials.Clear();
            PawnAuraStates.Clear();
        }
        
    }
    
    /// <summary>
    /// Harmony patch to draw auras when pawns are drawn
    /// Uses Pawn.DrawAt which is called during pawn rendering
    /// Guarded: DrawAt signature may differ between RimWorld versions
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
            
            // Check if pawn qualifies for aura (drafted colonist, hostile enemy, or allied fighter)
            if (!AuraSystem.ShouldShowAura(__instance)) return;
            
            // O(1) cached lookup
            var comp = IsekaiComponent.GetCached(__instance);
            if (comp == null) return;
            
            AuraSystem.DrawAura(__instance, comp, drawLoc);
        }
    }
}
