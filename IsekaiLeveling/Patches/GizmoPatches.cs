using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using IsekaiLeveling.UI;
using System.Linq;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Adds Isekai ability gizmos to pawns
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_AddAbilityGizmos
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            // Return original gizmos first
            foreach (var gizmo in __result)
            {
                yield return gizmo;
            }

            // Add Isekai gizmos if this is a player-controlled humanlike pawn
            if (__instance.RaceProps.Humanlike && __instance.Faction == Faction.OfPlayer)
            {
                foreach (var gizmo in UI.IsekaiGizmoProvider.GetGizmos(__instance))
                {
                    yield return gizmo;
                }
            }
        }
    }
    
    /// <summary>
    /// Detect right-click HOLD on Isekai pawns and show the status panel
    /// Requires holding right-click for ~0.3 seconds to avoid conflicts with other actions
    /// </summary>
    [HarmonyPatch(typeof(MapInterface), nameof(MapInterface.MapInterfaceOnGUI_BeforeMainTabs))]
    public static class Patch_DetectRightClickOnPawn
    {
        // Hold-to-open settings
        private const float HOLD_DURATION = 0.3f; // Seconds to hold right-click
        private const float MAX_MOVE_DISTANCE = 15f; // Pixels - cancel if mouse moves too far
        
        // Tracking state
        private static bool isHolding = false;
        private static float holdStartTime = 0f;
        private static Vector2 holdStartPos;
        private static IntVec3 holdStartCell;
        private static Pawn holdTargetPawn = null;
        private static bool panelOpened = false; // Prevent re-triggering while still holding
        
        [HarmonyPostfix]
        public static void Postfix()
        {
            // Only process if we're on a map and not in a menu
            if (Find.CurrentMap == null) return;
            if (Find.WindowStack.WindowsForcePause) return;
            if (Find.Targeter.IsTargeting) return;
            
            Vector2 mousePos = Event.current.mousePosition;
            
            // Handle right mouse button DOWN - start tracking hold
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
            {
                IntVec3 mouseCell = Verse.UI.MouseCell();
                if (!mouseCell.InBounds(Find.CurrentMap)) return;
                
                // Check if there's an Isekai pawn at this cell
                Pawn targetPawn = FindIsekaiPawnAtCell(mouseCell);
                
                if (targetPawn != null)
                {
                    // Start tracking the hold
                    isHolding = true;
                    holdStartTime = Time.realtimeSinceStartup;
                    holdStartPos = mousePos;
                    holdStartCell = mouseCell;
                    holdTargetPawn = targetPawn;
                    panelOpened = false;
                }
            }
            // Handle right mouse button UP - cancel hold
            else if (Event.current.type == EventType.MouseUp && Event.current.button == 1)
            {
                isHolding = false;
                holdTargetPawn = null;
                panelOpened = false;
            }
            // Check hold duration while holding
            else if (isHolding && holdTargetPawn != null && !panelOpened)
            {
                // Check if mouse moved too far (user is dragging to do something else)
                float mouseDist = Vector2.Distance(mousePos, holdStartPos);
                if (mouseDist > MAX_MOVE_DISTANCE)
                {
                    // Cancel the hold
                    isHolding = false;
                    holdTargetPawn = null;
                    return;
                }
                
                // Check if we've held long enough
                float holdDuration = Time.realtimeSinceStartup - holdStartTime;
                if (holdDuration >= HOLD_DURATION)
                {
                    // Open the status panel
                    OpenStatusPanel(holdTargetPawn);
                    panelOpened = true;
                    
                    // Consume the current event to prevent other actions
                    Event.current.Use();
                }
            }
        }
        
        private static Pawn FindIsekaiPawnAtCell(IntVec3 cell)
        {
            foreach (Thing thing in cell.GetThingList(Find.CurrentMap))
            {
                if (thing is Pawn p && p.Faction == Faction.OfPlayer && p.RaceProps.Humanlike)
                {
                    var comp = p.GetComp<IsekaiComponent>();
                    if (comp != null)
                    {
                        return p;
                    }
                }
            }
            return null;
        }
        
        private static void OpenStatusPanel(Pawn pawn)
        {
            // Get screen position to the RIGHT of the pawn
            Vector3 screenPos3D = Find.Camera.WorldToScreenPoint(pawn.DrawPos);
            // Position to the right of pawn with small gap, vertically centered
            Vector2 screenPos = new Vector2(screenPos3D.x + 40f, Screen.height - screenPos3D.y - 170f);
            
            // Open the Stats Attribution window in quick mode (no pause, easily dismissable)
            Find.WindowStack.Add(new Window_StatsAttribution(pawn, screenPos, quickMode: true));
        }
    }

    /// <summary>
    /// Adds "Open Forge" and "Open Runic Station" gizmos to the respective buildings.
    /// Patches Thing.GetGizmos since Building_WorkTable inherits but does not declare it.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetGizmos))]
    public static class Patch_ForgeBuildingGizmos
    {
        [HarmonyPostfix]
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Thing __instance)
        {
            foreach (var gizmo in __result)
                yield return gizmo;

            if (!(__instance is Building_WorkTable))
                yield break;

            if (__instance.def.defName == "Isekai_Forge")
            {
                yield return new Command_Action
                {
                    defaultLabel = "Isekai_Gizmo_OpenForge".Translate(),
                    defaultDesc = "Isekai_Gizmo_OpenForge_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Building/Isekai_ForgeIcon", false) ?? TexCommand.DesirePower,
                    action = () => Find.WindowStack.Add(new Forge.Window_Forge(__instance.Map))
                };
            }
            else if (__instance.def.defName == "Isekai_RunicStation")
            {
                yield return new Command_Action
                {
                    defaultLabel = "Isekai_Gizmo_OpenRunicStation".Translate(),
                    defaultDesc = "Isekai_Gizmo_OpenRunicStation_Desc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("Building/Isekai_RunicIcon", false) ?? TexCommand.DesirePower,
                    action = () => Find.WindowStack.Add(new Forge.Window_RunicStation(__instance.Map))
                };
            }
        }
    }

    /// <summary>
    /// Adds right-click float menu options on the Forge and Runic Station buildings
    /// so players can open them without needing to find the gizmo button.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetFloatMenuOptions))]
    public static class Patch_ForgeBuildingFloatMenu
    {
        [HarmonyPostfix]
        public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result, Thing __instance, Pawn selPawn)
        {
            foreach (var opt in __result)
                yield return opt;

            if (!(__instance is Building_WorkTable))
                yield break;

            if (__instance.def.defName == "Isekai_Forge")
            {
                yield return new FloatMenuOption("Isekai_FloatMenu_OpenForge".Translate(), () =>
                {
                    Find.WindowStack.Add(new Forge.Window_Forge(__instance.Map));
                });
            }
            else if (__instance.def.defName == "Isekai_RunicStation")
            {
                yield return new FloatMenuOption("Isekai_FloatMenu_OpenRunicStation".Translate(), () =>
                {
                    Find.WindowStack.Add(new Forge.Window_RunicStation(__instance.Map));
                });
            }
        }
    }
}
