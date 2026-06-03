using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    [StaticConstructorOnStartup]
    public static class RunicStationTextures
    {
        public static readonly Texture2D WindowBg;
        public static readonly Texture2D PanelLeft;
        public static readonly Texture2D PanelCenter;
        public static readonly Texture2D PanelRight;
        public static readonly Texture2D ButtonApply;
        public static readonly Texture2D ButtonRemove;
        public static readonly Texture2D SlotEmpty;
        public static readonly Texture2D SlotFilled;

        static RunicStationTextures()
        {
            WindowBg = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Window_BG");
            PanelLeft = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Panel_Left");
            PanelCenter = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Panel_Center");
            PanelRight = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Panel_Right");
            ButtonApply = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Button_Apply");
            ButtonRemove = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Button_Remove");
            SlotEmpty = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Slot_Empty");
            SlotFilled = TextureLoader.LoadUncompressed("UI/Runic/RunicStation_Slot_Filled");
        }
    }
}
