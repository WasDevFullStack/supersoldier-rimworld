using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace IsekaiLeveling.Patches
{
    /// <summary>
    /// Preserves Isekai leveling data across death and resurrection.
    /// 
    /// Problem: When a pawn dies and is resurrected (Mortis Repelio, resurrector mech serum,
    /// Anomaly mechanics, etc.), their IsekaiComponent data can be lost. This happens because
    /// some resurrection paths recreate the pawn's comps, triggering InitializeComps → our
    /// Harmony patch creates a fresh IsekaiComponent with randomly rolled stats, overwriting
    /// the original level 190 pawn with a random level ~7 E-rank.
    /// 
    /// Solution: Cache all Isekai progression data when any humanlike pawn dies. If the pawn
    /// is later resurrected and their data was lost (current level &lt; cached level), restore
    /// from the cache. This works regardless of the resurrection mechanism used.
    /// </summary>
    public static class ResurrectionPreserver
    {
        /// <summary>
        /// Cache keyed by thingIDNumber → snapshot of Isekai data at death.
        /// Covers the common case where the same Pawn object is resurrected.
        /// </summary>
        private static readonly Dictionary<int, IsekaiStackData> deathCache = new Dictionary<int, IsekaiStackData>();

        /// <summary>
        /// Name-based fallback for cases where a completely NEW pawn object is created
        /// from the dead one (some Anomaly mechanics, modded resurrection).
        /// Key: "KindDefName|FullName"
        /// </summary>
        private static readonly Dictionary<string, IsekaiStackData> nameCache = new Dictionary<string, IsekaiStackData>();

        /// <summary>Maximum cache entries to prevent unbounded memory growth.</summary>
        private const int MAX_CACHE_SIZE = 200;

        /// <summary>
        /// Snapshot a pawn's Isekai data on death for potential resurrection restoration.
        /// Called from Pawn.Kill prefix.
        /// </summary>
        public static void CacheOnDeath(Pawn pawn) => CacheSnapshot(pawn);

        /// <summary>
        /// Snapshot a pawn's Isekai progression into the cache (by thingID and full name) so it
        /// can be restored if the pawn is later resurrected, revived as a new entity, or
        /// transformed (e.g. Pawnmorpher de-morph). Safe to call frequently — it's a couple of
        /// dictionary writes. Only caches pawns with real progression (level > 1).
        /// </summary>
        public static void CacheSnapshot(Pawn pawn)
        {
            if (pawn == null || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return;

            var comp = IsekaiComponent.GetCached(pawn);
            if (comp == null || comp.currentLevel <= 1) return;

            var snapshot = new IsekaiStackData();
            snapshot.CopyFrom(comp);

            // Primary key: thingIDNumber (same pawn object resurrected)
            deathCache[pawn.thingIDNumber] = snapshot;

            // Secondary key: name-based (new/transformed pawn object created from this one)
            string nameKey = GetNameKey(pawn);
            if (nameKey != null)
                nameCache[nameKey] = snapshot;

            // Prevent unbounded growth
            if (deathCache.Count > MAX_CACHE_SIZE)
            {
                deathCache.Clear();
                nameCache.Clear();
            }
        }

        /// <summary>
        /// Try to restore Isekai data after resurrection if it was lost.
        /// Only restores when the current level is strictly lower than the cached level,
        /// avoiding unnecessary overwrites for pawns whose data survived intact.
        /// Returns true if data was restored.
        /// </summary>
        public static bool TryRestore(Pawn pawn, IsekaiComponent comp)
        {
            if (pawn == null || comp == null) return false;

            // Try primary key first (same pawn object)
            if (deathCache.TryGetValue(pawn.thingIDNumber, out var snapshot))
            {
                if (comp.currentLevel < snapshot.currentLevel)
                {
                    snapshot.ApplyTo(comp);
                    deathCache.Remove(pawn.thingIDNumber);

                    string nameKey = GetNameKey(pawn);
                    if (nameKey != null) nameCache.Remove(nameKey);

                    Log.Message($"[Isekai Leveling] Restored level {snapshot.currentLevel} for {pawn.LabelShort} after resurrection (ID match).");
                    return true;
                }
                // Data is intact — REFRESH the snapshot instead of dropping it, so if this
                // pawn is later revived/transformed into a fresh entity we can still restore.
                CacheSnapshot(pawn);
                return false;
            }

            // Try name-based fallback (new pawn object)
            string key = GetNameKey(pawn);
            if (key != null && nameCache.TryGetValue(key, out var nameSnapshot))
            {
                if (comp.currentLevel < nameSnapshot.currentLevel)
                {
                    nameSnapshot.ApplyTo(comp);
                    // Refresh under this pawn's current id/name so a second transform still works.
                    CacheSnapshot(pawn);

                    Log.Message($"[Isekai Leveling] Restored level {nameSnapshot.currentLevel} for {pawn.LabelShort} after resurrection (name match).");
                    return true;
                }
                // Intact — refresh the snapshot rather than dropping it.
                CacheSnapshot(pawn);
            }

            return false;
        }

        /// <summary>
        /// Clear all cached data. Called on game load to prevent cross-save contamination.
        /// </summary>
        public static void ClearAll()
        {
            deathCache.Clear();
            nameCache.Clear();
        }

        private static string GetNameKey(Pawn pawn)
        {
            // Match by full name ALONE (no kindDef). Mods that revive/transform a pawn often
            // create a new entity with a different pawn kind (Shadow Monarch soldiers,
            // Pawnmorpher de-morph, etc.) — keying on kind made those miss. Full names
            // (first + nick + last) are unique enough that collisions are negligible.
            string full = pawn?.Name?.ToStringFull;
            return string.IsNullOrEmpty(full) ? null : full;
        }
    }

    /// <summary>
    /// Cache Isekai data when a humanlike pawn dies, so it can be restored on resurrection.
    /// Uses Prefix to capture data BEFORE death processing clears anything.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_CacheIsekaiOnDeath
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance)
        {
            try
            {
                ResurrectionPreserver.CacheOnDeath(__instance);
            }
            catch { /* Silently ignore caching errors */ }
        }
    }
}
