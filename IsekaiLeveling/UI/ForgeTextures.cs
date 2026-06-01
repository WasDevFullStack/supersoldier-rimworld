using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    [StaticConstructorOnStartup]
    public static class ForgeTextures
    {
        public static readonly Texture2D WindowBg;
        public static readonly Texture2D PanelSection;
        public static readonly Texture2D PanelSectionCenter;
        public static readonly Texture2D ButtonRefine;

        static ForgeTextures()
        {
            WindowBg = TextureLoader.LoadUncompressed("UI/Forge/Forge_Window_BG");
            PanelSection = TextureLoader.LoadUncompressed("UI/Forge/Forge_Panel_Section");
            PanelSectionCenter = TextureLoader.LoadUncompressed("UI/Forge/Forge_Panel_Section_Center");
            ButtonRefine = TextureLoader.LoadUncompressed("UI/Forge/Forge_Button_Refine");
        }
    }
}
