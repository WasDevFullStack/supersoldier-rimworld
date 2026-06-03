using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Modern neumorphic UI helper for the Isekai Leveling mod
    /// Provides soft shadows, highlights, and modern styling
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IsekaiUIHelper
    {
        // Color Palette - Deep black with single blue accent
        public static readonly Color Background = new Color(0.02f, 0.02f, 0.02f, 1f);           // Deep black
        public static readonly Color Surface = new Color(0.06f, 0.06f, 0.06f, 1f);              // Slightly lighter black
        public static readonly Color SurfaceLight = new Color(0.10f, 0.10f, 0.10f, 1f);         // Card surfaces
        public static readonly Color SurfaceDark = new Color(0.01f, 0.01f, 0.01f, 1f);          // Inset areas (near black)
        
        // Single blue accent
        public static readonly Color Accent = new Color(0.20f, 0.50f, 0.95f, 1f);               // Main blue
        public static readonly Color AccentLight = new Color(0.20f, 0.50f, 0.95f, 1f);          // Same blue
        public static readonly Color AccentDark = new Color(0.20f, 0.50f, 0.95f, 1f);           // Same blue
        
        // Secondary accent - Gold for XP/levels
        public static readonly Color Gold = new Color(1f, 0.84f, 0.35f, 1f);
        public static readonly Color GoldDark = new Color(0.85f, 0.65f, 0.15f, 1f);
        
        // Text colors
        public static readonly Color TextPrimary = new Color(0.95f, 0.95f, 0.95f, 1f);
        public static readonly Color TextSecondary = new Color(0.60f, 0.60f, 0.60f, 1f);
        public static readonly Color TextMuted = new Color(0.40f, 0.40f, 0.40f, 1f);
        
        // Status colors
        public static readonly Color Success = new Color(0.30f, 0.85f, 0.50f, 1f);
        public static readonly Color Warning = new Color(1f, 0.75f, 0.25f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.35f, 0.40f, 1f);
        
        // Shadow/highlight for neumorphism
        public static readonly Color ShadowDark = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color ShadowLight = new Color(1f, 1f, 1f, 0.05f);
        
        private static Texture2D solidTexture;
        private static Texture2D circleTexture;
        private static Texture2D hexagonTexture;
        private static Texture2D hexagonMaskTexture;
        private static Texture2D roundedRectTexture;
        
        // GUIStyle cache - will be created on first use during OnGUI
        private static GUIStyle _labelStyleSmall;
        private static GUIStyle _labelStyleMedium;
        private static GUIStyle _labelStyleLarge;
        private static GUIStyle _labelStyleTitle;
        
        // Corner radius for rounded elements
        private const int RoundedCornerRadius = 8;
        private const int RoundedTextureSize = 32;
        
        /// <summary>
        /// Ensures GUIStyles are created (must be called during OnGUI)
        /// </summary>
        private static void EnsureStylesInitialized()
        {
            if (_labelStyleSmall != null) return;
            
            // Create styles based on RimWorld's existing label style
            _labelStyleSmall = new GUIStyle(Text.CurFontStyle);
            _labelStyleSmall.fontSize = 11;
            _labelStyleSmall.normal.textColor = TextPrimary;
            _labelStyleSmall.wordWrap = true;
            
            _labelStyleMedium = new GUIStyle(Text.CurFontStyle);
            _labelStyleMedium.fontSize = 13;
            _labelStyleMedium.normal.textColor = TextPrimary;
            _labelStyleMedium.wordWrap = true;
            
            _labelStyleLarge = new GUIStyle(Text.CurFontStyle);
            _labelStyleLarge.fontSize = 16;
            _labelStyleLarge.fontStyle = FontStyle.Bold;
            _labelStyleLarge.normal.textColor = TextPrimary;
            _labelStyleLarge.wordWrap = true;
            
            _labelStyleTitle = new GUIStyle(Text.CurFontStyle);
            _labelStyleTitle.fontSize = 20;
            _labelStyleTitle.fontStyle = FontStyle.Bold;
            _labelStyleTitle.normal.textColor = TextPrimary;
            _labelStyleTitle.wordWrap = true;
        }
        
        /// <summary>
        /// Get a GUIStyle for small text
        /// </summary>
        public static GUIStyle LabelStyleSmall
        {
            get
            {
                EnsureStylesInitialized();
                return _labelStyleSmall;
            }
        }
        
        /// <summary>
        /// Get a GUIStyle for medium text
        /// </summary>
        public static GUIStyle LabelStyleMedium
        {
            get
            {
                EnsureStylesInitialized();
                return _labelStyleMedium;
            }
        }
        
        /// <summary>
        /// Get a GUIStyle for large text
        /// </summary>
        public static GUIStyle LabelStyleLarge
        {
            get
            {
                EnsureStylesInitialized();
                return _labelStyleLarge;
            }
        }
        
        /// <summary>
        /// Get a GUIStyle for title text
        /// </summary>
        public static GUIStyle LabelStyleTitle
        {
            get
            {
                EnsureStylesInitialized();
                return _labelStyleTitle;
            }
        }
        
        /// <summary>
        /// Draw text with custom style and color
        /// </summary>
        public static void DrawLabel(Rect rect, string text, GUIStyle style, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            EnsureStylesInitialized();
            
            // Save original values, mutate in-place, then restore (zero-alloc)
            Color prevColor = GUI.color;
            Color prevTextColor = style.normal.textColor;
            TextAnchor prevAlignment = style.alignment;
            
            GUI.color = Color.white;
            style.normal.textColor = color;
            style.alignment = alignment;
            GUI.Label(rect, text, style);
            
            // Restore
            style.normal.textColor = prevTextColor;
            style.alignment = prevAlignment;
            GUI.color = prevColor;
        }
        
        /// <summary>
        /// Draw text with custom style (uses style's default text color)
        /// </summary>
        public static void DrawLabel(Rect rect, string text, GUIStyle style, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            DrawLabel(rect, text, style, style.normal.textColor, alignment);
        }
        
        public static Texture2D SolidTexture
        {
            get
            {
                if (solidTexture == null)
                {
                    solidTexture = new Texture2D(1, 1);
                    solidTexture.SetPixel(0, 0, Color.white);
                    solidTexture.Apply();
                }
                return solidTexture;
            }
        }
        
        /// <summary>
        /// Procedurally generated rounded rectangle texture
        /// </summary>
        public static Texture2D RoundedRectTexture
        {
            get
            {
                if (roundedRectTexture == null)
                {
                    int size = RoundedTextureSize;
                    int radius = RoundedCornerRadius;
                    roundedRectTexture = new Texture2D(size, size);
                    
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float alpha = 1f;
                            
                            // Check corners
                            // Bottom-left
                            if (x < radius && y < radius)
                            {
                                float dist = Mathf.Sqrt((x - radius) * (x - radius) + (y - radius) * (y - radius));
                                if (dist > radius) alpha = 0f;
                                else if (dist > radius - 1.5f) alpha = (radius - dist) / 1.5f;
                            }
                            // Bottom-right
                            else if (x >= size - radius && y < radius)
                            {
                                float dist = Mathf.Sqrt((x - (size - radius - 1)) * (x - (size - radius - 1)) + (y - radius) * (y - radius));
                                if (dist > radius) alpha = 0f;
                                else if (dist > radius - 1.5f) alpha = (radius - dist) / 1.5f;
                            }
                            // Top-left
                            else if (x < radius && y >= size - radius)
                            {
                                float dist = Mathf.Sqrt((x - radius) * (x - radius) + (y - (size - radius - 1)) * (y - (size - radius - 1)));
                                if (dist > radius) alpha = 0f;
                                else if (dist > radius - 1.5f) alpha = (radius - dist) / 1.5f;
                            }
                            // Top-right
                            else if (x >= size - radius && y >= size - radius)
                            {
                                float dist = Mathf.Sqrt((x - (size - radius - 1)) * (x - (size - radius - 1)) + (y - (size - radius - 1)) * (y - (size - radius - 1)));
                                if (dist > radius) alpha = 0f;
                                else if (dist > radius - 1.5f) alpha = (radius - dist) / 1.5f;
                            }
                            
                            roundedRectTexture.SetPixel(x, y, new Color(1, 1, 1, alpha));
                        }
                    }
                    roundedRectTexture.Apply();
                }
                return roundedRectTexture;
            }
        }
        
        /// <summary>
        /// Draw a rounded rectangle with the given color
        /// </summary>
        public static void DrawRoundedRect(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, RoundedRectTexture, ScaleMode.StretchToFill);
            GUI.color = Color.white;
        }
        
        /// <summary>
        /// Procedurally generated circle texture for round skill nodes
        /// </summary>
        public static Texture2D CircleTexture
        {
            get
            {
                if (circleTexture == null)
                {
                    int size = 64;
                    circleTexture = new Texture2D(size, size);
                    float radius = size / 2f;
                    float center = size / 2f;
                    
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dist = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                            if (dist < radius - 1)
                                circleTexture.SetPixel(x, y, Color.white);
                            else if (dist < radius)
                                circleTexture.SetPixel(x, y, new Color(1, 1, 1, radius - dist));
                            else
                                circleTexture.SetPixel(x, y, Color.clear);
                        }
                    }
                    circleTexture.Apply();
                }
                return circleTexture;
            }
        }
        
        /// <summary>
        /// Procedurally generated hexagon texture for active skill nodes
        /// </summary>
        public static Texture2D HexagonTexture
        {
            get
            {
                if (hexagonTexture == null)
                {
                    int size = 64;
                    hexagonTexture = new Texture2D(size, size);
                    float center = size / 2f;
                    float radius = size / 2f - 2;
                    
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            // Hexagon check using six lines
                            float px = x - center;
                            float py = y - center;
                            
                            // Hexagon formula
                            float q2x = Mathf.Abs(px);
                            float q2y = Mathf.Abs(py);
                            float hexDist = Mathf.Max(q2x * 0.866025f + q2y * 0.5f, q2y);
                            
                            if (hexDist < radius - 1)
                                hexagonTexture.SetPixel(x, y, Color.white);
                            else if (hexDist < radius)
                                hexagonTexture.SetPixel(x, y, new Color(1, 1, 1, radius - hexDist));
                            else
                                hexagonTexture.SetPixel(x, y, Color.clear);
                        }
                    }
                    hexagonTexture.Apply();
                }
                return hexagonTexture;
            }
        }
        
        /// <summary>
        /// Hexagon mask texture (opaque outside, transparent inside) for cropping icons
        /// </summary>
        public static Texture2D HexagonMaskTexture
        {
            get
            {
                if (hexagonMaskTexture == null)
                {
                    int size = 64;
                    hexagonMaskTexture = new Texture2D(size, size);
                    float center = size / 2f;
                    float radius = size / 2f - 2;
                    
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float px = x - center;
                            float py = y - center;
                            
                            // Hexagon formula
                            float q2x = Mathf.Abs(px);
                            float q2y = Mathf.Abs(py);
                            float hexDist = Mathf.Max(q2x * 0.866025f + q2y * 0.5f, q2y);
                            
                            // Inverse of hexagon - opaque outside, transparent inside
                            if (hexDist < radius - 1)
                                hexagonMaskTexture.SetPixel(x, y, Color.clear);
                            else if (hexDist < radius)
                                hexagonMaskTexture.SetPixel(x, y, new Color(1, 1, 1, hexDist - (radius - 1)));
                            else
                                hexagonMaskTexture.SetPixel(x, y, Color.white);
                        }
                    }
                    hexagonMaskTexture.Apply();
                }
                return hexagonMaskTexture;
            }
        }

        /// <summary>
        /// Draw a neumorphic card/panel with soft shadows
        /// </summary>
        public static void DrawNeumorphicPanel(Rect rect, bool raised = true, bool useAccent = false)
        {
            // Outer shadow (bottom-right)
            if (raised)
            {
                GUI.color = ShadowDark;
                GUI.DrawTexture(new Rect(rect.x + 4, rect.y + 4, rect.width, rect.height), SolidTexture);
            }
            
            // Main surface
            GUI.color = useAccent ? Accent : Surface;
            GUI.DrawTexture(rect, SolidTexture);
            
            // Top-left highlight
            if (raised)
            {
                GUI.color = ShadowLight;
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width - 1, 2), SolidTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height - 1), SolidTexture);
            }
            
            // Bottom edge (subtle)
            GUI.color = new Color(0f, 0f, 0f, 0.2f);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - 1, rect.width, 1), SolidTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1, rect.y, 1, rect.height), SolidTexture);
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a pressed/inset neumorphic panel
        /// </summary>
        public static void DrawNeumorphicInset(Rect rect)
        {
            // Inner shadow (top-left)
            GUI.color = ShadowDark;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 3), SolidTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), SolidTexture);
            
            // Main surface (darker)
            GUI.color = SurfaceDark;
            GUI.DrawTexture(rect.ContractedBy(1), SolidTexture);
            
            // Bottom-right highlight
            GUI.color = ShadowLight;
            GUI.DrawTexture(new Rect(rect.x + 1, rect.yMax - 2, rect.width - 2, 1), SolidTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 2, rect.y + 1, 1, rect.height - 2), SolidTexture);
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a modern button with hover and press states
        /// </summary>
        public static bool DrawNeumorphicButton(Rect rect, string label, bool accent = false, bool small = false)
        {
            bool isHovered = Mouse.IsOver(rect);
            bool isPressed = isHovered && Event.current.type == EventType.MouseDown;
            
            // Shadow
            if (!isPressed)
            {
                GUI.color = ShadowDark;
                GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), SolidTexture);
            }
            
            // Button background
            Color bgColor;
            if (accent)
            {
                bgColor = isPressed ? new Color(Accent.r * 0.7f, Accent.g * 0.7f, Accent.b * 0.7f) : 
                         (isHovered ? new Color(Accent.r * 1.2f, Accent.g * 1.2f, Accent.b * 1.2f) : Accent);
            }
            else
            {
                bgColor = isPressed ? SurfaceDark : (isHovered ? SurfaceLight : Surface);
            }
            
            GUI.color = bgColor;
            Rect buttonRect = isPressed ? new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height) : rect;
            GUI.DrawTexture(buttonRect, SolidTexture);
            
            // Highlight edge
            if (!isPressed)
            {
                GUI.color = ShadowLight;
                GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y, buttonRect.width, 1), SolidTexture);
                GUI.DrawTexture(new Rect(buttonRect.x, buttonRect.y, 1, buttonRect.height), SolidTexture);
            }
            
            // Label
            Color labelColor = accent ? TextPrimary : (isHovered ? TextPrimary : TextSecondary);
            GUIStyle buttonStyle = small ? LabelStyleSmall : LabelStyleMedium;
            DrawLabel(buttonRect, label, buttonStyle, labelColor, TextAnchor.MiddleCenter);
            GUI.color = Color.white;
            
            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>
        /// Draw a modern progress bar with glow effect
        /// </summary>
        public static void DrawProgressBar(Rect rect, float progress, Color barColor, bool showGlow = true)
        {
            progress = Mathf.Clamp01(progress);
            
            // Background (inset)
            DrawNeumorphicInset(rect);
            
            // Progress fill
            Rect fillRect = rect.ContractedBy(3);
            fillRect.width *= progress;
            
            if (fillRect.width > 0)
            {
                // Glow effect
                if (showGlow && progress > 0.02f)
                {
                    GUI.color = new Color(barColor.r, barColor.g, barColor.b, 0.3f);
                    GUI.DrawTexture(new Rect(fillRect.x - 2, fillRect.y - 2, fillRect.width + 4, fillRect.height + 4), SolidTexture);
                }
                
                // Main bar
                GUI.color = barColor;
                GUI.DrawTexture(fillRect, SolidTexture);
                
                // Highlight on bar
                GUI.color = new Color(1f, 1f, 1f, 0.2f);
                GUI.DrawTexture(new Rect(fillRect.x, fillRect.y, fillRect.width, 2), SolidTexture);
            }
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a skill node with neumorphic styling (wrapper for compatibility)
        /// </summary>
        public static bool DrawSkillNode(Rect rect, string name, int currentTier, int maxTier, bool isUnlocked, bool canUnlock, bool isHovered)
        {
            return DrawSkillNode(rect, name, currentTier, maxTier, isUnlocked, canUnlock, isHovered, false);
        }
        
        /// <summary>
        /// Draw a round skill node with neumorphic styling
        /// Active skills use hexagon shape, passive skills use circle
        /// </summary>
        public static bool DrawSkillNode(Rect rect, string name, int currentTier, int maxTier, bool isUnlocked, bool canUnlock, bool isHovered, bool isActiveSkill)
        {
            bool wasClicked = false;
            
            // Determine state colors
            Color nodeColor;
            Color borderColor;
            Color glowColor;
            
            if (isUnlocked)
            {
                nodeColor = AccentDark;
                borderColor = Accent;
                glowColor = isActiveSkill ? new Color(1f, 0.5f, 0.2f, 0.6f) : new Color(Accent.r, Accent.g, Accent.b, 0.4f);
            }
            else if (canUnlock)
            {
                nodeColor = Surface;
                borderColor = AccentLight;
                glowColor = new Color(AccentLight.r, AccentLight.g, AccentLight.b, 0.2f);
            }
            else
            {
                nodeColor = SurfaceDark;
                borderColor = TextMuted;
                glowColor = Color.clear;
            }
            
            // Active skills get orange/red tint
            if (isActiveSkill)
            {
                if (isUnlocked)
                {
                    nodeColor = new Color(0.45f, 0.25f, 0.15f, 1f);
                    borderColor = new Color(1f, 0.6f, 0.3f, 1f);
                }
                else if (canUnlock)
                {
                    borderColor = new Color(1f, 0.7f, 0.4f, 1f);
                }
            }
            
            // Calculate circle dimensions (make it square for proper circle)
            float size = Mathf.Min(rect.width, rect.height);
            Rect circleRect = new Rect(rect.x + (rect.width - size) / 2f, rect.y, size, size);
            
            // Use different textures for active vs passive
            Texture2D shapeTexture = isActiveSkill ? HexagonTexture : CircleTexture;
            
            // Outer glow for unlocked skills
            if (isUnlocked || (canUnlock && isHovered))
            {
                GUI.color = glowColor;
                Rect glowRect = new Rect(circleRect.x - 4, circleRect.y - 4, circleRect.width + 8, circleRect.height + 8);
                GUI.DrawTexture(glowRect, shapeTexture);
            }
            
            // Shadow
            GUI.color = ShadowDark;
            GUI.DrawTexture(new Rect(circleRect.x + 3, circleRect.y + 3, circleRect.width, circleRect.height), shapeTexture);
            
            // Main node shape
            GUI.color = isHovered ? Color.Lerp(nodeColor, Color.white, 0.15f) : nodeColor;
            GUI.DrawTexture(circleRect, shapeTexture);
            
            // Border ring (draw slightly larger)
            GUI.color = borderColor;
            float borderThickness = isUnlocked ? 3f : 2f;
            Rect borderRect = new Rect(circleRect.x - borderThickness/2, circleRect.y - borderThickness/2, 
                circleRect.width + borderThickness, circleRect.height + borderThickness);
            
            // Draw border by drawing larger shape behind
            Color prevColor = GUI.color;
            GUI.color = borderColor;
            GUI.DrawTexture(borderRect, shapeTexture);
            GUI.color = isHovered ? Color.Lerp(nodeColor, Color.white, 0.15f) : nodeColor;
            GUI.DrawTexture(circleRect.ContractedBy(1), shapeTexture);
            
            // Active skill indicator - small sword/lightning icon hint
            if (isActiveSkill)
            {
                Color iconColor = isUnlocked ? new Color(1f, 0.7f, 0.3f, 1f) : new Color(1f, 0.7f, 0.3f, 0.5f);
                DrawLabel(new Rect(circleRect.x, circleRect.y + 4, circleRect.width, 16), "⚔", LabelStyleSmall, iconColor, TextAnchor.UpperCenter);
            }
            
            // Tier pips (arranged in arc at bottom of circle)
            if (maxTier > 1)
            {
                float pipSize = 8f;
                float totalWidth = maxTier * pipSize + (maxTier - 1) * 3f;
                float startX = circleRect.center.x - totalWidth / 2f;
                float pipY = circleRect.yMax - 18f;
                
                for (int i = 0; i < maxTier; i++)
                {
                    Rect pipRect = new Rect(startX + i * (pipSize + 3), pipY, pipSize, pipSize);
                    GUI.color = i < currentTier ? Gold : new Color(0.2f, 0.2f, 0.25f, 1f);
                    GUI.DrawTexture(pipRect, CircleTexture);
                    
                    // Pip border
                    GUI.color = i < currentTier ? GoldDark : TextMuted;
                    Rect pipBorderRect = new Rect(pipRect.x - 1, pipRect.y - 1, pipRect.width + 2, pipRect.height + 2);
                    GUI.DrawTexture(pipBorderRect, CircleTexture);
                    GUI.color = i < currentTier ? Gold : new Color(0.2f, 0.2f, 0.25f, 1f);
                    GUI.DrawTexture(pipRect, CircleTexture);
                }
            }
            
            // Skill name - below the circle
            Color nameColor = isUnlocked ? TextPrimary : (canUnlock ? TextSecondary : TextMuted);
            
            // Truncate long names
            string displayName = name;
            if (name.Length > 12)
            {
                displayName = name.Substring(0, 10) + "..";
            }
            
            Rect labelRect = new Rect(rect.x - 10, circleRect.yMax + 2, rect.width + 20, 20);
            DrawLabel(labelRect, displayName, LabelStyleSmall, nameColor, TextAnchor.UpperCenter);
            
            GUI.color = Color.white;
            
            // Clickable area is the full node area
            wasClicked = Widgets.ButtonInvisible(rect);
            return wasClicked;
        }

        /// <summary>
        /// Draw a simple border
        /// </summary>
        public static void DrawBorder(Rect rect, int thickness = 1)
        {
            // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), SolidTexture);
            // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), SolidTexture);
            // Left
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), SolidTexture);
            // Right
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), SolidTexture);
        }

        /// <summary>
        /// Draw connection line between skill nodes
        /// </summary>
        public static void DrawNodeConnection(Vector2 start, Vector2 end, bool isUnlocked)
        {
            GUI.color = isUnlocked ? Accent : TextMuted;
            
            // Simple straight line (we can enhance this later with curves)
            Widgets.DrawLine(start, end, isUnlocked ? Accent : new Color(0.3f, 0.3f, 0.35f), 2f);
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a stat row with label and value
        /// </summary>
        public static void DrawStatRow(Rect rect, string label, string value, Color valueColor)
        {
            DrawLabel(new Rect(rect.x, rect.y, rect.width * 0.6f, rect.height), label, LabelStyleSmall, TextSecondary, TextAnchor.MiddleLeft);
            DrawLabel(new Rect(rect.x + rect.width * 0.6f, rect.y, rect.width * 0.4f, rect.height), value, LabelStyleSmall, valueColor, TextAnchor.MiddleRight);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw a section header
        /// </summary>
        public static void DrawSectionHeader(Rect rect, string title)
        {
            // Line before
            GUI.color = TextMuted;
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height / 2, 20, 1), SolidTexture);
            
            // Title
            DrawLabel(new Rect(rect.x + 25, rect.y, rect.width - 50, rect.height), title.ToUpper(), LabelStyleMedium, Accent, TextAnchor.MiddleLeft);
            
            // Line after
            float textWidth = LabelStyleMedium.CalcSize(new GUIContent(title.ToUpper())).x;
            GUI.color = TextMuted;
            GUI.DrawTexture(new Rect(rect.x + 30 + textWidth, rect.y + rect.height / 2, rect.width - 35 - textWidth, 1), SolidTexture);
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Draw an icon placeholder (can be replaced with actual textures later)
        /// </summary>
        public static void DrawIconPlaceholder(Rect rect, Color color, string letter = "")
        {
            GUI.color = new Color(color.r, color.g, color.b, 0.2f);
            GUI.DrawTexture(rect, SolidTexture);
            
            GUI.color = color;
            DrawBorder(rect, 1);
            
            if (!string.IsNullOrEmpty(letter))
            {
                DrawLabel(rect, letter, LabelStyleMedium, color, TextAnchor.MiddleCenter);
            }
            
            GUI.color = Color.white;
        }
    }
}
