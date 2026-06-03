using System;
using Verse;
using UnityEngine;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// RimHUD custom widget provider for Isekai Level &amp; Rank display.
    /// RimHUD discovers this class via reflection (defClass in CustomValueDef).
    /// No assembly reference to RimHUD is needed.
    /// 
    /// Shows: "Lv.42 [A] Hero" — level, rank letter, and rank title in one row.
    /// </summary>
    public static class RimHUD_IsekaiLevelRank
    {
        public static (string label, string value, Func<string> tooltip, Action onHover, Action onClick)
            GetParameters(Pawn pawn)
        {
            try
            {
                if (pawn == null) return (null, null, null, null, null);

                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return (null, null, null, null, null);

                string rank = comp.GetRankString();
                string title = GetRankTitle(rank);
                string label = "Isekai_RimHUD_Rank".Translate();
                string value = "Isekai_RimHUD_LevelFormat".Translate(comp.currentLevel, rank, title);

                Func<string> tooltip = () =>
                {
                    try
                    {
                        var c = IsekaiComponent.GetCached(pawn);
                        if (c == null) return null;
                        string r = c.GetRankString();
                        string t = GetRankTitle(r);
                        
                        string tip = "Isekai_RimHUD_Tooltip_Level".Translate(c.currentLevel.ToString())
                            + "\n" + "Isekai_RimHUD_Tooltip_Rank".Translate(r, t)
                            + "\n" + "Isekai_RimHUD_Tooltip_XP".Translate(
                                NumberFormatting.FormatNum(c.currentXP), 
                                NumberFormatting.FormatNum(c.XPToNextLevel));

                        if (c.stats?.availableStatPoints > 0)
                        {
                            tip += "\n\n" + "Isekai_RimHUD_Tooltip_StatPoints".Translate(c.stats.availableStatPoints.ToString());
                        }

                        // Active title
                        if (c.titles?.activeTitle != null)
                        {
                            tip += "\n\n" + "Isekai_RimHUD_Tooltip_Title".Translate(c.titles.activeTitle.LabelCap);
                        }

                        return tip;
                    }
                    catch { return null; }
                };

                return (label, value, tooltip, null, null);
            }
            catch { return (null, null, null, null, null); }
        }

        private static string GetRankTitle(string rank)
        {
            switch (rank)
            {
                case "SSS": return "Isekai_Title_Overlord".Translate();
                case "SS": return "Isekai_Title_Monarch".Translate();
                case "S": return "Isekai_Title_Legend".Translate();
                case "A": return "Isekai_Title_Hero".Translate();
                case "B": return "Isekai_Title_Champion".Translate();
                case "C": return "Isekai_Title_Warrior".Translate();
                case "D": return "Isekai_Title_Adventurer".Translate();
                case "E": return "Isekai_Title_Apprentice".Translate();
                case "F": return "Isekai_Title_Awakened".Translate();
                default: return "";
            }
        }
    }

    /// <summary>
    /// RimHUD custom bar widget for Isekai XP progress.
    /// Shows current XP / XP needed as a progress bar.
    /// </summary>
    public static class RimHUD_IsekaiXPBar
    {
        public static (string label, string value, float fill, float[] thresholds,
            Func<string> tooltip, Action onHover, Action onClick)
            GetParameters(Pawn pawn)
        {
            try
            {
                if (pawn == null) return (null, null, -1f, null, null, null, null);

                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return (null, null, -1f, null, null, null, null);

                string label = "Isekai_RimHUD_XP".Translate();
                float fill = comp.LevelProgress;
                string value = $"{Mathf.RoundToInt(fill * 100f)}%";

                Func<string> tooltip = () =>
                {
                    try
                    {
                        var c = IsekaiComponent.GetCached(pawn);
                        if (c == null) return null;
                        return "Isekai_RimHUD_Tooltip_XPBar".Translate(
                            NumberFormatting.FormatNum(c.currentXP),
                            NumberFormatting.FormatNum(c.XPToNextLevel),
                            Mathf.RoundToInt(c.LevelProgress * 100f).ToString());
                    }
                    catch { return null; }
                };

                return (label, value, fill, null, tooltip, null, null);
            }
            catch { return (null, null, -1f, null, null, null, null); }
        }
    }

    /// <summary>
    /// RimHUD custom value widget for available stat points.
    /// Only displays when the pawn has unspent points.
    /// </summary>
    public static class RimHUD_IsekaiStatPoints
    {
        public static (string label, string value, Func<string> tooltip, Action onHover, Action onClick)
            GetParameters(Pawn pawn)
        {
            try
            {
                if (pawn == null) return (null, null, null, null, null);

                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return (null, null, null, null, null);

                // Only show when there are unspent points
                if (comp.stats?.availableStatPoints <= 0) return (null, null, null, null, null);

                string label = "Isekai_RimHUD_StatPoints".Translate();
                string value = comp.stats.availableStatPoints.ToString();

                Func<string> tooltip = () =>
                {
                    try { return "Isekai_RimHUD_Tooltip_AllocateStats".Translate(); }
                    catch { return null; }
                };

                return (label, value, tooltip, null, null);
            }
            catch { return (null, null, null, null, null); }
        }
    }
}
