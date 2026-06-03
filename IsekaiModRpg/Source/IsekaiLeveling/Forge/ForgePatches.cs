using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Forge
{
    /// <summary>
    /// Harmony patches for the Forge system:
    /// - Weapon mastery XP gain on melee/ranged hits and kills
    /// - Rune proc effects (lifesteal, stun, fire, frost, splash)
    /// </summary>
    /// 
    // ══════════════════════════════════════════
    //  MASTERY XP — hooks into the existing PreApplyDamage postfix
    // ══════════════════════════════════════════

    /// <summary>
    /// Awards weapon mastery XP when a humanlike pawn deals combat damage.
    /// Also triggers rune proc effects (lifesteal, stun, fire, frost).
    /// Hooks into Pawn.PreApplyDamage (same hook as XPPatches.Patch_CombatXP_Melee).
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_ForgeCombat
    {
        // Recursion guard: Fire rune's TakeDamage(Flame) re-enters this postfix.
        // Without this, 2+ fire runes cause exponential damage stacking / stack overflow.
        [System.ThreadStatic]
        private static bool _inRuneProc;

        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref DamageInfo dinfo)
        {
            try
            {
                if (_inRuneProc) return;
                if (!(dinfo.Instigator is Pawn attacker)) return;

                // Skip non-combat damage (including rune-generated Flame/Burn to prevent recursion)
                if (dinfo.Def == DamageDefOf.Extinguish ||
                    dinfo.Def == DamageDefOf.SurgicalCut ||
                    dinfo.Def == DamageDefOf.ExecutionCut ||
                    dinfo.Def == DamageDefOf.Stun ||
                    dinfo.Def == DamageDefOf.EMP ||
                    dinfo.Def == DamageDefOf.Flame ||
                    dinfo.Def == DamageDefOf.Burn)
                    return;

                // Skip friendly fire
                if (attacker.Faction != null && __instance.Faction != null &&
                    !attacker.Faction.HostileTo(__instance.Faction))
                    return;

                // Only humanlike pawns gain mastery XP
                if (!attacker.RaceProps.Humanlike) return;

                var comp = IsekaiComponent.GetCached(attacker);
                if (comp == null) return;

                // ── Weapon Mastery XP ──
                if (IsekaiLevelingSettings.EnableWeaponMastery)
                {
                    string weaponDefName = attacker.equipment?.Primary?.def?.defName;
                    if (weaponDefName != null)
                    {
                        int masteryXP = Mathf.Max(1, Mathf.RoundToInt(1f * IsekaiLevelingSettings.MasteryXPMultiplier));
                        comp.weaponMastery.AddMasteryXP(weaponDefName, masteryXP);
                    }
                }

                // ── Rune Proc Effects ──
                if (IsekaiLevelingSettings.EnableForgeSystem)
                {
                    var weaponComp = attacker.equipment?.Primary?.TryGetComp<CompForgeEnhancement>();
                    if (weaponComp != null && weaponComp.UsedRuneSlots > 0)
                    {
                        _inRuneProc = true;
                        try { ProcessRuneProcs(attacker, __instance, dinfo, weaponComp); }
                        finally { _inRuneProc = false; }
                    }
                }
            }
            catch { }
        }

        private static void ProcessRuneProcs(Pawn attacker, Pawn victim, DamageInfo dinfo, CompForgeEnhancement weaponComp)
        {
            var runesWithRanks = weaponComp.GetAppliedRunesWithRanks();
            foreach (var (rune, rank) in runesWithRanks)
            {
                if (!rune.IsProcEffect) continue;
                float mag = rune.GetMagnitudeForRank(rank);

                switch (rune.effectType)
                {
                    case RuneEffectType.Lifesteal:
                        // Heal attacker by % of damage dealt
                        float healAmount = dinfo.Amount * mag;
                        if (healAmount > 0.5f && attacker.health?.hediffSet != null)
                        {
                            List<Hediff_Injury> injuries = new List<Hediff_Injury>();
                            attacker.health.hediffSet.GetHediffs(ref injuries);
                            for (int j = 0; j < injuries.Count; j++)
                            {
                                if (healAmount <= 0f) break;
                                float heal = Mathf.Min(injuries[j].Severity, healAmount);
                                injuries[j].Heal(heal);
                                healAmount -= heal;
                            }
                        }
                        break;

                    case RuneEffectType.StunChance:
                        // Chance to stun target
                        if (Rand.Chance(mag) && victim.stances != null)
                        {
                            victim.stances.stunner.StunFor(60, attacker);
                        }
                        break;

                    case RuneEffectType.FireDamage:
                        // Bonus fire damage — probabilistic + capped to prevent runaway stacking
                        if (victim.Map != null && Rand.Chance(0.5f))
                        {
                            // Cap fire damage at 2x the original hit so it can't one-shot through armor
                            float fireDmg = Mathf.Min(dinfo.Amount * mag, dinfo.Amount * 2f);
                            if (fireDmg > 0.5f)
                            {
                                DamageInfo fireDinfo = new DamageInfo(DamageDefOf.Flame, fireDmg, 0f,
                                    -1f, attacker, null, null, DamageInfo.SourceCategory.ThingOrUnknown);
                                victim.TakeDamage(fireDinfo);
                            }
                        }
                        break;

                    case RuneEffectType.SlowChance:
                        // Chance to slow target (apply a movement penalty hediff)
                        if (Rand.Chance(mag))
                        {
                            var slowHediff = HediffDefOf.Hypothermia;
                            if (slowHediff != null)
                            {
                                var existing = victim.health?.hediffSet?.GetFirstHediffOfDef(slowHediff);
                                if (existing == null)
                                {
                                    var hediff = HediffMaker.MakeHediff(slowHediff, victim);
                                    hediff.Severity = 0.15f; // Mild slowdown
                                    victim.health?.AddHediff(hediff);
                                }
                            }
                        }
                        break;

                    case RuneEffectType.AoESplash:
                        // Deal reduced damage to adjacent pawns (melee only)
                        bool isMelee = dinfo.Weapon != null && dinfo.Weapon.IsMeleeWeapon;
                        if (isMelee && victim.Map != null)
                        {
                            float splashDmg = dinfo.Amount * mag;
                            foreach (var cell in GenAdj.CellsAdjacent8Way(victim))
                            {
                                if (!cell.InBounds(victim.Map)) continue;
                                foreach (Thing thing in cell.GetThingList(victim.Map))
                                {
                                    if (thing is Pawn nearby && nearby != victim && nearby != attacker &&
                                        nearby.Faction != null && attacker.Faction != null &&
                                        nearby.Faction.HostileTo(attacker.Faction))
                                    {
                                        DamageInfo splashInfo = new DamageInfo(dinfo.Def, splashDmg, 0f,
                                            -1f, attacker, null, null, DamageInfo.SourceCategory.ThingOrUnknown);
                                        nearby.TakeDamage(splashInfo);
                                    }
                                }
                            }
                        }
                        break;
                }
            }
        }
    }

    // ══════════════════════════════════════════
    //  MASTERY KILL BONUS — bonus XP on kill
    // ══════════════════════════════════════════

    /// <summary>
    /// Awards +5 bonus weapon mastery XP when the pawn kills an enemy.
    /// Hooks into Pawn.Kill (same hook as MobRankRewards).
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_ForgeMasteryKillBonus
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (!IsekaiLevelingSettings.EnableWeaponMastery) return;
                if (dinfo == null || !(dinfo.Value.Instigator is Pawn killer)) return;
                if (!killer.RaceProps.Humanlike) return;

                // Skip friendly kills
                if (killer.Faction != null && __instance.Faction != null &&
                    !killer.Faction.HostileTo(__instance.Faction))
                    return;

                string weaponDefName = killer.equipment?.Primary?.def?.defName;
                if (weaponDefName == null) return;

                var comp = IsekaiComponent.GetCached(killer);
                if (comp?.weaponMastery == null) return;

                int bonusXP = Mathf.Max(1, Mathf.RoundToInt(5f * IsekaiLevelingSettings.MasteryXPMultiplier));
                comp.weaponMastery.AddMasteryXP(weaponDefName, bonusXP);
            }
            catch { }
        }
    }
}
