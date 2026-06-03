using System.Collections.Generic;
using System.Linq;
using IsekaiLeveling.Forge;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Window displaying all weapon type masteries for a pawn.
    /// Shows mastered weapon types with XP bars and tier info,
    /// plus unmastered weapon types at the bottom.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_Mastery : Window
    {
        private Pawn pawn;
        private Vector2 scrollPos = Vector2.zero;

        // Animation
        private float openTime = -1f;
        private const float APPEAR_DURATION = 0.25f;
        private Dictionary<string, float> hoverTimers = new Dictionary<string, float>();

        // Layout
        private const float WIN_WIDTH = 520f;
        private const float WIN_HEIGHT = 700f;
        private const float ROW_HEIGHT = 62f;
        private const float MARGIN = 10f;
        private const float HEADER_HEIGHT = 40f;
        private const float SECTION_HEADER = 28f;
        private const float ICON_SIZE = 40f;
        private const float BAR_HEIGHT = 12f;

        // Colors
        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.45f);
        private static readonly Color TextPrimary = new Color(0.92f, 0.88f, 0.82f);
        private static readonly Color TextSecondary = new Color(0.70f, 0.65f, 0.58f);
        private static readonly Color TextMuted = new Color(0.45f, 0.42f, 0.40f);
        private static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f);
        private static readonly Color BarFill = new Color(0.90f, 0.75f, 0.25f);
        private static readonly Color BarFillMax = new Color(0.95f, 0.55f, 0.20f);
        private static readonly Color PanelBg = new Color(0.12f, 0.12f, 0.14f, 0.95f);
        private static readonly Color RowHover = new Color(0.20f, 0.20f, 0.22f, 0.5f);
        private static readonly Color EquippedHighlight = new Color(0.85f, 0.72f, 0.45f, 0.15f);

        // Tier colors
        private static readonly Dictionary<MasteryTier, Color> TierColors = new Dictionary<MasteryTier, Color>
        {
            { MasteryTier.Novice,      new Color(0.55f, 0.55f, 0.55f) },
            { MasteryTier.Apprentice,  new Color(0.60f, 0.75f, 0.60f) },
            { MasteryTier.Skilled,     new Color(0.45f, 0.70f, 0.90f) },
            { MasteryTier.Adept,       new Color(0.65f, 0.50f, 0.90f) },
            { MasteryTier.Expert,      new Color(0.90f, 0.75f, 0.25f) },
            { MasteryTier.Master,      new Color(0.95f, 0.55f, 0.20f) },
            { MasteryTier.Grandmaster, new Color(0.95f, 0.30f, 0.30f) },
        };

        // Cached textures
        private static Texture2D barFillTex;
        private static Texture2D barBgTex;
        private static Texture2D barMaxTex;

        private static void EnsureTextures()
        {
            if (barFillTex == null) barFillTex = SolidColorMaterials.NewSolidColorTexture(BarFill);
            if (barBgTex == null) barBgTex = SolidColorMaterials.NewSolidColorTexture(BarBg);
            if (barMaxTex == null) barMaxTex = SolidColorMaterials.NewSolidColorTexture(BarFillMax);
        }

        public override Vector2 InitialSize => new Vector2(WIN_WIDTH, WIN_HEIGHT);

        protected override float Margin => IsekaiLevelingSettings.UseIsekaiUI ? 0f : 18f;

        public Window_Mastery(Pawn pawn)
        {
            this.pawn = pawn;
            forcePause = false;
            doCloseX = !IsekaiLevelingSettings.UseIsekaiUI;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
            if (IsekaiLevelingSettings.UseIsekaiUI)
            {
                doWindowBackground = false;
                drawShadow = false;
            }
            openTime = Time.realtimeSinceStartup;
        }

        private float GetHoverAmount(string id, bool isOver)
        {
            if (!hoverTimers.ContainsKey(id))
                hoverTimers[id] = 0f;
            float target = isOver ? 1f : 0f;
            hoverTimers[id] = Mathf.MoveTowards(hoverTimers[id], target, Time.deltaTime * 6f);
            return hoverTimers[id];
        }

        private static Rect ScaleRect(Rect r, float scale)
        {
            if (scale >= 1f) return r;
            Vector2 c = r.center;
            float w = r.width * scale;
            float h = r.height * scale;
            return new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
        }

        public override void DoWindowContents(Rect inRect)
        {
            EnsureTextures();
            bool useCustom = IsekaiLevelingSettings.UseIsekaiUI;

            // Appear animation
            float appearT = 1f;
            if (useCustom && openTime > 0f)
            {
                appearT = Mathf.Clamp01((Time.realtimeSinceStartup - openTime) / APPEAR_DURATION);
                appearT = 1f - (1f - appearT) * (1f - appearT); // ease-out quad
            }
            float fadeAlpha = appearT;

            if (pawn == null || pawn.Dead)
            {
                Widgets.Label(inRect, "No pawn selected.");
                return;
            }

            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null)
            {
                Widgets.Label(inRect, "No Isekai data found.");
                return;
            }

            // Custom background
            if (useCustom && MasteryTextures.WindowBg != null)
            {
                GUI.color = new Color(1f, 1f, 1f, fadeAlpha);
                GUI.DrawTexture(inRect, MasteryTextures.WindowBg, ScaleMode.StretchToFill);
            }

            // Custom close button
            if (useCustom)
            {
                float closeBtnSize = 20f;
                Rect closeRect = new Rect(inRect.xMax - closeBtnSize - 10f, inRect.y + 10f, closeBtnSize, closeBtnSize);
                float closeHover = GetHoverAmount("close", Mouse.IsOver(closeRect));
                GUI.color = Color.Lerp(new Color(0.6f, 0.5f, 0.45f, fadeAlpha), new Color(1f, 0.4f, 0.3f, fadeAlpha), closeHover);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(closeRect, "\u00d7");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(closeRect))
                    Close();
            }

            GUI.color = new Color(1f, 1f, 1f, fadeAlpha);

            float winPad = useCustom ? 24f : 0f;
            float headerTop = useCustom ? 60f : 0f;

            // Header
            Text.Font = GameFont.Medium;
            GUI.color = useCustom ? Gold : Color.white;
            Widgets.Label(new Rect(inRect.x + winPad, inRect.y + headerTop, inRect.width - winPad * 2f, HEADER_HEIGHT),
                $"Weapon Mastery ( {pawn.LabelShortCap} )");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Currently equipped weapon info
            float headerY = inRect.y + headerTop + HEADER_HEIGHT + 4f;
            string equippedDefName = null;
            if (pawn.equipment?.Primary != null)
            {
                var wpn = pawn.equipment.Primary;
                equippedDefName = wpn.def.defName;
                GUI.color = TextSecondary;
                Widgets.Label(new Rect(inRect.x + winPad, headerY, inRect.width - winPad * 2f, 22f),
                    $"Equipped: {wpn.LabelCap}");
                GUI.color = Color.white;
                headerY += 22f;
            }
            else
            {
                GUI.color = TextMuted;
                Widgets.Label(new Rect(inRect.x + winPad, headerY, inRect.width - winPad * 2f, 22f), "No weapon equipped");
                GUI.color = Color.white;
                headerY += 22f;
            }

            headerY += 4f;

            // Dev mode buttons
            if (Prefs.DevMode && DebugSettings.godMode)
            {
                headerY = DrawDevButtons(new Rect(inRect.x + winPad, inRect.y, inRect.width - winPad * 2f, inRect.height), headerY, comp, equippedDefName);
            }

            // Build weapon lists
            var tracker = comp.weaponMastery;
            var allWeaponDefs = GetAllWeaponDefs();
            var masteredWeapons = new List<(ThingDef def, int xp, MasteryTier tier)>();
            var unmasteredWeapons = new List<ThingDef>();

            foreach (var wpnDef in allWeaponDefs)
            {
                int xp = tracker.GetXP(wpnDef.defName);
                if (xp > 0)
                {
                    var tier = tracker.GetMasteryTier(wpnDef.defName);
                    masteredWeapons.Add((wpnDef, xp, tier));
                }
                else
                {
                    unmasteredWeapons.Add(wpnDef);
                }
            }

            // Sort mastered by XP descending
            masteredWeapons.Sort((a, b) => b.xp.CompareTo(a.xp));
            // Sort unmastered alphabetically
            unmasteredWeapons.Sort((a, b) => string.Compare(a.label, b.label, System.StringComparison.OrdinalIgnoreCase));

            // Calculate total height
            float totalHeight = 0f;
            if (masteredWeapons.Count > 0)
                totalHeight += SECTION_HEADER + masteredWeapons.Count * ROW_HEIGHT + 8f;
            if (unmasteredWeapons.Count > 0)
                totalHeight += SECTION_HEADER + unmasteredWeapons.Count * 30f + 8f;

            // Panel area
            float panelX = inRect.x + winPad;
            float panelW = inRect.width - winPad * 2f;
            float panelH = inRect.height - (headerY - inRect.y) - winPad;
            Rect panelRect = new Rect(panelX, headerY, panelW, panelH);

            // Panel scale animation
            float panelScale = useCustom ? Mathf.Lerp(0.85f, 1f, appearT) : 1f;
            Rect scaledPanel = ScaleRect(panelRect, panelScale);

            // Scroll area inside panel with padding
            float topPad = useCustom ? 8f : 0f;
            float botPad = useCustom ? 16f : 0f;
            Rect scrollRect = new Rect(scaledPanel.x + 4f, scaledPanel.y + topPad, scaledPanel.width - 8f, scaledPanel.height - topPad - botPad);
            Rect viewRect = new Rect(0f, 0f, scrollRect.width - 16f, totalHeight);

            Widgets.BeginScrollView(scrollRect, ref scrollPos, viewRect);
            float y = 0f;

            // ── Mastered weapons ──
            if (masteredWeapons.Count > 0)
            {
                DrawSectionHeader(viewRect.width, ref y, $"Mastered Weapons ({masteredWeapons.Count})");

                foreach (var (def, xp, tier) in masteredWeapons)
                {
                    Rect rowRect = new Rect(0f, y, viewRect.width, ROW_HEIGHT);
                    bool isEquipped = def.defName == equippedDefName;
                    DrawMasteredRow(rowRect, def, xp, tier, isEquipped, tracker, useCustom);
                    y += ROW_HEIGHT;
                }
                y += 8f;
            }

            // ── Unmastered weapons ──
            if (unmasteredWeapons.Count > 0)
            {
                DrawSectionHeader(viewRect.width, ref y, $"Unmastered Weapons ({unmasteredWeapons.Count})");

                foreach (var def in unmasteredWeapons)
                {
                    Rect rowRect = new Rect(0f, y, viewRect.width, 30f);
                    DrawUnmasteredRow(rowRect, def, useCustom);
                    y += 30f;
                }
            }

            Widgets.EndScrollView();
            GUI.color = Color.white;
        }

        private static readonly Color DevBtnBg = new Color(0.30f, 0.15f, 0.15f, 0.8f);
        private static readonly Color DevBtnBorder = new Color(0.80f, 0.30f, 0.30f, 0.6f);

        private float DrawDevButtons(Rect inRect, float y, IsekaiComponent comp, string equippedDefName)
        {
            var tracker = comp.weaponMastery;

            // Dev panel background
            float panelHeight = 56f;
            Rect panelRect = new Rect(inRect.x, y, inRect.width, panelHeight);
            Widgets.DrawBoxSolid(panelRect, DevBtnBg);
            GUI.color = DevBtnBorder;
            Widgets.DrawBox(panelRect);
            GUI.color = Color.white;

            float bx = inRect.x + 6f;
            float by = y + 4f;
            float btnH = 22f;

            // Header
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 0.4f, 0.4f);
            Widgets.Label(new Rect(bx, by, 120f, btnH), "DEV MODE");
            GUI.color = Color.white;
            bx += 70f;

            // Row 1: Equipped weapon XP buttons
            float btnW = 68f;
            float spacing = 4f;
            bool hasEquipped = equippedDefName != null;

            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "+100 XP", active: hasEquipped))
            {
                if (hasEquipped) tracker.AddMasteryXP(equippedDefName, 100);
            }
            bx += btnW + spacing;

            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "+1000 XP", active: hasEquipped))
            {
                if (hasEquipped) tracker.AddMasteryXP(equippedDefName, 1000);
            }
            bx += btnW + spacing;

            btnW = 80f;
            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "Max Equipped", active: hasEquipped))
            {
                if (hasEquipped) tracker.masteryXP[equippedDefName] = 6000;
            }
            bx += btnW + spacing;

            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "Reset Equipped", active: hasEquipped))
            {
                if (hasEquipped) tracker.masteryXP.Remove(equippedDefName);
            }

            // Row 2: All weapons
            bx = inRect.x + 76f;
            by += btnH + 4f;
            btnW = 68f;

            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "Max All"))
            {
                foreach (var def in GetAllWeaponDefs())
                    tracker.masteryXP[def.defName] = 6000;
            }
            bx += btnW + spacing;

            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "Reset All"))
            {
                tracker.masteryXP.Clear();
            }

            bx += btnW + spacing;
            btnW = 80f;
            if (Widgets.ButtonText(new Rect(bx, by, btnW, btnH), "+1 Level", active: hasEquipped))
            {
                if (hasEquipped)
                {
                    var currentTier = tracker.GetMasteryTier(equippedDefName);
                    int nextIdx = (int)currentTier + 1;
                    if (nextIdx < 7)
                    {
                        int[] thresholds = { 0, 100, 300, 700, 1500, 3000, 6000 };
                        tracker.masteryXP[equippedDefName] = thresholds[nextIdx];
                    }
                }
            }

            Text.Font = GameFont.Small;
            return y + panelHeight + 4f;
        }

        private void DrawSectionHeader(float width, ref float y, string label)
        {
            GUI.color = Gold;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(4f, y, width - 8f, SECTION_HEADER), label);
            // Separator line
            GUI.color = new Color(Gold.r, Gold.g, Gold.b, 0.3f);
            Widgets.DrawLineHorizontal(4f, y + SECTION_HEADER - 2f, width - 8f);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            y += SECTION_HEADER;
        }

        private void DrawMasteredRow(Rect rect, ThingDef def, int xp, MasteryTier tier, bool isEquipped, WeaponMasteryTracker tracker, bool useCustom)
        {
            // Row background
            float rowHover = GetHoverAmount($"mrow_{def.defName}", Mouse.IsOver(rect));
            if (useCustom && MasteryTextures.RowMastered != null)
            {
                float brightness = 1f + 0.15f * rowHover;
                GUI.color = isEquipped
                    ? new Color(1f, 0.92f, 0.75f, brightness)
                    : new Color(brightness, brightness, brightness, 1f);
                GUI.DrawTexture(rect, MasteryTextures.RowMastered, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }
            else
            {
                if (isEquipped)
                    Widgets.DrawBoxSolid(rect, EquippedHighlight);
                if (rowHover > 0f)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.08f * rowHover);
                    Widgets.DrawHighlight(rect);
                    GUI.color = Color.white;
                }
            }

            // Tooltip on hover
            if (Mouse.IsOver(rect))
                DrawMasteryTooltip(rect, def, xp, tier, tracker);

            float x = rect.x + 4f;
            float centerY = rect.y + (rect.height - ICON_SIZE) / 2f;

            // Weapon icon
            Rect iconRect = new Rect(x, centerY, ICON_SIZE, ICON_SIZE);
            Widgets.ThingIcon(iconRect, def);
            x += ICON_SIZE + 8f;

            float infoWidth = rect.width - x - 8f;
            float textY = rect.y + 4f;

            // Weapon label + equipped tag
            Text.Font = GameFont.Small;
            GUI.color = TextPrimary;
            string label = def.label?.CapitalizeFirst() ?? def.defName;
            if (isEquipped)
            {
                label += " [Equipped]";
                GUI.color = Gold;
            }
            Widgets.Label(new Rect(x, textY, infoWidth, 20f), label);
            textY += 18f;

            // Tier and XP
            Color tierColor = TierColors.TryGetValue(tier, out Color tc) ? tc : TextSecondary;
            string tierLabel = WeaponMasteryTracker.GetTierLabel(tier);
            int nextThreshold = tracker.GetXPForNextTier(def.defName);
            string xpText;
            if (nextThreshold < 0)
                xpText = $"{tierLabel} — {xp} XP (MAX)";
            else
                xpText = $"{tierLabel} — {xp} / {nextThreshold} XP";

            Text.Font = GameFont.Tiny;
            GUI.color = tierColor;
            Widgets.Label(new Rect(x, textY, infoWidth, 16f), xpText);
            textY += 16f;

            // XP progress bar
            float barY = textY + 2f;
            float barWidth = infoWidth;
            Rect barBgRect = new Rect(x, barY, barWidth, BAR_HEIGHT);
            GUI.DrawTexture(barBgRect, barBgTex);

            float fillFrac;
            bool isMax = tier == MasteryTier.Grandmaster;
            if (isMax)
            {
                fillFrac = 1f;
            }
            else
            {
                int currentThreshold = GetCurrentTierThreshold(tier);
                int range = nextThreshold - currentThreshold;
                fillFrac = range > 0 ? (float)(xp - currentThreshold) / range : 0f;
            }
            fillFrac = Mathf.Clamp01(fillFrac);

            if (fillFrac > 0f)
            {
                Rect fillRect = new Rect(barBgRect.x, barBgRect.y, barBgRect.width * fillFrac, BAR_HEIGHT);
                GUI.DrawTexture(fillRect, isMax ? barMaxTex : barFillTex);
            }

            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private void DrawUnmasteredRow(Rect rect, ThingDef def, bool useCustom)
        {
            float rowHover = GetHoverAmount($"urow_{def.defName}", Mouse.IsOver(rect));
            if (useCustom && MasteryTextures.RowUnmastered != null)
            {
                float brightness = 1f + 0.15f * rowHover;
                GUI.color = new Color(brightness, brightness, brightness, 1f);
                GUI.DrawTexture(rect, MasteryTextures.RowUnmastered, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }
            else
            {
                if (rowHover > 0f)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.08f * rowHover);
                    Widgets.DrawHighlight(rect);
                    GUI.color = Color.white;
                }
            }

            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, $"No mastery yet. Equip and fight to gain XP.\n\nType: {(def.IsRangedWeapon ? "Ranged" : "Melee")}");

            float x = rect.x + 4f;
            float centerY = rect.y + (rect.height - 24f) / 2f;

            // Small icon
            Rect iconRect = new Rect(x, centerY, 24f, 24f);
            Widgets.ThingIcon(iconRect, def);
            x += 32f;

            // Label
            Text.Font = GameFont.Small;
            GUI.color = TextMuted;
            string label = def.label?.CapitalizeFirst() ?? def.defName;
            Widgets.Label(new Rect(x, rect.y + 5f, rect.width - x - 8f, 20f), label);
            GUI.color = Color.white;
        }

        private void DrawMasteryTooltip(Rect rect, ThingDef def, int xp, MasteryTier tier, WeaponMasteryTracker tracker)
        {
            tracker.GetMasteryBonuses(def.defName, out float hitChance, out float attackSpeed, out float damage);

            string weaponType = def.IsRangedWeapon ? "Ranged" : "Melee";
            string tip = $"{def.label?.CapitalizeFirst()}\n" +
                         $"Type: {weaponType}\n" +
                         $"Tier: {WeaponMasteryTracker.GetTierLabel(tier)}\n" +
                         $"XP: {xp}\n\n" +
                         $"Current Bonuses:\n";

            if (hitChance > 0f)
                tip += $"  Hit Chance: +{hitChance * 100f:F0}%\n";
            if (attackSpeed > 0f)
                tip += $"  Attack Speed: +{attackSpeed * 100f:F0}%\n";
            if (damage > 0f)
                tip += $"  Damage: +{damage * 100f:F0}%\n";

            if (hitChance <= 0f && attackSpeed <= 0f && damage <= 0f)
                tip += "  None yet (reach Apprentice tier)\n";

            int next = tracker.GetXPForNextTier(def.defName);
            if (next >= 0)
                tip += $"\nNext tier at: {next} XP ({next - xp} XP needed)";
            else
                tip += "\nMaximum tier reached!";

            TooltipHandler.TipRegion(rect, tip);
        }

        private static int GetCurrentTierThreshold(MasteryTier tier)
        {
            int[] thresholds = { 0, 100, 300, 700, 1500, 3000, 6000 };
            int index = (int)tier;
            return index >= 0 && index < thresholds.Length ? thresholds[index] : 0;
        }

        /// <summary>
        /// Get all weapon ThingDefs that exist in the game (non-abstract, craftable/spawnable weapons).
        /// </summary>
        private List<ThingDef> GetAllWeaponDefs()
        {
            return DefDatabase<ThingDef>.AllDefsListForReading
                .Where(d => !d.IsBlueprint && !d.IsFrame &&
                            (d.IsMeleeWeapon || d.IsRangedWeapon) &&
                            d.category == ThingCategory.Item &&
                            !d.destroyOnDrop)
                .OrderBy(d => d.IsRangedWeapon ? 1 : 0)
                .ThenBy(d => d.label ?? d.defName)
                .ToList();
        }
    }
}
