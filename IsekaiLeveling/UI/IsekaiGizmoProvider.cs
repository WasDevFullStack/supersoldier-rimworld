using System.Collections.Generic;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Provides gizmos for the Isekai leveling system.
    /// Currently empty - Status is accessed via right-click or the Status tab.
    /// Kept for future expansion (abilities, skills, etc.)
    /// </summary>
    public static class IsekaiGizmoProvider
    {
        public static IEnumerable<Gizmo> GetGizmos(Pawn pawn)
        {
            // Status panel is now accessed via right-click on pawn or the Status tab
            // No gizmos are returned to keep the gizmo bar clean
            yield break;
        }
    }
}
