using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Loads and caches custom UI textures for the Isekai tab
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IsekaiTextures
    {
        // Main UI elements
        public static readonly Texture2D BackgroundTab;
        public static readonly Texture2D PawnDisplay;
        public static readonly Texture2D PrimaryButton;
        public static readonly Texture2D SecondaryButton;
        public static readonly Texture2D SecondaryButton2;
        public static readonly Texture2D ConstellationButton;
        public static readonly Texture2D StatTab;
        
        static IsekaiTextures()
        {
            // Load textures from mod folder
            BackgroundTab = ContentFinder<Texture2D>.Get("UI/backgroundtab", false);
            PawnDisplay = ContentFinder<Texture2D>.Get("UI/pawndisplay", false);
            PrimaryButton = ContentFinder<Texture2D>.Get("UI/primarybutton(dashboard)", false);
            SecondaryButton = ContentFinder<Texture2D>.Get("UI/secondarybutton(wallet)", false);
            SecondaryButton2 = ContentFinder<Texture2D>.Get("UI/secondarybutton2(wallet)", false);
            ConstellationButton = ContentFinder<Texture2D>.Get("UI/secondarybutton(constellation)", false);
            StatTab = ContentFinder<Texture2D>.Get("UI/stattab(emptyspace)", false);
            
            // Log loaded status
            // Texture loading verified silently
        }
    }
}
