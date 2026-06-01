using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Floating status panel that appears next to a pawn.
    /// Shows quick stats, rank, and navigation to other windows.
    /// Clean, professional dark theme design.
    /// </summary>
    [StaticConstructorOnStartup]
    public class FloatingStatusPanel : Window
    {
        private readonly Pawn pawn;
        private readonly IsekaiComponent comp;
        
        // Animation
        private float animationProgress = 0f;
        private const float ANIMATION_DURATION = 0.2f;
        private float openTime;
        
        // Sizing
        private const float PANEL_WIDTH = 240f;
        private const float PANEL_HEIGHT = 320f;
        
        // Colors - Professional dark theme
        private static readonly Color PanelBg = new Color(0.1f, 0.1f, 0.12f, 0.98f);
        private static readonly Color HeaderBg = new Color(0.08f, 0.08f, 0.1f, 1f);
        private static readonly Color CardBg = new Color(0.14f, 0.14f, 0.16f, 1f);
        private static readonly Color BorderColor = new Color(0.25f, 0.25f, 0.3f, 1f);
        private static readonly Color AccentGold = new Color(0.9f, 0.75f, 0.35f);
        private static readonly Color AccentBlue = new Color(0.4f, 0.65f, 1f);
        private static readonly Color TextPrimary = new Color(0.95f, 0.95f, 0.97f);
        private static readonly Color TextSecondary = new Color(0.65f, 0.65f, 0.7f);
        private static readonly Color TextMuted = new Color(0.5f, 0.5f, 0.55f);
        
        // Cached textures for XP bar — lazy-init to stay on main thread
        private static Texture2D xpBarFillTex;
        private static Texture2D xpBarBgTex;
        private static void EnsureBarTextures()
        {
            if (xpBarFillTex == null) xpBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.75f, 0.35f));
            if (xpBarBgTex == null) xpBarBgTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.2f));
        }
        
        public override Vector2 InitialSize => new Vector2(PANEL_WIDTH, PANEL_HEIGHT);
        protected override float Margin => 0f;
        
        public FloatingStatusPanel(Pawn pawn, Vector2 screenPosition)
        {
            this.pawn = pawn;
            this.comp = pawn.GetComp<IsekaiComponent>();
            this.openTime = Time.realtimeSinceStartup;
            
            // Window settings for floating panel - disable all default chrome
            this.doCloseButton = false;
            this.doCloseX = false;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = true;
            this.closeOnAccept = false;
            this.closeOnCancel = true;
            this.preventCameraMotion = false;
            this.drawShadow = false;
            this.doWindowBackground = false;
            this.layer = WindowLayer.SubSuper;
        }
        
        protected override void SetInitialSizeAndPosition()
        {
            Vector2 pos = CalculatePanelPosition();
            this.windowRect = new Rect(pos.x, pos.y, PANEL_WIDTH, PANEL_HEIGHT);
        }
        
        /// <summary>
        /// Calculate the panel position based on pawn's current world position
        /// </summary>
        private Vector2 CalculatePanelPosition()
        {
            if (pawn == null || !pawn.Spawned)
            {
                return new Vector2(Verse.UI.screenWidth * 0.5f, Verse.UI.screenHeight * 0.5f);
            }
            
            // Get pawn's current screen position (raw pixels)
            Vector3 screenPos3D = Find.Camera.WorldToScreenPoint(pawn.DrawPos);
            
            // Convert raw pixel coords to UI-scaled coords
            float uiScale = Prefs.UIScale;
            float x = screenPos3D.x / uiScale + 50f;
            float y = (Screen.height - screenPos3D.y) / uiScale - (PANEL_HEIGHT * 0.4f);
            
            // Keep on screen with padding (using UI-scaled screen dimensions)
            x = Mathf.Clamp(x, 15f, Verse.UI.screenWidth - PANEL_WIDTH - 15f);
            y = Mathf.Clamp(y, 15f, Verse.UI.screenHeight - PANEL_HEIGHT - 15f);
            
            return new Vector2(x, y);
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            if (comp == null || pawn == null || !pawn.Spawned)
            {
                Close();
                return;
            }
            
            // Update position to follow pawn smoothly
            Vector2 targetPos = CalculatePanelPosition();
            float smoothSpeed = 8f;
            windowRect.x = Mathf.Lerp(windowRect.x, targetPos.x, Time.deltaTime * smoothSpeed);
            windowRect.y = Mathf.Lerp(windowRect.y, targetPos.y, Time.deltaTime * smoothSpeed);
            
            if (!IsekaiLevelingSettings.UseIsekaiUI)
            {
                DrawVanillaPanel(inRect);
                return;
            }
            
            // Open animation
            float elapsed = Time.realtimeSinceStartup - openTime;
            animationProgress = Mathf.Clamp01(elapsed / ANIMATION_DURATION);
            
            // Smooth cubic ease-out curve
            float t = animationProgress;
            float easedProgress = 1f - Mathf.Pow(1f - t, 3f);
            
            float displayAlpha = easedProgress;
            float displayScale = Mathf.Lerp(0.92f, 1f, easedProgress);
            
            // Apply fade animation
            GUI.color = new Color(1f, 1f, 1f, displayAlpha);
            
            // Apply scale animation
            if (displayScale < 1f)
            {
                Vector2 pivot = new Vector2(inRect.width * 0.5f, inRect.height * 0.5f);
                Matrix4x4 oldMatrix = GUI.matrix;
                GUIUtility.ScaleAroundPivot(new Vector2(displayScale, displayScale), pivot);
                
                DrawMainPanel(inRect);
                
                GUI.matrix = oldMatrix;
            }
            else
            {
                DrawMainPanel(inRect);
            }
            
            GUI.color = Color.white;
        }
        
        private void DrawVanillaPanel(Rect inRect)
        {
            // Draw vanilla-style window background
            Widgets.DrawWindowBackground(inRect);
            
            float padding = 10f;
            float curY = padding;
            float contentWidth = inRect.width - padding * 2f;
            
            // Pawn name
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(padding, curY, contentWidth - 50f, 30f), pawn.LabelShortCap);
            
            // Level badge
            string levelText = "Isekai_Panel_LevelBadge".Translate(comp.currentLevel);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = AccentGold;
            Widgets.Label(new Rect(padding, curY + 4f, contentWidth, 24f), levelText);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 32f;
            
            // Rank and title
            string rank = CalculateRank();
            Color rankColor = GetRankColor(rank);
            string title = GetRankTitle(rank);
            Text.Font = GameFont.Small;
            GUI.color = rankColor;
            Widgets.Label(new Rect(padding, curY, contentWidth, 22f), "Isekai_Panel_Rank".Translate() + " " + rank + " — " + title);
            GUI.color = Color.white;
            curY += 24f;
            
            Widgets.DrawLineHorizontal(padding, curY, contentWidth);
            curY += 6f;
            
            // Stats - 2-column grid
            Text.Font = GameFont.Small;
            var stats = new (string label, int value)[]
            {
                ("Isekai_Stat_STR".Translate(), comp.stats.strength),
                ("Isekai_Stat_VIT".Translate(), comp.stats.vitality),
                ("Isekai_Stat_DEX".Translate(), comp.stats.dexterity),
                ("Isekai_Stat_INT".Translate(), comp.stats.intelligence),
                ("Isekai_Stat_WIS".Translate(), comp.stats.wisdom),
                ("Isekai_Stat_CHA".Translate(), comp.stats.charisma),
            };
            
            float colWidth = (contentWidth - 8f) / 2f;
            for (int i = 0; i < stats.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = padding + col * (colWidth + 8f);
                float y = curY + row * 22f;
                
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x, y, colWidth - 30f, 20f), stats[i].label);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(x, y, colWidth, 20f), stats[i].value.ToString());
            }
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 3 * 22f + 4f;
            
            // Available stat points
            if (comp.stats.availableStatPoints > 0)
            {
                GUI.color = AccentGold;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(padding, curY, contentWidth, 18f), "Isekai_StatPointsAvailable".Translate(comp.stats.availableStatPoints));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                curY += 20f;
            }
            
            Widgets.DrawLineHorizontal(padding, curY, contentWidth);
            curY += 6f;
            
            // XP bar
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(padding, curY, 30f, 16f), "Isekai_Panel_EXP".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(padding, curY, contentWidth, 16f),
                $"{NumberFormatting.FormatNum(comp.currentXP)} / {NumberFormatting.FormatNum(comp.XPToNextLevel)}");
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 18f;
            
            float xpFill = Mathf.Clamp01((float)comp.currentXP / Mathf.Max(1, comp.XPToNextLevel));
            Rect barRect = new Rect(padding, curY, contentWidth, 12f);
            EnsureBarTextures();
            Widgets.FillableBar(barRect, xpFill, xpBarFillTex, xpBarBgTex, false);
            curY += 18f;
            
            // Allocate Stats button
            if (Widgets.ButtonText(new Rect(padding, curY, contentWidth, 28f), "Isekai_Panel_AllocateStats".Translate()))
            {
                Close();
                OpenIsekaiTab();
            }
        }
        
        private void DrawMainPanel(Rect inRect)
        {
            // Main panel area
            Rect panelRect = new Rect(0f, 0f, PANEL_WIDTH, inRect.height);
            
            // Background (no border)
            GUI.color = PanelBg;
            GUI.DrawTexture(panelRect, BaseContent.WhiteTex);
            
            float curY = 0f;
            float padding = 12f;
            
            // Header section
            curY = DrawHeader(panelRect, curY, padding);
            
            // Separator line
            DrawHorizontalLine(new Rect(panelRect.x + padding, curY, panelRect.width - padding * 2f, 1f), BorderColor);
            curY += 8f;
            
            // Rank card
            curY = DrawRankCard(panelRect, curY, padding);
            
            // Stats grid
            curY = DrawStatsGrid(panelRect, curY, padding);
            
            // XP Progress
            curY = DrawXPProgress(panelRect, curY, padding);
            
            // Action buttons
            DrawActionButtons(panelRect, curY, padding);
        }
        
        private float DrawHeader(Rect inRect, float curY, float padding)
        {
            float headerHeight = 52f;
            Rect headerRect = new Rect(inRect.x, curY, inRect.width, headerHeight);
            
            // Header background
            GUI.color = HeaderBg;
            GUI.DrawTexture(headerRect, BaseContent.WhiteTex);
            
            // Pawn name
            GUI.color = TextPrimary;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(inRect.x + padding, curY + 6f, inRect.width - 70f, 24f);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            
            // Level badge
            DrawLevelBadge(new Rect(inRect.x + inRect.width - 52f, curY + 10f, 42f, 32f));
            
            // Subtitle (class or "Isekai Hero")
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            string subtitle = "Isekai_Panel_Subtitle".Translate();
            Widgets.Label(new Rect(inRect.x + padding, curY + 28f, inRect.width - padding * 2f, 18f), subtitle);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return curY + headerHeight + 4f;
        }
        
        private void DrawLevelBadge(Rect rect)
        {
            // Badge background
            GUI.color = new Color(AccentGold.r * 0.25f, AccentGold.g * 0.2f, AccentGold.b * 0.1f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Badge border
            DrawBorder(rect, AccentGold, 1f);
            
            // Level text
            GUI.color = AccentGold;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "Isekai_Panel_LevelBadge".Translate(comp.currentLevel));
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private float DrawRankCard(Rect inRect, float curY, float padding)
        {
            string rank = CalculateRank();
            Color rankColor = GetRankColor(rank);
            string title = GetRankTitle(rank);
            
            Rect cardRect = new Rect(inRect.x + padding, curY, inRect.width - padding * 2f, 36f);
            
            // Card background
            GUI.color = CardBg;
            GUI.DrawTexture(cardRect, BaseContent.WhiteTex);
            
            // Left accent bar
            GUI.color = rankColor;
            GUI.DrawTexture(new Rect(cardRect.x, cardRect.y, 4f, cardRect.height), BaseContent.WhiteTex);
            
            // Rank label
            GUI.color = TextMuted;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(cardRect.x + 14f, cardRect.y, 40f, cardRect.height), "Isekai_Panel_Rank".Translate());
            
            // Rank value
            GUI.color = rankColor;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(cardRect.x + 54f, cardRect.y, 50f, cardRect.height), rank);
            
            // Title
            GUI.color = new Color(rankColor.r, rankColor.g, rankColor.b, 0.8f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(cardRect.x, cardRect.y, cardRect.width - 10f, cardRect.height), title);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return curY + 44f;
        }
        
        private float DrawStatsGrid(Rect inRect, float curY, float padding)
        {
            float gridWidth = inRect.width - padding * 2f;
            float cellWidth = (gridWidth - 8f) / 2f;
            float cellHeight = 24f;
            float cellSpacing = 4f;
            
            var stats = new (string abbr, string name, int value, Color color)[]
            {
                ("Isekai_Stat_STR".Translate(), "Isekai_Stat_Strength".Translate(), comp.stats.strength, new Color(0.95f, 0.45f, 0.4f)),
                ("Isekai_Stat_VIT".Translate(), "Isekai_Stat_Vitality".Translate(), comp.stats.vitality, new Color(0.95f, 0.65f, 0.35f)),
                ("Isekai_Stat_DEX".Translate(), "Isekai_Stat_Dexterity".Translate(), comp.stats.dexterity, new Color(0.45f, 0.9f, 0.5f)),
                ("Isekai_Stat_INT".Translate(), "Isekai_Stat_Intelligence".Translate(), comp.stats.intelligence, new Color(0.45f, 0.65f, 0.98f)),
                ("Isekai_Stat_WIS".Translate(), "Isekai_Stat_Wisdom".Translate(), comp.stats.wisdom, new Color(0.75f, 0.55f, 0.95f)),
                ("Isekai_Stat_CHA".Translate(), "Isekai_Stat_Charisma".Translate(), comp.stats.charisma, new Color(0.98f, 0.88f, 0.35f)),
            };
            
            for (int i = 0; i < stats.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = inRect.x + padding + col * (cellWidth + 8f);
                float y = curY + row * (cellHeight + cellSpacing);
                
                DrawStatCell(new Rect(x, y, cellWidth, cellHeight), stats[i].abbr, stats[i].value, stats[i].color);
            }
            
            float gridHeight = 3 * (cellHeight + cellSpacing);
            
            // Show available points
            if (comp.stats.availableStatPoints > 0)
            {
                Rect pointsRect = new Rect(inRect.x + padding, curY + gridHeight + 2f, gridWidth, 20f);
                GUI.color = AccentGold;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(pointsRect, "Isekai_StatPointsAvailable".Translate(comp.stats.availableStatPoints));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return curY + gridHeight + 26f;
            }
            
            return curY + gridHeight + 6f;
        }
        
        private void DrawStatCell(Rect rect, string abbr, int value, Color color)
        {
            // Cell background
            GUI.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Stat abbreviation
            GUI.color = color;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 8f, rect.y, 32f, rect.height), abbr);
            
            // Stat value
            GUI.color = TextPrimary;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width - 8f, rect.height), value.ToString());
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        private float DrawXPProgress(Rect inRect, float curY, float padding)
        {
            float barHeight = 8f;
            Rect areaRect = new Rect(inRect.x + padding, curY, inRect.width - padding * 2f, 26f);
            
            // Label row
            GUI.color = TextMuted;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(areaRect.x, areaRect.y, 30f, 16f), "Isekai_Panel_EXP".Translate());
            
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(areaRect.x, areaRect.y, areaRect.width, 16f), 
                $"{NumberFormatting.FormatNum(comp.currentXP)} / {NumberFormatting.FormatNum(comp.XPToNextLevel)}");
            
            // Progress bar background
            Rect barRect = new Rect(areaRect.x, areaRect.y + 16f, areaRect.width, barHeight);
            GUI.color = new Color(0.08f, 0.08f, 0.1f, 1f);
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);
            
            // Progress bar fill
            float fillPercent = Mathf.Clamp01((float)comp.currentXP / Mathf.Max(1, comp.XPToNextLevel));
            if (fillPercent > 0f)
            {
                Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * fillPercent, barRect.height);
                
                // Gradient effect (gold to lighter gold)
                GUI.color = AccentGold;
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
                
                // Shine effect on top
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, fillRect.height * 0.4f), BaseContent.WhiteTex);
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return curY + 34f;
        }
        
        private void DrawActionButtons(Rect inRect, float curY, float padding)
        {
            float buttonWidth = inRect.width - padding * 2f;
            float buttonHeight = 28f;
            
            // Allocate Stats button - opens the ITab_IsekaiStats
            if (DrawButton(new Rect(inRect.x + padding, curY, buttonWidth, buttonHeight), "Isekai_Panel_AllocateStats".Translate(), AccentGold, comp.stats.availableStatPoints > 0))
            {
                Close();
                // Select the pawn and open the ITab
                OpenIsekaiTab();
            }
        }
        
        private void OpenIsekaiTab()
        {
            // Select the pawn to make ITabs available
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);
            
            // Find and open the Isekai Stats tab
            if (pawn.GetInspectTabs() != null)
            {
                foreach (var tab in pawn.GetInspectTabs())
                {
                    if (tab is ITab_IsekaiStats isekaiTab)
                    {
                        // Open the inspect pane with our tab selected
                        Find.MainTabsRoot.SetCurrentTab(RimWorld.MainButtonDefOf.Inspect, false);
                        isekaiTab.OnOpen();
                        break;
                    }
                }
            }
        }
        
        private bool DrawButton(Rect rect, string label, Color accentColor, bool hasNotification)
        {
            bool isHovered = Mouse.IsOver(rect);
            
            // Button background
            GUI.color = isHovered ? new Color(0.2f, 0.2f, 0.24f, 1f) : new Color(0.16f, 0.16f, 0.18f, 1f);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Left accent
            GUI.color = isHovered ? accentColor : new Color(accentColor.r * 0.7f, accentColor.g * 0.7f, accentColor.b * 0.7f, 1f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), BaseContent.WhiteTex);
            
            // Notification dot
            if (hasNotification)
            {
                GUI.color = accentColor;
                Rect dotRect = new Rect(rect.xMax - 16f, rect.y + (rect.height - 8f) / 2f, 8f, 8f);
                GUI.DrawTexture(dotRect, BaseContent.WhiteTex);
            }
            
            // Label
            GUI.color = isHovered ? TextPrimary : TextSecondary;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return Widgets.ButtonInvisible(rect);
        }
        
        private void DrawBorder(Rect rect, Color color, float thickness)
        {
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), BaseContent.WhiteTex); // Top
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), BaseContent.WhiteTex); // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), BaseContent.WhiteTex); // Left
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), BaseContent.WhiteTex); // Right
        }
        
        private void DrawHorizontalLine(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
        }
        
        private string CalculateRank()
        {
            if (comp == null) return "F";

            // Use level-based rank thresholds consistent with IsekaiComponent.GetRankFromLevel
            int level = comp.Level;
            if (level >= 401) return "SSS";
            if (level >= 201) return "SS";
            if (level >= 101) return "S";
            if (level >= 51) return "A";
            if (level >= 26) return "B";
            if (level >= 18) return "C";
            if (level >= 11) return "D";
            if (level >= 6) return "E";
            return "F";
        }
        
        private Color GetRankColor(string rank)
        {
            switch (rank)
            {
                case "SSS": return new Color(1f, 0.9f, 0.3f);      // Brilliant gold
                case "SS": return new Color(1f, 0.75f, 0.35f);     // Orange gold
                case "S": return new Color(0.85f, 0.45f, 0.9f);    // Magenta
                case "A": return new Color(0.4f, 0.9f, 0.55f);     // Emerald
                case "B": return new Color(0.45f, 0.7f, 1f);       // Sky blue
                case "C": return new Color(0.6f, 0.85f, 1f);       // Light cyan
                case "D": return new Color(0.75f, 0.75f, 0.78f);   // Silver
                case "E": return new Color(0.6f, 0.55f, 0.5f);     // Bronze
                case "F": return new Color(0.5f, 0.45f, 0.4f);     // Dull brown
                default: return TextSecondary;
            }
        }
        
        private string GetRankTitle(string rank)
        {
            switch (rank)
            {
                case "SSS": return "Isekai_Title_Overlord".Translate();
                case "SS": return "Isekai_Title_Monarch".Translate();
                case "S": return "Isekai_Title_Legend".Translate();
                case "A": return "Isekai_Title_Hero".Translate();
                case "B": return "Isekai_Title_Champion".Translate();
                case "C": return "Isekai_Title_Warrior".Translate();
                case "D": return "Isekai_Title_Adventurer".Translate();
                case "E": return "Isekai_Title_Apprentice".Translate();
                case "F": return "Isekai_Title_Awakened".Translate();
                default: return "";
            }
        }
    }
}
