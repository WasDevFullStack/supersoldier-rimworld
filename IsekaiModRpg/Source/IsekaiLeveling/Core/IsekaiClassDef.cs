using System.Collections.Generic;
using RimWorld;
using Verse;

namespace IsekaiLeveling
{
    /// <summary>
    /// Simple stat bonus class that doesn't use custom XML loading
    /// </summary>
    public class IsekaiStatBonus
    {
        public StatDef stat;
        public float value;
    }

    /// <summary>
    /// Definition for an Isekai class (Warrior, Mage, etc.)
    /// </summary>
    public class IsekaiClassDef : Def
    {
        public string className;
        public string classDescription;
        public string iconPath;
        
        // Stat modifiers per level
        public float healthPerLevel = 0f;
        public float meleeDamagePerLevel = 0f;
        public float rangedDamagePerLevel = 0f;
        public float moveSpeedPerLevel = 0f;
        public float workSpeedPerLevel = 0f;
        public float socialImpactPerLevel = 0f;
        
        // Base XP requirements
        public int baseXPToLevel = 100;
        public float xpScalingFactor = 1.5f;
        
        // Available skill tree nodes for this class
        public List<IsekaiSkillNodeDef> skillNodes;
        
        // Starting bonuses when choosing this class
        public List<IsekaiStatBonus> startingBonuses;
        
        public int GetXPForLevel(int level)
        {
            return (int)(baseXPToLevel * System.Math.Pow(xpScalingFactor, level - 1));
        }
    }
    
    /// <summary>
    /// Definition for a skill tree node
    /// </summary>
    public class IsekaiSkillNodeDef : Def
    {
        public string skillName;
        public string skillDescription;
        public string iconPath;
        
        // Position in skill tree UI
        public int treeRow;
        public int treeColumn;
        
        // Requirements
        public int requiredLevel = 1;
        public int skillPointCost = 1;
        public List<IsekaiSkillNodeDef> prerequisites;
        
        // Effects when unlocked
        public List<IsekaiStatBonus> statBonuses;
        public List<IsekaiAbilityDef> unlockedAbilities;
        
        // Skill tiers (can be upgraded multiple times)
        public int maxTier = 1;
        public float bonusPerTier = 1f;
        
        // Whether this is an active skill (vs passive)
        public bool isActiveSkill = false;
    }
    
    /// <summary>
    /// Definition for special abilities
    /// </summary>
    public class IsekaiAbilityDef : Def
    {
        public string abilityName;
        public string abilityDescription;
        public string iconPath;
        
        public AbilityType abilityType = AbilityType.Passive;
        public TargetType targetType = TargetType.Self;
        
        // For active abilities
        public int cooldownTicks = 0;
        public float manaCost = 0f;
        public float range = 0f;
        public float duration = 0f;
        
        // Effects
        public List<IsekaiStatBonus> selfBuffs;
        public List<IsekaiStatBonus> targetEffects;
        public float damageAmount = 0f;
        public float healAmount = 0f;
        
        // Special effect types
        public SpecialEffectType specialEffect = SpecialEffectType.None;
    }
    
    public enum AbilityType
    {
        Passive,    // Always active
        Active,     // Must be activated
        Toggle,     // Can be turned on/off
        Triggered   // Activates under certain conditions
    }
    
    public enum TargetType
    {
        Self,
        SingleEnemy,
        SingleAlly,
        AreaEnemies,
        AreaAllies,
        AllEnemies,
        AllAllies
    }
    
    public enum SpecialEffectType
    {
        None,
        Berserk,        // Increased damage, reduced defense
        Shield,         // Damage absorption
        Regeneration,   // Health over time
        Haste,          // Movement speed
        Inspire,        // Buff nearby allies
        Stealth,        // Reduced detection
        CriticalStrike, // Bonus crit chance
        Lifesteal,      // Heal on damage
        Reflect,        // Return damage
        Sanctuary,      // Cannot be targeted briefly
        Teleport,       // Instant movement
        Summon          // Create temporary ally
    }
}
