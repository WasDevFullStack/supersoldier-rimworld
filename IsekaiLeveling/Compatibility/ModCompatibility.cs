using System;
using Verse;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Central hub for detecting and managing mod compatibility.
    /// All compatibility is soft - mods are optional and detected at runtime.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ModCompatibility
    {
        // Mod package IDs
        public const string RimWorldOfMagic_PackageId = "torann.arimworldofmagic";
        public const string VanillaPsycastsExpanded_PackageId = "vanillaexpanded.vpsycastse";
        public const string CombatExtended_PackageId = "ceteam.combatextended";
        public const string Hospitality_PackageId = "orion.hospitality";
        public const string DubsBadHygiene_PackageId = "dubwise.dubsbadhygiene";
        public const string SimpleSidearms_PackageId = "petetimessix.simplesidearms";
        public const string CharacterEditor_PackageId = "void.charactereditor";
        public const string RimHUD_PackageId = "jaxe.rimhud";
        
        // Cached detection results
        public static bool RimWorldOfMagicActive { get; private set; }
        public static bool VanillaPsycastsExpandedActive { get; private set; }
        public static bool CombatExtendedActive { get; private set; }
        public static bool HospitalityActive { get; private set; }
        public static bool DubsBadHygieneActive { get; private set; }
        public static bool SimpleSidearmsActive { get; private set; }
        public static bool CharacterEditorActive { get; private set; }
        public static bool RimHUDActive { get; private set; }
        
        static ModCompatibility()
        {
            // Detect which mods are active
            RimWorldOfMagicActive = ModsConfig.IsActive(RimWorldOfMagic_PackageId);
            VanillaPsycastsExpandedActive = ModsConfig.IsActive(VanillaPsycastsExpanded_PackageId);
            CombatExtendedActive = ModsConfig.IsActive(CombatExtended_PackageId);
            HospitalityActive = ModsConfig.IsActive(Hospitality_PackageId);
            DubsBadHygieneActive = ModsConfig.IsActive(DubsBadHygiene_PackageId);
            SimpleSidearmsActive = ModsConfig.IsActive(SimpleSidearms_PackageId);
            CharacterEditorActive = ModsConfig.IsActive(CharacterEditor_PackageId);
            RimHUDActive = ModsConfig.IsActive(RimHUD_PackageId);
            
            // Log status
            bool anyActive = RimWorldOfMagicActive || VanillaPsycastsExpandedActive || 
                             CombatExtendedActive || HospitalityActive || 
                             DubsBadHygieneActive || SimpleSidearmsActive ||
                             CharacterEditorActive || RimHUDActive;
            
            if (anyActive)
            {
                Log.Message("[Isekai Leveling] Compatibility patches active:");
                if (RimWorldOfMagicActive)
                    Log.Message("  - RimWorld of Magic: Stats affect Mana and Stamina");
                if (VanillaPsycastsExpandedActive)
                    Log.Message("  - Vanilla Psycasts Expanded: Stats affect Psyfocus");
                if (CombatExtendedActive)
                    Log.Message("  - Combat Extended: Stats affect Accuracy, Recoil, Reload");
                if (HospitalityActive)
                    Log.Message("  - Hospitality: CHA affects Guest recruitment");
                if (DubsBadHygieneActive)
                    Log.Message("  - Dubs Bad Hygiene: VIT/WIS affect Hygiene resistance");
                if (SimpleSidearmsActive)
                    Log.Message("  - Simple Sidearms: DEX affects Weapon swap speed");
                if (CharacterEditorActive)
                    Log.Message("  - Character Editor: Isekai stats section added to character creation");
                if (RimHUDActive)
                    Log.Message("  - RimHUD: Custom widgets for Level, Rank, and XP");
            }
            
            // Initialize compatibility modules
            InitializeModule(RimWorldOfMagicActive, "RimWorld of Magic", RoMCompatibility.Initialize);
            InitializeModule(VanillaPsycastsExpandedActive, "Vanilla Psycasts Expanded", VPECompatibility.Initialize);
            InitializeModule(CombatExtendedActive, "Combat Extended", CECompatibility.Initialize);
            InitializeModule(HospitalityActive, "Hospitality", HospitalityCompatibility.Initialize);
            InitializeModule(DubsBadHygieneActive, "Dubs Bad Hygiene", DBHCompatibility.Initialize);
            InitializeModule(SimpleSidearmsActive, "Simple Sidearms", SimpleSidearmsCompatibility.Initialize);
            InitializeModule(CharacterEditorActive, "Character Editor", CharacterEditorCompatibility.Initialize);
        }
        
        private static void InitializeModule(bool isActive, string modName, Action initAction)
        {
            if (!isActive) return;
            
            try
            {
                initAction();
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Failed to initialize {modName} compatibility: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Apply all compatibility effects for a pawn based on their Isekai stats.
        /// Call this when stats change.
        /// </summary>
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            if (pawn == null || comp == null) return;
            
            if (RimWorldOfMagicActive)
                RoMCompatibility.ApplyStatEffects(pawn, comp);
            
            if (VanillaPsycastsExpandedActive)
                VPECompatibility.ApplyStatEffects(pawn, comp);
                
            if (CombatExtendedActive)
                CECompatibility.ApplyStatEffects(pawn, comp);
                
            if (HospitalityActive)
                HospitalityCompatibility.ApplyStatEffects(pawn, comp);
                
            if (DubsBadHygieneActive)
                DBHCompatibility.ApplyStatEffects(pawn, comp);
                
            if (SimpleSidearmsActive)
                SimpleSidearmsCompatibility.ApplyStatEffects(pawn, comp);
                
            if (CharacterEditorActive)
                CharacterEditorCompatibility.ApplyStatEffects(pawn, comp);
        }
    }
}
