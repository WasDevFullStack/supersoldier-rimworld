using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;
using IsekaiLeveling.Forge;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;

namespace IsekaiLeveling.WorldBoss
{
    /// <summary>
    /// Rare incident that spawns a World Boss quest.
    /// The Adventurer's Guild announces a catastrophic creature sighted on the world map.
    /// Players have 7 days to reach the tile and fight the boss.
    /// </summary>
    public class IncidentWorker_WorldBoss : IncidentWorker
    {
        private const int OFFER_EXPIRY_DAYS = 7;
        private const int COMPLETION_EXPIRY_DAYS = 21;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableGuildQuests)
                return false;

            Map map = parms.target as Map;
            if (map == null) return false;

            // Need at least one colonist with Isekai component at level 51+ (A-rank minimum)
            bool hasEligiblePawns = map.mapPawns.FreeColonists
                .Any(p =>
                {
                    var comp = p.GetComp<IsekaiComponent>();
                    return comp != null && comp.Level >= 51;
                });

            if (!hasEligiblePawns && Prefs.DevMode)
            {
                Log.Message("[Isekai WorldBoss] CanFireNowSub: rejected — no A-rank (level 51+) colonist on map");
            }

            return hasEligiblePawns;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (IsekaiMod.Settings != null && !IsekaiMod.Settings.EnableGuildQuests)
                return false;

            Map map = parms.target as Map;
            if (map == null) return false;

            // Select the strongest combat creature available
            PawnKindDef bossKind = SelectWorldBossCreature();
            if (bossKind == null)
            {
                Log.Warning("[Isekai WorldBoss] No suitable creature found for world boss");
                return false;
            }

            // Find a random tile on the world map
            int baseTile = map.Tile;
            if (!TryFindBossTile(baseTile, out int targetTile))
            {
                Log.Warning("[Isekai WorldBoss] Could not find suitable tile for world boss");
                return false;
            }

            // Calculate massive rewards
            float xpReward = CalculateWorldBossXP(bossKind.combatPower);
            float silverReward = CalculateWorldBossSilver(bossKind.combatPower);

            // Determine faction presence — always at least 2 groups
            int factionGroupCount = 2;
            float roll = Rand.Value;
            if (roll < 0.40f)
                factionGroupCount = 3; // 40% chance of 3 groups
            // else remains 2 (60% chance)

            // Create the quest
            CreateWorldBossQuest(bossKind, targetTile, baseTile, xpReward, silverReward, factionGroupCount, map);

            return true;
        }

        /// <summary>
        /// Selects the highest combat power creature available as the world boss.
        /// Prefers predators and aggressive creatures for dramatic encounters.
        /// </summary>
        public static PawnKindDef SelectWorldBossCreature()
        {
            var allCreatures = DefDatabase<PawnKindDef>.AllDefs
                .Where(pk => pk.RaceProps != null &&
                             !pk.RaceProps.Humanlike &&
                             !pk.RaceProps.Dryad &&
                             pk.combatPower >= 100f &&
                             !IsVehicleDef(pk.race) &&
                             !IsTransformingCreature(pk) &&
                             !IsBlacklistedBoss(pk) &&
                             !IsFactionDisabled(pk))
                .OrderByDescending(pk => pk.combatPower)
                .ToList();

            if (!allCreatures.Any()) return null;

            // Take top 30% strongest creatures and pick randomly for variety
            int topCount = Mathf.Max(1, allCreatures.Count * 30 / 100);
            var topCreatures = allCreatures.Take(topCount).ToList();

            // Prefer predators/aggressive creatures
            var aggressive = topCreatures
                .Where(pk => pk.RaceProps.predator ||
                             pk.RaceProps.manhunterOnDamageChance > 0.3f ||
                             pk.combatPower >= 150f)
                .ToList();

            return aggressive.Any() ? aggressive.RandomElement() : topCreatures.RandomElement();
        }

        private static bool IsVehicleDef(ThingDef def)
        {
            if (def == null) return false;
            string className = def.thingClass?.FullName ?? "";
            if (className.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (className.StartsWith("SRTS", StringComparison.OrdinalIgnoreCase)) return true;
            string defName = def.defName ?? "";
            if (defName.StartsWith("VVE_", StringComparison.OrdinalIgnoreCase) ||
                defName.IndexOf("Vehicle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.StartsWith("DVVE_", StringComparison.OrdinalIgnoreCase) ||
                defName.StartsWith("SRTS_", StringComparison.OrdinalIgnoreCase))
                return true;
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
            return false;
        }

        private static bool IsTransformingCreature(PawnKindDef pk)
        {
            if (pk == null) return false;
            string defName = pk.defName ?? "";
            string label = pk.label?.ToLower() ?? "";
            if (defName.IndexOf("Swarmling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.IndexOf("Larva", StringComparison.OrdinalIgnoreCase) >= 0 ||
                defName.IndexOf("Hatchling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.Contains("swarmling") || label.Contains("larva") || label.Contains("hatchling"))
                return true;
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
            
            if (pk.RaceProps.IsMechanoid && Faction.OfMechanoids == null)
                return true;
            
            if (pk.RaceProps.Insect && Faction.OfInsects == null)
                return true;
            
            return false;
        }

        /// <summary>
        /// Creatures that should never be selected as a world boss.
        /// These are mechanoid superweapons or other entities that don't work
        /// well as a traditional boss fight encounter.
        /// </summary>
        private static readonly HashSet<string> blacklistedBossDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Mech_Nociosphere",
            "Nociosphere",
            // Suicidal mechs — detonate on contact, unfit for boss fights
            "Mech_HunterDrone",
            "HunterDrone",
            "Mech_InfernoDrone",
            "InfernoDrone",
            "Mech_AgroBooster",
            "AgroBooster",
        };

        private static bool IsBlacklistedBoss(PawnKindDef pk)
        {
            if (pk == null) return false;
            string defName = pk.defName ?? "";
            if (blacklistedBossDefNames.Contains(defName)) return true;
            // Also catch by label in case of modded variants
            string label = pk.label?.ToLower() ?? "";
            if (label.Contains("nociosphere")) return true;
            return false;
        }

        public static bool TryFindBossTile(int baseTile, out int tile)
        {
            tile = -1;
            WorldGrid grid = Find.WorldGrid;

            // World bosses appear anywhere on the map — random distant tile (15-60 tiles)
            for (int attempts = 0; attempts < 500; attempts++)
            {
                int candidateTile = Rand.Range(0, grid.TilesCount);
                if (!grid.InBounds(candidateTile)) continue;
                if (grid[candidateTile].WaterCovered) continue;
                if (Find.World.Impassable(candidateTile)) continue;
                if (Find.WorldObjects.AnyWorldObjectAt(candidateTile)) continue;

                float dist = grid.ApproxDistanceInTiles(baseTile, candidateTile);
                if (dist >= 15f && dist <= 60f)
                {
                    tile = candidateTile;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// World Boss XP: estimate for quest display purposes.
        /// Actual per-pawn XP is calculated at award time using level-scaled formula.
        /// This provides an approximate display value based on current colony average level.
        /// </summary>
        public static float CalculateWorldBossXP(float combatPower)
        {
            // Estimate average colony level for display purposes
            float avgLevel = 30f; // Reasonable default
            try
            {
                Map map = Find.AnyPlayerHomeMap;
                if (map != null)
                {
                    var colonists = map.mapPawns.FreeColonists;
                    if (colonists.Any())
                    {
                        float totalLevel = 0f;
                        int count = 0;
                        foreach (var pawn in colonists)
                        {
                            var comp = IsekaiComponent.GetCached(pawn);
                            if (comp != null)
                            {
                                totalLevel += comp.currentLevel;
                                count++;
                            }
                        }
                        if (count > 0) avgLevel = totalLevel / count;
                    }
                }
            }
            catch { }

            // Estimate: ~15 levels worth of XP at the average colony level, for ~10 colonists
            // XPToNextLevel(n) = 100 * n^1.5; sum for 15 levels starting at avgLevel
            float estimatedPerPawn = 0f;
            for (int i = 0; i < WORLD_BOSS_TARGET_LEVELS; i++)
            {
                estimatedPerPawn += 100f * Mathf.Pow(avgLevel + i, 1.5f);
            }
            return estimatedPerPawn * 10f; // Display as total for ~10 colonists
        }

        /// <summary>Target number of levels each pawn gains from a world boss kill.</summary>
        public const int WORLD_BOSS_TARGET_LEVELS = 15;

        /// <summary>
        /// World Boss Silver: massive bounty
        /// </summary>
        public static float CalculateWorldBossSilver(float combatPower)
        {
            float baseSilver = 30f * Mathf.Pow(1.8f, 9) * 5f;
            float cpScale = Mathf.Clamp(0.8f + (combatPower / 150f), 0.8f, 3.0f);
            return baseSilver * cpScale;
        }

        public static void CreateWorldBossQuest(PawnKindDef bossKind, int targetTile, int baseTile,
            float xpReward, float silverReward, int factionGroupCount, Map homeMap)
        {
            Quest quest = Quest.MakeRaw();
            quest.name = "Isekai_WorldBoss_QuestName".Translate(bossKind.LabelCap);
            quest.appearanceTick = Find.TickManager.TicksGame;
            quest.challengeRating = 5; // Max difficulty

            // Set quest root (required — VEF's DoRow postfix calls quest.root.GetModExtension
            // without a null check, so a null root causes NRE in the Quests tab)
            quest.root = DefDatabase<QuestScriptDef>.GetNamedSilentFail("Isekai_HuntQuestScript")
                      ?? DefDatabase<QuestScriptDef>.GetNamedSilentFail("OpportunitySite_ItemStash");

            float distance = Find.WorldGrid.ApproxDistanceInTiles(baseTile, targetTile);

            quest.description = "Isekai_WorldBoss_QuestDesc".Translate(
                bossKind.LabelCap,
                NumberFormatting.FormatNum(xpReward),
                NumberFormatting.FormatNum(silverReward),
                distance.ToString("F0")
            );

            // Generate loot rewards (way more than SSS)
            List<Thing> lootRewards = GenerateWorldBossLoot(bossKind.combatPower);

            // Create the world boss quest part
            QuestPart_WorldBoss bossPart = new QuestPart_WorldBoss();
            bossPart.bossKind = bossKind;
            bossPart.targetTile = targetTile;
            bossPart.xpReward = xpReward;
            bossPart.silverReward = silverReward;
            bossPart.lootRewards = lootRewards;
            bossPart.factionGroupCount = factionGroupCount;
            bossPart.inSignalEnable = quest.InitiateSignal;
            quest.AddPart(bossPart);

            // Reward display.
            // Silver lives in lootRewards (which the QuestPart deep-scribes) so it
            // has a deep-save site Reward_Items.items can reference. Without this,
            // Reward_Items uses LookMode.Reference and would null-out the silver
            // Thing on load — see the equivalent fix in IncidentWorker_IsekaiHunt.
            if (silverReward > 0 && lootRewards != null)
            {
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = Mathf.RoundToInt(silverReward);
                lootRewards.Insert(0, silver);
            }
            List<Thing> allRewardItems = lootRewards != null
                ? new List<Thing>(lootRewards)
                : new List<Thing>();

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

            // XP display
            QuestPart_IsekaiXPReward xpPart = new QuestPart_IsekaiXPReward();
            xpPart.xpReward = xpReward;
            xpPart.inSignalEnable = quest.InitiateSignal;
            quest.AddPart(xpPart);

            // Offer expiration (7 days)
            int offerExpiryTicks = GenDate.TicksPerDay * OFFER_EXPIRY_DAYS;
            quest.acceptanceExpireTick = Find.TickManager.TicksGame + offerExpiryTicks;
            string offerExpiredSignal = quest.AddedSignal + ".OfferExpired";
            quest.AddPart(new QuestPart_Delay
            {
                delayTicks = offerExpiryTicks,
                outSignalsCompleted = new List<string> { offerExpiredSignal },
                expiryInfoPart = "Isekai_Quest_OfferExpiry".Translate(),
                expiryInfoPartTip = "Isekai_Quest_OfferExpiry_Tip".Translate()
            });
            quest.AddPart(new QuestPart_OfferExpiry
            {
                inSignal = offerExpiredSignal,
                offerOnly = true
            });

            // Completion deadline (21 days after acceptance)
            string completionExpiredSignal = quest.AddedSignal + ".Expired";
            quest.AddPart(new QuestPart_Delay
            {
                delayTicks = GenDate.TicksPerDay * COMPLETION_EXPIRY_DAYS,
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

            Find.QuestManager.Add(quest);

            // Dramatic notification
            Find.LetterStack.ReceiveLetter(
                "Isekai_WorldBoss_LetterLabel".Translate(),
                "Isekai_WorldBoss_LetterText".Translate(bossKind.LabelCap, distance.ToString("F0")),
                LetterDefOf.ThreatBig,
                GlobalTargetInfo.Invalid,
                relatedFaction: null,
                quest: quest
            );

            Log.Message($"[Isekai] World Boss quest created: {bossKind.LabelCap} at tile {targetTile} ({distance:F0} tiles away), {factionGroupCount} faction group(s)");
        }

        /// <summary>
        /// Generates world boss loot — randomized high-value pool with archotech chance.
        /// Not everything drops every time, but overall value stays massive.
        /// </summary>
        private static List<Thing> GenerateWorldBossLoot(float combatPower)
        {
            List<Thing> rewards = new List<Thing>();
            TechLevel maxTech = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Spacer;

            // === LEGENDARY WEAPONS (1-3, guaranteed at least 1) ===
            int weaponCount = Rand.RangeInclusive(1, 3);
            for (int i = 0; i < weaponCount; i++)
            {
                Thing weapon = GenerateLegendaryWeapon();
                if (weapon != null)
                    rewards.Add(weapon);
            }

            // === LEGENDARY ARMOR (0-2, 80% chance for each) ===
            int armorSlots = Rand.RangeInclusive(0, 2);
            for (int i = 0; i < armorSlots; i++)
            {
                if (Rand.Chance(0.8f))
                {
                    Thing armor = GenerateLegendaryArmor();
                    if (armor != null)
                        rewards.Add(armor);
                }
            }

            // === ARCHOTECH BODY PARTS (rare, 25% chance for 1-2 parts, spacer+ only) ===
            if ((int)maxTech >= (int)TechLevel.Spacer && Rand.Chance(0.25f))
            {
                int archCount = Rand.RangeInclusive(1, 2);
                for (int i = 0; i < archCount; i++)
                {
                    Thing archPart = GenerateArchotechPart();
                    if (archPart != null)
                        rewards.Add(archPart);
                }
            }

            // === AI PERSONA CORE (70% chance, spacer+ only) ===
            if ((int)maxTech >= (int)TechLevel.Spacer && Rand.Chance(0.70f))
            {
                ThingDef aiCoreDef = DefDatabase<ThingDef>.GetNamedSilentFail("AIPersonaCore");
                if (aiCoreDef != null)
                {
                    Thing aiCore = ThingMaker.MakeThing(aiCoreDef);
                    aiCore.stackCount = 1;
                    rewards.Add(aiCore);
                }
            }

            // === ADVANCED COMPONENTS (spacer+) or INDUSTRIAL COMPONENTS (industrial+) or EXTRA GOLD (pre-industrial) ===
            if ((int)maxTech >= (int)TechLevel.Spacer)
            {
                Thing advComp = ThingMaker.MakeThing(ThingDefOf.ComponentSpacer);
                advComp.stackCount = Rand.RangeInclusive(8, 20);
                rewards.Add(advComp);
            }
            else if ((int)maxTech >= (int)TechLevel.Industrial)
            {
                Thing indComp = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
                indComp.stackCount = Rand.RangeInclusive(15, 40);
                rewards.Add(indComp);
            }
            else
            {
                Thing bonusGold = ThingMaker.MakeThing(ThingDefOf.Gold);
                bonusGold.stackCount = Rand.RangeInclusive(50, 120);
                rewards.Add(bonusGold);
            }

            // === PLASTEEL (spacer+) or STEEL (pre-spacer) ===
            if ((int)maxTech >= (int)TechLevel.Spacer)
            {
                Thing plasteel = ThingMaker.MakeThing(ThingDefOf.Plasteel);
                plasteel.stackCount = Rand.RangeInclusive(150, 500);
                rewards.Add(plasteel);
            }
            else
            {
                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                steel.stackCount = Rand.RangeInclusive(300, 800);
                rewards.Add(steel);
            }

            // === GOLD (80% chance, 80-250) ===
            if (Rand.Chance(0.80f))
            {
                Thing gold = ThingMaker.MakeThing(ThingDefOf.Gold);
                gold.stackCount = Rand.RangeInclusive(80, 250);
                rewards.Add(gold);
            }

            // === ULTRATECH MEDICINE (75% chance, ultra+ only) or HERBAL MEDICINE (pre-industrial) ===
            if (Rand.Chance(0.75f))
            {
                if ((int)maxTech >= (int)TechLevel.Ultra)
                {
                    Thing meds = ThingMaker.MakeThing(ThingDefOf.MedicineUltratech);
                    meds.stackCount = Rand.RangeInclusive(10, 30);
                    rewards.Add(meds);
                }
                else if ((int)maxTech >= (int)TechLevel.Industrial)
                {
                    Thing meds = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                    meds.stackCount = Rand.RangeInclusive(15, 40);
                    rewards.Add(meds);
                }
                else
                {
                    ThingDef herbalDef = DefDatabase<ThingDef>.GetNamedSilentFail("MedicineHerbal");
                    if (herbalDef != null)
                    {
                        Thing meds = ThingMaker.MakeThing(herbalDef);
                        meds.stackCount = Rand.RangeInclusive(30, 60);
                        rewards.Add(meds);
                    }
                }
            }

            // === HUGE MANA CORES (always, 2-6) ===
            ThingDef hugeManaCoreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_HugeManaCore");
            if (hugeManaCoreDef != null)
            {
                Thing manaCores = ThingMaker.MakeThing(hugeManaCoreDef);
                manaCores.stackCount = Rand.RangeInclusive(2, 6);
                rewards.Add(manaCores);
            }

            // === STAR FRAGMENTS (always, 1-3) ===
            if (IsekaiLevelingSettings.enableStarFragmentDrops)
            {
                ThingDef starFragDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_StarFragment");
                if (starFragDef != null)
                {
                    Thing starFrags = ThingMaker.MakeThing(starFragDef);
                    starFrags.stackCount = Rand.RangeInclusive(1, 3);
                    rewards.Add(starFrags);
                }
            }

            // === REINFORCEMENT CORES (guaranteed, 3-5) ===
            if (IsekaiLevelingSettings.EnableForgeSystem)
            {
                ThingDef reinforcementCoreDef = DefDatabase<ThingDef>.GetNamedSilentFail("Isekai_ReinforcementCore");
                if (reinforcementCoreDef != null)
                {
                    Thing cores = ThingMaker.MakeThing(reinforcementCoreDef);
                    cores.stackCount = Rand.RangeInclusive(3, 5);
                    rewards.Add(cores);
                }
            }

            // === LUCIFERIUM (40% chance, 3-12, spacer+ only) ===
            if ((int)maxTech >= (int)TechLevel.Spacer && Rand.Chance(0.40f))
            {
                ThingDef luciDef = DefDatabase<ThingDef>.GetNamedSilentFail("Luciferium");
                if (luciDef != null)
                {
                    Thing luci = ThingMaker.MakeThing(luciDef);
                    luci.stackCount = Rand.RangeInclusive(3, 12);
                    rewards.Add(luci);
                }
            }

            // === URANIUM (50% chance, 100-300, industrial+ only) ===
            if ((int)maxTech >= (int)TechLevel.Industrial && Rand.Chance(0.50f))
            {
                Thing uranium = ThingMaker.MakeThing(ThingDefOf.Uranium);
                uranium.stackCount = Rand.RangeInclusive(100, 300);
                rewards.Add(uranium);
            }

            // === HYPERWEAVE (30% chance, 50-150, spacer+ only) ===
            if ((int)maxTech >= (int)TechLevel.Spacer && Rand.Chance(0.30f))
            {
                ThingDef hyperweaveDef = DefDatabase<ThingDef>.GetNamedSilentFail("Hyperweave");
                if (hyperweaveDef != null)
                {
                    Thing hyperweave = ThingMaker.MakeThing(hyperweaveDef);
                    hyperweave.stackCount = Rand.RangeInclusive(50, 150);
                    rewards.Add(hyperweave);
                }
            }

            // === TRAIT ARTIFACT (guaranteed 1 random) ===
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

            return rewards;
        }

        /// <summary>
        /// Generate a random archotech body part (arm, leg, eye)
        /// </summary>
        private static Thing GenerateArchotechPart()
        {
            var archotechDefNames = new string[]
            {
                "ArchotechArm",
                "ArchotechLeg",
                "ArchotechEye",
            };

            // Shuffle and try each until one is found
            var shuffled = archotechDefNames.InRandomOrder().ToList();
            foreach (string defName in shuffled)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def != null)
                {
                    Thing part = ThingMaker.MakeThing(def);
                    part.stackCount = 1;
                    return part;
                }
            }
            return null;
        }

        private static Thing GenerateLegendaryWeapon()
        {
            // Respect player faction tech level (compatible with World Tech Level mod)
            TechLevel maxTech = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Spacer;
            
            var weaponPool = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.IsWeapon && d.techLevel <= maxTech &&
                            d.tradeability != Tradeability.None && !d.destroyOnDrop)
                .ToList();

            if (!weaponPool.Any()) return null;

            ThingDef weaponDef = weaponPool.RandomElement();
            ThingDef stuff = null;
            if (weaponDef.MadeFromStuff)
            {
                var stuffOptions = GenStuff.AllowedStuffsFor(weaponDef).ToList();
                if ((int)maxTech >= (int)TechLevel.Spacer && stuffOptions.Any(s => s == ThingDefOf.Plasteel))
                    stuff = ThingDefOf.Plasteel;
                else if (stuffOptions.Any())
                    stuff = stuffOptions.RandomElement();
            }

            Thing weapon = ThingMaker.MakeThing(weaponDef, stuff);
            if (weapon.TryGetComp<CompQuality>() != null)
            {
                // Masterwork or Legendary quality
                QualityCategory quality = Rand.Chance(0.4f) ? QualityCategory.Legendary : QualityCategory.Masterwork;
                weapon.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Outsider);
            }

            // World boss weapons: +5 to +10 refinement, 90% rune chance per slot, up to rank V
            ForgeItemGenerator.TryApplyRandomEnhancement(weapon, 10, 0.90f, 5, 5);

            return weapon;
        }

        private static Thing GenerateLegendaryArmor()
        {
            // Respect player faction tech level (compatible with World Tech Level mod)
            TechLevel maxTech = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Spacer;
            
            var armorPool = DefDatabase<ThingDef>.AllDefs
                .Where(d => d.IsApparel && d.techLevel <= maxTech &&
                            (d.apparel?.bodyPartGroups?.Any(g => g.defName == "Torso") == true) &&
                            d.tradeability != Tradeability.None)
                .ToList();

            if (!armorPool.Any()) return null;

            ThingDef armorDef = armorPool.RandomElement();
            ThingDef stuff = null;
            if (armorDef.MadeFromStuff)
            {
                var stuffOptions = GenStuff.AllowedStuffsFor(armorDef).ToList();
                if (stuffOptions.Any())
                    stuff = stuffOptions.RandomElement();
            }

            Thing armor = ThingMaker.MakeThing(armorDef, stuff);
            if (armor.TryGetComp<CompQuality>() != null)
            {
                QualityCategory quality = Rand.Chance(0.3f) ? QualityCategory.Legendary : QualityCategory.Masterwork;
                armor.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Outsider);
            }

            // World boss armor: +5 to +10 refinement, 90% rune chance per slot, up to rank V
            ForgeItemGenerator.TryApplyRandomEnhancement(armor, 10, 0.90f, 5, 5);

            return armor;
        }
    }
}
