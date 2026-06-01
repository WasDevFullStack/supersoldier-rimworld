using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// A sexy, designer-quality listing class for Isekai UI.
    /// Provides smooth hover animations, consistent styling, and beautiful components.
    /// </summary>
    public class IsekaiListing : IDisposable
    {
        // Layout state
        private Rect containerRect;
        private float curY;
        private float curX;
        private float columnWidth;
        private int currentColumn;
        private int numColumns = 1;
        private float columnGap = 12f;
        private float alpha = 1f;
        
        // Animation helpers (static to persist across frames)
        private static readonly System.Collections.Generic.Dictionary<string, float> hoverStates 
            = new System.Collections.Generic.Dictionary<string, float>();
        
        // ═══════════════════════════════════════════════════════════════
        // COLOR PALETTE - Warm, elegant, premium feel
        // ═══════════════════════════════════════════════════════════════
        public static readonly Color TextPrimary = new Color(0.92f, 0.88f, 0.82f);
        public static readonly Color TextSecondary = new Color(0.65f, 0.60f, 0.55f);
        public static readonly Color TextMuted = new Color(0.45f, 0.42f, 0.40f);
        public static readonly Color AccentGold = new Color(0.85f, 0.70f, 0.45f);
        public static readonly Color AccentCopper = new Color(0.75f, 0.55f, 0.40f);
        public static readonly Color AccentSuccess = new Color(0.45f, 0.75f, 0.45f);
        public static readonly Color AccentDanger = new Color(0.85f, 0.40f, 0.40f);
        
        public static readonly Color BgDark = new Color(0.08f, 0.07f, 0.07f);
        public static readonly Color BgCard = new Color(0.14f, 0.12f, 0.12f);
        public static readonly Color BgHover = new Color(0.22f, 0.20f, 0.18f);
        public static readonly Color BgPressed = new Color(0.18f, 0.16f, 0.14f);
        
        // Stat-specific colors
        public static readonly Color StatSTR = new Color(0.95f, 0.45f, 0.4f);
        public static readonly Color StatVIT = new Color(0.95f, 0.65f, 0.35f);
        public static readonly Color StatDEX = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color StatINT = new Color(0.45f, 0.65f, 0.98f);
        public static readonly Color StatWIS = new Color(0.75f, 0.55f, 0.95f);
        public static readonly Color StatCHA = new Color(0.98f, 0.88f, 0.35f);
        
        // ═══════════════════════════════════════════════════════════════
        // CONSTRUCTOR & LIFECYCLE
        // ═══════════════════════════════════════════════════════════════
        
        public IsekaiListing(Rect rect, float alpha = 1f)
        {
            this.containerRect = rect;
            this.curY = rect.y;
            this.curX = rect.x;
            this.columnWidth = rect.width;
            this.alpha = alpha;
        }
        
        public void Dispose()
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }
        
        // ═══════════════════════════════════════════════════════════════
        // LAYOUT CONTROLS
        // ═══════════════════════════════════════════════════════════════
        
        public void BeginColumns(int columns, float gap = 12f)
        {
            numColumns = columns;
            columnGap = gap;
            columnWidth = (containerRect.width - (gap * (columns - 1))) / columns;
            currentColumn = 0;
        }
        
        public void EndColumns()
        {
            if (currentColumn > 0)
            {
                curY += 22f; // Advance to next row
            }
            numColumns = 1;
            columnWidth = containerRect.width;
            currentColumn = 0;
            curX = containerRect.x;
        }
        
        public void NextColumn()
        {
            currentColumn++;
            if (currentColumn >= numColumns)
            {
                currentColumn = 0;
                curY += 22f;
                curX = containerRect.x;
            }
            else
            {
                curX = containerRect.x + (columnWidth + columnGap) * currentColumn;
            }
        }
        
        public void Gap(float height = 8f)
        {
            curY += height;
        }
        
        public void GapLine(float height = 1f, float vPadding = 6f)
        {
            curY += vPadding;
            GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.3f * alpha);
            GUI.DrawTexture(new Rect(containerRect.x, curY, containerRect.width, height), BaseContent.WhiteTex);
            curY += height + vPadding;
            GUI.color = Color.white;
        }
        
        public float CurY => curY;
        public float RemainingHeight => containerRect.yMax - curY;
        
        // ═══════════════════════════════════════════════════════════════
        // TEXT COMPONENTS
        // ═══════════════════════════════════════════════════════════════
        
        public void Label(string text, GameFont font = GameFont.Small, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            float height = font == GameFont.Tiny ? 18f : (font == GameFont.Medium ? 28f : 22f);
            Rect rect = new Rect(curX, curY, columnWidth, height);
            
            Color c = color ?? TextPrimary;
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            Text.Font = font;
            Text.Anchor = anchor;
            Widgets.Label(rect, text);
            
            if (numColumns == 1) curY += height;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        public void LabelCentered(string text, GameFont font = GameFont.Small, Color? color = null)
        {
            Label(text, font, color, TextAnchor.MiddleCenter);
        }
        
        public void Header(string text)
        {
            float height = 26f;
            Rect rect = new Rect(containerRect.x, curY, containerRect.width, height);
            
            // Subtle line under header
            GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, 0.4f * alpha);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BaseContent.WhiteTex);
            
            GUI.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(rect, text);
            
            curY += height + 4f;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        // ═══════════════════════════════════════════════════════════════
        // STAT ROW - The star of the show
        // ═══════════════════════════════════════════════════════════════
        
        public bool StatRow(string label, int value, Color accentColor, string tooltip = null, bool showPlusButton = false)
        {
            float height = 24f;
            Rect rowRect = new Rect(curX, curY, columnWidth, height);
            
            string hoverId = $"stat_{label}_{curY}";
            float hover = AnimateHover(hoverId, Mouse.IsOver(rowRect));
            
            // Background on hover - subtle glass effect
            if (hover > 0.01f)
            {
                Color bgColor = Color.Lerp(Color.clear, new Color(BgHover.r, BgHover.g, BgHover.b, 0.5f), hover);
                GUI.color = new Color(bgColor.r, bgColor.g, bgColor.b, bgColor.a * alpha);
                
                // Rounded-ish look with gradient
                GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
            }
            
            // Accent bar on the left
            float barWidth = Mathf.Lerp(2f, 3f, hover);
            GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, alpha);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.y + 3f, barWidth, rowRect.height - 6f), BaseContent.WhiteTex);
            
            // Label
            Color labelColor = Color.Lerp(TextSecondary, TextPrimary, hover);
            GUI.color = new Color(labelColor.r, labelColor.g, labelColor.b, alpha);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y, 45f, rowRect.height), label);
            
            // Value - slightly larger, brighter
            GUI.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            Text.Anchor = TextAnchor.MiddleRight;
            float valueRightOffset = showPlusButton ? 28f : 4f;
            Widgets.Label(new Rect(rowRect.x + 50f, rowRect.y, rowRect.width - 54f - valueRightOffset, rowRect.height), 
                value.ToString());
            
            // Plus button
            bool clicked = false;
            int maxStat = IsekaiLeveling.IsekaiStatAllocation.GetEffectiveMaxStat();
            if (showPlusButton && value < maxStat)
            {
                Rect btnRect = new Rect(rowRect.xMax - 22f, rowRect.y + 3f, 18f, rowRect.height - 6f);
                clicked = PlusButton(btnRect, $"plus_{label}");
            }
            
            // Tooltip
            if (!string.IsNullOrEmpty(tooltip))
            {
                TooltipHandler.TipRegion(rowRect, tooltip);
            }
            
            if (numColumns == 1) curY += height + 2f;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return clicked;
        }
        
        /// <summary>
        /// Compact stat row for 2-column layouts
        /// </summary>
        public bool StatRowCompact(string abbr, int value, Color accentColor, bool showPlus = false)
        {
            float height = 22f;
            Rect rowRect = new Rect(curX, curY, columnWidth, height);
            
            string hoverId = $"cstat_{abbr}_{curX}_{curY}";
            float hover = AnimateHover(hoverId, Mouse.IsOver(rowRect));
            
            // Hover bg
            if (hover > 0.01f)
            {
                GUI.color = new Color(BgHover.r, BgHover.g, BgHover.b, 0.4f * hover * alpha);
                GUI.DrawTexture(rowRect, BaseContent.WhiteTex);
            }
            
            // Accent pip
            GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, alpha);
            GUI.DrawTexture(new Rect(rowRect.x, rowRect.y + 6f, 3f, rowRect.height - 12f), BaseContent.WhiteTex);
            
            // Abbr
            GUI.color = new Color(accentColor.r, accentColor.g, accentColor.b, Mathf.Lerp(0.8f, 1f, hover) * alpha);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y, 32f, rowRect.height), abbr);
            
            // Value
            GUI.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            Text.Anchor = TextAnchor.MiddleRight;
            float valueWidth = showPlus ? columnWidth - 60f : columnWidth - 40f;
            Widgets.Label(new Rect(rowRect.x + 38f, rowRect.y, valueWidth, rowRect.height), value.ToString());
            
            bool clicked = false;
            int maxStatCompact = IsekaiLeveling.IsekaiStatAllocation.GetEffectiveMaxStat();
            if (showPlus && value < maxStatCompact)
            {
                Rect btnRect = new Rect(rowRect.xMax - 20f, rowRect.y + 3f, 16f, rowRect.height - 6f);
                clicked = PlusButton(btnRect, $"cplus_{abbr}_{curX}");
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return clicked;
        }
        
        // ═══════════════════════════════════════════════════════════════
        // PROGRESS BARS
        // ═══════════════════════════════════════════════════════════════
        
        public void ProgressBar(string label, float value, float max, Color fillColor, bool showValues = true)
        {
            float height = 20f;
            Rect rect = new Rect(containerRect.x, curY, containerRect.width, height);
            
            // Label
            if (!string.IsNullOrEmpty(label))
            {
                GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, alpha);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rect.x, rect.y, 35f, 14f), label);
            }
            
            // Values on the right
            if (showValues)
            {
                GUI.color = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, alpha);
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(rect.x, rect.y, rect.width, 14f), $"{NumberFormatting.FormatNum(value)} / {NumberFormatting.FormatNum(max)}");
            }
            
            // Bar track
            Rect barRect = new Rect(rect.x, rect.y + 14f, rect.width, 5f);
            GUI.color = new Color(BgDark.r, BgDark.g, BgDark.b, alpha);
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);
            
            // Bar fill with glow effect
            float fillPct = Mathf.Clamp01(value / Mathf.Max(1f, max));
            if (fillPct > 0f)
            {
                Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * fillPct, barRect.height);
                
                // Main fill
                GUI.color = new Color(fillColor.r, fillColor.g, fillColor.b, alpha);
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
                
                // Highlight on top edge
                GUI.color = new Color(1f, 1f, 1f, 0.15f * alpha);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, 1f), BaseContent.WhiteTex);
            }
            
            curY += height + 2f;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        public void XPBar(long currentXP, long xpToNext)
        {
            ProgressBar("EXP", currentXP, xpToNext, AccentGold, true);
        }
        
        // ═══════════════════════════════════════════════════════════════
        // BUTTONS
        // ═══════════════════════════════════════════════════════════════
        
        public bool Button(string label, float height = 28f, Color? bgColor = null)
        {
            Rect rect = new Rect(containerRect.x, curY, containerRect.width, height);
            
            string hoverId = $"btn_{label}_{curY}";
            float hover = AnimateHover(hoverId, Mouse.IsOver(rect));
            
            // Background with hover effect
            Color bg = bgColor ?? BgCard;
            bg = Color.Lerp(bg, BgHover, hover * 0.5f);
            
            GUI.color = new Color(bg.r, bg.g, bg.b, alpha);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Border accent
            GUI.color = new Color(AccentCopper.r, AccentCopper.g, AccentCopper.b, 0.5f * alpha);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), BaseContent.WhiteTex);
            
            // Label
            Color textColor = Color.Lerp(TextSecondary, TextPrimary, hover);
            GUI.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            
            curY += height + 4f;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            return Widgets.ButtonInvisible(rect);
        }
        
        public bool ButtonSmall(string label, float width = 60f)
        {
            Rect rect = new Rect(curX, curY, width, 20f);
            
            string hoverId = $"sbtn_{label}_{curX}_{curY}";
            float hover = AnimateHover(hoverId, Mouse.IsOver(rect));
            
            Color bg = Color.Lerp(BgCard, BgHover, hover);
            GUI.color = new Color(bg.r, bg.g, bg.b, alpha);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            GUI.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            
            curX += width + 4f;
            return Widgets.ButtonInvisible(rect);
        }
        
        /// <summary>Draws a labeled checkbox and advances curY.</summary>
        public void Checkbox(string label, ref bool value, string tooltip = null)
        {
            Rect rect = new Rect(containerRect.x, curY, containerRect.width, 20f);
            GUI.color = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, alpha);
            Text.Font = GameFont.Tiny;
            Widgets.CheckboxLabeled(rect, label, ref value);
            if (tooltip != null)
                TooltipHandler.TipRegion(rect, tooltip);
            GUI.color = Color.white;
            curY += 20f;
        }
        
        private bool PlusButton(Rect rect, string id)
        {
            float hover = AnimateHover(id, Mouse.IsOver(rect));
            
            Color bg = Color.Lerp(
                new Color(AccentSuccess.r, AccentSuccess.g, AccentSuccess.b, 0.6f),
                new Color(AccentSuccess.r, AccentSuccess.g, AccentSuccess.b, 1f),
                hover
            );
            GUI.color = new Color(bg.r, bg.g, bg.b, bg.a * alpha);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, "+");
            
            return Widgets.ButtonInvisible(rect);
        }
        
        // ═══════════════════════════════════════════════════════════════
        // BADGES & INDICATORS
        // ═══════════════════════════════════════════════════════════════
        
        public void Badge(string text, Color bgColor)
        {
            float width = Text.CalcSize(text).x + 16f;
            float height = 20f;
            Rect rect = new Rect(containerRect.x + (containerRect.width - width) / 2f, curY, width, height);
            
            // Glow effect
            GUI.color = new Color(bgColor.r, bgColor.g, bgColor.b, 0.2f * alpha);
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 1f, rect.width + 4f, rect.height + 2f), BaseContent.WhiteTex);
            
            // Badge bg
            GUI.color = new Color(bgColor.r, bgColor.g, bgColor.b, 0.85f * alpha);
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            
            // Text
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            
            curY += height + 4f;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
        
        public void PointsAvailable(int points)
        {
            if (points > 0)
            {
                Badge($"✦ {points} Points Available ✦", AccentGold);
            }
        }
        
        // ═══════════════════════════════════════════════════════════════
        // ANIMATION HELPERS
        // ═══════════════════════════════════════════════════════════════
        
        private float AnimateHover(string id, bool isHovered)
        {
            if (!hoverStates.TryGetValue(id, out float current))
                current = 0f;
            
            float target = isHovered ? 1f : 0f;
            current = Mathf.MoveTowards(current, target, Time.deltaTime * 10f);
            hoverStates[id] = current;
            
            // Prevent unbounded growth: clear stale entries when dict gets too large
            if (hoverStates.Count > 500)
            {
                CleanupHoverStates();
            }
            
            return current;
        }
        
        /// <summary>
        /// Remove hover states that have fully faded out (value ~0).
        /// </summary>
        public static void CleanupHoverStates()
        {
            var toRemove = new System.Collections.Generic.List<string>();
            foreach (var kvp in hoverStates)
            {
                if (kvp.Value <= 0.001f)
                    toRemove.Add(kvp.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
                hoverStates.Remove(toRemove[i]);
        }
    }
}
