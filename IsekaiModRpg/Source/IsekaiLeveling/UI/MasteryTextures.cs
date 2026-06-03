using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    [StaticConstructorOnStartup]
    public static class MasteryTextures
    {
        public static readonly Texture2D WindowBg;
        public static readonly Texture2D PanelMain;
        public static readonly Texture2D RowMastered;
        public static readonly Texture2D RowUnmastered;

        static MasteryTextures()
        {
            WindowBg = TextureLoader.LoadUncompressed("UI/Mastery/Mastery_Window_BG");
            PanelMain = TextureLoader.LoadUncompressed("UI/Mastery/Mastery_Panel_Main");
            RowMastered = TextureLoader.LoadUncompressed("UI/Mastery/Mastery_Row_Mastered");
            RowUnmastered = TextureLoader.LoadUncompressed("UI/Mastery/Mastery_Row_Unmastered");
        }
    }
}
