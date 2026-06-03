using HarmonyLib;
using RimWorld;
using Verse;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Patches to control guild quest frequency based on settings.
    /// The main frequency control is in IsekaiHuntTracker.GameComponentTick().
    /// This just ensures the incident def is properly configured.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class GuildQuestSettings
    {
        static GuildQuestSettings()
        {
            // Apply initial settings
            ApplySettings();
        }
        
        /// <summary>
        /// Apply current settings to the incident def.
        /// Called at startup and can be called when settings change.
        /// </summary>
        public static void ApplySettings()
        {
            var incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail("Isekai_HuntSpawn");
            if (incidentDef == null) return;
            
            if (IsekaiMod.Settings == null) return;
            
            // If disabled, set base chance to 0 so storyteller doesn't spawn extra quests
            if (!IsekaiMod.Settings.EnableGuildQuests)
            {
                incidentDef.baseChance = 0f;
                if (Prefs.DevMode)
                {
                    Log.Message("[Isekai] Guild quests DISABLED");
                }
                return;
            }
            
            // Set very low base chance - we use our own timer in IsekaiHuntTracker
            // This prevents the storyteller from spawning random extra quests
            incidentDef.baseChance = 0.1f;
            incidentDef.minRefireDays = 0.1f;
            
            if (Prefs.DevMode)
            {
                Log.Message($"[Isekai] Guild quest frequency set to: every {IsekaiMod.Settings.GuildQuestFrequency} days");
            }
        }
    }
}
