using System;
using System.Collections.Generic;
using IsekaiLeveling.Compatibility;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Stats Attribution Window - Allows players to allocate stat points
    /// Uses custom themed assets matching the mockup design
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_StatsAttribution : Window
    {
        private Pawn pawn;
        private IsekaiComponent comp;
        
        // Pending stat changes (before confirmation)
        private int pendingSTR, pendingVIT, pendingDEX, pendingINT, pendingWIS, pendingCHA;
        private int pointsSpent = 0;
        
        // Scroll position for affected stats list
        private Vector2 statsScrollPosition = Vector2.zero;
        
        // Cached XP bar textures — lazy-init to stay on main thread
        private static Texture2D _vanillaXpFill;
        private static Texture2D _vanillaXpBg;
        
        // Hover states for animations
        private Dictionary<string, float> hoverStates = new Dictionary<string, float>();
        
        // Tooltip state - store to draw outside clipping group
        private bool showTooltip = false;
        private IsekaiStatType tooltipStatType;
        private int tooltipStatValue;
        private Rect tooltipStatRect;
        
        // Animation state
        private float openTime;
        private float appearProgress = 0f;
        private const float APPEAR_DURATION = 0.25f;
        private Dictionary<string, float> shineTimers = new Dictionary<string, float>();
        private Dictionary<string, float> scaleStates = new Dictionary<string, float>();
        private float pointsPulsePhase = 0f;
        
        // Colors
        private static readonly Color TextPrimary = new Color(0.92f, 0.88f, 0.82f);
        private static readonly Color TextSecondary = new Color(0.70f, 0.65f, 0.58f);
        private static readonly Color TextMuted = new Color(0.50f, 0.47f, 0.43f);
        private static readonly Color AccentGold = new Color(0.85f, 0.72f, 0.45f);
        
        // Stat type colors (matching status tab)
        private static readonly Color ColorSTR = new Color(0.95f, 0.45f, 0.4f);   // Red
        private static readonly Color ColorVIT = new Color(0.95f, 0.65f, 0.35f);  // Orange
        private static readonly Color ColorDEX = new Color(0.45f, 0.9f, 0.5f);    // Green
        private static readonly Color ColorINT = new Color(0.45f, 0.65f, 0.98f);  // Blue
        private static readonly Color ColorWIS = new Color(0.75f, 0.55f, 0.95f);  // Purple
        private static readonly Color ColorCHA = new Color(0.98f, 0.88f, 0.35f);  // Yellow
        
        // Scale factor to fit UI reasonably (60% of original asset sizes)
        private const float SCALE = 0.60f;
        
        // Layout constants - scaled from original asset sizes
        private const float WINDOW_WIDTH = 708f * SCALE;   // ~425
        private const float WINDOW_HEIGHT = 950f * SCALE;  // ~570
        
        private const float STAT_ROW_WIDTH = 180f;   // Bigger stat rows
        private const float STAT_ROW_HEIGHT = 50f;    // Bigger stat rows
        private const float STAT_GAP = 8f;
        
        // Scaled asset sizes (keeping proportions)
        private const float BOTTOM_BTN_WIDTH = 163f * SCALE;    // ~105
        private const float BOTTOM_BTN_HEIGHT = 159f * SCALE;   // ~103
        private const float GLOBAL_RANK_WIDTH = 250f * SCALE;   // ~150
        private const float GLOBAL_RANK_HEIGHT = 159f * SCALE;  // ~95
        
        // Drag state for manual window dragging
        private bool isDragging = false;
        private Vector2 dragStartMousePos = Vector2.zero;
        private Vector2 dragStartWindowPos = Vector2.zero;
        
        public override Vector2 InitialSize => IsekaiLevelingSettings.UseIsekaiUI 
            ? new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT) 
            : new Vector2(420f, 580f);
        
        // Remove default window background
        protected override float Margin => IsekaiLevelingSettings.UseIsekaiUI ? 0f : 18f;
        
        // Quick mode settings
        private bool quickMode = false;
        private Vector2? customPosition = null;
        
        public Window_StatsAttribution(Pawn pawn) : this(pawn, null, false) { }
        
        public Window_StatsAttribution(Pawn pawn, Vector2? position, bool quickMode = false)
        {
            this.pawn = pawn;
            this.comp = pawn.GetComp<IsekaiComponent>();
            this.quickMode = quickMode;
            this.customPosition = position;
            
            // Initialize pending values from current stats
            if (comp != null)
            {
                pendingSTR = comp.stats.strength;
                pendingVIT = comp.stats.vitality;
                pendingDEX = comp.stats.dexterity;
                pendingINT = comp.stats.intelligence;
                pendingWIS = comp.stats.wisdom;
                pendingCHA = comp.stats.charisma;
            }
            
            this.doCloseButton = false;
            this.doCloseX = false;
            this.drawShadow = false;
            this.soundAppear = null;
            this.soundClose = null;
            this.draggable = false;  // Using custom HandleWindowDrag instead
            
            // Never pause the game — players can keep playing while allocating stats
            this.forcePause = false;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = false;
            
            // Initialize appear animation
            this.openTime = Time.realtimeSinceStartup;
            this.appearProgress = 0f;
        }
        
        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            
            // If custom position provided, use it
            if (customPosition.HasValue)
            {
                Vector2 pos = customPosition.Value;
                
                // Clamp to screen bounds
                float maxX = Verse.UI.screenWidth - windowRect.width;
                float maxY = Verse.UI.screenHeight - windowRect.height;
                pos.x = Mathf.Clamp(pos.x, 0f, maxX);
                pos.y = Mathf.Clamp(pos.y, 0f, maxY);
                
                windowRect.x = pos.x;
                windowRect.y = pos.y;
            }
            // Otherwise, check for saved position from settings (useful for small monitors)
            else if (IsekaiMod.Settings != null && 
                     IsekaiMod.Settings.StatsWindowX >= 0 && 
                     IsekaiMod.Settings.StatsWindowY >= 0)
            {
                float maxX = Verse.UI.screenWidth - windowRect.width;
                float maxY = Verse.UI.screenHeight - windowRect.height;
                windowRect.x = Mathf.Clamp(IsekaiMod.Settings.StatsWindowX, 0f, maxX);
                windowRect.y = Mathf.Clamp(IsekaiMod.Settings.StatsWindowY, 0f, maxY);
            }
        }
        
        /// <summary>
        /// Override Close to properly reset input state and prevent drag selection issues
        /// </summary>
        public override void Close(bool doCloseSound = true)
        {
            // Save window position for next time (useful for small monitors)
            if (IsekaiMod.Settings != null && !quickMode)
            {
                IsekaiMod.Settings.StatsWindowX = windowRect.x;
                IsekaiMod.Settings.StatsWindowY = windowRect.y;
            }
            
            // Reset any hotControl to prevent drag state from persisting
            if (GUIUtility.hotControl != 0)
            {
                GUIUtility.hotControl = 0;
            }
            
            base.Close(doCloseSound);
            
            // Exit GUI processing immediately to prevent leftover events
            if (Event.current != null)
            {
                Event.current.Use();
            }
        }
        
        // Override the WindowOnGUI to skip default background but keep input processing
        public override void WindowOnGUI()
        {
            // Vanilla mode: use standard RimWorld window rendering
            if (!IsekaiLevelingSettings.UseIsekaiUI)
            {
                this.doWindowBackground = true;
                this.doCloseX = true;
                this.draggable = true;
                base.WindowOnGUI();
                return;
            }
            
            this.doWindowBackground = false;
            this.doCloseX = false;
            this.draggable = false;
            
            // Handle escape key to close
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            {
                Close();
                Event.current.Use();
                return;
            }
            
            // Update appear animation
            float timeSinceOpen = Time.realtimeSinceStartup - openTime;
            appearProgress = Mathf.Clamp01(timeSinceOpen / APPEAR_DURATION);
            float easedAppear = EaseOutBack(appearProgress);
            float fadeIn = EaseOutQuad(Mathf.Clamp01(timeSinceOpen / (APPEAR_DURATION * 0.6f)));
            
            // Calculate animated window rect (scale from center)
            float scale = 0.85f + 0.15f * easedAppear;
            Vector2 center = windowRect.center;
            Rect animatedRect = new Rect(
                center.x - (windowRect.width * scale) / 2f,
                center.y - (windowRect.height * scale) / 2f,
                windowRect.width * scale,
                windowRect.height * scale
            );
            
            Find.WindowStack.currentlyDrawnWindow = this;
            
            // Apply fade
            GUI.color = new Color(1f, 1f, 1f, fadeIn);
            
            // Draw our custom background first (covers any default)
            GUI.BeginGroup(animatedRect);
            GUI.color = Color.white;
            Rect bgRect = new Rect(0f, 0f, animatedRect.width, animatedRect.height);
            if (StatsWindowTextures.WindowBg != null)
            {
                GUI.DrawTexture(bgRect, StatsWindowTextures.WindowBg, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.12f, 0.10f, 0.10f);
                GUI.DrawTexture(bgRect, BaseContent.WhiteTex);
            }
            GUI.color = new Color(1f, 1f, 1f, fadeIn);
            GUI.EndGroup();
            
            // Draw window contents with proper event handling
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
            
            // Draw tooltip OUTSIDE the GUI group so it's not clipped by window bounds
            if (showTooltip && appearProgress >= 1f)
            {
                DrawStatTooltipAbsolute(tooltipStatRect, tooltipStatType, tooltipStatValue);
            }
            
            // ===== DRAG HANDLING (after all groups are closed) =====
            // Dragging works on the entire window for easy repositioning
            HandleWindowDrag();
        }
        
        private void HandleWindowDrag()
        {
            // Only handle drag if animation is complete
            if (appearProgress < 1f) return;
            
            Event ev = Event.current;
            if (ev == null) return;
            
            // After GUI.EndGroup(), mousePosition is in screen coordinates (same as windowRect)
            Vector2 mousePos = ev.mousePosition;
            
            // Start drag on left mouse button down within the window
            if (ev.type == EventType.MouseDown && ev.button == 0 && windowRect.Contains(mousePos))
            {
                isDragging = true;
                dragStartMousePos = mousePos;
                dragStartWindowPos = new Vector2(windowRect.x, windowRect.y);
                // Claim hotControl so RimWorld doesn't process this as a map interaction
                int controlId = GUIUtility.GetControlID(FocusType.Passive);
                GUIUtility.hotControl = controlId;
                ev.Use();
            }
            else if (ev.type == EventType.MouseDrag && ev.button == 0 && isDragging)
            {
                // Update position while dragging
                Vector2 delta = mousePos - dragStartMousePos;
                Vector2 newPos = dragStartWindowPos + delta;
                
                // Clamp to screen bounds
                float maxX = Verse.UI.screenWidth - windowRect.width;
                float maxY = Verse.UI.screenHeight - windowRect.height;
                windowRect.x = Mathf.Clamp(newPos.x, 0f, maxX);
                windowRect.y = Mathf.Clamp(newPos.y, 0f, maxY);
                
                // Update the drag start for smooth continuous dragging
                dragStartMousePos = mousePos;
                dragStartWindowPos = new Vector2(windowRect.x, windowRect.y);
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0 && isDragging)
            {
                // End drag and save position
                isDragging = false;
                GUIUtility.hotControl = 0;
                IsekaiMod.Settings.StatsWindowX = windowRect.x;
                IsekaiMod.Settings.StatsWindowY = windowRect.y;
                ev.Use();
            }
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            if (comp == null) 
            {
                Close();
                return;
            }
            
            if (!IsekaiLevelingSettings.UseIsekaiUI)
            {
                DoWindowContentsVanilla(inRect);
                return;
            }
            
            // Reset tooltip flag each frame
            showTooltip = false;
            
            // Draw main background
            DrawBackground(inRect);
            
            // Draw close X in top right
            DrawCloseX(inRect);
            
            // Draw points available (top left)
            DrawPointsAvailable(inRect);
            
            // Draw pawn name and level (center top)
            DrawPawnHeader(inRect);
            
            // Draw stat rows (2 columns)
            DrawStatRows(inRect);
            
            // Draw affected stats detail section
            DrawAffectedStats(inRect);
            
            // Draw bottom section (rank + buttons)
            DrawBottomSection(inRect);
            
            GUI.color = Color.white;
        }
        
        private void DoWindowContentsVanilla(Rect inRect)
        {
            float curY = 0f;
            float w = inRect.width;
            
            // === HEADER: Name + Level/Rank ===
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(0f, curY, w * 0.6f, 30f), pawn.LabelShortCap);
            
            string rank = GetLevelRank(comp.Level);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = AccentGold;
            Widgets.Label(new Rect(0f, curY, w, 30f), "Isekai_LevelRankDisplay".Translate(comp.Level, rank));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 32f;
            
            // === XP BAR ===
            float xpFill = comp.XPToNextLevel > 0 ? Mathf.Clamp01((float)comp.currentXP / comp.XPToNextLevel) : 1f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(0f, curY, w, 14f),
                $"{NumberFormatting.FormatNum(comp.currentXP)} / {NumberFormatting.FormatNum(comp.XPToNextLevel)} {"IsekaiXP".Translate()}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            curY += 15f;
            Rect xpBar = new Rect(0f, curY, w, 8f);
            if (_vanillaXpFill == null) _vanillaXpFill = SolidColorMaterials.NewSolidColorTexture(new Color(0.4f, 0.55f, 0.9f));
            if (_vanillaXpBg == null) _vanillaXpBg = SolidColorMaterials.NewSolidColorTexture(new Color(0.15f, 0.15f, 0.15f));
            Widgets.FillableBar(xpBar, xpFill, _vanillaXpFill, _vanillaXpBg, false);
            curY += 14f;
            
            // === AVAILABLE POINTS ===
            int available = comp.stats.availableStatPoints - pointsSpent;
            if (available > 0)
            {
                GUI.color = AccentGold;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0f, curY, w, 20f), "Isekai_StatPointsAvailable".Translate(available));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                curY += 22f;
            }
            
            // Section separator with accent color
            GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, 0.25f);
            GUI.DrawTexture(new Rect(0f, curY, w, 1f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            curY += 6f;
            
            // Section header
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0f, curY, w, 16f), "STAT ALLOCATION");
            GUI.color = Color.white;
            curY += 18f;
            
            // === STAT ROWS — 2 columns, centered ===
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            int maxStat = IsekaiStatAllocation.GetEffectiveMaxStat();
            
            float colGap = 16f;
            float cellWidth = (w - colGap) / 2f;
            float rowHeight = 32f;
            float rowGap = 4f;
            float btnW = 26f;
            float btnH = 24f;
            
            var statEntries = new (string abbr, IsekaiStatType type, Color color)[]
            {
                ("Isekai_Stat_STR".Translate(), IsekaiStatType.Strength, ColorSTR),
                ("Isekai_Stat_VIT".Translate(), IsekaiStatType.Vitality, ColorVIT),
                ("Isekai_Stat_DEX".Translate(), IsekaiStatType.Dexterity, ColorDEX),
                ("Isekai_Stat_INT".Translate(), IsekaiStatType.Intelligence, ColorINT),
                ("Isekai_Stat_WIS".Translate(), IsekaiStatType.Wisdom, ColorWIS),
                ("Isekai_Stat_CHA".Translate(), IsekaiStatType.Charisma, ColorCHA),
            };
            
            for (int i = 0; i < statEntries.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float cellX = col * (cellWidth + colGap);
                float cellY = curY + row * (rowHeight + rowGap);
                
                ref int pendingVal = ref GetPendingRef(statEntries[i].type);
                int origVal = GetOriginalStat(statEntries[i].type);
                
                // Row background with subtle tint
                Rect cellRect = new Rect(cellX, cellY, cellWidth, rowHeight);
                GUI.color = new Color(1f, 1f, 1f, 0.06f);
                GUI.DrawTexture(cellRect, BaseContent.WhiteTex);
                GUI.color = Color.white;
                
                // Colored left bar (accent strip)
                GUI.color = statEntries[i].color;
                GUI.DrawTexture(new Rect(cellX, cellY + 4f, 3f, rowHeight - 8f), BaseContent.WhiteTex);
                
                // Colored stat label
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(cellX + 8f, cellY, 38f, rowHeight), statEntries[i].abbr);
                GUI.color = Color.white;
                
                // Value — centered in cell, green if changed
                GUI.color = pendingVal != origVal ? new Color(0.5f, 0.9f, 0.5f) : TextPrimary;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(cellX + 42f, cellY, 36f, rowHeight), pendingVal.ToString());
                GUI.color = Color.white;
                
                // -/+ buttons, vertically centered in row
                float btnY = cellY + (rowHeight - btnH) / 2f;
                float btnAreaX = cellX + cellWidth - btnW * 2f - 10f;
                
                bool canDec = godMode ? pendingVal > 0 : CanDecrease(statEntries[i].type);
                if (Widgets.ButtonText(new Rect(btnAreaX, btnY, btnW, btnH), "-", true, true, canDec))
                {
                    if (canDec)
                    {
                        int bulk = IsekaiStatAllocation.GetBulkAmount();
                        int minVal = godMode ? 0 : origVal;
                        int dec = Math.Min(bulk, pendingVal - minVal);
                        if (dec > 0) { pendingVal -= dec; RecalculatePointsSpent(); }
                    }
                }
                
                bool canInc = godMode ? pendingVal < maxStat : (available > 0 && pendingVal < maxStat);
                if (Widgets.ButtonText(new Rect(btnAreaX + btnW + 4f, btnY, btnW, btnH), "+", true, true, canInc))
                {
                    if (canInc)
                    {
                        int bulk = IsekaiStatAllocation.GetBulkAmount();
                        int maxInc = maxStat - pendingVal;
                        if (!godMode) maxInc = Math.Min(maxInc, available);
                        int inc = Math.Min(bulk, maxInc);
                        if (inc > 0) { pendingVal += inc; RecalculatePointsSpent(); }
                    }
                }
                
                Text.Anchor = TextAnchor.UpperLeft;
            }
            curY += 3 * (rowHeight + rowGap) + 4f;
            
            // Section separator
            GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, 0.25f);
            GUI.DrawTexture(new Rect(0f, curY, w, 1f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            curY += 4f;
            
            // Section header
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0f, curY, w, 16f), "AFFECTED STATS");
            GUI.color = Color.white;
            curY += 18f;
            
            // === AFFECTED STATS — fill remaining space ===
            float bottomBtnHeight = 34f;
            float scrollHeight = inRect.height - curY - bottomBtnHeight - 16f;
            Rect scrollOuterRect = new Rect(0f, curY, w, scrollHeight);
            DrawAffectedStatsContent(scrollOuterRect);
            curY += scrollHeight + 6f;
            
            // === BOTTOM BUTTONS ===
            float btnWidth = (w - 8f) / 2f;
            if (Widgets.ButtonText(new Rect(0f, curY, btnWidth, 30f), "Isekai_Close".Translate()))
            {
                Close();
            }
            if (Widgets.ButtonText(new Rect(btnWidth + 8f, curY, btnWidth, 30f), "Isekai_Apply".Translate()))
            {
                ApplyChanges();
                Close();
            }
        }
        
        private ref int GetPendingRef(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return ref pendingSTR;
                case IsekaiStatType.Vitality: return ref pendingVIT;
                case IsekaiStatType.Dexterity: return ref pendingDEX;
                case IsekaiStatType.Intelligence: return ref pendingINT;
                case IsekaiStatType.Wisdom: return ref pendingWIS;
                case IsekaiStatType.Charisma: return ref pendingCHA;
                default: return ref pendingSTR;
            }
        }
        
        private void DrawBackground(Rect inRect)
        {
            GUI.color = Color.white;
            
            if (StatsWindowTextures.WindowBg != null)
            {
                GUI.DrawTexture(inRect, StatsWindowTextures.WindowBg, ScaleMode.StretchToFill);
            }
            else
            {
                // Fallback
                GUI.color = new Color(0.12f, 0.10f, 0.10f);
                GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            }
        }
        
        private void DrawCloseX(Rect inRect)
        {
            // Bigger close button in top right corner (lowered into background)
            Rect baseRect = new Rect(inRect.width - 50f, 22f, 40f, 40f);
            bool isOver = Mouse.IsOver(baseRect);
            float hover = AnimateHover("closeX", isOver);
            float scale = AnimateScale("closeX", isOver, 1f, 1.15f);
            
            // Scale from center
            Vector2 center = baseRect.center;
            Rect closeRect = new Rect(
                center.x - (baseRect.width * scale) / 2f,
                center.y - (baseRect.height * scale) / 2f,
                baseRect.width * scale,
                baseRect.height * scale
            );
            
            // Draw X symbol bigger with color transition
            GUI.color = Color.Lerp(TextSecondary, new Color(1f, 0.4f, 0.4f), hover);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(closeRect, "×");
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Consume MouseDown so RimWorld doesn't start a drag-select
            GUI.color = Color.white;
            if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
            }
            if (isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                Close();
            }
        }
        
        private void DrawPointsAvailable(Rect inRect)
        {
            int available = comp.stats.availableStatPoints - pointsSpent;
            
            // Hide the points UI when no points available
            if (available <= 0)
                return;
            
            // Update pulse animation phase
            pointsPulsePhase += Time.deltaTime;
            float pulse = PulseSine(pointsPulsePhase, 3f);
            float pulseScale = 1f + pulse * 0.03f;
            
            Rect baseRect = new Rect(16f, 40f, 90f, 80f);
            
            // Apply pulse scale from center
            Vector2 center = baseRect.center;
            Rect pointsRect = new Rect(
                center.x - (baseRect.width * pulseScale) / 2f,
                center.y - (baseRect.height * pulseScale) / 2f,
                baseRect.width * pulseScale,
                baseRect.height * pulseScale
            );
            
            // Draw points background texture with subtle glow
            float glowBrightness = 1f + pulse * 0.1f;
            GUI.color = new Color(glowBrightness, glowBrightness, glowBrightness);
            if (StatsWindowTextures.AvailablePoints != null)
            {
                GUI.DrawTexture(pointsRect, StatsWindowTextures.AvailablePoints, ScaleMode.ScaleToFit);
            }
            else
            {
                // Fallback
                GUI.color = new Color(0.18f + pulse * 0.05f, 0.16f, 0.15f);
                GUI.DrawTexture(pointsRect, BaseContent.WhiteTex);
            }
            
            // Draw number with golden pulse
            Color goldPulse = Color.Lerp(TextPrimary, AccentGold, pulse * 0.5f);
            GUI.color = goldPulse;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(pointsRect.x, pointsRect.y + 8f * pulseScale, pointsRect.width, 36f), available.ToString());
            
            // "Points" label
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(pointsRect.x, pointsRect.y + 44f, pointsRect.width, 20f), "Isekai_Points".Translate());
            
            // Bulk allocation hint
            GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.7f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(new Rect(pointsRect.x - 20f, pointsRect.yMax + 2f, pointsRect.width + 40f, 40f), 
                "Isekai_BulkAllocHint".Translate());
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private void DrawPawnHeader(Rect inRect)
        {
            float centerX = inRect.width / 2f;
            
            // Pawn name
            GUI.color = TextPrimary;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            Rect nameRect = new Rect(0f, 55f, inRect.width, 30f);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            
            // Level and Rank
            string rank = GetLevelRank(comp.Level);
            GUI.color = TextSecondary;
            Text.Font = GameFont.Small;
            
            Rect levelRect = new Rect(0f, 82f, inRect.width, 24f);
            Widgets.Label(levelRect, "Isekai_LevelRankDisplay".Translate(comp.Level, rank));
            
            // XP Bar
            DrawExpBar(inRect);
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private void DrawExpBar(Rect inRect)
        {
            float barWidth = 180f;
            float barHeight = 8f;
            float barX = (inRect.width - barWidth) / 2f;
            float barY = 108f;
            
            Rect barRect = new Rect(barX, barY, barWidth, barHeight);
            
            // Calculate XP progress
            int currentXP = comp.currentXP;
            int xpForNext = comp.XPToNextLevel;
            float progress = xpForNext > 0 ? Mathf.Clamp01((float)currentXP / xpForNext) : 1f;
            
            // Draw bar background
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);
            
            // Draw bar border
            GUI.color = new Color(0.3f, 0.28f, 0.25f, 0.8f);
            Widgets.DrawBox(barRect, 1);
            
            // Draw filled portion with gradient-like effect
            if (progress > 0f)
            {
                Rect fillRect = new Rect(barRect.x + 1f, barRect.y + 1f, (barRect.width - 2f) * progress, barRect.height - 2f);
                
                // Use a nice XP color (blueish-purple)
                Color xpColor = new Color(0.4f, 0.55f, 0.9f);
                Color xpColorBright = new Color(0.5f, 0.7f, 1f);
                
                // Main fill
                GUI.color = xpColor;
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
                
                // Highlight on top half for depth
                Rect highlightRect = new Rect(fillRect.x, fillRect.y, fillRect.width, fillRect.height * 0.4f);
                GUI.color = new Color(xpColorBright.r, xpColorBright.g, xpColorBright.b, 0.4f);
                GUI.DrawTexture(highlightRect, BaseContent.WhiteTex);
            }
            
            // Draw XP text below bar
            GUI.color = TextMuted;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect xpTextRect = new Rect(barX, barY + barHeight + 1f, barWidth, 18f);
            Widgets.Label(xpTextRect, $"{NumberFormatting.FormatNum(currentXP)} / {NumberFormatting.FormatNum(xpForNext)} {"IsekaiXP".Translate()}");
            
            GUI.color = Color.white;
        }
        
        private void DrawStatRows(Rect inRect)
        {
            // Anchor from bottom: scaled values
            float bottomSectionHeight = 120f;  // ~200 * 0.6
            float detailHeight = 114f;         // ~190 * 0.6
            float statRowsHeight = (STAT_ROW_HEIGHT + 6f) * 3;
            
            float startY = inRect.height - bottomSectionHeight - detailHeight - statRowsHeight - 12f;
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
            float scale = AnimateScale(hoverId, isOver, 1f, 1.03f);
            float shine = GetShineProgress(hoverId);
            
            // Calculate scaled rect (scale from center)
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scale) / 2f,
                center.y - (rect.height * scale) / 2f,
                rect.width * scale,
                rect.height * scale
            );
            
            // Draw stat row background texture with slight brightness boost on hover
            float brightness = 1f + hover * 0.12f;
            GUI.color = new Color(brightness, brightness, brightness, 0.95f + hover * 0.05f);
            if (bgTexture != null)
            {
                GUI.DrawTexture(scaledRect, bgTexture, ScaleMode.StretchToFill);
            }
            else
            {
                // Fallback
                GUI.color = new Color(0.2f + hover * 0.05f, 0.18f + hover * 0.05f, 0.17f + hover * 0.05f);
                GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            }
            
            // Draw shine effect on hover
            if (shine > 0f)
            {
                DrawShineEffect(scaledRect, shine, Color.white);
            }
            
            // Stat abbreviation - colored by type (with glow on hover)
            Color statColor = GetStatColor(type);
            GUI.color = Color.Lerp(statColor, Color.white, hover * 0.2f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 28f, rect.y, 44f, rect.height), abbr);
            
            // Stat value - white/cream
            GUI.color = TextPrimary;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x + 68f, rect.y, 44f, rect.height), pendingValue.ToString());
            
            // Plus/Minus buttons
            int available = comp.stats.availableStatPoints - pointsSpent;
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            float btnSize = 22f;
            float btnY = rect.y + (rect.height - btnSize) / 2f;
            float btnStartX = rect.x + 118f; // Right after value

            // Minus button (god mode: allow going down to 0)
            // Supports bulk: Shift=5, Ctrl=20, Ctrl+Shift=100
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

            // Plus button (god mode: bypass available check)
            // Supports bulk: Shift=5, Ctrl=20, Ctrl+Shift=100
            Rect plusRect = new Rect(btnStartX + btnSize + 5f, btnY, btnSize, btnSize);
            int maxStat = IsekaiLeveling.IsekaiStatAllocation.GetEffectiveMaxStat();
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
            
            // Store tooltip info for drawing outside GUI group (to avoid clipping)
            if (Mouse.IsOver(rect))
            {
                showTooltip = true;
                tooltipStatType = type;
                tooltipStatValue = pendingValue;
                tooltipStatRect = new Rect(rect.x + windowRect.x, rect.y + windowRect.y, rect.width, rect.height);
            }
        }
        
        private void DrawStatTooltip(Rect statRect, IsekaiStatType type, int value)
        {
            // Get stat effects description
            string title = GetStatFullName(type);
            string description = GetStatDescription(type);
            List<string> effects = GetStatEffects(type, value);
            
            // Calculate tooltip size
            float tooltipWidth = 220f;
            float lineHeight = 18f;
            float padding = 10f;
            float titleHeight = 24f;
            float descHeight = 36f;
            float tooltipHeight = titleHeight + descHeight + padding + (effects.Count * lineHeight) + padding * 2;
            
            // Position tooltip to the right of the stat row, or left if no room
            float tooltipX = statRect.xMax + 8f;
            float tooltipY = statRect.y;
            
            // Check if tooltip would go off screen to the right
            if (tooltipX + tooltipWidth > windowRect.width)
            {
                tooltipX = statRect.x - tooltipWidth - 8f;
            }
            
            Rect tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
            
            // Draw tooltip background
            GUI.color = new Color(0.08f, 0.07f, 0.07f, 0.95f);
            GUI.DrawTexture(tooltipRect, BaseContent.WhiteTex);
            
            // Draw border
            GUI.color = GetStatColor(type) * 0.7f;
            Widgets.DrawBox(tooltipRect, 1);
            
            float curY = tooltipRect.y + padding;
            
            // Title (stat name)
            GUI.color = GetStatColor(type);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, titleHeight), title);
            curY += titleHeight;
            
            // Description
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, descHeight), description);
            curY += descHeight + padding * 0.5f;
            
            // Separator line
            GUI.color = new Color(0.3f, 0.28f, 0.25f, 0.5f);
            GUI.DrawTexture(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, 1f), BaseContent.WhiteTex);
            curY += padding;
            
            // Effects list
            Text.Font = GameFont.Tiny;
            foreach (string effect in effects)
            {
                GUI.color = TextPrimary;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, lineHeight), effect);
                curY += lineHeight;
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        /// <summary>
        /// Draw tooltip using absolute screen coordinates (outside window's GUI group so it won't be clipped)
        /// </summary>
        private void DrawStatTooltipAbsolute(Rect statRect, IsekaiStatType type, int value)
        {
            // Get stat effects description
            string title = GetStatFullName(type);
            string description = GetStatDescription(type);
            List<string> effects = GetStatEffects(type, value);
            
            // Calculate tooltip size
            float tooltipWidth = 280f; // Increased width to prevent text wrapping
            float lineHeight = 18f;
            float padding = 10f;
            float titleHeight = 24f;
            
            // Calculate actual description height based on text content
            Text.Font = GameFont.Tiny;
            float descHeight = Text.CalcHeight(description, tooltipWidth - padding * 2);
            
            float tooltipHeight = titleHeight + descHeight + padding + (effects.Count * lineHeight) + padding * 2;
            
            // Position tooltip to the right of the stat row (statRect is already in screen coords)
            float tooltipX = statRect.xMax + 8f;
            float tooltipY = statRect.y;
            
            // Check if tooltip would go off screen to the right
            if (tooltipX + tooltipWidth > Verse.UI.screenWidth)
            {
                tooltipX = statRect.x - tooltipWidth - 8f;
            }
            
            // Clamp Y to stay on screen
            if (tooltipY + tooltipHeight > Verse.UI.screenHeight)
            {
                tooltipY = Verse.UI.screenHeight - tooltipHeight - 10f;
            }
            if (tooltipY < 10f)
            {
                tooltipY = 10f;
            }
            
            Rect tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
            
            // Draw tooltip background (high depth to render on top)
            GUI.color = new Color(0.08f, 0.07f, 0.07f, 0.98f);
            GUI.DrawTexture(tooltipRect, BaseContent.WhiteTex);
            
            // Draw border
            GUI.color = GetStatColor(type) * 0.8f;
            Widgets.DrawBox(tooltipRect, 2);
            
            float curY = tooltipRect.y + padding;
            
            // Title (stat name)
            GUI.color = GetStatColor(type);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, titleHeight), title);
            curY += titleHeight;
            
            // Description
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, descHeight), description);
            curY += descHeight + padding * 0.5f;
            
            // Separator line
            GUI.color = new Color(0.3f, 0.28f, 0.25f, 0.5f);
            GUI.DrawTexture(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, 1f), BaseContent.WhiteTex);
            curY += padding;
            
            // Effects list
            Text.Font = GameFont.Tiny;
            foreach (string effect in effects)
            {
                GUI.color = TextPrimary;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(tooltipRect.x + padding, curY, tooltipWidth - padding * 2, lineHeight), effect);
                curY += lineHeight;
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        private string GetStatFullName(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return "Isekai_Stat_Strength".Translate();
                case IsekaiStatType.Vitality: return "Isekai_Stat_Vitality".Translate();
                case IsekaiStatType.Dexterity: return "Isekai_Stat_Dexterity".Translate();
                case IsekaiStatType.Intelligence: return "Isekai_Stat_Intelligence".Translate();
                case IsekaiStatType.Wisdom: return "Isekai_Stat_Wisdom".Translate();
                case IsekaiStatType.Charisma: return "Isekai_Stat_Charisma".Translate();
                default: return "Unknown";
            }
        }
        
        private string GetStatDescription(IsekaiStatType type)
        {
            string baseKey;
            switch (type)
            {
                case IsekaiStatType.Strength: baseKey = "Isekai_Desc_STR"; break;
                case IsekaiStatType.Vitality: baseKey = "Isekai_Desc_VIT"; break;
                case IsekaiStatType.Dexterity: baseKey = "Isekai_Desc_DEX"; break;
                case IsekaiStatType.Intelligence: baseKey = "Isekai_Desc_INT"; break;
                case IsekaiStatType.Wisdom: baseKey = "Isekai_Desc_WIS"; break;
                case IsekaiStatType.Charisma: baseKey = "Isekai_Desc_CHA"; break;
                default: return "";
            }
            string desc = baseKey.Translate();
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                desc += (baseKey + "_RoM").Translate();
            }
            return desc;
        }
        
        private List<string> GetStatEffects(IsekaiStatType type, int value)
        {
            List<string> effects = new List<string>();
            float baseValue = 5f; // Default stat value
            float diff = value - baseValue;
            
            switch (type)
            {
                case IsekaiStatType.Strength:
                    effects.Add($"• {"Isekai_Effect_MeleeDamage".Translate()}: x{(1f + diff * IsekaiLevelingSettings.STR_MeleeDamage):F2}");
                    effects.Add($"• {"Isekai_Effect_CarryCapacity".Translate()}: x{(1f + diff * IsekaiLevelingSettings.STR_CarryCapacity):F2}");
                    effects.Add($"• {"Isekai_Effect_MiningSpeed".Translate()}: x{(1f + diff * IsekaiLevelingSettings.STR_MiningSpeed):F2}");
                    if (ModCompatibility.RimWorldOfMagicActive)
                    {
                        effects.Add($"• {"Isekai_Effect_RoM_MightDamage".Translate()}: x{(1f + Mathf.Clamp(diff * 0.015f, -0.06f, 2.0f)):F2}");
                    }
                    break;
                case IsekaiStatType.Vitality:
                    effects.Add($"• {"Isekai_Effect_MaxHealth".Translate()}: x{(1f + diff * IsekaiLevelingSettings.VIT_MaxHealth):F2}");
                    effects.Add($"• {"Isekai_Effect_HealthRegen".Translate()}: x{(1f + diff * IsekaiLevelingSettings.VIT_HealthRegen):F2}");
                    effects.Add($"• {"Isekai_Effect_ToxicResist".Translate()}: {(diff * IsekaiLevelingSettings.VIT_ToxicResist):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_ImmunityGain".Translate()}: x{Mathf.Clamp(1f + diff * IsekaiLevelingSettings.VIT_ImmunityGain, 0.5f, 5f):F2}");
                    effects.Add($"• {"Isekai_Effect_DamageReduction".Translate()}: x{Mathf.Clamp(1f / (1f + Mathf.Max(0f, diff) * IsekaiLevelingSettings.VIT_DamageReduction), 0.05f, 1.1f):F2}");
                    effects.Add($"• {"Isekai_Effect_SharpArmor".Translate()}: {Mathf.Clamp(diff * 0.008f, 0f, 0.80f):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_BluntArmor".Translate()}: {Mathf.Clamp(diff * 0.01f, 0f, 0.95f):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_HeatArmor".Translate()}: {Mathf.Clamp(diff * 0.007f, 0f, 0.70f):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_PainThreshold".Translate()}: +{((1f + diff * IsekaiLevelingSettings.VIT_MaxHealth) - 1f) * 0.8f:F2}");
                    effects.Add($"• {"Isekai_Effect_BleedRate".Translate()}: x{((1f + diff * IsekaiLevelingSettings.VIT_MaxHealth) > 1f ? 1f / (1f + diff * IsekaiLevelingSettings.VIT_MaxHealth) : 1f):F2}");
                    effects.Add($"• {"Isekai_Effect_RestRate".Translate()}: x{Mathf.Clamp(1f + diff * 0.015f, 0.5f, 3f):F2}");
                    float lifespanFactor = 1f + diff * IsekaiLevelingSettings.VIT_LifespanFactor;
                    effects.Add($"• {"Isekai_Effect_Lifespan".Translate()}: x{lifespanFactor:F2}");
                    if (ModCompatibility.RimWorldOfMagicActive)
                    {
                        effects.Add($"• {"Isekai_Effect_RoM_MaxStamina".Translate()}: x{(1f + Mathf.Clamp(diff * 0.02f, -0.08f, 2.0f)):F2}");
                        effects.Add($"• {"Isekai_Effect_RoM_ChiMax".Translate()}: x{(1f + Mathf.Clamp(diff * 0.02f, -0.08f, 2.0f)):F2}");
                    }
                    break;
                case IsekaiStatType.Dexterity:
                    effects.Add($"• {"Isekai_Effect_MoveSpeed".Translate()}: x{(1f + diff * IsekaiLevelingSettings.DEX_MoveSpeed):F2}");
                    effects.Add($"• {"Isekai_Effect_DodgeChance".Translate()}: {(diff * IsekaiLevelingSettings.DEX_MeleeDodge):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_ShootingAccuracy".Translate()}: {(diff * IsekaiLevelingSettings.DEX_ShootingAccuracy):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_MeleeHit".Translate()}: {(diff * IsekaiLevelingSettings.DEX_MeleeHitChance):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_AimingDelay".Translate()}: x{Mathf.Clamp(1f - diff * IsekaiLevelingSettings.DEX_AimingTime, 0.05f, 1.5f):F2}");
                    if (ModCompatibility.RimWorldOfMagicActive)
                    {
                        effects.Add($"• {"Isekai_Effect_RoM_StaminaRegen".Translate()}: x{(1f + Mathf.Clamp(diff * 0.015f, -0.06f, 1.5f)):F2}");
                        float dexCdMult = Mathf.Clamp(1f - diff * 0.008f, 0.3f, 1.08f);
                        effects.Add($"• {"Isekai_Effect_RoM_MightCooldown".Translate()}: x{dexCdMult:F2} ({(1f - dexCdMult):+0.0%;-0.0%})");
                        float dexCostMult = Mathf.Clamp(1f - diff * 0.008f, 0.3f, 1.08f);
                        effects.Add($"• {"Isekai_Effect_RoM_StaminaCost".Translate()}: x{dexCostMult:F2} ({(1f - dexCostMult):+0.0%;-0.0%})");
                    }
                    break;
                case IsekaiStatType.Intelligence:
                    effects.Add($"• {"Isekai_Effect_WorkSpeed".Translate()}: x{(1f + diff * IsekaiLevelingSettings.INT_WorkSpeed):F2}");
                    effects.Add($"• {"Isekai_Effect_ResearchSpeed".Translate()}: x{(1f + diff * IsekaiLevelingSettings.INT_ResearchSpeed):F2}");
                    effects.Add($"• {"Isekai_Effect_LearningRate".Translate()}: x{(1f + diff * IsekaiLevelingSettings.INT_LearningSpeed):F2}");
                    effects.Add($"• {"Isekai_Effect_HackingSpeed".Translate()}: x{(1f + diff * 0.02f):F2}");
                    if (ModCompatibility.RimWorldOfMagicActive)
                    {
                        effects.Add($"• {"Isekai_Effect_RoM_MaxMana".Translate()}: x{(1f + Mathf.Clamp(diff * 0.02f, -0.08f, 2.0f)):F2}");
                        float intCdMult = Mathf.Clamp(1f - diff * 0.008f, 0.3f, 1.08f);
                        effects.Add($"• {"Isekai_Effect_RoM_MagicCooldown".Translate()}: x{intCdMult:F2} ({(1f - intCdMult):+0.0%;-0.0%})");
                        effects.Add($"• {"Isekai_Effect_RoM_PsionicMax".Translate()}: x{(1f + Mathf.Clamp(diff * 0.02f, -0.08f, 2.0f)):F2}");
                    }
                    break;
                case IsekaiStatType.Wisdom:
                    effects.Add($"• {"Isekai_Effect_MentalBreakResist".Translate()}: {(-diff * IsekaiLevelingSettings.WIS_MentalBreak):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_MeditationGain".Translate()}: {(diff * IsekaiLevelingSettings.WIS_MeditationFocus):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_TendQuality".Translate()}: {(diff * IsekaiLevelingSettings.WIS_MedicalTendQuality):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_SurgerySuccess".Translate()}: x{Mathf.Clamp(1f + diff * IsekaiLevelingSettings.WIS_SurgerySuccess, 0.5f, 4f):F2}");
                    effects.Add($"• {"Isekai_Effect_TrainAnimal".Translate()}: x{Mathf.Clamp(1f + diff * IsekaiLevelingSettings.WIS_TrainAnimal, 0.5f, 3f):F2}");
                    effects.Add($"• {"Isekai_Effect_GatherYield".Translate()}: x{Mathf.Clamp(1f + diff * IsekaiLevelingSettings.WIS_AnimalGatherYield, 0.5f, 3f):F2}");
                    effects.Add($"• {"Isekai_Effect_PsychicSensitivity".Translate()}: x{Mathf.Clamp(1f + diff * IsekaiLevelingSettings.WIS_PsychicSensitivity, 0.5f, 3f):F2} (avg w/ INT)");
                    effects.Add($"• {"Isekai_Effect_NeuralHeatLimit".Translate()}: {Mathf.Clamp(diff * IsekaiLevelingSettings.WIS_NeuralHeatLimit, -10f, 150f):+0;-0} (avg w/ VIT)");
                    effects.Add($"• {"Isekai_Effect_NeuralRecovery".Translate()}: x{Mathf.Clamp(1f + diff * IsekaiLevelingSettings.WIS_NeuralHeatRecovery, 0.5f, 5f):F2}");
                    effects.Add($"• {"Isekai_Effect_PsyfocusCost".Translate()}: x{Mathf.Clamp(1f - diff * IsekaiLevelingSettings.WIS_PsyfocusCost, 0.3f, 1.2f):F2}");
                    if (ModCompatibility.RimWorldOfMagicActive)
                    {
                        effects.Add($"• {"Isekai_Effect_RoM_ManaRegen".Translate()}: x{(1f + Mathf.Clamp(diff * 0.015f, -0.06f, 1.5f)):F2}");
                        effects.Add($"• {"Isekai_Effect_RoM_MagicDamage".Translate()}: x{(1f + Mathf.Clamp(diff * 0.015f, -0.06f, 2.0f)):F2}");
                        float wisCostMult = Mathf.Clamp(1f - diff * 0.008f, 0.3f, 1.08f);
                        effects.Add($"• {"Isekai_Effect_RoM_ManaCost".Translate()}: x{wisCostMult:F2} ({(1f - wisCostMult):+0.0%;-0.0%})");
                    }
                    break;
                case IsekaiStatType.Charisma:
                    effects.Add($"• {"Isekai_Effect_SocialImpact".Translate()}: x{(1f + diff * IsekaiLevelingSettings.CHA_SocialImpact):F2}");
                    effects.Add($"• {"Isekai_Effect_Negotiation".Translate()}: x{(1f + diff * IsekaiLevelingSettings.CHA_NegotiationAbility):F2}");
                    effects.Add($"• {"Isekai_Effect_TradePriceImprove".Translate()}: {(diff * IsekaiLevelingSettings.CHA_TradePrice):+0.0%;-0.0%}");
                    effects.Add($"• {"Isekai_Effect_Taming".Translate()}: x{Mathf.Clamp(1f + diff * 0.015f, 0.5f, 3f):F2}");
                    effects.Add($"• {"Isekai_Effect_ArrestSuccess".Translate()}: {Mathf.Clamp(diff * IsekaiLevelingSettings.CHA_ArrestSuccess, -0.1f, 0.5f):+0.0%;-0.0%}");
                    if (ModCompatibility.RimWorldOfMagicActive)
                    {
                        effects.Add($"• {"Isekai_Effect_RoM_SummonDuration".Translate()}: x{(1f + Mathf.Clamp(diff * 0.02f, -0.08f, 3.0f)):F2}");
                        effects.Add($"• {"Isekai_Effect_RoM_BuffDuration".Translate()}: x{(1f + Mathf.Clamp(diff * 0.015f, -0.06f, 2.0f)):F2}");
                    }
                    break;
            }
            
            return effects;
        }
        
        private bool DrawCircleButton(Rect rect, string symbol, string id, bool enabled)
        {
            bool isOver = Mouse.IsOver(rect);
            float hover = AnimateHover(id, isOver && enabled);
            float scale = AnimateScale(id, isOver && enabled, 1f, 1.15f);
            
            // Calculate scaled rect from center
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scale) / 2f,
                center.y - (rect.height * scale) / 2f,
                rect.width * scale,
                rect.height * scale
            );
            
            // Button background with glow effect
            Color bgColor = enabled 
                ? Color.Lerp(new Color(0.25f, 0.23f, 0.22f), new Color(0.45f, 0.42f, 0.38f), hover)
                : new Color(0.15f, 0.14f, 0.13f);
            
            GUI.color = bgColor;
            GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            
            // Symbol with brightness boost
            Color textColor = enabled
                ? Color.Lerp(TextSecondary, Color.white, hover)
                : TextMuted;
            
            GUI.color = textColor;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(scaledRect, symbol);
            
            GUI.color = Color.white;
            
            // Consume MouseDown so RimWorld doesn't start a drag-select
            if (enabled && isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
            }
            if (enabled && isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            
            return false;
        }
        
        private void DrawAffectedStats(Rect inRect)
        {
            // Anchor from bottom: scaled values
            float bottomSectionHeight = 128f;  // Increased for more spacing
            float detailHeight = 114f;         // ~190 * 0.6 - 8 lines × 12px + padding
            float detailWidth = 372f;          // ~620 * 0.6
            float detailY = inRect.height - bottomSectionHeight - detailHeight - 6f;
            float detailX = (inRect.width - detailWidth) / 2f;
            
            Rect detailRect = new Rect(detailX, detailY, detailWidth, detailHeight);
            
            // Draw detail background
            GUI.color = Color.white;
            if (StatsWindowTextures.StatListDetail != null)
            {
                GUI.DrawTexture(detailRect, StatsWindowTextures.StatListDetail, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.14f, 0.12f, 0.11f);
                GUI.DrawTexture(detailRect, BaseContent.WhiteTex);
            }
            
            // Show affected stats based on pending changes
            Rect contentRect = detailRect.ContractedBy(12f);
            DrawAffectedStatsContent(contentRect);
        }
        
        private void DrawAffectedStatsContent(Rect rect)
        {
            float lineHeight = 16f;  // Slightly smaller for more lines
            int totalLines = 35;  // All stat effect lines (functional stats only)
            if (ModCompatibility.RimWorldOfMagicActive) totalLines += 14;  // RoM extra rows
            float totalContentHeight = totalLines * lineHeight;
            
            // Create scrollable view
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, totalContentHeight);
            
            Widgets.BeginScrollView(rect, ref statsScrollPosition, viewRect, true);
            
            float curY = 0f;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            
            // Always show current stat effects, highlight changes in green/red
            // Use actual settings values for accurate display
            
            // STR effects - use actual settings
            int strDiff = pendingSTR - comp.stats.strength;
            float strMeleePercent = (pendingSTR - 5) * IsekaiLevelingSettings.STR_MeleeDamage * 100f;
            float strMeleeDiffPercent = strDiff * IsekaiLevelingSettings.STR_MeleeDamage * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MeleeDamage".Translate(), strMeleePercent, strMeleeDiffPercent, "%", lineHeight);
            float strCarryPercent = (pendingSTR - 5) * IsekaiLevelingSettings.STR_CarryCapacity * 100f;
            float strCarryDiffPercent = strDiff * IsekaiLevelingSettings.STR_CarryCapacity * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_CarryCapacity".Translate(), strCarryPercent, strCarryDiffPercent, "%", lineHeight);
            float strMinePercent = (pendingSTR - 5) * IsekaiLevelingSettings.STR_MiningSpeed * 100f;
            float strMineDiffPercent = strDiff * IsekaiLevelingSettings.STR_MiningSpeed * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MiningSpeed".Translate(), strMinePercent, strMineDiffPercent, "%", lineHeight);
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                // STR → Might Damage
                float strMightDmgNew = Mathf.Clamp((pendingSTR - 5) * 0.015f, -0.06f, 2.0f) * 100f;
                float strMightDmgOld = Mathf.Clamp((comp.stats.strength - 5) * 0.015f, -0.06f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_MightDamage".Translate(), strMightDmgNew, strMightDmgNew - strMightDmgOld, "%", lineHeight);
            }
            
            // VIT effects - use actual settings
            int vitDiff = pendingVIT - comp.stats.vitality;
            float vitHealthPercent = (pendingVIT - 5) * IsekaiLevelingSettings.VIT_MaxHealth * 100f;
            float vitHealthDiffPercent = vitDiff * IsekaiLevelingSettings.VIT_MaxHealth * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MaxHealth".Translate(), vitHealthPercent, vitHealthDiffPercent, "%", lineHeight);
            float vitRegenPercent = (pendingVIT - 5) * IsekaiLevelingSettings.VIT_HealthRegen * 100f;
            float vitRegenDiffPercent = vitDiff * IsekaiLevelingSettings.VIT_HealthRegen * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_HealthRegen".Translate(), vitRegenPercent, vitRegenDiffPercent, "%", lineHeight);
            float vitToxicPercent = (pendingVIT - 5) * IsekaiLevelingSettings.VIT_ToxicResist * 100f;
            float vitToxicDiffPercent = vitDiff * IsekaiLevelingSettings.VIT_ToxicResist * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_ToxicResist".Translate(), vitToxicPercent, vitToxicDiffPercent, "%", lineHeight);
            float vitImmuneMultNew = 1f + (pendingVIT - 5f) * IsekaiLevelingSettings.VIT_ImmunityGain;
            float vitImmuneMultOld = 1f + (comp.stats.vitality - 5f) * IsekaiLevelingSettings.VIT_ImmunityGain;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_ImmunityGain".Translate(), (vitImmuneMultNew - 1f) * 100f, (vitImmuneMultNew - vitImmuneMultOld) * 100f, "%", lineHeight);
            float vitDmgRedNew = (1f - Mathf.Clamp(1f / (1f + Mathf.Max(0f, pendingVIT - 5f) * IsekaiLevelingSettings.VIT_DamageReduction), 0.05f, 1f)) * 100f;
            float vitDmgRedOld = (1f - Mathf.Clamp(1f / (1f + Mathf.Max(0f, comp.stats.vitality - 5f) * IsekaiLevelingSettings.VIT_DamageReduction), 0.05f, 1f)) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_DamageReduction".Translate(), vitDmgRedNew, vitDmgRedNew - vitDmgRedOld, "%", lineHeight);
            float vitSharpNew = Mathf.Clamp((pendingVIT - 5) * 0.008f, 0f, 0.80f) * 100f;
            float vitSharpOld = Mathf.Clamp((comp.stats.vitality - 5) * 0.008f, 0f, 0.80f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_SharpArmor".Translate(), vitSharpNew, vitSharpNew - vitSharpOld, "%", lineHeight);
            float vitBluntNew = Mathf.Clamp((pendingVIT - 5) * 0.01f, 0f, 0.95f) * 100f;
            float vitBluntOld = Mathf.Clamp((comp.stats.vitality - 5) * 0.01f, 0f, 0.95f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_BluntArmor".Translate(), vitBluntNew, vitBluntNew - vitBluntOld, "%", lineHeight);
            float vitHeatNew = Mathf.Clamp((pendingVIT - 5) * 0.007f, 0f, 0.70f) * 100f;
            float vitHeatOld = Mathf.Clamp((comp.stats.vitality - 5) * 0.007f, 0f, 0.70f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_HeatArmor".Translate(), vitHeatNew, vitHeatNew - vitHeatOld, "%", lineHeight);
            // Pain threshold now scales proportionally to health multiplier
            float vitHealthMultNew = 1f + Mathf.Max(0f, pendingVIT - 5f) * IsekaiLevelingSettings.VIT_MaxHealth;
            float vitHealthMultOld = 1f + Mathf.Max(0f, comp.stats.vitality - 5f) * IsekaiLevelingSettings.VIT_MaxHealth;
            float vitPainNew = (vitHealthMultNew - 1f) * 0.8f * 100f;
            float vitPainOld = (vitHealthMultOld - 1f) * 0.8f * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_PainThreshold".Translate(), vitPainNew, vitPainNew - vitPainOld, "%", lineHeight);
            // Bleed rate scales inversely with health multiplier
            float vitBleedNew = vitHealthMultNew > 1f ? (1f - 1f / vitHealthMultNew) * 100f : 0f;
            float vitBleedOld = vitHealthMultOld > 1f ? (1f - 1f / vitHealthMultOld) * 100f : 0f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_BleedRate".Translate(), -vitBleedNew, -(vitBleedNew - vitBleedOld), "%", lineHeight);
            float vitRestNew = (Mathf.Clamp(1f + (pendingVIT - 5) * 0.015f, 0.5f, 3f) - 1f) * 100f;
            float vitRestOld = (Mathf.Clamp(1f + (comp.stats.vitality - 5) * 0.015f, 0.5f, 3f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RestRate".Translate(), vitRestNew, vitRestNew - vitRestOld, "%", lineHeight);
            float vitLifeNew = (pendingVIT - 5) * IsekaiLevelingSettings.VIT_LifespanFactor * 100f;
            float vitLifeOld = (comp.stats.vitality - 5) * IsekaiLevelingSettings.VIT_LifespanFactor * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_Lifespan".Translate(), vitLifeNew, vitLifeNew - vitLifeOld, "%", lineHeight);
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                // VIT → Max Stamina
                float vitMaxStamNew = Mathf.Clamp((pendingVIT - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                float vitMaxStamOld = Mathf.Clamp((comp.stats.vitality - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_MaxStamina".Translate(), vitMaxStamNew, vitMaxStamNew - vitMaxStamOld, "%", lineHeight);
                // VIT → Chi Max
                float vitChiNew = Mathf.Clamp((pendingVIT - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                float vitChiOld = Mathf.Clamp((comp.stats.vitality - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_ChiMax".Translate(), vitChiNew, vitChiNew - vitChiOld, "%", lineHeight);
            }
            
            // DEX effects - use actual settings
            int dexDiff = pendingDEX - comp.stats.dexterity;
            float dexMovePercent = (pendingDEX - 5) * IsekaiLevelingSettings.DEX_MoveSpeed * 100f;
            float dexMoveDiffPercent = dexDiff * IsekaiLevelingSettings.DEX_MoveSpeed * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MoveSpeed".Translate(), dexMovePercent, dexMoveDiffPercent, "%", lineHeight);
            float dexHitPercent = (pendingDEX - 5) * IsekaiLevelingSettings.DEX_MeleeHitChance * 100f;
            float dexHitDiffPercent = dexDiff * IsekaiLevelingSettings.DEX_MeleeHitChance * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MeleeHit".Translate(), dexHitPercent, dexHitDiffPercent, "%", lineHeight);
            float dexDodgePercent = (pendingDEX - 5) * IsekaiLevelingSettings.DEX_MeleeDodge * 100f;
            float dexDodgeDiffPercent = dexDiff * IsekaiLevelingSettings.DEX_MeleeDodge * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_DodgeChance".Translate(), dexDodgePercent, dexDodgeDiffPercent, "%", lineHeight);
            float dexShootPercent = Mathf.Clamp((pendingDEX - 5) * IsekaiLevelingSettings.DEX_ShootingAccuracy, -0.1f, 0.95f) * 100f;
            float dexShootOld = Mathf.Clamp((comp.stats.dexterity - 5) * IsekaiLevelingSettings.DEX_ShootingAccuracy, -0.1f, 0.95f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_ShootingAccuracy".Translate(), dexShootPercent, dexShootPercent - dexShootOld, "%", lineHeight);
            float dexAimNew = (Mathf.Clamp(1f - (pendingDEX - 5) * IsekaiLevelingSettings.DEX_AimingTime, 0.05f, 1.5f) - 1f) * 100f;
            float dexAimOld = (Mathf.Clamp(1f - (comp.stats.dexterity - 5) * IsekaiLevelingSettings.DEX_AimingTime, 0.05f, 1.5f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_AimingDelay".Translate(), dexAimNew, dexAimNew - dexAimOld, "%", lineHeight);
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                // DEX → Stamina Regen
                float dexStamRegNew = Mathf.Clamp((pendingDEX - 5) * 0.015f, -0.06f, 1.5f) * 100f;
                float dexStamRegOld = Mathf.Clamp((comp.stats.dexterity - 5) * 0.015f, -0.06f, 1.5f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_StaminaRegen".Translate(), dexStamRegNew, dexStamRegNew - dexStamRegOld, "%", lineHeight);
                // DEX → Might Cooldown reduction (display as positive % cooldown saved)
                float dexCdRedNew = (1f - Mathf.Clamp(1f - (pendingDEX - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                float dexCdRedOld = (1f - Mathf.Clamp(1f - (comp.stats.dexterity - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_MightCooldown".Translate(), dexCdRedNew, dexCdRedNew - dexCdRedOld, "%", lineHeight);
                // DEX → Stamina Cost reduction
                float dexCostRedNew = (1f - Mathf.Clamp(1f - (pendingDEX - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                float dexCostRedOld = (1f - Mathf.Clamp(1f - (comp.stats.dexterity - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_StaminaCost".Translate(), dexCostRedNew, dexCostRedNew - dexCostRedOld, "%", lineHeight);
            }
            
            // INT effects - use actual settings
            int intDiff = pendingINT - comp.stats.intelligence;
            float intWorkPercent = (pendingINT - 5) * IsekaiLevelingSettings.INT_WorkSpeed * 100f;
            float intWorkDiffPercent = intDiff * IsekaiLevelingSettings.INT_WorkSpeed * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_WorkSpeed".Translate(), intWorkPercent, intWorkDiffPercent, "%", lineHeight);
            float intResearchPercent = (pendingINT - 5) * IsekaiLevelingSettings.INT_ResearchSpeed * 100f;
            float intResearchDiffPercent = intDiff * IsekaiLevelingSettings.INT_ResearchSpeed * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_ResearchSpeed".Translate(), intResearchPercent, intResearchDiffPercent, "%", lineHeight);
            float intLearnPercent = (pendingINT - 5) * IsekaiLevelingSettings.INT_LearningSpeed * 100f;
            float intLearnDiffPercent = intDiff * IsekaiLevelingSettings.INT_LearningSpeed * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_LearningRate".Translate(), intLearnPercent, intLearnDiffPercent, "%", lineHeight);
            float intHackPercent = (pendingINT - 5) * 0.02f * 100f;
            float intHackDiffPercent = intDiff * 0.02f * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_HackingSpeed".Translate(), intHackPercent, intHackDiffPercent, "%", lineHeight);
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                // INT → Max Mana
                float intMaxManaNew = Mathf.Clamp((pendingINT - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                float intMaxManaOld = Mathf.Clamp((comp.stats.intelligence - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_MaxMana".Translate(), intMaxManaNew, intMaxManaNew - intMaxManaOld, "%", lineHeight);
                // INT → Magic Cooldown reduction
                float intCdRedNew = (1f - Mathf.Clamp(1f - (pendingINT - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                float intCdRedOld = (1f - Mathf.Clamp(1f - (comp.stats.intelligence - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_MagicCooldown".Translate(), intCdRedNew, intCdRedNew - intCdRedOld, "%", lineHeight);
                // INT → Psionic Max
                float intPsiNew = Mathf.Clamp((pendingINT - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                float intPsiOld = Mathf.Clamp((comp.stats.intelligence - 5) * 0.02f, -0.08f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_PsionicMax".Translate(), intPsiNew, intPsiNew - intPsiOld, "%", lineHeight);
            }
            
            // WIS effects - use actual settings
            int wisDiff = pendingWIS - comp.stats.wisdom;
            float wisMentalPercent = (pendingWIS - 5) * IsekaiLevelingSettings.WIS_MentalBreak * 100f;
            float wisMentalDiffPercent = wisDiff * IsekaiLevelingSettings.WIS_MentalBreak * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MentalBreakResist".Translate(), wisMentalPercent, wisMentalDiffPercent, "%", lineHeight);
            float wisMeditationPercent = (pendingWIS - 5) * IsekaiLevelingSettings.WIS_MeditationFocus * 100f;
            float wisMeditationDiffPercent = wisDiff * IsekaiLevelingSettings.WIS_MeditationFocus * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_MeditationGain".Translate(), wisMeditationPercent, wisMeditationDiffPercent, "%", lineHeight);
            float psySensAvgNew = (pendingINT + pendingWIS) / 2f;
            float psySensAvgOld = (comp.stats.intelligence + comp.stats.wisdom) / 2f;
            float psySensNew = (Mathf.Clamp(1f + (psySensAvgNew - 5f) * IsekaiLevelingSettings.WIS_PsychicSensitivity, 0.5f, 3f) - 1f) * 100f;
            float psySensOld = (Mathf.Clamp(1f + (psySensAvgOld - 5f) * IsekaiLevelingSettings.WIS_PsychicSensitivity, 0.5f, 3f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_PsychicSensitivity".Translate(), psySensNew, psySensNew - psySensOld, "%", lineHeight);
            float neuralAvgNew = (pendingVIT + pendingWIS) / 2f;
            float neuralAvgOld = (comp.stats.vitality + comp.stats.wisdom) / 2f;
            float neuralNew = Mathf.Clamp((neuralAvgNew - 5f) * IsekaiLevelingSettings.WIS_NeuralHeatLimit, -10f, 150f);
            float neuralOld = Mathf.Clamp((neuralAvgOld - 5f) * IsekaiLevelingSettings.WIS_NeuralHeatLimit, -10f, 150f);
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_NeuralHeatLimit".Translate(), neuralNew, neuralNew - neuralOld, "", lineHeight);
            float wisRecovNew = (Mathf.Clamp(1f + (pendingWIS - 5) * IsekaiLevelingSettings.WIS_NeuralHeatRecovery, 0.5f, 5f) - 1f) * 100f;
            float wisRecovOld = (Mathf.Clamp(1f + (comp.stats.wisdom - 5) * IsekaiLevelingSettings.WIS_NeuralHeatRecovery, 0.5f, 5f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_NeuralRecovery".Translate(), wisRecovNew, wisRecovNew - wisRecovOld, "%", lineHeight);
            float wisPsyNew = (Mathf.Clamp(1f - (pendingWIS - 5) * IsekaiLevelingSettings.WIS_PsyfocusCost, 0.3f, 1.2f) - 1f) * 100f;
            float wisPsyOld = (Mathf.Clamp(1f - (comp.stats.wisdom - 5) * IsekaiLevelingSettings.WIS_PsyfocusCost, 0.3f, 1.2f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_PsyfocusCost".Translate(), wisPsyNew, wisPsyNew - wisPsyOld, "%", lineHeight);
            float wisTendNew = Mathf.Clamp((pendingWIS - 5) * IsekaiLevelingSettings.WIS_MedicalTendQuality, -0.1f, 0.5f) * 100f;
            float wisTendOld = Mathf.Clamp((comp.stats.wisdom - 5) * IsekaiLevelingSettings.WIS_MedicalTendQuality, -0.1f, 0.5f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_TendQuality".Translate(), wisTendNew, wisTendNew - wisTendOld, "%", lineHeight);
            float wisSurgNew = (Mathf.Clamp(1f + (pendingWIS - 5) * IsekaiLevelingSettings.WIS_SurgerySuccess, 0.5f, 4f) - 1f) * 100f;
            float wisSurgOld = (Mathf.Clamp(1f + (comp.stats.wisdom - 5) * IsekaiLevelingSettings.WIS_SurgerySuccess, 0.5f, 4f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_SurgerySuccess".Translate(), wisSurgNew, wisSurgNew - wisSurgOld, "%", lineHeight);
            float wisTrainNew = (Mathf.Clamp(1f + (pendingWIS - 5) * IsekaiLevelingSettings.WIS_TrainAnimal, 0.5f, 3f) - 1f) * 100f;
            float wisTrainOld = (Mathf.Clamp(1f + (comp.stats.wisdom - 5) * IsekaiLevelingSettings.WIS_TrainAnimal, 0.5f, 3f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_TrainAnimal".Translate(), wisTrainNew, wisTrainNew - wisTrainOld, "%", lineHeight);
            float wisGatherNew = (Mathf.Clamp(1f + (pendingWIS - 5) * IsekaiLevelingSettings.WIS_AnimalGatherYield, 0.5f, 3f) - 1f) * 100f;
            float wisGatherOld = (Mathf.Clamp(1f + (comp.stats.wisdom - 5) * IsekaiLevelingSettings.WIS_AnimalGatherYield, 0.5f, 3f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_GatherYield".Translate(), wisGatherNew, wisGatherNew - wisGatherOld, "%", lineHeight);
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                // WIS → Mana Regen
                float wisManaRegNew = Mathf.Clamp((pendingWIS - 5) * 0.015f, -0.06f, 1.5f) * 100f;
                float wisManaRegOld = Mathf.Clamp((comp.stats.wisdom - 5) * 0.015f, -0.06f, 1.5f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_ManaRegen".Translate(), wisManaRegNew, wisManaRegNew - wisManaRegOld, "%", lineHeight);
                // WIS → Magic Damage
                float wisMagDmgNew = Mathf.Clamp((pendingWIS - 5) * 0.015f, -0.06f, 2.0f) * 100f;
                float wisMagDmgOld = Mathf.Clamp((comp.stats.wisdom - 5) * 0.015f, -0.06f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_MagicDamage".Translate(), wisMagDmgNew, wisMagDmgNew - wisMagDmgOld, "%", lineHeight);
                // WIS → Mana Cost reduction
                float wisCostRedNew = (1f - Mathf.Clamp(1f - (pendingWIS - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                float wisCostRedOld = (1f - Mathf.Clamp(1f - (comp.stats.wisdom - 5) * 0.008f, 0.3f, 1.08f)) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_ManaCost".Translate(), wisCostRedNew, wisCostRedNew - wisCostRedOld, "%", lineHeight);
            }
            
            // CHA effects - use actual settings
            int chaDiff = pendingCHA - comp.stats.charisma;
            float chaTradePercent = (pendingCHA - 5) * IsekaiLevelingSettings.CHA_TradePrice * 100f;
            float chaTradeDiffPercent = chaDiff * IsekaiLevelingSettings.CHA_TradePrice * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_TradePriceImprove".Translate(), chaTradePercent, chaTradeDiffPercent, "%", lineHeight);
            float chaSocialPercent = (pendingCHA - 5) * IsekaiLevelingSettings.CHA_SocialImpact * 100f;
            float chaSocialDiffPercent = chaDiff * IsekaiLevelingSettings.CHA_SocialImpact * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_SocialImpact".Translate(), chaSocialPercent, chaSocialDiffPercent, "%", lineHeight);
            float chaNegPercent = (pendingCHA - 5) * IsekaiLevelingSettings.CHA_NegotiationAbility * 100f;
            float chaNegDiffPercent = chaDiff * IsekaiLevelingSettings.CHA_NegotiationAbility * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_Negotiation".Translate(), chaNegPercent, chaNegDiffPercent, "%", lineHeight);
            float chaTameNew = (Mathf.Clamp(1f + (pendingCHA - 5) * 0.015f, 0.5f, 3f) - 1f) * 100f;
            float chaTameOld = (Mathf.Clamp(1f + (comp.stats.charisma - 5) * 0.015f, 0.5f, 3f) - 1f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_Taming".Translate(), chaTameNew, chaTameNew - chaTameOld, "%", lineHeight);
            float chaArrestNew = Mathf.Clamp((pendingCHA - 5) * IsekaiLevelingSettings.CHA_ArrestSuccess, -0.1f, 0.5f) * 100f;
            float chaArrestOld = Mathf.Clamp((comp.stats.charisma - 5) * IsekaiLevelingSettings.CHA_ArrestSuccess, -0.1f, 0.5f) * 100f;
            DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_ArrestSuccess".Translate(), chaArrestNew, chaArrestNew - chaArrestOld, "%", lineHeight);
            if (ModCompatibility.RimWorldOfMagicActive)
            {
                // CHA → Summon Duration
                float chaSummonNew = Mathf.Clamp((pendingCHA - 5) * 0.02f, -0.08f, 3.0f) * 100f;
                float chaSummonOld = Mathf.Clamp((comp.stats.charisma - 5) * 0.02f, -0.08f, 3.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_SummonDuration".Translate(), chaSummonNew, chaSummonNew - chaSummonOld, "%", lineHeight);
                // CHA → Buff Duration
                float chaBuffNew = Mathf.Clamp((pendingCHA - 5) * 0.015f, -0.06f, 2.0f) * 100f;
                float chaBuffOld = Mathf.Clamp((comp.stats.charisma - 5) * 0.015f, -0.06f, 2.0f) * 100f;
                DrawStatDetailLine(ref curY, viewRect, "Isekai_Effect_RoM_BuffDuration".Translate(), chaBuffNew, chaBuffNew - chaBuffOld, "%", lineHeight);
            }
            
            Widgets.EndScrollView();
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        private void DrawStatDetailLine(ref float curY, Rect rect, string statName, float currentValue, float change, string suffix, float lineHeight)
        {
            // Stat name
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, curY, rect.width * 0.55f, lineHeight), statName);
            
            // Current value
            Text.Anchor = TextAnchor.MiddleRight;
            string valueText = $"+{currentValue:F1}{suffix}";
            
            // If there's a change, show with highlight color
            if (Mathf.Abs(change) > 0.01f)
            {
                GUI.color = change > 0 ? new Color(0.5f, 0.85f, 0.5f) : new Color(0.85f, 0.5f, 0.5f);
            }
            else
            {
                GUI.color = TextPrimary;
            }
            
            Widgets.Label(new Rect(rect.x, curY, rect.width, lineHeight), valueText);
            curY += lineHeight;
        }
        
        private void DrawBottomSection(Rect inRect)
        {
            // Anchor at bottom of window with increased padding
            float bottomPadding = 35f;
            float bottomY = inRect.height - GLOBAL_RANK_HEIGHT - bottomPadding;
            float sectionGap = 10f;
            
            // Center all elements using scaled asset sizes
            float totalWidth = GLOBAL_RANK_WIDTH + BOTTOM_BTN_WIDTH * 2 + sectionGap * 2;
            float startX = (inRect.width - totalWidth) / 2f;
            
            // Global Rank section (left) - original size 250x159
            Rect rankRect = new Rect(startX, bottomY, GLOBAL_RANK_WIDTH, GLOBAL_RANK_HEIGHT);
            DrawGlobalRank(rankRect);
            
            // Close button (center) - original size 175x172, vertically centered with rank
            float btnY = bottomY + (GLOBAL_RANK_HEIGHT - BOTTOM_BTN_HEIGHT) / 2f;
            Rect closeRect = new Rect(startX + GLOBAL_RANK_WIDTH + sectionGap, btnY, BOTTOM_BTN_WIDTH, BOTTOM_BTN_HEIGHT);
            if (DrawBottomButton(closeRect, "Isekai_Close".Translate(), StatsWindowTextures.CloseButton, false))
            {
                Close();
            }
            
            // Confirm button (right) - original size 175x172
            Rect confirmRect = new Rect(closeRect.xMax + sectionGap, btnY, BOTTOM_BTN_WIDTH, BOTTOM_BTN_HEIGHT);
            if (DrawBottomButton(confirmRect, "Isekai_Apply".Translate(), StatsWindowTextures.ConfirmButton, true))
            {
                ApplyChanges();
                Close();
            }
        }
        
        private void DrawGlobalRank(Rect rect)
        {
            // Draw rank background - scaled size
            GUI.color = Color.white;
            if (StatsWindowTextures.GlobalRank != null)
            {
                GUI.DrawTexture(rect, StatsWindowTextures.GlobalRank, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.18f, 0.16f, 0.15f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
            }
            
            // "Global Rank" label
            GUI.color = TextSecondary;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, rect.y + 8f, rect.width, 14f), "Isekai_GlobalRank".Translate());
            
            // Rank letter
            string rank = GetLevelRank(comp.Level);
            GUI.color = AccentGold;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y + 28f, rect.width, 30f), rank);
            
            // Value multiplier
            int multiplier = GetRankMultiplier(rank);
            GUI.color = TextMuted;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, rect.y + 65f, rect.width, 14f), "Isekai_ValueMultiplier".Translate(multiplier));
            
            Text.Anchor = TextAnchor.UpperLeft;
        }
        
        private bool DrawBottomButton(Rect rect, string label, Texture2D texture, bool highlighted)
        {
            string btnId = $"btn_{label}";
            bool isOver = Mouse.IsOver(rect);
            float hover = AnimateHover(btnId, isOver);
            float scale = AnimateScale(btnId, isOver, 1f, 1.06f);
            
            // Calculate scaled rect from center
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scale) / 2f,
                center.y - (rect.height * scale) / 2f,
                rect.width * scale,
                rect.height * scale
            );
            
            // Draw button texture with brightness and scale
            float brightness = 1f + hover * 0.2f;
            GUI.color = new Color(brightness, brightness, brightness);
            
            if (texture != null)
            {
                GUI.DrawTexture(scaledRect, texture, ScaleMode.ScaleToFit);
            }
            else
            {
                // Fallback
                Color bg = highlighted ? AccentGold : new Color(0.25f, 0.23f, 0.22f);
                bg = Color.Lerp(bg, Color.white, hover * 0.15f);
                GUI.color = bg;
                GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            }
            
            // Label under the icon with glow
            GUI.color = Color.Lerp(TextSecondary, Color.white, hover);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(scaledRect.x, scaledRect.yMax - 32f, scaledRect.width, 20f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Consume MouseDown over buttons so RimWorld doesn't start a drag-select
            GUI.color = Color.white;
            if (isOver && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
            }
            // Check for click on MouseUp
            if (isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }
        
        private void RecalculatePointsSpent()
        {
            int originalTotal = comp.stats.strength + comp.stats.vitality + comp.stats.dexterity +
                               comp.stats.intelligence + comp.stats.wisdom + comp.stats.charisma;
            int pendingTotal = pendingSTR + pendingVIT + pendingDEX + pendingINT + pendingWIS + pendingCHA;
            
            pointsSpent = pendingTotal - originalTotal;
        }
        
        private bool CanDecrease(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return pendingSTR > comp.stats.strength;
                case IsekaiStatType.Vitality: return pendingVIT > comp.stats.vitality;
                case IsekaiStatType.Dexterity: return pendingDEX > comp.stats.dexterity;
                case IsekaiStatType.Intelligence: return pendingINT > comp.stats.intelligence;
                case IsekaiStatType.Wisdom: return pendingWIS > comp.stats.wisdom;
                case IsekaiStatType.Charisma: return pendingCHA > comp.stats.charisma;
                default: return false;
            }
        }
        
        /// <summary>
        /// Get the original (committed) stat value — the floor for minus operations
        /// </summary>
        private int GetOriginalStat(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return comp.stats.strength;
                case IsekaiStatType.Vitality: return comp.stats.vitality;
                case IsekaiStatType.Dexterity: return comp.stats.dexterity;
                case IsekaiStatType.Intelligence: return comp.stats.intelligence;
                case IsekaiStatType.Wisdom: return comp.stats.wisdom;
                case IsekaiStatType.Charisma: return comp.stats.charisma;
                default: return 0;
            }
        }
        
        private void ApplyChanges()
        {
            if (comp == null) return;

            bool godMode = Prefs.DevMode && DebugSettings.godMode;

            if (godMode)
            {
                // God mode: set stats directly, bypass availableStatPoints
                comp.stats.strength = pendingSTR;
                comp.stats.vitality = pendingVIT;
                comp.stats.dexterity = pendingDEX;
                comp.stats.intelligence = pendingINT;
                comp.stats.wisdom = pendingWIS;
                comp.stats.charisma = pendingCHA;
            }
            else
            {
                // Normal mode: use AllocatePoint which consumes availableStatPoints
                int strChange = pendingSTR - comp.stats.strength;
                int vitChange = pendingVIT - comp.stats.vitality;
                int dexChange = pendingDEX - comp.stats.dexterity;
                int intChange = pendingINT - comp.stats.intelligence;
                int wisChange = pendingWIS - comp.stats.wisdom;
                int chaChange = pendingCHA - comp.stats.charisma;

                for (int i = 0; i < strChange; i++) comp.stats.AllocatePoint(IsekaiStatType.Strength);
                for (int i = 0; i < vitChange; i++) comp.stats.AllocatePoint(IsekaiStatType.Vitality);
                for (int i = 0; i < dexChange; i++) comp.stats.AllocatePoint(IsekaiStatType.Dexterity);
                for (int i = 0; i < intChange; i++) comp.stats.AllocatePoint(IsekaiStatType.Intelligence);
                for (int i = 0; i < wisChange; i++) comp.stats.AllocatePoint(IsekaiStatType.Wisdom);
                for (int i = 0; i < chaChange; i++) comp.stats.AllocatePoint(IsekaiStatType.Charisma);
            }

            // Update rank trait
            PawnStatGenerator.UpdateRankTraitFromStats(pawn, comp);

            Messages.Message("Isekai_StatsUpdated".Translate(pawn.LabelShortCap), MessageTypeDefOf.PositiveEvent);
        }
        
        private float AnimateHover(string id, bool isHovered)
        {
            if (!hoverStates.ContainsKey(id))
                hoverStates[id] = 0f;
            
            float target = isHovered ? 1f : 0f;
            float current = hoverStates[id];
            // Smooth spring-like animation
            float speed = isHovered ? 12f : 8f;
            hoverStates[id] = Mathf.Lerp(current, target, Time.deltaTime * speed);
            
            // Start shine timer when hovering begins
            if (isHovered && current < 0.1f && !shineTimers.ContainsKey(id))
            {
                shineTimers[id] = Time.realtimeSinceStartup;
            }
            else if (!isHovered && shineTimers.ContainsKey(id))
            {
                shineTimers.Remove(id);
            }
            
            return hoverStates[id];
        }
        
        private float AnimateScale(string id, bool isHovered, float minScale = 1f, float maxScale = 1.05f)
        {
            if (!scaleStates.ContainsKey(id))
                scaleStates[id] = minScale;
            
            float target = isHovered ? maxScale : minScale;
            float current = scaleStates[id];
            // Smooth spring animation with slight overshoot feel
            scaleStates[id] = Mathf.Lerp(current, target, Time.deltaTime * 10f);
            
            return scaleStates[id];
        }
        
        private float GetShineProgress(string id)
        {
            if (!shineTimers.ContainsKey(id))
                return 0f;
            
            float elapsed = Time.realtimeSinceStartup - shineTimers[id];
            float duration = 0.4f;
            
            if (elapsed > duration)
                return 0f;
            
            return 1f - (elapsed / duration);
        }
        
        private void DrawShineEffect(Rect rect, float shineProgress, Color baseColor)
        {
            if (shineProgress <= 0f)
                return;
            
            // Draw a white shine sweep across the element
            // Use manual clipping by calculating visible portion
            float shineWidth = rect.width * 0.4f;
            float shineX = Mathf.Lerp(-shineWidth, rect.width + shineWidth, 1f - shineProgress);
            
            // Calculate the actual shine rect in parent coordinates
            float shineLeft = rect.x + shineX - shineWidth / 2f;
            float shineRight = shineLeft + shineWidth;
            
            // Manually clip to parent rect bounds
            float clippedLeft = Mathf.Max(shineLeft, rect.x);
            float clippedRight = Mathf.Min(shineRight, rect.xMax);
            
            if (clippedRight <= clippedLeft)
                return; // Fully clipped, nothing to draw
            
            float clippedWidth = clippedRight - clippedLeft;
            
            // Create gradient shine effect with fade at edges (reduced opacity)
            float alpha = 0.15f * shineProgress;
            Color shineColor = new Color(1f, 1f, 1f, alpha);
            
            GUI.color = shineColor;
            Rect visibleShineRect = new Rect(clippedLeft, rect.y, clippedWidth, rect.height);
            GUI.DrawTexture(visibleShineRect, BaseContent.WhiteTex);
        }
        
        // Easing functions for smooth animations
        private float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }
        
        private float EaseOutBack(float t)
        {
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
        
        private float EaseOutElastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            
            float c4 = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * c4) + 1f;
        }
        
        private float PulseSine(float phase, float speed = 2f)
        {
            return (Mathf.Sin(phase * speed) + 1f) / 2f;
        }
        
        private string GetLevelRank(int level)
        {
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
        
        private string GetRankTranslated(string rank)
        {
            return ("Isekai_Rank_" + rank).Translate();
        }
        
        private string GetStatAbbr(string statKey)
        {
            return ("Isekai_Stat_" + statKey).Translate();
        }
        
        private int GetRankMultiplier(string rank)
        {
            switch (rank)
            {
                case "SSS": return 100;
                case "SS": return 50;
                case "S": return 25;
                case "A": return 12;
                case "B": return 6;
                case "C": return 3;
                case "D": return 2;
                case "E": return 1;
                default: return 1;
            }
        }
        
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
