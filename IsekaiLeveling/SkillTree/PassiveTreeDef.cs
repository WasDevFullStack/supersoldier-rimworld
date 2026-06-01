using System.Collections.Generic;
using Verse;

namespace IsekaiLeveling.SkillTree
{
    /// <summary>
    /// Types of passive bonuses that nodes can grant.
    /// Maps directly to existing stat modification pathways.
    /// </summary>
    public enum PassiveBonusType
    {
        // Offensive (STR-adjacent)
        MeleeDamage,
        CarryCapacity,
        // Defensive (VIT-adjacent)
        MaxHealth,
        HealthRegen,
        DamageReduction,
        SharpArmor,
        BluntArmor,
        HeatArmor,
        PainThreshold,
        RestRate,
        ToxicResist,
        ImmunityGain,
        // Agility (DEX-adjacent)
        MoveSpeed,
        MeleeDodge,
        ShootingAccuracy,
        MeleeHitChance,
        AimingDelay,
        // Mental (INT-adjacent)
        WorkSpeed,
        ResearchSpeed,
        LearningSpeed,
        // Spiritual (WIS-adjacent)
        MentalBreakThreshold,
        MeditationFocus,
        TendQuality,
        SurgerySuccess,
        TrainAnimal,
        GatherYield,
        // Social (CHA-adjacent)
        SocialImpact,
        Negotiation,
        TradePrice,
        Taming,
        ArrestSuccess,
        BondChance,
        // Class gimmick
        /// <summary>Each point unlocks or upgrades the class gimmick (Tier 1-4)</summary>
        ClassGimmickTier,

        // RimWorld of Magic (only active when RoM is loaded)
        /// <summary>RoM: Increases maximum mana pool</summary>
        RoM_MaxMana,
        /// <summary>RoM: Increases mana regeneration rate</summary>
        RoM_ManaRegen,
        /// <summary>RoM: Increases magic ability damage (arcaneDmg)</summary>
        RoM_MagicDamage,
        /// <summary>RoM: Reduces magic ability cooldowns</summary>
        RoM_MagicCooldown,
        /// <summary>RoM: Reduces mana cost of magic abilities</summary>
        RoM_ManaCost,
        /// <summary>RoM: Increases maximum stamina pool</summary>
        RoM_MaxStamina,
        /// <summary>RoM: Increases stamina regeneration rate</summary>
        RoM_StaminaRegen,
        /// <summary>RoM: Increases might ability damage (mightPwr)</summary>
        RoM_MightDamage,
        /// <summary>RoM: Reduces might ability cooldowns</summary>
        RoM_MightCooldown,
        /// <summary>RoM: Reduces stamina cost of might abilities</summary>
        RoM_StaminaCost,
        /// <summary>RoM: Increases maximum chi capacity (Monk)</summary>
        RoM_ChiMax,
        /// <summary>RoM: Increases maximum psionic energy capacity</summary>
        RoM_PsionicMax,
        /// <summary>RoM: Increases summon duration</summary>
        RoM_SummonDuration,
        /// <summary>RoM: Increases buff/aura duration</summary>
        RoM_BuffDuration,
    }

    /// <summary>
    /// Class-specific passive gimmick identifiers.
    /// Each class has a unique mechanic that activates once the tree is chosen.
    /// </summary>
    public enum ClassGimmickType
    {
        None,
        /// <summary>Knight: Below 50% HP, melee damage scales up (up to +50% at 10% HP)</summary>
        WrathOfTheFallen,
        /// <summary>Mage: High psyfocus overflows arcane energy, boosting psychic sensitivity and research</summary>
        ArcaneOverflow,
        /// <summary>Ranger: Consecutive ranged hits on the same target stack focus marks, boosting accuracy</summary>
        PredatorFocus,
        /// <summary>Duelist: Every dodged melee attack stores a counter charge; next melee strike consumes all charges for bonus damage</summary>
        CounterStrike,
        /// <summary>Crafter: Crafting skill amplifies work speed. Higher Crafting level → greater work speed bonus from the passive tree.</summary>
        MasterworkInsight,
        /// <summary>Paladin: Incoming damage stores retribution charge; next melee strike releases it as bonus damage and resets</summary>
        DivineRetribution,
        /// <summary>Sage: The longer the Sage goes undamaged, the more their calm amplifies healing and research. Any hit resets the timer.</summary>
        InnerCalm,
        /// <summary>Leader: Colony size amplifies social effectiveness. More free colonists → stronger social impact, negotiation, and trade prices.</summary>
        RallyingPresence,
        /// <summary>Survivor: Low mood fuels survival instincts. Below 50% mood, immunity gain and rest effectiveness scale inversely with mood level.</summary>
        UnyieldingSpirit,
        /// <summary>Berserker: Each kill within a time window stacks blood frenzy, boosting melee damage and move speed. Stacks decay if you stop killing.</summary>
        BloodFrenzy,
        /// <summary>Alchemist: Successful medical tends build insight stacks that boost tend quality and drug/medicine work speed.</summary>
        EurekaSynthesis,
        /// <summary>Beastmaster: Each bonded animal on the map boosts taming, training, and animal gather yield.</summary>
        PackAlpha,
    }

    /// <summary>
    /// Visual and mechanical classification of tree nodes
    /// </summary>
    public enum PassiveNodeType
    {
        Start,      // Entry point — always free, auto-unlocked on class selection
        Minor,      // Small stat bumps (small circle)
        Notable,    // Named passives with significant bonuses (medium glowing circle)
        Keystone,   // Game-changing nodes with tradeoffs (large diamond)
    }

    /// <summary>
    /// A single stat bonus attached to a passive node
    /// </summary>
    public class PassiveBonus
    {
        public PassiveBonusType bonusType;
        public float value; // Percentage as decimal, e.g. 0.03 = +3%
    }

    /// <summary>
    /// Record for a single node in the passive tree.
    /// Not a Def — lives as a data element inside PassiveTreeDef.
    /// </summary>
    public class PassiveNodeRecord
    {
        public string nodeId;
        public string label;
        public string description;
        public PassiveNodeType nodeType = PassiveNodeType.Minor;
        /// <summary>Texture path for the node icon (relative to Textures/, no extension). e.g. "SkillTree/Icons/khanda"</summary>
        public string icon;
        public float x;  // Grid coordinate
        public float y;  // Grid coordinate (positive = up in UI)
        public int cost = 1;
        public List<PassiveBonus> bonuses = new List<PassiveBonus>();
        /// <summary>
        /// When true, ALL neighboring connected nodes must be unlocked before this node can be unlocked.
        /// Use on cross-connector / convergence nodes that should require investment in both branches.
        /// </summary>
        public bool requireAllConnected = false;
    }

    /// <summary>
    /// Top-level Def for a class passive tree (Warrior, Mage, etc.)
    /// Contains all nodes and connection data.
    /// </summary>
    public class PassiveTreeDef : Def
    {
        public string treeClass;    // "Warrior", "Mage", "Ranger", etc.
        public string treeDescription;
        public string iconPath;
        public ClassGimmickType classGimmick = ClassGimmickType.None;
        public string classGimmickName;
        public string classGimmickDescription;

        public List<PassiveNodeRecord> nodes = new List<PassiveNodeRecord>();

        /// <summary>
        /// Connection pairs as "NodeA~NodeB" strings.
        /// Parsed into a bidirectional adjacency map at resolve time.
        /// </summary>
        public List<string> connections = new List<string>();

        // ── Runtime caches ──
        private Dictionary<string, PassiveNodeRecord> nodeMap;
        private Dictionary<string, List<string>> adjacency;

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            BuildCaches();
        }

        private void BuildCaches()
        {
            nodeMap = new Dictionary<string, PassiveNodeRecord>();
            adjacency = new Dictionary<string, List<string>>();

            foreach (var node in nodes)
            {
                if (node.nodeId == null) continue;
                nodeMap[node.nodeId] = node;
                adjacency[node.nodeId] = new List<string>();
            }

            foreach (var conn in connections)
            {
                if (string.IsNullOrEmpty(conn)) continue;
                var parts = conn.Split('~');
                if (parts.Length != 2) continue;
                string a = parts[0].Trim();
                string b = parts[1].Trim();
                if (adjacency.ContainsKey(a) && !adjacency[a].Contains(b))
                    adjacency[a].Add(b);
                if (adjacency.ContainsKey(b) && !adjacency[b].Contains(a))
                    adjacency[b].Add(a);
            }
        }

        /// <summary>Ensure caches exist (safety for hot-reload scenarios)</summary>
        private void EnsureCaches()
        {
            if (nodeMap == null || adjacency == null) BuildCaches();
        }

        public PassiveNodeRecord GetNode(string id)
        {
            EnsureCaches();
            return nodeMap != null && nodeMap.TryGetValue(id, out var n) ? n : null;
        }

        public List<string> GetNeighbors(string id)
        {
            EnsureCaches();
            return adjacency != null && adjacency.TryGetValue(id, out var n) ? n : new List<string>();
        }

        public PassiveNodeRecord GetStartNode()
        {
            return nodes.Find(n => n.nodeType == PassiveNodeType.Start);
        }

        /// <summary>All connection pairs for rendering lines</summary>
        public IEnumerable<(PassiveNodeRecord, PassiveNodeRecord)> GetConnectionPairs()
        {
            EnsureCaches();
            var seen = new HashSet<string>();
            foreach (var conn in connections)
            {
                if (string.IsNullOrEmpty(conn)) continue;
                // Deduplicate (A~B == B~A)
                var parts = conn.Split('~');
                if (parts.Length != 2) continue;
                string key = string.Compare(parts[0], parts[1]) < 0
                    ? parts[0] + "~" + parts[1]
                    : parts[1] + "~" + parts[0];
                if (!seen.Add(key)) continue;

                var a = GetNode(parts[0].Trim());
                var b = GetNode(parts[1].Trim());
                if (a != null && b != null)
                    yield return (a, b);
            }
        }
    }
}
