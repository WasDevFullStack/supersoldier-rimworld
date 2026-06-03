using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Adds an Isekai stats panel to the vanilla character creation screen.
    /// Shows level, rank, and 6 stats for the currently displayed pawn,
    /// with a button to re-randomize the stats.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), nameof(Page_ConfigureStartingPawns.DoWindowContents))]
    public static class Patch_CharacterCreation
    {
        private static FieldInfo curPawnField;

        [HarmonyPostfix]
        public static void Postfix(Page_ConfigureStartingPawns __instance, Rect rect)
        {
            try
            {
                // Get the currently displayed pawn via reflection
                if (curPawnField == null)
                {
                    curPawnField = typeof(Page_ConfigureStartingPawns).GetField("curPawn",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }

                Pawn pawn = curPawnField?.GetValue(__instance) as Pawn;
                if (pawn == null) return;

                var comp = pawn.GetComp<IsekaiComponent>();
                if (comp == null) return;

                DrawIsekaiPanel(rect, pawn, comp);

                // Cache stats for EdB Prepare Carefully compatibility.
                // EdB may recreate pawn objects when the game starts, losing
                // the stats set here. Caching each frame captures the latest state.
                PawnStatGenerator.CacheCharacterCreationStats(pawn, comp);
            }
            catch (Exception)
            {
                // Silent fail - don't disrupt character creation
            }
        }

        private static void DrawIsekaiPanel(Rect pageRect, Pawn pawn, IsekaiComponent comp)
        {
            // Panel dimensions and position (bottom-right of the page)
            float panelWidth = 200f;
            float panelHeight = 230f;
            float margin = 10f;
            Rect panelRect = new Rect(
                pageRect.xMax - panelWidth - margin,
                pageRect.yMax - panelHeight - 50f,
                panelWidth,
                panelHeight);

            // Draw panel background
            Widgets.DrawBoxSolid(panelRect, new Color(0.12f, 0.12f, 0.14f, 0.92f));
            Widgets.DrawBox(panelRect);

            float curY = panelRect.y + 8f;
            float innerX = panelRect.x + 10f;
            float innerWidth = panelWidth - 20f;

            // Title
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(innerX, curY, innerWidth, 22f), "Isekai_CharCreate_Title".Translate());
            curY += 24f;

            // Level and Rank display
            string rank = comp.GetRankString();
            Color rankColor = GetRankColor(rank);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            // "Lv. 12 — D-Rank"
            string levelText = "Isekai_CharCreate_Level".Translate() + " " + comp.currentLevel;
            Rect levelRect = new Rect(innerX, curY, innerWidth * 0.55f, 22f);
            Widgets.Label(levelRect, levelText);

            GUI.color = rankColor;
            Rect rankRect = new Rect(innerX + innerWidth * 0.55f, curY, innerWidth * 0.45f, 22f);
            Widgets.Label(rankRect, "Isekai_RankWithSuffix".Translate(rank));
            GUI.color = Color.white;
            curY += 26f;

            // Separator line
            Widgets.DrawLineHorizontal(innerX, curY, innerWidth);
            curY += 6f;

            // Stats in 2-column grid (3 rows)
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float colWidth = innerWidth / 2f;
            float rowH = 20f;

            DrawStatCell(new Rect(innerX, curY, colWidth, rowH), "STR", comp.stats.strength);
            DrawStatCell(new Rect(innerX + colWidth, curY, colWidth, rowH), "DEX", comp.stats.dexterity);
            curY += rowH;

            DrawStatCell(new Rect(innerX, curY, colWidth, rowH), "VIT", comp.stats.vitality);
            DrawStatCell(new Rect(innerX + colWidth, curY, colWidth, rowH), "INT", comp.stats.intelligence);
            curY += rowH;

            DrawStatCell(new Rect(innerX, curY, colWidth, rowH), "WIS", comp.stats.wisdom);
            DrawStatCell(new Rect(innerX + colWidth, curY, colWidth, rowH), "CHA", comp.stats.charisma);
            curY += rowH + 8f;

            // Reroll button
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect buttonRect = new Rect(innerX, curY, innerWidth, 30f);
            if (Widgets.ButtonText(buttonRect, "Isekai_CharCreate_Reroll".Translate()))
            {
                PawnStatGenerator.InitializePawnStats(pawn, comp);
            }

            // Reset text state
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static void DrawStatCell(Rect rect, string label, int value)
        {
            // Label
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            Widgets.Label(new Rect(rect.x, rect.y, 32f, rect.height), label);

            // Value
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 34f, rect.y, rect.width - 34f, rect.height), value.ToString());
        }

        private static Color GetRankColor(string rank)
        {
            switch (rank)
            {
                case "SSS": return new Color(1f, 0.85f, 0.3f);
                case "SS": return new Color(0.9f, 0.5f, 0.9f);
                case "S": return new Color(1f, 0.6f, 0.2f);
                case "A": return new Color(0.85f, 0.3f, 0.3f);
                case "B": return new Color(0.6f, 0.4f, 0.8f);
                case "C": return new Color(0.3f, 0.6f, 0.9f);
                case "D": return new Color(0.4f, 0.8f, 0.4f);
                case "E": return new Color(0.75f, 0.75f, 0.78f);
                case "F": return new Color(0.6f, 0.55f, 0.5f);
                default: return Color.gray;
            }
        }
    }
}
