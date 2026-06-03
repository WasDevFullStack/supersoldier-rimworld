using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Patches for Isekai trait mechanics that hook into pawn health events.
    /// - EchoOfDefeat: x2 XP for 3 days after being downed
    /// - Undying: Cheat death on lethal hit, 3-day cooldown
    /// </summary>
    /// 
    // ═══════════════════════════════════════════
    //  ECHO OF DEFEAT — x2 XP after downed
    // ═══════════════════════════════════════════

    /// <summary>
    /// When a pawn with EchoOfDefeat is downed, activate 3-day x2 XP bonus.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_EchoOfDefeat_Downed
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn_HealthTracker __instance)
        {
            try
            {
                // Access the pawn from the health tracker via Traverse
                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn == null) return;

                if (!IsekaiTraitHelper.HasTrait(pawn, IsekaiTraitHelper.EchoOfDefeat))
                    return;

                var comp = IsekaiComponent.GetCached(pawn);
                if (comp == null) return;

                // Activate x2 XP for 3 days (180000 ticks)
                int activateUntil = Find.TickManager.TicksGame + 180000;
                comp.echoOfDefeatActiveUntilTick = activateUntil;

                if (pawn.Faction != null && pawn.Faction.IsPlayer)
                {
                    Messages.Message(
                        $"[Isekai] {pawn.LabelShort}'s Echo of Defeat activates! x2 XP for 3 days.",
                        pawn, MessageTypeDefOf.PositiveEvent);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] EchoOfDefeat downed patch error: {ex.Message}");
            }
        }
    }

    // ═══════════════════════════════════════════
    //  UNDYING — Cheat death mechanic
    // ═══════════════════════════════════════════

    /// <summary>
    /// Prevent death for pawns with the Undying trait (3-day cooldown).
    /// When a pawn would die: heal lethal injuries, set HP to survivable level,
    /// start cooldown timer, and flash visual feedback.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Undying_CheatDeath
    {
        [HarmonyPrefix]
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo)
        {
            try
            {
                if (__instance == null) return true;
                if (!IsekaiTraitHelper.HasTrait(__instance, IsekaiTraitHelper.Undying))
                    return true;

                var comp = IsekaiComponent.GetCached(__instance);
                if (comp == null) return true;

                int currentTick = Find.TickManager.TicksGame;

                // Check cooldown (3 days = 180000 ticks)
                if (comp.undyingCooldownTick > currentTick)
                    return true; // Still on cooldown, allow death

                // Cheat death! Set cooldown
                comp.undyingCooldownTick = currentTick + 180000;

                // Heal lethal injuries to survivable state
                HealLethalDamage(__instance);

                // Visual + notification
                if (__instance.Spawned)
                {
                    MoteMaker.ThrowText(__instance.DrawPos, __instance.Map, "Undying!", Color.yellow);
                }

                if (__instance.Faction != null && __instance.Faction.IsPlayer)
                {
                    Find.LetterStack.ReceiveLetter(
                        "Undying Activated",
                        $"{__instance.LabelShort} has cheated death! The Undying trait saved them from a fatal blow.\n\n" +
                        "Cooldown: 3 days before this can trigger again.\n" +
                        "They will feel 'Touched by Death' while the memory lingers.",
                        LetterDefOf.PositiveEvent, __instance);
                }

                return false; // Prevent the kill
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Undying cheat-death error: {ex.Message}");
                return true; // On error, allow death to prevent stuck states
            }
        }

        /// <summary>
        /// Heal injuries enough to prevent immediate re-death.
        /// Reduces all injury severity, clears blood loss, and ensures the pawn is stable.
        /// </summary>
        private static void HealLethalDamage(Pawn pawn)
        {
            var health = pawn.health;
            if (health == null) return;

            var hediffs = health.hediffSet.hediffs;
            for (int i = hediffs.Count - 1; i >= 0; i--)
            {
                var hediff = hediffs[i];

                // Heal injuries to 10% of their current severity
                if (hediff is Hediff_Injury injury)
                {
                    float healAmount = injury.Severity * 0.9f;
                    if (healAmount > 0f)
                        injury.Heal(healAmount);
                }
            }

            // Clear blood loss
            var bloodLoss = health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            if (bloodLoss != null)
            {
                bloodLoss.Severity = 0.1f;
            }

            // Force pawn out of downed state if they were downed
            if (pawn.Downed && !pawn.Dead)
            {
                // The pawn will naturally recover from downed state with reduced injuries
            }
        }
    }
}
