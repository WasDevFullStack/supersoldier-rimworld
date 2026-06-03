using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Shared helper to extract rune rank label from a defName.
    /// </summary>
    public static class RuneRankHelper
    {
        private static readonly string[] RankSuffixes = { "_V", "_IV", "_III", "_II" };
        private static readonly string[] RankLabels   = { "V",  "IV",  "III",  "II" };
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        public static string GetRankLabel(string defName)
        {
            if (defName == null || !defName.StartsWith("Isekai_Rune_")) return null;

            if (Cache.TryGetValue(defName, out var cached)) return cached;

            string label = null;
            for (int i = 0; i < RankSuffixes.Length; i++)
            {
                if (defName.EndsWith(RankSuffixes[i]))
                {
                    label = RankLabels[i];
                    break;
                }
            }

            Cache[defName] = label;
            return label;
        }

        public static void DrawRankLabel(Rect iconRect, string label)
        {
            if (label == null) return;

            var prevColor = GUI.color;
            var prevFont = Text.Font;
            var prevAnchor = Text.Anchor;

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerRight;

            float pad = 2f;
            Rect labelRect = new Rect(iconRect.x + pad, iconRect.y + pad,
                                       iconRect.width - pad * 2f, iconRect.height - pad * 2f);

            // Shadow
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            Rect shadowRect = new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height);
            Widgets.Label(shadowRect, label);

            // Gold text
            GUI.color = new Color(0.95f, 0.85f, 0.55f);
            Widgets.Label(labelRect, label);

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }
    }

    /// <summary>
    /// Draws rank numeral on rune items when dropped on the map.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.DrawGUIOverlay))]
    public static class RuneRankOverlayPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Thing __instance)
        {
            if (!__instance.Spawned) return;
            string label = RuneRankHelper.GetRankLabel(__instance.def.defName);
            if (label == null) return;

            Vector2 screenPos = GenMapUI.LabelDrawPosFor(__instance, -0.6f);
            Rect iconRect = new Rect(screenPos.x - 12f, screenPos.y - 12f, 24f, 24f);
            RuneRankHelper.DrawRankLabel(iconRect, label);
        }
    }

    /// <summary>
    /// Draws rank numeral on rune icons in all UI contexts (inventory, trade, bills, etc.).
    /// Uses TargetMethod to locate the (Rect, Thing, ...) overload regardless of
    /// additional trailing parameters added in later RimWorld versions.
    /// </summary>
    [HarmonyPatch]
    public static class RuneRankIconPatch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            // Find the Widgets.ThingIcon overload starting with (Rect, Thing, ...).
            // 1.6 signature: (Rect, Thing, float, Rot4?, StyleCategoryDef)
            foreach (var m in typeof(Widgets).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (m.Name != nameof(Widgets.ThingIcon)) continue;
                var ps = m.GetParameters();
                if (ps.Length >= 2 && ps[0].ParameterType == typeof(Rect) && ps[1].ParameterType == typeof(Thing))
                    return m;
            }
            return null;
        }

        [HarmonyPostfix]
        public static void Postfix(Rect rect, Thing thing)
        {
            if (thing == null) return;
            string label = RuneRankHelper.GetRankLabel(thing.def.defName);
            RuneRankHelper.DrawRankLabel(rect, label);
        }
    }
}
