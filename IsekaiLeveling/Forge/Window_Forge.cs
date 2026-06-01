using System.Collections.Generic;
using System.Linq;
using IsekaiLeveling.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Forge refinement window. Opened via gizmo on the Isekai Forge building.
    /// Left panel: scrollable equipment list. Center: item details. Right: refine action.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Window_Forge : Window
    {
        private Map map;
        private Thing selectedItem;
        private Vector2 equipScrollPos = Vector2.zero;
        private string searchText = "";

        // Animation
        private float flashTimer = 0f;
        private Color flashColor = Color.clear;
        private string lastResultText = null;
        private float openTime = -1f;
        private const float APPEAR_DURATION = 0.25f;

        // Hover tracking
        private Dictionary<string, float> hoverTimers = new Dictionary<string, float>();

        // Layout (sized to match textures)
        private const float WIN_WIDTH = 765f;
        private const float WIN_HEIGHT = 500f;
        private const float LIST_WIDTH = 224f;
        private const float DETAIL_WIDTH = 265f;
        private const float ACTION_WIDTH = 224f;
        private const float ROW_HEIGHT = 44f;
        private const float MARGIN = 8f;

        // Success bar textures (procedural)
        private static Texture2D _successBarBgTex;
        private static Texture2D _successBarFillTex;
        private static Texture2D _downgradeBarFillTex;
        private static Texture2D _destroyBarFillTex;

        private static void EnsureBarTextures()
        {
            if (_successBarBgTex == null)
                _successBarBgTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.12f, 0.12f, 0.14f));
            if (_successBarFillTex == null)
                _successBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.30f, 0.85f, 0.35f));
            if (_downgradeBarFillTex == null)
                _downgradeBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.65f, 0.60f, 0.45f));
            if (_destroyBarFillTex == null)
                _destroyBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.85f, 0.25f, 0.25f));
        }

        // Colors
        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.45f);
        private static readonly Color TextPrimary = new Color(0.92f, 0.88f, 0.82f);
        private static readonly Color TextSecondary = new Color(0.70f, 0.65f, 0.58f);
        private static readonly Color SuccessGreen = new Color(0.3f, 0.9f, 0.3f);
        private static readonly Color FailRed = new Color(0.9f, 0.3f, 0.3f);
        private static readonly Color DestroyOrange = new Color(0.95f, 0.5f, 0.1f);

        public override Vector2 InitialSize => new Vector2(WIN_WIDTH, WIN_HEIGHT);

        protected override float Margin => IsekaiLevelingSettings.UseIsekaiUI ? 0f : 18f;

        public Window_Forge(Map map)
        {
            this.map = map;
            forcePause = false;
            doCloseX = !IsekaiLevelingSettings.UseIsekaiUI;
            absorbInputAroundWindow = false;
            closeOnClickedOutside = false;
            draggable = true;
            if (IsekaiLevelingSettings.UseIsekaiUI)
                doWindowBackground = false;
            openTime = Time.realtimeSinceStartup;
        }

        public override void DoWindowContents(Rect inRect)
        {
            EnsureBarTextures();
            bool useCustom = IsekaiLevelingSettings.UseIsekaiUI;

            // Appear animation
            float appearT = 1f;
            if (useCustom && openTime > 0f)
            {
                appearT = Mathf.Clamp01((Time.realtimeSinceStartup - openTime) / APPEAR_DURATION);
                appearT = 1f - (1f - appearT) * (1f - appearT); // ease-out quad
            }
            float fadeAlpha = appearT;

            if (!IsekaiLevelingSettings.EnableForgeSystem)
            {
                Widgets.Label(inRect, "Isekai_Forge_Disabled".Translate());
                return;
            }

            // Custom background
            if (useCustom && ForgeTextures.WindowBg != null)
            {
                GUI.color = new Color(1f, 1f, 1f, fadeAlpha);
                GUI.DrawTexture(inRect, ForgeTextures.WindowBg, ScaleMode.StretchToFill);
            }

            // Custom close button (top-right)
            if (useCustom)
            {
                float closeBtnSize = 20f;
                Rect closeRect = new Rect(inRect.xMax - closeBtnSize - 10f, inRect.y + 10f, closeBtnSize, closeBtnSize);
                float closeHover = GetHoverAmount("close", Mouse.IsOver(closeRect));
                GUI.color = Color.Lerp(new Color(0.6f, 0.5f, 0.45f, fadeAlpha), new Color(1f, 0.4f, 0.3f, fadeAlpha), closeHover);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(closeRect, "×");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(closeRect))
                    Close();
            }

            GUI.color = new Color(1f, 1f, 1f, fadeAlpha);

            // Flash animation
            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                float alpha = Mathf.Clamp01(flashTimer / 0.5f) * 0.3f;
                GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
                Widgets.DrawBoxSolid(inRect, GUI.color);
                GUI.color = Color.white;
            }

            Text.Font = GameFont.Medium;
            GUI.color = useCustom ? Gold : Color.white;
            float winPad = useCustom ? 16f : 0f;
            Widgets.Label(new Rect(inRect.x + winPad, inRect.y + winPad, inRect.width, 30f), "Isekai_Forge_Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float topOffset = inRect.y + 35f + winPad;
            Rect contentRect = new Rect(inRect.x + winPad, topOffset, inRect.width - winPad * 2f, inRect.height - (topOffset - inRect.y) - winPad);

            // Panel scale: panels grow from 0.85 to 1.0 during appear
            float panelScale = useCustom ? Mathf.Lerp(0.85f, 1f, appearT) : 1f;

            // Three panels
            Rect listRect = new Rect(contentRect.x, contentRect.y, LIST_WIDTH, contentRect.height);
            Rect detailRect = new Rect(contentRect.x + LIST_WIDTH + MARGIN, contentRect.y, DETAIL_WIDTH, contentRect.height);
            Rect actionRect = new Rect(contentRect.x + LIST_WIDTH + DETAIL_WIDTH + MARGIN * 2, contentRect.y, ACTION_WIDTH, contentRect.height);

            DrawEquipmentList(ScaleRect(listRect, panelScale), useCustom);
            DrawItemDetails(ScaleRect(detailRect, panelScale), useCustom);
            DrawRefineAction(ScaleRect(actionRect, panelScale), useCustom);

            GUI.color = Color.white;
        }

        /// <summary>Scale a rect from its center by the given factor.</summary>
        private static Rect ScaleRect(Rect r, float scale)
        {
            if (scale >= 1f) return r;
            Vector2 c = r.center;
            float w = r.width * scale;
            float h = r.height * scale;
            return new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
        }

        private float GetHoverAmount(string id, bool isOver)
        {
            if (!hoverTimers.ContainsKey(id))
                hoverTimers[id] = 0f;
            float target = isOver ? 1f : 0f;
            hoverTimers[id] = Mathf.MoveTowards(hoverTimers[id], target, Time.deltaTime * 6f);
            return hoverTimers[id];
        }

        private void DrawEquipmentList(Rect rect, bool useCustom)
        {
            if (useCustom && ForgeTextures.PanelSection != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, ForgeTextures.PanelSection, ScaleMode.StretchToFill);
            }
            else
            {
                Widgets.DrawMenuSection(rect);
            }

            // Pad the scroll area so items aren't hidden behind panel decoration
            float topPad = useCustom ? 32f : 2f;
            float botPad = useCustom ? 32f : 2f;

            // Search bar
            float searchHeight = 24f;
            float searchPad = 2f;
            Rect searchRect = new Rect(rect.x + 4f, rect.y + topPad, rect.width - 8f, searchHeight);
            searchText = Widgets.TextField(searchRect, searchText);
            if (string.IsNullOrEmpty(searchText))
            {
                GUI.color = TextSecondary;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(searchRect.x + 4f, searchRect.y, searchRect.width - 8f, searchRect.height), "Isekai_UI_Search".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            Rect scrollOuter = new Rect(rect.x + 2f, rect.y + topPad + searchHeight + searchPad, rect.width - 4f, rect.height - topPad - botPad - searchHeight - searchPad);

            var items = GetAllEquipment();
            if (!string.IsNullOrEmpty(searchText))
            {
                string filter = searchText.ToLowerInvariant();
                items = items.Where(t => t.LabelCapNoCount.ToLower().Contains(filter)
                    || (t.ParentHolder is Pawn_EquipmentTracker eq2 && eq2.pawn?.LabelShortCap?.ToLower().Contains(filter) == true)
                    || (t.ParentHolder is Pawn_ApparelTracker ap2 && ap2.pawn?.LabelShortCap?.ToLower().Contains(filter) == true)).ToList();
            }
            float viewHeight = items.Count * ROW_HEIGHT;
            Rect viewRect = new Rect(0f, 0f, scrollOuter.width - 16f, viewHeight);

            Widgets.BeginScrollView(scrollOuter, ref equipScrollPos, viewRect);

            float y = 0f;
            foreach (var item in items)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, ROW_HEIGHT);

                // Highlight selected
                if (selectedItem == item)
                    Widgets.DrawHighlightSelected(rowRect);
                else
                {
                    float rowHover = GetHoverAmount($"row_{item.ThingID}", Mouse.IsOver(rowRect));
                    if (rowHover > 0f)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.08f * rowHover);
                        Widgets.DrawHighlight(rowRect);
                        GUI.color = Color.white;
                    }
                }

                // Item icon
                Rect iconRect = new Rect(4f, y + 4f, 36f, 36f);
                Widgets.ThingIcon(iconRect, item);

                float textX = 44f;
                float textW = viewRect.width - textX - 4f;

                // Label with refinement suffix
                var forge = item.TryGetComp<CompForgeEnhancement>();
                string label = item.LabelCapNoCount;
                if (label.Length > 24)
                    label = label.Substring(0, 21) + "...";

                // Determine owner
                string owner = null;
                if (item.ParentHolder is Pawn_EquipmentTracker eq)
                    owner = eq.pawn?.LabelShortCap;
                else if (item.ParentHolder is Pawn_ApparelTracker ap)
                    owner = ap.pawn?.LabelShortCap;

                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(textX, y + 2f, textW, 22f), label);

                if (owner != null)
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = TextSecondary;
                    Widgets.Label(new Rect(textX, y + 22f, textW, 18f), owner);
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = TextSecondary;
                    Widgets.Label(new Rect(textX, y + 22f, textW, 18f), "Isekai_UI_InStorage".Translate());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    selectedItem = item;
                    lastResultText = null;
                }

                y += ROW_HEIGHT;
            }

            Widgets.EndScrollView();
        }

        private void DrawItemDetails(Rect rect, bool useCustom)
        {
            if (useCustom && ForgeTextures.PanelSectionCenter != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, ForgeTextures.PanelSectionCenter, ScaleMode.StretchToFill);
            }
            else
            {
                Widgets.DrawMenuSection(rect);
            }
            Rect inner = rect.ContractedBy(8f);

            if (selectedItem == null)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = TextSecondary;
                Widgets.Label(inner, "Isekai_Forge_SelectFromList".Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            var comp = selectedItem.TryGetComp<CompForgeEnhancement>();
            if (comp == null)
            {
                Widgets.Label(inner, "Isekai_Forge_CannotEnhance".Translate());
                return;
            }

            float y = inner.y;

            // Item name
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), selectedItem.LabelCapNoCount);
            Text.Font = GameFont.Small;
            y += 30f;

            // Quality
            QualityCategory quality;
            if (selectedItem.TryGetQuality(out quality))
            {
                GUI.color = TextSecondary;
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Forge_Quality".Translate(quality.GetLabel().CapitalizeFirst()));
                GUI.color = Color.white;
                y += 22f;
            }

            // Refinement level
            string refText = comp.refinementLevel > 0 
                ? (string)"Isekai_Forge_RefinementLevel".Translate(comp.refinementLevel.ToString())
                : (string)"Isekai_Forge_RefinementNone".Translate();
            GUI.color = comp.refinementLevel > 0 ? GetRefinementColor(comp.refinementLevel) : TextSecondary;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), refText);
            GUI.color = Color.white;
            y += 24f;

            // Stat bonuses
            if (comp.refinementLevel > 0)
            {
                GUI.color = SuccessGreen;
                if (selectedItem.def.IsMeleeWeapon)
                {
                    float dmg = ForgeUtility.GetMeleeDamageBonus(comp.refinementLevel) * 100f;
                    float spd = ForgeUtility.GetMeleeSpeedBonus(comp.refinementLevel) * 100f;
                    float mass = ForgeUtility.GetWeaponMassReduction(comp.refinementLevel) * 100f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_MeleeDmgBonus".Translate(dmg.ToString("F0")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_MeleeCdBonus".Translate(spd.ToString("F0")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_WeaponMassBonus".Translate(mass.ToString("F1")));
                    y += 20f;
                }
                else if (selectedItem.def.IsRangedWeapon)
                {
                    float dmg = ForgeUtility.GetRangedDamageBonus(comp.refinementLevel) * 100f;
                    float cd = ForgeUtility.GetRangedCooldownReduction(comp.refinementLevel) * 100f;
                    float acc = ForgeUtility.GetRangedAccuracyBonus(comp.refinementLevel) * 100f;
                    float mass = ForgeUtility.GetWeaponMassReduction(comp.refinementLevel) * 100f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_RangedDmgBonus".Translate(dmg.ToString("F0")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_CooldownBonus".Translate(cd.ToString("F0")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_AccuracyBonus".Translate(acc.ToString("F0")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_WeaponMassBonus2".Translate(mass.ToString("F1")));
                    y += 20f;
                }
                else if (selectedItem.def.IsApparel)
                {
                    float armor = ForgeUtility.GetArmorBonus(comp.refinementLevel) * 100f;
                    float spd = ForgeUtility.GetArmorMoveSpeedBonus(comp.refinementLevel) * 100f;
                    float mass = ForgeUtility.GetArmorMassReduction(comp.refinementLevel) * 100f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_ArmorRatingBonus".Translate(armor.ToString("F0")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_MoveSpeedBonus".Translate(spd.ToString("F1")));
                    y += 18f;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_ArmorMassBonus".Translate(mass.ToString("F0")));
                    y += 20f;
                }
                GUI.color = Color.white;
            }

            // Rune slots
            y += 6f;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Forge_RuneSlotsInfo".Translate(comp.UsedRuneSlots.ToString(), comp.MaxRuneSlots.ToString()));
            y += 22f;

            // Applied runes
            if (comp.UsedRuneSlots > 0)
            {
                foreach (var rune in comp.GetAppliedRunes())
                {
                    GUI.color = rune.runeColor;
                    Widgets.Label(new Rect(inner.x + 10f, y, inner.width - 10f, 20f), 
                        $"• {rune.LabelCap}: {rune.statDescription ?? rune.description}");
                    GUI.color = Color.white;
                    y += 20f;
                }
            }

            // Result text
            if (lastResultText != null)
            {
                y += 10f;
                GUI.color = flashColor;
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(inner.x, y, inner.width, 30f), lastResultText);
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        private void DrawRefineAction(Rect rect, bool useCustom)
        {
            if (useCustom && ForgeTextures.PanelSection != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, ForgeTextures.PanelSection, ScaleMode.StretchToFill);
            }
            else
            {
                Widgets.DrawMenuSection(rect);
            }
            float topPad = useCustom ? 32f : 8f;
            float botPad = useCustom ? 32f : 8f;
            Rect inner = new Rect(rect.x + 8f, rect.y + topPad, rect.width - 16f, rect.height - topPad - botPad);

            if (selectedItem == null) return;

            var comp = selectedItem.TryGetComp<CompForgeEnhancement>();
            if (comp == null) return;

            float y = inner.y;

            if (!comp.CanRefine())
            {
                GUI.color = Gold;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(inner.x, y, inner.width, 40f), "Isekai_Forge_MaxRefinement".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += 44f;
                DrawRepairSection(inner, y, useCustom);
                return;
            }

            int targetLevel = comp.refinementLevel + 1;
            ForgeUtility.RefineCost cost = ForgeUtility.GetRefineCost(targetLevel);

            // Target level header
            Text.Font = GameFont.Medium;
            GUI.color = GetRefinementColor(targetLevel);
            Widgets.Label(new Rect(inner.x, y, inner.width, 26f), "Isekai_Forge_RefineTo".Translate(targetLevel.ToString()));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 30f;

            // Materials needed
            Widgets.Label(new Rect(inner.x, y, inner.width, 20f), "Isekai_Forge_MaterialsLabel".Translate());
            y += 20f;

            y = DrawCostLine(inner.x, y, inner.width, cost.coreDef, cost.coreCount);
            if (cost.secondaryCoreDef != null && cost.secondaryCoreCount > 0)
                y = DrawCostLine(inner.x, y, inner.width, cost.secondaryCoreDef, cost.secondaryCoreCount);
            if (cost.steel > 0)
                y = DrawCostLine(inner.x, y, inner.width, ThingDefOf.Steel, cost.steel);
            if (cost.components > 0)
                y = DrawCostLine(inner.x, y, inner.width, ThingDefOf.ComponentIndustrial, cost.components);

            y += 6f;

            // Success / failure rates
            float success = ForgeUtility.GetSuccessChance(targetLevel, null) * 100f;
            float destroy = ForgeUtility.GetDestroyChance(targetLevel) * 100f;
            float downgrade = Mathf.Max(0f, 100f - success - destroy);

            // Success bar
            float barH = 16f;
            float barW = inner.width;
            Rect barRect = new Rect(inner.x, y, barW, barH);

            // Background
            GUI.DrawTexture(barRect, _successBarBgTex);

            // Stacked fill: success (green) | downgrade (yellow) | destroy (red)
            float successW = barW * (success / 100f);
            float downgradeW = barW * (downgrade / 100f);
            float destroyW = barW * (destroy / 100f);

            float fx = barRect.x;
            if (successW > 0f)
            {
                GUI.DrawTexture(new Rect(fx, barRect.y, successW, barH), _successBarFillTex);
                fx += successW;
            }
            if (downgradeW > 0f)
            {
                GUI.DrawTexture(new Rect(fx, barRect.y, downgradeW, barH), _downgradeBarFillTex);
                fx += downgradeW;
            }
            if (destroyW > 0f)
            {
                GUI.DrawTexture(new Rect(fx, barRect.y, destroyW, barH), _destroyBarFillTex);
            }

            // Border
            GUI.color = new Color(0.3f, 0.3f, 0.35f, 0.8f);
            Widgets.DrawBox(barRect);
            GUI.color = Color.white;
            y += barH + 4f;

            // Labels
            GUI.color = SuccessGreen;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Forge_SuccessLabel".Translate(success.ToString("F0")));
            y += 20f;

            if (downgrade > 0f)
            {
                GUI.color = TextSecondary;
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Forge_DowngradeLabel".Translate(downgrade.ToString("F0")));
                y += 20f;
            }

            if (destroy > 0f)
            {
                GUI.color = FailRed;
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Forge_DestroyLabel".Translate(destroy.ToString("F0")));
                y += 20f;
            }

            GUI.color = Color.white;
            y += 6f;

            // Refine button
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            bool hasMats = godMode || ForgeUtility.HasMaterials(map, cost);

            if (!hasMats)
            {
                GUI.color = TextSecondary;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(inner.x, y, inner.width, 35f), "Isekai_Forge_MissingMaterials".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                y += 38f;
            }
            else
            {
                float btnW = useCustom ? 161f : inner.width;
                float btnX = inner.x + (inner.width - btnW) / 2f;
                Rect buttonRect = new Rect(btnX, y, btnW, 36f);

                bool clicked;
                if (useCustom && ForgeTextures.ButtonRefine != null)
                {
                    bool hover = Mouse.IsOver(buttonRect);

                    // Scale up on hover for juicy feedback
                    Rect drawRect = buttonRect;
                    if (hover)
                    {
                        float sc = 1.06f;
                        Vector2 c = buttonRect.center;
                        drawRect = new Rect(
                            c.x - buttonRect.width * sc / 2f,
                            c.y - buttonRect.height * sc / 2f,
                            buttonRect.width * sc,
                            buttonRect.height * sc);
                    }

                    float brightness = hover ? 1.25f : 1f;
                    GUI.color = new Color(brightness, brightness, brightness, 1f);
                    GUI.DrawTexture(drawRect, ForgeTextures.ButtonRefine, ScaleMode.StretchToFill);
                    GUI.color = Color.white;

                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = hover ? Gold : TextPrimary;
                    Widgets.Label(drawRect, "Isekai_Forge_RefineTo".Translate(targetLevel.ToString()));
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;

                    clicked = Widgets.ButtonInvisible(buttonRect);
                }
                else
                {
                    clicked = Widgets.ButtonText(buttonRect, "Isekai_Forge_RefineTo".Translate(targetLevel.ToString()));
                }

                if (clicked)
                {
                    // Confirmation dialog for dangerous refinements (+6 and above)
                    if (targetLevel >= 6)
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "Isekai_Forge_ConfirmDestroy".Translate(targetLevel.ToString(), destroy.ToString("F0")),
                            () => DoRefinement(comp, targetLevel)));
                    }
                    else
                    {
                        DoRefinement(comp, targetLevel);
                    }
                }
                y += 40f;
            }

            // ── Repair section (only when item is damaged) ──
            DrawRepairSection(inner, y, useCustom);
        }

        private void DrawRepairSection(Rect inner, float y, bool useCustom)
        {
            if (!ForgeUtility.NeedsRepair(selectedItem)) return;

            bool godMode = Prefs.DevMode && DebugSettings.godMode;

            y += 4f;
            // Separator line
            GUI.color = new Color(0.4f, 0.35f, 0.3f, 0.6f);
            Widgets.DrawLineHorizontal(inner.x, y, inner.width);
            GUI.color = Color.white;
            y += 6f;

            int repairCost = ForgeUtility.GetRepairEssenceCost(selectedItem);
            float hpPercent = (float)selectedItem.HitPoints / selectedItem.MaxHitPoints * 100f;
            ThingDef essenceDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ManaEssence");

            // Durability display
            GUI.color = Color.Lerp(FailRed, TextSecondary, hpPercent / 100f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 20f),
                "Isekai_Forge_Durability".Translate(selectedItem.HitPoints.ToString(), selectedItem.MaxHitPoints.ToString(), hpPercent.ToString("F0")));
            GUI.color = Color.white;
            y += 22f;

            // Cost line
            if (essenceDef != null)
            {
                y = DrawCostLine(inner.x, y, inner.width, essenceDef, repairCost);
            }
            y += 4f;

            // Repair button
            bool canAfford = godMode || (essenceDef != null && ForgeUtility.CountOnMap(map, essenceDef) >= repairCost);
            if (!canAfford)
            {
                GUI.color = TextSecondary;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(inner.x, y, inner.width, 28f), "Isekai_Forge_NeedEssence".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }
            else
            {
                float repBtnW = useCustom ? 161f : inner.width;
                float repBtnX = inner.x + (inner.width - repBtnW) / 2f;
                Rect repairBtnRect = new Rect(repBtnX, y, repBtnW, 30f);

                bool repairClicked;
                if (useCustom && ForgeTextures.ButtonRefine != null)
                {
                    bool hover = Mouse.IsOver(repairBtnRect);
                    float brightness = hover ? 1.25f : 1f;
                    GUI.color = new Color(brightness, brightness, brightness, 1f);
                    GUI.DrawTexture(repairBtnRect, ForgeTextures.ButtonRefine, ScaleMode.StretchToFill);
                    GUI.color = Color.white;

                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = hover ? Gold : TextPrimary;
                    Widgets.Label(repairBtnRect, "Isekai_Forge_Repair".Translate());
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;

                    repairClicked = Widgets.ButtonInvisible(repairBtnRect);
                }
                else
                {
                    repairClicked = Widgets.ButtonText(repairBtnRect, "Isekai_Forge_Repair".Translate());
                }

                if (repairClicked)
                {
                    if (ForgeUtility.RepairItem(selectedItem, map))
                    {
                        flashColor = SuccessGreen;
                        flashTimer = 0.4f;
                        lastResultText = "Isekai_Forge_Repaired".Translate();
                        SoundDefOf.Quest_Concluded.PlayOneShotOnCamera();
                    }
                }
            }
        }

        private void DoRefinement(CompForgeEnhancement comp, int targetLevel)
        {
            var result = ForgeUtility.AttemptRefinement(selectedItem, map, null);

            switch (result)
            {
                case ForgeUtility.RefineResult.Success:
                    flashColor = SuccessGreen;
                    flashTimer = 0.6f;
                    lastResultText = "Isekai_Forge_ResultSuccess".Translate(comp.refinementLevel.ToString());
                    SoundDefOf.Quest_Concluded.PlayOneShotOnCamera();
                    break;

                case ForgeUtility.RefineResult.Downgrade:
                    flashColor = DestroyOrange;
                    flashTimer = 0.5f;
                    lastResultText = "Isekai_Forge_ResultDowngrade".Translate(comp.refinementLevel.ToString());
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;

                case ForgeUtility.RefineResult.Destroyed:
                    flashColor = FailRed;
                    flashTimer = 0.8f;
                    lastResultText = "Isekai_Forge_ResultDestroyed".Translate();
                    SoundDefOf.Crunch.PlayOneShotOnCamera();

                    // Destroy the item
                    if (selectedItem != null && !selectedItem.Destroyed)
                    {
                        selectedItem.Destroy();
                    }
                    selectedItem = null;
                    break;
            }
        }

        private float DrawCostLine(float x, float y, float width, ThingDef def, int count)
        {
            if (def == null) return y;
            int available = ForgeUtility.CountOnMap(map, def);
            bool enough = available >= count;

            // Material icon
            Rect iconRect = new Rect(x + 4f, y + 1f, 20f, 20f);
            GUI.color = Color.white;
            Widgets.ThingIcon(iconRect, def);

            GUI.color = enough ? TextPrimary : FailRed;
            Widgets.Label(new Rect(x + 28f, y, width - 28f, 22f), 
                $"{def.LabelCap} ×{count} ({available})");
            GUI.color = Color.white;
            return y + 22f;
        }

        private List<Thing> GetAllEquipment()
        {
            var result = new List<Thing>();
            if (map == null) return result;

            // Equipment from stockpiles
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.Destroyed || thing.def == null) continue;
                if (!thing.def.IsWeapon && !thing.def.IsApparel) continue;
                if (thing.def.category != ThingCategory.Item) continue;
                if (thing.TryGetComp<CompForgeEnhancement>() == null) continue;

                // Skip items held by pawns (we'll add equipped items separately)
                if (thing.ParentHolder is Pawn_EquipmentTracker ||
                    thing.ParentHolder is Pawn_ApparelTracker)
                    continue;

                // Only include player-accessible items (in stockpiles, not forbidden)
                if (!thing.IsForbidden(Faction.OfPlayer))
                    result.Add(thing);
            }

            // Equipped items from colonists
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.equipment?.Primary != null)
                {
                    var primary = pawn.equipment.Primary;
                    if (primary.TryGetComp<CompForgeEnhancement>() != null)
                        result.Add(primary);
                }

                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var apparel in pawn.apparel.WornApparel)
                    {
                        if (apparel.TryGetComp<CompForgeEnhancement>() != null)
                            result.Add(apparel);
                    }
                }
            }

            return result.OrderBy(t =>
            {
                // Group by owner: equipped items sorted by pawn name, then storage items last
                if (t.ParentHolder is Pawn_EquipmentTracker eq)
                    return "0_" + (eq.pawn?.LabelShortCap ?? "");
                if (t.ParentHolder is Pawn_ApparelTracker ap)
                    return "0_" + (ap.pawn?.LabelShortCap ?? "");
                return "1_storage";
            }).ThenBy(t => t.def.label).ToList();
        }

        private static Color GetRefinementColor(int level)
        {
            if (level >= 10) return new Color(1f, 0.84f, 0f);     // Gold
            if (level >= 8) return new Color(0.7f, 0.4f, 0.9f);   // Purple
            if (level >= 6) return new Color(0.3f, 0.5f, 1f);     // Blue
            if (level >= 4) return new Color(0.3f, 0.9f, 0.3f);   // Green
            return new Color(0.85f, 0.85f, 0.85f);                // White
        }
    }
}
