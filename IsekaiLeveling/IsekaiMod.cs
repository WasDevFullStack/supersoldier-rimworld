using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Main mod class for the Isekai Leveling System
    /// </summary>
    public class IsekaiMod : Mod
    {
        public static IsekaiMod Instance { get; private set; }
        public static IsekaiSettings Settings { get; private set; }
        
        // Settings UI state
        private static Vector2 scrollPosition = Vector2.zero;
        private static int currentTab = 0;
        private static readonly string[] tabNames = { "General", "Quests", "Effects", "STR", "DEX", "VIT", "INT", "WIS", "CHA" };

        public IsekaiMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<IsekaiSettings>();
            
            // Apply Harmony patches individually so one failure doesn't block all patches
            var harmony = new Harmony("JellyCreative.IsekaiLeveling");
            int patchedCount = 0;
            int skippedCount = 0;
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            
            foreach (var type in assembly.GetTypes())
            {
                try
                {
                    var patchMethods = HarmonyMethodExtensions.GetFromType(type);
                    if (patchMethods != null && patchMethods.Any())
                    {
                        var processor = harmony.CreateClassProcessor(type);
                        processor.Patch();
                        patchedCount++;
                    }
                }
                catch (System.Exception ex)
                {
                    skippedCount++;
                    Log.Warning($"[Isekai Leveling] Skipped Harmony patch {type.Name}: {ex.Message}");
                }
            }
            
            // Apply world boss visual scaling patch (uses runtime discovery)
            try
            {
                WorldBoss.WorldBossSizePatch.ApplyPatches(harmony);
            }
            catch (System.Exception ex)
            {
                skippedCount++;
                Log.Warning($"[Isekai Leveling] Skipped WorldBoss size patch: {ex.Message}");
            }
            
            if (skippedCount == 0)
                Log.Message($"[Isekai Leveling] Mod initialized successfully! ({patchedCount} patches applied)");
            else
                Log.Warning($"[Isekai Leveling] Mod initialized with {skippedCount} patch(es) skipped. ({patchedCount} applied)");
            
            // Verify critical combat patches are actually applied
            VerifyCriticalPatches(harmony);
        }
        
        /// <summary>
        /// Verify that the most important combat patches are applied correctly.
        /// Logs explicit warnings if any critical patch is missing.
        /// </summary>
        private void VerifyCriticalPatches(Harmony harmony)
        {
            try
            {
                var criticalTargets = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodBase>
                {
                    { "BodyPartDef.GetMaxHealth (creature health)", AccessTools.Method(typeof(BodyPartDef), "GetMaxHealth", new System.Type[] { typeof(Pawn) }) },
                    { "Thing.TakeDamage (creature damage)", AccessTools.Method(typeof(Thing), "TakeDamage") },
                    { "StatWorker.GetValueUnfinalized (stat bonuses)", AccessTools.Method(typeof(StatWorker), "GetValueUnfinalized") },
                    { "HediffSet.BleedRateTotal (bleed reduction)", AccessTools.PropertyGetter(typeof(HediffSet), "BleedRateTotal") },
                };
                
                int verified = 0;
                foreach (var kvp in criticalTargets)
                {
                    if (kvp.Value == null)
                    {
                        Log.Error($"[Isekai Leveling] CRITICAL: Cannot find method {kvp.Key} — patch target does not exist in this RimWorld version!");
                        continue;
                    }
                    
                    var patchInfo = Harmony.GetPatchInfo(kvp.Value);
                    if (patchInfo == null)
                    {
                        Log.Error($"[Isekai Leveling] CRITICAL: {kvp.Key} has NO patches applied at all!");
                        continue;
                    }
                    
                    bool hasOurs = false;
                    foreach (var patch in patchInfo.Postfixes)
                    {
                        if (patch.owner == "JellyCreative.IsekaiLeveling") { hasOurs = true; break; }
                    }
                    if (!hasOurs)
                    {
                        foreach (var patch in patchInfo.Prefixes)
                        {
                            if (patch.owner == "JellyCreative.IsekaiLeveling") { hasOurs = true; break; }
                        }
                    }
                    
                    if (hasOurs)
                    {
                        verified++;
                    }
                    else
                    {
                        Log.Error($"[Isekai Leveling] CRITICAL: {kvp.Key} is patched but our patch is MISSING! Other mods may conflict.");
                    }
                }
                
                Log.Message($"[Isekai Leveling] Critical patch verification: {verified}/{criticalTargets.Count} verified OK");
                
                // Log current settings that affect creature combat
                Log.Message($"[Isekai Leveling] Settings: TamedRetention={Settings?.TamedAnimalBonusRetention ?? -1f}, " +
                    $"HealthMult={Settings?.MobRankHealthMultiplier ?? -1f}, " +
                    $"STR_MeleeDmg={Settings?.STR_MeleeDamage ?? -1f}, " +
                    $"VIT_MaxHP={Settings?.VIT_MaxHealth ?? -1f}, " +
                    $"VIT_DR={Settings?.VIT_DamageReduction ?? -1f}, " +
                    $"ExclAnimals={Settings?.ExcludeAnimalsFromRanking ?? false}");
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Patch verification failed: {ex.Message}");
            }
        }

        public override string SettingsCategory()
        {
            return "Isekai Leveling System";
        }

        private static string GetRankColor(int rankIndex)
        {
            switch (rankIndex)
            {
                case 0: return "#888888"; // F - gray
                case 1: return "#AAAAAA"; // E - light gray
                case 2: return "#55CC55"; // D - green
                case 3: return "#5599FF"; // C - blue
                case 4: return "#AA55FF"; // B - purple
                case 5: return "#FFD700"; // A - gold
                case 6: return "#FF4444"; // S - red
                default: return "#FFFFFF";
            }
        }

        public override void DoSettingsWindowContents(UnityEngine.Rect inRect)
        {
            // Tab bar at top
            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
            float tabWidth = inRect.width / tabNames.Length;
            
            for (int i = 0; i < tabNames.Length; i++)
            {
                Rect buttonRect = new Rect(tabRect.x + i * tabWidth, tabRect.y, tabWidth - 2f, tabRect.height);
                bool selected = currentTab == i;
                
                if (selected)
                    GUI.color = new Color(0.4f, 0.8f, 0.4f);
                
                if (Widgets.ButtonText(buttonRect, tabNames[i]))
                    currentTab = i;
                    
                GUI.color = Color.white;
            }
            
            // Content area below tabs
            Rect contentRect = new Rect(inRect.x, inRect.y + 45f, inRect.width, inRect.height - 45f);
            float contentHeight = GetContentHeight(currentTab);
            Rect viewRect = new Rect(0, 0, contentRect.width - 16f, contentHeight);
            
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);
            
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(new Rect(0, 0, viewRect.width, contentHeight));
            
            switch (currentTab)
            {
                case 0: DrawGeneralSettings(listing); break;
                case 1: DrawQuestsSettings(listing); break;
                case 2: DrawEffectsSettings(listing); break;
                case 3: DrawSTRSettings(listing); break;
                case 4: DrawDEXSettings(listing); break;
                case 5: DrawVITSettings(listing); break;
                case 6: DrawINTSettings(listing); break;
                case 7: DrawWISSettings(listing); break;
                case 8: DrawCHASettings(listing); break;
            }
            
            listing.End();
            Widgets.EndScrollView();
        }
        
        private float GetContentHeight(int tab)
        {
            switch (tab)
            {
                case 0: return 2350f;  // General
                case 1: return 700f;  // Quests (Guild Quests + Elite Mobs + Mod Blacklist)
                case 2: return 450f;  // Effects
                case 3: return 350f;  // STR
                case 4: return 500f;  // DEX
                case 5: return 400f;  // VIT
                case 6: return 400f;  // INT
                case 7: return 800f;  // WIS (11 sliders — PsyfocusCost was offscreen at 500)
                case 8: return 400f;  // CHA
                default: return 400f;
            }
        }
        
        private void DrawGeneralSettings(Listing_Standard listing)
        {
            // Restart warning
            GUI.color = new Color(1f, 0.8f, 0.5f);
            listing.Label("Isekai_Settings_RestartWarning".Translate());
            GUI.color = Color.white;
            listing.Gap();
            
            listing.Label("<b>" + "Isekai_Settings_XPLevelingHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.Label("Isekai_Settings_XPMultiplier".Translate() + ": " + Settings.XPMultiplier.ToString("F1") + "x  <color=#888888>(Default: 3.0x)</color>");
            Settings.XPMultiplier = listing.Slider(Settings.XPMultiplier, 0.1f, 5f);
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_SkillPointsPerLevel".Translate() + ": " + Settings.SkillPointsPerLevel + "  <color=#888888>(Default: 1)</color>");
            Settings.SkillPointsPerLevel = (int)listing.Slider(Settings.SkillPointsPerLevel, 1, 5);
            
            listing.Gap();
            
            listing.CheckboxLabeled("Isekai_Settings_EnableXPNotifications".Translate(), ref Settings.EnableXPNotifications, 
                "Isekai_Settings_EnableXPNotifications_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_EnableLevelUpNotifications".Translate(), ref Settings.EnableLevelUpNotifications, 
                "Isekai_Settings_EnableLevelUpNotifications_Desc".Translate());
            
            if (Settings.EnableLevelUpNotifications)
            {
                listing.CheckboxLabeled("Isekai_Settings_UseLevelUpNotice".Translate(), ref Settings.UseLevelUpNotice, 
                    "Isekai_Settings_UseLevelUpNotice_Desc".Translate());
            }
            
            listing.Gap();
            
            // Max Level with text field for precise input
            listing.Label("Isekai_Settings_MaxLevel".Translate() + ": " + Settings.MaxLevel + "  <color=#888888>(Default: 9999)</color>");
            Rect maxLevelRect = listing.GetRect(30f);
            Rect sliderRect = new Rect(maxLevelRect.x, maxLevelRect.y, maxLevelRect.width * 0.75f, maxLevelRect.height);
            Rect textFieldRect = new Rect(maxLevelRect.x + maxLevelRect.width * 0.77f, maxLevelRect.y, maxLevelRect.width * 0.23f, maxLevelRect.height);
            
            Settings.MaxLevel = (int)Widgets.HorizontalSlider(sliderRect, Settings.MaxLevel, 10, 9999, true);
            
            string buffer = Settings.MaxLevel.ToString();
            string edited = Widgets.TextField(textFieldRect, buffer);
            if (int.TryParse(edited, out int parsed))
            {
                Settings.MaxLevel = Mathf.Clamp(parsed, 10, 9999);
            }
            
            listing.Gap();
            
            // Max Stat Cap with text field for precise input
            string statCapLabel = Settings.MaxStatCap >= 9999 
                ? "Isekai_Settings_MaxStatCap_Unlimited".Translate().ToString()
                : Settings.MaxStatCap.ToString();
            listing.Label("Isekai_Settings_MaxStatCap".Translate() + ": " + statCapLabel + "  <color=#888888>(Default: 9999)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_MaxStatCap_Desc".Translate() + "</color>");
            Rect maxStatRect = listing.GetRect(30f);
            Rect statSliderRect = new Rect(maxStatRect.x, maxStatRect.y, maxStatRect.width * 0.75f, maxStatRect.height);
            Rect statTextRect = new Rect(maxStatRect.x + maxStatRect.width * 0.77f, maxStatRect.y, maxStatRect.width * 0.23f, maxStatRect.height);
            
            Settings.MaxStatCap = (int)Widgets.HorizontalSlider(statSliderRect, Settings.MaxStatCap, 10, 9999, true);
            
            string statBuffer = Settings.MaxStatCap.ToString();
            string statEdited = Widgets.TextField(statTextRect, statBuffer);
            if (int.TryParse(statEdited, out int statParsed))
            {
                Settings.MaxStatCap = Mathf.Clamp(statParsed, 10, 9999);
            }
            
            listing.Gap();
            
            // Starting Pawn Level
            string startLevelLabel = Settings.StartingPawnLevel == 0 
                ? "Isekai_Settings_StartingLevel_Random".Translate().ToString()
                : Settings.StartingPawnLevel.ToString();
            listing.Label("Isekai_Settings_StartingLevel".Translate() + ": " + startLevelLabel + "  <color=#888888>(Default: Random)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_StartingLevel_Desc".Translate() + "</color>");
            Settings.StartingPawnLevel = (int)listing.Slider(Settings.StartingPawnLevel, 0, 100);
            
            listing.CheckboxLabeled("Isekai_Settings_NewbornsUseRankRolling".Translate(), ref Settings.NewbornsUseRankRolling, 
                "Isekai_Settings_NewbornsUseRankRolling_Desc".Translate());
            
            listing.Gap();
            
            listing.Label("<b>" + "Isekai_Settings_MobRankingHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.CheckboxLabeled("Isekai_Settings_ShowMobRanks".Translate(), ref Settings.ShowMobRanks, 
                "Isekai_Settings_ShowMobRanks_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_ExcludeAnimals".Translate(), ref Settings.ExcludeAnimalsFromRanking, 
                "Isekai_Settings_ExcludeAnimals_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_ExcludeMechs".Translate(), ref Settings.ExcludeMechsFromRanking, 
                "Isekai_Settings_ExcludeMechs_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_ExcludeEntities".Translate(), ref Settings.ExcludeEntitiesFromRanking, 
                "Isekai_Settings_ExcludeEntities_Desc".Translate());
            
            listing.Gap();
            
            string retentionLabel = Settings.TamedAnimalBonusRetention <= 0f 
                ? "Isekai_Settings_TamedBonusRetention_None".Translate().ToString()
                : (Settings.TamedAnimalBonusRetention * 100f).ToString("F0") + "%";
            listing.Label("Isekai_Settings_TamedBonusRetention".Translate() + ": " + retentionLabel + "  <color=#888888>(Default: 50%)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_TamedBonusRetention_Desc".Translate() + "</color>");
            Settings.TamedAnimalBonusRetention = listing.Slider(Settings.TamedAnimalBonusRetention, 0.0f, 1.0f);
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_MobSpeedMultiplier".Translate() + ": " + Settings.MobRankSpeedMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_MobSpeedMultiplier_Desc".Translate() + "</color>");
            Settings.MobRankSpeedMultiplier = listing.Slider(Settings.MobRankSpeedMultiplier, 0.0f, 2.0f);
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_MobArmorMultiplier".Translate() + ": " + Settings.MobRankArmorMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_MobArmorMultiplier_Desc".Translate() + "</color>");
            Settings.MobRankArmorMultiplier = listing.Slider(Settings.MobRankArmorMultiplier, 0.0f, 2.0f);
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_MobHealthMultiplier".Translate() + ": " + Settings.MobRankHealthMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_MobHealthMultiplier_Desc".Translate() + "</color>");
            Settings.MobRankHealthMultiplier = listing.Slider(Settings.MobRankHealthMultiplier, 0.0f, 2.0f);
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_MobRankValueMultiplier".Translate() + ": " + Settings.MobRankValueMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_MobRankValueMultiplier_Desc".Translate() + "</color>");
            Settings.MobRankValueMultiplier = listing.Slider(Settings.MobRankValueMultiplier, 0.0f, 2.0f);
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_PawnLevelValueMultiplier".Translate() + ": " + Settings.PawnLevelValueMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_PawnLevelValueMultiplier_Desc".Translate() + "</color>");
            Settings.PawnLevelValueMultiplier = listing.Slider(Settings.PawnLevelValueMultiplier, 0.0f, 2.0f);
            
            listing.Gap();
            listing.Gap();
            
            listing.Label("<b>" + "Isekai_Settings_RaidEnemyRanksHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.CheckboxLabeled("Isekai_Settings_EnableRaidRanking".Translate(), ref Settings.EnableRaidRanking, 
                "Isekai_Settings_EnableRaidRanking_Desc".Translate());
            
            listing.Label("Isekai_Settings_RaidPointsMultiplier".Translate() + ": " + Settings.RaidRankPointsMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_RaidPointsMultiplier_Desc".Translate() + "</color>");
            Settings.RaidRankPointsMultiplier = listing.Slider(Settings.RaidRankPointsMultiplier, 0.1f, 2.0f);
            
            // Max Raid Rank cap slider
            string[] rankNames = { "F", "E", "D", "C", "B", "A", "S" };
            int rankIndex = Mathf.Clamp(Settings.MaxRaidRank, 0, rankNames.Length - 1);
            string rankColor = GetRankColor(rankIndex);
            listing.Label("Isekai_Settings_MaxRaidRank".Translate() + ": <color=" + rankColor + "><b>" + rankNames[rankIndex] + "</b></color>  <color=#888888>(Default: S)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_MaxRaidRank_Desc".Translate() + "</color>");
            Settings.MaxRaidRank = (int)listing.Slider(Settings.MaxRaidRank, 0, 6);
            
            listing.CheckboxLabeled("Isekai_Settings_ClassicRNGRaids".Translate(), ref Settings.ClassicRNGRaids, 
                "Isekai_Settings_ClassicRNGRaids_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_AdaptiveRaidRanks".Translate(), ref Settings.AdaptiveRaidRanks, 
                "Isekai_Settings_AdaptiveRaidRanks_Desc".Translate());
            
            listing.Gap();
            listing.Gap();
            
            listing.Label("<b>" + "Isekai_Settings_ConstellationHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.Label("Isekai_Settings_ConstellationBonusMultiplier".Translate() + ": " + Settings.ConstellationBonusMultiplier.ToString("F2") + "x  <color=#888888>(Default: 1.00x)</color>");
            listing.Label("<color=#888888>" + "Isekai_Settings_ConstellationBonusMultiplier_Desc".Translate() + "</color>");
            Settings.ConstellationBonusMultiplier = listing.Slider(Settings.ConstellationBonusMultiplier, 0.25f, 3.0f);
            
            listing.Gap();
            listing.Gap();
            
            // Mechanitor XP Settings (only show if Biotech is active)
            if (ModsConfig.BiotechActive)
            {
                listing.Label("<b>" + "Isekai_Settings_MechanitorXPHeader".Translate() + "</b>");
                listing.GapLine();
                
                listing.CheckboxLabeled("Isekai_Settings_EnableMechanitorXP".Translate(), ref Settings.EnableMechanitorXP, 
                    "Isekai_Settings_EnableMechanitorXP_Desc".Translate());
                listing.CheckboxLabeled("Isekai_Settings_EnableMechCreatureXP".Translate(), ref Settings.EnableMechCreatureXP, 
                    "Isekai_Settings_EnableMechCreatureXP_Desc".Translate());
                
                listing.Gap();
                listing.Gap();
            }
            
            // Cross-map XP settings
            listing.Label("<b>" + "Isekai_Settings_CrossMapXPHeader".Translate() + "</b>");
            listing.GapLine();
            listing.CheckboxLabeled("Isekai_Settings_ShareKillXPAcrossMaps".Translate(), ref Settings.ShareKillXPAcrossMaps,
                "Isekai_Settings_ShareKillXPAcrossMaps_Desc".Translate());
            listing.Gap();
            listing.Gap();
            
            if (listing.ButtonText("Isekai_Settings_ResetStatMultipliers".Translate()))
            {
                Settings.ResetStatMultipliers();
            }
            
            listing.Gap();
            listing.Gap();
            
            listing.Label("<b>Forge System</b>");
            listing.GapLine();
            
            listing.CheckboxLabeled("Enable Forge System", ref Settings.EnableForgeSystem, 
                "Enable weapon/armor refinement, mastery, and rune socketing.");
            
            if (Settings.EnableForgeSystem)
            {
                listing.Label("Refinement Success Multiplier: " + Settings.RefinementSuccessMultiplier.ToString("F2") + "x  <color=#888888>(Default: 1.00x)</color>");
                Settings.RefinementSuccessMultiplier = listing.Slider(Settings.RefinementSuccessMultiplier, 0.5f, 2.0f);
                
                listing.Gap();
                
                listing.CheckboxLabeled("Enable Weapon Mastery", ref Settings.EnableWeaponMastery, 
                    "Enable per-weapon-type mastery XP and stat bonuses from combat use.");
                
                if (Settings.EnableWeaponMastery)
                {
                    listing.Label("Mastery XP Multiplier: " + Settings.MasteryXPMultiplier.ToString("F2") + "x  <color=#888888>(Default: 1.00x)</color>");
                    Settings.MasteryXPMultiplier = listing.Slider(Settings.MasteryXPMultiplier, 0.5f, 3.0f);
                }
            }
        }
        
        private void DrawQuestsSettings(Listing_Standard listing)
        {
            // Guild Quest Settings
            listing.Label("<b>" + "Isekai_Settings_GuildQuestsHeader".Translate() + "</b>");
            listing.GapLine();
            
            bool prevEnableQuests = Settings.EnableGuildQuests;
            float prevFrequency = Settings.GuildQuestFrequency;
            
            listing.CheckboxLabeled("Isekai_Settings_EnableGuildQuests".Translate(), ref Settings.EnableGuildQuests, 
                "Isekai_Settings_EnableGuildQuests_Desc".Translate());
            
            if (Settings.EnableGuildQuests)
            {
                string freqLabel = Settings.GuildQuestFrequency < 1.0f 
                    ? "Isekai_Settings_GuildQuestFrequency_PerDay".Translate((1f / Settings.GuildQuestFrequency).ToString("F1")).ToString()
                    : "Isekai_Settings_GuildQuestFrequency_Days".Translate(Settings.GuildQuestFrequency.ToString("F1")).ToString();
                listing.Label("Isekai_Settings_GuildQuestFrequency".Translate() + ": " + freqLabel + "  <color=#888888>(Default: 1.0 day)</color>");
                listing.Label("<color=#888888>" + "Isekai_Settings_GuildQuestFrequency_Desc".Translate() + "</color>");
                Settings.GuildQuestFrequency = listing.Slider(Settings.GuildQuestFrequency, 0.25f, 7.0f);
                
                listing.Gap();
                
                // Minimum Quest Rank slider
                string[] questRankNames = { "F", "E", "D", "C", "B", "A", "S", "SS", "SSS" };
                int minRankIdx = Mathf.Clamp(Settings.MinQuestRank, 0, questRankNames.Length - 1);
                string minRankColor = GetRankColor(Mathf.Min(minRankIdx, 6));
                string minRankLabel = Settings.MinQuestRank == 0
                    ? "Isekai_Settings_MinQuestRank_All".Translate().ToString()
                    : questRankNames[minRankIdx] + "+";
                listing.Label("Isekai_Settings_MinQuestRank".Translate() + ": <color=" + minRankColor + "><b>" + minRankLabel + "</b></color>  <color=#888888>(Default: All)</color>");
                listing.Label("<color=#888888>" + "Isekai_Settings_MinQuestRank_Desc".Translate() + "</color>");
                Settings.MinQuestRank = (int)listing.Slider(Settings.MinQuestRank, 0, 8);
            }
            
            // Apply settings immediately when changed
            if (prevEnableQuests != Settings.EnableGuildQuests || prevFrequency != Settings.GuildQuestFrequency)
            {
                Quests.GuildQuestSettings.ApplySettings();
            }
            
            listing.Gap();
            listing.Gap();
            
            // Elite Mob Settings
            listing.Label("<b>" + "Isekai_Settings_EliteMobsHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.CheckboxLabeled("Isekai_Settings_ShowEliteMobTitles".Translate(), ref Settings.ShowEliteMobTitles, 
                "Isekai_Settings_ShowEliteMobTitles_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_EnableTraitColors".Translate(), ref Settings.EnableTraitColors, 
                "Isekai_Settings_EnableTraitColors_Desc".Translate());
            
            listing.Gap();
            listing.Gap();
            
            // Quest Creature Mod Blacklist
            listing.Label("<b>" + "Isekai_Settings_QuestBlacklistHeader".Translate() + "</b>");
            listing.GapLine();
            listing.Label("Isekai_Settings_QuestBlacklistDesc".Translate());
            listing.Label("<color=#888888>" + "Isekai_Settings_QuestBlacklistHint".Translate() + "</color>");
            listing.Gap(4f);
            
            Rect textRect = listing.GetRect(30f);
            Settings.QuestCreatureModBlacklist = Widgets.TextField(textRect, Settings.QuestCreatureModBlacklist ?? "");
            
            listing.Gap(4f);
            listing.Label("<color=#666666>" + "Isekai_Settings_QuestBlacklistBuiltIn".Translate() + "</color>");
        }
        
        private void DrawEffectsSettings(Listing_Standard listing)
        {
            // UI Style
            listing.Label("<b>" + "Isekai_Settings_UIStyleHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.CheckboxLabeled("Isekai_Settings_UseIsekaiUI".Translate(), ref Settings.UseIsekaiUI, 
                "Isekai_Settings_UseIsekaiUI_Desc".Translate());
            
            listing.Gap();
            listing.Gap();
            
            listing.Label("<b>" + "Isekai_Settings_VisualEffectsHeader".Translate() + "</b>");
            listing.GapLine();
            
            listing.CheckboxLabeled("Isekai_Settings_EnableManaCoreDrops".Translate(), ref Settings.EnableManaCoreDrops, 
                "Isekai_Settings_EnableManaCoreDrops_Desc".Translate());

            listing.CheckboxLabeled("Isekai_Settings_EnableStarFragmentDrops".Translate(), ref Settings.EnableStarFragmentDrops,
                "Isekai_Settings_EnableStarFragmentDrops_Desc".Translate());
            
            listing.CheckboxLabeled("Isekai_Settings_AutoAllowCrystalHauling".Translate(), ref Settings.AutoAllowCrystalHauling, 
                "Isekai_Settings_AutoAllowCrystalHauling_Desc".Translate());
            
            listing.Gap();
            listing.Gap();
            
            listing.Label("<b>" + "Isekai_Settings_DraftedAuraHeader".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_DraftedAuraSubtext".Translate() + "</color>");
            listing.Gap();
            
            listing.CheckboxLabeled("Isekai_Settings_EnableDraftedAura".Translate(), ref Settings.EnableDraftedAura, 
                "Isekai_Settings_EnableDraftedAura_Desc".Translate());
            
            listing.Gap();
            
            listing.Label("Isekai_Settings_AuraOpacity".Translate() + ": " + ((int)(Settings.AuraOpacity * 100)) + "%  <color=#888888>(Default: 25%)</color>");
            Settings.AuraOpacity = listing.Slider(Settings.AuraOpacity, 0.1f, 1f);
            
            listing.Label("Isekai_Settings_AuraSizeMultiplier".Translate() + ": " + Settings.AuraSizeMultiplier.ToString("F1") + "x  <color=#888888>(Default: 1.2x)</color>");
            Settings.AuraSizeMultiplier = listing.Slider(Settings.AuraSizeMultiplier, 0.5f, 2f);
            
            listing.Gap();
            
            listing.CheckboxLabeled("Isekai_Settings_EnableAuraPulse".Translate(), ref Settings.EnableAuraPulse, 
                "Isekai_Settings_EnableAuraPulse_Desc".Translate());
            
            listing.Label("Isekai_Settings_AuraPulseSpeed".Translate() + ": " + Settings.AuraPulseSpeed.ToString("F1") + "x  <color=#888888>(Default: 1.0x)</color>");
            Settings.AuraPulseSpeed = listing.Slider(Settings.AuraPulseSpeed, 0.5f, 3f);
            
            listing.Gap();
            
            listing.CheckboxLabeled("Isekai_Settings_AuraUseFavoriteColor".Translate(), ref Settings.AuraUseFavoriteColor, 
                "Isekai_Settings_AuraUseFavoriteColor_Desc".Translate());
            
            listing.Gap();
            listing.Gap();
            
            // Window position reset (helpful for small monitors)
            listing.Label("<b>" + "Isekai_Settings_WindowPositionHeader".Translate() + "</b>");
            listing.GapLine();
            listing.Label("<color=#888888>" + "Isekai_Settings_WindowPositionDesc".Translate() + "</color>");
            listing.Gap();
            
            if (listing.ButtonText("Isekai_Settings_ResetWindowPosition".Translate()))
            {
                Settings.StatsWindowX = -1f;
                Settings.StatsWindowY = -1f;
                Messages.Message("Isekai_Settings_WindowPositionReset".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
        }
        
        private void DrawSTRSettings(Listing_Standard listing)
        {
            listing.Label("<b>" + "Isekai_Settings_STRMultipliers".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_BonusPerPoint".Translate() + "</color>");
            listing.GapLine();
            
            DrawMultiplierSlider(listing, "Isekai_Settings_MeleeDamage".Translate(), ref Settings.STR_MeleeDamage, 0f, 0.30f, 0.02f);
            DrawMultiplierSlider(listing, "Isekai_Settings_CarryCapacity".Translate(), ref Settings.STR_CarryCapacity, 0f, 0.30f, 0.02f);
            DrawMultiplierSlider(listing, "Isekai_Settings_MiningSpeed".Translate(), ref Settings.STR_MiningSpeed, 0f, 0.30f, 0.02f);
        }
        
        private void DrawDEXSettings(Listing_Standard listing)
        {
            listing.Label("<b>" + "Isekai_Settings_DEXMultipliers".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_BonusPerPoint".Translate() + "</color>");
            listing.GapLine();
            
            DrawMultiplierSlider(listing, "Isekai_Settings_MoveSpeed".Translate(), ref Settings.DEX_MoveSpeed, 0f, 0.10f, 0.03f);
            DrawMultiplierSlider(listing, "Isekai_Settings_MeleeHitChance".Translate(), ref Settings.DEX_MeleeHitChance, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_MeleeDodgeChance".Translate(), ref Settings.DEX_MeleeDodge, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_ShootingAccuracy".Translate(), ref Settings.DEX_ShootingAccuracy, 0f, 0.30f, 0.06f);
            DrawMultiplierSlider(listing, "Isekai_Settings_RangedAttackSpeed".Translate(), ref Settings.DEX_AimingTime, 0f, 0.10f, 0.02f);
        }
        
        private void DrawVITSettings(Listing_Standard listing)
        {
            listing.Label("<b>" + "Isekai_Settings_VITMultipliers".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_BonusPerPoint".Translate() + "</color>");
            listing.GapLine();
            
            DrawMultiplierSlider(listing, "Isekai_Settings_MaxHealth".Translate(), ref Settings.VIT_MaxHealth, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_HealthRegenFactor".Translate(), ref Settings.VIT_HealthRegen, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_ToxicResistance".Translate(), ref Settings.VIT_ToxicResist, 0f, 0.10f, 0.03f);
            DrawMultiplierSlider(listing, "Isekai_Settings_ImmunityGainSpeed".Translate(), ref Settings.VIT_ImmunityGain, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_DamageReduction".Translate(), ref Settings.VIT_DamageReduction, 0f, 0.04f, 0.01f);
            DrawMultiplierSlider(listing, "Isekai_Settings_LifespanFactor".Translate(), ref Settings.VIT_LifespanFactor, 0f, 0.05f, 0.01f);
        }
        
        private void DrawINTSettings(Listing_Standard listing)
        {
            listing.Label("<b>" + "Isekai_Settings_INTMultipliers".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_BonusPerPoint".Translate() + "</color>");
            listing.GapLine();
            
            DrawMultiplierSlider(listing, "Isekai_Settings_GeneralWorkSpeed".Translate(), ref Settings.INT_WorkSpeed, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_ResearchSpeed".Translate(), ref Settings.INT_ResearchSpeed, 0f, 0.20f, 0.06f);
            DrawMultiplierSlider(listing, "Isekai_Settings_LearningSpeed".Translate(), ref Settings.INT_LearningSpeed, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_CraftingQuality".Translate(), ref Settings.INT_CraftingQuality, 0f, 0.04f, 0.01f);
        }
        
        private void DrawWISSettings(Listing_Standard listing)
        {
            listing.Label("<b>" + "Isekai_Settings_WISMultipliers".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_BonusPerPoint".Translate() + "</color>");
            listing.GapLine();
            
            DrawMultiplierSlider(listing, "Isekai_Settings_MentalBreakThreshold".Translate(), ref Settings.WIS_MentalBreak, 0f, 0.10f, 0.03f);
            DrawMultiplierSlider(listing, "Isekai_Settings_MeditationFocus".Translate(), ref Settings.WIS_MeditationFocus, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_MedicalTendQuality".Translate(), ref Settings.WIS_MedicalTendQuality, 0f, 0.10f, 0.02f);
            DrawMultiplierSlider(listing, "Isekai_Settings_SurgerySuccess".Translate(), ref Settings.WIS_SurgerySuccess, 0f, 0.10f, 0.03f);
            DrawMultiplierSlider(listing, "Isekai_Settings_TrainAnimal".Translate(), ref Settings.WIS_TrainAnimal, 0f, 0.10f, 0.03f);
            DrawMultiplierSlider(listing, "Isekai_Settings_AnimalGatherYield".Translate(), ref Settings.WIS_AnimalGatherYield, 0f, 0.10f, 0.02f);
            DrawMultiplierSlider(listing, "Isekai_Settings_PsychicSensitivity".Translate(), ref Settings.WIS_PsychicSensitivity, 0f, 0.05f, 0.01f);
            
            // Neural Heat Limit uses a flat offset per point, not a percentage
            float neuralPct = Settings.WIS_NeuralHeatLimit;
            string neuralDefaultStr = $"  <color=#888888>(Default: {1.5f:F1})</color>";
            listing.Label($"{"Isekai_Settings_NeuralHeatLimit".Translate()}: +{neuralPct:F1} per point{neuralDefaultStr}");
            Settings.WIS_NeuralHeatLimit = listing.Slider(Settings.WIS_NeuralHeatLimit, 0f, 5f);
            
            DrawMultiplierSlider(listing, "Isekai_Settings_NeuralHeatRecovery".Translate(), ref Settings.WIS_NeuralHeatRecovery, 0f, 0.05f, 0.015f);
            DrawMultiplierSlider(listing, "Isekai_Settings_PsyfocusCost".Translate(), ref Settings.WIS_PsyfocusCost, 0f, 0.03f, 0.008f);
        }
        
        private void DrawCHASettings(Listing_Standard listing)
        {
            listing.Label("<b>" + "Isekai_Settings_CHAMultipliers".Translate() + "</b>");
            listing.Label("<color=#888888>" + "Isekai_Settings_BonusPerPoint".Translate() + "</color>");
            listing.GapLine();
            
            DrawMultiplierSlider(listing, "Isekai_Settings_TradePriceBonus".Translate(), ref Settings.CHA_TradePrice, 0f, 0.10f, 0.02f);
            DrawMultiplierSlider(listing, "Isekai_Settings_SocialImpact".Translate(), ref Settings.CHA_SocialImpact, 0f, 0.20f, 0.05f);
            DrawMultiplierSlider(listing, "Isekai_Settings_NegotiationAbility".Translate(), ref Settings.CHA_NegotiationAbility, 0f, 0.20f, 0.04f);
            DrawMultiplierSlider(listing, "Isekai_Settings_ArrestSuccess".Translate(), ref Settings.CHA_ArrestSuccess, 0f, 0.20f, 0.04f);
        }
        
        // Buffers for text input fields keyed by label string
        private Dictionary<string, string> multiplierTextBuffers = new Dictionary<string, string>();

        private void DrawMultiplierSlider(Listing_Standard listing, string label, ref float value, float min, float max, float defaultValue = -1f)
        {
            float pct = value * 100f;
            string defaultStr = defaultValue >= 0f ? $"  <color=#888888>(Default: {defaultValue * 100f:F1}%)</color>" : "";
            listing.Label($"{label}: +{pct:F1}% per point{defaultStr}");

            // Layout: slider on the left, text input on the right
            Rect fullRect = listing.GetRect(22f);
            float textBoxWidth = 60f;
            float gap = 8f;
            Rect sliderRect = new Rect(fullRect.x, fullRect.y, fullRect.width - textBoxWidth - gap, fullRect.height);
            Rect textRect = new Rect(sliderRect.xMax + gap, fullRect.y, textBoxWidth, fullRect.height);

            // Draw slider
            float sliderVal = Widgets.HorizontalSlider(sliderRect, value, min, max);
            if (sliderVal != value)
            {
                value = sliderVal;
                // Sync text buffer when slider moves
                multiplierTextBuffers[label] = (value * 100f).ToString("F1");
            }

            // Draw text input (in percentage, e.g. "2.0" means 0.02)
            if (!multiplierTextBuffers.ContainsKey(label))
                multiplierTextBuffers[label] = (value * 100f).ToString("F1");

            string buffer = multiplierTextBuffers[label];
            string newBuffer = Widgets.TextField(textRect, buffer);
            if (newBuffer != buffer)
            {
                multiplierTextBuffers[label] = newBuffer;
                if (float.TryParse(newBuffer, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    float clamped = Mathf.Clamp(parsed / 100f, min, max);
                    value = clamped;
                }
            }
        }
    }

    /// <summary>
    /// Mod settings that persist between saves
    /// </summary>
    public class IsekaiSettings : ModSettings
    {
        // Core settings
        public float XPMultiplier = 3.0f;
        public int SkillPointsPerLevel = 1;
        public bool EnableXPNotifications = true;
        public bool EnableLevelUpNotifications = true;
        public bool UseLevelUpNotice = false;  // Use notice (corner popup) instead of letter for level ups
        public int MaxLevel = 9999;
        public int MaxStatCap = 9999;  // Maximum value for any stat (0 = unlimited)
        
        // Mob Ranking settings
        public bool ShowMobRanks = true;
        public float MobRankSpeedMultiplier = 1.0f;  // Global multiplier for mob rank speed bonuses (0-2x)
        public float MobRankArmorMultiplier = 1.0f;  // Global multiplier for mob rank armor bonuses (0-2x)
        public float MobRankHealthMultiplier = 1.0f;  // Global multiplier for mob rank health bonuses (0-2x)
        public float MobRankValueMultiplier = 1.0f; // Multiplier for mob rank market value (0-2x)
        public float PawnLevelValueMultiplier = 1.0f; // Multiplier for pawn level market value (0-2x)
        public bool ExcludeAnimalsFromRanking = false; // Don't apply mob ranks to animals
        public bool ExcludeMechsFromRanking = false; // Don't apply mob ranks to mechs
        public bool ExcludeEntitiesFromRanking = false; // Don't apply mob ranks to Anomaly entities
        public float TamedAnimalBonusRetention = 0.5f; // How much rank bonus tamed animals keep (0=none, 0.5=half, 1=full)
        
        // Starting Pawn Level (for scenario preparation / Character Editor compatibility)
        public int StartingPawnLevel = 0;  // Level for newly generated colonists (0 = use random rank)
        public bool NewbornsUseRankRolling = false; // When true, newborns use normal rank RNG instead of starting at level 1
        
        // Raid Rank settings
        public bool EnableRaidRanking = true;
        public float RaidRankPointsMultiplier = 1.0f;
        public bool RaidRankCapAtS = true;  // Legacy - kept for save compat, mapped from MaxRaidRank
        public int MaxRaidRank = 6;  // 0=F, 1=E, 2=D, 3=C, 4=B, 5=A, 6=S (default S)
        public bool ClassicRNGRaids = false;  // Use classic RNG variance (more chaotic raids)
        public bool AdaptiveRaidRanks = false;  // Clamp raid enemy ranks to ±1 of colony's best pawn rank
        
        // Mechanitor XP settings (Biotech DLC)
        public bool EnableMechanitorXP = true;  // Mechanitors get XP when their mechs kill enemies
        public bool EnableMechCreatureXP = true;  // Mechs gain creature rank XP (combat, kills, quests)
        
        // Cross-map XP settings
        public bool ShareKillXPAcrossMaps = false;  // When false, kill XP only goes to pawns on the same map as the victim
        
        // Guild Quest settings
        public bool EnableGuildQuests = true;  // Enable daily guild quests
        public float GuildQuestFrequency = 1.0f;  // Quest frequency in days (0.5 = twice per day, 2.0 = every 2 days)
        public int MinQuestRank = 0;  // Minimum quest rank to generate (0=F, 1=E, 2=D, ... 8=SSS)
        
        // Elite Mob settings
        public bool ShowEliteMobTitles = true;  // Show elite mob titles above their heads
        
        // UI style settings
        public bool UseIsekaiUI = true;  // Use custom themed UI (false = vanilla RimWorld UI style)
        
        // Trait display settings
        public bool EnableTraitColors = true;  // Color isekai trait labels by rarity in the character card

        // Quest creature mod blacklist (comma-separated keywords matched against mod package ID and name)
        public string QuestCreatureModBlacklist = "";

        // Forge System settings
        public bool EnableForgeSystem = true;  // Master toggle for the forge refinement system
        public float RefinementSuccessMultiplier = 1.0f;  // Multiplier for refinement success chance (0.5-2.0x)
        public bool EnableWeaponMastery = true;  // Enable weapon type mastery from combat
        public float MasteryXPMultiplier = 1.0f;  // Multiplier for mastery XP gain (0.5-3.0x)
        
        // Constellation settings
        public float ConstellationBonusMultiplier = 1.0f;  // Global multiplier for constellation node stat bonuses (0.25-3.0x)
        
        // Window position persistence (for small monitors)
        public float StatsWindowX = -1f;  // -1 means use default centered position
        public float StatsWindowY = -1f;
        
        // Aura/Effects settings
        public bool EnableDraftedAura = true;
        public float AuraOpacity = 0.25f;
        public float AuraSizeMultiplier = 1.2f;
        public bool EnableAuraParticles = true;
        public float AuraParticleRate = 1f;
        public bool EnableAuraPulse = true;
        public float AuraPulseSpeed = 1f;
        public bool AuraUseFavoriteColor = true; // Use pawn's favorite color instead of rank-based color
        
        // Loot settings
        public bool EnableManaCoreDrops = true;
        public bool EnableStarFragmentDrops = true;
        public bool AutoAllowCrystalHauling = false;  // If true, mana cores and star fragments spawn allowed for hauling
        
        // === STRENGTH Multipliers (per point above base 5) ===
        public float STR_MeleeDamage = 0.02f;           // +2% melee damage per point
        public float STR_CarryCapacity = 0.02f;         // +2% carry capacity per point
        public float STR_MiningSpeed = 0.02f;           // +2% mining speed per point
        
        // === DEXTERITY Multipliers ===
        public float DEX_MoveSpeed = 0.03f;             // +3% move speed per point
        public float DEX_MeleeHitChance = 0.04f;        // +4% melee hit chance per point
        public float DEX_MeleeDodge = 0.04f;            // +4% melee dodge per point
        public float DEX_AimingTime = 0.02f;            // -2% aiming time per point
        public float DEX_ShootingAccuracy = 0.06f;      // +6% shooting accuracy per point
        
        // === VITALITY Multipliers ===
        public float VIT_MaxHealth = 0.04f;             // +4% max health per point
        public float VIT_HealthRegen = 0.04f;           // +4% health regen per point
        public float VIT_ToxicResist = 0.03f;           // +3% toxic resistance per point
        public float VIT_DamageReduction = 0.01f;       // +1% damage reduction per point (adjustable, can be OP)
        public float VIT_ImmunityGain = 0.04f;          // +4% immunity gain speed per point
        public float VIT_LifespanFactor = 0.01f;         // +1% lifespan per point (VIT 100 = ~2x lifespan)
        
        // === INTELLIGENCE Multipliers ===
        public float INT_WorkSpeed = 0.04f;             // +4% general work speed per point
        public float INT_ResearchSpeed = 0.06f;         // +6% research speed per point
        public float INT_LearningSpeed = 0.04f;         // +4% learning speed per point
        public float INT_CraftingQuality = 0.01f;       // +1% chance per point to boost crafting quality by 1 tier
        
        // === WISDOM Multipliers ===
        public float WIS_MentalBreak = 0.03f;           // -3% mental break threshold per point
        public float WIS_MeditationFocus = 0.04f;       // +4% meditation focus per point
        public float WIS_MedicalTendQuality = 0.02f;    // +2% medical tend quality per point
        public float WIS_SurgerySuccess = 0.03f;        // +3% surgery success per point
        public float WIS_TrainAnimal = 0.03f;            // +3% train animal chance per point
        public float WIS_AnimalGatherYield = 0.02f;      // +2% animal gather yield per point
        public float WIS_PsychicSensitivity = 0.01f;     // +1% psychic sensitivity per point (uses INT+WIS avg)
        public float WIS_NeuralHeatLimit = 1.5f;         // +1.5 neural heat limit per point (uses VIT+WIS avg)
        public float WIS_NeuralHeatRecovery = 0.015f;    // +1.5% neural heat recovery per point
        public float WIS_PsyfocusCost = 0.008f;          // -0.8% psyfocus cost per point
        
        // === CHARISMA Multipliers ===
        public float CHA_TradePrice = 0.02f;            // +2% trade price improvement per point
        public float CHA_SocialImpact = 0.05f;          // +5% social impact per point
        public float CHA_NegotiationAbility = 0.04f;    // +4% negotiation ability per point
        public float CHA_ArrestSuccess = 0.04f;         // +4% arrest success per point

        public override void ExposeData()
        {
            base.ExposeData();
            
            // Core settings
            Scribe_Values.Look(ref XPMultiplier, "XPMultiplier", 3.0f);
            Scribe_Values.Look(ref SkillPointsPerLevel, "SkillPointsPerLevel", 1);
            Scribe_Values.Look(ref EnableXPNotifications, "EnableXPNotifications", true);
            Scribe_Values.Look(ref EnableLevelUpNotifications, "EnableLevelUpNotifications", true);
            Scribe_Values.Look(ref UseLevelUpNotice, "UseLevelUpNotice", false);
            Scribe_Values.Look(ref MaxLevel, "MaxLevel", 9999);
            Scribe_Values.Look(ref MaxStatCap, "MaxStatCap", 9999);
            Scribe_Values.Look(ref ShowMobRanks, "ShowMobRanks", true);
            Scribe_Values.Look(ref MobRankSpeedMultiplier, "MobRankSpeedMultiplier", 1.0f);
            Scribe_Values.Look(ref MobRankArmorMultiplier, "MobRankArmorMultiplier", 1.0f);
            Scribe_Values.Look(ref MobRankHealthMultiplier, "MobRankHealthMultiplier", 1.0f);
            Scribe_Values.Look(ref MobRankValueMultiplier, "MobRankValueMultiplier", 1.0f);
            Scribe_Values.Look(ref PawnLevelValueMultiplier, "PawnLevelValueMultiplier", 1.0f);
            Scribe_Values.Look(ref ExcludeAnimalsFromRanking, "ExcludeAnimalsFromRanking", false);
            Scribe_Values.Look(ref ExcludeMechsFromRanking, "ExcludeMechsFromRanking", false);
            Scribe_Values.Look(ref ExcludeEntitiesFromRanking, "ExcludeEntitiesFromRanking", false);
            Scribe_Values.Look(ref TamedAnimalBonusRetention, "TamedAnimalBonusRetention", 0.5f);
            Scribe_Values.Look(ref StartingPawnLevel, "StartingPawnLevel", 0);
            Scribe_Values.Look(ref NewbornsUseRankRolling, "NewbornsUseRankRolling", false);
            
            // Raid Rank settings
            Scribe_Values.Look(ref EnableRaidRanking, "EnableRaidRanking", true);
            Scribe_Values.Look(ref RaidRankPointsMultiplier, "RaidRankPointsMultiplier", 1.0f);
            Scribe_Values.Look(ref RaidRankCapAtS, "RaidRankCapAtS", true);
            Scribe_Values.Look(ref MaxRaidRank, "MaxRaidRank", 6);
            Scribe_Values.Look(ref ClassicRNGRaids, "ClassicRNGRaids", false);
            Scribe_Values.Look(ref AdaptiveRaidRanks, "AdaptiveRaidRanks", false);
            
            // Mechanitor XP settings
            Scribe_Values.Look(ref EnableMechanitorXP, "EnableMechanitorXP", true);
            Scribe_Values.Look(ref EnableMechCreatureXP, "EnableMechCreatureXP", true);
            Scribe_Values.Look(ref ShareKillXPAcrossMaps, "ShareKillXPAcrossMaps", false);
            
            // Guild Quest settings
            Scribe_Values.Look(ref EnableGuildQuests, "EnableGuildQuests", true);
            Scribe_Values.Look(ref GuildQuestFrequency, "GuildQuestFrequency", 1.0f);
            Scribe_Values.Look(ref MinQuestRank, "MinQuestRank", 0);
            
            // Elite Mob settings
            Scribe_Values.Look(ref ShowEliteMobTitles, "ShowEliteMobTitles", true);
            
            // UI style settings
            Scribe_Values.Look(ref UseIsekaiUI, "UseIsekaiUI", true);
            
            // Trait display settings
            Scribe_Values.Look(ref EnableTraitColors, "EnableTraitColors", true);

            // Quest creature mod blacklist
            Scribe_Values.Look(ref QuestCreatureModBlacklist, "QuestCreatureModBlacklist", "");

            // Forge System settings
            Scribe_Values.Look(ref EnableForgeSystem, "EnableForgeSystem", true);
            Scribe_Values.Look(ref RefinementSuccessMultiplier, "RefinementSuccessMultiplier", 1.0f);
            Scribe_Values.Look(ref EnableWeaponMastery, "EnableWeaponMastery", true);
            Scribe_Values.Look(ref MasteryXPMultiplier, "MasteryXPMultiplier", 1.0f);
            
            // Constellation settings
            Scribe_Values.Look(ref ConstellationBonusMultiplier, "ConstellationBonusMultiplier", 1.0f);
            
            // Window position persistence
            Scribe_Values.Look(ref StatsWindowX, "StatsWindowX", -1f);
            Scribe_Values.Look(ref StatsWindowY, "StatsWindowY", -1f);
            
            // Aura/Effects settings
            Scribe_Values.Look(ref EnableDraftedAura, "EnableDraftedAura", true);
            Scribe_Values.Look(ref AuraOpacity, "AuraOpacity", 0.25f);
            Scribe_Values.Look(ref AuraSizeMultiplier, "AuraSizeMultiplier", 1.2f);
            Scribe_Values.Look(ref EnableAuraParticles, "EnableAuraParticles", true);
            Scribe_Values.Look(ref AuraParticleRate, "AuraParticleRate", 1f);
            Scribe_Values.Look(ref EnableAuraPulse, "EnableAuraPulse", true);
            Scribe_Values.Look(ref AuraPulseSpeed, "AuraPulseSpeed", 1f);
            Scribe_Values.Look(ref AuraUseFavoriteColor, "AuraUseFavoriteColor", true);
            
            // Loot settings
            Scribe_Values.Look(ref EnableManaCoreDrops, "EnableManaCoreDrops", true);
            Scribe_Values.Look(ref EnableStarFragmentDrops, "EnableStarFragmentDrops", true);
            Scribe_Values.Look(ref AutoAllowCrystalHauling, "AutoAllowCrystalHauling", false);
            
            // STR multipliers
            Scribe_Values.Look(ref STR_MeleeDamage, "STR_MeleeDamage", 0.02f);
            Scribe_Values.Look(ref STR_CarryCapacity, "STR_CarryCapacity", 0.02f);
            Scribe_Values.Look(ref STR_MiningSpeed, "STR_MiningSpeed", 0.02f);
            
            // DEX multipliers
            Scribe_Values.Look(ref DEX_MoveSpeed, "DEX_MoveSpeed", 0.03f);
            Scribe_Values.Look(ref DEX_MeleeHitChance, "DEX_MeleeHitChance", 0.04f);
            Scribe_Values.Look(ref DEX_MeleeDodge, "DEX_MeleeDodge", 0.04f);
            Scribe_Values.Look(ref DEX_AimingTime, "DEX_AimingTime", 0.02f);
            Scribe_Values.Look(ref DEX_ShootingAccuracy, "DEX_ShootingAccuracy", 0.06f);
            
            // VIT multipliers
            Scribe_Values.Look(ref VIT_MaxHealth, "VIT_MaxHealth", 0.04f);
            Scribe_Values.Look(ref VIT_HealthRegen, "VIT_HealthRegen", 0.04f);
            Scribe_Values.Look(ref VIT_ToxicResist, "VIT_ToxicResist", 0.03f);
            Scribe_Values.Look(ref VIT_DamageReduction, "VIT_DamageReduction", 0.01f);
            Scribe_Values.Look(ref VIT_ImmunityGain, "VIT_ImmunityGain", 0.04f);
            Scribe_Values.Look(ref VIT_LifespanFactor, "VIT_LifespanFactor", 0.01f);
            
            // INT multipliers
            Scribe_Values.Look(ref INT_WorkSpeed, "INT_WorkSpeed", 0.04f);
            Scribe_Values.Look(ref INT_ResearchSpeed, "INT_ResearchSpeed", 0.06f);
            Scribe_Values.Look(ref INT_LearningSpeed, "INT_LearningSpeed", 0.04f);
            Scribe_Values.Look(ref INT_CraftingQuality, "INT_CraftingQuality", 0.01f);
            
            // WIS multipliers
            Scribe_Values.Look(ref WIS_MentalBreak, "WIS_MentalBreak", 0.03f);
            Scribe_Values.Look(ref WIS_MeditationFocus, "WIS_MeditationFocus", 0.04f);
            Scribe_Values.Look(ref WIS_MedicalTendQuality, "WIS_MedicalTendQuality", 0.02f);
            Scribe_Values.Look(ref WIS_SurgerySuccess, "WIS_SurgerySuccess", 0.03f);
            Scribe_Values.Look(ref WIS_TrainAnimal, "WIS_TrainAnimal", 0.03f);
            Scribe_Values.Look(ref WIS_AnimalGatherYield, "WIS_AnimalGatherYield", 0.02f);
            Scribe_Values.Look(ref WIS_PsychicSensitivity, "WIS_PsychicSensitivity", 0.01f);
            Scribe_Values.Look(ref WIS_NeuralHeatLimit, "WIS_NeuralHeatLimit", 1.5f);
            Scribe_Values.Look(ref WIS_NeuralHeatRecovery, "WIS_NeuralHeatRecovery", 0.015f);
            Scribe_Values.Look(ref WIS_PsyfocusCost, "WIS_PsyfocusCost", 0.008f);
            
            // CHA multipliers
            Scribe_Values.Look(ref CHA_TradePrice, "CHA_TradePrice", 0.02f);
            Scribe_Values.Look(ref CHA_SocialImpact, "CHA_SocialImpact", 0.05f);
            Scribe_Values.Look(ref CHA_NegotiationAbility, "CHA_NegotiationAbility", 0.04f);
            Scribe_Values.Look(ref CHA_ArrestSuccess, "CHA_ArrestSuccess", 0.04f);
        }
        
        /// <summary>
        /// Reset all stat multipliers to their default values
        /// </summary>
        public void ResetStatMultipliers()
        {
            // STR
            STR_MeleeDamage = 0.02f;
            STR_CarryCapacity = 0.02f;
            STR_MiningSpeed = 0.02f;
            
            // DEX
            DEX_MoveSpeed = 0.03f;
            DEX_MeleeHitChance = 0.04f;
            DEX_MeleeDodge = 0.04f;
            DEX_AimingTime = 0.02f;
            DEX_ShootingAccuracy = 0.06f;
            
            // VIT
            VIT_MaxHealth = 0.04f;
            VIT_HealthRegen = 0.04f;
            VIT_ToxicResist = 0.03f;
            VIT_DamageReduction = 0.01f;
            VIT_ImmunityGain = 0.04f;
            VIT_LifespanFactor = 0.01f;
            
            // INT
            INT_WorkSpeed = 0.04f;
            INT_ResearchSpeed = 0.06f;
            INT_LearningSpeed = 0.04f;
            INT_CraftingQuality = 0.01f;
            
            // WIS
            WIS_MentalBreak = 0.03f;
            WIS_MeditationFocus = 0.04f;
            WIS_MedicalTendQuality = 0.02f;
            WIS_SurgerySuccess = 0.03f;
            WIS_TrainAnimal = 0.03f;
            WIS_AnimalGatherYield = 0.02f;
            WIS_PsychicSensitivity = 0.01f;
            WIS_NeuralHeatLimit = 1.5f;
            WIS_NeuralHeatRecovery = 0.015f;
            WIS_PsyfocusCost = 0.008f;
            
            // CHA
            CHA_TradePrice = 0.02f;
            CHA_SocialImpact = 0.05f;
            CHA_NegotiationAbility = 0.04f;
            CHA_ArrestSuccess = 0.04f;
            
            // Constellation
            ConstellationBonusMultiplier = 1.0f;
        }
    }
    
    /// <summary>
    /// Static accessor for settings - used by components that can't easily get the mod instance
    /// </summary>
    public static class IsekaiLevelingSettings
    {
        public static IsekaiSettings Settings => IsekaiMod.Settings;
        
        // Core settings
        public static bool showMobRanks => Settings?.ShowMobRanks ?? true;
        public static bool excludeAnimalsFromRanking => Settings?.ExcludeAnimalsFromRanking ?? false;
        public static bool excludeMechsFromRanking => Settings?.ExcludeMechsFromRanking ?? false;
        public static bool excludeEntitiesFromRanking => Settings?.ExcludeEntitiesFromRanking ?? false;
        
        // UI style
        public static bool UseIsekaiUI => Settings?.UseIsekaiUI ?? true;
        
        // Aura settings
        public static bool enableDraftedAura => Settings?.EnableDraftedAura ?? true;
        public static float auraOpacity => Settings?.AuraOpacity ?? 0.25f;
        public static float auraSizeMultiplier => Settings?.AuraSizeMultiplier ?? 1.2f;
        public static bool enableAuraParticles => Settings?.EnableAuraParticles ?? true;
        public static float auraParticleRate => Settings?.AuraParticleRate ?? 1f;
        public static bool enableAuraPulse => Settings?.EnableAuraPulse ?? true;
        public static float auraPulseSpeed => Settings?.AuraPulseSpeed ?? 1f;
        public static bool auraUseFavoriteColor => Settings?.AuraUseFavoriteColor ?? true;
        
        // Loot settings
        public static bool enableManaCoreDrops => Settings?.EnableManaCoreDrops ?? true;
        public static bool enableStarFragmentDrops => Settings?.EnableStarFragmentDrops ?? true;
        
        // Mechanitor XP (Biotech DLC)
        public static bool EnableMechanitorXP => Settings?.EnableMechanitorXP ?? true;
        public static bool EnableMechCreatureXP => Settings?.EnableMechCreatureXP ?? true;
        
        // Cross-map XP
        public static bool ShareKillXPAcrossMaps => Settings?.ShareKillXPAcrossMaps ?? false;
        
        public static float xpMultiplier => Settings?.XPMultiplier ?? 3f;
        public static int skillPointsPerLevel => Settings?.SkillPointsPerLevel ?? 1;
        public static bool enableXPNotifications => Settings?.EnableXPNotifications ?? true;
        public static bool enableLevelUpNotifications => Settings?.EnableLevelUpNotifications ?? true;
        public static bool useLevelUpNotice => Settings?.UseLevelUpNotice ?? false;
        public static int maxLevel => Settings?.MaxLevel ?? 9999;
        
        // Mob Ranking multipliers
        public static float MobRankSpeedMultiplier => Settings?.MobRankSpeedMultiplier ?? 1.0f;
        public static float MobRankArmorMultiplier => Settings?.MobRankArmorMultiplier ?? 1.0f;
        public static float MobRankHealthMultiplier => Settings?.MobRankHealthMultiplier ?? 1.0f;
        public static float TamedAnimalBonusRetention => Settings?.TamedAnimalBonusRetention ?? 0.5f;
        
        // Starting level for player colonists
        public static int StartingPawnLevel => Settings?.StartingPawnLevel ?? 0;
        public static bool NewbornsUseRankRolling => Settings?.NewbornsUseRankRolling ?? false;
        
        // STR multipliers
        public static float STR_MeleeDamage => Settings?.STR_MeleeDamage ?? 0.02f;
        public static float STR_CarryCapacity => Settings?.STR_CarryCapacity ?? 0.02f;
        public static float STR_MiningSpeed => Settings?.STR_MiningSpeed ?? 0.02f;
        
        // DEX multipliers
        public static float DEX_MoveSpeed => Settings?.DEX_MoveSpeed ?? 0.03f;
        public static float DEX_MeleeHitChance => Settings?.DEX_MeleeHitChance ?? 0.04f;
        public static float DEX_MeleeDodge => Settings?.DEX_MeleeDodge ?? 0.04f;
        public static float DEX_AimingTime => Settings?.DEX_AimingTime ?? 0.02f;
        public static float DEX_ShootingAccuracy => Settings?.DEX_ShootingAccuracy ?? 0.06f;
        
        // VIT multipliers
        public static float VIT_MaxHealth => Settings?.VIT_MaxHealth ?? 0.04f;
        public static float VIT_HealthRegen => Settings?.VIT_HealthRegen ?? 0.04f;
        public static float VIT_ToxicResist => Settings?.VIT_ToxicResist ?? 0.03f;
        public static float VIT_DamageReduction => Settings?.VIT_DamageReduction ?? 0.01f;
        public static float VIT_ImmunityGain => Settings?.VIT_ImmunityGain ?? 0.04f;
        public static float VIT_LifespanFactor => Settings?.VIT_LifespanFactor ?? 0.01f;
        
        // INT multipliers
        public static float INT_WorkSpeed => Settings?.INT_WorkSpeed ?? 0.04f;
        public static float INT_ResearchSpeed => Settings?.INT_ResearchSpeed ?? 0.06f;
        public static float INT_LearningSpeed => Settings?.INT_LearningSpeed ?? 0.04f;
        public static float INT_CraftingQuality => Settings?.INT_CraftingQuality ?? 0.01f;
        
        // WIS multipliers
        public static float WIS_MentalBreak => Settings?.WIS_MentalBreak ?? 0.03f;
        public static float WIS_MeditationFocus => Settings?.WIS_MeditationFocus ?? 0.04f;
        public static float WIS_MedicalTendQuality => Settings?.WIS_MedicalTendQuality ?? 0.02f;
        public static float WIS_SurgerySuccess => Settings?.WIS_SurgerySuccess ?? 0.03f;
        public static float WIS_TrainAnimal => Settings?.WIS_TrainAnimal ?? 0.03f;
        public static float WIS_AnimalGatherYield => Settings?.WIS_AnimalGatherYield ?? 0.02f;
        public static float WIS_PsychicSensitivity => Settings?.WIS_PsychicSensitivity ?? 0.01f;
        public static float WIS_NeuralHeatLimit => Settings?.WIS_NeuralHeatLimit ?? 1.5f;
        public static float WIS_NeuralHeatRecovery => Settings?.WIS_NeuralHeatRecovery ?? 0.015f;
        public static float WIS_PsyfocusCost => Settings?.WIS_PsyfocusCost ?? 0.008f;
        
        // CHA multipliers
        public static float CHA_TradePrice => Settings?.CHA_TradePrice ?? 0.02f;
        public static float CHA_SocialImpact => Settings?.CHA_SocialImpact ?? 0.05f;
        public static float CHA_NegotiationAbility => Settings?.CHA_NegotiationAbility ?? 0.04f;
        public static float CHA_ArrestSuccess => Settings?.CHA_ArrestSuccess ?? 0.04f;
        
        // Forge System settings
        public static bool EnableForgeSystem => Settings?.EnableForgeSystem ?? true;
        public static float RefinementSuccessMultiplier => Settings?.RefinementSuccessMultiplier ?? 1.0f;
        public static bool EnableWeaponMastery => Settings?.EnableWeaponMastery ?? true;
        public static float MasteryXPMultiplier => Settings?.MasteryXPMultiplier ?? 1.0f;
    }
}
