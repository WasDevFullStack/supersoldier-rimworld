using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiLeveling.WorldBoss
{
    /// <summary>
    /// Handles visual scaling, hitbox enlargement, and team cohesion for World Boss encounters.
    /// 
    /// SCALING: Pure per-pawn approach. The shared PawnKindDef is NEVER modified.
    /// A Harmony postfix on EVERY PawnRenderNode subclass that overrides GraphicFor
    /// intercepts the graphic and scales it UP for world boss pawns only.
    /// Wave mobs are naturally normal-sized because the def is untouched.
    /// 
    /// INFIGHTING: ManhunterPermanent is used for animal aggression. A postfix on
    /// GenHostility.HostileTo prevents boss-team pawns from targeting each other.
    /// </summary>
    public static class WorldBossSizePatch
    {
        public const float BOSS_SIZE_SCALE = 2.5f;

        // Extra HP multiplier applied ON TOP of the Nation-rank * elite multiplier
        // (so a world boss is meaningfully tankier than a "regular" Nation-rank
        // creature would be). The full chain ends up roughly at:
        //
        //   bodyPartHP × HealthScale × HEALTH_SCALE_BOOST × rank(12) × elite(1.2) × HEALTH_BOOST
        //   = bodyPartHP × HealthScale × 3 × 12 × 1.2 × 5  ≈  216×
        //
        // Tuned so a Nation boss can survive a player + raid dogpile but still
        // dies in a sustained ~minute-long fight with high-DPS weapons.
        public const float BOSS_HEALTH_BOOST = 5f;
        public const float HEALTH_SCALE_BOOST = 3f;

        // Per-pawn tracking — re-populated on load via WorldBossMapComponent.ExposeData
        private static readonly HashSet<int> worldBossIds = new HashSet<int>();
        private static readonly HashSet<int> bossTeamIds = new HashSet<int>(); // boss + wave mobs

        // ─────────────────────────────────────────────
        //  Registration
        // ─────────────────────────────────────────────

        public static void RegisterWorldBoss(Pawn pawn)
        {
            if (pawn == null) return;
            worldBossIds.Add(pawn.thingIDNumber);
            bossTeamIds.Add(pawn.thingIDNumber);
            Log.Message($"[Isekai WorldBoss] Registered world boss: {pawn.LabelCap} (ID: {pawn.thingIDNumber})");
            ForceGraphicsRefresh(pawn);
        }

        public static void RegisterWaveMob(Pawn pawn)
        {
            if (pawn == null) return;
            bossTeamIds.Add(pawn.thingIDNumber);
        }

        public static void UnregisterWorldBoss(Pawn pawn)
        {
            if (pawn == null) return;
            worldBossIds.Remove(pawn.thingIDNumber);
            bossTeamIds.Remove(pawn.thingIDNumber);
            ForceGraphicsRefresh(pawn);
        }

        public static void ClearAll()
        {
            worldBossIds.Clear();
            bossTeamIds.Clear();
        }

        public static bool IsWorldBoss(Pawn pawn)
        {
            return pawn != null && worldBossIds.Contains(pawn.thingIDNumber);
        }

        // ─────────────────────────────────────────────
        //  Graphics Refresh
        // ─────────────────────────────────────────────

        public static void ForceGraphicsRefresh(Pawn pawn)
        {
            try
            {
                if (pawn?.Drawer == null) return;
                var renderer = pawn.Drawer.renderer;
                if (renderer == null) return;

                MethodInfo setDirty = AccessTools.Method(renderer.GetType(), "SetAllGraphicsDirty");
                if (setDirty != null)
                {
                    setDirty.Invoke(renderer, null);
                    return;
                }

                FieldInfo graphicsField = AccessTools.Field(renderer.GetType(), "graphics");
                if (graphicsField != null)
                {
                    object graphics = graphicsField.GetValue(renderer);
                    MethodInfo resolve = AccessTools.Method(graphics?.GetType(), "ResolveAllGraphics");
                    resolve?.Invoke(graphics, null);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai WorldBoss] ForceGraphicsRefresh failed: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────
        //  Harmony Patches
        // ─────────────────────────────────────────────

        public static void ApplyPatches(Harmony harmony)
        {
            try
            {
                // 1) Pawn.BodySize — hitbox scaling for world bosses
                var bodySizeGetter = AccessTools.PropertyGetter(typeof(Pawn), "BodySize");
                if (bodySizeGetter != null)
                {
                    harmony.Patch(bodySizeGetter,
                        postfix: new HarmonyMethod(typeof(WorldBossSizePatch), nameof(BodySize_Postfix)));
                    Log.Message("[Isekai WorldBoss] BodySize hitbox patch applied");
                }

                // 1b) Pawn.HealthScale — body part HP scaling for world bosses.
                //     BodyPartDef.GetMaxHealth multiplies by HealthScale, so boosting
                //     HealthScale is the cleanest way to scale every body part at once
                //     without a per-part patch. The existing rank multiplier in
                //     MobRankStatPatches stacks on top of this.
                var healthScaleGetter = AccessTools.PropertyGetter(typeof(Pawn), "HealthScale");
                if (healthScaleGetter != null)
                {
                    harmony.Patch(healthScaleGetter,
                        postfix: new HarmonyMethod(typeof(WorldBossSizePatch), nameof(HealthScale_Postfix)));
                    Log.Message("[Isekai WorldBoss] HealthScale body-HP patch applied");
                }

                // 2) Patch EVERY PawnRenderNode subclass that overrides GraphicFor.
                //    The base class patch alone doesn't work — Harmony postfixes on a
                //    virtual method do NOT fire for overriding implementations.
                //    We must patch each override individually.
                PatchAllGraphicForOverrides(harmony);

                // 3) GenHostility.HostileTo — prevent boss-team infighting.
                MethodInfo hostileTo = AccessTools.Method(typeof(GenHostility), "HostileTo",
                    new[] { typeof(Thing), typeof(Thing) });
                if (hostileTo != null)
                {
                    harmony.Patch(hostileTo,
                        postfix: new HarmonyMethod(typeof(WorldBossSizePatch),
                            nameof(HostileTo_Postfix)));
                    Log.Message("[Isekai WorldBoss] HostileTo patch applied (boss-team cohesion)");
                }
                else
                {
                    Log.Warning("[Isekai WorldBoss] GenHostility.HostileTo not found!");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai WorldBoss] Patch failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Discovers and patches ALL PawnRenderNode subclasses that override GraphicFor(Pawn).
        /// This is the nuclear option — we don't guess which subclass handles animal bodies,
        /// we patch every single one of them so our postfix always fires.
        /// </summary>
        private static void PatchAllGraphicForOverrides(Harmony harmony)
        {
            // First, find the base PawnRenderNode type
            Type baseNodeType = AccessTools.TypeByName("Verse.PawnRenderNode")
                ?? AccessTools.TypeByName("PawnRenderNode");

            if (baseNodeType == null)
            {
                Log.Warning("[Isekai WorldBoss] PawnRenderNode type not found! Visual scaling will not work.");
                return;
            }

            // Get the base GraphicFor method
            MethodInfo baseMethod = AccessTools.Method(baseNodeType, "GraphicFor",
                new[] { typeof(Pawn) });

            if (baseMethod == null)
            {
                Log.Warning("[Isekai WorldBoss] PawnRenderNode.GraphicFor(Pawn) not found! Visual scaling will not work.");
                return;
            }

            // Patch the base class
            var postfix = new HarmonyMethod(typeof(WorldBossSizePatch), nameof(GraphicFor_Postfix));
            harmony.Patch(baseMethod, postfix: postfix);
            Log.Message("[Isekai WorldBoss] Patched PawnRenderNode.GraphicFor (base)");

            // Find ALL subclasses and patch any that override GraphicFor
            int patchCount = 0;
            foreach (Type subType in baseNodeType.Assembly.GetTypes())
            {
                if (!subType.IsSubclassOf(baseNodeType)) continue;
                if (subType.IsAbstract) continue;

                try
                {
                    // Check if this subclass declares its own GraphicFor override
                    MethodInfo overrideMethod = subType.GetMethod("GraphicFor",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(Pawn) }, null);

                    if (overrideMethod != null)
                    {
                        harmony.Patch(overrideMethod, postfix: postfix);
                        patchCount++;
                        Log.Message($"[Isekai WorldBoss] Patched {subType.Name}.GraphicFor");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[Isekai WorldBoss] Failed to patch {subType.Name}: {ex.Message}");
                }
            }

            Log.Message($"[Isekai WorldBoss] Total GraphicFor patches: {patchCount + 1} (base + {patchCount} overrides)");
        }

        // ─────────────────────────────────────────────
        //  Postfix: Pawn.BodySize (hitbox)
        // ─────────────────────────────────────────────

        public static void BodySize_Postfix(ref float __result, Pawn __instance)
        {
            if (__instance != null && worldBossIds.Contains(__instance.thingIDNumber))
            {
                __result *= BOSS_SIZE_SCALE;
            }
        }

        // ─────────────────────────────────────────────
        //  Postfix: Pawn.HealthScale (body part HP)
        //  Vanilla BodyPartDef.GetMaxHealth = baseHP * HealthScale.
        //  Boosting HealthScale here scales every body part on the boss in one
        //  shot, without needing a per-part patch.
        // ─────────────────────────────────────────────

        public static void HealthScale_Postfix(ref float __result, Pawn __instance)
        {
            if (__instance != null && worldBossIds.Contains(__instance.thingIDNumber))
            {
                __result *= HEALTH_SCALE_BOOST;
            }
        }

        // ─────────────────────────────────────────────
        //  Postfix: PawnRenderNode*.GraphicFor
        //  Scales UP boss graphics only.
        //  Wave mobs and all other pawns are untouched.
        //  No shared def mutation occurs anywhere.
        // ─────────────────────────────────────────────

        public static void GraphicFor_Postfix(ref Graphic __result, Pawn pawn)
        {
            try
            {
                if (__result == null || pawn == null) return;
                if (!worldBossIds.Contains(pawn.thingIDNumber)) return;

                Vector2 scaledSize = __result.drawSize * BOSS_SIZE_SCALE;

                // GraphicDatabase.Get caches by (type, path, shader, drawSize, color, colorTwo).
                // Requesting a different drawSize returns a distinct cached Graphic.
                Graphic newGraphic = GraphicDatabase.Get(
                    __result.GetType(),
                    __result.path,
                    __result.Shader,
                    scaledSize,
                    __result.color,
                    __result.colorTwo);

                if (newGraphic != null)
                    __result = newGraphic;
            }
            catch { }
        }

        // ─────────────────────────────────────────────
        //  Postfix: GenHostility.HostileTo
        //  Prevents boss-team pawns from attacking
        //  each other despite ManhunterPermanent.
        // ─────────────────────────────────────────────

        public static void HostileTo_Postfix(ref bool __result, Thing a, Thing b)
        {
            if (!__result) return;
            if (bossTeamIds.Count == 0) return;

            Pawn pawnA = a as Pawn;
            Pawn pawnB = b as Pawn;
            if (pawnA == null || pawnB == null) return;

            if (bossTeamIds.Contains(pawnA.thingIDNumber) &&
                bossTeamIds.Contains(pawnB.thingIDNumber))
            {
                __result = false;
            }
        }
    }
}
