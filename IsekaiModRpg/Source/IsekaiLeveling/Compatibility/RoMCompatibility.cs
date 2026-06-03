using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using IsekaiLeveling.SkillTree;

namespace IsekaiLeveling.Compatibility
{
    /// <summary>
    /// Compatibility patches for RimWorld of Magic.
    /// 
    /// Stat Mappings (StatParts on RoM StatDefs):
    /// - INT → +Max Mana (each point above 5 = +2% max mana)
    /// - WIS → +Mana Regen (each point above 5 = +1.5% mana regen)
    /// - VIT → +Max Stamina (each point above 5 = +2% max stamina)
    /// - DEX → +Stamina Regen (each point above 5 = +1.5% stamina regen)
    /// 
    /// Harmony Patches (on RoM ability system):
    /// - STR → +Might damage (mightPwr multiplier)
    /// - DEX → -Might cooldown, -Stamina cost
    /// - WIS → +Magic damage (arcaneDmg), -Mana cost
    /// - INT → -Magic cooldown, +Psionic max
    /// - VIT → +Chi max (Monk)
    /// - CHA → +Summon duration, +Buff duration
    /// </summary>
    public static class RoMCompatibility
    {
        private static bool initialized = false;
        
        // Cached types from RoM assembly
        private static Type compAbilityUserMagicType;
        private static Type compAbilityUserMightType;
        private static Type magicAbilityType;
        private static Type mightAbilityType;
        internal static Type compSummonedType;
        internal static Type compGolemType;
        internal static Type tmPawnGolemType;
        private static Type hediffComp_ChiType;
        private static Type hediffComp_PsionicType;
        
        // CompSummoned.spawner / .temporary / .sustained reflection
        internal static FieldInfo field_summoned_spawner;
        internal static FieldInfo field_summoned_temporary;
        internal static FieldInfo field_summoned_sustained;
        // CompGolem.pawnMaster — master of the golem (the golemancer who built it)
        internal static FieldInfo field_golem_pawnMaster;
        
        // Cached fields for Harmony patches
        private static FieldInfo field_coolDown;     // CompAbilityUserTMBase.coolDown
        private static FieldInfo field_arcaneDmg;    // CompAbilityUserMagic.arcaneDmg
        private static FieldInfo field_mightPwr;     // CompAbilityUserMight.mightPwr
        private static FieldInfo field_chi_maxSev;   // HediffComp_Chi.maxSev
        private static FieldInfo field_summoned_ticksToDestroy; // CompSummoned.ticksToDestroy
        
        // Cached methods for Harmony patches
        private static MethodInfo method_MagicAbility_PostAbilityAttempt;
        private static MethodInfo method_MightAbility_PostAbilityAttempt;
        private static MethodInfo method_ActualManaCost;
        private static MethodInfo method_ActualStaminaCost;
        private static MethodInfo method_Chi_CompPostTick;
        private static MethodInfo method_Psionic_CompPostTick;
        private static MethodInfo method_Summoned_PostSpawnSetup;
        
        // Cached properties 
        private static PropertyInfo prop_MagicUser;   // MagicAbility.MagicUser
        private static PropertyInfo prop_MightUser;   // MightAbility.MightUser
        
        // Cached fields on CompAbilityUserMagic / CompAbilityUserMight for direct manipulation
        // (these are public float fields per RoM source — Need_Mana/Need_Stamina read them
        //  directly for MaxLevel, CurLevel clamp, and GainNeed regen formula)
        internal static FieldInfo field_maxMP;        // CompAbilityUserMagic.maxMP
        internal static FieldInfo field_mpRegenRate;  // CompAbilityUserMagic.mpRegenRate
        internal static FieldInfo field_maxSP;        // CompAbilityUserMight.maxSP
        internal static FieldInfo field_spRegenRate;  // CompAbilityUserMight.spRegenRate
        
        // Cached CompTick methods on the comps for Harmony patching
        internal static MethodInfo method_CompMagic_CompTick;
        internal static MethodInfo method_CompMight_CompTick;
        
        // Per-pawn baseline tracking for non-compounding multiplication.
        // Key = pawn.thingIDNumber. Tuple = (baseValue, lastAppliedValue).
        // baseValue = the unmultiplied RoM value (last seen when RoM updated it).
        // lastAppliedValue = what we wrote last; if field == lastAppliedValue, RoM hasn't touched it.
        internal static readonly System.Collections.Generic.Dictionary<int, (float baseVal, float lastApplied)> 
            magicMaxMPTracker = new System.Collections.Generic.Dictionary<int, (float, float)>();
        internal static readonly System.Collections.Generic.Dictionary<int, (float baseVal, float lastApplied)> 
            magicRegenTracker = new System.Collections.Generic.Dictionary<int, (float, float)>();
        internal static readonly System.Collections.Generic.Dictionary<int, (float baseVal, float lastApplied)> 
            mightMaxSPTracker = new System.Collections.Generic.Dictionary<int, (float, float)>();
        internal static readonly System.Collections.Generic.Dictionary<int, (float baseVal, float lastApplied)> 
            mightRegenTracker = new System.Collections.Generic.Dictionary<int, (float, float)>();
        
        // Base Chi maxSev per pawn (to avoid per-tick stacking)
        private static readonly System.Collections.Generic.Dictionary<int, float> chiBaseMaxSev 
            = new System.Collections.Generic.Dictionary<int, float>();
        
        // Base Psionic maxSev per pawn
        private static readonly System.Collections.Generic.Dictionary<int, float> psionicBaseMax 
            = new System.Collections.Generic.Dictionary<int, float>();
        
        public static void Initialize()
        {
            if (initialized) return;
            
            try
            {
                Assembly romAssembly = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == "TorannMagic")
                    {
                        romAssembly = assembly;
                        break;
                    }
                }
                
                if (romAssembly == null)
                {
                    Log.Warning("[Isekai Leveling] RoM assembly 'TorannMagic' not found.");
                    return;
                }
                
                // Cache types
                compAbilityUserMagicType = romAssembly.GetType("TorannMagic.CompAbilityUserMagic");
                compAbilityUserMightType = romAssembly.GetType("TorannMagic.CompAbilityUserMight");
                magicAbilityType = romAssembly.GetType("TorannMagic.MagicAbility");
                mightAbilityType = romAssembly.GetType("TorannMagic.MightAbility");
                compSummonedType = romAssembly.GetType("TorannMagic.CompSummoned");
                compGolemType = romAssembly.GetType("TorannMagic.Golems.CompGolem");
                tmPawnGolemType = romAssembly.GetType("TorannMagic.Golems.TMPawnGolem");
                hediffComp_ChiType = romAssembly.GetType("TorannMagic.HediffComp_Chi");
                hediffComp_PsionicType = romAssembly.GetType("TorannMagic.HediffComp_Psionic");
                
                // Cache base class type for shared fields
                var compAbilityUserTMBaseType = romAssembly.GetType("TorannMagic.CompAbilityUserTMBase");
                
                // Cache fields
                if (compAbilityUserTMBaseType != null)
                    field_coolDown = compAbilityUserTMBaseType.GetField("coolDown", BindingFlags.Public | BindingFlags.Instance);
                if (compAbilityUserMagicType != null)
                    field_arcaneDmg = compAbilityUserMagicType.GetField("arcaneDmg", BindingFlags.Public | BindingFlags.Instance);
                if (compAbilityUserMightType != null)
                    field_mightPwr = compAbilityUserMightType.GetField("mightPwr", BindingFlags.Public | BindingFlags.Instance);
                if (hediffComp_ChiType != null)
                    field_chi_maxSev = hediffComp_ChiType.GetField("maxSev", BindingFlags.Public | BindingFlags.Instance);
                if (compSummonedType != null)
                    field_summoned_ticksToDestroy = compSummonedType.GetField("ticksToDestroy", BindingFlags.Public | BindingFlags.Instance);
                if (compSummonedType != null)
                {
                    field_summoned_spawner = compSummonedType.GetField("spawner",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    field_summoned_temporary = compSummonedType.GetField("temporary",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    field_summoned_sustained = compSummonedType.GetField("sustained",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (compGolemType != null)
                {
                    field_golem_pawnMaster = compGolemType.GetField("pawnMaster",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                
                // Cache methods for Harmony patching
                if (magicAbilityType != null)
                {
                    method_MagicAbility_PostAbilityAttempt = magicAbilityType.GetMethod("PostAbilityAttempt", 
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    prop_MagicUser = magicAbilityType.GetProperty("MagicUser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (mightAbilityType != null)
                {
                    method_MightAbility_PostAbilityAttempt = mightAbilityType.GetMethod("PostAbilityAttempt",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                    prop_MightUser = mightAbilityType.GetProperty("MightUser", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }
                if (compAbilityUserMagicType != null)
                    method_ActualManaCost = compAbilityUserMagicType.GetMethod("ActualManaCost", BindingFlags.Public | BindingFlags.Instance);
                if (compAbilityUserMightType != null)
                    method_ActualStaminaCost = compAbilityUserMightType.GetMethod("ActualStaminaCost", BindingFlags.Public | BindingFlags.Instance);
                if (hediffComp_ChiType != null)
                    method_Chi_CompPostTick = hediffComp_ChiType.GetMethod("CompPostTick", BindingFlags.Public | BindingFlags.Instance);
                if (hediffComp_PsionicType != null)
                    method_Psionic_CompPostTick = hediffComp_PsionicType.GetMethod("CompPostTick", BindingFlags.Public | BindingFlags.Instance);
                if (compSummonedType != null)
                {
                    // CompSummoned doesn't override PostSpawnSetup — Harmony requires patching
                    // the declared (base) method. Patch ThingComp.PostSpawnSetup and filter by
                    // type inside the postfix.
                    method_Summoned_PostSpawnSetup = typeof(ThingComp).GetMethod("PostSpawnSetup",
                        BindingFlags.Public | BindingFlags.Instance);
                }
                
                // Cache the public float fields on CompAbilityUserMagic / CompAbilityUserMight
                // that drive Need_Mana / Need_Stamina behavior internally
                if (compAbilityUserMagicType != null)
                {
                    field_maxMP = compAbilityUserMagicType.GetField("maxMP", BindingFlags.Public | BindingFlags.Instance);
                    field_mpRegenRate = compAbilityUserMagicType.GetField("mpRegenRate", BindingFlags.Public | BindingFlags.Instance);
                    method_CompMagic_CompTick = compAbilityUserMagicType.GetMethod("CompTick",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }
                if (compAbilityUserMightType != null)
                {
                    field_maxSP = compAbilityUserMightType.GetField("maxSP", BindingFlags.Public | BindingFlags.Instance);
                    field_spRegenRate = compAbilityUserMightType.GetField("spRegenRate", BindingFlags.Public | BindingFlags.Instance);
                    method_CompMight_CompTick = compAbilityUserMightType.GetMethod("CompTick",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }
                
                // Apply harmony patches for the ability system hooks and need patches
                ApplyHarmonyPatches();
                
                initialized = true;
                Log.Message("[Isekai Leveling] RimWorld of Magic compatibility initialized successfully." +
                    " Stats affect: Mana Pool, Mana Regen, Stamina Pool, Stamina Regen, Damage, Cooldowns, Costs, Chi, Psionic, Summons, Buffs.");
            }
            catch (Exception ex)
            {
                Log.Error($"[Isekai Leveling] Error initializing RoM compatibility: {ex}");
                throw;
            }
        }
        
        private static void ApplyHarmonyPatches()
        {
            var harmony = new Harmony("IsekaiLeveling.RoMCompatibility");
            int applied = 0;
            
            // 1. MagicAbility.PostAbilityAttempt → INT cooldown + WIS damage context
            if (method_MagicAbility_PostAbilityAttempt != null)
            {
                try
                {
                    harmony.Patch(method_MagicAbility_PostAbilityAttempt,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.MagicAbility_PostAbilityAttempt_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: MagicAbility.PostAbilityAttempt — {ex.Message}"); }
            }
            
            // 2. MightAbility.PostAbilityAttempt → DEX cooldown
            if (method_MightAbility_PostAbilityAttempt != null)
            {
                try
                {
                    harmony.Patch(method_MightAbility_PostAbilityAttempt,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.MightAbility_PostAbilityAttempt_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: MightAbility.PostAbilityAttempt — {ex.Message}"); }
            }
            
            // 3. ActualManaCost → WIS cost reduction
            if (method_ActualManaCost != null)
            {
                try
                {
                    harmony.Patch(method_ActualManaCost,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.ActualManaCost_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: ActualManaCost — {ex.Message}"); }
            }
            
            // 4. ActualStaminaCost → DEX cost reduction
            if (method_ActualStaminaCost != null)
            {
                try
                {
                    harmony.Patch(method_ActualStaminaCost,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.ActualStaminaCost_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: ActualStaminaCost — {ex.Message}"); }
            }
            
            // 5. HediffComp_Chi.CompPostTick → VIT chi max
            if (method_Chi_CompPostTick != null)
            {
                try
                {
                    harmony.Patch(method_Chi_CompPostTick,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.Chi_CompPostTick_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: HediffComp_Chi.CompPostTick — {ex.Message}"); }
            }
            
            // 6. HediffComp_Psionic.CompPostTick → INT psionic max
            //    Transpiler (not postfix) because vanilla hardcodes the upper clamp at 100f
            //    inside the method and a postfix runs AFTER the clamp — see the patch comment.
            if (method_Psionic_CompPostTick != null)
            {
                try
                {
                    harmony.Patch(method_Psionic_CompPostTick,
                        transpiler: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.Psionic_CompPostTick_Transpiler)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: HediffComp_Psionic.CompPostTick — {ex.Message}"); }
            }
            
            // 7. CompSummoned.PostSpawnSetup → CHA summon duration
            if (method_Summoned_PostSpawnSetup != null)
            {
                try
                {
                    harmony.Patch(method_Summoned_PostSpawnSetup,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.CompSummoned_PostSpawnSetup_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: CompSummoned.PostSpawnSetup — {ex.Message}"); }
            }
            
            // 8. CompAbilityUserMagic.CompTick → directly multiply maxMP & mpRegenRate
            //    so Need_Mana.MaxLevel / CurLevel clamp / GainNeed regen all naturally use boosted values
            if (method_CompMagic_CompTick != null && field_maxMP != null && field_mpRegenRate != null)
            {
                try
                {
                    harmony.Patch(method_CompMagic_CompTick,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.CompMagic_CompTick_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: CompAbilityUserMagic.CompTick — {ex.Message}"); }
            }
            
            // 9. CompAbilityUserMight.CompTick → directly multiply maxSP & spRegenRate
            if (method_CompMight_CompTick != null && field_maxSP != null && field_spRegenRate != null)
            {
                try
                {
                    harmony.Patch(method_CompMight_CompTick,
                        postfix: new HarmonyMethod(typeof(RoMHarmonyPatches), nameof(RoMHarmonyPatches.CompMight_CompTick_Postfix)));
                    applied++;
                }
                catch (Exception ex) { Log.Warning($"[Isekai Leveling] RoM patch failed: CompAbilityUserMight.CompTick — {ex.Message}"); }
            }
            
            Log.Message($"[Isekai Leveling] RoM Harmony patches applied: {applied}/9");
        }
        
        /// <summary>
        /// Apply stat effects to a pawn's RoM components.
        /// Called when Isekai stats change.
        /// </summary>
        public static void ApplyStatEffects(Pawn pawn, IsekaiComponent comp)
        {
            // Force stat recalculation
            pawn.health?.capacities?.Notify_CapacityLevelsDirty();
        }
        
        /// <summary>Clear cached base values on game load to prevent stale data.</summary>
        public static void ClearCaches()
        {
            chiBaseMaxSev.Clear();
            psionicBaseMax.Clear();
            magicMaxMPTracker.Clear();
            magicRegenTracker.Clear();
            mightMaxSPTracker.Clear();
            mightRegenTracker.Clear();
        }
        
        // ══════════════════ RESOURCE POOL MULTIPLIERS (StatParts) ══════════════════
        
        /// <summary>
        /// Calculate the max mana multiplier based on INT stat.
        /// Base is 1.0, each INT above 5 adds 2%.
        /// At 100 INT = 1.0 + (95 * 0.02) = 2.9x max mana
        /// </summary>
        public static float GetMaxManaMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int intStat = comp.stats.intelligence;
            float bonus = (intStat - 5) * 0.02f;
            float result = 1f + Mathf.Clamp(bonus, -0.08f, 2.0f);
            // Stack passive tree bonus
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_MaxMana));
            return result;
        }
        
        /// <summary>
        /// Calculate mana regen multiplier based on WIS stat.
        /// Each WIS above 5 adds 1.5% regen.
        /// </summary>
        public static float GetManaRegenMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float bonus = (wisStat - 5) * 0.015f;
            float result = 1f + Mathf.Clamp(bonus, -0.06f, 1.5f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_ManaRegen));
            return result;
        }
        
        /// <summary>
        /// Calculate max stamina multiplier based on VIT stat.
        /// Each VIT above 5 adds 2%.
        /// </summary>
        public static float GetMaxStaminaMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int vitStat = comp.stats.vitality;
            float bonus = (vitStat - 5) * 0.02f;
            float result = 1f + Mathf.Clamp(bonus, -0.08f, 2.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_MaxStamina));
            return result;
        }
        
        /// <summary>
        /// Calculate stamina regen multiplier based on DEX stat.
        /// Each DEX above 5 adds 1.5%.
        /// </summary>
        public static float GetStaminaRegenMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int dexStat = comp.stats.dexterity;
            float bonus = (dexStat - 5) * 0.015f;
            float result = 1f + Mathf.Clamp(bonus, -0.06f, 1.5f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_StaminaRegen));
            return result;
        }
        
        // ══════════════════ DAMAGE MULTIPLIERS ══════════════════
        
        /// <summary>
        /// STR → Might damage multiplier.
        /// Each STR above 5 adds 1.5% might damage.
        /// At 100 STR = 1.0 + (95 * 0.015) = 2.425x
        /// </summary>
        public static float GetMightDamageMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int strStat = comp.stats.strength;
            float bonus = (strStat - 5) * 0.015f;
            float result = 1f + Mathf.Clamp(bonus, -0.06f, 2.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_MightDamage));
            return result;
        }
        
        /// <summary>
        /// WIS → Magic damage multiplier (affects arcaneDmg).
        /// Each WIS above 5 adds 1.5% magic damage.
        /// </summary>
        public static float GetMagicDamageMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float bonus = (wisStat - 5) * 0.015f;
            float result = 1f + Mathf.Clamp(bonus, -0.06f, 2.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_MagicDamage));
            return result;
        }
        
        // ══════════════════ COOLDOWN MULTIPLIERS ══════════════════
        
        /// <summary>
        /// INT → Magic cooldown reduction.
        /// Each INT above 5 reduces cooldown by 0.8%.
        /// At 100 INT = 1.0 - (95 * 0.008) = 0.24 → clamped to 0.3 (70% reduction max)
        /// </summary>
        public static float GetMagicCooldownMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int intStat = comp.stats.intelligence;
            float reduction = (intStat - 5) * 0.008f;
            float result = Mathf.Clamp(1f - reduction, 0.3f, 1.08f);
            // Passive tree reduces further (bonus is negative direction → multiply)
            float passiveBonus = GetPassiveBonus(comp, PassiveBonusType.RoM_MagicCooldown);
            if (passiveBonus != 0f) result *= Mathf.Max(0.3f, 1f - passiveBonus);
            return result;
        }
        
        /// <summary>
        /// DEX → Might cooldown reduction.
        /// Each DEX above 5 reduces cooldown by 0.8%.
        /// </summary>
        public static float GetMightCooldownMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int dexStat = comp.stats.dexterity;
            float reduction = (dexStat - 5) * 0.008f;
            float result = Mathf.Clamp(1f - reduction, 0.3f, 1.08f);
            float passiveBonus = GetPassiveBonus(comp, PassiveBonusType.RoM_MightCooldown);
            if (passiveBonus != 0f) result *= Mathf.Max(0.3f, 1f - passiveBonus);
            return result;
        }
        
        // ══════════════════ COST MULTIPLIERS ══════════════════
        
        /// <summary>
        /// WIS → Mana cost reduction.
        /// Each WIS above 5 reduces mana cost by 0.8%.
        /// At 100 WIS = 1.0 - (95 * 0.008) = 0.24 → clamped to 0.3
        /// </summary>
        public static float GetManaCostMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int wisStat = comp.stats.wisdom;
            float reduction = (wisStat - 5) * 0.008f;
            float result = Mathf.Clamp(1f - reduction, 0.3f, 1.08f);
            float passiveBonus = GetPassiveBonus(comp, PassiveBonusType.RoM_ManaCost);
            if (passiveBonus != 0f) result *= Mathf.Max(0.3f, 1f - passiveBonus);
            return result;
        }
        
        /// <summary>
        /// DEX → Stamina cost reduction.
        /// Each DEX above 5 reduces stamina cost by 0.8%.
        /// </summary>
        public static float GetStaminaCostMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int dexStat = comp.stats.dexterity;
            float reduction = (dexStat - 5) * 0.008f;
            float result = Mathf.Clamp(1f - reduction, 0.3f, 1.08f);
            float passiveBonus = GetPassiveBonus(comp, PassiveBonusType.RoM_StaminaCost);
            if (passiveBonus != 0f) result *= Mathf.Max(0.3f, 1f - passiveBonus);
            return result;
        }
        
        // ══════════════════ SPECIAL RESOURCE MULTIPLIERS ══════════════════
        
        /// <summary>
        /// VIT → Chi max multiplier (Monk class).
        /// Each VIT above 5 adds 2% to chi capacity.
        /// </summary>
        public static float GetChiMaxMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int vitStat = comp.stats.vitality;
            float bonus = (vitStat - 5) * 0.02f;
            float result = 1f + Mathf.Clamp(bonus, -0.08f, 2.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_ChiMax));
            return result;
        }
        
        /// <summary>
        /// INT → Psionic max multiplier.
        /// Each INT above 5 adds 2% to psionic energy capacity.
        /// </summary>
        public static float GetPsionicMaxMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int intStat = comp.stats.intelligence;
            float bonus = (intStat - 5) * 0.02f;
            float result = 1f + Mathf.Clamp(bonus, -0.08f, 2.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_PsionicMax));
            return result;
        }
        
        // ══════════════════ DURATION MULTIPLIERS ══════════════════
        
        /// <summary>
        /// CHA → Summon duration multiplier.
        /// Each CHA above 5 adds 2% summon duration.
        /// </summary>
        public static float GetSummonDurationMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int chaStat = comp.stats.charisma;
            float bonus = (chaStat - 5) * 0.02f;
            float result = 1f + Mathf.Clamp(bonus, -0.08f, 3.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_SummonDuration));
            return result;
        }
        
        /// <summary>
        /// CHA → Buff duration multiplier (increases initial severity).
        /// Each CHA above 5 adds 1.5% buff duration.
        /// </summary>
        public static float GetBuffDurationMultiplier(IsekaiComponent comp)
        {
            if (comp?.stats == null) return 1f;
            int chaStat = comp.stats.charisma;
            float bonus = (chaStat - 5) * 0.015f;
            float result = 1f + Mathf.Clamp(bonus, -0.06f, 2.0f);
            result *= (1f + GetPassiveBonus(comp, PassiveBonusType.RoM_BuffDuration));
            return result;
        }
        
        // ══════════════════ ACCESSORS FOR HARMONY PATCHES ══════════════════
        
        /// <summary>Get the Pawn from a MagicAbility or MightAbility instance.</summary>
        public static Pawn GetPawnFromAbility(object abilityInstance)
        {
            try
            {
                // PawnAbility has a Pawn property
                var pawnProp = abilityInstance.GetType().GetProperty("Pawn", BindingFlags.Public | BindingFlags.Instance);
                return pawnProp?.GetValue(abilityInstance) as Pawn;
            }
            catch { return null; }
        }
        
        /// <summary>Get the CooldownTicksLeft field/property from a PawnAbility instance.</summary>
        public static void SetCooldownTicksLeft(object abilityInstance, int value)
        {
            try
            {
                var prop = abilityInstance.GetType().GetProperty("CooldownTicksLeft", 
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(abilityInstance, value);
                    return;
                }
                var field = abilityInstance.GetType().GetField("CooldownTicksLeft",
                    BindingFlags.Public | BindingFlags.Instance);
                field?.SetValue(abilityInstance, value);
            }
            catch { }
        }
        
        public static int GetCooldownTicksLeft(object abilityInstance)
        {
            try
            {
                var prop = abilityInstance.GetType().GetProperty("CooldownTicksLeft",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null) return (int)prop.GetValue(abilityInstance);
                var field = abilityInstance.GetType().GetField("CooldownTicksLeft",
                    BindingFlags.Public | BindingFlags.Instance);
                if (field != null) return (int)field.GetValue(abilityInstance);
            }
            catch { }
            return 0;
        }
        
        /// <summary>Get the Pawn that owns a HediffComp (Chi/Psionic).</summary>
        public static Pawn GetPawnFromHediffComp(HediffComp comp)
        {
            return comp?.parent?.pawn;
        }
        
        /// <summary>Get/set Chi maxSev, tracking the base value to avoid per-tick stacking.</summary>
        public static float GetBoostedChiMax(HediffComp chiComp, IsekaiComponent isekaiComp)
        {
            if (field_chi_maxSev == null || chiComp == null) return 100f;
            
            float currentMax = (float)field_chi_maxSev.GetValue(chiComp);
            int pawnId = chiComp.parent.pawn.thingIDNumber;
            
            // Store the first value we see as the base
            if (!chiBaseMaxSev.ContainsKey(pawnId))
                chiBaseMaxSev[pawnId] = currentMax;
            
            float baseMax = chiBaseMaxSev[pawnId];
            return baseMax * GetChiMaxMultiplier(isekaiComp);
        }
        
        /// <summary>Compute boosted psionic max. Psionic doesn't have a field — it uses local maxSev = 100.</summary>
        public static float GetBoostedPsionicMax(IsekaiComponent isekaiComp)
        {
            return 100f * GetPsionicMaxMultiplier(isekaiComp);
        }
        
        /// <summary>Get the summoner pawn from a CompSummoned component.</summary>
        public static Pawn GetSummonerPawn(ThingComp summonedComp)
        {
            try
            {
                if (summonedComp == null) return null;
                // CompSummoned exposes 'spawner' (field) / 'Spawner' (property) — the pawn who cast the summon
                if (field_summoned_spawner != null)
                {
                    var val = field_summoned_spawner.GetValue(summonedComp) as Pawn;
                    if (val != null) return val;
                }
                // Fallback via property lookup (handles RoM versions with renamed members)
                var spawnerProp = summonedComp.GetType().GetProperty("Spawner", BindingFlags.Public | BindingFlags.Instance);
                if (spawnerProp != null) return spawnerProp.GetValue(summonedComp) as Pawn;
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Return true if a CompSummoned represents a permanent / sustained summon
        /// (e.g. the Shaman's Guardian Spirit), false for timed summons like Spirit Wolves.
        /// Reads the 'temporary' field when available.
        /// </summary>
        public static bool IsPermanentSummon(ThingComp summonedComp)
        {
            try
            {
                if (summonedComp == null) return false;
                if (field_summoned_temporary != null)
                {
                    object val = field_summoned_temporary.GetValue(summonedComp);
                    if (val is bool b) return !b;
                }
                if (field_summoned_sustained != null)
                {
                    object val = field_summoned_sustained.GetValue(summonedComp);
                    if (val is bool s && s) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Get the TMPawnGolem (as a Pawn) belonging to a CompGolem's master, or the master from a golem.
        /// Returns the master Pawn that owns this golem, or null if not a golem / no master.
        /// </summary>
        public static Pawn GetGolemMaster(Pawn golemPawn)
        {
            try
            {
                if (golemPawn == null || compGolemType == null || field_golem_pawnMaster == null) return null;
                var comp = golemPawn.AllComps?.FirstOrDefault(c => compGolemType.IsInstanceOfType(c));
                if (comp == null) return null;
                return field_golem_pawnMaster.GetValue(comp) as Pawn;
            }
            catch { return null; }
        }

        /// <summary>
        /// Return true if a pawn is a RoM golem (TMPawnGolem / has CompGolem).
        /// </summary>
        public static bool IsGolemPawn(Pawn pawn)
        {
            if (pawn == null) return false;
            try
            {
                if (tmPawnGolemType != null && tmPawnGolemType.IsInstanceOfType(pawn)) return true;
                if (compGolemType != null)
                {
                    return pawn.AllComps?.Any(c => compGolemType.IsInstanceOfType(c)) ?? false;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Enumerate all golems whose pawnMaster is the given master pawn.
        /// Walks map pawns on the master's current map only.
        /// </summary>
        public static System.Collections.Generic.IEnumerable<Pawn> GetGolemsOwnedBy(Pawn master)
        {
            if (master?.Map == null || compGolemType == null || field_golem_pawnMaster == null)
                yield break;
            var allPawns = master.Map.mapPawns?.AllPawnsSpawned;
            if (allPawns == null) yield break;
            foreach (var p in allPawns)
            {
                if (p == null || p.Dead || p.Destroyed) continue;
                var comp = p.AllComps?.FirstOrDefault(c => compGolemType.IsInstanceOfType(c));
                if (comp == null) continue;
                Pawn m = null;
                try { m = field_golem_pawnMaster.GetValue(comp) as Pawn; } catch { }
                if (m == master) yield return p;
            }
        }
        
        // ══════════════════ DETECTION HELPERS ══════════════════
        
        /// <summary>Check if a pawn has a magic class from RoM.</summary>
        public static bool HasMagicClass(Pawn pawn)
        {
            if (compAbilityUserMagicType == null) return false;
            try
            {
                var comp = pawn.AllComps?.FirstOrDefault(c => compAbilityUserMagicType.IsInstanceOfType(c));
                if (comp == null) return false;
                
                var isMageField = compAbilityUserMagicType.GetProperty("IsMagicUser");
                if (isMageField != null)
                    return (bool)isMageField.GetValue(comp);
            }
            catch { }
            return false;
        }
        
        /// <summary>Check if a pawn has a might class from RoM.</summary>
        public static bool HasMightClass(Pawn pawn)
        {
            if (compAbilityUserMightType == null) return false;
            try
            {
                var comp = pawn.AllComps?.FirstOrDefault(c => compAbilityUserMightType.IsInstanceOfType(c));
                if (comp == null) return false;
                
                var isMightField = compAbilityUserMightType.GetProperty("IsMightUser");
                if (isMightField != null)
                    return (bool)isMightField.GetValue(comp);
            }
            catch { }
            return false;
        }
        
        // ══════════════════ HELPERS ══════════════════
        
        private static float GetPassiveBonus(IsekaiComponent comp, PassiveBonusType bonusType)
        {
            try
            {
                if (comp?.passiveTree == null) return 0f;
                return comp.passiveTree.GetTotalBonus(bonusType);
            }
            catch { return 0f; }
        }
    }
}
