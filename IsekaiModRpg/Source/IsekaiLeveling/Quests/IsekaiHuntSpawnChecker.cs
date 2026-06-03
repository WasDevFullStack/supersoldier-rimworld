using RimWorld.Planet;
using Verse;
using IsekaiLeveling.WorldBoss;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Lightweight MapComponent added to Site maps during FinalizeInit.
    /// Acts as a tick-based safety net: if Map.FinalizeInit's spawn check
    /// missed for any reason (timing, exception), this will catch it
    /// within the first 5 seconds of the map existing.
    /// Self-removes after the check window expires.
    /// </summary>
    public class IsekaiHuntSpawnChecker : MapComponent
    {
        private int ticksRemaining = 300; // Check for ~5 seconds (300 ticks)
        
        public IsekaiHuntSpawnChecker(Map map) : base(map) { }
        
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            
            if (ticksRemaining <= 0) return;
            ticksRemaining--;
            
            // Only check every 60 ticks (1 second)
            if (ticksRemaining % 60 != 0) return;
            
            // Only relevant for Site maps
            if (!(map.Parent is Site)) return;
            
            try
            {
                QuestPart_IsekaiWorldHunt.TrySpawnOnExistingMap(map);
                QuestPart_WorldBoss.TrySpawnOnExistingMap(map);
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Leveling] Safety-net spawn checker error: {ex.Message}");
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", 0);
        }
    }
}
