using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Loads and caches textures for the Stats Attribution Window.
    /// All textures located in Textures/UI/Stats/
    /// </summary>
    [StaticConstructorOnStartup]
    public static class StatsWindowTextures
    {
        // Main window background
        public static readonly Texture2D WindowBg;
        
        // Header/decorative elements
        public static readonly Texture2D StatTab;
        public static readonly Texture2D StatTabEmpty;
        
        // Points display
        public static readonly Texture2D AvailablePoints;
        
        // Stat row backgrounds (each has colored accent)
        public static readonly Texture2D StatSTR;
        public static readonly Texture2D StatVIT;
        public static readonly Texture2D StatDEX;
        public static readonly Texture2D StatINT;
        public static readonly Texture2D StatWIS;
        public static readonly Texture2D StatCHA;
        
        // Detail section
        public static readonly Texture2D StatListDetail;
        
        // Bottom section
        public static readonly Texture2D GlobalRank;
        public static readonly Texture2D CloseButton;
        public static readonly Texture2D ConfirmButton;
        
        static StatsWindowTextures()
        {
            WindowBg = ContentFinder<Texture2D>.Get("UI/Stats/statwindow-bg", false);
            StatTab = ContentFinder<Texture2D>.Get("UI/Stats/stat tab", false);
            StatTabEmpty = ContentFinder<Texture2D>.Get("UI/Stats/stattab(emptyspace)", false);
            AvailablePoints = ContentFinder<Texture2D>.Get("UI/Stats/availablepoints", false);
            
            StatSTR = ContentFinder<Texture2D>.Get("UI/Stats/stat-str", false);
            StatVIT = ContentFinder<Texture2D>.Get("UI/Stats/stat-vit", false);
            StatDEX = ContentFinder<Texture2D>.Get("UI/Stats/stat-dex", false);
            StatINT = ContentFinder<Texture2D>.Get("UI/Stats/stat-int", false);
            StatWIS = ContentFinder<Texture2D>.Get("UI/Stats/stat-wis", false);
            StatCHA = ContentFinder<Texture2D>.Get("UI/Stats/stat-cha", false);
            
            StatListDetail = ContentFinder<Texture2D>.Get("UI/Stats/statlist-detail", false);
            
            GlobalRank = ContentFinder<Texture2D>.Get("UI/Stats/global-rank", false);
            CloseButton = ContentFinder<Texture2D>.Get("UI/Stats/closebutton", false);
            ConfirmButton = ContentFinder<Texture2D>.Get("UI/Stats/confirmbutton", false);
            
            Log.Message("[IsekaiLeveling] Stats window textures loaded.");
        }
        
        /// <summary>
        /// Get the appropriate stat texture by type
        /// </summary>
        public static Texture2D GetStatTexture(IsekaiStatType type)
        {
            switch (type)
            {
                case IsekaiStatType.Strength: return StatSTR;
                case IsekaiStatType.Vitality: return StatVIT;
                case IsekaiStatType.Dexterity: return StatDEX;
                case IsekaiStatType.Intelligence: return StatINT;
                case IsekaiStatType.Wisdom: return StatWIS;
                case IsekaiStatType.Charisma: return StatCHA;
                default: return StatSTR;
            }
        }
    }
}
