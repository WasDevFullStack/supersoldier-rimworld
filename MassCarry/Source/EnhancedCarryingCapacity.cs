using HarmonyLib;
using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace EnhancedCarryingCapacity
{
    public sealed class EnhancedCarryingCapacity : Mod
    {
        public static EnhancedCarryingCapacity Instance { get; private set; }

        public bool CombatExtendedActive { get; private set; }

        public EnhancedCarryingCapacity(ModContentPack content) : base(content) {
            Instance = this;
            CombatExtendedActive = LoadedModManager.RunningMods.Any(x => x.PackageId.EqualsIgnoreCase("CETeam.CombatExtended"));
            new Harmony("Designer225.EnhancedCarryingCapacity").PatchAll();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            EnhancedCarryingCapacitySettings.Instance.DoSettingsWindowContents(inRect);
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "EnhancedCarryingCapacity".Translate();
        }
    }
}
