using UnityEngine;
using Verse;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Static texture loader for Isekai quest icons
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IsekaiQuestTextures
    {
        public static readonly Texture2D HuntIcon;
        public static readonly Texture2D ExpeditionIcon;
        public static readonly Texture2D RaidIcon;
        
        static IsekaiQuestTextures()
        {
            HuntIcon = ContentFinder<Texture2D>.Get("World/Sites/Isekai_Hunt", false);
            ExpeditionIcon = ContentFinder<Texture2D>.Get("World/Sites/Isekai_Expedition", false);
            RaidIcon = ContentFinder<Texture2D>.Get("World/Sites/Isekai_Raid", false);
            
            if (HuntIcon == null)
                Log.Warning("[Isekai] Could not load Hunt icon from World/Sites/Isekai_Hunt");
            if (ExpeditionIcon == null)
                Log.Warning("[Isekai] Could not load Expedition icon from World/Sites/Isekai_Expedition");
            if (RaidIcon == null)
                Log.Warning("[Isekai] Could not load Raid icon from World/Sites/Isekai_Raid");
        }
        
        /// <summary>
        /// Gets the appropriate icon for an Isekai quest based on its name
        /// </summary>
        public static Texture2D GetIconForQuest(RimWorld.Quest quest)
        {
            if (quest == null || string.IsNullOrEmpty(quest.name))
                return null;
            
            string name = quest.name;
            
            // Check for Raid (S-SSS rank)
            if (name.Contains("Raid") || name.Contains("SSS-Rank") || name.Contains("SS-Rank") || name.Contains("S-Rank"))
            {
                return RaidIcon;
            }
            
            // Check for Expedition (B-A rank)
            if (name.Contains("Expedition") || name.Contains("A-Rank") || name.Contains("B-Rank"))
            {
                return ExpeditionIcon;
            }
            
            // Check for Hunt (F-D rank) or any other Isekai quest
            if (name.Contains("Hunt") || name.Contains("F-Rank") || name.Contains("E-Rank") || 
                name.Contains("D-Rank") || name.Contains("C-Rank"))
            {
                return HuntIcon;
            }
            
            return null;
        }
        
        /// <summary>
        /// Checks if a quest is an Isekai quest
        /// </summary>
        public static bool IsIsekaiQuest(RimWorld.Quest quest)
        {
            if (quest == null)
                return false;
            
            // Check if it has our custom quest parts
            foreach (var part in quest.PartsListForReading)
            {
                if (part is QuestPart_IsekaiLocalHunt || part is QuestPart_IsekaiWorldHunt || part is QuestPart_IsekaiXPReward)
                    return true;
            }
            
            // Fallback: check name
            if (quest.name != null && (quest.name.Contains("-Rank") && 
                (quest.name.Contains("Hunt") || quest.name.Contains("Expedition") || quest.name.Contains("Raid"))))
            {
                return true;
            }
            
            return false;
        }
    }
}
