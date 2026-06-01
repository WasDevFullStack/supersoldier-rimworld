using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Patches Trait.get_LabelCap to wrap isekai trait labels in rich text color tags.
    /// RimWorld's Widgets.Label uses Unity GUI.Label with GUIStyle that supports rich text,
    /// so <color=#RRGGBB>label</color> tags produce colored trait names in the character card.
    /// </summary>
    [HarmonyPatch]
    public static class TraitColorPatch
    {
        // Color hex codes by trait category
        private const string Gold   = "#FFD919";   // Archetype + Rank S/SS/SSS
        private const string Purple = "#B266FF";   // Rare traits + Rank A
        private const string Green  = "#66E666";   // Uncommon traits + Rank C
        private const string Blue   = "#66B3FF";   // Rank B
        private const string Gray   = "#999999";   // Rank F/E
        private const string Red    = "#E65959";   // Negative/Curse traits

        private static readonly Dictionary<string, string> traitColors = new Dictionary<string, string>
        {
            // === Archetype Traits (Gold) ===
            { "Isekai_Protagonist",      Gold },
            { "Isekai_Antagonist",       Gold },
            { "Isekai_Reincarnated",     Gold },
            { "Isekai_Regressor",        Gold },
            { "Isekai_SummonedHero",     Gold },

            // === Rare/Powerful Traits (Purple) ===
            { "Isekai_Prodigy",          Purple },
            { "Isekai_AwakenedPotential", Purple },
            { "Isekai_Genius",           Purple },
            { "Isekai_Undying",          Purple },
            { "Isekai_PowerSpike",       Purple },

            // === Growth Traits (Green) ===
            { "Isekai_NaturalTalent",    Green },
            { "Isekai_LateBloomer",      Green },
            { "Isekai_QuickLearner",     Green },
            { "Isekai_SlowGrind",        Green },
            { "Isekai_BattleManiac",     Green },

            // === Combat Traits (Green) ===
            { "Isekai_BerserkerBlood",   Green },
            { "Isekai_IronWill",         Green },
            { "Isekai_GlassCannon",      Green },
            { "Isekai_Fortress",         Green },
            { "Isekai_ShadowStep",       Green },
            { "Isekai_PredatorInstinct", Green },

            // === Utility Traits (Green) ===
            { "Isekai_MerchantEye",      Green },
            { "Isekai_CraftsmanSoul",    Green },
            { "Isekai_BeastWhisperer",   Green },
            { "Isekai_HealerTouch",      Green },
            { "Isekai_Lucky",            Green },

            // === Negative/Curse Traits (Red) ===
            { "Isekai_CursedLuck",       Red },
            { "Isekai_SystemGlitch",     Red },
            { "Isekai_HollowCore",       Red },
            { "Isekai_FragileVessel",    Red },
            { "Isekai_EchoOfDefeat",     Red },
            { "Isekai_SealedPower",      Red },

            // === Rank Traits ===
            { "Isekai_Rank_F",           Gray },
            { "Isekai_Rank_E",           Gray },
            // Rank D = default white (not in dictionary)
            { "Isekai_Rank_C",           Green },
            { "Isekai_Rank_B",           Blue },
            { "Isekai_Rank_A",           Purple },
            { "Isekai_Rank_S",           Gold },
            { "Isekai_Rank_SS",          Gold },
            { "Isekai_Rank_SSS",         Gold },

            // Stat Affinity traits (Mighty, Agile, Resilient, Brilliant, Enlightened, SilverTongue)
            // intentionally omitted — remain default white
        };

        static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Trait), nameof(Trait.LabelCap));
        }

        [HarmonyPostfix]
        public static void Postfix(Trait __instance, ref string __result)
        {
            if (__instance?.def == null || __result == null)
                return;
            
            // Check if trait colors are enabled in settings
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableTraitColors)
                return;

            if (traitColors.TryGetValue(__instance.def.defName, out string hex))
            {
                __result = "<color=" + hex + ">" + __result + "</color>";
            }
        }
    }
}
