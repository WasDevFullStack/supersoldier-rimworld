using System.Collections.Generic;
using System.Linq;
using IsekaiLeveling.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Runic Station window for applying and removing runes on equipment.
    /// Left panel: equipment list with slot indicators.
    /// Center: selected item rune slot details.
    /// Right: available runes to apply or remove.
    /// </summary>
    public class Window_RunicStation : Window
    {
        private Map map;
        private Thing selectedItem;
        private int selectedSlotIndex = -1;
        private int selectedRank = 1;
        private Vector2 equipScrollPos = Vector2.zero;
        private Vector2 runeScrollPos = Vector2.zero;
        private string searchText = "";

        // Animation
        private float openTime = -1f;
        private const float APPEAR_DURATION = 0.25f;
        private Dictionary<string, float> hoverTimers = new Dictionary<string, float>();

        // Layout (sized to match textures)
        private const float WIN_WIDTH = 800f;
        private const float WIN_HEIGHT = 520f;
        private const float LIST_WIDTH = 210f;
        private const float SLOT_WIDTH = 250f;
        private const float RUNE_WIDTH = 280f;
        private const float ROW_HEIGHT = 44f;
        private const float MARGIN = 10f;

        private static readonly Color Gold = new Color(0.85f, 0.72f, 0.45f);
        private static readonly Color RuneBlue = new Color(0.4f, 0.6f, 1.0f);
        private static readonly Color SlotEmptyColor = new Color(0.3f, 0.3f, 0.35f);
        private static readonly Color SlotFilledColor = new Color(0.3f, 0.7f, 0.5f);
        private static readonly Color TextSecondary = new Color(0.70f, 0.65f, 0.58f);

        public override Vector2 InitialSize => new Vector2(WIN_WIDTH, WIN_HEIGHT);

        protected override float Margin => IsekaiLevelingSettings.UseIsekaiUI ? 0f : 18f;

        public Window_RunicStation(Map map)
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
            if (useCustom && RunicStationTextures.WindowBg != null)
            {
                GUI.color = new Color(1f, 1f, 1f, fadeAlpha);
                GUI.DrawTexture(inRect, RunicStationTextures.WindowBg, ScaleMode.StretchToFill);
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

            // Panel scale: panels grow from 0.85 to 1.0 during appear
            float panelScale = useCustom ? Mathf.Lerp(0.85f, 1f, appearT) : 1f;

            Text.Font = GameFont.Medium;
            GUI.color = useCustom ? Gold : Color.white;
            float winPad = useCustom ? 16f : 0f;
            Widgets.Label(new Rect(inRect.x + winPad, inRect.y + winPad, inRect.width, 30f), "Isekai_Rune_Title".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float topOffset = inRect.y + 35f + winPad;
            Rect contentRect = new Rect(inRect.x + winPad, topOffset, inRect.width - winPad * 2f, inRect.height - (topOffset - inRect.y) - winPad);

            Rect listRect = new Rect(contentRect.x, contentRect.y, LIST_WIDTH, contentRect.height);
            Rect slotRect = new Rect(contentRect.x + LIST_WIDTH + MARGIN, contentRect.y, SLOT_WIDTH, contentRect.height);
            Rect runeRect = new Rect(contentRect.x + LIST_WIDTH + SLOT_WIDTH + MARGIN * 2, contentRect.y, RUNE_WIDTH, contentRect.height);

            DrawEquipmentList(ScaleRect(listRect, panelScale), useCustom);
            DrawRuneSlots(ScaleRect(slotRect, panelScale), useCustom);
            DrawAvailableRunes(ScaleRect(runeRect, panelScale), useCustom);

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
            if (useCustom && RunicStationTextures.PanelLeft != null)
                GUI.DrawTexture(rect, RunicStationTextures.PanelLeft, ScaleMode.StretchToFill);
            else
                Widgets.DrawMenuSection(rect);

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
                    || (t.ParentHolder is Pawn_EquipmentTracker eq3 && eq3.pawn?.LabelShortCap?.ToLower().Contains(filter) == true)
                    || (t.ParentHolder is Pawn_ApparelTracker ap3 && ap3.pawn?.LabelShortCap?.ToLower().Contains(filter) == true)).ToList();
            }
            float viewHeight = items.Count * ROW_HEIGHT;
            Rect viewRect = new Rect(0f, 0f, scrollOuter.width - 16f, viewHeight);

            Widgets.BeginScrollView(scrollOuter, ref equipScrollPos, viewRect);

            float y = 0f;
            foreach (var item in items)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, ROW_HEIGHT);
                var comp = item.TryGetComp<CompForgeEnhancement>();

                // Highlight selected
                if (item == selectedItem)
                {
                    Widgets.DrawHighlightSelected(rowRect);
                }
                else
                {
                    float rowHover = GetHoverAmount($"eq_{item.ThingID}", Mouse.IsOver(rowRect));
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
                float textW = viewRect.width - textX - 50f;

                // Label
                string label = item.LabelCapNoCount;
                if (label.Length > 22)
                    label = label.Substring(0, 19) + "...";

                // Slot indicator
                string slotInfo = comp != null ? $"[{comp.UsedRuneSlots}/{comp.MaxRuneSlots}]" : "";
                Color slotColor = comp != null && comp.FreeRuneSlots > 0 ? SlotFilledColor : SlotEmptyColor;

                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(textX, y + 2f, textW, 22f), label);

                // Owner
                string owner = null;
                if (item.ParentHolder is Pawn_EquipmentTracker eq)
                    owner = eq.pawn?.LabelShortCap;
                else if (item.ParentHolder is Pawn_ApparelTracker ap)
                    owner = ap.pawn?.LabelShortCap;

                Text.Font = GameFont.Tiny;
                GUI.color = TextSecondary;
                Widgets.Label(new Rect(textX, y + 22f, textW, 18f), owner ?? (string)"Isekai_UI_InStorage".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                // Slot count (right-aligned)
                GUI.color = slotColor;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(rowRect.x, rowRect.y, rowRect.width - 4f, rowRect.height), slotInfo);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                if (Widgets.ButtonInvisible(rowRect))
                {
                    selectedItem = item;
                    selectedSlotIndex = -1;
                }

                y += ROW_HEIGHT;
            }

            Widgets.EndScrollView();
        }

        private void DrawRuneSlots(Rect rect, bool useCustom)
        {
            if (useCustom && RunicStationTextures.PanelCenter != null)
                GUI.DrawTexture(rect, RunicStationTextures.PanelCenter, ScaleMode.StretchToFill);
            else
                Widgets.DrawMenuSection(rect);

            float pad = useCustom ? 32f : 8f;
            Rect inner = new Rect(rect.x + 8f, rect.y + pad, rect.width - 16f, rect.height - pad * 2f);

            if (selectedItem == null)
            {
                GUI.color = TextSecondary;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, "Isekai_Rune_SelectFromList".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var comp = selectedItem.TryGetComp<CompForgeEnhancement>();
            if (comp == null)
            {
                GUI.color = TextSecondary;
                Widgets.Label(inner, "Isekai_Rune_CannotHoldRunes".Translate());
                GUI.color = Color.white;
                return;
            }

            float y = inner.y;

            // Item name
            Text.Font = GameFont.Medium;
            GUI.color = Gold;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), selectedItem.LabelCap);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 30f;

            // Type
            string type = selectedItem.def.IsWeapon ? (string)"Isekai_Rune_TypeWeapon".Translate() : (string)"Isekai_Rune_TypeArmor".Translate();
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), type);
            y += 22f;

            // Refinement
            if (comp.refinementLevel > 0)
            {
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Forge_RefinementLevel".Translate(comp.refinementLevel.ToString()));
                y += 22f;
            }

            y += 8f;

            // Rune slots header
            GUI.color = RuneBlue;
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), "Isekai_Rune_SlotsHeader".Translate(comp.UsedRuneSlots.ToString(), comp.MaxRuneSlots.ToString()));
            GUI.color = Color.white;
            y += 26f;

            // Draw each slot
            var appliedRunesWithRanks = comp.GetAppliedRunesWithRanks();
            for (int i = 0; i < comp.MaxRuneSlots; i++)
            {
                Rect slotRect = new Rect(inner.x, y, inner.width, 52f);
                bool isFilled = i < appliedRunesWithRanks.Count;
                bool isSelected = selectedSlotIndex == i;

                // Slot background — use custom textures or fallback
                if (useCustom)
                {
                    Texture2D slotTex = isFilled ? RunicStationTextures.SlotFilled : RunicStationTextures.SlotEmpty;
                    if (slotTex != null)
                    {
                        GUI.color = isSelected ? new Color(1f, 1f, 1f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
                        GUI.DrawTexture(slotRect, slotTex, ScaleMode.StretchToFill);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        Color bgColor = isSelected ? new Color(0.35f, 0.35f, 0.4f) : new Color(0.18f, 0.18f, 0.22f);
                        Widgets.DrawBoxSolid(slotRect, bgColor);
                        Widgets.DrawBox(slotRect);
                    }

                    // Highlight selected slot with subtle glow
                    if (isSelected)
                    {
                        GUI.color = new Color(RuneBlue.r, RuneBlue.g, RuneBlue.b, 0.15f);
                        Widgets.DrawBoxSolid(slotRect, GUI.color);
                        GUI.color = Color.white;
                    }
                }
                else
                {
                    Color bgColor = isSelected ? new Color(0.35f, 0.35f, 0.4f) : new Color(0.18f, 0.18f, 0.22f);
                    Widgets.DrawBoxSolid(slotRect, bgColor);
                    Widgets.DrawBox(slotRect);
                }

                if (isFilled)
                {
                    var (rune, rank) = appliedRunesWithRanks[i];
                    GUI.color = rune.runeColor;
                    Text.Font = GameFont.Small;
                    Widgets.Label(new Rect(slotRect.x + 6f, slotRect.y + 2f, slotRect.width - 12f, 22f),
                        $"{rune.LabelCap} {RuneDef.GetRomanNumeral(rank)}");
                    GUI.color = TextSecondary;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(slotRect.x + 6f, slotRect.y + 22f, slotRect.width - 12f, 22f),
                        rune.GetStatDescriptionForRank(rank));
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                }
                else
                {
                    GUI.color = SlotEmptyColor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(slotRect, "Isekai_Rune_EmptySlot".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                if (Widgets.ButtonInvisible(slotRect))
                    selectedSlotIndex = i;

                y += 56f;
            }

            // Remove rune button (if a filled slot is selected)
            y += 8f;
            if (selectedSlotIndex >= 0 && selectedSlotIndex < appliedRunesWithRanks.Count)
            {
                var (selectedRune, selectedRuneRank) = appliedRunesWithRanks[selectedSlotIndex];
                string runeLabel = $"{selectedRune.LabelCap} {RuneDef.GetRomanNumeral(selectedRuneRank)}";
                Rect removeRect = new Rect(inner.x, y, inner.width, 32f);

                if (useCustom && RunicStationTextures.ButtonRemove != null)
                {
                    float removeHover = GetHoverAmount("remove_btn", Mouse.IsOver(removeRect));
                    Rect drawRect = removeRect;
                    if (removeHover > 0f)
                    {
                        float sc = 1f + 0.04f * removeHover;
                        Vector2 c = removeRect.center;
                        drawRect = new Rect(c.x - removeRect.width * sc / 2f, c.y - removeRect.height * sc / 2f,
                            removeRect.width * sc, removeRect.height * sc);
                    }
                    float brightness = 1f + 0.25f * removeHover;
                    GUI.color = new Color(brightness, brightness, brightness, 1f);
                    GUI.DrawTexture(drawRect, RunicStationTextures.ButtonRemove, ScaleMode.StretchToFill);
                    GUI.color = Color.white;

                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(1f, 0.85f, 0.85f);
                    Widgets.Label(removeRect, "Isekai_Rune_RemoveButton".Translate());
                    GUI.color = Color.white;
                    Text.Anchor = TextAnchor.UpperLeft;

                    if (Widgets.ButtonInvisible(removeRect))
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "Isekai_Rune_RemoveConfirm".Translate(runeLabel, selectedItem.LabelCap),
                            () =>
                            {
                                comp.RemoveRuneAt(selectedSlotIndex);
                                selectedSlotIndex = -1;
                            }));
                    }
                }
                else
                {
                    if (Widgets.ButtonText(removeRect, "Isekai_Rune_RemoveWithName".Translate(runeLabel)))
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "Isekai_Rune_RemoveConfirm".Translate(runeLabel, selectedItem.LabelCap),
                            () =>
                            {
                                comp.RemoveRuneAt(selectedSlotIndex);
                                selectedSlotIndex = -1;
                            }));
                    }
                }
            }
        }

        private void DrawAvailableRunes(Rect rect, bool useCustom)
        {
            if (useCustom && RunicStationTextures.PanelRight != null)
                GUI.DrawTexture(rect, RunicStationTextures.PanelRight, ScaleMode.StretchToFill);
            else
                Widgets.DrawMenuSection(rect);

            float topPad = useCustom ? 32f : 8f;
            float botPad = useCustom ? 32f : 8f;
            Rect inner = new Rect(rect.x + 8f, rect.y + topPad, rect.width - 16f, rect.height - topPad - botPad);

            if (selectedItem == null)
            {
                GUI.color = TextSecondary;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inner, "Isekai_Rune_SelectFirst".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            var comp = selectedItem.TryGetComp<CompForgeEnhancement>();
            if (comp == null) return;

            // Header
            GUI.color = RuneBlue;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "Isekai_Rune_Available".Translate());
            GUI.color = Color.white;

            // Get available rune items from the map
            bool godMode = Prefs.DevMode && DebugSettings.godMode;
            var runeItems = GetAvailableRuneItems(comp);

            if (runeItems.Count == 0 && !godMode)
            {
                GUI.color = TextSecondary;
                Widgets.Label(new Rect(inner.x, inner.y + 28f, inner.width, 44f),
                    "Isekai_Rune_NoRunesAvailable".Translate());
                GUI.color = Color.white;
                return;
            }

            // In god mode with no physical items, show all compatible RuneDefs directly
            if (godMode && runeItems.Count == 0)
            {
                DrawGodModeRunes(inner, comp, useCustom);
                return;
            }

            float viewHeight = runeItems.Count * 58f;
            Rect scrollArea = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            Rect viewRect = new Rect(0f, 0f, scrollArea.width - 16f, viewHeight);

            Widgets.BeginScrollView(scrollArea, ref runeScrollPos, viewRect);

            float y = 0f;
            foreach (var runeItem in runeItems)
            {
                var (runeDef, itemRank) = FindRuneDefAndRankForItem(runeItem.def.defName);
                if (runeDef == null) continue;

                Rect rowRect = new Rect(0f, y, viewRect.width, 54f);

                // Row background — use slot texture or fallback
                float rowHover = GetHoverAmount($"rune_{runeItem.ThingID}", Mouse.IsOver(rowRect));
                if (useCustom && RunicStationTextures.SlotEmpty != null)
                {
                    float brightness = 1f + 0.15f * rowHover;
                    GUI.color = new Color(brightness, brightness, brightness, 1f);
                    GUI.DrawTexture(rowRect, RunicStationTextures.SlotEmpty, ScaleMode.StretchToFill);
                    GUI.color = Color.white;
                }
                else
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.2f));
                    if (rowHover > 0f)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.06f * rowHover);
                        Widgets.DrawHighlight(rowRect);
                        GUI.color = Color.white;
                    }
                    Widgets.DrawBox(rowRect);
                }

                // Rune name + rank + color
                string runeLabel = $"{runeDef.LabelCap} {RuneDef.GetRomanNumeral(itemRank)}";
                GUI.color = runeDef.runeColor;
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 2f, rowRect.width - 50f, 22f), runeLabel);
                GUI.color = Color.white;

                // Count
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(rowRect.x, rowRect.y + 2f, rowRect.width - 6f, 22f), $"×{runeItem.stackCount}");
                Text.Anchor = TextAnchor.UpperLeft;

                // Rank-scaled description
                GUI.color = TextSecondary;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 22f, rowRect.width - 60f, 20f),
                    runeDef.GetStatDescriptionForRank(itemRank));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                // Apply button
                bool canApply = comp.CanAddRune() && runeItem.stackCount >= 1;
                Rect applyRect = new Rect(rowRect.xMax - 58f, rowRect.y + 28f, 56f, 22f);
                if (canApply)
                {
                    if (useCustom && RunicStationTextures.ButtonApply != null)
                    {
                        float applyHover = GetHoverAmount($"apply_{runeItem.ThingID}", Mouse.IsOver(applyRect));
                        Rect drawRect = applyRect;
                        if (applyHover > 0f)
                        {
                            float sc = 1f + 0.06f * applyHover;
                            Vector2 c = applyRect.center;
                            drawRect = new Rect(c.x - applyRect.width * sc / 2f, c.y - applyRect.height * sc / 2f,
                                applyRect.width * sc, applyRect.height * sc);
                        }
                        float brightness = 1f + 0.25f * applyHover;
                        GUI.color = new Color(brightness, brightness, brightness, 1f);
                        GUI.DrawTexture(drawRect, RunicStationTextures.ButtonApply, ScaleMode.StretchToFill);
                        GUI.color = Color.white;

                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(applyRect, "Isekai_Rune_Apply".Translate());
                        Text.Anchor = TextAnchor.UpperLeft;

                        if (Widgets.ButtonInvisible(applyRect))
                        {
                            if (comp.TryAddRune(runeDef, itemRank))
                            {
                                if (!(Prefs.DevMode && DebugSettings.godMode))
                                {
                                    if (runeItem.stackCount > 1)
                                        runeItem.stackCount -= 1;
                                    else
                                        runeItem.Destroy();
                                }
                            }
                        }
                    }
                    else
                    {
                        if (Widgets.ButtonText(applyRect, "Isekai_Rune_Apply".Translate()))
                        {
                            if (comp.TryAddRune(runeDef, itemRank))
                            {
                                if (!(Prefs.DevMode && DebugSettings.godMode))
                                {
                                    if (runeItem.stackCount > 1)
                                        runeItem.stackCount -= 1;
                                    else
                                        runeItem.Destroy();
                                }
                            }
                        }
                    }
                }
                else if (!comp.CanAddRune())
                {
                    GUI.color = TextSecondary;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(applyRect, "Isekai_Rune_SlotsFull".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                y += 58f;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Get all weapons and apparel on the map (in stockpiles + equipped by colonists).
        /// </summary>
        private List<Thing> GetAllEquipment()
        {
            var result = new List<Thing>();
            if (map == null) return result;

            // From stockpiles / map
            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t.def.category != ThingCategory.Item) continue;
                if (!t.def.IsWeapon && !t.def.IsApparel) continue;
                // Skip stuff materials (wood, steel, etc.) that count as weapons
                // in vanilla but aren't real equipment. Belt-and-suspenders for saves
                // that injected the comp before this filter existed.
                if (t.def.IsStuff) continue;
                if (t.TryGetComp<CompForgeEnhancement>() == null) continue;
                result.Add(t);
            }

            // Equipped by colonists
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.equipment?.Primary != null)
                {
                    var wp = pawn.equipment.Primary;
                    if (wp.TryGetComp<CompForgeEnhancement>() != null && !result.Contains(wp))
                        result.Add(wp);
                }
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (var ap in pawn.apparel.WornApparel)
                    {
                        if (ap.TryGetComp<CompForgeEnhancement>() != null && !result.Contains(ap))
                            result.Add(ap);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get rune items on the map that are compatible with the given equipment.
        /// </summary>
        private List<Thing> GetAvailableRuneItems(CompForgeEnhancement comp)
        {
            var result = new List<Thing>();
            if (map == null || comp == null) return result;

            bool isWeapon = comp.parent.def.IsWeapon;
            RuneCategory targetCategory = isWeapon ? RuneCategory.Weapon : RuneCategory.Armor;

            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t.def.category != ThingCategory.Item) continue;
                if (!t.def.defName.StartsWith("Isekai_Rune_")) continue;

                RuneDef runeDef = FindRuneDefAndRankForItem(t.def.defName).runeDef;
                if (runeDef != null && runeDef.category == targetCategory)
                    result.Add(t);
            }

            return result;
        }

        private static readonly string[] RankSuffixes = { "_V", "_IV", "_III", "_II" };
        private static readonly int[] RankValues = { 5, 4, 3, 2 };

        /// <summary>
        /// Map an Isekai_Rune_X or Isekai_Rune_X_III item defName to RuneDef + rank.
        /// Convention: "Isekai_Rune_Fury" → rank I, "Isekai_Rune_Fury_III" → rank III.
        /// </summary>
        private static (RuneDef runeDef, int rank) FindRuneDefAndRankForItem(string itemDefName)
        {
            if (!itemDefName.StartsWith("Isekai_Rune_")) return (null, 1);
            string afterPrefix = itemDefName.Substring("Isekai_Rune_".Length);

            for (int i = 0; i < RankSuffixes.Length; i++)
            {
                if (afterPrefix.EndsWith(RankSuffixes[i]))
                {
                    string runeName = afterPrefix.Substring(0, afterPrefix.Length - RankSuffixes[i].Length);
                    var def = DefDatabase<RuneDef>.GetNamedSilentFail("Isekai_RuneDef_" + runeName);
                    if (def != null) return (def, RankValues[i]);
                }
            }

            var baseDef = DefDatabase<RuneDef>.GetNamedSilentFail("Isekai_RuneDef_" + afterPrefix);
            return (baseDef, 1);
        }

        /// <summary>
        /// God mode: show all compatible RuneDefs for free application without needing items.
        /// </summary>
        private void DrawGodModeRunes(Rect inner, CompForgeEnhancement comp, bool useCustom)
        {
            bool isWeapon = comp.parent.def.IsWeapon;
            RuneCategory targetCategory = isWeapon ? RuneCategory.Weapon : RuneCategory.Armor;
            var allRunes = DefDatabase<RuneDef>.AllDefsListForReading
                .Where(r => r.category == targetCategory).ToList();

            if (allRunes.Count == 0) return;

            float viewHeight = allRunes.Count * 82f;
            Rect scrollArea = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            Rect viewRect = new Rect(0f, 0f, scrollArea.width - 16f, viewHeight);

            Widgets.BeginScrollView(scrollArea, ref runeScrollPos, viewRect);

            float y = 0f;
            foreach (var runeDef in allRunes)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, 78f);

                // Row background — use slot texture or fallback
                float rowHover = GetHoverAmount($"god_{runeDef.defName}", Mouse.IsOver(rowRect));
                if (useCustom && RunicStationTextures.SlotEmpty != null)
                {
                    float brightness = 1f + 0.15f * rowHover;
                    GUI.color = new Color(brightness, brightness, brightness, 1f);
                    GUI.DrawTexture(rowRect, RunicStationTextures.SlotEmpty, ScaleMode.StretchToFill);
                    GUI.color = Color.white;
                }
                else
                {
                    Widgets.DrawBoxSolid(rowRect, new Color(0.15f, 0.15f, 0.2f));
                    if (rowHover > 0f)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.06f * rowHover);
                        Widgets.DrawHighlight(rowRect);
                        GUI.color = Color.white;
                    }
                    Widgets.DrawBox(rowRect);
                }

                GUI.color = runeDef.runeColor;
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 2f, rowRect.width - 50f, 22f), runeDef.LabelCap);
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(rowRect.x, rowRect.y + 2f, rowRect.width - 6f, 22f), "Isekai_Rune_DevLabel".Translate());
                Text.Anchor = TextAnchor.UpperLeft;

                GUI.color = TextSecondary;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rowRect.x + 6f, rowRect.y + 20f, rowRect.width - 12f, 20f),
                    runeDef.GetStatDescriptionForRank(selectedRank));
                Text.Font = GameFont.Small;
                GUI.color = Color.white;

                // Rank selector buttons
                float btnW = 28f;
                float rankY = rowRect.y + 42f;
                float rankX = rowRect.x + 6f;
                for (int r = 1; r <= runeDef.maxRank; r++)
                {
                    Rect btnRect = new Rect(rankX, rankY, btnW, 22f);
                    bool isActive = selectedRank == r;
                    if (isActive)
                        Widgets.DrawBoxSolid(btnRect, new Color(0.3f, 0.5f, 0.8f, 0.5f));
                    if (Widgets.ButtonText(btnRect, RuneDef.GetRomanNumeral(r), true, true, true))
                        selectedRank = r;
                    rankX += btnW + 2f;
                }

                bool canApply = comp.CanAddRune();
                Rect applyRect = new Rect(rowRect.xMax - 58f, rankY, 56f, 22f);
                if (canApply)
                {
                    if (useCustom && RunicStationTextures.ButtonApply != null)
                    {
                        float applyHover = GetHoverAmount($"gapply_{runeDef.defName}", Mouse.IsOver(applyRect));
                        Rect drawRect = applyRect;
                        if (applyHover > 0f)
                        {
                            float sc = 1f + 0.06f * applyHover;
                            Vector2 c = applyRect.center;
                            drawRect = new Rect(c.x - applyRect.width * sc / 2f, c.y - applyRect.height * sc / 2f,
                                applyRect.width * sc, applyRect.height * sc);
                        }
                        float brightness = 1f + 0.25f * applyHover;
                        GUI.color = new Color(brightness, brightness, brightness, 1f);
                        GUI.DrawTexture(drawRect, RunicStationTextures.ButtonApply, ScaleMode.StretchToFill);
                        GUI.color = Color.white;

                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(applyRect, "Isekai_Rune_Apply".Translate());
                        Text.Anchor = TextAnchor.UpperLeft;

                        if (Widgets.ButtonInvisible(applyRect))
                            comp.TryAddRune(runeDef, selectedRank);
                    }
                    else
                    {
                        if (Widgets.ButtonText(applyRect, "Isekai_Rune_Apply".Translate()))
                            comp.TryAddRune(runeDef, selectedRank);
                    }
                }
                else
                {
                    GUI.color = TextSecondary;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(applyRect, "Isekai_Rune_SlotsFull".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                y += 82f;
            }

            Widgets.EndScrollView();
        }
    }
}
