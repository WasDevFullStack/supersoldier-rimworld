using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Character Editor compatibility - adds Isekai stats to Character Editor interface
    /// Allows editing pawn level and stats (STR, DEX, VIT, INT, WIS, CHA) during character creation
    /// Uses Harmony patches to inject UI into Character Editor
    /// </summary>
    public static class CharacterEditorCompatibility
    {
        private static bool initialized = false;
        
        public static void Initialize()
        {
            if (initialized) return;
            
            // Character Editor uses generic DialogTemplate<T> which Harmony can't patch directly
            // Instead, we provide a floating button when Character Editor is open
            // The button opens our standalone Isekai Stats editor window
            
            Log.Message("[Isekai Leveling] Character Editor detected - Isekai Stats window available via mod settings or dev mode");
            initialized = true;
        }
        
        // Harmony postfix - adds our section at the bottom of Character Editor window
        private static void DoWindowContents_Postfix(Window __instance, Rect inRect)
        {
            try
            {
                // Get the pawn being edited via reflection
                Pawn pawn = GetEditingPawn(__instance);
                if (pawn == null) return;
                
                // Draw our section at the bottom
                float sectionHeight = 450f;
                Rect isekaiRect = new Rect(inRect.x, inRect.yMax - sectionHeight - 10f, inRect.width, sectionHeight);
                
                DrawIsekaiSection(isekaiRect, pawn);
            }
            catch (Exception ex)
            {
                // Silent fail - don't spam errors
                if (Time.frameCount % 300 == 0) // Log once every 5 seconds
                {
                    Log.Warning($"[Isekai Leveling] Character Editor integration error: {ex.Message}");
                }
            }
        }
        
        private static Pawn GetEditingPawn(Window window)
        {
            // Try to find pawn field via reflection
            var fields = window.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            // Common field names
            string[] pawnFieldNames = new[] { "pawn", "editingPawn", "targetPawn", "currentPawn", "selectedPawn" };
            
            foreach (var fieldName in pawnFieldNames)
            {
                var field = fields.FirstOrDefault(f => f.Name.ToLower() == fieldName.ToLower());
                if (field != null && field.FieldType == typeof(Pawn))
                {
                    return field.GetValue(window) as Pawn;
                }
            }
            
            // If not found by name, find first Pawn field
            var pawnField = fields.FirstOrDefault(f => f.FieldType == typeof(Pawn));
            if (pawnField != null)
            {
                return pawnField.GetValue(window) as Pawn;
            }
            
            return null;
        }
        
        /// <summary>
        /// Draws the Isekai stats section in Character Editor
        /// </summary>
        private static void DrawIsekaiSection(Rect rect, Pawn pawn)
        {
            if (pawn == null) return;
            
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null)
            {
                // Add component if missing - use thingClass approach since we can't create comp directly
                ThingWithComps thing = pawn as ThingWithComps;
                if (thing != null)
                {
                    comp = (IsekaiComponent)Activator.CreateInstance(typeof(IsekaiComponent));
                    thing.AllComps.Add(comp);
                    comp.parent = pawn;
                    // Don't call PostSpawnSetup in character editor - pawn not spawned yet
                }
                else
                {
                    return; // Can't add component
                }
            }
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(rect);
            
            // Header
            Text.Font = GameFont.Medium;
            listing.Label("Isekai Leveling Stats");
            Text.Font = GameFont.Small;
            listing.GapLine();
            
            // Level control
            Rect levelRect = listing.GetRect(30f);
            Rect levelLabelRect = new Rect(levelRect.x, levelRect.y, levelRect.width * 0.3f, levelRect.height);
            Rect levelSliderRect = new Rect(levelRect.x + levelRect.width * 0.3f + 5f, levelRect.y, levelRect.width * 0.5f, levelRect.height);
            Rect levelFieldRect = new Rect(levelRect.x + levelRect.width * 0.82f, levelRect.y, levelRect.width * 0.18f, levelRect.height);
            
            Widgets.Label(levelLabelRect, "Level: ");
            int newLevel = (int)Widgets.HorizontalSlider(levelSliderRect, comp.Level, 1f, 500f, true, comp.Level.ToString());
            
            string levelBuffer = comp.Level.ToString();
            Widgets.TextFieldNumeric(levelFieldRect, ref newLevel, ref levelBuffer, 1f, 500f);
            
            if (newLevel != comp.Level)
            {
                SetLevelDirectly(comp, newLevel);
            }
            
            listing.Gap(10f);
            
            // Available stat points
            string pointsText = $"Available Points: {comp.stats.availableStatPoints}";
            listing.Label(pointsText);
            listing.Gap(5f);
            
            // Stat controls (STR, DEX, VIT, INT, WIS, CHA)
            DrawStatControl(listing, "STR (Strength)", comp, IsekaiStatType.Strength);
            DrawStatControl(listing, "DEX (Dexterity)", comp, IsekaiStatType.Dexterity);
            DrawStatControl(listing, "VIT (Vitality)", comp, IsekaiStatType.Vitality);
            DrawStatControl(listing, "INT (Intelligence)", comp, IsekaiStatType.Intelligence);
            DrawStatControl(listing, "WIS (Wisdom)", comp, IsekaiStatType.Wisdom);
            DrawStatControl(listing, "CHA (Charisma)", comp, IsekaiStatType.Charisma);
            
            listing.Gap(10f);
            
            // Reset button
            if (listing.ButtonText("Reset All Stats"))
            {
                comp.stats.strength = 5;
                comp.stats.dexterity = 5;
                comp.stats.vitality = 5;
                comp.stats.intelligence = 5;
                comp.stats.wisdom = 5;
                comp.stats.charisma = 5;
                RecalculateAvailablePoints(comp);
            }
            
            // Auto-allocate button
            if (listing.ButtonText("Auto-Distribute Points"))
            {
                AutoDistributePoints(comp);
            }
            
            listing.End();
        }
        
        private static void DrawStatControl(Listing_Standard listing, string label, IsekaiComponent comp, IsekaiStatType statType)
        {
            int currentValue = comp.stats.GetStat(statType);
            
            Rect statRect = listing.GetRect(30f);
            Rect labelRect = new Rect(statRect.x, statRect.y, statRect.width * 0.35f, statRect.height);
            Rect minusRect = new Rect(statRect.x + statRect.width * 0.35f, statRect.y, 30f, statRect.height);
            Rect valueRect = new Rect(statRect.x + statRect.width * 0.35f + 35f, statRect.y, 60f, statRect.height);
            Rect plusRect = new Rect(statRect.x + statRect.width * 0.35f + 100f, statRect.y, 30f, statRect.height);
            Rect sliderRect = new Rect(statRect.x + statRect.width * 0.35f + 135f, statRect.y, statRect.width * 0.45f, statRect.height);
            
            // Label
            Widgets.Label(labelRect, label);
            
            // Minus button
            if (Widgets.ButtonText(minusRect, "-") && currentValue > 1)
            {
                comp.stats.SetStat(statType, currentValue - 1);
                RecalculateAvailablePoints(comp);
            }
            
            // Current value display
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(valueRect, currentValue.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Plus button
            if (Widgets.ButtonText(plusRect, "+") && comp.stats.availableStatPoints > 0)
            {
                comp.stats.SetStat(statType, currentValue + 1);
                comp.stats.availableStatPoints--;
            }
            
            // Slider for direct value setting
            int newValue = (int)Widgets.HorizontalSlider(sliderRect, currentValue, 1f, 200f, false);
            if (newValue != currentValue)
            {
                comp.stats.SetStat(statType, newValue);
                RecalculateAvailablePoints(comp);
            }
        }
        
        /// <summary>
        /// Directly set level and recalculate stat points
        /// </summary>
        public static void SetLevelDirectly(IsekaiComponent comp, int newLevel)
        {
            // Use reflection to set the private currentLevel field
            var levelField = typeof(IsekaiComponent).GetField("currentLevel", 
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (levelField != null)
            {
                levelField.SetValue(comp, newLevel);
                RecalculateAvailablePoints(comp);
            }
        }
        
        /// <summary>
        /// Recalculate available stat points based on level and allocated stats
        /// </summary>
        public static void RecalculateAvailablePoints(IsekaiComponent comp)
        {
            // Total points from leveling
            int totalPointsFromLevels = comp.Level * IsekaiLevelingSettings.skillPointsPerLevel;
            
            // Points already allocated to stats
            int allocatedPoints = comp.stats.TotalAllocatedPoints;
            
            // Available = total - allocated
            comp.stats.availableStatPoints = totalPointsFromLevels - allocatedPoints;
        }
        
        public static void AutoDistributePoints(IsekaiComponent comp)
        {
            // Distribute available points evenly across all stats
            while (comp.stats.availableStatPoints > 0)
            {
                comp.stats.strength++;
                comp.stats.availableStatPoints--;
                if (comp.stats.availableStatPoints <= 0) break;
                
                comp.stats.dexterity++;
                comp.stats.availableStatPoints--;
                if (comp.stats.availableStatPoints <= 0) break;
                
                comp.stats.vitality++;
                comp.stats.availableStatPoints--;
                if (comp.stats.availableStatPoints <= 0) break;
                
                comp.stats.intelligence++;
                comp.stats.availableStatPoints--;
                if (comp.stats.availableStatPoints <= 0) break;
                
                comp.stats.wisdom++;
                comp.stats.availableStatPoints--;
                if (comp.stats.availableStatPoints <= 0) break;
                
                comp.stats.charisma++;
                comp.stats.availableStatPoints--;
            }
        }
        
        /// <summary>
        /// Apply stat effects when pawn is created in Character Editor
        /// </summary>
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            if (pawn == null || comp == null) return;
            
            // Refresh all stat-dependent values
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
            pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
        }
    }
    
    /// <summary>
    /// Standalone window for editing Isekai stats - opened from Character Editor
    /// </summary>
    public class Window_IsekaiEditor : Window
    {
        private Pawn pawn;
        private IsekaiComponent comp;
        
        public override Vector2 InitialSize => new Vector2(400f, 550f);
        
        public Window_IsekaiEditor(Pawn pawn)
        {
            this.pawn = pawn;
            this.doCloseButton = true;
            this.doCloseX = true;
            this.absorbInputAroundWindow = false;
            this.closeOnClickedOutside = false;
            this.draggable = true;
            
            // Get or create component
            comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null)
            {
                ThingWithComps thing = pawn as ThingWithComps;
                if (thing != null)
                {
                    comp = (IsekaiComponent)Activator.CreateInstance(typeof(IsekaiComponent));
                    thing.AllComps.Add(comp);
                    comp.parent = pawn;
                }
            }
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            if (pawn == null || comp == null)
            {
                Widgets.Label(inRect, "No pawn selected");
                return;
            }
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            
            // Header
            Text.Font = GameFont.Medium;
            listing.Label($"Isekai Stats - {pawn.LabelShortCap}");
            Text.Font = GameFont.Small;
            listing.GapLine();
            
            // Level control
            listing.Label($"Level: {comp.Level}");
            Rect levelSliderRect = listing.GetRect(22f);
            int newLevel = (int)Widgets.HorizontalSlider(levelSliderRect, comp.Level, 1f, 200f, true, $"Level: {comp.Level}");
            if (newLevel != comp.Level)
            {
                CharacterEditorCompatibility.SetLevelDirectly(comp, newLevel);
            }
            
            listing.Gap(10f);
            
            // Available stat points
            listing.Label($"Available Points: {comp.stats.availableStatPoints}");
            listing.Gap(5f);
            
            // Stat controls
            DrawStatRow(listing, "STR (Strength)", IsekaiStatType.Strength);
            DrawStatRow(listing, "DEX (Dexterity)", IsekaiStatType.Dexterity);
            DrawStatRow(listing, "VIT (Vitality)", IsekaiStatType.Vitality);
            DrawStatRow(listing, "INT (Intelligence)", IsekaiStatType.Intelligence);
            DrawStatRow(listing, "WIS (Wisdom)", IsekaiStatType.Wisdom);
            DrawStatRow(listing, "CHA (Charisma)", IsekaiStatType.Charisma);
            
            listing.Gap(15f);
            
            // Buttons
            if (listing.ButtonText("Reset All Stats"))
            {
                comp.stats.strength = 5;
                comp.stats.dexterity = 5;
                comp.stats.vitality = 5;
                comp.stats.intelligence = 5;
                comp.stats.wisdom = 5;
                comp.stats.charisma = 5;
                CharacterEditorCompatibility.RecalculateAvailablePoints(comp);
            }
            
            if (listing.ButtonText("Auto-Distribute Points"))
            {
                CharacterEditorCompatibility.AutoDistributePoints(comp);
            }
            
            listing.End();
        }
        
        private void DrawStatRow(Listing_Standard listing, string label, IsekaiStatType statType)
        {
            int currentValue = comp.stats.GetStat(statType);
            
            Rect rowRect = listing.GetRect(28f);
            Rect labelRect = new Rect(rowRect.x, rowRect.y, 130f, rowRect.height);
            Rect minusRect = new Rect(labelRect.xMax + 5f, rowRect.y, 30f, rowRect.height);
            Rect valueRect = new Rect(minusRect.xMax + 5f, rowRect.y, 50f, rowRect.height);
            Rect plusRect = new Rect(valueRect.xMax + 5f, rowRect.y, 30f, rowRect.height);
            
            Widgets.Label(labelRect, label);
            
            if (Widgets.ButtonText(minusRect, "-") && currentValue > 1)
            {
                comp.stats.SetStat(statType, currentValue - 1);
                CharacterEditorCompatibility.RecalculateAvailablePoints(comp);
            }
            
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(valueRect, currentValue.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            
            if (Widgets.ButtonText(plusRect, "+") && comp.stats.availableStatPoints > 0)
            {
                comp.stats.SetStat(statType, currentValue + 1);
                comp.stats.availableStatPoints--;
            }
        }
    }
}
