using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using IsekaiLeveling.SkillTree;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Compatibility patch for Altered Carbon 2 (ReSleeved).
    /// Ensures Isekai level, XP, stats, and constellation data transfer with cortical stacks.
    ///
    /// How it works:
    /// - NeuralData.CopyFromPawn: AC2 saves a pawn's mind to a stack → we also save Isekai data
    /// - NeuralData.OverwritePawn: AC2 writes a stack's mind onto a new body → we restore Isekai data
    /// - NeuralData.CopyDataFrom: AC2 duplicates stack data → we duplicate Isekai data
    /// - NeuralData.ExposeData: AC2 saves/loads stack → we save/load Isekai data inline
    /// </summary>
    [StaticConstructorOnStartup]
    public static class AlteredCarbonCompat
    {
        private static readonly Type NeuralDataType;

        /// <summary>
        /// Maps NeuralData instances to their stored Isekai progression data.
        /// Entries are created on CopyFromPawn and persisted via ExposeData.
        /// </summary>
        internal static readonly Dictionary<object, IsekaiStackData> stackDataMap 
            = new Dictionary<object, IsekaiStackData>();

        static AlteredCarbonCompat()
        {
            NeuralDataType = AccessTools.TypeByName("AlteredCarbon.NeuralData");
            if (NeuralDataType == null)
            {
                // Altered Carbon 2 not loaded — skip all patches
                return;
            }

            var harmony = new Harmony("IsekaiLeveling.AlteredCarbonCompat");

            // Patch CopyFromPawn(Pawn pawn, ThingDef sourceStack, bool copyRaceGenderInfo, bool canBackupPsychicStuff)
            var copyFromPawn = AccessTools.Method(NeuralDataType, "CopyFromPawn");
            if (copyFromPawn != null)
            {
                harmony.Patch(copyFromPawn,
                    postfix: new HarmonyMethod(typeof(AlteredCarbonCompat), nameof(CopyFromPawn_Postfix)));
            }

            // Patch OverwritePawn(Pawn pawn, bool changeGlobalData)
            var overwritePawn = AccessTools.Method(NeuralDataType, "OverwritePawn");
            if (overwritePawn != null)
            {
                harmony.Patch(overwritePawn,
                    postfix: new HarmonyMethod(typeof(AlteredCarbonCompat), nameof(OverwritePawn_Postfix)));
            }

            // Patch CopyDataFrom(NeuralData other, bool isDuplicateOperation)
            var copyDataFrom = AccessTools.Method(NeuralDataType, "CopyDataFrom");
            if (copyDataFrom != null)
            {
                harmony.Patch(copyDataFrom,
                    postfix: new HarmonyMethod(typeof(AlteredCarbonCompat), nameof(CopyDataFrom_Postfix)));
            }

            // Patch ExposeData() for save/load
            var exposeData = AccessTools.Method(NeuralDataType, "ExposeData");
            if (exposeData != null)
            {
                harmony.Patch(exposeData,
                    postfix: new HarmonyMethod(typeof(AlteredCarbonCompat), nameof(ExposeData_Postfix)));
            }

            Log.Message("[Isekai Leveling] Altered Carbon 2 compatibility patches applied.");
        }

        /// <summary>
        /// After AC2 copies pawn data into a stack, save Isekai data alongside it.
        /// </summary>
        public static void CopyFromPawn_Postfix(object __instance, Pawn pawn)
        {
            try
            {
                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;

                var data = new IsekaiStackData();
                data.CopyFrom(comp);
                stackDataMap[__instance] = data;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai Leveling] AC2 CopyFromPawn error: {ex.Message}");
            }
        }

        /// <summary>
        /// After AC2 writes stack data onto a new pawn (re-sleeving), restore Isekai data.
        /// </summary>
        public static void OverwritePawn_Postfix(object __instance, Pawn pawn)
        {
            try
            {
                if (!stackDataMap.TryGetValue(__instance, out var data)) return;

                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;

                data.ApplyTo(comp);
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai Leveling] AC2 OverwritePawn error: {ex.Message}");
            }
        }

        /// <summary>
        /// After AC2 copies one NeuralData to another (stack duplication), copy Isekai data too.
        /// CopyDataFrom(NeuralData other, bool isDuplicateOperation)
        /// </summary>
        public static void CopyDataFrom_Postfix(object __instance, object other)
        {
            try
            {
                if (other != null && stackDataMap.TryGetValue(other, out var data))
                {
                    stackDataMap[__instance] = data.Clone();
                }
            }
            catch (Exception)
            {
                // Silent — CopyDataFrom may have different signatures across AC2 versions
            }
        }

        /// <summary>
        /// After AC2 saves/loads NeuralData, also save/load Isekai data inline.
        /// Since we're in a Postfix on ExposeData, the Scribe context is still active
        /// and our calls nest cleanly within the NeuralData save block.
        /// </summary>
        public static void ExposeData_Postfix(object __instance)
        {
            try
            {
                IsekaiStackData data = null;

                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    stackDataMap.TryGetValue(__instance, out data);
                }

                Scribe_Deep.Look(ref data, "isekaiLevelingData");

                if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    if (data != null)
                    {
                        stackDataMap[__instance] = data;
                    }
                    else
                    {
                        stackDataMap.Remove(__instance);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai Leveling] AC2 ExposeData error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all state. Called from Game.FinalizeInit to prevent cross-save leaks.
        /// Stack data is re-populated via ExposeData during load, so clearing is safe.
        /// </summary>
        public static void ClearAll()
        {
            stackDataMap.Clear();
        }
    }

    /// <summary>
    /// Serializable snapshot of IsekaiComponent data for cortical stack transfers.
    /// Stores all progression data: level, XP, stat allocation, constellation tree, and titles.
    /// </summary>
    public class IsekaiStackData : IExposable
    {
        // Core leveling
        public int currentLevel = 1;
        public int currentXP = 0;
        public bool statsInitialized = false;

        // Stat allocation
        public int strength = 5;
        public int dexterity = 5;
        public int vitality = 5;
        public int intelligence = 5;
        public int wisdom = 5;
        public int charisma = 5;
        public int availableStatPoints = 0;

        // Passive constellation tree
        public string assignedTree;
        public int availablePoints = 0;
        public int respecCount = 0;
        public List<string> unlockedNodeIds = new List<string>();

        // Titles
        public List<IsekaiTitleDef> earnedTitles = new List<IsekaiTitleDef>();
        public IsekaiTitleDef activeTitle;

        /// <summary>
        /// Copy all Isekai data from a pawn's component.
        /// </summary>
        public void CopyFrom(IsekaiComponent comp)
        {
            currentLevel = comp.currentLevel;
            currentXP = comp.currentXP;
            statsInitialized = comp.statsInitialized;

            if (comp.stats != null)
            {
                strength = comp.stats.strength;
                dexterity = comp.stats.dexterity;
                vitality = comp.stats.vitality;
                intelligence = comp.stats.intelligence;
                wisdom = comp.stats.wisdom;
                charisma = comp.stats.charisma;
                availableStatPoints = comp.stats.availableStatPoints;
            }

            if (comp.passiveTree != null)
            {
                assignedTree = comp.passiveTree.assignedTree;
                availablePoints = comp.passiveTree.availablePoints;
                respecCount = comp.passiveTree.respecCount;
                unlockedNodeIds = comp.passiveTree.unlockedNodeIds != null
                    ? new List<string>(comp.passiveTree.unlockedNodeIds)
                    : new List<string>();
            }

            if (comp.titles != null)
            {
                earnedTitles = comp.titles.earnedTitles != null
                    ? new List<IsekaiTitleDef>(comp.titles.earnedTitles)
                    : new List<IsekaiTitleDef>();
                activeTitle = comp.titles.activeTitle;
            }
        }

        /// <summary>
        /// Apply saved Isekai data onto a pawn's component (after re-sleeving).
        /// </summary>
        public void ApplyTo(IsekaiComponent comp)
        {
            comp.currentLevel = currentLevel;
            comp.currentXP = currentXP;
            comp.statsInitialized = true; // Prevent re-initialization

            if (comp.stats != null)
            {
                comp.stats.strength = strength;
                comp.stats.dexterity = dexterity;
                comp.stats.vitality = vitality;
                comp.stats.intelligence = intelligence;
                comp.stats.wisdom = wisdom;
                comp.stats.charisma = charisma;
                comp.stats.availableStatPoints = availableStatPoints;
            }

            if (comp.passiveTree != null)
            {
                comp.passiveTree.assignedTree = assignedTree;
                comp.passiveTree.availablePoints = availablePoints;
                comp.passiveTree.respecCount = respecCount;
                comp.passiveTree.unlockedNodeIds = unlockedNodeIds != null
                    ? new List<string>(unlockedNodeIds)
                    : new List<string>();
                // Rebuild fast-lookup set from restored node list
                comp.passiveTree.RebuildUnlockedSet();
            }

            if (comp.titles != null)
            {
                comp.titles.earnedTitles = earnedTitles != null
                    ? new List<IsekaiTitleDef>(earnedTitles)
                    : new List<IsekaiTitleDef>();
                comp.titles.activeTitle = activeTitle;
            }

            // Update the pawn's rank trait to match restored level
            PawnStatGenerator.UpdateRankTraitFromStats(comp.Pawn, comp);

            // Invalidate stat caches so bonuses are recalculated
            comp.InvalidateStatCache();
        }

        /// <summary>
        /// Deep clone for stack duplication.
        /// </summary>
        public IsekaiStackData Clone()
        {
            return new IsekaiStackData
            {
                currentLevel = this.currentLevel,
                currentXP = this.currentXP,
                statsInitialized = this.statsInitialized,
                strength = this.strength,
                dexterity = this.dexterity,
                vitality = this.vitality,
                intelligence = this.intelligence,
                wisdom = this.wisdom,
                charisma = this.charisma,
                availableStatPoints = this.availableStatPoints,
                assignedTree = this.assignedTree,
                availablePoints = this.availablePoints,
                respecCount = this.respecCount,
                unlockedNodeIds = this.unlockedNodeIds != null
                    ? new List<string>(this.unlockedNodeIds)
                    : new List<string>(),
                earnedTitles = this.earnedTitles != null
                    ? new List<IsekaiTitleDef>(this.earnedTitles)
                    : new List<IsekaiTitleDef>(),
                activeTitle = this.activeTitle,
            };
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref currentLevel, "currentLevel", 1);
            Scribe_Values.Look(ref currentXP, "currentXP", 0);
            Scribe_Values.Look(ref statsInitialized, "statsInitialized", false);

            Scribe_Values.Look(ref strength, "str", 5);
            Scribe_Values.Look(ref dexterity, "dex", 5);
            Scribe_Values.Look(ref vitality, "vit", 5);
            Scribe_Values.Look(ref intelligence, "intl", 5);
            Scribe_Values.Look(ref wisdom, "wis", 5);
            Scribe_Values.Look(ref charisma, "cha", 5);
            Scribe_Values.Look(ref availableStatPoints, "availableStatPoints", 0);

            Scribe_Values.Look(ref assignedTree, "assignedTree");
            Scribe_Values.Look(ref availablePoints, "availablePoints", 0);
            Scribe_Values.Look(ref respecCount, "respecCount", 0);
            Scribe_Collections.Look(ref unlockedNodeIds, "unlockedNodeIds", LookMode.Value);

            Scribe_Collections.Look(ref earnedTitles, "earnedTitles", LookMode.Def);
            Scribe_Defs.Look(ref activeTitle, "activeTitle");

            // Safety null checks after load
            if (unlockedNodeIds == null)
                unlockedNodeIds = new List<string>();
            if (earnedTitles == null)
                earnedTitles = new List<IsekaiTitleDef>();
            earnedTitles.RemoveAll(t => t == null);
        }
    }
}
