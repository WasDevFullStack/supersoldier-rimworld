using System;
using IsekaiLeveling.MobRanking;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Status tab for tamed/allied non-humanlike creatures (animals, mechs, ghouls, etc.)
    /// Shows the creature's rank, level, and 6 RPG stats (STR/DEX/VIT/INT/WIS/CHA).
    /// Mirrors the pawn ITab_IsekaiStats layout but read-only (no stat allocation).
    /// </summary>
    [StaticConstructorOnStartup]
    public class ITab_CreatureStats : ITab
    {
        // Animation
        private float openTime = -1f;
        private const float ANIMATION_DURATION = 0.25f;
        
        // Button hover state
        private float statsButtonHover = 0f;
        
        // Cached textures for XP bar — lazy-init to stay on main thread
        private static Texture2D xpBarFillTex;
        private static Texture2D xpBarBgTex;
        private static void EnsureBarTextures()
        {
            if (xpBarFillTex == null) xpBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.75f, 0.25f));
            if (xpBarBgTex == null) xpBarBgTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.2f));
        }
        
        // Layout constants (same proportions as pawn tab)
        private const float TAB_WIDTH = 350f;
        private const float TAB_HEIGHT_MAX = 720f;
        
        // Pawn display: 256x442 scaled
        private const float PAWN_FRAME_WIDTH = 154f;
        private const float PAWN_FRAME_HEIGHT_BASE = 265f;
        
        // Stat panel: 543x271 scaled
        private const float STAT_TAB_WIDTH = 326f;
        private const float STAT_TAB_HEIGHT = 180f;
        
        // Dynamic layout
        private float tabHeight;
        private float pawnFrameHeight;

        public override bool IsVisible
        {
            get
            {
                if (SelPawn == null) return false;
                // Only for non-humanlike creatures (humanlike pawns use ITab_IsekaiStats)
                if (SelPawn.RaceProps.Humanlike) return false;
                // Only for tamed/allied (player faction)
                if (SelPawn.Faction != Faction.OfPlayer) return false;
                // Respect mod-settings opt-outs (animals/mechs/anomaly entities) — if the
                // player disabled the category, the stat tab disappears too. Without this
                // newborn animals from tamed parents would still surface a Status tab.
                if (MobRanking.MobRankInjector.IsExcludedFromRanking(SelPawn)) return false;
                // Must have the MobRankComponent with initialized stats
                var rankComp = SelPawn.TryGetComp<MobRankComponent>();
                return rankComp != null && rankComp.IsInitialized;
            }
        }

        public ITab_CreatureStats()
        {
            size = new Vector2(TAB_WIDTH, TAB_HEIGHT_MAX);
            labelKey = "TabStatus";
        }

        /// <summary>Compute effective tab height based on available screen space.</summary>
        private void UpdateDynamicLayout()
        {
            float maxAvailable = Verse.UI.screenHeight - 175f;
            tabHeight = Mathf.Min(TAB_HEIGHT_MAX, maxAvailable);
            
            float s = Mathf.Clamp(tabHeight / TAB_HEIGHT_MAX, 0.75f, 1f);
            pawnFrameHeight = PAWN_FRAME_HEIGHT_BASE * s;
            
            size = new Vector2(TAB_WIDTH, tabHeight);
        }

        protected override void FillTab()
        {
            UpdateDynamicLayout();

            if (!IsekaiLevelingSettings.UseIsekaiUI)
            {
                FillTabVanilla();
                return;
            }

            if (openTime < 0f)
                openTime = Time.realtimeSinceStartup;
            
            Pawn pawn = SelPawn;
            if (pawn == null) return;

            var rankComp = pawn.TryGetComp<MobRankComponent>();
            if (rankComp == null) return;

            Rect fullRect = new Rect(0f, 0f, size.x, size.y);
            
            float elapsed = Time.realtimeSinceStartup - openTime;
            float animProgress = Mathf.Clamp01(elapsed / ANIMATION_DURATION);
            float alpha = 1f - Mathf.Pow(1f - animProgress, 3f);
            
            // Background
            DrawBackground(fullRect, alpha);
            
            // Layout from bottom up
            float bottomPadding = 12f;
            float spacing = 8f;
            
            // Stat panel at bottom
            float statPanelY = fullRect.height - STAT_TAB_HEIGHT - bottomPadding;
            DrawStatPanel(fullRect, rankComp, statPanelY, alpha);
            
            // Stats button above stat panel (only if player-owned)
            float btnHeight = 103f;
            float btnAreaY = statPanelY - btnHeight - spacing;
            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                DrawStatsButton(fullRect, pawn, rankComp, btnAreaY + btnHeight - 30f, alpha);
            }
            
            // XP bar above button (only for player-owned)
            float xpBarHeight = 24f;
            float xpBarY = btnAreaY - xpBarHeight;
            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                DrawXPBar(fullRect, rankComp, xpBarY, alpha);
                xpBarY -= spacing;
            }
            
            // Creature info above XP bar
            float infoHeight = 48f;
            float infoY = xpBarY - infoHeight;
            DrawCreatureInfo(fullRect, pawn, rankComp, infoY, alpha);
            
            // Portrait above info
            float portraitY = infoY - pawnFrameHeight - 2f;
            DrawCreaturePortrait(fullRect, pawn, rankComp, portraitY, alpha);
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Vanilla-style FillTab for creatures using standard RimWorld UI patterns.
        /// </summary>
        private void FillTabVanilla()
        {
            size = new Vector2(TAB_WIDTH, Mathf.Min(420f, Verse.UI.screenHeight - 175f));
            
            Pawn pawn = SelPawn;
            if (pawn == null) return;

            var rankComp = pawn.TryGetComp<MobRankComponent>();
            if (rankComp == null) return;

            Rect fullRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            
            // Portrait at top
            Rect portraitRect = new Rect(fullRect.x + (fullRect.width - 100f) / 2f, fullRect.y, 100f, 100f);
            try
            {
                RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(200f, 200f), Rot4.South, new Vector3(0f, 0f, 0.1f), 1.15f, true, true, true, true, null, null, false);
                if (portrait != null) GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                else Widgets.ThingIcon(portraitRect, pawn);
            }
            catch { Widgets.ThingIcon(portraitRect, pawn); }
            
            float curY = portraitRect.yMax + 6f;
            
            // Name
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 24f), pawn.LabelShortCap);
            curY += 24f;
            
            // Rank + Level
            string rankStr = MobRankUtility.GetRankString(rankComp.Rank);
            string title = MobRankUtility.GetRankTitle(rankComp.Rank);
            Color rankColor = MobRankUtility.GetRankColor(rankComp.Rank);
            
            Text.Font = GameFont.Small;
            GUI.color = rankColor;
            string eliteStr = rankComp.IsElite ? " ★" : "";
            Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 20f), $"{title}{eliteStr} — {"Isekai_LevelRankDisplay".Translate(rankComp.currentLevel, rankStr)}");
            GUI.color = Color.white;
            curY += 24f;
            Text.Anchor = TextAnchor.UpperLeft;
            
            // XP bar (player-owned only)
            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                int currentXP = rankComp.currentXP;
                int xpForNext = rankComp.XPToNextLevel;
                float xpFill = xpForNext > 0 ? Mathf.Clamp01((float)currentXP / xpForNext) : 1f;
                
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 16f), $"XP: {NumberFormatting.FormatNum(currentXP)} / {NumberFormatting.FormatNum(xpForNext)}");
                curY += 14f;
                
                Rect xpBarRect = new Rect(fullRect.x, curY, fullRect.width, 12f);
                EnsureBarTextures();
                Widgets.FillableBar(xpBarRect, xpFill, xpBarFillTex, xpBarBgTex, false);
                curY += 18f;
            }
            
            curY += 2f;
            Widgets.DrawLineHorizontal(fullRect.x, curY, fullRect.width);
            curY += 6f;
            
            // Stats in 2 columns (read-only)
            float colWidth = (fullRect.width - 10f) / 2f;
            var statEntries = new (string label, int value, Color color)[]
            {
                ("Isekai_Stat_STR".Translate(), rankComp.stats.strength, new Color(0.95f, 0.45f, 0.4f)),
                ("Isekai_Stat_VIT".Translate(), rankComp.stats.vitality, new Color(0.95f, 0.65f, 0.35f)),
                ("Isekai_Stat_DEX".Translate(), rankComp.stats.dexterity, new Color(0.45f, 0.9f, 0.5f)),
                ("Isekai_Stat_INT".Translate(), rankComp.stats.intelligence, new Color(0.45f, 0.65f, 0.98f)),
                ("Isekai_Stat_WIS".Translate(), rankComp.stats.wisdom, new Color(0.75f, 0.55f, 0.95f)),
                ("Isekai_Stat_CHA".Translate(), rankComp.stats.charisma, new Color(0.98f, 0.88f, 0.35f)),
            };
            
            for (int i = 0; i < statEntries.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = fullRect.x + col * (colWidth + 10f);
                float y = curY + row * 24f;
                
                // Color bar
                GUI.color = statEntries[i].color;
                GUI.DrawTexture(new Rect(x, y + 3f, 3f, 16f), BaseContent.WhiteTex);
                GUI.color = Color.white;
                
                // Label + value
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x + 6f, y, 35f, 22f), statEntries[i].label);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(x + 6f, y, colWidth - 6f, 22f), statEntries[i].value.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            curY += 3 * 24f + 8f;
            
            // Stats button (player-owned)
            if (pawn.Faction != null && pawn.Faction.IsPlayer)
            {
                string btnLabel = "Isekai_Stats".Translate();
                int available = rankComp.stats.availableStatPoints;
                if (available > 0) btnLabel += $" ({available})";
                
                if (Widgets.ButtonText(new Rect(fullRect.x, curY, fullRect.width, 30f), btnLabel))
                {
                    Find.WindowStack.Add(new Window_CreatureStats(pawn));
                }
            }
            
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        protected override void CloseTab()
        {
            base.CloseTab();
            openTime = -1f;
        }

        // ═══════════════════════════════════════════════════════════════
        // DRAWING HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void DrawBackground(Rect rect, float alpha)
        {
            float expandAmount = 20f;
            Rect expandedRect = new Rect(
                rect.x - expandAmount,
                rect.y - expandAmount,
                rect.width + expandAmount * 2f,
                rect.height + expandAmount * 2f
            );
            
            // Solid color to cover default tab chrome
            GUI.color = new Color(0.08f, 0.07f, 0.07f, 1f);
            GUI.DrawTexture(expandedRect, BaseContent.WhiteTex);
            
            // Custom background texture
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (IsekaiTextures.BackgroundTab != null)
            {
                GUI.DrawTexture(rect, IsekaiTextures.BackgroundTab, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.12f, 0.10f, 0.10f, 0.98f * alpha);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
            }
        }

        private void DrawCreaturePortrait(Rect fullRect, Pawn pawn, MobRankComponent rankComp, float startY, float alpha)
        {
            float frameX = (fullRect.width - PAWN_FRAME_WIDTH) / 2f;
            
            // Rank title above portrait (colored by rank)
            Color rankColor = MobRankUtility.GetRankColor(rankComp.Rank);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(rankColor.r, rankColor.g, rankColor.b, alpha);
            
            string title = MobRankUtility.GetRankTitle(rankComp.Rank);
            if (rankComp.IsElite) title += " ★";
            Rect titleRect = new Rect(0f, startY - 28f, fullRect.width, 24f);
            Widgets.Label(titleRect, title);
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Portrait frame
            Rect frameRect = new Rect(frameX, startY, PAWN_FRAME_WIDTH, pawnFrameHeight);
            
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (IsekaiTextures.PawnDisplay != null)
            {
                GUI.DrawTexture(frameRect, IsekaiTextures.PawnDisplay, ScaleMode.StretchToFill);
            }
            
            // Creature portrait inside the frame
            float portraitWidth = PAWN_FRAME_WIDTH * 0.65f;
            float portraitHeight = pawnFrameHeight * 0.55f;
            float portraitX = frameX + (PAWN_FRAME_WIDTH - portraitWidth) / 2f;
            float portraitY = startY + pawnFrameHeight * 0.18f;
            Rect portraitRect = new Rect(portraitX, portraitY, portraitWidth, portraitHeight);
            
            try
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                
                RenderTexture portrait = PortraitsCache.Get(
                    pawn,
                    new Vector2(portraitWidth * 2f, portraitHeight * 2f),
                    Rot4.South,
                    new Vector3(0f, 0f, 0.1f),
                    1.15f,
                    true, true, true, true, null, null, false
                );
                
                if (portrait != null)
                {
                    GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                }
                else
                {
                    Widgets.ThingIcon(portraitRect, pawn);
                }
            }
            catch
            {
                try
                {
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    Widgets.ThingIcon(portraitRect, pawn);
                }
                catch
                {
                    // Final fallback: first letter of name
                    GUI.color = new Color(IsekaiListing.TextSecondary.r, IsekaiListing.TextSecondary.g, 
                        IsekaiListing.TextSecondary.b, alpha * 0.5f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Medium;
                    string label = pawn.LabelShortCap;
                    Widgets.Label(portraitRect, label.Length > 0 ? label.Substring(0, 1) : "?");
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
            
            GUI.color = Color.white;
        }

        private void DrawCreatureInfo(Rect fullRect, Pawn pawn, MobRankComponent rankComp, float startY, float alpha)
        {
            // Name
            GUI.color = new Color(IsekaiListing.TextPrimary.r, IsekaiListing.TextPrimary.g, 
                IsekaiListing.TextPrimary.b, alpha);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            Rect nameRect = new Rect(0f, startY, fullRect.width, 24f);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            
            // Level + Rank
            string rankStr = MobRankUtility.GetRankString(rankComp.Rank);
            string levelText = "Isekai_LevelRankDisplay".Translate(rankComp.currentLevel, rankStr);
            
            GUI.color = new Color(IsekaiListing.TextSecondary.r, IsekaiListing.TextSecondary.g, 
                IsekaiListing.TextSecondary.b, alpha);
            Text.Font = GameFont.Small;
            
            Rect levelRect = new Rect(0f, startY + 22f, fullRect.width, 20f);
            Widgets.Label(levelRect, levelText);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawXPBar(Rect fullRect, MobRankComponent rankComp, float startY, float alpha)
        {
            float barPadding = 30f;
            float barWidth = fullRect.width - barPadding * 2f;
            float barHeight = 6f;
            float barX = barPadding;
            
            int currentXP = rankComp.currentXP;
            int xpForNext = rankComp.XPToNextLevel;
            float progress = xpForNext > 0 ? Mathf.Clamp01((float)currentXP / xpForNext) : 1f;
            
            // XP label
            GUI.color = new Color(IsekaiListing.TextMuted.r, IsekaiListing.TextMuted.g, 
                IsekaiListing.TextMuted.b, alpha);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(barX, startY, 30f, 14f), "EXP");
            
            // XP values on the right
            GUI.color = new Color(IsekaiListing.TextSecondary.r, IsekaiListing.TextSecondary.g, 
                IsekaiListing.TextSecondary.b, alpha);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(barX, startY, barWidth, 14f), 
                $"{NumberFormatting.FormatNum(currentXP)} / {NumberFormatting.FormatNum(xpForNext)}");
            
            // Bar track
            Rect barRect = new Rect(barX, startY + 14f, barWidth, barHeight);
            GUI.color = new Color(0.08f, 0.07f, 0.07f, alpha);
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);
            
            // Bar fill
            if (progress > 0f)
            {
                Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * progress, barRect.height);
                GUI.color = new Color(IsekaiListing.AccentGold.r, IsekaiListing.AccentGold.g, 
                    IsekaiListing.AccentGold.b, alpha);
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
                
                // Highlight on top edge
                GUI.color = new Color(1f, 1f, 1f, 0.15f * alpha);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, 1f), BaseContent.WhiteTex);
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        private void DrawStatsButton(Rect fullRect, Pawn pawn, MobRankComponent rankComp, float startY, float alpha)
        {
            // Same size as pawn ITab nav buttons (107x103, scaled from 178x171 at 0.6x)
            float btnWidth = 107f;
            float btnHeight = 103f;
            float btnX = (fullRect.width - btnWidth) / 2f;
            Rect btnRect = new Rect(btnX, startY - btnHeight + 30f, btnWidth, btnHeight);
            
            bool isOver = Mouse.IsOver(btnRect);
            float hoverTarget = isOver ? 1f : 0f;
            float hoverSpeed = isOver ? 12f : 8f;
            statsButtonHover = Mathf.Lerp(statsButtonHover, hoverTarget, Time.deltaTime * hoverSpeed);
            
            // Scale from center on hover (same as pawn tab: 1.0 → 1.08)
            float scale = Mathf.Lerp(1f, 1.08f, statsButtonHover);
            Vector2 center = btnRect.center;
            Rect scaledRect = new Rect(
                center.x - (btnRect.width * scale) / 2f,
                center.y - (btnRect.height * scale) / 2f,
                btnRect.width * scale,
                btnRect.height * scale
            );
            
            // Button texture (PrimaryButton, same as pawn "Stats" button)
            Texture2D btnTex = IsekaiTextures.PrimaryButton;
            if (btnTex != null)
            {
                float brightness = 1f + statsButtonHover * 0.25f;
                GUI.color = new Color(brightness, brightness, brightness, alpha);
                GUI.DrawTexture(scaledRect, btnTex, ScaleMode.StretchToFill);
            }
            else
            {
                Color bgColor = Color.Lerp(
                    new Color(0.35f, 0.25f, 0.2f, alpha),
                    new Color(0.5f, 0.38f, 0.25f, alpha),
                    statsButtonHover);
                GUI.color = bgColor;
                GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            }
            
            // Button label in lower portion (same layout as pawn tab nav buttons)
            string label = "Isekai_Stats".Translate();
            int available = rankComp.stats.availableStatPoints;
            if (available > 0)
                label += $" ({available})";
            
            Color textColor = Color.Lerp(IsekaiListing.TextSecondary, IsekaiListing.TextPrimary, statsButtonHover);
            GUI.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(scaledRect.x, scaledRect.y + scaledRect.height * 0.5f, scaledRect.width, scaledRect.height * 0.4f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            GUI.color = Color.white;
            
            // Click handling
            if (isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                Find.WindowStack.Add(new Window_CreatureStats(pawn));
            }
        }

        private void DrawStatPanel(Rect fullRect, MobRankComponent rankComp, float curY, float alpha)
        {
            float panelX = (fullRect.width - STAT_TAB_WIDTH) / 2f;
            Rect panelRect = new Rect(panelX, curY, STAT_TAB_WIDTH, STAT_TAB_HEIGHT);
            
            // Use the StatTab texture if available, otherwise a tinted background
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (IsekaiTextures.StatTab != null)
            {
                GUI.DrawTexture(panelRect, IsekaiTextures.StatTab, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.247f, 0.173f, 0.165f, 0.95f * alpha);
                GUI.DrawTexture(panelRect, BaseContent.WhiteTex);
            }
            
            float padding = 14f;
            Rect contentRect = new Rect(
                panelRect.x + padding, 
                panelRect.y + padding,
                panelRect.width - padding * 2f, 
                panelRect.height - padding * 2f
            );
            
            using (var listing = new IsekaiListing(contentRect, alpha))
            {
                // "Stats" header
                listing.Label("Isekai_Stats".Translate(), GameFont.Small, IsekaiListing.AccentGold);
                listing.GapLine();
                listing.Gap(4f);
                
                // 6 stats in 2 columns (read-only, no + buttons)
                listing.BeginColumns(2, 12f);
                
                listing.StatRowCompact("Isekai_Stat_STR".Translate(), rankComp.stats.strength, IsekaiListing.StatSTR, false);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Stat_VIT".Translate(), rankComp.stats.vitality, IsekaiListing.StatVIT, false);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Stat_DEX".Translate(), rankComp.stats.dexterity, IsekaiListing.StatDEX, false);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Stat_INT".Translate(), rankComp.stats.intelligence, IsekaiListing.StatINT, false);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Stat_WIS".Translate(), rankComp.stats.wisdom, IsekaiListing.StatWIS, false);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Stat_CHA".Translate(), rankComp.stats.charisma, IsekaiListing.StatCHA, false);
                
                listing.EndColumns();
            }
        }
    }
}
