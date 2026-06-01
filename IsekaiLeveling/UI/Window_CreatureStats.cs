using System;
using System.Collections.Generic;
using IsekaiLeveling.MobRanking;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Creature Stats Attribution Window - Allows players to allocate stat points
    /// for tamed/allied creatures. Simplified version of Window_StatsAttribution
    /// that uses MobRankComponent instead of IsekaiComponent.
    /// </summary>
    public class Window_CreatureStats : Window
    {
        private Pawn pawn;
        private MobRankComponent rankComp;
        
        // Pending stat changes (before confirmation)
        private int pendingSTR, pendingVIT, pendingDEX, pendingINT, pendingWIS, pendingCHA;
        private int pointsSpent = 0;
        
        // Hover states for animations
        private Dictionary<string, float> hoverStates = new Dictionary<string, float>();
        private Dictionary<string, float> scaleStates = new Dictionary<string, float>();
        private Dictionary<string, float> shineTimers = new Dictionary<string, float>();
        
        // Tooltip state
        private bool showTooltip = false;
        private IsekaiStatType tooltipStatType;
        private int tooltipStatValue;
        private Rect tooltipStatRect;
        
        // Animation state
        private float openTime;
        private float appearProgress = 0f;
        private const float APPEAR_DURATION = 0.25f;
        private float pointsPulsePhase = 0f;
        
        // Drag state
        private bool isDragging = false;
        private Vector2 dragStartMousePos = Vector2.zero;
        private Vector2 dragStartWindowPos = Vector2.zero;
        
        // Colors (matching pawn stats window)
        private static readonly Color TextPrimary = new Color(0.92f, 0.88f, 0.82f);
        private static readonly Color TextSecondary = new Color(0.70f, 0.65f, 0.58f);
        private static readonly Color TextMuted = new Color(0.50f, 0.47f, 0.43f);
        private static readonly Color AccentGold = new Color(0.85f, 0.72f, 0.45f);
        
        // Stat type colors
        private static readonly Color ColorSTR = new Color(0.95f, 0.45f, 0.4f);
        private static readonly Color ColorVIT = new Color(0.95f, 0.65f, 0.35f);
        private static readonly Color ColorDEX = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color ColorINT = new Color(0.45f, 0.65f, 0.98f);
        private static readonly Color ColorWIS = new Color(0.75f, 0.55f, 0.95f);
        private static readonly Color ColorCHA = new Color(0.98f, 0.88f, 0.35f);
        
        // Scale factor
        private const float SCALE = 0.60f;
        
        // Layout constants
        private const float WINDOW_WIDTH = 708f * SCALE;
        private const float WINDOW_HEIGHT = 850f * SCALE;
        
        private const float STAT_ROW_WIDTH = 180f;
        private const float STAT_ROW_HEIGHT = 50f;
        private const float STAT_GAP = 8f;
        
        private const float BOTTOM_BTN_WIDTH = 163f * SCALE;
        private const float BOTTOM_BTN_HEIGHT = 159f * SCALE;
        private const float GLOBAL_RANK_WIDTH = 250f * SCALE;
        private const float GLOBAL_RANK_HEIGHT = 159f * SCALE;
        
        public override Vector2 InitialSize => new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
        
        protected override float Margin => 0f;
        
        public Window_CreatureStats(Pawn pawn)
        {
            this.pawn = pawn;
            this.rankComp = pawn.TryGetComp<MobRankComponent>();
            
            if (rankComp != null)
            {
                pendingSTR = rankComp.stats.strength;
                pendingVIT = rankComp.stats.vitality;
                pendingDEX = rankComp.stats.dexterity;
                pendingINT = rankComp.stats.intelligence;
                pendingWIS = rankComp.stats.wisdom;
                pendingCHA = rankComp.stats.charisma;
            }
            
            this.doCloseButton = false;
            this.doCloseX = false;
            this.drawShadow = false;
            this.soundAppear = null;
            this.soundClose = null;
            this.draggable = false;
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = true;
            
            this.openTime = Time.realtimeSinceStartup;
            this.appearProgress = 0f;
        }

        public override void Close(bool doCloseSound = true)
        {
            if (GUIUtility.hotControl != 0)
                GUIUtility.hotControl = 0;
            
            base.Close(doCloseSound);
            
            if (Event.current != null)
                Event.current.Use();
        }
        
        public override void WindowOnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
                return;
            }
            
            float timeSinceOpen = Time.realtimeSinceStartup - openTime;
            appearProgress = Mathf.Clamp01(timeSinceOpen / APPEAR_DURATION);
            float easedAppear = EaseOutBack(appearProgress);
            float fadeIn = EaseOutQuad(Mathf.Clamp01(timeSinceOpen / (APPEAR_DURATION * 0.6f)));
            
            float scale = 0.85f + 0.15f * easedAppear;
            Vector2 center = windowRect.center;
            Rect animatedRect = new Rect(
                center.x - (windowRect.width * scale) / 2f,
                center.y - (windowRect.height * scale) / 2f,
                windowRect.width * scale,
                windowRect.height * scale
            );
            
            Find.WindowStack.currentlyDrawnWindow = this;
            
            GUI.color = new Color(1f, 1f, 1f, fadeIn);
            
            // Draw background
            GUI.BeginGroup(animatedRect);
            GUI.color = Color.white;
            Rect bgRect = new Rect(0f, 0f, animatedRect.width, animatedRect.height);
            if (StatsWindowTextures.WindowBg != null)
                GUI.DrawTexture(bgRect, StatsWindowTextures.WindowBg, ScaleMode.StretchToFill);
            else
            {
                GUI.color = new Color(0.12f, 0.10f, 0.10f);
                GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            }
            GUI.color = new Color(1f, 1f, 1f, fadeIn);
            GUI.EndGroup();
            
            // Draw contents
            GUI.BeginGroup(animatedRect);
            try
            {
                Rect innerRect = new Rect(0f, 0f, animatedRect.width, animatedRect.height);
                DoWindowContents(innerRect);
            }
            finally
            {
                GUI.EndGroup();
                Find.WindowStack.currentlyDrawnWindow = null;
            }
            
            // Tooltip outside group
            if (showTooltip && appearProgress >= 1f)
                DrawStatTooltipAbsolute(tooltipStatRect, tooltipStatType, tooltipStatValue);
            
            HandleWindowDrag();
        }
        
        private void HandleWindowDrag()
        {
            if (appearProgress < 1f) return;
            
            Event ev = Event.current;
            if (ev == null) return;
            
            Vector2 mousePos = ev.mousePosition;
            
            if (ev.type == EventType.MouseDown && ev.button == 0 && windowRect.Contains(mousePos))
            {
                isDragging = true;
                dragStartMousePos = mousePos;
                dragStartWindowPos = new Vector2(windowRect.x, windowRect.y);
                int controlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = controlId;
                ev.Use();
            }
            else if (ev.type == EventType.MouseDrag && ev.button == 0 && isDragging)
            {
                Vector2 delta = mousePos - dragStartMousePos;
                Vector2 newPos = dragStartWindowPos + delta;
                float maxX = Verse.UI.screenWidth - windowRect.width;
                float maxY = Verse.UI.screenHeight - windowRect.height;
                windowRect.x = Mathf.Clamp(newPos.x, 0f, maxX);
                windowRect.y = Mathf.Clamp(newPos.y, 0f, maxY);
                dragStartMousePos = mousePos;
                dragStartWindowPos = new Vector2(windowRect.x, windowRect.y);
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0 && isDragging)
            {
                isDragging = false;
                GUIUtility.hotControl = 0;
                ev.Use();
            }
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            if (rankComp == null)
            {
                Close();
                return;
            }
            
            showTooltip = false;
            
            DrawBackground(inRect);
            DrawCloseX(inRect);
            DrawPointsAvailable(inRect);
            DrawCreatureHeader(inRect);
            DrawStatRows(inRect);
            DrawBottomSection(inRect);
            
            GUI.color = Color.white;
        }
        
        private void DrawBackground(Rect inRect)
        {
            GUI.color = Color.white;
            if (StatsWindowTextures.WindowBg != null)
                GUI.DrawTexture(inRect, StatsWindowTextures.WindowBg, ScaleMode.StretchToFill);
            else
            {
                GUI.color = new Color(0.12f, 0.10f, 0.10f);
                GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            }
        }
        
        private void DrawCloseX(Rect inRect)
        {
            Rect baseRect = new Rect(inRect.width - 50f, 22f, 40f, 40f);
            bool isOver = Mouse.IsOver(baseRect);
            float hover = AnimateHover("closeX", isOver);
            float scl = AnimateScale("closeX", isOver, 1f, 1.15f);
            
            Vector2 center = baseRect.center;
            Rect closeRect = new Rect(
                center.x - (baseRect.width * scl) / 2f,
                center.y - (baseRect.height * scl) / 2f,
                baseRect.width * scl,
                baseRect.height * scl
            );
            
            GUI.color = Color.Lerp(TextSecondary, new Color(1f, 0.4f, 0.4f), hover);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(closeRect, "×");
            Text.Anchor = TextAnchor.UpperLeft;
            
            GUI.color = Color.white;
            if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                Event.current.Use();
            if (isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                Close();
            }
        }
        
        private void DrawPointsAvailable(Rect inRect)
        {
            int available = rankComp.stats.availableStatPoints - pointsSpent;
            if (available <= 0) return;
            
            pointsPulsePhase += Time.deltaTime;
            float pulse = PulseSine(pointsPulsePhase, 3f);
            float pulseScale = 1f + pulse * 0.03f;
            
            Rect baseRect = new Rect(16f, 40f, 90f, 80f);
            Vector2 center = baseRect.center;
            Rect pointsRect = new Rect(
                center.x - (baseRect.width * pulseScale) / 2f,
                center.y - (baseRect.height * pulseScale) / 2f,
                baseRect.width * pulseScale,
                baseRect.height * pulseScale
            );
            
            float glowBrightness = 1f + pulse * 0.1f;
            GUI.color = new Color(glowBrightness, glowBrightness, glowBrightness);
            if (StatsWindowTextures.AvailablePoints != null)
                GUI.DrawTexture(pointsRect, StatsWindowTextures.AvailablePoints, ScaleMode.ScaleToFit);
            else
            {
                GUI.color = new Color(0.18f + pulse * 0.05f, 0.16f, 0.15f);
                GUI.DrawTexture(pointsRect, BaseContent.WhiteTex);
            }
            
            Color goldPulse = Color.Lerp(TextPrimary, AccentGold, pulse * 0.5f);
            GUI.color = goldPulse;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(pointsRect.x, pointsRect.y + 8f * pulseScale, pointsRect.width, 36f), available.ToString());
            
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(pointsRect.x, pointsRect.y + 44f, pointsRect.width, 20f), "Isekai_Points".Translate());
            
            GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.7f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(pointsRect.x - 20f, pointsRect.yMax + 2f, pointsRect.width + 40f, 40f),
                "Isekai_BulkAllocHint".Translate());
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private void DrawCreatureHeader(Rect inRect)
        {
            // Creature name
            GUI.color = TextPrimary;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect nameRect = new Rect(0f, 55f, inRect.width, 30f);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            
            // Level and Rank
            string rankStr = MobRankUtility.GetRankString(rankComp.Rank);
            GUI.color = TextSecondary;
            Text.Font = GameFont.Small;
            Rect levelRect = new Rect(0f, 82f, inRect.width, 24f);
            Widgets.Label(levelRect, "Isekai_LevelRankDisplay".Translate(rankComp.currentLevel, rankStr));
            
            // Rank title with color
            Color rankColor = MobRankUtility.GetRankColor(rankComp.Rank);
            string title = MobRankUtility.GetRankTitle(rankComp.Rank);
            if (rankComp.IsElite) title += " ★";
            GUI.color = rankColor;
            Text.Font = GameFont.Tiny;
            Rect titleRect = new Rect(0f, 100f, inRect.width, 18f);
            Widgets.Label(titleRect, title);
            
            // XP Bar
            DrawExpBar(inRect);
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private void DrawExpBar(Rect inRect)
        {
            float barWidth = 180f;
            float barHeight = 8f;
            float barX = (inRect.width - barWidth) / 2f;
            float barY = 120f;
            
            Rect barRect = new Rect(barX, barY, barWidth, barHeight);
            
            int currentXP = rankComp.currentXP;
            int xpForNext = rankComp.XPToNextLevel;
            float progress = xpForNext > 0 ? Mathf.Clamp01((float)currentXP / xpForNext) : 1f;
            
            // Bar background
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);
            GUI.color = new Color(0.3f, 0.28f, 0.25f, 0.8f);
            Widgets.DrawBox(barRect, 1);
            
            // Fill
            if (progress > 0f)
            {
                Rect fillRect = new Rect(barRect.x + 1f, barRect.y + 1f, (barRect.width - 2f) * progress, barRect.height - 2f);
                Color xpColor = new Color(0.4f, 0.55f, 0.9f);
                Color xpColorBright = new Color(0.5f, 0.7f, 1f);
                
                GUI.color = xpColor;
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
                
                Rect highlightRect = new Rect(fillRect.x, fillRect.y, fillRect.width, fillRect.height * 0.4f);
                GUI.color = new Color(xpColorBright.r, xpColorBright.g, xpColorBright.b, 0.4f);
                GUI.DrawTexture(highlightRect, BaseContent.WhiteTex);
            }
            
            // XP text
            GUI.color = TextMuted;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect xpTextRect = new Rect(barX, barY + barHeight + 1f, barWidth, 18f);
            Widgets.Label(xpTextRect, $"{NumberFormatting.FormatNum(currentXP)} / {NumberFormatting.FormatNum(xpForNext)} {"IsekaiXP".Translate()}");
            
            GUI.color = Color.white;
        }
        
        private void DrawStatRows(Rect inRect)
        {
            float bottomSectionHeight = 120f;
            float statRowsHeight = (STAT_ROW_HEIGHT + 6f) * 3;
            float startY = inRect.height - bottomSectionHeight - statRowsHeight - 24f;
            float colGap = 10f;
            float rowGap = 6f;
            float totalWidth = STAT_ROW_WIDTH * 2 + colGap;
            float startX = (inRect.width - totalWidth) / 2f;
            
            // Row 1: STR | VIT
            DrawStatRow(new Rect(startX, startY, STAT_ROW_WIDTH, STAT_ROW_HEIGHT),
                "Isekai_Stat_STR".Translate(), ref pendingSTR, IsekaiStatType.Strength, StatsWindowTextures.StatSTR);
            DrawStatRow(new Rect(startX + STAT_ROW_WIDTH + colGap, startY, STAT_ROW_WIDTH, STAT_ROW_HEIGHT),
                "Isekai_Stat_VIT".Translate(), ref pendingVIT, IsekaiStatType.Vitality, StatsWindowTextures.StatVIT);
            startY += STAT_ROW_HEIGHT + rowGap;
            
            // Row 2: DEX | INT
            DrawStatRow(new Rect(startX, startY, STAT_ROW_WIDTH, STAT_ROW_HEIGHT),
                "Isekai_Stat_DEX".Translate(), ref pendingDEX, IsekaiStatType.Dexterity, StatsWindowTextures.StatDEX);
            DrawStatRow(new Rect(startX + STAT_ROW_WIDTH + colGap, startY, STAT_ROW_WIDTH, STAT_ROW_HEIGHT),
                "Isekai_Stat_INT".Translate(), ref pendingINT, IsekaiStatType.Intelligence, StatsWindowTextures.StatINT);
            startY += STAT_ROW_HEIGHT + rowGap;
            
            // Row 3: WIS | CHA
            DrawStatRow(new Rect(startX, startY, STAT_ROW_WIDTH, STAT_ROW_HEIGHT),
                "Isekai_Stat_WIS".Translate(), ref pendingWIS, IsekaiStatType.Wisdom, StatsWindowTextures.StatWIS);
            DrawStatRow(new Rect(startX + STAT_ROW_WIDTH + colGap, startY, STAT_ROW_WIDTH, STAT_ROW_HEIGHT),
                "Isekai_Stat_CHA".Translate(), ref pendingCHA, IsekaiStatType.Charisma, StatsWindowTextures.StatCHA);
        }
        
        private void DrawStatRow(Rect rect, string abbr, ref int pendingValue, IsekaiStatType type, Texture2D bgTexture)
        {
            string hoverId = $"stat_{abbr}";
            bool isOver = Mouse.IsOver(rect);
            float hover = AnimateHover(hoverId, isOver);
            float scl = AnimateScale(hoverId, isOver, 1f, 1.03f);
            float shine = GetShineProgress(hoverId);
            
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scl) / 2f,
                center.y - (rect.height * scl) / 2f,
                rect.width * scl,
                rect.height * scl
            );
            
            float brightness = 1f + hover * 0.12f;
            GUI.color = new Color(brightness, brightness, brightness, 0.95f + hover * 0.05f);
            if (bgTexture != null)
                GUI.DrawTexture(scaledRect, bgTexture, ScaleMode.StretchToFill);
            else
            {
                GUI.color = new Color(0.2f + hover * 0.05f, 0.18f + hover * 0.05f, 0.17f + hover * 0.05f);
                GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            }
            
            if (shine > 0f)
                DrawShineEffect(scaledRect, shine, Color.white);
            
            // Stat abbreviation
            Color statColor = GetStatColor(type);
            GUI.color = Color.Lerp(statColor, Color.white, hover * 0.2f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 28f, rect.y, 44f, rect.height), abbr);
            
            // Stat value
            GUI.color = TextPrimary;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x + 68f, rect.y, 44f, rect.height), pendingValue.ToString());
            
            // Plus/Minus buttons
            int available = rankComp.stats.availableStatPoints - pointsSpent;
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            float btnSize = 22f;
            float btnY = rect.y + (rect.height - btnSize) / 2f;
            float btnStartX = rect.x + 118f;
            
            // Minus button
            Rect minusRect = new Rect(btnStartX, btnY, btnSize, btnSize);
            bool canDecrease = godMode ? (pendingValue > 0) : CanDecrease(type);
            if (DrawCircleButton(minusRect, "-", $"minus_{abbr}", canDecrease))
            {
                if (canDecrease)
                {
                    int bulk = IsekaiStatAllocation.GetBulkAmount();
                    int minValue = godMode ? 0 : GetOriginalStat(type);
                    int decrease = Math.Min(bulk, pendingValue - minValue);
                    if (decrease > 0)
                    {
                        pendingValue -= decrease;
                        RecalculatePointsSpent();
                    }
                }
            }
            
            // Plus button
            Rect plusRect = new Rect(btnStartX + btnSize + 5f, btnY, btnSize, btnSize);
            int maxStat = IsekaiStatAllocation.GetEffectiveMaxStat();
            bool canIncrease = godMode ? (pendingValue < maxStat) : (available > 0 && pendingValue < maxStat);
            if (DrawCircleButton(plusRect, "+", $"plus_{abbr}", canIncrease))
            {
                if (canIncrease)
                {
                    int bulk = IsekaiStatAllocation.GetBulkAmount();
                    int maxIncrease = maxStat - pendingValue;
                    if (!godMode) maxIncrease = Math.Min(maxIncrease, available);
                    int increase = Math.Min(bulk, maxIncrease);
                    if (increase > 0)
                    {
                        pendingValue += increase;
                        RecalculatePointsSpent();
                    }
                }
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Store tooltip info
            if (Mouse.IsOver(rect))
            {
                showTooltip = true;
                tooltipStatType = type;
                tooltipStatValue = pendingValue;
                tooltipStatRect = new Rect(rect.x + windowRect.x, rect.y + windowRect.y, rect.width, rect.height);
            }
        }
        
        private void DrawBottomSection(Rect inRect)
        {
            float bottomPadding = 35f;
            float bottomY = inRect.height - GLOBAL_RANK_HEIGHT - bottomPadding;
            float sectionGap = 10f;
            
            float totalWidth = GLOBAL_RANK_WIDTH + BOTTOM_BTN_WIDTH * 2 + sectionGap * 2;
            float startX = (inRect.width - totalWidth) / 2f;
            
            // Global Rank section
            Rect rankRect = new Rect(startX, bottomY, GLOBAL_RANK_WIDTH, GLOBAL_RANK_HEIGHT);
            DrawGlobalRank(rankRect);
            
            // Close button
            float btnY = bottomY + (GLOBAL_RANK_HEIGHT - BOTTOM_BTN_HEIGHT) / 2f;
            Rect closeRect = new Rect(startX + GLOBAL_RANK_WIDTH + sectionGap, btnY, BOTTOM_BTN_WIDTH, BOTTOM_BTN_HEIGHT);
            if (DrawBottomButton(closeRect, "Isekai_Close".Translate(), StatsWindowTextures.CloseButton, false))
                Close();
            
            // Confirm button
            Rect confirmRect = new Rect(closeRect.xMax + sectionGap, btnY, BOTTOM_BTN_WIDTH, BOTTOM_BTN_HEIGHT);
            if (DrawBottomButton(confirmRect, "Isekai_Apply".Translate(), StatsWindowTextures.ConfirmButton, true))
            {
                ApplyChanges();
                Close();
            }
        }
        
        private void DrawGlobalRank(Rect rect)
        {
            GUI.color = Color.white;
            if (StatsWindowTextures.GlobalRank != null)
                GUI.DrawTexture(rect, StatsWindowTextures.GlobalRank, ScaleMode.StretchToFill);
            else
            {
                GUI.color = new Color(0.18f, 0.16f, 0.15f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
            }
            
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 8f, rect.width, 14f), "Isekai_GlobalRank".Translate());
            
            string rank = rankComp.GetRankFromLevel();
            GUI.color = AccentGold;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y + 28f, rect.width, 30f), rank);
            
            GUI.color = TextMuted;
            Text.Font = GameFont.Tiny;
            string eliteStr = rankComp.IsElite ? " (Elite)" : "";
            Widgets.Label(new Rect(rect.x, rect.y + 65f, rect.width, 14f), $"Lv{rankComp.currentLevel}{eliteStr}");
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        // ═══════════════════════════════════════════════════════════════
        // UI HELPERS (same patterns as Window_StatsAttribution)
        // ═══════════════════════════════════════════════════════════════
        
        private bool DrawCircleButton(Rect rect, string symbol, string id, bool enabled)
        {
            bool isOver = Mouse.IsOver(rect);
            float hover = AnimateHover(id, isOver && enabled);
            float scl = AnimateScale(id, isOver && enabled, 1f, 1.15f);
            
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scl) / 2f,
                center.y - (rect.height * scl) / 2f,
                rect.width * scl,
                rect.height * scl
            );
            
            Color bgColor = enabled
                ? Color.Lerp(new Color(0.25f, 0.23f, 0.22f), new Color(0.45f, 0.42f, 0.38f), hover)
                : new Color(0.15f, 0.14f, 0.13f);
            
            GUI.color = bgColor;
            GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            
            Color textColor = enabled
                ? Color.Lerp(TextSecondary, Color.white, hover)
                : TextMuted;
            
            GUI.color = textColor;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(scaledRect, symbol);
            
            GUI.color = Color.white;
            
            if (enabled && isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                Event.current.Use();
            if (enabled && isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            
            return false;
        }
        
        private bool DrawBottomButton(Rect rect, string label, Texture2D texture, bool highlighted)
        {
            string btnId = $"btn_{label}";
            bool isOver = Mouse.IsOver(rect);
            float hover = AnimateHover(btnId, isOver);
            float scl = AnimateScale(btnId, isOver, 1f, 1.06f);
            
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scl) / 2f,
                center.y - (rect.height * scl) / 2f,
                rect.width * scl,
                rect.height * scl
            );
            
            float brightness = 1f + hover * 0.2f;
            GUI.color = new Color(brightness, brightness, brightness);
            
            if (texture != null)
                GUI.DrawTexture(scaledRect, texture, ScaleMode.ScaleToFit);
            else
            {
                Color bg = highlighted ? AccentGold : new Color(0.25f, 0.23f, 0.22f);
                bg = Color.Lerp(bg, Color.white, hover * 0.15f);
                GUI.color = bg;
                GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            }
            
            GUI.color = Color.Lerp(TextSecondary, Color.white, hover);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(scaledRect.x, scaledRect.yMax - 32f, scaledRect.width, 20f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            GUI.color = Color.white;
            if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                Event.current.Use();
            if (isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
        
        private void DrawStatTooltipAbsolute(Rect statRect, IsekaiStatType type, int value)
        {
            string title = IsekaiStatInfo.GetStatName(type);
            string description = IsekaiStatInfo.GetCreatureStatDescription(type);
            string effectsStr = IsekaiStatInfo.GetCreatureStatEffects(type, value,
                (pawn?.Faction != null && pawn.Faction.IsPlayer) ? IsekaiLevelingSettings.TamedAnimalBonusRetention : 1f);
            string[] effects = effectsStr.Split('\n');
            
            float tooltipWidth = 280f;
            float lineHeight = 18f;
            float padding = 10f;
            float titleHeight = 24f;
            
            Text.Font = GameFont.Tiny;
            float descHeight = Text.CalcHeight(description, tooltipWidth - padding * 2);
            float tooltipHeight = titleHeight + descHeight + padding + (effects.Length * lineHeight) + padding * 2;
            
            float tooltipX = statRect.xMax + 8f;
            float tooltipY = statRect.y;
            
            if (tooltipX + tooltipWidth > Verse.UI.screenWidth)
                tooltipX = statRect.x - tooltipWidth - 8f;
            if (tooltipY + tooltipHeight > Verse.UI.screenHeight)
                tooltipY = Verse.UI.screenHeight - tooltipHeight - 10f;
            if (tooltipY < 10f)
                tooltipY = 10f;
            
            Rect tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
            
            GUI.color = new Color(0.08f, 0.07f, 0.07f, 0.98f);
            GUI.DrawTexture(tooltipRect, BaseContent.WhiteTex);
            GUI.color = GetStatColor(type) * 0.8f;
            Widgets.DrawBox(tooltipRect, 2);
            
            float curY = tooltipRect.y + padding;
            
            GUI.color = GetStatColor(type);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, titleHeight), title);
            curY += titleHeight;
            
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, descHeight), description);
            curY += descHeight + padding * 0.5f;
            
            GUI.color = new Color(0.3f, 0.28f, 0.25f, 0.5f);
            GUI.DrawTexture(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, 1f), BaseContent.WhiteTex);
            curY += padding;
            
            Text.Font = GameFont.Tiny;
            foreach (string effect in effects)
            {
                if (string.IsNullOrEmpty(effect)) continue;
                GUI.color = TextPrimary;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, lineHeight), effect);
                curY += lineHeight;
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        private void DrawShineEffect(Rect rect, float shineProgress, Color baseColor)
        {
            if (shineProgress <= 0f) return;
            
            float shineWidth = rect.width * 0.4f;
            float shineX = Mathf.Lerp(-shineWidth, rect.width + shineWidth, 1f - shineProgress);
            float shineLeft = rect.x + shineX - shineWidth / 2f;
            float shineRight = shineLeft + shineWidth;
            float clippedLeft = Mathf.Max(shineLeft, rect.x);
            float clippedRight = Mathf.Min(shineRight, rect.xMax);
            
            if (clippedRight <= clippedLeft) return;
            
            float alpha = 0.15f * shineProgress;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(clippedLeft, rect.y, clippedRight - clippedLeft, rect.height), BaseContent.WhiteTex);
        }
        
        // ═══════════════════════════════════════════════════════════════
        // STATE MANAGEMENT
        // ═══════════════════════════════════════════════════════════════
        
        private void RecalculatePointsSpent()
        {
            int originalTotal = rankComp.stats.strength + rankComp.stats.vitality + rankComp.stats.dexterity +
                               rankComp.stats.intelligence + rankComp.stats.wisdom + rankComp.stats.charisma;
            int pendingTotal = pendingSTR + pendingVIT + pendingDEX + pendingINT + pendingWIS + pendingCHA;
            pointsSpent = pendingTotal - originalTotal;
        }
        
        private bool CanDecrease(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return pendingSTR > rankComp.stats.strength;
                case IsekaiStatType.Vitality: return pendingVIT > rankComp.stats.vitality;
                case IsekaiStatType.Dexterity: return pendingDEX > rankComp.stats.dexterity;
                case IsekaiStatType.Intelligence: return pendingINT > rankComp.stats.intelligence;
                case IsekaiStatType.Wisdom: return pendingWIS > rankComp.stats.wisdom;
                case IsekaiStatType.Charisma: return pendingCHA > rankComp.stats.charisma;
                default: return false;
            }
        }
        
        private int GetOriginalStat(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return rankComp.stats.strength;
                case IsekaiStatType.Vitality: return rankComp.stats.vitality;
                case IsekaiStatType.Dexterity: return rankComp.stats.dexterity;
                case IsekaiStatType.Intelligence: return rankComp.stats.intelligence;
                case IsekaiStatType.Wisdom: return rankComp.stats.wisdom;
                case IsekaiStatType.Charisma: return rankComp.stats.charisma;
                default: return 0;
            }
        }
        
        private void ApplyChanges()
        {
            if (rankComp == null) return;
            
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            
            if (godMode)
            {
                rankComp.stats.strength = pendingSTR;
                rankComp.stats.vitality = pendingVIT;
                rankComp.stats.dexterity = pendingDEX;
                rankComp.stats.intelligence = pendingINT;
                rankComp.stats.wisdom = pendingWIS;
                rankComp.stats.charisma = pendingCHA;
            }
            else
            {
                int strChange = pendingSTR - rankComp.stats.strength;
                int vitChange = pendingVIT - rankComp.stats.vitality;
                int dexChange = pendingDEX - rankComp.stats.dexterity;
                int intChange = pendingINT - rankComp.stats.intelligence;
                int wisChange = pendingWIS - rankComp.stats.wisdom;
                int chaChange = pendingCHA - rankComp.stats.charisma;
                
                for (int i = 0; i < strChange; i++) rankComp.stats.AllocatePoint(IsekaiStatType.Strength);
                for (int i = 0; i < vitChange; i++) rankComp.stats.AllocatePoint(IsekaiStatType.Vitality);
                for (int i = 0; i < dexChange; i++) rankComp.stats.AllocatePoint(IsekaiStatType.Dexterity);
                for (int i = 0; i < intChange; i++) rankComp.stats.AllocatePoint(IsekaiStatType.Intelligence);
                for (int i = 0; i < wisChange; i++) rankComp.stats.AllocatePoint(IsekaiStatType.Wisdom);
                for (int i = 0; i < chaChange; i++) rankComp.stats.AllocatePoint(IsekaiStatType.Charisma);
            }
            
            Messages.Message("Isekai_StatsUpdated".Translate(pawn.LabelShortCap), MessageTypeDefOf.PositiveEvent);
        }
        
        // ═══════════════════════════════════════════════════════════════
        // ANIMATION HELPERS
        // ═══════════════════════════════════════════════════════════════
        
        private float AnimateHover(string id, bool isHovered)
        {
            if (!hoverStates.ContainsKey(id))
                hoverStates[id] = 0f;
            
            float target = isHovered ? 1f : 0f;
            float current = hoverStates[id];
            float speed = isHovered ? 12f : 8f;
            hoverStates[id] = Mathf.Lerp(current, target, Time.deltaTime * speed);
            
            if (isHovered && current < 0.1f && !shineTimers.ContainsKey(id))
                shineTimers[id] = Time.realtimeSinceStartup;
            else if (!isHovered && shineTimers.ContainsKey(id))
                shineTimers.Remove(id);
            
            return hoverStates[id];
        }
        
        private float AnimateScale(string id, bool isHovered, float minScale = 1f, float maxScale = 1.05f)
        {
            if (!scaleStates.ContainsKey(id))
                scaleStates[id] = minScale;
            
            float target = isHovered ? maxScale : minScale;
            scaleStates[id] = Mathf.Lerp(scaleStates[id], target, Time.deltaTime * 10f);
            return scaleStates[id];
        }
        
        private float GetShineProgress(string id)
        {
            if (!shineTimers.ContainsKey(id))
                return 0f;
            
            float elapsed = Time.realtimeSinceStartup - shineTimers[id];
            float duration = 0.4f;
            return elapsed > duration ? 0f : 1f - (elapsed / duration);
        }
        
        private float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        
        private float EaseOutBack(float t)
        {
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
        
        private float PulseSine(float phase, float speed = 2f) => (Mathf.Sin(phase * speed) + 1f) / 2f;
        
        private Color GetStatColor(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return ColorSTR;
                case IsekaiStatType.Vitality: return ColorVIT;
                case IsekaiStatType.Dexterity: return ColorDEX;
                case IsekaiStatType.Intelligence: return ColorINT;
                case IsekaiStatType.Wisdom: return ColorWIS;
                case IsekaiStatType.Charisma: return ColorCHA;
                default: return TextPrimary;
            }
        }
    }
}
