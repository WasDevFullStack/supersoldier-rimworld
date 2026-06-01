using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;
using UnityEngine;
using Verse.AI.Group;
using IsekaiLeveling.Forge;
using IsekaiLeveling.MobRanking;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Incident worker that spawns an Isekai hunt quest offer
    /// Creates a quest that the player can accept or decline
    /// </summary>
    public class IncidentWorker_IsekaiHunt : IncidentWorker
    {
        /// <summary>
        /// Tracks pawn IDs that are bounty quest targets whose rank was explicitly forced.
        /// The raid rank system checks this to avoid overwriting bounty pawns' forced ranks.
        /// Cleared on game load via ClearStaticState calls.
        /// </summary>
        private static readonly HashSet<int> bountyPawnIds = new HashSet<int>();
        
        /// <summary>
        /// Returns true if this pawn is a bounty quest target with a forced rank.
        /// Called by RaidRankSystem to skip re-ranking bounty pawns.
        /// </summary>
        public static bool IsBountyPawn(Pawn pawn)
        {
            return pawn != null && bountyPawnIds.Contains(pawn.thingIDNumber);
        }
        
        /// <summary>
        /// Register a pawn as a bounty target so the raid system won't overwrite its rank.
        /// </summary>
        public static void RegisterBountyPawn(Pawn pawn)
        {
            if (pawn != null)
                bountyPawnIds.Add(pawn.thingIDNumber);
        }
        
        /// <summary>
        /// Remove a pawn from the bounty registry (e.g. on death/despawn).
        /// </summary>
        public static void UnregisterBountyPawn(Pawn pawn)
        {
            if (pawn != null)
                bountyPawnIds.Remove(pawn.thingIDNumber);
        }
        
        /// <summary>
        /// Gets the quest type suffix for translation keys based on rank:
        /// F-D = Hunt, B-A = Expedition, S-SSS = Raid
        /// Bounty quests use _Bounty suffix regardless of rank.
        /// </summary>
        public static string GetQuestTypeSuffix(QuestRank rank, bool isBounty = false)
        {
            if (isBounty) return "_Bounty";
            int rankInt = (int)rank;
            if (rankInt >= (int)QuestRank.S) return "_Raid";      // S, SS, SSS
            if (rankInt >= (int)QuestRank.B) return "_Expedition"; // B, A
            return "_Hunt";                                        // F, E, D, C
        }
        
        /// <summary>
        /// Pre-computes the pack size for bounty quests at quest creation time.
        /// B-Rank: 3-5, A-Rank: 5-8, S+: 1 (legendary boss), below B: 1 (local single target).
        /// </summary>
        public static int GetBountyPackSize(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.B: return Rand.RangeInclusive(3, 5);
                case QuestRank.A: return Rand.RangeInclusive(5, 8);
                default: return 1;
            }
        }

        /// <summary>
        /// Gets the human-readable quest type name for display
        /// </summary>
        public static string GetQuestTypeName(QuestRank rank, bool isBounty = false)
        {
            if (isBounty) return "Isekai_QuestType_Bounty".Translate();
            int rankInt = (int)rank;
            if (rankInt >= (int)QuestRank.S) return "Isekai_QuestType_Raid".Translate();
            if (rankInt >= (int)QuestRank.B) return "Isekai_QuestType_Expedition".Translate();
            return "Isekai_QuestType_Hunt".Translate();
        }
        
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            // Check if guild quests are enabled in settings
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableGuildQuests)
                return false;
            
            // Don't call base - it enforces biome, threat point, and other restrictions
            // We only need a map with colonists that have the Isekai component
            
            Map map = parms.target as Map;
            if (map == null) return false;
            
            // Need at least one colonist with Isekai component
            bool hasIsekaiPawns = map.mapPawns.FreeColonists.Any(p => p.GetComp<IsekaiComponent>() != null);
            if (!hasIsekaiPawns) return false;
            
            return true;
        }
        
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            // Double-check setting in case it changed after CanFireNowSub
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableGuildQuests)
                return false;
            
            Map map = parms.target as Map;
            if (map == null) return false;
            
            // Determine rank based on colony's average level
            QuestRank rank = DetermineRankForColony(map);
            
            // Enforce minimum quest rank setting — re-roll below the threshold
            int minRank = IsekaiMod.Settings?.MinQuestRank ?? 0;
            if (minRank > 0 && (int)rank < minRank)
            {
                rank = (QuestRank)minRank;
            }
            
            // 40% chance for bounty quest (hostile pawn elimination) at C+ ranks
            bool isBounty = (int)rank >= (int)QuestRank.C && Rand.Chance(0.40f);
            
            if (isBounty)
            {
                // Select hostile pawn kind for bounty quest
                PawnKindDef bountyTarget = SelectHostilePawnForRank(rank);
                if (bountyTarget == null)
                {
                    Log.Warning("[Isekai Hunt] No suitable hostile pawn found for bounty, falling back to creature hunt");
                    isBounty = false;
                }
                else
                {
                    float xpReward = CalculateXPReward(rank, bountyTarget.combatPower);
                    float silverReward = CalculateSilverReward(rank, bountyTarget.combatPower);
                    CreateHuntQuest(bountyTarget, rank, xpReward, silverReward, map, isBounty: true);
                    return true;
                }
            }
            
            // Standard creature hunt
            PawnKindDef creatureKind = SelectCreatureForRank(rank, parms.points);
            if (creatureKind == null)
            {
                Log.Warning("[Isekai Hunt] No suitable creature found");
                return false;
            }
            
            // Calculate rewards (scale with creature's combat power)
            float xpReward2 = CalculateXPReward(rank, creatureKind.combatPower);
            float silverReward2 = CalculateSilverReward(rank, creatureKind.combatPower);
            
            // Create quest offer
            CreateHuntQuest(creatureKind, rank, xpReward2, silverReward2, map);
            
            return true;
        }
        
        /// <summary>
        /// Creates a hunt quest that appears in the Available tab
        /// For F-D rank: spawns on home map
        /// For C+ rank: creates a world map site
        /// </summary>
        public static void CreateHuntQuest(PawnKindDef creatureKind, QuestRank rank, float xpReward, float silverReward, Map homeMap, bool isBounty = false)
        {
            bool isWorldHunt = (int)rank >= (int)QuestRank.B;
            string questTypeSuffix = GetQuestTypeSuffix(rank, isBounty);

            // ── World-tile lookup MUST happen before pack-size determination ──
            // Previously this ran AFTER worldPackSize was set, so when the lookup
            // failed (no suitable site within distance — common on island bases or
            // mostly-water worlds) we'd fall back to a local hunt with quest.name
            // already locked in as e.g. "Hunt 5x Conflagrator". The local hunt path
            // only spawns ONE creature, producing the user-reported mismatch where
            // the quest text advertises a pack but a single pawn appears on the map.
            int baseTile = homeMap?.Tile ?? Find.AnyPlayerHomeMap?.Tile ?? -1;
            int targetTile = -1;

            if (isWorldHunt && baseTile >= 0)
            {
                int minDist = GetMinDistance(rank);
                int maxDist = GetMaxDistance(rank);

                if (!TryFindHuntSiteTile(baseTile, minDist, maxDist, out targetTile))
                {
                    isWorldHunt = false;
                    Log.Warning($"[Isekai Hunt] Could not find suitable tile for {rank} hunt, falling back to home map");
                }
            }
            else if (isWorldHunt && baseTile < 0)
            {
                // No valid base tile to measure distance from
                isWorldHunt = false;
            }

            // ── Now compute pack size against the FINAL isWorldHunt value ──
            // Bounty: B=3-5, A=5-8, S+=1
            // Non-bounty expedition: B=3-5, A=5-8, S+=1 (legendary boss)
            // Local fallback: always 1 (LocalHunt QuestPart doesn't pack-spawn)
            int worldPackSize;
            if (!isWorldHunt)
            {
                worldPackSize = 1;
            }
            else if (isBounty)
            {
                worldPackSize = GetBountyPackSize(rank);
            }
            else
            {
                switch (rank)
                {
                    case QuestRank.B: worldPackSize = Rand.RangeInclusive(3, 5); break;
                    case QuestRank.A: worldPackSize = Rand.RangeInclusive(5, 8); break;
                    default: worldPackSize = 1; break; // S+ legendary boss
                }
            }

            // Determine display suffix for translation keys
            string displaySuffix;
            if (isBounty && worldPackSize > 1)
                displaySuffix = "_BountyPack";
            else if (!isBounty && worldPackSize > 1)
                displaySuffix = "_ExpeditionPack";
            else
                displaySuffix = questTypeSuffix;

            // Create quest
            Quest quest = Quest.MakeRaw();
            quest.name = (worldPackSize > 1)
                ? ("Isekai_Quest_Name" + displaySuffix).Translate(rank.ToString(), creatureKind.LabelCap, worldPackSize.ToString())
                : ("Isekai_Quest_Name" + questTypeSuffix).Translate(rank.ToString(), creatureKind.LabelCap);
            quest.appearanceTick = Find.TickManager.TicksGame;

            // Set quest root (required — VEF's DoRow postfix calls quest.root.GetModExtension
            // without a null check, so a null root causes NRE in the Quests tab)
            quest.root = DefDatabase<QuestScriptDef>.GetNamedSilentFail("Isekai_HuntQuestScript")
                      ?? DefDatabase<QuestScriptDef>.GetNamedSilentFail("OpportunitySite_ItemStash");

            // Set quest difficulty rating (1-5 stars based on rank)
            quest.challengeRating = GetQuestDifficulty(rank);
            
            // Generate loot rewards (scale with creature's combat power)
            List<Thing> lootRewards = GenerateLootRewards(rank, creatureKind.combatPower);

            // Add silver as a physical Thing INTO lootRewards (which the QuestPart
            // deep-scribes), so it has a deep-save site. AwardLoot filters silver
            // out of its spawn loop because AwardSilver handles silver separately.
            //
            // Why this lives in lootRewards and not in a parallel list:
            // vanilla Reward_Items.items uses LookMode.Reference (NOT Deep — see
            // RimWorld.Reward_Items.ExposeData), so any Thing it holds must be
            // deep-saved somewhere else or the reference resolves to null on load
            // and Reward_Items strips it via its PostLoadInit RemoveAll(null).
            // An empty Reward_Items causes QuestPart_Choice to hide the accept
            // button on offered (not-yet-accepted) quests after a save+load cycle.
            if (silverReward > 0)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Mathf.RoundToInt(silverReward);
                lootRewards.Insert(0, silver);
            }

            // Reward_Items.items SHARES references with QuestPart.lootRewards. The
            // QuestPart's deep-save is the only Thing-data writer; Reward_Items
            // resolves these by ID at load. (No clones — earlier "duplicate Thing ID"
            // errors came from HuntData.lootRewards being a third deep-save site,
            // which has since been removed in IsekaiHuntTracker.)
            List<Thing> allRewardItems = new List<Thing>(lootRewards);
            
            if (isWorldHunt && targetTile >= 0)
            {
                // Calculate approximate distance for description
                float distance = Find.WorldGrid.ApproxDistanceInTiles(baseTile, targetTile);
                
                // Use quest type-specific description (Expedition for B-A, Raid for S-SSS)
                // NOTE: Must use explicit NamedArgument array because RimWorld's Translate()
                // only has typed overloads up to 4 parameters. With 5+ args, the compiler
                // silently resolves to the 4-arg overload and drops the 5th ({4} = distance).
                string descKey = "Isekai_Quest_Description" + displaySuffix;
                quest.description = (worldPackSize > 1)
                    ? descKey.Translate(
                        new NamedArgument[]
                        {
                            rank.ToString(),
                            creatureKind.LabelCap,
                            NumberFormatting.FormatNum(xpReward),
                            NumberFormatting.FormatNum(silverReward),
                            distance.ToString("F0"),
                            worldPackSize.ToString()
                        }
                    )
                    : descKey.Translate(
                        new NamedArgument[]
                        {
                            rank.ToString(),
                            creatureKind.LabelCap,
                            NumberFormatting.FormatNum(xpReward),
                            NumberFormatting.FormatNum(silverReward),
                            distance.ToString("F0")
                        }
                    );
                
                // World hunt part - creates site WHEN ACCEPTED, not immediately
                QuestPart_IsekaiWorldHunt worldPart = new QuestPart_IsekaiWorldHunt();
                worldPart.creatureKind = creatureKind;
                worldPart.rank = rank;
                worldPart.xpReward = xpReward;
                worldPart.silverReward = silverReward;
                worldPart.lootRewards = lootRewards;
                worldPart.targetTile = targetTile;
                worldPart.isBounty = isBounty;
                worldPart.precomputedPackSize = worldPackSize;
                worldPart.inSignalEnable = quest.InitiateSignal;
                quest.AddPart(worldPart);
                
                // === NATIVE REWARD DISPLAY FOR WORLD HUNTS ===
                QuestPart_Choice choicePart = new QuestPart_Choice();
                choicePart.inSignalChoiceUsed = quest.InitiateSignal;
                
                QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
                choice.rewards = new List<Reward>();
                choice.questParts = new List<QuestPart>();
                
                if (allRewardItems.Count > 0)
                {
                    Reward_Items itemReward = new Reward_Items();
                    itemReward.items = new List<Thing>(allRewardItems);
                    choice.rewards.Add(itemReward);
                }
                
                choicePart.choices = new List<QuestPart_Choice.Choice> { choice };
                quest.AddPart(choicePart);
                
                // Add XP display as description part
                QuestPart_IsekaiXPReward xpPart = new QuestPart_IsekaiXPReward();
                xpPart.xpReward = xpReward;
                xpPart.inSignalEnable = quest.InitiateSignal;
                quest.AddPart(xpPart);
            }
            else
            {
                // Use quest type-specific description (Hunt for F-D)
                quest.description = ("Isekai_Quest_Description" + questTypeSuffix).Translate(
                    rank.ToString(),
                    creatureKind.LabelCap,
                    NumberFormatting.FormatNum(xpReward),
                    NumberFormatting.FormatNum(silverReward)
                );
                
                // Store pending hunt data for when quest is accepted
                QuestPart_IsekaiLocalHunt localPart = new QuestPart_IsekaiLocalHunt();
                localPart.creatureKind = creatureKind;
                localPart.rank = rank;
                localPart.xpReward = xpReward;
                localPart.silverReward = silverReward;
                localPart.lootRewards = lootRewards;
                localPart.targetMap = homeMap;
                localPart.isBounty = isBounty;
                localPart.inSignalEnable = quest.InitiateSignal;
                quest.AddPart(localPart);
                
                // === NATIVE REWARD DISPLAY ===
                // Use QuestPart_Choice for "Accept for:" button with item icons
                QuestPart_Choice choicePart = new QuestPart_Choice();
                choicePart.inSignalChoiceUsed = quest.InitiateSignal;
                
                QuestPart_Choice.Choice choice = new QuestPart_Choice.Choice();
                choice.rewards = new List<Reward>();
                choice.questParts = new List<QuestPart>();
                
                // Add item rewards for visual display
                if (allRewardItems.Count > 0)
                {
                    Reward_Items itemReward = new Reward_Items();
                    itemReward.items = new List<Thing>(allRewardItems);
                    choice.rewards.Add(itemReward);
                }
                
                choicePart.choices = new List<QuestPart_Choice.Choice> { choice };
                quest.AddPart(choicePart);
                
                // Add XP display as description part (XP is custom, not a vanilla reward type)
                QuestPart_IsekaiXPReward xpPart = new QuestPart_IsekaiXPReward();
                xpPart.xpReward = xpReward;
                xpPart.inSignalEnable = quest.InitiateSignal;
                quest.AddPart(xpPart);
            }
            
            // Pre-acceptance expiration: quest vanishes from Available tab if not accepted in time
            // 3 days for local hunts, 7 days for world hunts
            int offerExpiryTicks = isWorldHunt ? GenDate.TicksPerDay * 7 : GenDate.TicksPerDay * 3;
            quest.acceptanceExpireTick = Find.TickManager.TicksGame + offerExpiryTicks;
            string offerExpiredSignal = quest.AddedSignal + ".OfferExpired";
            
            // Delay timer starts immediately (no inSignalEnable = starts on quest add)
            quest.AddPart(new QuestPart_Delay
            {
                delayTicks = offerExpiryTicks,
                outSignalsCompleted = new List<string> { offerExpiredSignal },
                expiryInfoPart = "Isekai_Quest_OfferExpiry".Translate(),
                expiryInfoPartTip = "Isekai_Quest_OfferExpiry_Tip".Translate()
            });
            
            // When the offer delay fires, end the quest if still unaccepted
            quest.AddPart(new QuestPart_OfferExpiry
            {
                inSignal = offerExpiredSignal,
                offerOnly = true
            });
            
            // Post-acceptance expiration: time limit to complete the hunt after accepting
            int completionExpiryTicks = isWorldHunt ? GenDate.TicksPerDay * 14 : GenDate.TicksPerDay * 7;
            string completionExpiredSignal = quest.AddedSignal + ".Expired";
            quest.AddPart(new QuestPart_Delay
            {
                delayTicks = completionExpiryTicks,
                inSignalEnable = quest.InitiateSignal,
                outSignalsCompleted = new List<string> { completionExpiredSignal },
                expiryInfoPart = "Isekai_Quest_Expiry".Translate(),
                expiryInfoPartTip = "Isekai_Quest_Expiry_Tip".Translate()
            });
            
            // When completion timer fires, end the quest as failed
            quest.AddPart(new QuestPart_OfferExpiry
            {
                inSignal = completionExpiredSignal,
                sendLetter = true
            });
            
            // Add the quest (not yet accepted - appears in Available tab)
            Find.QuestManager.Add(quest);
            
            // Send notification letter (no site yet for world hunts - created on acceptance)
            GlobalTargetInfo target = GlobalTargetInfo.Invalid;
            
            Find.LetterStack.ReceiveLetter(
                ("Isekai_Quest_Offer_Label" + displaySuffix).Translate(rank.ToString()),
                (worldPackSize > 1)
                    ? ("Isekai_Quest_Offer_Text" + displaySuffix).Translate(
                        new NamedArgument[]
                        {
                            rank.ToString(),
                            creatureKind.LabelCap,
                            NumberFormatting.FormatNum(xpReward),
                            NumberFormatting.FormatNum(silverReward),
                            worldPackSize.ToString()
                        }
                    )
                    : ("Isekai_Quest_Offer_Text" + questTypeSuffix).Translate(
                        rank.ToString(), 
                        creatureKind.LabelCap, 
                        NumberFormatting.FormatNum(xpReward), 
                        NumberFormatting.FormatNum(silverReward)
                    ),
                LetterDefOf.NeutralEvent,  // Changed from PositiveEvent - quieter, less intrusive sound
                target,
                relatedFaction: null,
                quest: quest
            );
            
            string questType = GetQuestTypeName(rank, isBounty);
            Log.Message($"[Isekai] Created {rank}-Rank {questType} quest for {creatureKind.LabelCap}" + 
                       (isWorldHunt ? $" at tile {targetTile}" : " (local)"));
        }
        
        private static int GetMinDistance(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.B: return 3;
                case QuestRank.A: return 6;
                case QuestRank.S: return 12;
                case QuestRank.SS: return 18;
                case QuestRank.SSS: return 25;
                default: return 0;
            }
        }
        
        private static int GetMaxDistance(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.B: return 8;
                case QuestRank.A: return 15;
                case QuestRank.S: return 22;
                case QuestRank.SS: return 35;
                case QuestRank.SSS: return 50;
                default: return 3;
            }
        }
        
        /// <summary>
        /// Get quest difficulty rating (1-5 stars) based on rank
        /// </summary>
        private static int GetQuestDifficulty(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.F:
                case QuestRank.E: return 1;
                case QuestRank.D:
                case QuestRank.C: return 2;
                case QuestRank.B:
                case QuestRank.A: return 3;
                case QuestRank.S:
                case QuestRank.SS: return 4;
                case QuestRank.SSS: return 5;
                default: return 1;
            }
        }
        
        /// <summary>Player faction tech level, cached per reward generation call.</summary>
        private static TechLevel GetMaxTechLevel()
        {
            return Faction.OfPlayer?.def?.techLevel ?? TechLevel.Spacer;
        }

        /// <summary>Determine rune rank from a rune item defName suffix (I=1, II=2, III=3, IV=4, V=5).</summary>
        private static int GetRuneItemRank(string defName)
        {
            if (defName.EndsWith("_V")) return 5;
            if (defName.EndsWith("_IV")) return 4;
            if (defName.EndsWith("_III")) return 3;
            if (defName.EndsWith("_II")) return 2;
            return 1;
        }

        /// <summary>
        /// Generate loot rewards based on quest rank - includes weapons, armor, food, materials, and valuables
        /// </summary>
        private static List<Thing> GenerateLootRewards(QuestRank rank, float combatPower)
        {
            List<Thing> rewards = new List<Thing>();
            float remainingValue = GetLootMarketValue(rank, combatPower);
            
            // === FOOD (all ranks) ===
            Thing food = GenerateFood(rank);
            if (food != null)
            {
                rewards.Add(food);
                remainingValue -= food.MarketValue * food.stackCount;
            }
            
            // === MEDICINE (D+ ranks) ===
            if ((int)rank >= (int)QuestRank.D)
            {
                Thing meds = GenerateMedicine(rank);
                if (meds != null)
                {
                    rewards.Add(meds);
                    remainingValue -= meds.MarketValue * meds.stackCount;
                }
            }
            
            // === WEAPONS (C+ ranks) ===
            if ((int)rank >= (int)QuestRank.C && Rand.Chance(0.7f))
            {
                Thing weapon = GenerateWeapon(rank);
                if (weapon != null)
                {
                    rewards.Add(weapon);
                    remainingValue -= weapon.MarketValue;
                }
            }
            
            // === ARMOR (B+ ranks) ===
            if ((int)rank >= (int)QuestRank.B && Rand.Chance(0.6f))
            {
                Thing armor = GenerateArmor(rank);
                if (armor != null)
                {
                    rewards.Add(armor);
                    remainingValue -= armor.MarketValue;
                }
            }
            
            // === COMPONENTS (C+ ranks, tech-gated) ===
            if ((int)rank >= (int)QuestRank.C)
            {
                TechLevel maxTech = GetMaxTechLevel();
                ThingDef componentDef = null;
                if ((int)rank >= (int)QuestRank.S && (int)maxTech >= (int)TechLevel.Spacer)
                    componentDef = ThingDefOf.ComponentSpacer;
                else if ((int)maxTech >= (int)TechLevel.Industrial)
                    componentDef = ThingDefOf.ComponentIndustrial;

                if (componentDef != null)
                {
                    int componentCount = Mathf.Clamp((int)rank - 2, 1, 8);
                    Thing components = ThingMaker.MakeThing(componentDef);
                    components.stackCount = componentCount;
                    rewards.Add(components);
                    remainingValue -= componentDef.BaseMarketValue * componentCount;
                }
            }
            
            // === MATERIALS (D+ ranks) ===
            if ((int)rank >= (int)QuestRank.D && remainingValue > 100f)
            {
                Thing materials = GenerateMaterials(rank, remainingValue * 0.4f);
                if (materials != null)
                {
                    rewards.Add(materials);
                    remainingValue -= materials.MarketValue * materials.stackCount;
                }
            }
            
            // === VALUABLES (A+ ranks) ===
            if ((int)rank >= (int)QuestRank.A)
            {
                Thing valuables = GenerateValuables(rank);
                if (valuables != null)
                {
                    rewards.Add(valuables);
                }
            }
            
            // === DRUGS/STIMULANTS (B+ ranks, chance-based) ===
            if ((int)rank >= (int)QuestRank.B && Rand.Chance(0.4f))
            {
                Thing drugs = GenerateDrugs(rank);
                if (drugs != null)
                {
                    rewards.Add(drugs);
                }
            }

            // === RUNE ITEMS (B+ ranks) ===
            if ((int)rank >= (int)QuestRank.B)
            {
                int runeCount;
                int maxRuneRank;
                switch (rank)
                {
                    case QuestRank.B:   runeCount = 1; maxRuneRank = 1; break;
                    case QuestRank.A:   runeCount = 1; maxRuneRank = 2; break;
                    case QuestRank.S:   runeCount = 2; maxRuneRank = 3; break;
                    case QuestRank.SS:  runeCount = 2; maxRuneRank = 4; break;
                    case QuestRank.SSS: runeCount = 3; maxRuneRank = 5; break;
                    default:            runeCount = 0; maxRuneRank = 1; break;
                }

                var allRuneItems = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.defName.StartsWith("Isekai_Rune_"))
                    .ToList();

                if (allRuneItems.Count > 0)
                {
                    for (int i = 0; i < runeCount; i++)
                    {
                        // Pick a random rank (weighted toward lower ranks within range)
                        int chosenRank = Rand.RangeInclusive(1, maxRuneRank);

                        // Filter rune items by the chosen rank
                        var rankFiltered = allRuneItems.Where(d => GetRuneItemRank(d.defName) == chosenRank).ToList();
                        if (rankFiltered.Count == 0) continue;

                        ThingDef runeDef = rankFiltered.RandomElement();
                        Thing rune = ThingMaker.MakeThing(runeDef);
                        rune.stackCount = 1;
                        rewards.Add(rune);
                    }
                }
            }

            // === REINFORCEMENT CORES (S+ ranks) ===
            if (IsekaiLevelingSettings.EnableForgeSystem && (int)rank >= (int)QuestRank.S)
            {
                float coreChance;
                int minCores, maxCores;
                switch (rank)
                {
                    case QuestRank.S:   coreChance = 0.30f; minCores = 1; maxCores = 1; break;
                    case QuestRank.SS:  coreChance = 0.50f; minCores = 1; maxCores = 2; break;
                    case QuestRank.SSS: coreChance = 0.70f; minCores = 2; maxCores = 3; break;
                    default:            coreChance = 0f;    minCores = 0; maxCores = 0; break;
                }
                if (Rand.Chance(coreChance))
                {
                    ThingDef coreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ReinforcementCore");
                    if (coreDef != null)
                    {
                        Thing cores = ThingMaker.MakeThing(coreDef);
                        cores.stackCount = Rand.RangeInclusive(minCores, maxCores);
                        rewards.Add(cores);
                    }
                }
            }

            // === TRAIT ARTIFACT (A+ ranks, very rare) ===
            if ((int)rank >= (int)QuestRank.A)
            {
                float artifactChance;
                switch (rank)
                {
                    case QuestRank.A:   artifactChance = 0.01f; break; // 1%
                    case QuestRank.S:   artifactChance = 0.03f; break; // 3%
                    case QuestRank.SS:  artifactChance = 0.07f; break; // 7%
                    case QuestRank.SSS: artifactChance = 0.12f; break; // 12%
                    default:            artifactChance = 0f;    break;
                }

                if (Rand.Chance(artifactChance))
                {
                    string[] traitArtifacts = new string[]
                    {
                        "Isekai_HerosAwakeningScroll",
                        "Isekai_ReincarnationCrystal",
                        "Isekai_VillainsMask",
                        "Isekai_SummoningCircleScroll",
                        "Isekai_RegressionOrb"
                    };
                    string chosen = traitArtifacts[Rand.Range(0, traitArtifacts.Length)];
                    ThingDef artifactDef = DefDatabase<ThingDef>.GetNamedSilentFail(chosen);
                    if (artifactDef != null)
                    {
                        Thing artifact = ThingMaker.MakeThing(artifactDef);
                        artifact.stackCount = 1;
                        rewards.Add(artifact);
                    }
                }
            }
            
            return rewards;
        }
        
        private static Thing GenerateFood(QuestRank rank)
        {
            // Higher ranks get better food (tech-gated)
            TechLevel maxTech = GetMaxTechLevel();
            ThingDef foodDef;
            int count;
            
            if ((int)rank >= (int)QuestRank.S)
            {
                if ((int)maxTech >= (int)TechLevel.Industrial)
                {
                    // Lavish meals or packaged survival meals
                    foodDef = Rand.Bool ? ThingDefOf.MealSurvivalPack : DefDatabase<ThingDef>.GetNamedSilentFail("MealLavish");
                }
                else
                {
                    // Pre-industrial: fine meals or pemmican
                    foodDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealFine") ?? ThingDefOf.Pemmican;
                }
                count = Mathf.Clamp((int)rank - 4, 5, 20);
            }
            else if ((int)rank >= (int)QuestRank.C)
            {
                // Fine meals (available all tech levels)
                foodDef = DefDatabase<ThingDef>.GetNamedSilentFail("MealFine") ?? ThingDefOf.Pemmican;
                count = Mathf.Clamp((int)rank, 5, 15);
            }
            else
            {
                // Simple meals or pemmican
                foodDef = Rand.Bool ? ThingDefOf.Pemmican : DefDatabase<ThingDef>.GetNamedSilentFail("MealSimple");
                count = Mathf.Clamp((int)rank + 5, 5, 20);
            }
            
            if (foodDef == null) return null;
            
            Thing food = ThingMaker.MakeThing(foodDef);
            food.stackCount = count;
            return food;
        }
        
        private static Thing GenerateMedicine(QuestRank rank)
        {
            TechLevel maxTech = GetMaxTechLevel();
            ThingDef medDef;
            int count;
            
            if ((int)rank >= (int)QuestRank.SS && (int)maxTech >= (int)TechLevel.Ultra)
            {
                medDef = ThingDefOf.MedicineUltratech;
                count = Mathf.Clamp((int)rank - 5, 2, 6);
            }
            else if ((int)rank >= (int)QuestRank.A && (int)maxTech >= (int)TechLevel.Industrial)
            {
                medDef = ThingDefOf.MedicineIndustrial;
                count = Mathf.Clamp((int)rank - 3, 3, 10);
            }
            else
            {
                medDef = DefDatabase<ThingDef>.GetNamedSilentFail("MedicineHerbal");
                if (medDef == null && (int)maxTech >= (int)TechLevel.Industrial)
                    medDef = ThingDefOf.MedicineIndustrial;
                count = Mathf.Clamp((int)rank + 2, 3, 8);
            }
            
            if (medDef == null) return null;
            
            Thing meds = ThingMaker.MakeThing(medDef);
            meds.stackCount = count;
            return meds;
        }
        
        private static Thing GenerateWeapon(QuestRank rank)
        {
            // Get quality based on rank
            QualityCategory minQuality = GetMinQuality(rank);
            QualityCategory maxQuality = GetMaxQuality(rank);
            
            // Respect player faction tech level (compatible with World Tech Level mod)
            TechLevel maxTech = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Spacer;
            
            // Select weapon tier based on rank, capped by faction tech level
            IEnumerable<ThingDef> weaponPool;
            
            if ((int)rank >= (int)QuestRank.SS)
            {
                // Spacer tier weapons (capped by faction tech)
                TechLevel targetTech = (TechLevel)Mathf.Min((int)TechLevel.Spacer, (int)maxTech);
                weaponPool = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsWeapon && d.techLevel <= targetTech && d.tradeability != Tradeability.None && !d.destroyOnDrop);
            }
            else if ((int)rank >= (int)QuestRank.A)
            {
                // Industrial tier ranged or quality melee (capped by faction tech)
                TechLevel targetTech = (TechLevel)Mathf.Min((int)TechLevel.Spacer, (int)maxTech);
                weaponPool = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsWeapon && (d.techLevel == TechLevel.Industrial || d.techLevel == TechLevel.Spacer)
                           && d.techLevel <= targetTech
                           && d.tradeability != Tradeability.None && !d.destroyOnDrop);
            }
            else
            {
                // Medieval/Industrial weapons (capped by faction tech)
                TechLevel targetTech = (TechLevel)Mathf.Min((int)TechLevel.Industrial, (int)maxTech);
                weaponPool = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsWeapon && d.techLevel <= targetTech
                           && d.tradeability != Tradeability.None && !d.destroyOnDrop && d.BaseMarketValue < 500f);
            }
            
            var weaponList = weaponPool.ToList();
            if (!weaponList.Any()) return null;
            
            ThingDef weaponDef = weaponList.RandomElement();
            
            // Determine stuff (material) for the weapon
            ThingDef stuff = null;
            if (weaponDef.MadeFromStuff)
            {
                var stuffOptions = GenStuff.AllowedStuffsFor(weaponDef).ToList();
                if (stuffOptions.Any())
                {
                    // Higher ranks get better materials
                    if ((int)rank >= (int)QuestRank.S && (int)maxTech >= (int)TechLevel.Spacer && stuffOptions.Any(s => s == ThingDefOf.Plasteel))
                        stuff = ThingDefOf.Plasteel;
                    else if ((int)rank >= (int)QuestRank.A && (int)maxTech >= (int)TechLevel.Industrial && stuffOptions.Any(s => s == ThingDefOf.Uranium))
                        stuff = ThingDefOf.Uranium;
                    else
                        stuff = stuffOptions.RandomElement();
                }
            }
            
            Thing weapon = ThingMaker.MakeThing(weaponDef, stuff);
            
            // Apply quality
            if (weapon.TryGetComp<CompQuality>() != null)
            {
                QualityCategory quality = (QualityCategory)Rand.Range((int)minQuality, (int)maxQuality + 1);
                weapon.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Outsider);
            }

            // Apply forge enhancements for quest rewards (C+ rank)
            if ((int)rank >= (int)QuestRank.C)
            {
                int minRef, maxRef;
                float runeChance;
                int maxRuneRank;
                switch (rank)
                {
                    case QuestRank.C:   minRef = 1; maxRef = 2; runeChance = 0.15f; maxRuneRank = 1; break;
                    case QuestRank.B:   minRef = 2; maxRef = 3; runeChance = 0.30f; maxRuneRank = 2; break;
                    case QuestRank.A:   minRef = 3; maxRef = 5; runeChance = 0.50f; maxRuneRank = 2; break;
                    case QuestRank.S:   minRef = 4; maxRef = 6; runeChance = 0.70f; maxRuneRank = 3; break;
                    case QuestRank.SS:  minRef = 5; maxRef = 8; runeChance = 0.85f; maxRuneRank = 4; break;
                    case QuestRank.SSS: minRef = 6; maxRef = 10; runeChance = 1.00f; maxRuneRank = 5; break;
                    default:            minRef = 0; maxRef = 0; runeChance = 0f;   maxRuneRank = 1; break;
                }
                ForgeItemGenerator.TryApplyRandomEnhancement(weapon, maxRef, runeChance, maxRuneRank, minRef);
            }
            
            return weapon;
        }
        
        private static Thing GenerateArmor(QuestRank rank)
        {
            QualityCategory minQuality = GetMinQuality(rank);
            QualityCategory maxQuality = GetMaxQuality(rank);
            
            // Respect player faction tech level (compatible with World Tech Level mod)
            TechLevel maxTech = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Spacer;
            
            // Select armor tier based on rank, capped by faction tech level
            IEnumerable<ThingDef> armorPool;
            
            if ((int)rank >= (int)QuestRank.SS)
            {
                // Power armor, marine armor (capped by faction tech)
                TechLevel targetTech = (TechLevel)Mathf.Min((int)TechLevel.Spacer, (int)maxTech);
                armorPool = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsApparel && d.techLevel <= targetTech
                           && (d.apparel?.bodyPartGroups?.Any(g => g.defName == "Torso") == true)
                           && d.tradeability != Tradeability.None);
            }
            else if ((int)rank >= (int)QuestRank.A)
            {
                // Flak armor, devilstrand (capped by faction tech)
                armorPool = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsApparel && d.techLevel >= TechLevel.Industrial && d.techLevel <= maxTech
                           && d.statBases?.Any(s => s.stat == StatDefOf.ArmorRating_Sharp && s.value > 0.1f) == true
                           && d.tradeability != Tradeability.None);
            }
            else
            {
                // Basic armor (capped by faction tech)
                TechLevel targetTech = (TechLevel)Mathf.Min((int)TechLevel.Industrial, (int)maxTech);
                armorPool = DefDatabase<ThingDef>.AllDefs
                    .Where(d => d.IsApparel && d.techLevel <= targetTech
                           && (d.apparel?.bodyPartGroups?.Any(g => g.defName == "Torso") == true)
                           && d.tradeability != Tradeability.None && d.BaseMarketValue < 300f);
            }
            
            var armorList = armorPool.ToList();
            if (!armorList.Any()) return null;
            
            ThingDef armorDef = armorList.RandomElement();
            
            // Determine stuff
            ThingDef stuff = null;
            if (armorDef.MadeFromStuff)
            {
                var stuffOptions = GenStuff.AllowedStuffsFor(armorDef).ToList();
                if (stuffOptions.Any())
                {
                    // Higher ranks get better materials
                    if ((int)rank >= (int)QuestRank.SS && stuffOptions.Any(s => s.defName == "DevilstrandCloth"))
                        stuff = DefDatabase<ThingDef>.GetNamedSilentFail("DevilstrandCloth");
                    else if ((int)rank >= (int)QuestRank.A && stuffOptions.Any(s => s.defName == "Hyperweave"))
                        stuff = DefDatabase<ThingDef>.GetNamedSilentFail("Hyperweave");
                    
                    if (stuff == null)
                        stuff = stuffOptions.RandomElement();
                }
            }
            
            Thing armor = ThingMaker.MakeThing(armorDef, stuff);
            
            // Apply quality
            if (armor.TryGetComp<CompQuality>() != null)
            {
                QualityCategory quality = (QualityCategory)Rand.Range((int)minQuality, (int)maxQuality + 1);
                armor.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Outsider);
            }

            // Apply forge enhancements for quest rewards (C+ rank)
            if ((int)rank >= (int)QuestRank.C)
            {
                int minRef, maxRef;
                float runeChance;
                int maxRuneRank;
                switch (rank)
                {
                    case QuestRank.C:   minRef = 1; maxRef = 2; runeChance = 0.15f; maxRuneRank = 1; break;
                    case QuestRank.B:   minRef = 2; maxRef = 3; runeChance = 0.30f; maxRuneRank = 2; break;
                    case QuestRank.A:   minRef = 3; maxRef = 5; runeChance = 0.50f; maxRuneRank = 2; break;
                    case QuestRank.S:   minRef = 4; maxRef = 6; runeChance = 0.70f; maxRuneRank = 3; break;
                    case QuestRank.SS:  minRef = 5; maxRef = 8; runeChance = 0.85f; maxRuneRank = 4; break;
                    case QuestRank.SSS: minRef = 6; maxRef = 10; runeChance = 1.00f; maxRuneRank = 5; break;
                    default:            minRef = 0; maxRef = 0; runeChance = 0f;   maxRuneRank = 1; break;
                }
                ForgeItemGenerator.TryApplyRandomEnhancement(armor, maxRef, runeChance, maxRuneRank, minRef);
            }
            
            return armor;
        }
        
        private static Thing GenerateMaterials(QuestRank rank, float targetValue)
        {
            TechLevel maxTech = GetMaxTechLevel();
            ThingDef materialDef;
            
            if ((int)rank >= (int)QuestRank.SS && (int)maxTech >= (int)TechLevel.Spacer)
                materialDef = ThingDefOf.Plasteel;
            else if ((int)rank >= (int)QuestRank.A && (int)maxTech >= (int)TechLevel.Industrial)
                materialDef = Rand.Bool ? (((int)maxTech >= (int)TechLevel.Spacer) ? ThingDefOf.Plasteel : ThingDefOf.Steel) : ThingDefOf.Uranium;
            else if ((int)rank >= (int)QuestRank.C)
                materialDef = ThingDefOf.Steel;
            else
                materialDef = Rand.Bool ? ThingDefOf.Steel : ThingDefOf.WoodLog;
            
            int count = Mathf.Clamp(Mathf.RoundToInt(targetValue / materialDef.BaseMarketValue), 20, 150);
            
            Thing material = ThingMaker.MakeThing(materialDef);
            material.stackCount = count;
            return material;
        }
        
        private static Thing GenerateValuables(QuestRank rank)
        {
            TechLevel maxTech = GetMaxTechLevel();

            if ((int)rank >= (int)QuestRank.SSS)
            {
                ThingDef valuableDef;
                int stackCount;

                if ((int)maxTech >= (int)TechLevel.Spacer)
                {
                    // AI persona core or advanced components
                    valuableDef = Rand.Chance(0.3f) 
                        ? DefDatabase<ThingDef>.GetNamedSilentFail("AIPersonaCore")
                        : ThingDefOf.ComponentSpacer;
                    if (valuableDef == null) valuableDef = ThingDefOf.ComponentSpacer;
                    stackCount = valuableDef == ThingDefOf.ComponentSpacer ? 5 : 1;
                }
                else
                {
                    // Pre-spacer: large gold haul
                    valuableDef = ThingDefOf.Gold;
                    stackCount = Rand.Range(30, 60);
                }

                Thing valuable = ThingMaker.MakeThing(valuableDef);
                valuable.stackCount = stackCount;
                return valuable;
            }
            else if ((int)rank >= (int)QuestRank.SS)
            {
                // Gold and jade (available all tech levels)
                ThingDef valuableDef = Rand.Bool ? ThingDefOf.Gold : ThingDefOf.Jade;
                Thing valuable = ThingMaker.MakeThing(valuableDef);
                valuable.stackCount = Rand.Range(15, 30);
                return valuable;
            }
            else
            {
                // Gold or silver (available all tech levels)
                ThingDef valuableDef = Rand.Bool ? ThingDefOf.Gold : ThingDefOf.Silver;
                int count = valuableDef == ThingDefOf.Gold ? Rand.Range(5, 15) : Rand.Range(100, 300);
                Thing valuable = ThingMaker.MakeThing(valuableDef);
                valuable.stackCount = count;
                return valuable;
            }
        }
        
        private static Thing GenerateDrugs(QuestRank rank)
        {
            // Combat-useful drugs based on rank (tech-gated)
            TechLevel maxTech = GetMaxTechLevel();
            ThingDef drugDef;
            int count;
            
            if ((int)rank >= (int)QuestRank.SS && (int)maxTech >= (int)TechLevel.Spacer)
            {
                drugDef = DefDatabase<ThingDef>.GetNamedSilentFail("Luciferium") 
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("GoJuice");
                count = drugDef?.defName == "Luciferium" ? Rand.Range(3, 8) : Rand.Range(5, 10);
            }
            else if ((int)rank >= (int)QuestRank.A && (int)maxTech >= (int)TechLevel.Industrial)
            {
                drugDef = DefDatabase<ThingDef>.GetNamedSilentFail("GoJuice");
                count = Rand.Range(5, 12);
            }
            else
            {
                // Pre-industrial or lower ranks: beer or psychite tea
                drugDef = DefDatabase<ThingDef>.GetNamedSilentFail("Penoxycyline");
                if (drugDef == null || (int)maxTech < (int)TechLevel.Industrial)
                    drugDef = DefDatabase<ThingDef>.GetNamedSilentFail("Beer");
                count = Rand.Range(8, 20);
            }
            
            if (drugDef == null) return null;
            
            Thing drug = ThingMaker.MakeThing(drugDef);
            drug.stackCount = count;
            return drug;
        }
        
        private static QualityCategory GetMinQuality(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.SSS: return QualityCategory.Excellent;
                case QuestRank.SS: return QualityCategory.Good;
                case QuestRank.S: return QualityCategory.Good;
                case QuestRank.A: return QualityCategory.Normal;
                case QuestRank.B: return QualityCategory.Normal;
                default: return QualityCategory.Poor;
            }
        }
        
        private static QualityCategory GetMaxQuality(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.SSS: return QualityCategory.Legendary;
                case QuestRank.SS: return QualityCategory.Masterwork;
                case QuestRank.S: return QualityCategory.Excellent;
                case QuestRank.A: return QualityCategory.Good;
                case QuestRank.B: return QualityCategory.Good;
                default: return QualityCategory.Normal;
            }
        }
        
        /// <summary>
        /// Get the target market value for loot based on rank and combat power
        /// </summary>
        private static float GetLootMarketValue(QuestRank rank, float combatPower)
        {
            // Base loot values by rank (scaled down from previous - loot was too generous)
            float baseLootValue;
            switch (rank)
            {
                case QuestRank.F: baseLootValue = 100f; break;
                case QuestRank.E: baseLootValue = 200f; break;
                case QuestRank.D: baseLootValue = 400f; break;
                case QuestRank.C: baseLootValue = 800f; break;
                case QuestRank.B: baseLootValue = 1600f; break;
                case QuestRank.A: baseLootValue = 3200f; break;
                case QuestRank.S: baseLootValue = 6400f; break;
                case QuestRank.SS: baseLootValue = 12800f; break;
                case QuestRank.SSS: baseLootValue = 25600f; break;
                default: baseLootValue = 100f; break;
            }
            
            // Scale by combat power (same scaling as XP/silver)
            float cpScale = Mathf.Clamp(0.5f + (combatPower / 200f), 0.5f, 2.5f);
            
            return baseLootValue * cpScale;
        }
        
        private static bool TryFindHuntSiteTile(int baseTile, int minDist, int maxDist, out int tile)
        {
            // Find a passable land tile within range (not ocean/water)
            tile = -1;
            WorldGrid grid = Find.WorldGrid;
            
            for (int attempts = 0; attempts < 500; attempts++)
            {
                // Pick a random tile
                int candidateTile = Rand.Range(0, grid.TilesCount);
                
                if (!grid.InBounds(candidateTile)) continue;
                
                // Check if tile is water/ocean (including ice-covered ocean)
                if (grid[candidateTile].WaterCovered) continue;
                
                // Check if tile is impassable
                if (Find.World.Impassable(candidateTile)) continue;
                
                // Check if anything already there
                if (Find.WorldObjects.AnyWorldObjectAt(candidateTile)) continue;
                
                // Check distance
                float dist = grid.ApproxDistanceInTiles(baseTile, candidateTile);
                if (dist >= minDist && dist <= maxDist)
                {
                    tile = candidateTile;
                    return true;
                }
            }
            return false;
        }
        
        public static Site CreateHuntSite(int tile, PawnKindDef creatureKind, QuestRank rank, Quest quest)
        {
            // Use our custom Isekai_HuntGrounds SitePartDef for proper labeling
            SitePartDef partDef = DefDatabase<SitePartDef>.GetNamedSilentFail("Isekai_HuntGrounds");
            if (partDef == null)
            {
                Log.Warning("[Isekai Hunt] Isekai_HuntGrounds SitePartDef not found, falling back to ItemStash");
                partDef = DefDatabase<SitePartDef>.GetNamedSilentFail("ItemStash");
            }
            if (partDef == null)
            {
                // Try PreciousLump as fallback
                partDef = DefDatabase<SitePartDef>.GetNamedSilentFail("PreciousLump");
            }
            if (partDef == null)
            {
                Log.Error("[Isekai Hunt] Could not find any suitable SitePartDef!");
                return null;
            }
            
            // Create the site using vanilla SiteMaker
            Site site = SiteMaker.MakeSite(
                sitePart: partDef,
                tile: tile,
                faction: null,  // No faction - just a wilderness hunt location
                threatPoints: 0f
            );
            
            if (site == null)
            {
                Log.Error("[Isekai Hunt] SiteMaker.MakeSite returned null!");
                return null;
            }
            
            site.customLabel = string.Format("{0}-Rank Hunt: {1}", rank.ToString(), creatureKind.LabelCap);
            
            Find.WorldObjects.Add(site);
            return site;
        }
        
        private static float GetThreatPoints(QuestRank rank)
        {
            switch (rank)
            {
                case QuestRank.SSS: return 5000f;
                case QuestRank.SS: return 3000f;
                case QuestRank.S: return 2000f;
                case QuestRank.A: return 1200f;
                case QuestRank.B: return 800f;
                case QuestRank.C: return 500f;
                default: return 200f;
            }
        }
        
        // Get color string for rank-based notifications
        public static string GetRankColor(QuestRank rank)
        {
            // F-D: White (default)
            if (rank <= QuestRank.D)
                return "#FFFFFF";
            
            // C-A: Blue
            if (rank >= QuestRank.C && rank <= QuestRank.A)
                return "#5599FF";
            
            // S: Gold
            if (rank == QuestRank.S)
                return "#FFD700";
            
            // SS-SSS: Red
            if (rank >= QuestRank.SS)
                return "#FF4444";
            
            return "#FFFFFF"; // Default
        }
        
        private QuestRank DetermineRankForColony(Map map)
        {
            var colonists = IsekaiComponent.GetIsekaiPawnsOnMap(map);
            
            if (!colonists.Any()) return QuestRank.F;
            
            // Factor 1: Pawn power — use the HIGHER of average level and best pawn's level.
            // A colony with one level-200 hero shouldn't be dragged down by 10 fresh recruits.
            float avgLevel = (float)colonists.Average(p => IsekaiComponent.GetCached(p)?.Level ?? 1);
            float maxLevel = (float)colonists.Max(p => IsekaiComponent.GetCached(p)?.Level ?? 1);
            float effectiveLevel = Mathf.Max(avgLevel, maxLevel * 0.75f); // Best pawn contributes 75%
            
            // Factor 2: Colony wealth — granular tiers up to 3M+
            float colonyWealth = map.wealthWatcher.WealthTotal;
            // 0=<50k, 1=50-150k, 2=150-300k, 3=300-500k, 4=500k-800k, 5=800k-1.2M, 6=1.2-2M, 7=2M-3M, 8=3M+
            int wealthTier = colonyWealth < 50000 ? 0 :
                            colonyWealth < 150000 ? 1 :
                            colonyWealth < 300000 ? 2 :
                            colonyWealth < 500000 ? 3 :
                            colonyWealth < 800000 ? 4 :
                            colonyWealth < 1200000 ? 5 :
                            colonyWealth < 2000000 ? 6 :
                            colonyWealth < 3000000 ? 7 : 8;
            
            // Determine maximum possible rank based on effective level
            QuestRank maxRank;
            if (effectiveLevel >= 150) maxRank = QuestRank.SSS;
            else if (effectiveLevel >= 80) maxRank = QuestRank.SS;
            else if (effectiveLevel >= 50) maxRank = QuestRank.S;
            else if (effectiveLevel >= 35) maxRank = QuestRank.A;
            else if (effectiveLevel >= 22) maxRank = QuestRank.B;
            else if (effectiveLevel >= 14) maxRank = QuestRank.C;
            else if (effectiveLevel >= 11) maxRank = QuestRank.D;
            else if (effectiveLevel >= 6) maxRank = QuestRank.E;
            else maxRank = QuestRank.F;
            
            // Wealth can push maxRank up by 1 tier at high wealth
            if (wealthTier >= 6 && (int)maxRank < (int)QuestRank.SSS)
                maxRank = (QuestRank)((int)maxRank + 1);
            
            // Build weighted probability table
            // Flatter curve: high ranks are rare but not impossibly so
            // Base weights: F=50, E=45, D=40, C=35, B=30, A=25, S=20, SS=15, SSS=10
            Dictionary<QuestRank, float> weights = new Dictionary<QuestRank, float>();
            
            for (int i = 0; i <= (int)maxRank; i++)
            {
                QuestRank rank = (QuestRank)i;
                float baseWeight;
                
                switch (rank)
                {
                    case QuestRank.F: baseWeight = 50f; break;
                    case QuestRank.E: baseWeight = 45f; break;
                    case QuestRank.D: baseWeight = 40f; break;
                    case QuestRank.C: baseWeight = 35f; break;
                    case QuestRank.B: baseWeight = 30f; break;
                    case QuestRank.A: baseWeight = 25f; break;
                    case QuestRank.S: baseWeight = 20f; break;
                    case QuestRank.SS: baseWeight = 15f; break;
                    case QuestRank.SSS: baseWeight = 10f; break;
                    default: baseWeight = 50f; break;
                }
                
                // Wealth scaling: high wealth significantly boosts C+ ranks and suppresses low ranks
                if ((int)rank >= (int)QuestRank.C)
                {
                    // Each wealth tier adds +25% weight to C+ ranks (tier 8 = ×3.0)
                    float wealthBonus = 1f + (wealthTier * 0.25f);
                    baseWeight *= wealthBonus;
                }
                else if (wealthTier >= 4)
                {
                    // Suppress F/E/D ranks at high wealth — you've outgrown them
                    float suppression = 1f - (wealthTier - 3) * 0.12f; // tier 8 → ×0.40
                    baseWeight *= Mathf.Max(0.15f, suppression);
                }
                
                weights[rank] = baseWeight;
            }
            
            // Weighted random selection
            float totalWeight = weights.Values.Sum();
            float randomValue = Rand.Range(0f, totalWeight);
            float cumulative = 0f;
            
            foreach (var kvp in weights.OrderBy(x => x.Key))
            {
                cumulative += kvp.Value;
                if (randomValue <= cumulative)
                {
                    return kvp.Key;
                }
            }
            
            return maxRank; // Fallback
        }
        
        /// <summary>
        /// Check if a ThingDef is a vehicle (from Vanilla Vehicles Expanded, Vehicle Framework, SRTS, etc.).
        /// Vehicles should not be spawned as quest hunt targets since they are inert and non-combatant.
        /// Walks the entire type hierarchy to catch subclassed vehicle types.
        /// </summary>
        private static bool IsVehicleDef(ThingDef def)
        {
            if (def == null) return false;
            
            // Walk the entire type hierarchy — catches VehiclePawn and any subclass
            Type type = def.thingClass;
            while (type != null)
            {
                string fullName = type.FullName ?? "";
                if (fullName.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (fullName.StartsWith("SRTS", StringComparison.OrdinalIgnoreCase))
                    return true;
                type = type.BaseType;
            }
            
            // Check defName for vehicle patterns from various vehicle mods
            string defName = def.defName ?? "";
            if (defName.StartsWith("VVE_", StringComparison.OrdinalIgnoreCase) || 
                defName.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.StartsWith("DVVE_", StringComparison.OrdinalIgnoreCase) ||
                defName.StartsWith("SRTS_", StringComparison.OrdinalIgnoreCase))
                return true;
            
            // Check label for vehicle-related terms
            string label = def.label?.ToLower() ?? "";
            if (label.Contains("vehicle") || label.Contains("shuttle") || label.Contains("transport pod"))
                return true;
            
            // Check modExtensions or comps for vehicle-related components
            if (def.comps != null)
            {
                foreach (var comp in def.comps)
                {
                    string compClass = comp?.compClass?.FullName ?? "";
                    if (compClass.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        compClass.StartsWith("SRTS", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            
            // Check if the ThingDef's modContentPack is from a known vehicle mod
            string packageId = def.modContentPack?.PackageId?.ToLower() ?? "";
            if (packageId.Contains("vehicles") || packageId.Contains("srts"))
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if a PawnKindDef is a larva, swarmling, or other creature that transforms/hatches
        /// into something else, making it unsuitable as a quest target.
        /// </summary>
        private static bool IsTransformingCreature(PawnKindDef pk)
        {
            if (pk == null) return false;
            
            // Check defName for common larva/swarmling/hatchling patterns
            string defName = pk.defName ?? "";
            string label = pk.label?.ToLower() ?? "";
            if (defName.IndexOf("Swarmling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.IndexOf("Larva", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.IndexOf("Hatchling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.Contains("swarmling") || label.Contains("larva") || label.Contains("hatchling"))
                return true;
            
            // Check if creature has an extremely short lifespan (transforms before it can be hunted)
            // Normal animals live years; larvae live days/hours
            if (pk.RaceProps?.lifeExpectancy > 0 && pk.RaceProps.lifeExpectancy < 1f)
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if a creature's faction is disabled in the current game.
        /// E.g. mechanoids when mechanoid hive is turned off, insects when insectoids disabled.
        /// Wild animals (no faction association) are always allowed.
        /// </summary>
        private static bool IsFactionDisabled(PawnKindDef pk)
        {
            if (pk?.RaceProps == null) return false;
            
            // Mechanoids require the mechanoid faction to be active
            if (pk.RaceProps.IsMechanoid && Faction.OfMechanoids == null)
                return true;
            
            // Insectoids require the insect faction to be active
            if (pk.RaceProps.Insect && Faction.OfInsects == null)
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Check if a PawnKindDef is a self-destructing / detonate-on-contact mechanoid
        /// (e.g. Hunter Drone, Inferno Drone, Agro Booster). These are unfit hunt targets
        /// because they self-destruct on engagement and give no combat XP / drops.
        /// </summary>
        private static bool IsSuicidalMech(PawnKindDef pk)
        {
            if (pk?.race == null) return false;

            // defName/race name patterns for known suicide mechs (vanilla Biotech + modded)
            string defName = (pk.defName ?? "").ToLowerInvariant();
            string raceDefName = (pk.race.defName ?? "").ToLowerInvariant();
            string combined = defName + "|" + raceDefName;
            if (combined.Contains("hunterdrone") || combined.Contains("hunter_drone") ||
                combined.Contains("infernodrone") || combined.Contains("inferno_drone") ||
                combined.Contains("agrobooster") || combined.Contains("agro_booster") ||
                combined.Contains("suicidedrone") || combined.Contains("explosivedrone"))
                return true;

            // Scan race comps for explosive-on-damage component classes (string match, compat-safe)
            if (pk.race.comps != null)
            {
                for (int i = 0; i < pk.race.comps.Count; i++)
                {
                    string compTypeName = pk.race.comps[i]?.GetType()?.Name ?? "";
                    if (compTypeName.IndexOf("Explosive", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a PawnKindDef's source mod is on the user's quest creature blacklist.
        /// Users configure this in Mod Settings → Quests → Quest Creature Mod Blacklist.
        /// Each comma-separated keyword is matched (case-insensitive) against the mod's package ID and name.
        /// </summary>
        private static bool IsUserBlacklistedMod(PawnKindDef pk)
        {
            if (pk?.race?.modContentPack == null) return false;
            
            string blacklist = IsekaiMod.Settings?.QuestCreatureModBlacklist;
            if (string.IsNullOrEmpty(blacklist)) return false;
            
            string packageId = pk.race.modContentPack.PackageId ?? "";
            string modName = pk.race.modContentPack.Name ?? "";
            
            string[] patterns = blacklist.Split(',');
            for (int i = 0; i < patterns.Length; i++)
            {
                string pattern = patterns[i].Trim();
                if (string.IsNullOrEmpty(pattern)) continue;
                
                if (packageId.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    modName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            
            return false;
        }

        /// <summary>
        /// Check if a PawnKindDef is from A RimWorld of Magic (TorannMagic).
        /// RoM creatures are summons, magical constructs, or ability-spawned entities
        /// (elementals, undead, golems, sentinels, demons, spirit animals, etc.).
        /// They either vanish (summons expire), spawn as dormant buildings (golems),
        /// or fail to appear on the map — none are suitable hunt quest targets.
        /// </summary>
        private static bool IsRoMCreature(PawnKindDef pk)
        {
            if (pk?.race == null) return false;

            // Primary check: mod content pack from TorannMagic
            string packageId = pk.race.modContentPack?.PackageId;
            if (packageId != null && 
                (packageId.IndexOf("torann", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 packageId.IndexOf("rimworldofmagic", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;

            // Fallback: TM_ prefix used by all RoM creature defs
            string defName = pk.defName ?? "";
            string raceDefName = pk.race.defName ?? "";
            if (defName.StartsWith("TM_", StringComparison.OrdinalIgnoreCase) ||
                raceDefName.StartsWith("TM_", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Check if a PawnKindDef is an Anomaly DLC entity (fleshmass, sightstealers, revenants, etc.).
        /// These are not designed for conventional combat and should not be quest targets.
        /// </summary>
        private static bool IsAnomalyEntity(PawnKindDef pk)
        {
            if (pk?.race == null) return false;
            if (pk.RaceProps.Humanlike) return false;
            if (pk.RaceProps.Animal) return false;
            if (pk.RaceProps.IsMechanoid) return false;
            
            // Check mod content pack — Anomaly DLC entities come from ludeon.rimworld.anomaly
            string packageId = pk.race.modContentPack?.PackageId;
            if (packageId != null && packageId.IndexOf("anomaly", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            
            // Fallback: check defName for known anomaly creature patterns
            string defName = (pk.defName ?? "").ToLower();
            string raceDefName = (pk.race.defName ?? "").ToLower();
            string combined = defName + "|" + raceDefName;
            if (combined.Contains("sightsteal") || combined.Contains("revenant") || 
                combined.Contains("shambler") || combined.Contains("gorehulk") ||
                combined.Contains("fleshmass") || combined.Contains("chimera") ||
                combined.Contains("horror") || combined.Contains("noctol") ||
                combined.Contains("metalhorror") || combined.Contains("devourer") ||
                combined.Contains("nucleus"))
                return true;
            
            return false;
        }
        
        public static PawnKindDef SelectCreatureForRank(QuestRank rank, float points = 0f)
        {
            // Get all huntable creatures sorted by combat power
            // INCLUSIVE filter: Include everything EXCEPT humanlike, dryads, vehicles, transforming creatures, and Anomaly entities
            // This catches Animals, Mechanoids, Insects but excludes non-combat Anomaly DLC creatures
            // Also excludes creatures whose default faction is disabled (e.g. mechanoids when mech hive is off)
            var allCreatures = DefDatabase<PawnKindDef>.AllDefs
                .Where(pk => pk?.race != null &&
                             pk.RaceProps != null &&
                             !pk.RaceProps.Humanlike &&
                             !pk.RaceProps.Dryad &&
                             pk.combatPower > 0 &&
                             !IsVehicleDef(pk.race) &&
                             !IsTransformingCreature(pk) &&
                             !IsFactionDisabled(pk) &&
                             !IsAnomalyEntity(pk) &&
                             !IsRoMCreature(pk) &&
                             !IsSuicidalMech(pk) &&
                             !IsUserBlacklistedMod(pk))
                .OrderBy(pk => pk.combatPower)
                .ToList();
            
            if (!allCreatures.Any())
            {
                Log.Warning("[Isekai Hunt] No suitable creatures found!");
                return null;
            }
            
            // Use COMBAT POWER thresholds first for difficulty balance, then percentiles for variety
            // High-rank quests should ONLY spawn actually dangerous creatures
            float minCombatPower;
            float maxCombatPower;
            
            switch (rank)
            {
                // SSS: Only the absolute strongest (Thrumbo 220+, Megasloth 200)
                case QuestRank.SSS:
                    minCombatPower = 200f;
                    maxCombatPower = 9999f;
                    break;

                // SS: Very strong creatures (150+: Bears, Rhinos, etc.)
                case QuestRank.SS:
                    minCombatPower = 150f;
                    maxCombatPower = 9999f;
                    break;

                // S: Strong predators and large threats (120+)
                case QuestRank.S:
                    minCombatPower = 120f;
                    maxCombatPower = 9999f;
                    break;

                // A: Dangerous creatures (75-250)
                case QuestRank.A:
                    minCombatPower = 75f;
                    maxCombatPower = 250f;
                    break;

                // B: Medium creatures (50-150)
                case QuestRank.B:
                    minCombatPower = 50f;
                    maxCombatPower = 150f;
                    break;
                
                // C: Lower-medium creatures (25-70)
                case QuestRank.C:
                    minCombatPower = 25f;
                    maxCombatPower = 70f;
                    break;
                
                // D: Weak creatures (15-50)
                case QuestRank.D:
                    minCombatPower = 15f;
                    maxCombatPower = 50f;
                    break;
                
                // E-F: Very weak creatures (0-35)
                case QuestRank.E:
                case QuestRank.F:
                default:
                    minCombatPower = 0f;
                    maxCombatPower = 35f;
                    break;
            }
            
            // Filter by combat power range
            var validCreatures = allCreatures
                .Where(pk => pk.combatPower >= minCombatPower && pk.combatPower <= maxCombatPower)
                .ToList();

            // For S+ rank quests, prefer predators/aggressive creatures over passive herbivores
            if (rank >= QuestRank.S && validCreatures.Count > 1)
            {
                var threatening = validCreatures
                    .Where(pk => pk.RaceProps.predator ||
                                 pk.RaceProps.manhunterOnDamageChance > 0.3f ||
                                 pk.RaceProps.manhunterOnTameFailChance > 0.3f ||
                                 pk.RaceProps.FleshType?.defName == "Mechanoid" ||
                                 pk.RaceProps.FleshType?.defName == "Insectoid" ||
                                 pk.RaceProps.Insect)
                    .ToList();
                if (threatening.Any())
                    validCreatures = threatening;
            }
            
            if (!validCreatures.Any())
            {
                Log.Warning($"[Isekai Hunt] No creatures in combat power range {minCombatPower}-{maxCombatPower} for {rank}, expanding search");
                // Fallback: just use minimum combat power threshold
                validCreatures = allCreatures.Where(pk => pk.combatPower >= minCombatPower).ToList();
                if (!validCreatures.Any())
                    validCreatures = allCreatures; // Last resort
            }
            
            // For variety, use percentile selection WITHIN the valid combat power range
            List<PawnKindDef> pool;
            int count = validCreatures.Count;
            
            if (rank >= QuestRank.S)
            {
                // High ranks: prefer top 60% of valid creatures
                int start = (count * 40) / 100;
                pool = validCreatures.Skip(start).ToList();
            }
            else if (rank >= QuestRank.B)
            {
                // Medium ranks: use middle 80%
                int start = (count * 10) / 100;
                int end = (count * 90) / 100;
                pool = validCreatures.Skip(start).Take(Mathf.Max(end - start, 1)).ToList();
            }
            else
            {
                // Low ranks: use all valid creatures
                pool = validCreatures;
            }
            
            if (!pool.Any())
                pool = validCreatures;
            
            // Random selection from the pool
            PawnKindDef selected = pool.RandomElement();
            
            // Debug logging
            Log.Message($"[Isekai Hunt] Selected '{selected.LabelCap}' (CP:{selected.combatPower:F0}) for {rank} rank from pool of {pool.Count} creatures (CP range: {minCombatPower}-{maxCombatPower})");
            
            return selected;
        }
        
        /// <summary>
        /// Selects a hostile humanlike pawn kind for bounty quests based on rank.
        /// Picks from existing hostile factions' pawn kinds, filtered by combat power.
        /// </summary>
        public static PawnKindDef SelectHostilePawnForRank(QuestRank rank)
        {
            // Collect all humanlike pawn kinds from hostile factions
            var hostileFactions = Find.FactionManager.AllFactions
                .Where(f => f != null && f != Faction.OfPlayer && f.HostileTo(Faction.OfPlayer) && !f.defeated && !f.Hidden)
                .ToList();
            
            // Also include pirate/rough factions even if not currently hostile
            var roughFactions = Find.FactionManager.AllFactions
                .Where(f => f != null && f != Faction.OfPlayer && !f.defeated && !f.Hidden &&
                            (f.def.permanentEnemy || f.def.defName.Contains("Pirate") || f.def.defName.Contains("Rough")))
                .ToList();
            
            var allFactions = hostileFactions.Union(roughFactions).Distinct().ToList();
            
            if (!allFactions.Any())
            {
                // Fallback: use AncientsHostile as a source
                var ancients = Faction.OfAncientsHostile;
                if (ancients != null)
                    allFactions.Add(ancients);
            }
            
            // Get all humanlike pawn kinds from these factions
            var pawnKinds = new HashSet<PawnKindDef>();
            foreach (var faction in allFactions)
            {
                if (faction.def.pawnGroupMakers != null)
                {
                    foreach (var groupMaker in faction.def.pawnGroupMakers)
                    {
                        if (groupMaker.options != null)
                        {
                            foreach (var option in groupMaker.options)
                            {
                                if (option.kind?.RaceProps?.Humanlike == true && option.kind.combatPower > 0)
                                    pawnKinds.Add(option.kind);
                            }
                        }
                        if (groupMaker.traders != null)
                        {
                            foreach (var option in groupMaker.traders)
                            {
                                if (option.kind?.RaceProps?.Humanlike == true && option.kind.combatPower > 0)
                                    pawnKinds.Add(option.kind);
                            }
                        }
                        if (groupMaker.guards != null)
                        {
                            foreach (var option in groupMaker.guards)
                            {
                                if (option.kind?.RaceProps?.Humanlike == true && option.kind.combatPower > 0)
                                    pawnKinds.Add(option.kind);
                            }
                        }
                    }
                }
            }
            
            if (!pawnKinds.Any())
            {
                Log.Warning("[Isekai Hunt] No hostile humanlike pawn kinds found for bounty quest");
                return null;
            }
            
            // Combat power thresholds for humanlike pawns (similar ranges to creature hunts)
            float minCP, maxCP;
            switch (rank)
            {
                case QuestRank.SSS: minCP = 200f; maxCP = 9999f; break;
                case QuestRank.SS:  minCP = 150f; maxCP = 9999f; break;
                case QuestRank.S:   minCP = 120f; maxCP = 9999f; break;
                case QuestRank.A:   minCP = 80f;  maxCP = 250f;  break;
                case QuestRank.B:   minCP = 50f;  maxCP = 150f;  break;
                case QuestRank.C:   minCP = 30f;  maxCP = 100f;  break;
                default:            minCP = 0f;   maxCP = 80f;   break;
            }
            
            var validPawns = pawnKinds.Where(pk => pk.combatPower >= minCP && pk.combatPower <= maxCP).ToList();
            
            // Fallback: expand search if no matches
            if (!validPawns.Any())
            {
                validPawns = pawnKinds.Where(pk => pk.combatPower >= minCP).ToList();
                if (!validPawns.Any())
                    validPawns = pawnKinds.ToList(); // Last resort: any humanlike pawn kind
            }
            
            var selected = validPawns.RandomElement();
            Log.Message($"[Isekai Hunt] Selected bounty target '{selected.LabelCap}' (CP:{selected.combatPower:F0}) for {rank} rank from pool of {validPawns.Count} hostile pawns");
            return selected;
        }
        
        /// <summary>
        /// Returns a hostile faction for bounty quest pawns.
        /// Prefers pirate/permanent enemy factions, falls back to AncientsHostile.
        /// </summary>
        public static Faction GetHostileFactionForBounty()
        {
            // Prefer pirate / permanent enemy factions
            var pirates = Find.FactionManager.AllFactions
                .Where(f => f != null && !f.defeated && !f.Hidden && f != Faction.OfPlayer &&
                            (f.def.permanentEnemy || f.def.defName.Contains("Pirate")))
                .ToList();
            
            if (pirates.Any())
                return pirates.RandomElement();
            
            // Fallback: any hostile faction
            var hostile = Find.FactionManager.AllFactions
                .Where(f => f != null && !f.defeated && !f.Hidden && f != Faction.OfPlayer && f.HostileTo(Faction.OfPlayer))
                .ToList();
            
            if (hostile.Any())
                return hostile.RandomElement();
            
            // Last resort
            return Faction.OfAncientsHostile;
        }
        
        /// <summary>
        /// Forces a humanlike bounty pawn's Isekai rank to match the quest rank.
        /// Sets their level to the minimum for the rank and assigns the rank trait.
        /// </summary>
        public static void ForceBountyPawnRank(Pawn pawn, QuestRank questRank)
        {
            if (pawn == null || !pawn.RaceProps.Humanlike) return;
            
            string rankStr = questRank.ToString();
            
            // Get or initialize the Isekai component
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp != null)
            {
                // ALWAYS force rank to match quest rank (not just when current < min).
                // PawnGenerator's postfix may have already rolled a low rank from the tribal PawnKind,
                // overwriting it so SSS bounties never show up as F-rank level 4.
                if (comp.stats == null) comp.stats = new IsekaiStatAllocation();
                PawnStatGenerator.GenerateStatsForRank(rankStr, comp.stats);
                comp.currentLevel = PawnStatGenerator.CalculateLevelFromStats(comp.stats);
                comp.currentXP = 0;
                
                // Enforce minimum level for the requested rank — stat distribution rounding
                // or stat cap overflow can cause the calculated level to land below the rank's
                // threshold, making a C-rank quest spawn a D-rank pawn.
                int minLevel = PawnStatGenerator.GetMinLevelForRank(rankStr);
                if (comp.currentLevel < minLevel)
                {
                    comp.currentLevel = minLevel;
                }
                
                // Mark stats as initialized so PostSpawnSetup doesn't re-roll them
                // with a random rank. Critical when called before GenSpawn.Spawn.
                comp.statsInitialized = true;
                
                // Force rank trait
                PawnStatGenerator.AssignRankTrait(pawn, rankStr);
                
                // Register as bounty pawn so the raid rank system skips this pawn
                RegisterBountyPawn(pawn);
                
                Log.Message($"[Isekai Hunt] Forced bounty pawn {pawn.LabelCap} to {rankStr}-Rank (level {comp.currentLevel})");
            }
            else
            {
                Log.Warning($"[Isekai Hunt] Could not force rank on {pawn.LabelCap}: IsekaiComponent is null");
            }
            
            // Re-apply forge enhancements with the forced rank
            // (the GeneratePawn postfix already ran with the pawn's natural low rank)
            if (IsekaiLevelingSettings.EnableForgeSystem)
            {
                ApplyForgeEnhancementsForRank(pawn, questRank);
            }
        }
        
        /// <summary>
        /// Applies forge enhancements (refinement + runes) to a pawn's equipped gear
        /// based on the given quest rank. Used after forcing a bounty pawn's rank so
        /// their gear matches their displayed rank.
        /// </summary>
        public static void ApplyForgeEnhancementsForRank(Pawn pawn, QuestRank rank)
        {
            int maxRef; float runeChance; int maxRuneRank;
            switch (rank)
            {
                case QuestRank.SSS: maxRef = 10; runeChance = 1.00f; maxRuneRank = 5; break;
                case QuestRank.SS:  maxRef = 9;  runeChance = 0.85f; maxRuneRank = 4; break;
                case QuestRank.S:   maxRef = 8;  runeChance = 0.75f; maxRuneRank = 3; break;
                case QuestRank.A:   maxRef = 6;  runeChance = 0.65f; maxRuneRank = 3; break;
                case QuestRank.B:   maxRef = 4;  runeChance = 0.50f; maxRuneRank = 2; break;
                case QuestRank.C:   maxRef = 3;  runeChance = 0.35f; maxRuneRank = 1; break;
                case QuestRank.D:   maxRef = 2;  runeChance = 0.20f; maxRuneRank = 1; break;
                default:            maxRef = 1;  runeChance = 0.10f; maxRuneRank = 1; break;
            }
            
            // Enhance primary weapon
            if (pawn.equipment?.Primary != null)
            {
                ForgeItemGenerator.TryApplyRandomEnhancement(pawn.equipment.Primary, maxRef, runeChance, maxRuneRank);
            }
            
            // Enhance worn apparel
            if (pawn.apparel?.WornApparel != null)
            {
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    ForgeItemGenerator.TryApplyRandomEnhancement(apparel, maxRef, runeChance, maxRuneRank);
                }
            }
            
            Log.Message($"[Isekai Hunt] Applied forge enhancements to {pawn.LabelCap}'s gear for {rank}-Rank");
        }
        
        /// <summary>
        /// Combat power ranges aligned with MobRank system
        /// These are minimum thresholds to ensure creature matches quest rank
        /// Note: Thrumbo is ~220 combat power, Elephant ~160
        /// For SSS/SS, we need to spawn multiple or enhanced creatures since few animals exceed 250
        /// </summary>
        private static float GetMinCombatPower(QuestRank rank)
        {
            // Aligned with MobRankCalculator thresholds
            switch (rank)
            {
                case QuestRank.SSS: return 220f;   // Thrumbo-tier (highest vanilla animal)
                case QuestRank.SS: return 200f;    // Thrumbo/Megasloth-tier
                case QuestRank.S: return 150f;     // Elephant-tier
                case QuestRank.A: return 100f;     // Bear-tier
                case QuestRank.B: return 70f;      // Warg-tier
                case QuestRank.C: return 50f;      // Panther-tier  
                case QuestRank.D: return 35f;      // Wolf-tier
                case QuestRank.E: return 20f;      // Fox-tier
                default: return 0f;                // Rat-tier
            }
        }
        
        private static float GetMaxCombatPower(QuestRank rank)
        {
            // Cap at next tier's minimum to prevent rank mismatch
            switch (rank)
            {
                case QuestRank.SSS: return 9999f;  // No cap - spawn the strongest
                case QuestRank.SS: return 9999f;   // SS and SSS share top tier creatures
                case QuestRank.S: return 199f;
                case QuestRank.A: return 149f;
                case QuestRank.B: return 99f;
                case QuestRank.C: return 69f;
                case QuestRank.D: return 49f;
                case QuestRank.E: return 34f;
                default: return 19f;
            }
        }
        
        public static float CalculateXPReward(QuestRank rank, float combatPower)
        {
            // Base 200 XP, x2.5 per rank (was x3 — too explosive at high ranks)
            float baseXP = 200f * Mathf.Pow(2.5f, (int)rank);
            
            // Raid bonus: S/SS/SSS rank quests get XP boost (they require elite teams!)
            if (rank == QuestRank.S)
                baseXP *= 2f;   // S-Rank raids: 2x bonus
            else if (rank == QuestRank.SS)
                baseXP *= 3f;   // SS-Rank raids: 3x bonus
            else if (rank == QuestRank.SSS)
                baseXP *= 5f;   // SSS-Rank raids: 5x bonus
            
            // Scale by combat power (creatures with higher CP give more XP)
            // Combat power ranges: F-D (0-35), C (25-70), B (35-100), A (50-200), S (75-9999), SS/SSS (100-9999)
            // Scale factor: 0.5x at CP 20, 1.0x at CP 100, 1.5x at CP 200, 2.0x at CP 300+
            float cpScale = Mathf.Clamp(0.5f + (combatPower / 200f), 0.5f, 2.0f);
            
            return baseXP * cpScale;
        }
        
        public static float CalculateSilverReward(QuestRank rank, float combatPower)
        {
            // Base 30 silver, x1.8 per rank (was 50 silver, x2.5 per rank - way too much)
            float baseSilver = 30f * Mathf.Pow(1.8f, (int)rank);
            
            // Scale by combat power (same as XP)
            float cpScale = Mathf.Clamp(0.5f + (combatPower / 200f), 0.5f, 2.5f);
            
            return baseSilver * cpScale;
        }
        
        /// <summary>
        /// Removes blindness, deafness, and other conditions that would prevent
        /// the creature from being a proper combat threat
        /// </summary>
        public static void RemoveIncapacitatingConditions(Pawn creature)
        {
            if (creature?.health?.hediffSet == null) return;
            
            // List of hediffs that would make the creature an invalid hunt target
            List<Hediff> hediffsToRemove = new List<Hediff>();
            
            foreach (var hediff in creature.health.hediffSet.hediffs)
            {
                // Remove blindness (Blindness hediff or destroyed/missing eyes)
                if (hediff.def == HediffDefOf.Blindness ||
                    hediff.def.defName == "Blind" ||
                    (hediff is Hediff_MissingPart missing && missing.Part?.def?.tags != null && 
                     missing.Part.def.tags.Contains(BodyPartTagDefOf.SightSource)))
                {
                    hediffsToRemove.Add(hediff);
                    continue;
                }
                
                // Remove cataracts and other sight-reducing conditions
                if (hediff.def.defName == "Cataract" || 
                    hediff.def.defName == "HearingLoss" ||
                    hediff.def.defName == "Blindness")
                {
                    hediffsToRemove.Add(hediff);
                    continue;
                }
                
                // Remove conditions that cause incapacitation
                if (hediff.def.lethalSeverity > 0 && hediff.Severity >= hediff.def.lethalSeverity * 0.9f)
                {
                    hediffsToRemove.Add(hediff);
                    continue;
                }
                
                // Remove dementia and other mental incapacitation
                if (hediff.def.defName == "Dementia" || hediff.def.defName == "Alzheimers")
                {
                    hediffsToRemove.Add(hediff);
                }
            }
            
            foreach (var hediff in hediffsToRemove)
            {
                creature.health.RemoveHediff(hediff);
            }
            
            // If eyes are missing, we need to restore them
            if (creature.health.capacities.GetLevel(PawnCapacityDefOf.Sight) < 0.1f)
            {
                // Find and restore sight parts
                foreach (var part in creature.RaceProps.body.AllParts)
                {
                    if (part.def.tags != null && part.def.tags.Contains(BodyPartTagDefOf.SightSource))
                    {
                        // Check if this part is missing
                        var missingHediff = creature.health.hediffSet.hediffs
                            .OfType<Hediff_MissingPart>()
                            .FirstOrDefault(h => h.Part == part);
                        
                        if (missingHediff != null)
                        {
                            creature.health.RestorePart(part);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Ensures a bounty pawn is capable of violence. If their backstory makes them
        /// incapable of violence, replaces the offending backstory with a combat-capable one.
        /// This prevents bounty targets from being free kills that can't fight back.
        /// </summary>
        public static void EnsureCombatCapable(Pawn pawn)
        {
            if (pawn == null || !pawn.RaceProps.Humanlike) return;
            if (pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                // Find which backstory disables violence and replace it
                if (pawn.story?.Childhood != null && pawn.story.Childhood.workDisables.HasFlag(WorkTags.Violent))
                {
                    var replacement = DefDatabase<BackstoryDef>.AllDefsListForReading
                        .Where(b => b.slot == BackstorySlot.Childhood && !b.workDisables.HasFlag(WorkTags.Violent))
                        .RandomElementWithFallback(null);
                    if (replacement != null)
                    {
                        pawn.story.Childhood = replacement;
                        Log.Message($"[Isekai Hunt] Replaced pacifist childhood backstory on bounty pawn {pawn.LabelCap}");
                    }
                }
                if (pawn.story?.Adulthood != null && pawn.story.Adulthood.workDisables.HasFlag(WorkTags.Violent))
                {
                    var replacement = DefDatabase<BackstoryDef>.AllDefsListForReading
                        .Where(b => b.slot == BackstorySlot.Adulthood && !b.workDisables.HasFlag(WorkTags.Violent))
                        .RandomElementWithFallback(null);
                    if (replacement != null)
                    {
                        pawn.story.Adulthood = replacement;
                        Log.Message($"[Isekai Hunt] Replaced pacifist adulthood backstory on bounty pawn {pawn.LabelCap}");
                    }
                }
            }
        }

        /// <summary>
        /// Checks if any player colonists are present on the given map, including
        /// colonists held inside Vehicle Framework vehicles (which are not counted
        /// by FreeColonistsSpawned). Without this, map cleanup kills vehicles and
        /// colonists inside them when the site is prematurely destroyed.
        /// </summary>
        public static bool AnyColonistsOnMap(Map map)
        {
            if (map?.mapPawns == null) return false;
            
            // Standard check: free colonists spawned on the map
            if (map.mapPawns.FreeColonistsSpawnedCount > 0)
                return true;
            
            // Vehicle Framework check: colonists may be inside vehicles (not "spawned")
            // Check all spawned things for any that hold player pawns
            foreach (var thing in map.listerThings.AllThings)
            {
                // Skip non-vehicle things quickly by checking the type hierarchy
                if (thing is Pawn) continue;
                
                var holders = thing as IThingHolder;
                if (holders == null) continue;
                
                // Check if this thing's type name contains "Vehicle" (Vehicle Framework, VVE, SRTS)
                var typeName = thing.GetType().FullName ?? "";
                if (typeName.IndexOf("Vehicle", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    typeName.IndexOf("SRTS", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                
                // This is a vehicle — check if it holds any player colonists
                var tmpThings = new System.Collections.Generic.List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(holders, tmpThings, false);
                foreach (var t in tmpThings)
                {
                    if (t is Pawn p && p.IsColonist && !p.Dead)
                        return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Returns false for the shocklance → capture → execute exploit: a humanlike
        /// bounty target gets one-shot downed by a shocklance (vanilla mechanic — no
        /// resistance from rank), captured, then killed via the Execute Prisoner job.
        /// Prisoner status persists through <c>Pawn.Kill</c>, so we can detect it
        /// here and refuse to count the kill as a bounty completion. Without this
        /// gate, B-rank colonists can trivially "kill" SSS-rank bounty bosses
        /// because no combat actually happens.
        ///
        /// Returns true for every other kill path — combat, bleedout, fire, raid
        /// killing a recruited ex-boss, prisoner escape attempts, etc.
        /// </summary>
        /// <summary>
        /// Produces a fresh <see cref="Thing"/> with the same def, stuff, stack, hitpoints,
        /// quality, and forge enhancements as the source. Crucial for splitting the loot
        /// reward list between vanilla's <c>Reward_Items</c> (which Scribe_Deep's its
        /// items) and our own QuestPart's <c>lootRewards</c> field — without this they'd
        /// share Thing instances and Scribe would register the same loadID twice on
        /// load, producing "ID already used by..." errors.
        /// </summary>
        public static Thing CloneRewardItem(Thing source)
        {
            if (source == null) return null;

            Thing clone = ThingMaker.MakeThing(source.def, source.Stuff);
            clone.stackCount = source.stackCount;

            CompQuality srcQuality = source.TryGetComp<CompQuality>();
            CompQuality dstQuality = clone.TryGetComp<CompQuality>();
            if (srcQuality != null && dstQuality != null)
                dstQuality.SetQuality(srcQuality.Quality, ArtGenerationContext.Outsider);

            if (source.def.useHitPoints)
                clone.HitPoints = source.HitPoints;

            var srcForge = source.TryGetComp<Forge.CompForgeEnhancement>();
            var dstForge = clone.TryGetComp<Forge.CompForgeEnhancement>();
            if (srcForge != null && dstForge != null)
            {
                dstForge.refinementLevel = srcForge.refinementLevel;
                if (srcForge.appliedRuneDefNames != null && srcForge.appliedRuneDefNames.Count > 0)
                {
                    dstForge.appliedRuneDefNames = new List<string>(srcForge.appliedRuneDefNames);
                    dstForge.appliedRuneRanks = new List<int>(srcForge.appliedRuneRanks ?? new List<int>());
                }
            }

            return clone;
        }

        public static bool IsCombatKill(Pawn pawn)
        {
            if (pawn == null) return true;
            // IsPrisonerOfColony is a property on Pawn (delegates to guest.IsPrisoner
            // && guest.HostFaction == Faction.OfPlayer). Persists through Pawn.Kill,
            // so it's still observable inside Notify_PawnKilled.
            return !pawn.IsPrisonerOfColony;
        }

        /// <summary>
        /// Closes out a bounty quest as failed when the target was killed in custody
        /// rather than combat. Sends a flavor letter so the player understands why
        /// they didn't get paid. Used by both local and world hunt QuestParts.
        /// </summary>
        public static void VoidBountyContract(Pawn pawn, Quest quest)
        {
            Log.Message($"[Isekai Hunt] Bounty contract voided: {pawn?.LabelCap} died as a prisoner of the colony (capture+execute exploit prevention).");

            Find.LetterStack.ReceiveLetter(
                "Bounty contract voided",
                $"{pawn?.LabelShort ?? "The bounty target"} died in custody rather than in combat. " +
                "The contract pays only for combat kills — executing a captured prisoner does not qualify. " +
                "No XP, silver, or loot will be awarded.",
                LetterDefOf.NegativeEvent);

            if (quest != null && quest.State == QuestState.Ongoing)
            {
                try { quest.End(QuestEndOutcome.Fail, sendLetter: false); }
                catch (System.Exception ex) { Log.Warning($"[Isekai Hunt] Failed to end voided quest: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Checks if a pawn kill is a legitimate combat kill vs a map-cleanup kill.
        /// When a player leaves a map (via gravship/SRTS/caravan), the map may be destroyed,
        /// which calls Kill() on remaining pawns. These should NOT count as quest completions.
        /// Returns true if the kill appears to be from actual combat.
        /// </summary>
        public static bool IsLegitimateQuestKill(Pawn pawn, DamageInfo? dinfo, Map fallbackMap = null)
        {
            // Check instigator FIRST — pawn.Map is null after despawn during Kill(),
            // but dinfo is still valid. A direct hit with an instigator is always legitimate.
            if (dinfo.HasValue && dinfo.Value.Instigator != null)
                return true;

            // For indirect kills (bleedout, fire), check if colonists are present.
            // pawn.Map is null after despawn, so use the quest-provided fallback map.
            Map deathMap = pawn?.Map ?? fallbackMap;
            if (deathMap == null)
                return false;

            // If there are player colonists on the death map (including inside vehicles),
            // it's a legitimate game context (e.g., creature died to fire, bleedout while player is present)
            if (AnyColonistsOnMap(deathMap))
                return true;

            // No instigator and no colonists = map cleanup or abandoned scenario
            return false;
        }
    }

    /// <summary>
    /// QuestPart for local hunts (F, E, D rank) - spawns creature on home map when accepted
    /// </summary>
    public class QuestPart_IsekaiLocalHunt : QuestPart
    {
        public PawnKindDef creatureKind;
        public QuestRank rank;
        public float xpReward;
        public float silverReward;
        public List<Thing> lootRewards = new List<Thing>();
        public Map targetMap;
        public string inSignalEnable;
        public string inSignalAccept;
        public bool isBounty = false;
        
        private Pawn spawnedCreature;
        private bool accepted = false;
        private bool pendingSpawn = false;
        private bool questCompleted = false;
        
        // Static registry of pending local hunts for deferred spawning (gravship travel etc.)
        private static List<QuestPart_IsekaiLocalHunt> pendingHunts = new List<QuestPart_IsekaiLocalHunt>();

        /// <summary>
        /// Migrates saves that were created with the broken CloneRewardItem code.
        /// Older versions put cloned Thing instances into the parent quest's
        /// Reward_Items.items, but vanilla Reward_Items uses LookMode.Reference and
        /// the clones had no deep-save site — so on load the references couldn't be
        /// resolved and Reward_Items.items.RemoveAll(null) emptied the list. An
        /// empty Reward_Items causes MainTabWindow_Quests.DoRewards to skip the
        /// whole choice (its <c>if (!tmpStackElements.Any()) continue;</c> check),
        /// and the "Accept Quest for:" button vanishes from the UI.
        ///
        /// Repair: walk the parent quest's parts, find any Reward_Items left empty,
        /// and repopulate from our deep-saved <c>lootRewards</c>. The Things in
        /// lootRewards are owned by THIS QuestPart's Scribe_Deep so the references
        /// resolve cleanly.
        /// </summary>
        public static void RepairBrokenRewardItems(Quest quest, List<Thing> lootRewards)
        {
            if (quest == null || lootRewards == null || lootRewards.Count == 0) return;
            try
            {
                foreach (var part in quest.PartsListForReading)
                {
                    if (!(part is QuestPart_Choice choicePart) || choicePart.choices == null) continue;
                    foreach (var ch in choicePart.choices)
                    {
                        if (ch?.rewards == null) continue;
                        foreach (var reward in ch.rewards)
                        {
                            if (reward is Reward_Items rItems
                                && (rItems.items == null || rItems.items.Count == 0))
                            {
                                rItems.items = new List<Thing>(lootRewards);
                                Log.Message($"[Isekai Hunt] Repaired empty Reward_Items on quest {quest.id} (legacy save migration — refilled with {lootRewards.Count} loot items).");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai Hunt] RepairBrokenRewardItems failed for quest {quest?.id}: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clear stale static state on game load (prevents cross-save contamination)
        /// </summary>
        public static void ClearStaticState()
        {
            pendingHunts.Clear();
        }
        
        /// <summary>
        /// Called from Game.FinalizeInit AFTER ClearStaticState.
        /// Scans all active quests to rebuild the pendingHunts list.
        /// </summary>
        public static void ReRegisterAfterLoad()
        {
            if (Find.QuestManager != null)
            {
                foreach (var quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.State == QuestState.Ongoing)
                    {
                        foreach (var part in quest.PartsListForReading)
                        {
                            if (part is QuestPart_IsekaiLocalHunt hunt && hunt.pendingSpawn && hunt.spawnedCreature == null)
                            {
                                if (!pendingHunts.Contains(hunt))
                                    pendingHunts.Add(hunt);
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Called from IsekaiHuntTracker.GameComponentTick() every 250 ticks.
        /// Attempts to resolve any deferred spawns (e.g., player was on gravship when quest was accepted).
        /// </summary>
        public static void TryResolvePendingSpawns()
        {
            for (int i = pendingHunts.Count - 1; i >= 0; i--)
            {
                var hunt = pendingHunts[i];
                if (hunt == null || hunt.quest == null || hunt.quest.State != QuestState.Ongoing)
                {
                    pendingHunts.RemoveAt(i);
                    continue;
                }
                
                Map spawnMap = hunt.FindValidSpawnMap();
                if (spawnMap != null)
                {
                    hunt.pendingSpawn = false;
                    pendingHunts.RemoveAt(i);
                    hunt.SpawnCreatureOnMap(spawnMap);
                }
            }
        }
        
        /// <summary>
        /// Finds the best map to spawn the hunt creature on.
        /// Prefers maps with colonists to ensure the hunt target is reachable.
        /// Returns null if no valid map is available (e.g., during gravship travel).
        /// </summary>
        private Map FindValidSpawnMap()
        {
            // First: try the original target map if it's still loaded and has colonists
            if (targetMap != null && !targetMap.Disposed && Find.Maps.Contains(targetMap)
                && targetMap.mapPawns?.FreeColonistsCount > 0)
                return targetMap;
            
            // Second: any player home map with colonists
            foreach (var m in Find.Maps)
            {
                if (m != null && !m.Disposed && m.IsPlayerHome && m.mapPawns?.FreeColonistsCount > 0)
                    return m;
            }
            
            // Third: original target map if still loaded (even without colonists — better than nothing)
            if (targetMap != null && !targetMap.Disposed && Find.Maps.Contains(targetMap))
                return targetMap;
            
            // Fourth: any valid player home map
            Map fallback = Find.AnyPlayerHomeMap;
            if (fallback != null && !fallback.Disposed)
                return fallback;
            
            return null;
        }
        
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            
            // Only spawn when the quest transitions to Ongoing (accepted by player)
            // Check signal tag to avoid false triggers from delay/expiry signals
            if (!accepted && quest != null && quest.State == QuestState.Ongoing)
            {
                // Verify this is an acceptance signal, not an expiry or other internal signal
                string signalTag = signal.tag;
                if (signalTag != null && signalTag.Contains("Expired"))
                    return;
                
                accepted = true;
                
                // Find a valid, populated map for spawning
                Map spawnMap = FindValidSpawnMap();
                if (spawnMap != null)
                {
                    SpawnCreatureOnMap(spawnMap);
                }
                else
                {
                    // No valid map right now (e.g., traveling on gravship, no settlements loaded)
                    // Defer spawning — IsekaiHuntTracker.GameComponentTick will retry when a map becomes available
                    pendingSpawn = true;
                    if (!pendingHunts.Contains(this))
                        pendingHunts.Add(this);
                    Log.Message($"[Isekai Hunt] No valid map for {creatureKind?.LabelCap} spawn — deferring until player arrives at a settlement.");
                    Messages.Message("Isekai_Hunt_Deferred".Translate(), MessageTypeDefOf.NeutralEvent);
                }
            }
        }
        
        private void SpawnCreatureOnMap(Map map)
        {
            
            IntVec3 spawnLoc;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out spawnLoc, map, CellFinder.EdgeRoadChance_Animal))
            {
                // Fallback 1: Try any walkable edge cell (helps on island maps)
                if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Ignore, out spawnLoc))
                {
                    // Fallback 2: Try any walkable cell near map edge
                    if (!CellFinder.TryFindRandomCellNear(map.Center, map, Mathf.Min(map.Size.x, map.Size.z) / 2, 
                        c => c.Standable(map) && !c.Fogged(map) && c.GetRoom(map) != null, out spawnLoc))
                    {
                        // Fallback 3: Map center (guaranteed to be inside the map bounds)
                        spawnLoc = map.Center;
                    }
                }
            }
            
            // RelationWithExtraPawnChanceFactor=0 + ColonistRelationChanceFactor=0
            // suppress the auto-generated parent/sibling pawns RimWorld would otherwise
            // create for backstory. Those extras aren't saved with the quest, so on
            // load the original target's "Parent" relation points at a null pawn and
            // RimWorld logs "Pawn X has relation 'Parent' with null pawn after loading"
            // — see the user-reported warnings for "Devil" and "Koag".
            var spawnReq = new PawnGenerationRequest(
                creatureKind,
                faction: isBounty ? IncidentWorker_IsekaiHunt.GetHostileFactionForBounty() : null,
                PawnGenerationContext.NonPlayer,
                map.Tile,
                forceGenerateNewPawn: true,
                allowDowned: false,
                allowDead: false
            );
            spawnReq.RelationWithExtraPawnChanceFactor = 0f;
            spawnReq.ColonistRelationChanceFactor = 0f;
            spawnedCreature = PawnGenerator.GeneratePawn(spawnReq);
            
            if (spawnedCreature == null)
            {
                Messages.Message("Isekai_Hunt_SpawnFailed".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            
            // Ensure bounty pawns can fight back
            if (isBounty)
            {
                IncidentWorker_IsekaiHunt.EnsureCombatCapable(spawnedCreature);
            }
            
            // === FORCE RANK BEFORE SPAWN ===
            // Must happen before GenSpawn.Spawn so PostSpawnSetup finds the correct rank
            // and doesn't initialize with a random one that overwrites our override.
            
            // Force creature mob rank (non-humanlike)
            var mobRankComp = spawnedCreature.TryGetComp<MobRankComponent>();
            if (mobRankComp != null)
            {
                MobRankTier forcedRank = (MobRankTier)((int)rank);
                mobRankComp.SetRankOverride(forcedRank);
                mobRankComp.SetEliteOverride(true);
            }
            
            // Force humanlike Isekai rank
            if (isBounty && spawnedCreature.RaceProps.Humanlike)
            {
                IncidentWorker_IsekaiHunt.ForceBountyPawnRank(spawnedCreature, rank);
            }
            
            GenSpawn.Spawn(spawnedCreature, spawnLoc, map, Rot4.Random);
            
            // === VERIFY RANK SURVIVED SPAWN ===
            // PostSpawnSetup runs during Spawn and may interfere; re-force if needed.
            if (isBounty && spawnedCreature.RaceProps.Humanlike)
            {
                var comp = spawnedCreature.GetComp<IsekaiComponent>();
                if (comp != null)
                {
                    string expectedRank = rank.ToString();
                    string actualRank = comp.GetRankString();
                    if (actualRank != expectedRank)
                    {
                        Log.Warning($"[Isekai Hunt] Rank changed during spawn! Expected {expectedRank} but got {actualRank} (level {comp.currentLevel}). Re-forcing...");
                        IncidentWorker_IsekaiHunt.ForceBountyPawnRank(spawnedCreature, rank);
                    }
                }
            }
            else if (mobRankComp != null)
            {
                MobRankTier expectedTier = (MobRankTier)((int)rank);
                if (mobRankComp.Rank != expectedTier)
                {
                    Log.Warning($"[Isekai Hunt] Creature rank changed during spawn! Expected {expectedTier} but got {mobRankComp.Rank}. Re-forcing...");
                    mobRankComp.SetRankOverride(expectedTier);
                    mobRankComp.SetEliteOverride(true);
                }
            }
            
            // Elite quest creatures should always be at full health
            spawnedCreature.health.Reset();
            
            // Remove any blindness or other incapacitating conditions - quest mobs should be combat-ready
            IncidentWorker_IsekaiHunt.RemoveIncapacitatingConditions(spawnedCreature);
            
            // Force quest targets to be hostile
            if (isBounty)
            {
                // Bounty pawns: ensure they have a hostile faction (should already from generation)
                if (spawnedCreature.Faction == null || !spawnedCreature.Faction.HostileTo(Faction.OfPlayer))
                {
                    spawnedCreature.SetFaction(Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile), null);
                }
                // Assault Lord with flee/kidnap/steal disabled — bounty contracts pay
                // for combat resolution, so the target must fight to the death instead
                // of fleeing off-map at low HP (vanilla LordJob_AssaultColony default
                // is canTimeoutOrFlee=true, which lets a wounded SSS-rank target run).
                LordMaker.MakeNewLord(
                    spawnedCreature.Faction,
                    new LordJob_AssaultColony(spawnedCreature.Faction,
                        canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                        useAvoidGridSmart: false, canSteal: false),
                    map,
                    new List<Pawn> { spawnedCreature });
            }
            else if (spawnedCreature.RaceProps.IsMechanoid)
            {
                // Mechanoids don't have mental states - make them hostile via faction
                spawnedCreature.SetFaction(Faction.OfMechanoids, null);
                // Assault Lord so the mech actively pursues colonists. Without this it
                // stands idle at spawn and only retaliates when fired on at close range,
                // which presents to players as "the mech boss won't chase me, then dies
                // in a couple shots after I disengage and re-approach."
                // canTimeoutOrFlee=false so wounded mech bosses don't run off-map (the
                // vanilla default lets a 95%-HP boss flee — observed with a Conflagrator).
                LordMaker.MakeNewLord(
                    Faction.OfMechanoids,
                    new LordJob_AssaultColony(Faction.OfMechanoids,
                        canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                        useAvoidGridSmart: false, canSteal: false),
                    map,
                    new List<Pawn> { spawnedCreature });
            }
            else if (spawnedCreature.Faction != null && spawnedCreature.Faction.HostileTo(Faction.OfPlayer))
            {
                // Already has a hostile faction (anomaly entities, modded creatures) - leave as is
            }
            else if (spawnedCreature.mindState?.mentalStateHandler != null)
            {
                // Animals/entities that can have mental states: use manhunter
                spawnedCreature.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
            }
            else
            {
                // Fallback: force hostile faction if no mental state handler
                spawnedCreature.SetFaction(Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile), null);
            }
            
            // Register with tracker (include loot rewards)
            var tracker = Current.Game.GetComponent<IsekaiHuntTracker>();
            if (tracker == null)
            {
                tracker = new IsekaiHuntTracker(Current.Game);
                Current.Game.components.Add(tracker);
            }
            tracker.RegisterHunt(spawnedCreature, rank, xpReward, silverReward, lootRewards, quest, isBounty);
            CameraJumper.TryJumpAndSelect(spawnedCreature);
            
            // Color notification based on rank
            string rankColor = IncidentWorker_IsekaiHunt.GetRankColor(rank);
            string questTypeSuffix = IncidentWorker_IsekaiHunt.GetQuestTypeSuffix(rank, isBounty);
            string coloredMessage = $"<color={rankColor}>" + ("Isekai_Hunt_Started" + questTypeSuffix).Translate(rank.ToString(), spawnedCreature.LabelCap) + "</color>";
            
            Messages.Message(
                coloredMessage,
                new LookTargets(spawnedCreature),
                MessageTypeDefOf.PositiveEvent
            );
        }
        
        public override void Notify_PawnKilled(Pawn pawn, DamageInfo? dinfo)
        {
            if (pawn == spawnedCreature && spawnedCreature != null)
            {
                if (questCompleted) return;

                // Guard against map-cleanup kills (gravship/SRTS departure destroys map → kills pawns)
                // Pass targetMap as fallback since pawn.Map is null after despawn
                if (!IncidentWorker_IsekaiHunt.IsLegitimateQuestKill(pawn, dinfo, targetMap))
                {
                    Log.Message($"[Isekai Hunt] Ignoring map-cleanup kill of {pawn.LabelCap} (no colonists present)");
                    return;
                }

                // Cheese-prevention: shocklance → capture → execute the prisoner is a
                // trivial "skip the fight" exploit on humanlike bounty targets. Void the
                // contract instead of paying full bounty rewards for a custody execution.
                if (!IncidentWorker_IsekaiHunt.IsCombatKill(pawn))
                {
                    questCompleted = true;
                    IncidentWorker_IsekaiHunt.VoidBountyContract(pawn, quest);
                    return;
                }

                CompleteLocalHunt(dinfo?.Instigator as Pawn);
            }
        }
        
        /// <summary>
        /// Safety net: called from IsekaiHuntTracker.GameComponentTick every 250 ticks.
        /// If the creature died but Notify_PawnKilled didn't complete the quest
        /// (e.g., edge case in notification dispatch), catch it here.
        /// </summary>
        public void CheckSafetyNet()
        {
            if (questCompleted) return;
            if (spawnedCreature == null || !spawnedCreature.Dead) return;
            if (quest == null || quest.State != QuestState.Ongoing) return;

            // Verify colonists are/were present (reject gravship map-cleanup deaths)
            if (targetMap != null && !targetMap.Disposed && IncidentWorker_IsekaiHunt.AnyColonistsOnMap(targetMap))
            {
                // Same cheese-prevention as Notify_PawnKilled — the safety net can't
                // bypass the gate, otherwise the player could rely on the 250-tick
                // sweep to retroactively pay out a custody execution.
                if (!IncidentWorker_IsekaiHunt.IsCombatKill(spawnedCreature))
                {
                    questCompleted = true;
                    IncidentWorker_IsekaiHunt.VoidBountyContract(spawnedCreature, quest);
                    return;
                }
                Log.Message($"[Isekai Hunt] Safety net: completing local hunt for dead {spawnedCreature.LabelCap}");
                CompleteLocalHunt(null);
            }
        }
        
        private void CompleteLocalHunt(Pawn killer)
        {
            if (questCompleted) return;
            questCompleted = true;
            
            var tracker = Current.Game.GetComponent<IsekaiHuntTracker>();
            if (tracker != null)
            {
                // Pass targetMap so rewards land correctly even when killer is null
                tracker.OnCreatureKilledByQuest(spawnedCreature, killer, quest, targetMap);
            }
            
            // Delay quest ending to avoid NullRef during kill processing
            var questToEnd = quest;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (questToEnd != null && questToEnd.State == QuestState.Ongoing)
                    {
                        questToEnd.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[Isekai Hunt] Quest end had minor issue (quest still completed successfully): {ex.Message}");
                }
            });
        }
        
        public override string DescriptionPart
        {
            get
            {
                if (!accepted)
                    return "Isekai_Quest_Local_Pending".Translate(creatureKind.LabelCap);
                if (pendingSpawn)
                    return "Isekai_Quest_Local_Deferred".Translate(creatureKind.LabelCap);
                if (spawnedCreature == null || spawnedCreature.Dead)
                    return "Isekai_Quest_Target_Dead".Translate();
                return "Isekai_Quest_Target_Alive".Translate(spawnedCreature.LabelCap);
            }
        }
        
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (spawnedCreature != null && !spawnedCreature.Dead && spawnedCreature.Spawned)
                    yield return spawnedCreature;
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref creatureKind, "creatureKind");
            Scribe_Values.Look(ref rank, "rank", QuestRank.F);
            Scribe_Values.Look(ref xpReward, "xpReward", 0f);
            Scribe_Values.Look(ref silverReward, "silverReward", 0f);
            Scribe_Collections.Look(ref lootRewards, "lootRewards", LookMode.Deep);
            Scribe_References.Look(ref targetMap, "targetMap");
            Scribe_References.Look(ref spawnedCreature, "spawnedCreature");
            Scribe_Values.Look(ref accepted, "accepted", false);
            Scribe_Values.Look(ref pendingSpawn, "pendingSpawn", false);
            Scribe_Values.Look(ref questCompleted, "questCompleted", false);
            Scribe_Values.Look(ref inSignalEnable, "inSignalEnable");
            Scribe_Values.Look(ref inSignalAccept, "inSignalAccept");
            Scribe_Values.Look(ref isBounty, "isBounty", false);

            if (lootRewards == null)
                lootRewards = new List<Thing>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RepairBrokenRewardItems(quest, lootRewards);
            }
        }
    }
    
    /// <summary>
    /// QuestPart for world hunts (B+ rank) - creates site on acceptance, spawns creature(s) when player enters
    /// B-Rank spawns 3-5 creatures, A-Rank spawns 5-8 creatures (pack expeditions)
    /// S+ Rank spawns single legendary creature (raid bosses)
    /// </summary>
    public class QuestPart_IsekaiWorldHunt : QuestPart
    {
        public Site site;
        public PawnKindDef creatureKind;
        public QuestRank rank;
        public float xpReward;
        public float silverReward;
        public List<Thing> lootRewards = new List<Thing>();
        public string inSignalEnable;
        public int targetTile = -1;
        public bool isBounty = false;
        public int precomputedPackSize = 0; // Pre-rolled at quest creation for bounty packs (0 = use GetPackSizeForRank)
        
        private Pawn spawnedCreature; // Primary creature (for backwards compatibility)
        private List<Pawn> spawnedPack = new List<Pawn>(); // All spawned creatures for pack expeditions
        private int packSize = 1; // How many creatures to spawn
        private bool siteCreated = false;
        private bool creatureSpawned = false;
        private bool questCompleted = false;
        
        // Static registry for active hunt quests so patch can find them
        private static List<QuestPart_IsekaiWorldHunt> activeHunts = new List<QuestPart_IsekaiWorldHunt>();
        
        /// <summary>
        /// Clear stale static state on game load (prevents cross-save contamination)
        /// </summary>
        public static void ClearStaticState()
        {
            activeHunts.Clear();
        }
        
        /// <summary>
        /// Called from Game.FinalizeInit AFTER ClearStaticState.
        /// Scans all active quests to rebuild the activeHunts list.
        /// </summary>
        public static void ReRegisterAfterLoad()
        {
            if (Find.QuestManager != null)
            {
                foreach (var quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.State == QuestState.Ongoing || quest.State == QuestState.NotYetAccepted)
                    {
                        foreach (var part in quest.PartsListForReading)
                        {
                            if (part is QuestPart_IsekaiWorldHunt hunt && !activeHunts.Contains(hunt))
                            {
                                activeHunts.Add(hunt);
                            }
                        }
                    }
                }
            }
        }
        
        public static void OnMapGenerated(Map map)
        {
            // Called by Harmony patch when any map is generated
            foreach (var hunt in activeHunts.ToList())
            {
                if (hunt == null) continue;
                
                // Use map.Parent for robust matching — site.HasMap can be unreliable during FinalizeInit
                if (hunt.siteCreated && !hunt.creatureSpawned && 
                    hunt.site != null && map.Parent == hunt.site)
                {
                    Log.Message($"[Isekai Hunt] Player entered hunt site, spawning {hunt.creatureKind?.LabelCap}");
                    hunt.SpawnCreatureOnSite(map);
                }
            }
        }
        
        /// <summary>
        /// Called by IsekaiHuntSpawnChecker MapComponent as a tick-based safety net.
        /// Re-checks all active hunts against a map that already exists.
        /// </summary>
        public static void TrySpawnOnExistingMap(Map map)
        {
            foreach (var hunt in activeHunts.ToList())
            {
                if (hunt == null) continue;
                if (hunt.siteCreated && !hunt.creatureSpawned && 
                    hunt.site != null && map.Parent == hunt.site)
                {
                    Log.Message($"[Isekai Hunt] Safety-net spawn triggered for {hunt.creatureKind?.LabelCap}");
                    hunt.SpawnCreatureOnSite(map);
                }
            }
        }
        
        public override void PostQuestAdded()
        {
            base.PostQuestAdded();
            if (!activeHunts.Contains(this))
                activeHunts.Add(this);
        }
        
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            
            // When quest is accepted (InitiateSignal), create the site
            if (!siteCreated && signal.tag == quest.InitiateSignal && targetTile >= 0)
            {
                CreateSiteOnWorldMap();
            }
            
            // Also check here for map entry
            if (siteCreated && !creatureSpawned && site != null && site.HasMap)
            {
                SpawnCreatureOnSite(site.Map);
            }
            
            // Check if colonists have left the site after quest completion
            if (questCompleted && site != null && site.Spawned)
            {
                CheckIfColonistsLeftSite();
            }
        }
        
        private void CreateSiteOnWorldMap()
        {
            siteCreated = true;
            
            // Create the site now that quest is accepted
            site = IncidentWorker_IsekaiHunt.CreateHuntSite(targetTile, creatureKind, rank, quest);
            
            if (site != null)
            {
                // Color notification based on rank
                string rankColor = IncidentWorker_IsekaiHunt.GetRankColor(rank);
                string questTypeSuffix = IncidentWorker_IsekaiHunt.GetQuestTypeSuffix(rank, isBounty);
                // Use pack-aware message key for packs (bounty or expedition)
                string packSuffix = isBounty ? "_BountyPack" : "_ExpeditionPack";
                string siteRevealedKey = (precomputedPackSize > 1)
                    ? "Isekai_Hunt_SiteRevealed" + packSuffix
                    : "Isekai_Hunt_SiteRevealed" + questTypeSuffix;
                string coloredMessage = (precomputedPackSize > 1)
                    ? $"<color={rankColor}>" + siteRevealedKey.Translate(rank.ToString(), creatureKind.LabelCap, precomputedPackSize.ToString()) + "</color>"
                    : $"<color={rankColor}>" + siteRevealedKey.Translate(rank.ToString(), creatureKind.LabelCap) + "</color>";
                
                // Send message about site appearing
                Messages.Message(
                    coloredMessage,
                    new LookTargets(site),
                    MessageTypeDefOf.PositiveEvent
                );
            }
            else
            {
                Log.Error("[Isekai Hunt] Failed to create hunt site!");
                quest.End(QuestEndOutcome.Fail, sendLetter: false);
            }
        }
        
        /// <summary>
        /// Called when a map is generated - check if it's our hunt site
        /// </summary>
        public override void Notify_FactionRemoved(Faction faction) { }
        
        public void CheckForMapEntry()
        {
            // Check if player has entered the site and creature needs spawning
            if (site != null && site.HasMap && !creatureSpawned)
            {
                SpawnCreatureOnSite(site.Map);
            }
            
            // Check if site was destroyed
            if (siteCreated && site != null && !site.Spawned && !creatureSpawned)
            {
                quest.End(QuestEndOutcome.Fail, sendLetter: false);
            }
        }
        
        /// <summary>
        /// Determines pack size for expedition quests
        /// B-Rank: 3-5 creatures, A-Rank: 5-8 creatures, S+ Rank: 1 (legendary boss)
        /// </summary>
        private int GetPackSizeForRank()
        {
            switch (rank)
            {
                case QuestRank.B:
                    return Rand.RangeInclusive(3, 5);
                case QuestRank.A:
                    return Rand.RangeInclusive(5, 8);
                default:
                    // S, SS, SSS are legendary boss fights - single powerful creature
                    return 1;
            }
        }
        
        private void SpawnCreatureOnSite(Map map)
        {
            creatureSpawned = true;
            spawnedPack = new List<Pawn>();
            
            // Use pre-computed pack size if available (bounty packs), otherwise determine from rank
            packSize = precomputedPackSize > 0 ? precomputedPackSize : GetPackSizeForRank();
            
            // Get tracker ready
            var tracker = Current.Game.GetComponent<IsekaiHuntTracker>();
            if (tracker == null)
            {
                tracker = new IsekaiHuntTracker(Current.Game);
                Current.Game.components.Add(tracker);
            }
            
            // Resolve ONE unified faction up-front so the entire pack is on the same side.
            // Previously faction was resolved per-pawn, which mixed bounty pawns across factions
            // and caused expedition mobs to attack each other.
            Faction packFaction = null;
            bool creatureIsMechanoid = creatureKind?.RaceProps?.IsMechanoid == true;
            if (isBounty)
            {
                packFaction = IncidentWorker_IsekaiHunt.GetHostileFactionForBounty();
            }
            else if (creatureIsMechanoid)
            {
                packFaction = Faction.OfMechanoids;
            }
            // else: animals stay faction=null so they can go manhunter

            // Spawn all creatures in the pack
            int successfullySpawned = 0;
            for (int i = 0; i < packSize; i++)
            {
                IntVec3 spawnLoc = map.Center;
                // Prefer outdoor (non-roofed) cells — don't filter by fog since map may be fully fogged during FinalizeInit
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, 25 + (i * 3), 
                    c => c.Standable(map) && !c.Roofed(map), out spawnLoc))
                {
                    // Fallback: any standable cell
                    if (!CellFinder.TryFindRandomCellNear(map.Center, map, 40, 
                        c => c.Standable(map), out spawnLoc))
                    {
                        spawnLoc = map.Center;
                    }
                }
                
                Pawn creature = null;
                // Retry once on failure so a single bad roll doesn't silently shrink the pack
                for (int attempt = 0; attempt < 2 && creature == null; attempt++)
                {
                    try
                    {
                        var packReq = new PawnGenerationRequest(
                            creatureKind,
                            faction: packFaction,
                            PawnGenerationContext.NonPlayer,
                            map.Tile,
                            forceGenerateNewPawn: true,
                            allowDowned: false,
                            allowDead: false
                        );
                        // Suppress orphan parent/sibling relations on quest-spawned pack members
                        packReq.RelationWithExtraPawnChanceFactor = 0f;
                        packReq.ColonistRelationChanceFactor = 0f;
                        creature = PawnGenerator.GeneratePawn(packReq);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"[Isekai Hunt] Failed to generate pawn of kind {creatureKind?.defName ?? "null"} (attempt {attempt + 1}): {ex.Message}");
                        creature = null;
                    }
                }
                
                if (creature == null)
                {
                    Log.Warning($"[Isekai Hunt] PawnGenerator returned null for {creatureKind?.defName ?? "null"} after 2 attempts");
                    continue;
                }
                
                // Ensure bounty pawns can fight back
                if (isBounty)
                {
                    IncidentWorker_IsekaiHunt.EnsureCombatCapable(creature);
                }
                
                // === FORCE RANK BEFORE SPAWN ===
                // Must happen before GenSpawn.Spawn so PostSpawnSetup finds the correct rank.
                var mobRankComp = creature.TryGetComp<MobRankComponent>();
                if (mobRankComp != null)
                {
                    MobRankTier forcedRank = (MobRankTier)((int)rank);
                    mobRankComp.SetRankOverride(forcedRank);
                    mobRankComp.SetEliteOverride(true);
                }
                
                if (isBounty && creature.RaceProps.Humanlike)
                {
                    IncidentWorker_IsekaiHunt.ForceBountyPawnRank(creature, rank);
                }
                
                GenSpawn.Spawn(creature, spawnLoc, map, Rot4.Random);
                
                // === VERIFY RANK SURVIVED SPAWN ===
                if (isBounty && creature.RaceProps.Humanlike)
                {
                    var comp = creature.GetComp<IsekaiComponent>();
                    if (comp != null)
                    {
                        string expectedRank = rank.ToString();
                        string actualRank = comp.GetRankString();
                        if (actualRank != expectedRank)
                        {
                            Log.Warning($"[Isekai Hunt] Pack pawn rank changed during spawn! Expected {expectedRank} but got {actualRank} (level {comp.currentLevel}). Re-forcing...");
                            IncidentWorker_IsekaiHunt.ForceBountyPawnRank(creature, rank);
                        }
                    }
                }
                else if (mobRankComp != null)
                {
                    MobRankTier expectedTier = (MobRankTier)((int)rank);
                    if (mobRankComp.Rank != expectedTier)
                    {
                        Log.Warning($"[Isekai Hunt] Pack creature rank changed during spawn! Expected {expectedTier} but got {mobRankComp.Rank}. Re-forcing...");
                        mobRankComp.SetRankOverride(expectedTier);
                        mobRankComp.SetEliteOverride(true);
                    }
                }
                
                // Elite quest creatures should always be at full health
                creature.health.Reset();
                
                // Remove any blindness or other incapacitating conditions - quest mobs should be combat-ready
                IncidentWorker_IsekaiHunt.RemoveIncapacitatingConditions(creature);
                
                // Ensure creature's faction matches the pack faction (if we resolved one).
                // This fixes expedition mobs attacking each other when they ended up on different factions.
                if (packFaction != null)
                {
                    if (creature.Faction != packFaction)
                    {
                        creature.SetFaction(packFaction, null);
                    }
                }
                else if (creature.Faction != null && creature.Faction.HostileTo(Faction.OfPlayer))
                {
                    // Anomaly entities / modded creatures with inherent hostile faction — leave as is
                }
                else if (creature.mindState?.mentalStateHandler != null)
                {
                    // Animals/entities with mental states: manhunter (shared state won't target each other)
                    if (!creature.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter))
                    {
                        Log.Warning($"[Isekai Hunt] Manhunter state failed for {creature.LabelCap}, using hostile faction fallback");
                        creature.SetFaction(Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile), null);
                    }
                }
                else
                {
                    creature.SetFaction(Find.FactionManager.FirstFactionOfDef(FactionDefOf.AncientsHostile), null);
                }
                
                spawnedPack.Add(creature);
                successfullySpawned++;
                
                // Set first creature as the "main" one for backwards compatibility
                if (i == 0)
                {
                    spawnedCreature = creature;
                }
            }
            
            // Top-up guarantee: if we under-spawned badly, log it so the issue surfaces
            if (successfullySpawned < packSize)
            {
                Log.Warning($"[Isekai Hunt] Pack under-spawned: {successfullySpawned}/{packSize} for kind {creatureKind?.defName ?? "null"} (rank {rank}). Quest may be trivial.");
            }
            
            // Assign assault Lord to humanlike/mechanoid packs so they attack coordinated instead of wandering.
            // Animal packs use manhunter state per-pawn and do not need a Lord.
            // canTimeoutOrFlee=false so the pack fights to the death rather than fleeing
            // off-map after the first member drops below vanilla's flee threshold.
            if (packFaction != null && spawnedPack.Count > 0 &&
                (spawnedPack[0].RaceProps.Humanlike || spawnedPack[0].RaceProps.IsMechanoid))
            {
                LordMaker.MakeNewLord(
                    packFaction,
                    new LordJob_AssaultColony(packFaction,
                        canKidnap: false, canTimeoutOrFlee: false, sappers: false,
                        useAvoidGridSmart: false, canSteal: false),
                    map,
                    spawnedPack);
            }
            
            // Register only the first creature with tracker for XP/silver rewards
            // All pack members share the same loot pool
            if (spawnedCreature != null)
            {
                tracker.RegisterHunt(spawnedCreature, rank, xpReward, silverReward, lootRewards, quest, isBounty);
            }
            else
            {
                // All spawns failed - abort quest silently
                Log.Warning("[Isekai Hunt] All creature spawns failed, aborting quest.");
                return;
            }

            // Color notification based on rank (combat alert)
            string rankColor = IncidentWorker_IsekaiHunt.GetRankColor(rank);
            string creatureLabel = packSize > 1
                ? $"{packSize}x {creatureKind.LabelCap}"
                : spawnedCreature.LabelCap.ToString();
            string coloredMessage = $"<color={rankColor}>" + "Isekai_Hunt_Appeared".Translate(rank.ToString(), creatureLabel) + "</color>";
            
            Messages.Message(
                coloredMessage,
                new LookTargets(spawnedCreature),
                MessageTypeDefOf.ThreatBig
            );
        }
        
        /// <summary>
        /// Returns the count of pack members still alive
        /// </summary>
        private int GetAlivePackCount()
        {
            if (spawnedPack == null || spawnedPack.Count == 0)
            {
                // Backwards compatibility: just check the single creature
                return (spawnedCreature != null && !spawnedCreature.Dead) ? 1 : 0;
            }
            return spawnedPack.Count(p => p != null && !p.Dead);
        }
        
        /// <summary>
        /// Checks if all pack members are dead (quest complete condition)
        /// </summary>
        private bool IsPackDefeated()
        {
            return GetAlivePackCount() == 0;
        }
        
        private void CheckIfColonistsLeftSite()
        {
            // Don't destroy until colonists have left
            if (site == null || !site.HasMap) 
            {
                // Map was already unloaded, safe to destroy
                if (site != null && site.Spawned)
                {
                    Find.WorldObjects.Remove(site);
                }
                return;
            }
            
            // Check if any colonists are still on the map (including inside vehicles)
            Map siteMap = site.Map;
            bool hasColonists = IncidentWorker_IsekaiHunt.AnyColonistsOnMap(siteMap);
            
            if (!hasColonists)
            {
                // All colonists have left, safe to destroy
                Find.WorldObjects.Remove(site);
            }
        }
        
        public override void Notify_PawnKilled(Pawn pawn, DamageInfo? dinfo)
        {
            // Check if the killed pawn is part of our pack
            bool isPackMember = spawnedPack != null && spawnedPack.Contains(pawn);
            bool isMainCreature = pawn == spawnedCreature;
            
            if (!isPackMember && !isMainCreature) return;
            if (questCompleted) return;
            
            // Guard against map-cleanup kills (gravship/SRTS departure destroys map → kills pawns)
            // Pass site?.Map as fallback since pawn.Map is null after despawn
            if (!IncidentWorker_IsekaiHunt.IsLegitimateQuestKill(pawn, dinfo, site?.Map))
            {
                Log.Message($"[Isekai Hunt] Ignoring map-cleanup kill of {pawn.LabelCap} (no colonists present)");
                return;
            }
            
            // Check if all pack members are now dead
            if (!IsPackDefeated())
            {
                // Still have creatures alive - show progress message
                int remaining = GetAlivePackCount();
                string rankColor = IncidentWorker_IsekaiHunt.GetRankColor(rank);
                Messages.Message(
                    $"<color={rankColor}>" + "Isekai_Hunt_PackProgress".Translate(remaining) + "</color>",
                    MessageTypeDefOf.PositiveEvent
                );
                return;
            }

            // Cheese-prevention: the kill that COMPLETED the pack defeat must have
            // happened in combat. If the player captured the last pack member and
            // executed them, void the contract — same logic as local hunts.
            if (!IncidentWorker_IsekaiHunt.IsCombatKill(pawn))
            {
                questCompleted = true;
                IncidentWorker_IsekaiHunt.VoidBountyContract(pawn, quest);
                return;
            }

            CompleteWorldHunt(dinfo?.Instigator as Pawn);
        }
        
        /// <summary>
        /// Safety net: called from IsekaiHuntTracker.GameComponentTick every 250 ticks.
        /// If all creatures died but Notify_PawnKilled didn't complete the quest
        /// (e.g., edge case in notification dispatch), catch it here.
        /// </summary>
        public void CheckSafetyNet()
        {
            if (questCompleted) return;
            if (!creatureSpawned) return;
            if (quest == null || quest.State != QuestState.Ongoing) return;
            if (!IsPackDefeated()) return;
            
            // Verify colonists are/were present on site (reject gravship cleanup deaths)
            if (site != null && site.HasMap && IncidentWorker_IsekaiHunt.AnyColonistsOnMap(site.Map))
            {
                // Cheese-prevention sweep: if the entire pack died and EVERY member
                // was killed in custody, the player cheesed the whole thing — void.
                // If at least one died in combat we treat the rest as legitimate
                // (the player may have captured stragglers after the real fight).
                if (spawnedPack != null && spawnedPack.Count > 0
                    && spawnedPack.All(p => p == null || p.Dead && !IncidentWorker_IsekaiHunt.IsCombatKill(p)))
                {
                    questCompleted = true;
                    IncidentWorker_IsekaiHunt.VoidBountyContract(spawnedPack[0], quest);
                    return;
                }
                Log.Message($"[Isekai Hunt] Safety net: completing world hunt for dead pack");
                CompleteWorldHunt(null);
            }
        }
        
        private void CompleteWorldHunt(Pawn killer)
        {
            if (questCompleted) return;
            questCompleted = true;
                
            var tracker = Current.Game.GetComponent<IsekaiHuntTracker>();
            if (tracker != null)
            {
                // Pass site map so rewards (XP, silver, loot) land on the quest site
                // even when killer is null (bleedout, turret, fire, trap kills)
                tracker.OnCreatureKilledByQuest(spawnedCreature ?? spawnedPack?.FirstOrDefault(), killer, quest, site?.Map);
            }
            
            Messages.Message(
                "Isekai_Hunt_Complete_CanLeave".Translate(),
                MessageTypeDefOf.PositiveEvent
            );
            
            // Delay quest ending to avoid NullRef during kill processing
            var questToEnd = quest;
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (questToEnd != null && questToEnd.State == QuestState.Ongoing)
                    {
                        questToEnd.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[Isekai Hunt] Quest end had minor issue (quest still completed successfully): {ex.Message}");
                }
            });
        }
        
        public override string DescriptionPart
        {
            get
            {
                if (!siteCreated)
                    return "Isekai_Quest_World_Pending".Translate(creatureKind.LabelCap);
                if (!creatureSpawned)
                    return "Isekai_Quest_World_Travel".Translate(site?.Label ?? "???");
                
                // Check if all creatures are dead (pack expedition complete)
                if (IsPackDefeated())
                    return "Isekai_Quest_Target_Dead".Translate();
                
                // Show pack status if multiple creatures
                int aliveCount = GetAlivePackCount();
                string targetLocation = spawnedCreature?.Map != null ? spawnedCreature.Map.Parent.LabelCap : "Isekai_Hunt_Unknown".Translate();
                
                if (packSize > 1)
                {
                    // Pack expedition - show remaining count
                    string packStatus = $"Pack Status: {aliveCount}/{packSize} remaining";
                    string baseDescription = "Isekai_Quest_Target_Alive".Translate($"{packSize}x {creatureKind.LabelCap}");
                    return $"{baseDescription}\n\n{packStatus}";
                }
                else
                {
                    // Single creature (S+ rank boss)
                    string baseDescription = "Isekai_Quest_Target_Alive".Translate(spawnedCreature?.LabelCap ?? creatureKind.LabelCap);
                    string jumpHint = "Isekai_Hunt_TargetSpawned".Translate(spawnedCreature?.LabelCap ?? creatureKind.LabelCap, targetLocation);
                    return baseDescription + "\n\n" + jumpHint;
                }
            }
        }
        
        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                if (siteCreated && site != null && site.Spawned)
                    yield return site;
                    
                // Show all alive pack members as look targets
                if (spawnedPack != null)
                {
                    foreach (var creature in spawnedPack)
                    {
                        if (creature != null && !creature.Dead && creature.Spawned)
                            yield return creature;
                    }
                }
                else if (spawnedCreature != null && !spawnedCreature.Dead && spawnedCreature.Spawned)
                {
                    yield return spawnedCreature;
                }
            }
        }
        
        public override void Cleanup()
        {
            base.Cleanup();
            
            // Remove from active hunts tracking
            activeHunts.Remove(this);
            
            // Only clean up site if no colonists are on it
            if (siteCreated && site != null && site.Spawned)
            {
                if (site.HasMap && IncidentWorker_IsekaiHunt.AnyColonistsOnMap(site.Map))
                {
                    // Colonists still there (or inside vehicles) - delay cleanup
                    // The site will be cleaned up when map is unloaded
                    return;
                }
                Find.WorldObjects.Remove(site);
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref site, "site");
            Scribe_Defs.Look(ref creatureKind, "creatureKind");
            Scribe_Values.Look(ref rank, "rank", QuestRank.F);
            Scribe_Values.Look(ref xpReward, "xpReward", 0f);
            Scribe_Values.Look(ref silverReward, "silverReward", 0f);
            Scribe_Collections.Look(ref lootRewards, "lootRewards", LookMode.Deep);
            Scribe_References.Look(ref spawnedCreature, "spawnedCreature");
            Scribe_Collections.Look(ref spawnedPack, "spawnedPack", LookMode.Reference);
            Scribe_Values.Look(ref packSize, "packSize", 1);
            Scribe_Values.Look(ref siteCreated, "siteCreated", false);
            Scribe_Values.Look(ref creatureSpawned, "creatureSpawned", false);
            Scribe_Values.Look(ref questCompleted, "questCompleted", false);
            Scribe_Values.Look(ref inSignalEnable, "inSignalEnable");
            Scribe_Values.Look(ref targetTile, "targetTile", -1);
            Scribe_Values.Look(ref isBounty, "isBounty", false);
            Scribe_Values.Look(ref precomputedPackSize, "precomputedPackSize", 0);
            
            if (lootRewards == null)
                lootRewards = new List<Thing>();
            if (spawnedPack == null)
                spawnedPack = new List<Pawn>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                QuestPart_IsekaiLocalHunt.RepairBrokenRewardItems(quest, lootRewards);
            }

            // NOTE: Re-registration into activeHunts is handled by ReRegisterAfterLoad(),
            // called from Game.FinalizeInit AFTER ClearStaticState. This ensures correct ordering.
        }
    }
    
    /// <summary>
    /// QuestPart that displays rewards in the quest tab UI description
    /// </summary>
    /// <summary>
    /// Quest part that displays XP reward in the description
    /// (XP is a custom Isekai system, not a vanilla RimWorld item)
    /// </summary>
    public class QuestPart_IsekaiXPReward : QuestPart
    {
        public float xpReward;
        public string inSignalEnable;
        
        public override string DescriptionPart
        {
            get
            {
                return "Isekai_Quest_XPReward".Translate(NumberFormatting.FormatNum(xpReward));
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref xpReward, "xpReward", 0f);
            Scribe_Values.Look(ref inSignalEnable, "inSignalEnable");
        }
    }
    
    /// <summary>
    /// Legacy rewards display (kept for save compatibility)
    /// </summary>
    public class QuestPart_IsekaiRewards : QuestPart
    {
        public float xpReward;
        public float silverReward;
        public List<Thing> lootRewards = new List<Thing>();
        public string inSignalEnable;
        
        public override string DescriptionPart
        {
            get
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine();
                sb.Append("Isekai_Quest_RewardDisplay".Translate(
                    NumberFormatting.FormatNum(silverReward),
                    NumberFormatting.FormatNum(xpReward)
                ));
                
                // Add loot items to display
                if (lootRewards != null && lootRewards.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Isekai_Quest_LootRewards".Translate());
                    foreach (var item in lootRewards)
                    {
                        if (item != null)
                            sb.AppendLine($"  - {item.LabelCapNoCount} x{item.stackCount}");
                    }
                }
                
                return sb.ToString();
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref xpReward, "xpReward", 0f);
            Scribe_Values.Look(ref silverReward, "silverReward", 0f);
            Scribe_Collections.Look(ref lootRewards, "lootRewards", LookMode.Deep);
            Scribe_Values.Look(ref inSignalEnable, "inSignalEnable");
            
            if (lootRewards == null)
                lootRewards = new List<Thing>();
        }
    }
    
    /// <summary>
    /// Quest rank enum
    /// </summary>
    public enum QuestRank
    {
        F = 0, E = 1, D = 2, C = 3, B = 4, A = 5, S = 6, SS = 7, SSS = 8
    }
    
    /// <summary>
    /// Game component that tracks active hunts and awards rewards on kill
    /// Also handles quest scheduling based on settings frequency
    /// </summary>
    public class IsekaiHuntTracker : GameComponent
    {
        private Dictionary<int, HuntData> activeHunts = new Dictionary<int, HuntData>();
        
        // Quest scheduler
        private int lastQuestTick = -1;
        private const int TicksPerDay = 60000; // 24 in-game hours
        
        public IsekaiHuntTracker(Game game) : base() { }
        
        /// <summary>
        /// Get the number of ticks between quests based on settings.
        /// </summary>
        private int GetTicksBetweenQuests()
        {
            float frequencyDays = IsekaiMod.Settings?.GuildQuestFrequency ?? 1.0f;
            return (int)(frequencyDays * TicksPerDay);
        }
        
        public override void GameComponentTick()
        {
            base.GameComponentTick();
            
            // Check if quests are enabled
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableGuildQuests)
                return;
            
            // Only check every 250 ticks (saves performance)
            if (Find.TickManager.TicksGame % 250 != 0)
                return;
            
            // Resolve any deferred local hunt spawns (e.g., accepted while on gravship)
            QuestPart_IsekaiLocalHunt.TryResolvePendingSpawns();
            
            // Safety net: check for stuck quests where creature died but quest wasn't completed
            CheckForStuckQuests();
            
            // Initialize lastQuestTick if this is a new game
            if (lastQuestTick < 0)
            {
                lastQuestTick = Find.TickManager.TicksGame;
                return;
            }
            
            // Check if enough time has passed since last quest based on frequency setting
            int ticksBetweenQuests = GetTicksBetweenQuests();
            int ticksSinceLastQuest = Find.TickManager.TicksGame - lastQuestTick;
            if (ticksSinceLastQuest >= ticksBetweenQuests)
            {
                TrySpawnDailyHuntQuest();
            }
        }
        
        private void TrySpawnDailyHuntQuest()
        {
            // Double-check quests are enabled
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableGuildQuests)
                return;
            
            // Find a suitable player home map
            Map map = Find.Maps.FirstOrDefault(m => m.IsPlayerHome && m.mapPawns.FreeColonists.Any(p => p.GetComp<IsekaiComponent>() != null));
            if (map == null)
            {
                return; // No valid map, try again later
            }
            
            // Get the incident def
            IncidentDef incidentDef = DefDatabase<IncidentDef>.GetNamedSilentFail("Isekai_HuntSpawn");
            if (incidentDef == null)
            {
                Log.Warning("[Isekai Hunt] Could not find Isekai_HuntSpawn incident def for scheduled quest");
                return;
            }
            
            // Create incident parameters
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(incidentDef.category, map);
            parms.target = map;
            
            // Bypass the normal worker checks and call our custom execution
            var worker = incidentDef.Worker as IncidentWorker_IsekaiHunt;
            bool success = false;
            
            if (worker != null)
            {
                // Call TryExecute which will handle all the quest creation
                success = worker.TryExecute(parms);
            }
            
            float frequencyDays = IsekaiMod.Settings?.GuildQuestFrequency ?? 1.0f;
            
            if (success)
            {
                lastQuestTick = Find.TickManager.TicksGame;
                if (Prefs.DevMode)
                {
                    Log.Message($"[Isekai Hunt] Quest spawned at tick {lastQuestTick}. Next in {frequencyDays} days.");
                }
            }
            else
            {
                // If failed, still update the tick but try again in 1/4 of the frequency time
                int retryTicks = GetTicksBetweenQuests() / 4;
                lastQuestTick = Find.TickManager.TicksGame - GetTicksBetweenQuests() + retryTicks;
                if (Prefs.DevMode)
                {
                    Log.Warning($"[Isekai Hunt] Quest failed to spawn, will retry in {retryTicks / (float)TicksPerDay:F2} days");
                }
            }
        }
        
        /// <summary>
        /// Safety net: scans ongoing quests for dead hunt/boss targets that weren't
        /// completed via Notify_PawnKilled (covers edge cases in notification dispatch).
        /// Called every 250 ticks from GameComponentTick.
        /// </summary>
        private void CheckForStuckQuests()
        {
            try
            {
                if (Find.QuestManager == null) return;
                
                foreach (var quest in Find.QuestManager.QuestsListForReading)
                {
                    if (quest.State != QuestState.Ongoing) continue;
                    
                    foreach (var part in quest.PartsListForReading)
                    {
                        if (part is QuestPart_IsekaiLocalHunt localHunt)
                            localHunt.CheckSafetyNet();
                        else if (part is QuestPart_IsekaiWorldHunt worldHunt)
                            worldHunt.CheckSafetyNet();
                        else if (part is WorldBoss.QuestPart_WorldBoss worldBoss)
                            worldBoss.CheckSafetyNet();
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[Isekai Hunt] Safety net check error: {ex.Message}");
            }
        }
        
        public void RegisterHunt(Pawn creature, QuestRank rank, float xp, float silver, List<Thing> loot, Quest quest, bool isBounty = false)
        {
            // The 'loot' parameter is intentionally NOT stored on HuntData anymore.
            // Loot is resolved lazily via questId → QuestPart.lootRewards at award
            // time (see GetLootForQuest), which keeps the Thing references in a
            // single place (the QuestPart) and prevents the duplicate Scribe_Deep
            // registration that previously caused load errors.
            activeHunts[creature.thingIDNumber] = new HuntData
            {
                rank = rank,
                xpReward = xp,
                silverReward = silver,
                questId = quest?.id ?? -1,
                isBounty = isBounty
            };
        }
        
        /// <summary>
        /// Lazily resolves the loot list for a quest by walking its parts and reading
        /// the QuestPart's lootRewards. Replaces the old <c>HuntData.lootRewards</c>
        /// field, which was a third Scribe_Deep site for the same Thing references
        /// and caused "Cannot register Thing X, Id already used by..." on load.
        /// </summary>
        private static List<Thing> GetLootForQuest(int questId)
        {
            if (questId < 0) return null;
            Quest quest = Find.QuestManager?.QuestsListForReading
                ?.FirstOrDefault(q => q.id == questId);
            if (quest == null) return null;
            foreach (var part in quest.PartsListForReading)
            {
                if (part is QuestPart_IsekaiLocalHunt local && local.lootRewards != null && local.lootRewards.Count > 0)
                    return local.lootRewards;
                if (part is QuestPart_IsekaiWorldHunt world && world.lootRewards != null && world.lootRewards.Count > 0)
                    return world.lootRewards;
            }
            return null;
        }

        public void OnCreatureKilledByQuest(Pawn creature, Pawn killer, Quest quest, Map questSiteMap = null)
        {
            Log.Message($"[Isekai Hunt] OnCreatureKilledByQuest called for {creature?.LabelCap}, killer: {killer?.LabelCap}, questMap: {questSiteMap?.ToString() ?? "null"}");
            
            if (creature == null)
            {
                Log.Warning("[Isekai Hunt] OnCreatureKilledByQuest called with null creature — awarding rewards from first active hunt entry");
                // Fallback: grab the first hunt data entry (if any) so rewards aren't lost
                if (activeHunts.Count > 0)
                {
                    var firstEntry = activeHunts.First();
                    var fallbackData = firstEntry.Value;
                    activeHunts.Remove(firstEntry.Key);
                    AwardXP(fallbackData.xpReward, killer, null, questSiteMap);
                    AwardSilver(fallbackData.silverReward, killer, questSiteMap);
                    AwardLoot(GetLootForQuest(fallbackData.questId), killer, questSiteMap);
                    
                    string fallbackSuffix = IncidentWorker_IsekaiHunt.GetQuestTypeSuffix(fallbackData.rank, fallbackData.isBounty);
                    Map fallbackMap = killer?.Map ?? Find.AnyPlayerHomeMap;
                    GlobalTargetInfo fallbackTarget = GlobalTargetInfo.Invalid;
                    if (fallbackMap != null)
                    {
                        IntVec3 fallbackPos = SafeDropCell(fallbackMap, killer);
                        if (fallbackPos.IsValid)
                            fallbackTarget = new GlobalTargetInfo(fallbackPos, fallbackMap);
                    }
                    Find.LetterStack.ReceiveLetter(
                        ("Isekai_Hunt_Complete_Label" + fallbackSuffix).Translate(fallbackData.rank.ToString()),
                        ("Isekai_Hunt_Complete_Text" + fallbackSuffix).Translate(
                            fallbackData.rank.ToString(),
                            NumberFormatting.FormatNum(fallbackData.xpReward),
                            NumberFormatting.FormatNum(fallbackData.silverReward)
                        ),
                        LetterDefOf.PositiveEvent,
                        fallbackTarget
                    );
                }
                return;
            }
            
            if (!activeHunts.TryGetValue(creature.thingIDNumber, out var huntData))
            {
                Log.Warning($"[Isekai Hunt] Creature {creature.thingIDNumber} not found in activeHunts!");
                return;
            }
            
            activeHunts.Remove(creature.thingIDNumber);
            
            Log.Message($"[Isekai Hunt] Awarding rewards: {huntData.xpReward} XP, {huntData.silverReward} silver");
            AwardXP(huntData.xpReward, killer, creature, questSiteMap);
            AwardSilver(huntData.silverReward, killer ?? creature, questSiteMap);
            AwardLoot(GetLootForQuest(huntData.questId), killer ?? creature, questSiteMap);
            
            // Determine letter target: prefer killer's position, then creature corpse location, then trade drop spot
            Map letterMap;
            IntVec3 letterPos;
            if (killer != null && killer.Map != null)
            {
                letterMap = killer.Map;
                letterPos = killer.Position;
            }
            else if (creature != null)
            {
                // Dead creature: use corpse's map/position (MapHeld/PositionHeld track the corpse)
                letterMap = creature.MapHeld ?? creature.Map ?? Find.AnyPlayerHomeMap;
                letterPos = creature.MapHeld != null ? creature.PositionHeld : creature.Position;
                // Validate position is actually on this map (prevents quest-site coords on home map)
                if (letterMap != null && !letterPos.InBounds(letterMap))
                    letterPos = SafeDropCell(letterMap, creature);
            }
            else
            {
                letterMap = Find.AnyPlayerHomeMap;
                letterPos = letterMap != null ? SafeDropCell(letterMap, null) : IntVec3.Zero;
            }
            GlobalTargetInfo letterTarget = letterMap != null 
                ? new GlobalTargetInfo(letterPos, letterMap)
                : GlobalTargetInfo.Invalid;
            
            string questTypeSuffix = IncidentWorker_IsekaiHunt.GetQuestTypeSuffix(huntData.rank, huntData.isBounty);
            Find.LetterStack.ReceiveLetter(
                ("Isekai_Hunt_Complete_Label" + questTypeSuffix).Translate(huntData.rank.ToString()),
                ("Isekai_Hunt_Complete_Text" + questTypeSuffix).Translate(
                    huntData.rank.ToString(),
                    NumberFormatting.FormatNum(huntData.xpReward),
                    NumberFormatting.FormatNum(huntData.silverReward)
                ),
                LetterDefOf.PositiveEvent,
                letterTarget
            );
        }
        
        public void OnCreatureKilled(Pawn creature, Pawn killer)
        {
            if (!activeHunts.TryGetValue(creature.thingIDNumber, out var huntData))
                return;
            
            if (huntData.questId >= 0)
            {
                var quest = Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == huntData.questId);
                if (quest != null && quest.State == QuestState.Ongoing)
                    return;
            }
            
            activeHunts.Remove(creature.thingIDNumber);
            AwardXP(huntData.xpReward, killer, creature);
            AwardSilver(huntData.silverReward, killer ?? creature);
            AwardLoot(GetLootForQuest(huntData.questId), killer ?? creature);
            
            string questTypeSuffix = IncidentWorker_IsekaiHunt.GetQuestTypeSuffix(huntData.rank, huntData.isBounty);
            Find.LetterStack.ReceiveLetter(
                ("Isekai_Hunt_Complete_Label" + questTypeSuffix).Translate(huntData.rank.ToString()),
                ("Isekai_Hunt_Complete_Text" + questTypeSuffix).Translate(
                    huntData.rank.ToString(),
                    NumberFormatting.FormatNum(huntData.xpReward),
                    NumberFormatting.FormatNum(huntData.silverReward)
                ),
                LetterDefOf.PositiveEvent
            );
        }
        
        private void AwardXP(float totalXP, Pawn killer = null, Pawn creature = null, Map questSiteMap = null)
        {
            // Priority 1: Get eligible pawns from the quest site map (where the fight happened)
            // Uses GetIsekaiPawnsOnMap which includes ghouls (unlike FreeColonists)
            List<Pawn> colonists = null;
            
            // Use the quest-provided site map first (always valid), then try killer/creature maps
            Map questMap = questSiteMap ?? killer?.Map ?? creature?.MapHeld ?? creature?.Map;
            if (questMap != null)
            {
                colonists = IsekaiComponent.GetIsekaiPawnsOnMap(questMap);
            }
            
            // Priority 2: If no colonists found on quest map (e.g. caravan scenario),
            // check all maps for colonists (including non-home maps like quest sites)
            if (colonists == null || !colonists.Any())
            {
                colonists = IsekaiComponent.GetIsekaiPawnsAllMaps();
            }
            
            // Priority 3: Check caravans — pawns traveling in a caravan won't be on any map
            if (!colonists.Any())
            {
                colonists = IsekaiComponent.GetIsekaiPawnsInCaravans();
            }
            
            if (!colonists.Any()) return;
            
            // Pre-divide by global XP multiplier since GainXP will re-apply it.
            // This matches WorldBoss reward behavior and ensures the quest letter
            // shows the actual effective XP, not a value that gets secretly tripled.
            float xpMult = IsekaiMod.Settings?.XPMultiplier ?? 3f;
            if (xpMult <= 0f) xpMult = 1f;
            int xpPerPawn = Mathf.RoundToInt((totalXP / xpMult) / colonists.Count);
            
            foreach (var pawn in colonists)
            {
                var comp = IsekaiComponent.GetCached(pawn);
                comp?.GainXP(xpPerPawn, "HuntReward");
            }
            
            // Award XP to player-owned creatures (bonded pets/mechs) on the same map
            // Only bonded animals receive quest XP to prevent all tamed animals (chickens, etc.) from leveling
            int creatureCount = 0;
            Map xpMap = questMap ?? Find.AnyPlayerHomeMap;
            if (xpMap != null)
            {
                foreach (var critter in xpMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
                {
                    if (critter.RaceProps.Humanlike) continue;
                    // Animals must be "combat pets": bonded, attack-trained, or master-assigned.
                    // Plain farm animals (chickens, etc.) are still excluded so they don't level
                    // from quests they had no part in.
                    if (critter.RaceProps.Animal && !IsCombatPet(critter)) continue;
                    var rankComp = critter.TryGetComp<MobRankComponent>();
                    if (rankComp == null) continue;
                    rankComp.GainXP(xpPerPawn, "HuntReward");
                    creatureCount++;
                }
            }
            
            Log.Message($"[Isekai Hunt] Awarded {xpPerPawn} XP to {colonists.Count} colonists and {creatureCount} creatures (quest site map: {questMap?.ToString() ?? "none"})");
        }
        
        /// <summary>
        /// Check if an animal is bonded to any colonist.
        /// </summary>
        public static bool IsBondedToColonist(Pawn animal)
        {
            if (animal?.relations == null) return false;
            foreach (var rel in animal.relations.DirectRelations)
            {
                if (rel.def == PawnRelationDefOf.Bond && rel.otherPawn != null && !rel.otherPawn.Dead)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if an animal qualifies as a "combat pet" eligible for quest XP.
        /// Counts as combat pet when ANY of:
        ///   - bonded to a colonist (Bond relation)
        ///   - trained for Release (attack training)
        ///   - has a master assigned via PlayerSettings
        /// This prevents quest XP from leaking onto chickens/cows while still rewarding
        /// huskies, war elephants, manhunter-trained dromedaries, etc.
        /// </summary>
        public static bool IsCombatPet(Pawn animal)
        {
            if (animal == null) return false;
            if (IsBondedToColonist(animal)) return true;
            try
            {
                if (animal.training != null && TrainableDefOf.Release != null
                    && animal.training.HasLearned(TrainableDefOf.Release))
                    return true;
            }
            catch { /* TrainableDefOf.Release may be null in odd configs */ }
            if (animal.playerSettings?.Master != null && !animal.playerSettings.Master.Dead)
                return true;
            return false;
        }
        
        /// <summary>
        /// Picks a safe drop cell on <paramref name="map"/> without risking a native
        /// crash inside vanilla pathfinding. Tries (in order): the pawn's current/held
        /// position, a guarded TradeDropSpot, then map.Center as a last-resort fallback
        /// that requires no path-grid access.
        /// </summary>
        /// <remarks>
        /// Why: DropCellFinder.TradeDropSpot walks Map.pathing.PathGrid; if the grid
        /// is in a transitional/corrupted state (observed mid-Pawn.Kill cascades with
        /// Yayo's Combat + AllowTool + custom pawn-class mods), the native walker
        /// SIGSEGVs and the crash cannot be caught. We therefore avoid TradeDropSpot
        /// entirely whenever we already have a perfectly good pawn position.
        /// </remarks>
        internal static IntVec3 SafeDropCell(Map map, Pawn primary, Pawn secondary = null)
        {
            if (map == null) return IntVec3.Invalid;

            if (primary != null)
            {
                if (primary.MapHeld == map && primary.PositionHeld.IsValid && primary.PositionHeld.InBounds(map))
                    return primary.PositionHeld;
                if (primary.Map == map && primary.Position.IsValid && primary.Position.InBounds(map))
                    return primary.Position;
            }

            if (secondary != null)
            {
                if (secondary.MapHeld == map && secondary.PositionHeld.IsValid && secondary.PositionHeld.InBounds(map))
                    return secondary.PositionHeld;
                if (secondary.Map == map && secondary.Position.IsValid && secondary.Position.InBounds(map))
                    return secondary.Position;
            }

            try
            {
                IntVec3 trade = DropCellFinder.TradeDropSpot(map);
                if (trade.IsValid && trade.InBounds(map))
                    return trade;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Isekai] DropCellFinder.TradeDropSpot threw on map {map}: {ex.Message}. Falling back to map center.");
            }

            return map.Center.IsValid && map.Center.InBounds(map) ? map.Center : IntVec3.Invalid;
        }

        private void AwardSilver(float amount, Pawn nearPawn, Map questSiteMap = null)
        {
            // Use quest site map first (always valid for world hunts), then pawn maps
            Map map = questSiteMap ?? nearPawn?.MapHeld ?? nearPawn?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            IntVec3 dropLoc = SafeDropCell(map, nearPawn);
            if (!dropLoc.IsValid) return;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = Mathf.RoundToInt(amount);
            GenPlace.TryPlaceThing(silver, dropLoc, map, ThingPlaceMode.Near);

            Log.Message($"[Isekai Hunt] Spawned {silver.stackCount} silver at {dropLoc}");
        }
        
        private void AwardLoot(List<Thing> loot, Pawn nearPawn, Map questSiteMap = null)
        {
            if (loot == null || loot.Count == 0) return;

            // Use quest site map first (always valid for world hunts), then pawn maps
            Map map = questSiteMap ?? nearPawn?.MapHeld ?? nearPawn?.Map ?? Find.AnyPlayerHomeMap;
            if (map == null) return;

            IntVec3 dropLoc = SafeDropCell(map, nearPawn);
            if (!dropLoc.IsValid) return;

            foreach (var item in loot)
            {
                if (item == null) continue;
                // Silver lives in lootRewards purely so it has a deep-save site for
                // Reward_Items.items to reference (Reward_Items uses LookMode.Reference).
                // The actual silver award is handled by AwardSilver — skip here to
                // avoid double-paying.
                if (item.def == ThingDefOf.Silver) continue;
                Thing spawnedItem = IncidentWorker_IsekaiHunt.CloneRewardItem(item);
                if (spawnedItem == null) continue;
                GenPlace.TryPlaceThing(spawnedItem, dropLoc, map, ThingPlaceMode.Near);
            }

            Log.Message($"[Isekai Hunt] Spawned {loot.Count} loot items at {dropLoc}");
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            
            // Save/load daily quest scheduler
            Scribe_Values.Look(ref lastQuestTick, "lastQuestTick", -1);
            
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var keys = activeHunts.Keys.ToList();
                var values = activeHunts.Values.ToList();
                Scribe_Collections.Look(ref keys, "huntKeys", LookMode.Value);
                Scribe_Collections.Look(ref values, "huntValues", LookMode.Deep);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                List<int> keys = null;
                List<HuntData> values = null;
                Scribe_Collections.Look(ref keys, "huntKeys", LookMode.Value);
                Scribe_Collections.Look(ref values, "huntValues", LookMode.Deep);
                
                activeHunts = new Dictionary<int, HuntData>();
                if (keys != null && values != null)
                {
                    for (int i = 0; i < keys.Count && i < values.Count; i++)
                    {
                        activeHunts[keys[i]] = values[i];
                    }
                }
            }
        }
        
        private class HuntData : IExposable
        {
            public QuestRank rank;
            public float xpReward;
            public float silverReward;
            public int questId = -1;
            public bool isBounty = false;

            // NOTE: lootRewards used to live here as a Scribe_Deep List<Thing>. That made
            // it the THIRD deep-save site for the same Thing references (QuestPart's
            // lootRewards + vanilla Reward_Items.items + this), which produced
            // "Cannot register Thing X, Id already used by..." errors on load.
            // Now we resolve loot lazily via questId → QuestPart at award time, so
            // this class no longer owns any Thing references and there's nothing to
            // double-register. See IsekaiHuntTracker.GetLootForQuest.

            public void ExposeData()
            {
                Scribe_Values.Look(ref rank, "rank", QuestRank.F);
                Scribe_Values.Look(ref xpReward, "xpReward", 0f);
                Scribe_Values.Look(ref silverReward, "silverReward", 0f);
                Scribe_Values.Look(ref questId, "questId", -1);
                Scribe_Values.Look(ref isBounty, "isBounty", false);

                // Legacy <lootRewards> Things from old saves are intentionally NOT
                // looked up here — letting Scribe parse them would re-register their
                // IDs and clash with the same Things already deep-saved in the
                // QuestPart and Reward_Items. By omitting the Look call entirely the
                // XML element is silently orphaned. Quest still completes via the
                // QuestPart-side lookup; only loot data from pre-fix saves is lost.
            }
        }
    }
    
    /// <summary>
    /// Quest part that ends a quest when a specific signal is received.
    /// Used for both offer expiry (unaccepted quests) and completion expiry (accepted quests that timed out).
    /// </summary>
    public class QuestPart_OfferExpiry : QuestPart
    {
        public string inSignal;
        public bool sendLetter;
        /// <summary>When true, only ends the quest if it has NOT been accepted yet.</summary>
        public bool offerOnly;
        
        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            
            if (!string.IsNullOrEmpty(inSignal) && signal.tag == inSignal)
            {
                if (quest == null) return;
                
                if (offerOnly)
                {
                    // Pre-acceptance expiry: only end if not yet accepted
                    if (quest.State == QuestState.NotYetAccepted)
                        quest.End(QuestEndOutcome.Fail, sendLetter: sendLetter);
                }
                else
                {
                    // Post-acceptance expiry: end if still ongoing (accepted but not completed)
                    if (quest.State == QuestState.Ongoing)
                        quest.End(QuestEndOutcome.Fail, sendLetter: sendLetter);
                }
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref sendLetter, "sendLetter");
            Scribe_Values.Look(ref offerOnly, "offerOnly");
        }
    }
}