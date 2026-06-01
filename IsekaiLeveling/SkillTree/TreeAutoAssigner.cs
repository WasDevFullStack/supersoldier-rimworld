using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace IsekaiLeveling.SkillTree
{
    /// <summary>
    /// Auto-assigns class trees and unlocks nodes for NPC pawns based on their level.
    /// Called during pawn generation so NPCs arrive with class progression proportional
    /// to their power level.
    ///
    /// Progression rules:
    ///   Level  1–10  (F/E):   No class assigned.
    ///   Level 11–100 (D–A):   1 random class, points spent via BFS walk.
    ///   Level 101–200 (S):    1 class + 25% chance of a 2nd class (30% points to 2nd).
    ///   Level 201–400 (SS):   1 class + 50% chance of a 2nd class (40% points to 2nd).
    ///   Level 401+   (SSS):   1 class + 75% chance of a 2nd class (45% points to 2nd).
    ///
    /// Class selection is stat-weighted when a pawn's IsekaiStatAllocation is provided:
    ///   each class has a primary and secondary stat affinity; higher values in those
    ///   stats raise the class's selection weight, so a STR-heavy pawn is far more
    ///   likely to become a Knight or Berserker than a Sage.
    /// </summary>
    public static class TreeAutoAssigner
    {
        private static readonly Random rng = new Random();

        // Implemented classes — must match the isImplemented check in the UI.
        private static readonly string[] ImplementedClasses =
        {
            "Knight", "Mage", "Paladin", "Sage", "Ranger",
            "Duelist", "Crafter", "Leader", "Survivor",
            "Berserker", "Alchemist", "Beastmaster"
        };

        // Primary and secondary stat affinities per class.
        // Primary contributes 3× weight, secondary contributes 1×.
        // All values are relative to BASE_STAT_VALUE (5); excess drives the weight up.
        private static readonly Dictionary<string, (IsekaiStatType primary, IsekaiStatType secondary)> ClassStatAffinity =
            new Dictionary<string, (IsekaiStatType, IsekaiStatType)>
        {
            { "Knight",      (IsekaiStatType.Strength,     IsekaiStatType.Vitality)      },
            { "Berserker",   (IsekaiStatType.Strength,     IsekaiStatType.Strength)      }, // double-weight STR
            { "Duelist",     (IsekaiStatType.Dexterity,    IsekaiStatType.Strength)      },
            { "Ranger",      (IsekaiStatType.Dexterity,    IsekaiStatType.Wisdom)        },
            { "Paladin",     (IsekaiStatType.Vitality,     IsekaiStatType.Charisma)      },
            { "Survivor",    (IsekaiStatType.Vitality,     IsekaiStatType.Dexterity)     },
            { "Mage",        (IsekaiStatType.Intelligence, IsekaiStatType.Wisdom)        },
            { "Alchemist",   (IsekaiStatType.Intelligence, IsekaiStatType.Wisdom)        },
            { "Crafter",     (IsekaiStatType.Intelligence, IsekaiStatType.Dexterity)     },
            { "Sage",        (IsekaiStatType.Wisdom,       IsekaiStatType.Intelligence)  },
            { "Beastmaster", (IsekaiStatType.Wisdom,       IsekaiStatType.Charisma)      },
            { "Leader",      (IsekaiStatType.Charisma,     IsekaiStatType.Wisdom)        },
        };

        /// <summary>
        /// Main entry point. Assigns a class tree and unlocks nodes for a pawn
        /// based on its level. The pawn's <see cref="PassiveTreeTracker.availablePoints"/>
        /// is set to 0 after allocation (all points are "spent").
        /// Safe to call on pawns that already have a tree — it will skip them.
        /// Pass <paramref name="stats"/> to enable stat-weighted class selection;
        /// omit or pass null for a uniformly random pick.
        /// </summary>
        public static void AssignTreeProgression(PassiveTreeTracker tracker, int level, IsekaiStatAllocation stats = null)
        {
            if (tracker == null) return;
            if (level < 11) return; // Below D rank — no class
            if (!string.IsNullOrEmpty(tracker.assignedTree)) return; // Already has a class

            // Total passive points this pawn should have = 1 per 5 levels
            int totalPoints = level / 5;

            // ── Determine multi-class eligibility ──
            bool multiClass = false;
            float secondaryShare = 0f;

            if (level >= 401) // SSS
            {
                multiClass = rng.NextDouble() < 0.75;
                secondaryShare = 0.45f;
            }
            else if (level >= 201) // SS
            {
                multiClass = rng.NextDouble() < 0.50;
                secondaryShare = 0.40f;
            }
            else if (level >= 101) // S
            {
                multiClass = rng.NextDouble() < 0.25;
                secondaryShare = 0.30f;
            }

            // ── Pick classes (stat-weighted when stats are available) ──
            string primaryClass = PickClass(null, stats);
            string secondaryClass = multiClass ? PickClass(primaryClass, stats) : null;

            // ── Distribute points ──
            int secondaryPoints = multiClass ? (int)(totalPoints * secondaryShare) : 0;
            int primaryPoints = totalPoints - secondaryPoints;

            // ── Assign primary tree ──
            var primaryTree = FindTree(primaryClass);
            if (primaryTree == null) return;

            tracker.assignedTree = primaryClass;
            SpendPointsOnTree(tracker, primaryTree, primaryPoints);

            // ── Assign secondary tree (if multi-class) ──
            if (multiClass && secondaryClass != null)
            {
                var secondaryTree = FindTree(secondaryClass);
                if (secondaryTree != null)
                {
                    SpendPointsOnTree(tracker, secondaryTree, secondaryPoints);
                }
            }

            // All points were "spent" — leave 0 available
            tracker.availablePoints = 0;
        }

        /// <summary>
        /// Auto-spend any accumulated (unspent) star points for an NPC that already
        /// has a class tree. Called on level-up so NPCs keep growing their constellation.
        /// Does nothing if the pawn has no assigned tree or no available points.
        /// </summary>
        public static void AutoSpendAccumulated(PassiveTreeTracker tracker)
        {
            if (tracker == null) return;
            if (string.IsNullOrEmpty(tracker.assignedTree)) return;
            if (tracker.availablePoints <= 0) return;

            var tree = FindTree(tracker.assignedTree);
            if (tree == null) return;

            int budget = tracker.availablePoints;
            SpendPointsOnTree(tracker, tree, budget);
            tracker.availablePoints = 0;
        }

        /// <summary>
        /// Picks a class, optionally excluding one.
        /// When <paramref name="stats"/> is provided, selection is weighted by stat affinity:
        /// each class gets a base weight of 10 plus (primaryStat - BASE) * 3 + (secondaryStat - BASE) * 1.
        /// This means high-STR pawns strongly favour Knight/Berserker, high-CHA favours Leader, etc.,
        /// while every class still retains a minimum chance.
        /// Falls back to uniform random when stats are null.
        /// </summary>
        private static string PickClass(string exclude, IsekaiStatAllocation stats)
        {
            var candidates = exclude == null
                ? ImplementedClasses
                : ImplementedClasses.Where(c => c != exclude).ToArray();

            if (candidates.Length == 0) return ImplementedClasses[0];

            // Uniform random fallback when no stats provided
            if (stats == null)
                return candidates[rng.Next(candidates.Length)];

            // Build weighted list
            int baseWeight = 10;
            int baseStat = IsekaiStatAllocation.BASE_STAT_VALUE;

            var weights = new float[candidates.Length];
            float totalWeight = 0f;

            for (int i = 0; i < candidates.Length; i++)
            {
                float w = baseWeight;
                if (ClassStatAffinity.TryGetValue(candidates[i], out var affinity))
                {
                    int primaryVal  = stats.GetStat(affinity.primary);
                    int secondaryVal= stats.GetStat(affinity.secondary);
                    w += Math.Max(0, primaryVal  - baseStat) * 3f;
                    w += Math.Max(0, secondaryVal - baseStat) * 1f;
                }
                weights[i] = w;
                totalWeight += w;
            }

            // Weighted random pick
            double roll = rng.NextDouble() * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < candidates.Length; i++)
            {
                cumulative += weights[i];
                if (roll < cumulative)
                    return candidates[i];
            }

            // Fallback (floating-point rounding safety)
            return candidates[candidates.Length - 1];
        }

        /// <summary>
        /// Finds the PassiveTreeDef for a given class name.
        /// </summary>
        private static PassiveTreeDef FindTree(string treeClass)
        {
            return DefDatabase<PassiveTreeDef>.AllDefs
                .FirstOrDefault(t => t.treeClass == treeClass);
        }

        /// <summary>
        /// Spends points by walking the tree outward from the Start node via BFS.
        /// Unlocks nodes in a natural breadth-first pattern, respecting node costs
        /// and <c>requireAllConnected</c> gates. Randomizes neighbor order each wave
        /// so different NPCs get varied builds.
        /// </summary>
        private static void SpendPointsOnTree(PassiveTreeTracker tracker, PassiveTreeDef tree, int budget)
        {
            if (tree?.nodes == null || tree.nodes.Count == 0) return;

            // ── 1. Unlock the Start node ──
            var startNode = tree.GetStartNode();
            if (startNode == null) return;

            if (!tracker.IsUnlocked(startNode.nodeId))
            {
                int startCost = startNode.cost;
                if (budget < startCost) return;

                ForceUnlock(tracker, startNode.nodeId);
                budget -= startCost;
            }

            if (budget <= 0) return;

            // ── 2. BFS wave expansion ──
            // We keep expanding until we run out of budget or unlockable nodes.
            // Each iteration, we collect all nodes adjacent to at least one unlocked node,
            // shuffle them (for variety), then try to unlock them one by one.
            int maxIterations = tree.nodes.Count + 10; // Safety cap
            for (int iter = 0; iter < maxIterations && budget > 0; iter++)
            {
                var frontier = BuildFrontier(tracker, tree);
                if (frontier.Count == 0) break;

                // Shuffle for variety
                Shuffle(frontier);

                bool unlockedAny = false;
                foreach (var node in frontier)
                {
                    if (budget < node.cost) continue;
                    if (!CanUnlockForAutoAssign(tracker, tree, node)) continue;

                    ForceUnlock(tracker, node.nodeId);
                    budget -= node.cost;
                    unlockedAny = true;

                    // After unlocking, the frontier may have changed —
                    // break and rebuild on next iteration for accuracy.
                    break;
                }

                // If we couldn't unlock anything this pass, we're stuck
                if (!unlockedAny) break;
            }
        }

        /// <summary>
        /// Build the set of candidate nodes: not yet unlocked, adjacent to at least
        /// one unlocked node (or all neighbors for requireAllConnected nodes).
        /// </summary>
        private static List<PassiveNodeRecord> BuildFrontier(PassiveTreeTracker tracker, PassiveTreeDef tree)
        {
            var frontier = new List<PassiveNodeRecord>();

            foreach (var node in tree.nodes)
            {
                if (tracker.IsUnlocked(node.nodeId)) continue;
                if (node.nodeType == PassiveNodeType.Start) continue; // Don't re-unlock start

                var neighbors = tree.GetNeighbors(node.nodeId);
                if (neighbors.Count == 0) continue;

                if (node.requireAllConnected)
                {
                    // All neighbors must be unlocked
                    bool allUnlocked = true;
                    foreach (var nid in neighbors)
                    {
                        if (!tracker.IsUnlocked(nid)) { allUnlocked = false; break; }
                    }
                    if (allUnlocked) frontier.Add(node);
                }
                else
                {
                    // At least one neighbor must be unlocked
                    foreach (var nid in neighbors)
                    {
                        if (tracker.IsUnlocked(nid))
                        {
                            frontier.Add(node);
                            break;
                        }
                    }
                }
            }

            return frontier;
        }

        /// <summary>
        /// Checks unlock eligibility for auto-assign (ignores point cost and pawn/fragment requirements).
        /// Only checks adjacency rules.
        /// </summary>
        private static bool CanUnlockForAutoAssign(PassiveTreeTracker tracker, PassiveTreeDef tree, PassiveNodeRecord node)
        {
            if (tracker.IsUnlocked(node.nodeId)) return false;
            var neighbors = tree.GetNeighbors(node.nodeId);

            if (node.requireAllConnected)
            {
                foreach (var nid in neighbors)
                    if (!tracker.IsUnlocked(nid)) return false;
                return true;
            }

            foreach (var nid in neighbors)
                if (tracker.IsUnlocked(nid)) return true;

            return false;
        }

        /// <summary>
        /// Force-unlocks a node without checking points or fragment requirements.
        /// Used by the auto-assigner since it manages its own budget.
        /// </summary>
        private static void ForceUnlock(PassiveTreeTracker tracker, string nodeId)
        {
            tracker.ForceUnlockNode(nodeId);
        }

        /// <summary>
        /// Fisher-Yates shuffle for variety in NPC builds.
        /// </summary>
        private static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
