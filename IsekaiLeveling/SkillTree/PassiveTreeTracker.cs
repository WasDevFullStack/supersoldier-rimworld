using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.SkillTree
{
    /// <summary>
    /// Per-pawn tracker for passive skill tree progression.
    /// Stores which class tree is selected, which nodes are unlocked,
    /// and available passive points. Saved with the pawn via IsekaiComponent.
    /// </summary>
    public class PassiveTreeTracker : IExposable
    {
        /// <summary>Class tree identifier (e.g. "Warrior"). Null if not yet chosen.</summary>
        public string assignedTree;

        /// <summary>Star Points available to spend in the Constellation (1 per level-up, +1 per Star Fragment absorbed)</summary>
        public int availablePoints = 0;

        /// <summary>Multiclass unlocks remaining. Each absorbed Star Fragment grants 1. Consumed when entering another class tree's Start node.</summary>
        public int starFragmentsAbsorbed = 0;

        /// <summary>Number of times this pawn has used a full respec (stats + constellation).</summary>
        public int respecCount = 0;

        /// <summary>Maximum number of full respecs allowed per pawn.</summary>
        public const int MAX_RESPECS = 3;

        /// <summary>Persisted list of unlocked node IDs</summary>
        internal List<string> unlockedNodeIds = new List<string>();

        /// <summary>Runtime lookup set (rebuilt on load)</summary>
        private HashSet<string> unlockedSet = new HashSet<string>();

        // ── Cached bonus totals (invalidated on unlock/respec) ──
        private Dictionary<PassiveBonusType, float> bonusCache;

        // ────────────────────── Save / Load ──────────────────────

        public void ExposeData()
        {
            Scribe_Values.Look(ref assignedTree, "passiveTree", null);
            Scribe_Values.Look(ref availablePoints, "passivePoints", 0);
            Scribe_Values.Look(ref starFragmentsAbsorbed, "starFragmentsAbsorbed", 0);
            Scribe_Values.Look(ref respecCount, "respecCount", 0);
            Scribe_Collections.Look(ref unlockedNodeIds, "unlockedNodes", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unlockedNodeIds == null) unlockedNodeIds = new List<string>();
                unlockedSet = new HashSet<string>(unlockedNodeIds);
                bonusCache = null;
                cachedGimmick = null;
                gimmickTierCache = null;

                // ── Save migration: rename old treeClass values ──
                if (assignedTree == "Warrior") assignedTree = "Knight";
            }
        }

        // ────────────────────── Queries ──────────────────────

        public bool IsUnlocked(string nodeId) => unlockedSet.Contains(nodeId);

        /// <summary>
        /// Rebuilds the runtime lookup set from the persisted node list.
        /// Called after AC2 stack transfers to sync internal state.
        /// </summary>
        public void RebuildUnlockedSet()
        {
            unlockedSet = new HashSet<string>(unlockedNodeIds ?? new List<string>());
            bonusCache = null;
        }

        // ────────────────────── Star Fragment Helpers ──────────────────────

        /// <summary>Always returns false — Star Fragments no longer gate nodes; they instead grant Star Points when absorbed.</summary>
        public static bool RequiresStarFragment(PassiveNodeType nodeType) => false;

        /// <summary>Returns true if the pawn has an absorbed Star Fragment available for multiclassing.</summary>
        public static bool PawnHasStarFragment(Pawn pawn)
        {
            if (pawn == null) return false;
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp?.passiveTree == null) return false;
            return comp.passiveTree.starFragmentsAbsorbed > 0;
        }

        /// <summary>Consumes one absorbed Star Fragment (multiclass unlock) from the pawn's tracker.</summary>
        private static void ConsumeStarFragment(Pawn pawn)
        {
            if (pawn == null) return;
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp?.passiveTree == null) return;
            if (comp.passiveTree.starFragmentsAbsorbed > 0)
                comp.passiveTree.starFragmentsAbsorbed--;
        }

        public int UnlockedCount => unlockedNodeIds.Count;

        /// <summary>Returns true if at least one unlocked node is NOT a Start node (real tree investment exists).</summary>
        public bool HasNonStartNodes
        {
            get
            {
                foreach (var nodeId in unlockedNodeIds)
                {
                    var node = FindNodeInAnyTree(nodeId);
                    if (node != null && node.nodeType != PassiveNodeType.Start)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Count of unlocked nodes that belong to the specified tree.</summary>
        public int UnlockedCountInTree(PassiveTreeDef tree)
        {
            if (tree?.nodes == null) return 0;
            int count = 0;
            foreach (var nd in tree.nodes)
                if (unlockedSet.Contains(nd.nodeId)) count++;
            return count;
        }

        public int TotalAllocatedPoints
        {
            get
            {
                if (unlockedNodeIds.Count == 0) return 0;
                int total = 0;
                foreach (var id in unlockedNodeIds)
                {
                    var node = FindNodeInAnyTree(id);
                    total += node?.cost ?? 1;
                }
                return total;
            }
        }

        /// <summary>Find which tree a node belongs to (checks all loaded tree defs).</summary>
        public PassiveTreeDef FindTreeForNode(string nodeId)
        {
            foreach (var tree in DefDatabase<PassiveTreeDef>.AllDefs)
                if (tree.GetNode(nodeId) != null)
                    return tree;
            return null;
        }

        /// <summary>Find a node record from any loaded tree.</summary>
        private PassiveNodeRecord FindNodeInAnyTree(string nodeId)
        {
            foreach (var tree in DefDatabase<PassiveTreeDef>.AllDefs)
            {
                var node = tree.GetNode(nodeId);
                if (node != null) return node;
            }
            return null;
        }

        /// <summary>Returns true if the pawn has unlocked any node in the given tree.</summary>
        public bool HasEnteredTree(PassiveTreeDef tree)
        {
            if (tree?.nodes == null) return false;
            foreach (var node in tree.nodes)
                if (unlockedSet.Contains(node.nodeId)) return true;
            return false;
        }

        public PassiveTreeDef GetTreeDef()
        {
            if (string.IsNullOrEmpty(assignedTree)) return null;
            return DefDatabase<PassiveTreeDef>.AllDefs
                .FirstOrDefault(t => t.treeClass == assignedTree);
        }

        /// <summary>
        /// Can this node be unlocked right now?
        /// Requirements: not already unlocked, enough points, adjacent to an unlocked node.
        /// If <paramref name="pawn"/> is provided, also checks Star Fragment requirement.
        /// Multi-class: Start nodes from other trees are unlockable if the pawn has a Star Fragment.
        /// </summary>
        public bool CanUnlock(string nodeId, Pawn pawn = null)
        {
            if (IsUnlocked(nodeId)) return false;

            var tree = GetTreeDef();
            if (tree == null)
            {
                // No tree assigned yet — only Start nodes are unlockable
                // (choosing the tree happens implicitly on first unlock)
                tree = FindTreeForNode(nodeId);
                if (tree == null) return false;
                var startCheck = tree.GetNode(nodeId);
                if (startCheck == null || startCheck.nodeType != PassiveNodeType.Start) return false;
                if (availablePoints < startCheck.cost) return false;
                
                // Require D rank (level 11) to unlock first class
                if (pawn != null)
                {
                    var comp = pawn.GetComp<IsekaiComponent>();
                    if (comp != null && comp.Level < 11) return false;
                }
                
                return true;
            }

            var node = tree.GetNode(nodeId);

            // ── Multi-class: node not found in assigned tree — look in other trees ──
            if (node == null)
            {
                var otherTree = FindTreeForNode(nodeId);
                if (otherTree == null) return false;
                node = otherTree.GetNode(nodeId);
                tree = otherTree;

                // Start node of another tree: requires Star Fragment
                if (node.nodeType == PassiveNodeType.Start)
                {
                    if (availablePoints < node.cost) return false;
                    if (pawn != null && !PawnHasStarFragment(pawn)) return false;
                    return true;
                }
                // Non-start node of another tree: must have already entered that tree
                // Fall through to normal adjacency check below
            }

            if (availablePoints < node.cost) return false;

            // Start node is always unlockable (entry point)
            if (node.nodeType == PassiveNodeType.Start)
                return true;

            // Must be adjacent to an unlocked node.
            // If requireAllConnected, every neighbor must be unlocked (convergence nodes).
            var neighbors = tree.GetNeighbors(nodeId);
            if (node.requireAllConnected)
            {
                if (neighbors.Count == 0) return false;
                foreach (var nid in neighbors)
                    if (!IsUnlocked(nid)) return false;
                return true;
            }

            foreach (var nid in neighbors)
            {
                if (IsUnlocked(nid))
                    return true;
            }
            return false;
        }

        // ────────────────────── Mutations ──────────────────────

        /// <summary>
        /// Attempt to unlock a node. Returns true on success.
        /// If no tree is assigned yet, this implicitly selects the tree.
        /// Multi-class: unlocking a Start node from another tree consumes a Star Fragment.
        /// </summary>
        public bool Unlock(string nodeId, Pawn pawn = null)
        {
            if (IsUnlocked(nodeId)) return false;

            // Resolve tree — may set assignedTree if first node
            PassiveTreeDef tree;
            if (string.IsNullOrEmpty(assignedTree))
            {
                tree = FindTreeForNode(nodeId);
                if (tree == null) return false;
                var startNode = tree.GetNode(nodeId);
                if (startNode == null || startNode.nodeType != PassiveNodeType.Start) return false;
                if (availablePoints < startNode.cost) return false;
                assignedTree = tree.treeClass;
            }
            else
            {
                tree = GetTreeDef();
                // Multi-class: node not in assigned tree
                if (tree?.GetNode(nodeId) == null)
                {
                    tree = FindTreeForNode(nodeId);
                }
            }

            if (tree == null) return false;
            var node = tree.GetNode(nodeId);
            if (node == null) return false;
            if (!CanUnlock(nodeId, pawn)) return false;

            availablePoints -= node.cost;
            unlockedNodeIds.Add(nodeId);
            unlockedSet.Add(nodeId);
            bonusCache = null;
            cachedGimmick = null; // Invalidate
            gimmickTierCache = null;

            // Multi-class: consume Star Fragment when entering another class tree
            if (pawn != null && node.nodeType == PassiveNodeType.Start && tree.treeClass != assignedTree)
            {
                ConsumeStarFragment(pawn);
            }

            return true;
        }

        /// <summary>Called on level-up to grant passive points (trait-aware).</summary>
        /// <param name="newLevel">The new level the pawn just reached.</param>
        /// <param name="pawn">The pawn leveling up (for trait queries). Null uses default logic.</param>
        public void OnLevelUp(int newLevel, Pawn pawn = null)
        {
            int points = IsekaiTraitHelper.GetStarPointsForLevel(pawn, newLevel);
            if (points > 0)
                availablePoints += points;
        }

        /// <summary>
        /// Force-unlock a node without checking points, adjacency, or fragment requirements.
        /// Used by <see cref="TreeAutoAssigner"/> during NPC generation.
        /// </summary>
        public void ForceUnlockNode(string nodeId)
        {
            if (unlockedSet.Contains(nodeId)) return;
            unlockedNodeIds.Add(nodeId);
            unlockedSet.Add(nodeId);
            bonusCache = null;
        }

        /// <summary>Respec non-Start nodes only: refund all nodes except Start nodes (all trees), return points. Counts toward the per-pawn respec limit.</summary>
        public void Respec()
        {
            int refunded = 0;
            var toKeep = new List<string>();

            foreach (var nodeId in unlockedNodeIds)
            {
                var node = FindNodeInAnyTree(nodeId);
                if (node != null && node.nodeType == PassiveNodeType.Start)
                {
                    toKeep.Add(nodeId);
                }
                else
                {
                    refunded += node?.cost ?? 1;
                }
            }

            unlockedNodeIds = toKeep;
            unlockedSet = new HashSet<string>(toKeep);
            availablePoints += refunded;
            bonusCache = null;
            cachedGimmick = null;
            gimmickTierCache = null;
            ResetGimmickState();
            respecCount++;
        }

        /// <summary>
        /// Full respec: refund ALL nodes including Start nodes, clear class assignment.
        /// Does NOT increment the respec counter (used by Respec Orb which is unlimited).
        /// Returns total constellation points refunded.
        /// Also restores multiclass unlocks (starFragmentsAbsorbed) that were consumed
        /// when entering cross-tree Start nodes.
        /// </summary>
        public int FullRespec()
        {
            int refunded = 0;
            int multiclassRestored = 0;

            foreach (var nodeId in unlockedNodeIds)
            {
                var node = FindNodeInAnyTree(nodeId);
                refunded += node?.cost ?? 1;

                // Restore consumed multiclass unlocks: count Start nodes from non-primary trees
                if (node != null && node.nodeType == PassiveNodeType.Start && !string.IsNullOrEmpty(assignedTree))
                {
                    var nodeTree = FindTreeForNode(nodeId);
                    if (nodeTree != null && nodeTree.treeClass != assignedTree)
                        multiclassRestored++;
                }
            }

            unlockedNodeIds.Clear();
            unlockedSet.Clear();
            assignedTree = null;
            availablePoints += refunded;
            starFragmentsAbsorbed += multiclassRestored;
            bonusCache = null;
            cachedGimmick = null;
            gimmickTierCache = null;
            ResetGimmickState();
            return refunded;
        }

        /// <summary>Clear all transient gimmick state (charges, stacks, timers).</summary>
        private void ResetGimmickState()
        {
            retributionStoredDamage = 0;
            counterStrikeCharges = 0;
            frenzyStacks = 0;
            lastFrenzyKillTick = -1;
            huntMarkTargetId = -1;
            huntMarkStacks = 0;
            lastHitTick = -1;
            eurekaInsightStacks = 0;
            lastEurekaTendTick = -1;
        }

        /// <summary>Returns true if this pawn can still respec (under the MAX_RESPECS limit).</summary>
        public bool CanRespec => respecCount < MAX_RESPECS;

        /// <summary>How many respecs remain.</summary>
        public int RespecsRemaining => MAX_RESPECS - respecCount;

        // ────────────────────── Bonus Resolution ──────────────────────

        /// <summary>
        /// Get the total bonus of a specific type from all unlocked nodes.
        /// Result is cached and invalidated on unlock/respec.
        /// </summary>
        public float GetTotalBonus(PassiveBonusType type)
        {
            if (string.IsNullOrEmpty(assignedTree)) return 0f;

            if (bonusCache != null && bonusCache.TryGetValue(type, out float cached))
            {
                if (type == PassiveBonusType.ClassGimmickTier) return cached;
                return cached * (IsekaiMod.Settings?.ConstellationBonusMultiplier ?? 1f);
            }

            // Rebuild full cache — scan ALL trees for unlocked nodes (multi-class support)
            if (bonusCache == null)
            {
                bonusCache = new Dictionary<PassiveBonusType, float>();

                foreach (var nodeId in unlockedNodeIds)
                {
                    var node = FindNodeInAnyTree(nodeId);
                    if (node?.bonuses == null) continue;
                    foreach (var bonus in node.bonuses)
                    {
                        if (!bonusCache.ContainsKey(bonus.bonusType))
                            bonusCache[bonus.bonusType] = 0f;
                        bonusCache[bonus.bonusType] += bonus.value;
                    }
                }
            }

            if (!bonusCache.TryGetValue(type, out float val)) return 0f;
            if (type == PassiveBonusType.ClassGimmickTier) return val;
            return val * (IsekaiMod.Settings?.ConstellationBonusMultiplier ?? 1f);
        }

        /// <summary>
        /// Retroactively grant passive points for existing level
        /// (called when mod updates to v1.1.1 on an existing save)
        /// </summary>
        public void RetroactiveGrant(int currentLevel)
        {
            // Only if this tracker is completely fresh (no points spent, no points available beyond default)
            if (availablePoints == 0 && unlockedNodeIds.Count == 0 && currentLevel > 1)
            {
                availablePoints = currentLevel / 5;
            }
        }

        // ────────────────────── Class Gimmick ──────────────────────

        /// <summary>Cached gimmick type — invalidated on tree change, respec, and load.</summary>
        private ClassGimmickType? cachedGimmick;

        /// <summary>Cached per-tree gimmick tiers — maps gimmick type to tier (0-4). Invalidated with cachedGimmick.</summary>
        private Dictionary<ClassGimmickType, int> gimmickTierCache;

        /// <summary>
        /// Get the active class gimmick type for this pawn's primary (first chosen) tree.
        /// Returns None if no tree is assigned. Result is cached to avoid
        /// repeated LINQ scans on hot stat paths.
        /// </summary>
        public ClassGimmickType GetActiveGimmick()
        {
            if (cachedGimmick.HasValue) return cachedGimmick.Value;
            var tree = GetTreeDef();
            cachedGimmick = tree?.classGimmick ?? ClassGimmickType.None;
            return cachedGimmick.Value;
        }

        /// <summary>
        /// Returns true if this pawn has entered a class tree that provides the given gimmick.
        /// Supports multiclass — checks all entered trees, not just the primary.
        /// </summary>
        public bool HasGimmick(ClassGimmickType type)
        {
            if (type == ClassGimmickType.None) return false;
            EnsureGimmickTierCache();
            return gimmickTierCache.ContainsKey(type);
        }

        /// <summary>
        /// Returns all active gimmick types for this pawn (from all entered trees).
        /// Used by UI to display all class passives.
        /// </summary>
        public List<ClassGimmickType> GetActiveGimmicks()
        {
            EnsureGimmickTierCache();
            return new List<ClassGimmickType>(gimmickTierCache.Keys);
        }

        /// <summary>
        /// Get the gimmick tier (0-4) for a specific gimmick type, counting
        /// ClassGimmickTier nodes only from the tree that provides that gimmick.
        /// Returns 0 if the pawn hasn't entered that tree.
        /// </summary>
        public int GetGimmickTierFor(ClassGimmickType type)
        {
            if (type == ClassGimmickType.None) return 0;
            EnsureGimmickTierCache();
            return gimmickTierCache.TryGetValue(type, out int tier) ? tier : 0;
        }

        /// <summary>
        /// Builds the per-gimmick tier cache by scanning all tree defs the pawn has entered.
        /// </summary>
        private void EnsureGimmickTierCache()
        {
            if (gimmickTierCache != null) return;
            gimmickTierCache = new Dictionary<ClassGimmickType, int>();

            foreach (var tree in DefDatabase<PassiveTreeDef>.AllDefs)
            {
                if (tree.classGimmick == ClassGimmickType.None) continue;
                if (!HasEnteredTree(tree)) continue;

                int tier = 0;
                foreach (var nodeId in unlockedNodeIds)
                {
                    var node = tree.GetNode(nodeId);
                    if (node?.bonuses == null) continue;
                    foreach (var bonus in node.bonuses)
                    {
                        if (bonus.bonusType == PassiveBonusType.ClassGimmickTier)
                            tier += Mathf.RoundToInt(bonus.value);
                    }
                }
                gimmickTierCache[tree.classGimmick] = Mathf.Clamp(tier, 0, 4);
            }
        }

        /// <summary>
        /// Get the current gimmick tier (0-4) based on how many ClassGimmickTier
        /// bonuses the pawn has unlocked in their passive tree.
        /// Tier 0 = gimmick not yet unlocked. Each ClassGimmickTier bonus adds 1 tier.
        /// </summary>
        public int GetGimmickTier()
        {
            float raw = GetTotalBonus(PassiveBonusType.ClassGimmickTier);
            return Mathf.Clamp(Mathf.RoundToInt(raw), 0, 4);
        }

        /// <summary>
        /// Calculate the Warrior gimmick bonus: "Wrath of the Fallen"
        /// Scales with gimmick tier from passive tree nodes:
        ///   Tier 0: Inactive (no bonus)
        ///   Tier 1: Below 50% HP → up to +25% melee damage
        ///   Tier 2: Below 50% HP → up to +35% melee damage
        ///   Tier 3: Below 50% HP → up to +50% melee damage
        ///   Tier 4: Below 50% HP → up to +75% melee damage
        /// Returns 0 if gimmick tier is 0 or HP is above the threshold.
        /// </summary>
        public static float CalcWrathOfTheFallen(Pawn pawn, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;
            if (pawn?.health?.hediffSet == null) return 0f;
            float hpPct = pawn.health.summaryHealth?.SummaryHealthPercent ?? 1f;

            // Tier determines HP threshold and max bonus
            float threshold;
            float maxBonus;
            switch (gimmickTier)
            {
                case 1: threshold = 0.50f; maxBonus = 0.25f; break;
                case 2: threshold = 0.50f; maxBonus = 0.35f; break;
                case 3: threshold = 0.50f; maxBonus = 0.50f; break;
                default: threshold = 0.50f; maxBonus = 0.75f; break; // Tier 4+
            }

            if (hpPct >= threshold) return 0f;

            // Linear scale: at threshold = 0%, at near death = maxBonus
            float ratio = 1f - (hpPct / threshold);
            float bonus = ratio * maxBonus;
            return Mathf.Clamp(bonus, 0f, maxBonus);
        }

        /// <summary>Convenience overload that reads tier from this tracker</summary>
        public float CalcWrathOfTheFallen(Pawn pawn)
        {
            return CalcWrathOfTheFallen(pawn, GetGimmickTier());
        }

        /// <summary>
        /// Calculate the Mage gimmick bonus: "Arcane Overflow"
        /// Activates when psyfocus exceeds threshold, scaling linearly to maxBonus at 100% focus:
        ///   Tier 0: Inactive (no bonus)
        ///   Tier 1: Above 50% psyfocus → up to +20% psychic sensitivity
        ///   Tier 2: Above 40% psyfocus → up to +35% psychic sensitivity
        ///   Tier 3: Above 30% psyfocus → up to +55% psychic sensitivity
        ///   Tier 4: Above 20% psyfocus → up to +80% psychic sensitivity
        /// Research speed receives the same bonus at half strength.
        /// Returns 0 if gimmick tier is 0 or psyfocus is below the threshold.
        /// </summary>
        public static float CalcArcaneOverflow(Pawn pawn, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;
            if (pawn?.psychicEntropy == null) return 0f;
            float psyfocus = pawn.psychicEntropy.CurrentPsyfocus; // 0–1

            float threshold;
            float maxBonus;
            switch (gimmickTier)
            {
                case 1: threshold = 0.50f; maxBonus = 0.20f; break;
                case 2: threshold = 0.40f; maxBonus = 0.35f; break;
                case 3: threshold = 0.30f; maxBonus = 0.55f; break;
                default: threshold = 0.20f; maxBonus = 0.80f; break; // Tier 4+
            }

            if (psyfocus <= threshold) return 0f;

            // Linear scale: at threshold = 0%, at 100% psyfocus = maxBonus
            float ratio = (psyfocus - threshold) / (1.0f - threshold);
            return Mathf.Clamp(ratio * maxBonus, 0f, maxBonus);
        }

        /// <summary>Convenience overload that reads tier from this tracker</summary>
        public float CalcArcaneOverflow(Pawn pawn)
        {
            return CalcArcaneOverflow(pawn, GetGimmickTier());
        }

        // ──────────────────── DIVINE RETRIBUTION (Paladin) ─────────────────────

        /// <summary>
        /// Transient retribution charge accumulated from incoming damage this combat cycle.
        /// Not saved — resets between play sessions (not listed in ExposeData).
        /// Capped by the active gimmick tier. Consumed and reset on the next outgoing melee strike.
        /// </summary>
        public int retributionStoredDamage = 0;

        /// <summary>
        /// Charge cap and max bonus by tier:
        ///   Tier 1 (Saint's Guard): cap 50  → up to +15%
        ///   Tier 2 (Iron Covenant): cap 75  → up to +30%
        ///   Tier 3 (Vow of Steel):  cap 100 → up to +45%
        ///   Tier 4 (Holy Wrath):    cap 150 → up to +60%
        /// Bonus scales linearly: bonus = (storedDamage / cap) * maxBonus
        /// </summary>
        public static float CalcDivineRetribution(int storedDamage, int gimmickTier)
        {
            if (gimmickTier <= 0 || storedDamage <= 0) return 0f;

            int cap;
            float maxBonus;
            switch (gimmickTier)
            {
                case 1: cap = 50;  maxBonus = 0.15f; break;
                case 2: cap = 75;  maxBonus = 0.30f; break;
                case 3: cap = 100; maxBonus = 0.45f; break;
                default: cap = 150; maxBonus = 0.60f; break;
            }

            float ratio = Mathf.Clamp01((float)storedDamage / cap);
            return ratio * maxBonus;
        }

        /// <summary>Instance overload — reads current stored charge and tier.</summary>
        public float CalcDivineRetribution()
        {
            return CalcDivineRetribution(retributionStoredDamage, GetGimmickTier());
        }

        /// <summary>
        /// Accumulate incoming damage into retribution charge.
        /// Called from XPPatches when the Paladin is the damage recipient.
        /// Clamps to the per-tier cap so excess damage is not double-banked.
        /// </summary>
        public void AccumulateRetribution(int incomingDamage)
        {
            int tier = GetGimmickTier();
            if (tier <= 0) return;

            int cap;
            switch (tier)
            {
                case 1: cap = 50;  break;
                case 2: cap = 75;  break;
                case 3: cap = 100; break;
                default: cap = 150; break;
            }

            retributionStoredDamage = Mathf.Min(retributionStoredDamage + incomingDamage, cap);
        }

        /// <summary>
        /// Consume and reset the retribution charge.
        /// Called from XPPatches when the Paladin lands a melee strike.
        /// Returns the charge that was stored (for display/logging if needed).
        /// </summary>
        public int ConsumeRetribution()
        {
            int stored = retributionStoredDamage;
            retributionStoredDamage = 0;
            return stored;
        }

        // ═══════════════════════════════════════════════════
        // ▓▓ Inner Calm (Sage gimmick) ▓▓
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Time (in game ticks) when the Sage last received hostile damage.
        /// -1 = never hit this session. Not saved — resets between sessions.
        /// Resets to TicksGame when damage is received via ResetCalmTimer().
        /// </summary>
        public int lastHitTick = -1;

        /// <summary>
        /// Reset the Inner Calm timer to now (called from XPPatches when Sage takes damage).
        /// No-op if the active gimmick is not InnerCalm.
        /// </summary>
        public void ResetCalmTimer()
        {
            if (HasGimmick(ClassGimmickType.InnerCalm))
                lastHitTick = Find.TickManager?.TicksGame ?? 0;
        }

        /// <summary>
        /// Inner Calm bonus by tier. Lower cap = reaches max faster.
        ///   Tier 1 (Tranquil Mind):     10000 ticks (~4h) → up to +20% TendQ/Surgery, +10% Research
        ///   Tier 2 (Serene Focus):       7500 ticks (~3h) → up to +35% / +18%
        ///   Tier 3 (Sage's Peace):       5000 ticks (~2h) → up to +55% / +28%
        ///   Tier 4 (Perfect Stillness):  2500 ticks (~1h) → up to +80% / +40%
        /// Bonus scales linearly 0→max. Research bonus is half the TendQ/Surgery bonus.
        /// </summary>
        public static float CalcInnerCalm(int lastHitTick, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;

            int calmTicks;
            if (lastHitTick < 0)
                calmTicks = int.MaxValue / 2; // Never hit → fully calm (avoid overflow)
            else
                calmTicks = (Find.TickManager?.TicksGame ?? 0) - lastHitTick;

            int capTicks;
            float maxBonus;
            switch (gimmickTier)
            {
                case 1: capTicks = 10000; maxBonus = 0.20f; break;
                case 2: capTicks = 7500;  maxBonus = 0.35f; break;
                case 3: capTicks = 5000;  maxBonus = 0.55f; break;
                default: capTicks = 2500; maxBonus = 0.80f; break;
            }

            float ratio = Mathf.Clamp01((float)Mathf.Max(0, calmTicks) / capTicks);
            return ratio * maxBonus;
        }

        /// <summary>Instance overload — reads lastHitTick and gimmick tier from this tracker.</summary>
        public float CalcInnerCalm() => CalcInnerCalm(lastHitTick, GetGimmickTier());

        // ═══════════════════════════════════════════════════
        // ▓▓ Predator Focus (Ranger gimmick) ▓▓
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// ThingIDNumber of the currently marked target. -1 = no mark.
        /// Not saved — resets between sessions (transient, like other gimmick state).
        /// </summary>
        public int huntMarkTargetId = -1;

        /// <summary>
        /// Current focus stacks on the marked target.
        /// Each consecutive ranged hit on the same target adds 1 stack (capped by tier).
        /// Switching targets resets stacks to 1 on the new prey.
        /// </summary>
        public int huntMarkStacks = 0;

        /// <summary>
        /// Stack Predator Focus when the Ranger lands a ranged hit.
        /// If hitting the same target, increments stacks (up to tier cap).
        /// If hitting a different target, resets to 1 stack on the new target.
        /// </summary>
        public void StackPredatorFocus(Thing target)
        {
            if (target == null) return;
            int tier = GetGimmickTier();
            if (tier <= 0) return;

            int maxStacks;
            switch (tier)
            {
                case 1: maxStacks = 3; break;
                case 2: maxStacks = 4; break;
                case 3: maxStacks = 5; break;
                default: maxStacks = 7; break;
            }

            int targetId = target.thingIDNumber;
            if (targetId == huntMarkTargetId)
            {
                huntMarkStacks = Mathf.Min(huntMarkStacks + 1, maxStacks);
            }
            else
            {
                huntMarkTargetId = targetId;
                huntMarkStacks = 1;
            }
        }

        /// <summary>
        /// Predator Focus accuracy bonus by tier:
        ///   Tier 1 (Keen Eye):         max 3 stacks,  +4% each → max +12%
        ///   Tier 2 (Focused Aim):      max 4 stacks,  +5% each → max +20%
        ///   Tier 3 (Lethal Precision): max 5 stacks,  +6% each → max +30%
        ///   Tier 4 (Apex Predator):    max 7 stacks,  +7% each → max +49%
        /// </summary>
        public static float CalcPredatorFocus(int stacks, int gimmickTier)
        {
            if (gimmickTier <= 0 || stacks <= 0) return 0f;

            float perStack;
            switch (gimmickTier)
            {
                case 1: perStack = 0.04f; break;
                case 2: perStack = 0.05f; break;
                case 3: perStack = 0.06f; break;
                default: perStack = 0.07f; break;
            }

            return stacks * perStack;
        }

        /// <summary>Instance overload — reads stacks and tier from this tracker.</summary>
        public float CalcPredatorFocus() => CalcPredatorFocus(huntMarkStacks, GetGimmickTier());

        // ═══════════════════════════════════════════════════
        // ▓▓ Counter Strike (Duelist gimmick) ▓▓
        // ═══════════════════════════════════════════════════

        /// <summary>
        /// Counter charges accumulated from dodging enemy melee attacks.
        /// Not saved — resets between sessions (transient).
        /// Consumed and reset when the Duelist lands a melee hit.
        /// </summary>
        public int counterStrikeCharges = 0;

        /// <summary>
        /// Accumulate a counter charge when the Duelist dodges a melee attack.
        /// Capped by the active gimmick tier.
        /// </summary>
        public void AccumulateCounterCharge()
        {
            int tier = GetGimmickTier();
            if (tier <= 0) return;

            int maxCharges;
            switch (tier)
            {
                case 1: maxCharges = 3; break;
                case 2: maxCharges = 5; break;
                case 3: maxCharges = 7; break;
                default: maxCharges = 10; break;
            }

            counterStrikeCharges = Mathf.Min(counterStrikeCharges + 1, maxCharges);
        }

        /// <summary>
        /// Consume and reset all counter charges when the Duelist lands a melee strike.
        /// Returns the number of charges that were stored.
        /// </summary>
        public int ConsumeCounterStrike()
        {
            int stored = counterStrikeCharges;
            counterStrikeCharges = 0;
            return stored;
        }

        /// <summary>
        /// Counter Strike melee damage bonus by tier:
        ///   Tier 1 (Parry):           max 3 charges,  +7% each → max +21%
        ///   Tier 2 (Riposte):         max 5 charges,  +8% each → max +40%
        ///   Tier 3 (Blade Dance):     max 7 charges,  +9% each → max +63%
        ///   Tier 4 (Perfect Counter): max 10 charges, +10% each → max +100%
        /// </summary>
        public static float CalcCounterStrike(int charges, int gimmickTier)
        {
            if (gimmickTier <= 0 || charges <= 0) return 0f;

            float perCharge;
            switch (gimmickTier)
            {
                case 1: perCharge = 0.07f; break;
                case 2: perCharge = 0.08f; break;
                case 3: perCharge = 0.09f; break;
                default: perCharge = 0.10f; break;
            }

            return charges * perCharge;
        }

        /// <summary>Instance overload — reads charges and tier from this tracker.</summary>
        public float CalcCounterStrike() => CalcCounterStrike(counterStrikeCharges, GetGimmickTier());

        // ═══════════════════════════════════════════════════════════════
        // ▓▓  MASTERWORK INSIGHT  (Crafter) — skill-based, no state   ▓▓
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Crafter gimmick: Work speed scales with the pawn's Crafting skill level.
        /// Higher natural skill → greater work speed amplification.
        /// No stored state — pure calculation.
        /// </summary>
        public static float CalcMasterworkInsight(Pawn pawn, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;
            if (pawn?.skills == null) return 0f;

            var craftingSkill = pawn.skills.GetSkill(SkillDefOf.Crafting);
            if (craftingSkill == null) return 0f;

            float skillLevel = craftingSkill.Level; // 0-20

            float maxBonus;
            switch (gimmickTier)
            {
                case 1:  maxBonus = 0.15f; break;
                case 2:  maxBonus = 0.30f; break;
                case 3:  maxBonus = 0.50f; break;
                default: maxBonus = 0.75f; break; // Tier 4+
            }

            float ratio = Mathf.Clamp01(skillLevel / 20f);
            return ratio * maxBonus;
        }

        /// <summary>Instance overload — reads pawn and tier from this tracker's owner.</summary>
        public float CalcMasterworkInsight(Pawn pawn)
        {
            return CalcMasterworkInsight(pawn, GetGimmickTier());
        }

        // ═══════════════════════════════════════════════════════════════
        // ▓▓  RALLYING PRESENCE  (Leader) — colony-size, no state     ▓▓
        // ═══════════════════════════════════════════════════════════════

        // Cached colonist count per map — NEVER access FreeColonistsCount from
        // stat parts. FreeColonistsCount can trigger FreeColonists list rebuild
        // (Clear + re-Add), which corrupts any in-progress enumeration of that list
        // (social interactions, needs loop, etc.) causing "collection was modified".
        // Cache is updated ONLY from CompTickRare (safe, outside enumeration).
        private static readonly Dictionary<int, int> cachedColonistCounts = new Dictionary<int, int>();

        /// <summary>
        /// Get colony count for a map. Returns only the cached value — never
        /// accesses FreeColonistsCount directly. Safe to call from stat parts.
        /// Returns 0 if cache hasn't been populated yet (first ~4 seconds of game load).
        /// </summary>
        public static int GetCachedColonistCount(Map map)
        {
            if (map == null) return 0;
            if (cachedColonistCounts.TryGetValue(map.uniqueID, out int count))
                return count;
            return 0;
        }

        /// <summary>
        /// Proactively update the cached colonist count for a map.
        /// Called from CompTickRare — safe context, outside any FreeColonists enumeration.
        /// </summary>
        public static void UpdateColonistCountCache(Map map)
        {
            if (map == null) return;
            try
            {
                cachedColonistCounts[map.uniqueID] = map.mapPawns.FreeColonistsCount;
            }
            catch
            {
                // Fallback: don't update (keep previous cached value)
            }
        }

        /// <summary>
        /// Leader gimmick: Social effectiveness scales with the number of
        /// free colonists in the faction. More colonists → stronger presence.
        /// No stored state — pure calculation.
        /// Uses cached colonist count to avoid corruption of FreeColonists list
        /// when this is called from stat parts during colonist enumeration.
        /// </summary>
        public static float CalcRallyingPresence(Pawn pawn, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;
            if (pawn?.Map == null) return 0f;

            int colonistCount = GetCachedColonistCount(pawn.Map);

            float perColonist;
            int maxCounted;
            switch (gimmickTier)
            {
                case 1:  perColonist = 0.02f; maxCounted = 8;  break;
                case 2:  perColonist = 0.025f; maxCounted = 12; break;
                case 3:  perColonist = 0.03f; maxCounted = 16; break;
                default: perColonist = 0.035f; maxCounted = 20; break; // Tier 4+
            }

            int effective = Mathf.Min(colonistCount, maxCounted);
            return effective * perColonist;
        }

        /// <summary>Instance overload — reads pawn and tier from this tracker's owner.</summary>
        public float CalcRallyingPresence(Pawn pawn)
        {
            return CalcRallyingPresence(pawn, GetGimmickTier());
        }

        // ═══════════════════════════════════════════════════════════════
        // ▓▓  UNYIELDING SPIRIT  (Survivor) — mood-based, no state   ▓▓
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Survivor gimmick: When mood drops below 50%, immunity gain and
        /// rest effectiveness scale inversely with mood level. Lower mood
        /// = stronger survival bonuses. Mirrors Wrath of the Fallen
        /// (HP → damage) but for mood → survival stats.
        /// No stored state — pure calculation.
        /// </summary>
        public static float CalcUnyieldingSpirit(Pawn pawn, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;
            if (pawn?.needs?.mood == null) return 0f;

            float mood = pawn.needs.mood.CurLevel;

            float threshold;
            float maxBonus;
            switch (gimmickTier)
            {
                case 1:  threshold = 0.50f; maxBonus = 0.20f; break;
                case 2:  threshold = 0.50f; maxBonus = 0.35f; break;
                case 3:  threshold = 0.50f; maxBonus = 0.55f; break;
                default: threshold = 0.50f; maxBonus = 0.80f; break; // Tier 4+
            }

            if (mood >= threshold) return 0f;

            float ratio = 1f - (mood / threshold);
            return Mathf.Clamp(ratio * maxBonus, 0f, maxBonus);
        }

        /// <summary>Instance overload — reads pawn and tier from this tracker's owner.</summary>
        public float CalcUnyieldingSpirit(Pawn pawn)
        {
            return CalcUnyieldingSpirit(pawn, GetGimmickTier());
        }

        // ═══════════════════════════════════════════════════════════════
        // ▓▓  BLOOD FRENZY  (Berserker) — kill-stacking, stateful     ▓▓
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Frenzy stacks accumulated from kills within a time window.
        /// Not saved — resets between play sessions (transient).
        /// </summary>
        public int frenzyStacks = 0;

        /// <summary>
        /// Tick of the last kill that contributed a frenzy stack.
        /// Used for time-window decay: if too long since last kill, stacks reset to 0.
        /// </summary>
        public int lastFrenzyKillTick = -1;

        /// <summary>Ticks before frenzy fully decays (~42 seconds).</summary>
        private const int FRENZY_DECAY_WINDOW = 2500;

        /// <summary>
        /// Accumulate a frenzy stack when the Berserker kills an enemy.
        /// If the kill is within the decay window, stacks increment (up to tier cap).
        /// If outside the window, stacks reset to 1 for the fresh kill.
        /// </summary>
        public void AccumulateFrenzy()
        {
            int tier = GetGimmickTier();
            if (tier <= 0) return;

            int maxStacks;
            switch (tier)
            {
                case 1: maxStacks = 3;  break;
                case 2: maxStacks = 5;  break;
                case 3: maxStacks = 7;  break;
                default: maxStacks = 10; break;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            bool withinWindow = lastFrenzyKillTick >= 0
                             && (now - lastFrenzyKillTick) <= FRENZY_DECAY_WINDOW;

            if (withinWindow)
                frenzyStacks = Mathf.Min(frenzyStacks + 1, maxStacks);
            else
                frenzyStacks = 1; // Fresh kill restarts the chain

            lastFrenzyKillTick = now;
        }

        /// <summary>
        /// Blood Frenzy bonus by tier. Stacks decay to 0 if no kill within window.
        ///   Tier 1 (Blood Rage):    max 3 stacks,  +5%/stack → max +15%
        ///   Tier 2 (Carnage):       max 5 stacks,  +6%/stack → max +30%
        ///   Tier 3 (Bloodbath):     max 7 stacks,  +7%/stack → max +49%
        ///   Tier 4 (Massacre):      max 10 stacks, +8%/stack → max +80%
        /// Applies to MeleeDamage (full) and MoveSpeed (full).
        /// </summary>
        public static float CalcBloodFrenzy(int stacks, int lastKillTick, int gimmickTier)
        {
            if (gimmickTier <= 0 || stacks <= 0) return 0f;

            // Check if stacks have decayed
            int now = Find.TickManager?.TicksGame ?? 0;
            if (lastKillTick >= 0 && (now - lastKillTick) > FRENZY_DECAY_WINDOW)
                return 0f; // Decayed — stacks will be cleared on next accumulation

            float perStack;
            switch (gimmickTier)
            {
                case 1: perStack = 0.05f; break;
                case 2: perStack = 0.06f; break;
                case 3: perStack = 0.07f; break;
                default: perStack = 0.08f; break;
            }

            return stacks * perStack;
        }

        /// <summary>Instance overload — reads stacks, lastKillTick, and tier from this tracker. Clears stale stacks on decay.</summary>
        public float CalcBloodFrenzy()
        {
            float result = CalcBloodFrenzy(frenzyStacks, lastFrenzyKillTick, GetGimmickTier());
            // Clear stale stacks when decayed so UI/tooltips never show phantom counts
            if (result == 0f && frenzyStacks > 0)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (lastFrenzyKillTick >= 0 && (now - lastFrenzyKillTick) > FRENZY_DECAY_WINDOW)
                {
                    frenzyStacks = 0;
                    lastFrenzyKillTick = -1;
                }
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // ▓▓  EUREKA SYNTHESIS  (Alchemist) — tend-stacking, stateful ▓▓
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Insight stacks accumulated from successful medical tends.
        /// Not saved — resets between play sessions (transient).
        /// </summary>
        public int eurekaInsightStacks = 0;

        /// <summary>
        /// Tick of the last successful tend that contributed an insight stack.
        /// Used for slow decay: insight fades over a longer window than frenzy.
        /// </summary>
        public int lastEurekaTendTick = -1;

        /// <summary>Ticks before eureka insight fully decays (~5 minutes / 5000 ticks).</summary>
        private const int EUREKA_DECAY_WINDOW = 5000;

        /// <summary>
        /// Accumulate an insight stack when the Alchemist successfully tends a patient.
        /// If within the decay window, stacks increment (up to tier cap).
        /// If outside the window, stacks reset to 1.
        /// </summary>
        public void AccumulateEurekaInsight()
        {
            int tier = GetGimmickTier();
            if (tier <= 0) return;

            int maxStacks;
            switch (tier)
            {
                case 1: maxStacks = 5;  break;
                case 2: maxStacks = 8;  break;
                case 3: maxStacks = 12; break;
                default: maxStacks = 16; break;
            }

            int now = Find.TickManager?.TicksGame ?? 0;
            bool withinWindow = lastEurekaTendTick >= 0
                             && (now - lastEurekaTendTick) <= EUREKA_DECAY_WINDOW;

            if (withinWindow)
                eurekaInsightStacks = Mathf.Min(eurekaInsightStacks + 1, maxStacks);
            else
                eurekaInsightStacks = 1;

            lastEurekaTendTick = now;
        }

        /// <summary>
        /// Eureka Synthesis bonus by tier. Stacks decay if no tend within window.
        ///   Tier 1 (First Aid):       max 5 stacks,  +3%/stack → max +15%
        ///   Tier 2 (Clinical):        max 8 stacks,  +4%/stack → max +32%
        ///   Tier 3 (Specialist):      max 12 stacks, +5%/stack → max +60%
        ///   Tier 4 (Grand Alchemist): max 16 stacks, +6%/stack → max +96%
        /// Applies to TendQuality (full) and WorkSpeed (half).
        /// </summary>
        public static float CalcEurekaSynthesis(int stacks, int lastTendTick, int gimmickTier)
        {
            if (gimmickTier <= 0 || stacks <= 0) return 0f;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (lastTendTick >= 0 && (now - lastTendTick) > EUREKA_DECAY_WINDOW)
                return 0f;

            float perStack;
            switch (gimmickTier)
            {
                case 1: perStack = 0.03f; break;
                case 2: perStack = 0.04f; break;
                case 3: perStack = 0.05f; break;
                default: perStack = 0.06f; break;
            }

            return stacks * perStack;
        }

        /// <summary>Instance overload. Clears stale stacks on decay.</summary>
        public float CalcEurekaSynthesis()
        {
            float result = CalcEurekaSynthesis(eurekaInsightStacks, lastEurekaTendTick, GetGimmickTier());
            // Clear stale stacks when decayed so UI/tooltips never show phantom counts
            if (result == 0f && eurekaInsightStacks > 0)
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                if (lastEurekaTendTick >= 0 && (now - lastEurekaTendTick) > EUREKA_DECAY_WINDOW)
                {
                    eurekaInsightStacks = 0;
                    lastEurekaTendTick = -1;
                }
            }
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        // ▓▓  PACK ALPHA  (Beastmaster) — bonded-animal, stateless    ▓▓
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Beastmaster gimmick: Each bonded animal on the same map grants passive
        /// bonuses to taming, training, and animal gather yield.
        /// No stored state — pure calculation from pawn relations.
        ///   Tier 1: +3% per animal, max 3 counted
        ///   Tier 2: +4% per animal, max 5 counted
        ///   Tier 3: +5% per animal, max 7 counted
        ///   Tier 4: +6% per animal, max 10 counted
        /// </summary>
        public static float CalcPackAlpha(Pawn pawn, int gimmickTier)
        {
            if (gimmickTier <= 0) return 0f;
            if (pawn?.relations == null || pawn.Map == null) return 0f;

            int bondCount = 0;
            foreach (var rel in pawn.relations.DirectRelations)
            {
                if (rel.def == PawnRelationDefOf.Bond
                    && rel.otherPawn != null
                    && !rel.otherPawn.Dead
                    && rel.otherPawn.Map == pawn.Map)
                {
                    bondCount++;
                }
            }

            if (bondCount <= 0) return 0f;

            float perAnimal;
            int maxCounted;
            switch (gimmickTier)
            {
                case 1: perAnimal = 0.03f; maxCounted = 3;  break;
                case 2: perAnimal = 0.04f; maxCounted = 5;  break;
                case 3: perAnimal = 0.05f; maxCounted = 7;  break;
                default: perAnimal = 0.06f; maxCounted = 10; break;
            }

            int effective = Mathf.Min(bondCount, maxCounted);
            return effective * perAnimal;
        }

        /// <summary>Instance overload.</summary>
        public float CalcPackAlpha(Pawn pawn)
        {
            return CalcPackAlpha(pawn, GetGimmickTier());
        }
    }
}
