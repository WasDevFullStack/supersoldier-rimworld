using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.WorldBoss;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Draw elite mob names above their heads with smooth fade animation.
    /// Guarded: uses string method name for stability across versions.
    /// </summary>
    [StaticConstructorOnStartup]
    [HarmonyPatch]
    public static class Patch_EliteNameOverlay
    {
        static bool Prepare()
        {
            var method = AccessTools.Method(typeof(PawnUIOverlay), "DrawPawnGUIOverlay");
            if (method == null)
            {
                Log.Warning("[Isekai] PawnUIOverlay.DrawPawnGUIOverlay not found \u2014 elite name overlay patch skipped");
                return false;
            }
            return true;
        }

        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(PawnUIOverlay), "DrawPawnGUIOverlay");
        }
        // Cached reflection field — resolved once, not per frame
        private static readonly FieldInfo pawnField = AccessTools.Field(typeof(PawnUIOverlay), "pawn");

        // Cached texture for elite title background
        private static Texture2D eliteTitleBg;
        private static Texture2D worldBossTitleBg;
        private static Texture2D bossHealthBarBg;
        private static Texture2D healthBarFillTex;
        private static bool textureLoaded = false;

        // Animation state tracking
        private class AnimationState
        {
            public float progress = 0f;      // 0 = hidden, 1 = fully visible
            public bool isHovering = false;
            public float lastUpdateTime = 0f;
        }

        private static Dictionary<int, AnimationState> animationStates = new Dictionary<int, AnimationState>();
        private const float ANIMATION_DURATION = 0.25f; // 0.25 seconds for smooth animation

        private static void LoadTexture()
        {
            if (textureLoaded) return;
            textureLoaded = true;
            eliteTitleBg = ContentFinder<Texture2D>.Get("UI/elite_title", false);
            worldBossTitleBg = ContentFinder<Texture2D>.Get("UI/worldboss_title", false);
            bossHealthBarBg = ContentFinder<Texture2D>.Get("UI/bosshealthbar", false);
            // Create a simple white 1x1 texture for the health fill bar
            healthBarFillTex = SolidColorMaterials.NewSolidColorTexture(Color.white);
        }

        // Cubic bezier easing (ease-in-out) for smooth animation
        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        [HarmonyPostfix]
        public static void Postfix(PawnUIOverlay __instance)
        {
            // Early-out: skip ALL work when the overlay is disabled (perf-critical)
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.ShowEliteMobTitles)
                return;

            if (pawnField == null) return;

            // Load texture if needed
            LoadTexture();

            Pawn pawn = pawnField.GetValue(__instance) as Pawn;
            if (pawn == null || !pawn.Spawned) return;
            if (pawn.RaceProps != null && pawn.RaceProps.Humanlike) return;

            var rankComp = pawn.TryGetComp<MobRankComponent>();
            if (rankComp == null) return;
            if (!rankComp.IsInitialized) return;

            if (!rankComp.IsElite) return;

            if (pawn.Name == null) return;
            string nameText = pawn.Name.ToStringShort;
            if (string.IsNullOrWhiteSpace(nameText)) return;

            // Only show when zoomed in enough (Close or Middle zoom only)
            CameraZoomRange currentZoom = Find.CameraDriver.CurrentZoom;
            if (currentZoom != CameraZoomRange.Closest && currentZoom != CameraZoomRange.Close && currentZoom != CameraZoomRange.Middle) return;

            // Check if hovering
            IntVec3 mouseCell = Verse.UI.MouseCell();
            bool isHovering = false;
            float bodySize = pawn.BodySize; // Get body size once for reuse
            if (mouseCell.InBounds(pawn.Map))
            {
                float distToPawn = (mouseCell.ToVector3() - pawn.Position.ToVector3()).MagnitudeHorizontal();
                // Cap detection radius for world bosses so their huge bodySize doesn't make the hitbox too large
                float clampedBodySize = Mathf.Min(bodySize, 3f);
                float detectionRadius = Mathf.Max(clampedBodySize, 1.5f) + 0.5f;
                isHovering = distToPawn <= detectionRadius;
            }

            // Get or create animation state
            int pawnId = pawn.thingIDNumber;
            if (!animationStates.TryGetValue(pawnId, out AnimationState state))
            {
                state = new AnimationState();
                animationStates[pawnId] = state;
            }

            // Update animation progress
            float currentTime = Time.realtimeSinceStartup;
            float deltaTime = currentTime - state.lastUpdateTime;
            state.lastUpdateTime = currentTime;

            if (isHovering && state.progress < 1f)
            {
                // Fade in
                state.progress = Mathf.Min(1f, state.progress + deltaTime / ANIMATION_DURATION);
            }
            else if (!isHovering && state.progress > 0f)
            {
                // Fade out
                state.progress = Mathf.Max(0f, state.progress - deltaTime / ANIMATION_DURATION);
            }

            state.isHovering = isHovering;

            // Don't render if fully hidden
            if (state.progress <= 0f)
            {
                // Clean up old entries — avoid LINQ allocations on this render-hot path
                if (animationStates.Count > 100)
                {
                    CleanupStaleAnimationStates();
                }
                return;
            }

            // Apply easing curve
            float easedProgress = EaseInOutCubic(state.progress);
            float alpha = easedProgress;

            // Staggered title animation: title fades in after health bar, fades out before it
            float titleDelayNorm = 0.3f; // title starts at 40% of overall progress
            float titleProgress = Mathf.Clamp01((state.progress - titleDelayNorm) / (1f - titleDelayNorm));
            float titleEased = EaseInOutCubic(titleProgress);
            float titleAlpha = titleEased;

            Vector2 labelPos = GenMapUI.LabelDrawPosFor(pawn, 0f);
            
            // Calculate dynamic offset based on mob size to prevent overlap
            float baseOffset = 45f + (bodySize * 15f); // Scale with creature size
            labelPos.y -= baseOffset;
            
            // Add vertical slide-up animation (starts 20 pixels below, moves up)
            float verticalOffset = (1f - easedProgress) * 20f;
            labelPos.y += verticalOffset;

            Color oldColor = GUI.color;
            TextAnchor oldAnchor = Text.Anchor;
            GameFont oldFont = Text.Font;

            // Calculate text width to scale background
            Text.Font = GameFont.Small;
            string fullText = "★ " + nameText;
            float textWidth = Text.CalcSize(fullText).x;

            bool isWorldBoss = WorldBossSizePatch.IsWorldBoss(pawn);
            float bgWidth;
            float bgHeight;
            Texture2D titleBgTex;

            if (isWorldBoss && worldBossTitleBg != null)
            {
                // World boss background scales proportionally with text (native 200x60)
                bgWidth = Mathf.Max(textWidth + 60f, 200f);
                float scale = bgWidth / 200f;
                bgHeight = 60f * scale;
                titleBgTex = worldBossTitleBg;
            }
            else
            {
                // Regular elites use elastic background scaled to text
                bgWidth = Mathf.Max(textWidth + 30f, 120f);
                bgHeight = 34f;
                titleBgTex = eliteTitleBg;
            }

            // Draw background texture with alpha
            if (titleBgTex != null)
            {
                Rect bgRect = new Rect(labelPos.x - bgWidth / 2f, labelPos.y, bgWidth, bgHeight);
                GUI.color = new Color(1f, 1f, 1f, titleAlpha);
                GUI.DrawTexture(bgRect, titleBgTex);
            }

            float currentX = labelPos.x - textWidth / 2f;
            float textY = labelPos.y + (bgHeight - 20f) / 2f + 6f; // Vertically center text in background, nudged down

            // Draw star in rank color with delayed title alpha
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float starWidth = Text.CalcSize("★ ").x;
            Rect starRect = new Rect(currentX, textY, starWidth, 24f);
            // World boss star matches health bar crimson; regular elites use rank color
            Color starColor = isWorldBoss ? new Color(0.431f, 0.082f, 0.153f) : rankComp.RankColor;
            GUI.color = new Color(starColor.r, starColor.g, starColor.b, titleAlpha);
            Widgets.Label(starRect, "★ ");
            currentX += starWidth;

            // Draw name text in white with delayed title alpha
            float nameWidth = Text.CalcSize(nameText).x;
            Rect nameRect = new Rect(currentX, textY, nameWidth, 24f);
            GUI.color = new Color(1f, 1f, 1f, titleAlpha);
            Widgets.Label(nameRect, nameText);

            // === WORLD BOSS HEALTH BAR ===
            // Draw a health bar below the title for world boss pawns only
            // Background texture is 471x87, fill area inside is 347x44
            if (WorldBossSizePatch.IsWorldBoss(pawn) && pawn.health != null)
            {
                float hpPct = pawn.health.summaryHealth.SummaryHealthPercent;
                
                // Render the background at a fixed width, maintaining the 471:87 aspect ratio
                float hpBarWidth = 220f;
                float scale = hpBarWidth / 471f;
                float hpBarHeight = 87f * scale; // ~40.6px

                // Center below the title with a small gap
                float hpBarY = labelPos.y + bgHeight + 2f;
                float hpBarX = labelPos.x - hpBarWidth / 2f;

                // Draw background texture (the ornate frame with skull)
                if (bossHealthBarBg != null)
                {
                    Rect hpBgRect = new Rect(hpBarX, hpBarY, hpBarWidth, hpBarHeight);
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    GUI.DrawTexture(hpBgRect, bossHealthBarBg);
                }

                // Fill area positioned proportionally inside the background
                // In the 471x87 source: fill starts after skull area, y centered
                // Fill area is 347x44
                float fillOffsetX = 92f * scale;
                float fillOffsetY = 21.5f * scale;
                float fillMaxWidth = 347f * scale;
                float fillHeight = 44f * scale;
                float fillWidth = fillMaxWidth * Mathf.Clamp01(hpPct);

                if (fillWidth > 0f && healthBarFillTex != null)
                {
                    Rect fillRect = new Rect(hpBarX + fillOffsetX, hpBarY + fillOffsetY, fillWidth, fillHeight);
                    // Solid crimson fill: #6e1527
                    GUI.color = new Color(0.431f, 0.082f, 0.153f, alpha);
                    GUI.DrawTexture(fillRect, healthBarFillTex);
                }

                // Draw HP percentage text centered on the fill area
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                string hpText = $"{hpPct * 100f:F0}%";
                Rect hpTextAreaRect = new Rect(hpBarX + fillOffsetX, hpBarY, fillMaxWidth, hpBarHeight);
                // Shadow
                GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
                Widgets.Label(new Rect(hpTextAreaRect.x + 1f, hpTextAreaRect.y + 1f, hpTextAreaRect.width, hpTextAreaRect.height), hpText);
                // White text
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Widgets.Label(hpTextAreaRect, hpText);
            }

            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
            GUI.color = oldColor;
        }

        /// <summary>
        /// Remove stale animation entries without LINQ to avoid GC pressure on render path.
        /// </summary>
        private static readonly List<int> _staleKeys = new List<int>();
        private static void CleanupStaleAnimationStates()
        {
            _staleKeys.Clear();
            int removed = 0;
            foreach (var kvp in animationStates)
            {
                if (kvp.Value.progress <= 0f && !kvp.Value.isHovering)
                {
                    _staleKeys.Add(kvp.Key);
                    if (++removed >= 50) break;
                }
            }
            for (int i = 0; i < _staleKeys.Count; i++)
                animationStates.Remove(_staleKeys[i]);
        }
        
        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// </summary>
        public static void ClearAll()
        {
            animationStates.Clear();
        }
    }
}
