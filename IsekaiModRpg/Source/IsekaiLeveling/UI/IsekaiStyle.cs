using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// New UI style inspired by Death March to the Parallel World Rhapsody
    /// Dark gradient backgrounds with golden accents
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IsekaiStyle
    {
        // ===== COLOR PALETTE =====
        
        // Background gradients (dark to darker)
        public static readonly Color BgDark = new Color(0.08f, 0.08f, 0.10f, 1f);
        public static readonly Color BgDarker = new Color(0.04f, 0.04f, 0.06f, 1f);
        public static readonly Color BgBlack = new Color(0.02f, 0.02f, 0.03f, 1f);
        
        // Panel backgrounds
        public static readonly Color PanelBg = new Color(0.10f, 0.10f, 0.12f, 0.95f);
        public static readonly Color PanelBorder = new Color(0.25f, 0.25f, 0.30f, 0.8f);
        
        // Header - Golden/Bronze gradient
        public static readonly Color HeaderGold = new Color(0.85f, 0.65f, 0.30f, 1f);
        public static readonly Color HeaderGoldDark = new Color(0.65f, 0.45f, 0.20f, 1f);
        public static readonly Color HeaderGoldLight = new Color(1f, 0.85f, 0.50f, 1f);
        
        // Accent colors
        public static readonly Color Gold = new Color(0.95f, 0.80f, 0.35f, 1f);
        public static readonly Color GoldDim = new Color(0.70f, 0.55f, 0.25f, 1f);
        public static readonly Color Blue = new Color(0.35f, 0.55f, 0.85f, 1f);
        public static readonly Color Purple = new Color(0.65f, 0.40f, 0.85f, 1f);
        public static readonly Color Red = new Color(0.85f, 0.30f, 0.35f, 1f);
        public static readonly Color Green = new Color(0.35f, 0.75f, 0.45f, 1f);
        public static readonly Color Cyan = new Color(0.35f, 0.75f, 0.85f, 1f);
        
        // Text colors
        public static readonly Color TextWhite = new Color(0.95f, 0.95f, 0.95f, 1f);
        public static readonly Color TextLight = new Color(0.80f, 0.80f, 0.85f, 1f);
        public static readonly Color TextGray = new Color(0.55f, 0.55f, 0.60f, 1f);
        public static readonly Color TextDark = new Color(0.35f, 0.35f, 0.40f, 1f);
        
        // Bar colors
        public static readonly Color BarHP = new Color(0.85f, 0.25f, 0.30f, 1f);
        public static readonly Color BarMP = new Color(0.30f, 0.45f, 0.90f, 1f);
        public static readonly Color BarStamina = new Color(0.35f, 0.75f, 0.40f, 1f);
        public static readonly Color BarXP = new Color(0.90f, 0.75f, 0.25f, 1f);
        public static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f, 1f);
        
        // ===== TEXTURES =====
        
        private static Texture2D _solidTex;
        private static Texture2D _gradientPanelTex;
        private static Texture2D _gradientHeaderTex;
        private static Texture2D _gradientBarTex;
        private static Texture2D _gradientSectionTex;
        
        public static Texture2D SolidTex
        {
            get
            {
                if (_solidTex == null)
                {
                    _solidTex = new Texture2D(1, 1);
                    _solidTex.SetPixel(0, 0, Color.white);
                    _solidTex.Apply();
                }
                return _solidTex;
            }
        }
        
        /// <summary>
        /// Vertical gradient for main panel background (dark to darker from top to bottom)
        /// </summary>
        public static Texture2D GradientPanelTex
        {
            get
            {
                if (_gradientPanelTex == null)
                {
                    _gradientPanelTex = CreateVerticalGradient(64, 
                        new Color(0.12f, 0.12f, 0.15f, 1f),  // Top: slightly lighter
                        new Color(0.03f, 0.03f, 0.04f, 1f)); // Bottom: very dark
                }
                return _gradientPanelTex;
            }
        }
        
        /// <summary>
        /// Horizontal gradient for golden header (bright center, darker edges)
        /// </summary>
        public static Texture2D GradientHeaderTex
        {
            get
            {
                if (_gradientHeaderTex == null)
                {
                    _gradientHeaderTex = CreateHorizontalGradient3(128,
                        new Color(0.60f, 0.45f, 0.18f, 1f),  // Left: darker gold
                        new Color(0.92f, 0.72f, 0.32f, 1f),  // Center: bright gold
                        new Color(0.55f, 0.40f, 0.15f, 1f)); // Right: darker gold
                }
                return _gradientHeaderTex;
            }
        }
        
        /// <summary>
        /// Vertical gradient for bars (brighter at top, darker at bottom for 3D effect)
        /// </summary>
        public static Texture2D GradientBarTex
        {
            get
            {
                if (_gradientBarTex == null)
                {
                    _gradientBarTex = CreateVerticalGradient(16,
                        new Color(1f, 1f, 1f, 1f),     // Top: bright
                        new Color(0.6f, 0.6f, 0.6f, 1f)); // Bottom: darker
                }
                return _gradientBarTex;
            }
        }
        
        /// <summary>
        /// Vertical gradient for section panels (subtle depth)
        /// </summary>
        public static Texture2D GradientSectionTex
        {
            get
            {
                if (_gradientSectionTex == null)
                {
                    _gradientSectionTex = CreateVerticalGradient(32,
                        new Color(0.08f, 0.08f, 0.10f, 1f),  // Top: slightly lighter
                        new Color(0.02f, 0.02f, 0.03f, 1f)); // Bottom: very dark
                }
                return _gradientSectionTex;
            }
        }
        
        private static Texture2D CreateVerticalGradient(int height, Color top, Color bottom)
        {
            var tex = new Texture2D(1, height);
            for (int y = 0; y < height; y++)
            {
                float t = (float)y / (height - 1);
                tex.SetPixel(0, y, Color.Lerp(bottom, top, t));
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
        
        private static Texture2D CreateHorizontalGradient3(int width, Color left, Color center, Color right)
        {
            var tex = new Texture2D(width, 1);
            int half = width / 2;
            for (int x = 0; x < width; x++)
            {
                Color c;
                if (x < half)
                {
                    float t = (float)x / half;
                    c = Color.Lerp(left, center, t);
                }
                else
                {
                    float t = (float)(x - half) / half;
                    c = Color.Lerp(center, right, t);
                }
                tex.SetPixel(x, 0, c);
            }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
        
        // ===== GUI STYLES =====
        
        private static GUIStyle _labelSmall;
        private static GUIStyle _labelMedium;
        private static GUIStyle _labelLarge;
        private static GUIStyle _labelTitle;
        private static GUIStyle _labelHeader;
        
        private static void EnsureStyles()
        {
            if (_labelSmall != null) return;
            
            _labelSmall = new GUIStyle(Text.CurFontStyle)
            {
                fontSize = 11,
                wordWrap = true
            };
            _labelSmall.normal.textColor = TextLight;
            
            _labelMedium = new GUIStyle(Text.CurFontStyle)
            {
                fontSize = 13,
                wordWrap = true
            };
            _labelMedium.normal.textColor = TextWhite;
            
            _labelLarge = new GUIStyle(Text.CurFontStyle)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _labelLarge.normal.textColor = TextWhite;
            
            _labelTitle = new GUIStyle(Text.CurFontStyle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _labelTitle.normal.textColor = TextWhite;
            
            _labelHeader = new GUIStyle(Text.CurFontStyle)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };
            _labelHeader.normal.textColor = BgDark;
        }
        
        public static GUIStyle LabelSmall { get { EnsureStyles(); return _labelSmall; } }
        public static GUIStyle LabelMedium { get { EnsureStyles(); return _labelMedium; } }
        public static GUIStyle LabelLarge { get { EnsureStyles(); return _labelLarge; } }
        public static GUIStyle LabelTitle { get { EnsureStyles(); return _labelTitle; } }
        public static GUIStyle LabelHeader { get { EnsureStyles(); return _labelHeader; } }
        
        // ===== DRAWING METHODS =====
        
        /// <summary>
        /// Draw text with custom color and alignment
        /// </summary>
        public static void Label(Rect rect, string text, GUIStyle style, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            EnsureStyles();
            Color prevGui = GUI.color;
            Color prevTextColor = style.normal.textColor;
            TextAnchor prevAlign = style.alignment;
            
            GUI.color = Color.white;
            style.normal.textColor = color;
            style.alignment = align;
            GUI.Label(rect, text, style);
            
            style.normal.textColor = prevTextColor;
            style.alignment = prevAlign;
            GUI.color = prevGui;
        }
        
        /// <summary>
        /// Draw the main panel background with dark gradient
        /// </summary>
        public static void DrawPanelBackground(Rect rect)
        {
            // Dark gradient background (top lighter, bottom darker)
            GUI.color = Color.white;
            GUI.DrawTexture(rect, GradientPanelTex, ScaleMode.StretchToFill);
            
            // Subtle border with gradient effect
            GUI.color = new Color(0.35f, 0.35f, 0.40f, 0.6f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), SolidTex); // Top - lighter
            GUI.color = new Color(0.15f, 0.15f, 0.18f, 0.8f);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), SolidTex); // Bottom - darker
            GUI.color = new Color(0.25f, 0.25f, 0.30f, 0.7f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), SolidTex);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), SolidTex);
            
            GUI.color = Color.white;
        }
        
        /// <summary>
        /// Draw golden header bar (like "STATUS | PARAMETER" in the image)
        /// </summary>
        public static void DrawGoldenHeader(Rect rect, string text, string text2 = null)
        {
            // Golden horizontal gradient background (darker edges, bright center)
            GUI.color = Color.white;
            GUI.DrawTexture(rect, GradientHeaderTex, ScaleMode.StretchToFill);
            
            // Darker bottom edge
            GUI.color = HeaderGoldDark;
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 3, rect.width, 3), SolidTex);
            
            // Light top edge
            GUI.color = HeaderGoldLight;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), SolidTex);
            
            // Text
            if (text2 != null)
            {
                // Two tabs style
                float halfWidth = rect.width / 2f;
                Label(new Rect(rect.x, rect.y, halfWidth, rect.height), text, LabelHeader, BgDark, TextAnchor.MiddleCenter);
                Label(new Rect(rect.x + halfWidth, rect.y, halfWidth, rect.height), text2, LabelHeader, new Color(BgDark.r, BgDark.g, BgDark.b, 0.6f), TextAnchor.MiddleCenter);
            }
            else
            {
                Label(rect, text, LabelHeader, BgDark, TextAnchor.MiddleCenter);
            }
            
            GUI.color = Color.white;
        }
        
        /// <summary>
        /// Draw a section panel (dark inset box with gradient)
        /// </summary>
        public static void DrawSectionPanel(Rect rect)
        {
            // Gradient background (subtle depth effect)
            GUI.color = Color.white;
            GUI.DrawTexture(rect, GradientSectionTex, ScaleMode.StretchToFill);
            
            // Inner shadow at top (gives inset appearance)
            GUI.color = new Color(0f, 0f, 0f, 0.3f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), SolidTex);
            
            // Inner highlight at bottom (subtle)
            GUI.color = new Color(1f, 1f, 1f, 0.02f);
            GUI.DrawTexture(new Rect(rect.x + 1, rect.yMax - 1, rect.width - 2, 1), SolidTex);
            
            GUI.color = Color.white;
        }
        
        /// <summary>
        /// Draw a horizontal bar with gradient (HP, MP, XP, etc.)
        /// </summary>
        public static void DrawBar(Rect rect, float fillPercent, Color barColor, Color bgColor = default)
        {
            if (bgColor == default) bgColor = BarBg;
            
            // Background with slight gradient
            GUI.color = new Color(bgColor.r * 0.7f, bgColor.g * 0.7f, bgColor.b * 0.7f, bgColor.a);
            GUI.DrawTexture(rect, SolidTex);
            GUI.color = bgColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, rect.height * 0.5f), SolidTex);
            
            // Fill with gradient effect
            if (fillPercent > 0)
            {
                Rect fillRect = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fillPercent), rect.height);
                
                // Use gradient texture with bar color tint
                GUI.color = barColor;
                GUI.DrawTexture(fillRect, GradientBarTex, ScaleMode.StretchToFill);
                
                // Bright highlight line at top
                GUI.color = new Color(barColor.r + 0.3f, barColor.g + 0.3f, barColor.b + 0.3f, 0.6f);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, 1), SolidTex);
                // Highlight at top
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, 1), SolidTex);
            }
            
            // Border
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), SolidTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), SolidTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), SolidTex);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), SolidTex);
            
            GUI.color = Color.white;
        }
        
        /// <summary>
        /// Draw a stat row with label, value box, and optional bonus
        /// </summary>
        public static void DrawStatRow(Rect rect, string statName, int value, int bonus = 0, Color? statColor = null)
        {
            float labelWidth = 50f;
            float valueWidth = 50f;
            float bonusWidth = 50f;
            
            Color sColor = statColor ?? TextLight;
            
            // Stat name
            Label(new Rect(rect.x, rect.y, labelWidth, rect.height), statName, LabelMedium, sColor, TextAnchor.MiddleLeft);
            
            // Value box
            Rect valueBox = new Rect(rect.x + labelWidth + 5, rect.y + 2, valueWidth, rect.height - 4);
            DrawSectionPanel(valueBox);
            Label(valueBox, value.ToString(), LabelMedium, TextWhite, TextAnchor.MiddleCenter);
            
            // Bonus (if any)
            if (bonus != 0)
            {
                string bonusText = bonus > 0 ? $"+{bonus}" : bonus.ToString();
                Color bonusColor = bonus > 0 ? Green : Red;
                Label(new Rect(rect.x + labelWidth + valueWidth + 10, rect.y, bonusWidth, rect.height), bonusText, LabelMedium, bonusColor, TextAnchor.MiddleLeft);
            }
        }
        
        /// <summary>
        /// Draw a clickable stat row with +/- buttons for allocation
        /// </summary>
        public static bool DrawStatRowWithButtons(Rect rect, string statName, int value, int bonus, bool canIncrease, bool canDecrease, Color? statColor = null)
        {
            float labelWidth = 45f;
            float buttonWidth = 22f;
            float valueWidth = 45f;
            float bonusWidth = 45f;
            
            Color sColor = statColor ?? TextLight;
            bool changed = false;
            
            // Stat name
            Label(new Rect(rect.x, rect.y, labelWidth, rect.height), statName, LabelMedium, sColor, TextAnchor.MiddleLeft);
            
            float curX = rect.x + labelWidth;
            
            // Minus button with gradient
            Rect minusRect = new Rect(curX, rect.y + 2, buttonWidth, rect.height - 4);
            if (canDecrease)
            {
                GUI.color = new Color(0.7f, 0.3f, 0.3f, 1f);
                GUI.DrawTexture(minusRect, GradientBarTex, ScaleMode.StretchToFill);
                Label(minusRect, "-", LabelMedium, TextWhite, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                
                if (Widgets.ButtonInvisible(minusRect))
                {
                    changed = true;
                }
            }
            else
            {
                GUI.color = new Color(0.25f, 0.25f, 0.28f, 0.5f);
                GUI.DrawTexture(minusRect, GradientBarTex, ScaleMode.StretchToFill);
                Label(minusRect, "-", LabelMedium, TextDark, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
            }
            curX += buttonWidth + 2;
            
            // Value box
            Rect valueBox = new Rect(curX, rect.y + 2, valueWidth, rect.height - 4);
            DrawSectionPanel(valueBox);
            Label(valueBox, value.ToString(), LabelMedium, Gold, TextAnchor.MiddleCenter);
            curX += valueWidth + 2;
            
            // Plus button with gradient
            Rect plusRect = new Rect(curX, rect.y + 2, buttonWidth, rect.height - 4);
            if (canIncrease)
            {
                GUI.color = new Color(0.3f, 0.7f, 0.35f, 1f);
                GUI.DrawTexture(plusRect, GradientBarTex, ScaleMode.StretchToFill);
                Label(plusRect, "+", LabelMedium, TextWhite, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
                
                if (Widgets.ButtonInvisible(plusRect))
                {
                    changed = true;
                }
            }
            else
            {
                GUI.color = new Color(0.25f, 0.25f, 0.28f, 0.5f);
                GUI.DrawTexture(plusRect, GradientBarTex, ScaleMode.StretchToFill);
                Label(plusRect, "+", LabelMedium, TextDark, TextAnchor.MiddleCenter);
                GUI.color = Color.white;
            }
            curX += buttonWidth + 8;
            
            // Bonus display
            if (bonus != 0)
            {
                string bonusText = bonus > 0 ? $"+{bonus}" : bonus.ToString();
                Color bonusColor = bonus > 0 ? Green : Red;
                Label(new Rect(curX, rect.y, bonusWidth, rect.height), bonusText, LabelSmall, bonusColor, TextAnchor.MiddleLeft);
            }
            
            return changed;
        }
        
        /// <summary>
        /// Draw a derived stat display (like Hit Points: 3100/3100)
        /// </summary>
        public static void DrawDerivedStat(Rect rect, string label, string value)
        {
            float labelWidth = rect.width * 0.55f;
            
            // Label
            Label(new Rect(rect.x, rect.y, labelWidth, rect.height), label, LabelSmall, TextGray, TextAnchor.MiddleRight);
            
            // Value box
            Rect valueBox = new Rect(rect.x + labelWidth + 5, rect.y + 2, rect.width - labelWidth - 5, rect.height - 4);
            DrawSectionPanel(valueBox);
            Label(valueBox, value, LabelSmall, TextWhite, TextAnchor.MiddleCenter);
        }
        
        /// <summary>
        /// Draw a simple button with gradient
        /// </summary>
        public static bool DrawButton(Rect rect, string text, bool highlighted = false)
        {
            bool hover = Mouse.IsOver(rect);
            
            // Gradient background
            if (hover)
            {
                GUI.color = new Color(0.32f, 0.32f, 0.36f, 1f);
                GUI.DrawTexture(rect, GradientBarTex, ScaleMode.StretchToFill);
            }
            else if (highlighted)
            {
                GUI.color = new Color(0.28f, 0.25f, 0.18f, 1f);
                GUI.DrawTexture(rect, GradientBarTex, ScaleMode.StretchToFill);
            }
            else
            {
                GUI.color = new Color(0.22f, 0.22f, 0.25f, 1f);
                GUI.DrawTexture(rect, GradientBarTex, ScaleMode.StretchToFill);
            }
            
            // Border with glow effect when highlighted
            GUI.color = highlighted ? Gold : (hover ? new Color(0.4f, 0.4f, 0.45f, 1f) : PanelBorder);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), SolidTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), SolidTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), SolidTex);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), SolidTex);
            
            // Text
            Label(rect, text, LabelMedium, highlighted ? Gold : TextWhite, TextAnchor.MiddleCenter);
            
            GUI.color = Color.white;
            return Widgets.ButtonInvisible(rect);
        }
        
        /// <summary>
        /// Draw section header with line
        /// </summary>
        public static void DrawSectionHeader(Rect rect, string text)
        {
            Label(new Rect(rect.x, rect.y, 100f, rect.height), text, LabelMedium, Gold, TextAnchor.MiddleLeft);
            
            // Line after text
            float textWidth = LabelMedium.CalcSize(new GUIContent(text)).x + 10f;
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.3f);
            GUI.DrawTexture(new Rect(rect.x + textWidth, rect.y + rect.height / 2f, rect.width - textWidth, 1), SolidTex);
            GUI.color = Color.white;
        }
    }
}
