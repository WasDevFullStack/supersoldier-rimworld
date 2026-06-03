using System.Collections.Generic;
using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Defines a title that can be earned and equipped by pawns
    /// Titles provide passive bonuses and can unlock special abilities
    /// </summary>
    public class IsekaiTitleDef : Def
    {
        public string titleName;
        public string titleDescription;
        
        // How to earn this title
        public TitleUnlockType unlockType = TitleUnlockType.Level;
        public int requiredLevel = 1;
        public string unlockConditionDesc;
        
        // Rarity/tier of the title
        public TitleRarity rarity = TitleRarity.Common;
        
        // Stat bonuses when equipped
        public float strengthBonus = 0;
        public float dexterityBonus = 0;
        public float vitalityBonus = 0;
        public float intelligenceBonus = 0;
        public float wisdomBonus = 0;
        public float charismaBonus = 0;
        
        // Percentage bonuses
        public float xpGainBonus = 0;           // +X% XP gain
        public float manaRegenBonus = 0;        // +X% mana regen
        public float enduranceRegenBonus = 0;   // +X% endurance regen
        public float allStatsBonus = 0;         // +X to all stats
        
        // Special effects
        public List<string> specialEffects;     // For future implementation
        
        public string GetRarityString()
        {
            switch (rarity)
            {
                case TitleRarity.Common: return "Common";
                case TitleRarity.Uncommon: return "Uncommon";
                case TitleRarity.Rare: return "Rare";
                case TitleRarity.Epic: return "Epic";
                case TitleRarity.Legendary: return "Legendary";
                case TitleRarity.Mythic: return "Mythic";
                default: return "Unknown";
            }
        }
        
        public UnityEngine.Color GetRarityColor()
        {
            switch (rarity)
            {
                case TitleRarity.Common: return new UnityEngine.Color(0.7f, 0.7f, 0.7f);
                case TitleRarity.Uncommon: return new UnityEngine.Color(0.3f, 0.8f, 0.3f);
                case TitleRarity.Rare: return new UnityEngine.Color(0.3f, 0.5f, 0.9f);
                case TitleRarity.Epic: return new UnityEngine.Color(0.7f, 0.3f, 0.9f);
                case TitleRarity.Legendary: return new UnityEngine.Color(1f, 0.8f, 0.2f);
                case TitleRarity.Mythic: return new UnityEngine.Color(1f, 0.4f, 0.4f);
                default: return UnityEngine.Color.white;
            }
        }
    }
    
    public enum TitleUnlockType
    {
        Level,          // Reach a certain level
        Class,          // Choose a specific class
        Achievement,    // Complete a specific achievement
        Quest,          // Complete a quest (future)
        Special         // Special unlock condition
    }
    
    public enum TitleRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
        Mythic
    }
    
    /// <summary>
    /// Tracks a pawn's earned and active titles
    /// </summary>
    public class IsekaiTitleTracker : IExposable
    {
        public List<IsekaiTitleDef> earnedTitles = new List<IsekaiTitleDef>();
        public IsekaiTitleDef activeTitle;
        
        public void ExposeData()
        {
            Scribe_Collections.Look(ref earnedTitles, "earnedTitles", LookMode.Def);
            Scribe_Defs.Look(ref activeTitle, "activeTitle");
            
            if (earnedTitles == null)
                earnedTitles = new List<IsekaiTitleDef>();
            
            // Clean up null entries from removed/renamed title defs (save compat)
            earnedTitles.RemoveAll(t => t == null);
        }
        
        public void EarnTitle(IsekaiTitleDef title)
        {
            if (!earnedTitles.Contains(title))
            {
                earnedTitles.Add(title);
            }
        }
        
        public void SetActiveTitle(IsekaiTitleDef title)
        {
            if (title == null || earnedTitles.Contains(title))
            {
                activeTitle = title;
            }
        }
        
        public bool HasTitle(IsekaiTitleDef title)
        {
            return earnedTitles.Contains(title);
        }
        
        /// <summary>
        /// Get total stat bonus from active title
        /// </summary>
        public float GetStatBonus(IsekaiStatType stat)
        {
            if (activeTitle == null) return 0;
            
            float bonus = activeTitle.allStatsBonus;
            switch (stat)
            {
                case IsekaiStatType.Strength: bonus += activeTitle.strengthBonus; break;
                case IsekaiStatType.Dexterity: bonus += activeTitle.dexterityBonus; break;
                case IsekaiStatType.Vitality: bonus += activeTitle.vitalityBonus; break;
                case IsekaiStatType.Intelligence: bonus += activeTitle.intelligenceBonus; break;
                case IsekaiStatType.Wisdom: bonus += activeTitle.wisdomBonus; break;
                case IsekaiStatType.Charisma: bonus += activeTitle.charismaBonus; break;
            }
            return bonus;
        }
        
        public float GetXPGainBonus()
        {
            return activeTitle?.xpGainBonus ?? 0f;
        }
        
        public float GetManaRegenBonus()
        {
            return activeTitle?.manaRegenBonus ?? 0f;
        }
        
        public float GetEnduranceRegenBonus()
        {
            return activeTitle?.enduranceRegenBonus ?? 0f;
        }
    }
}
