using System;
using System.Collections.Generic;
using IsekaiLeveling.SkillTree;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiLeveling.UI
{
    /// <summary>
    /// Isekai Status tab with custom themed UI matching the design mockup
    /// Uses custom textures for background, pawn display frame, buttons, and stat panel
    /// 
    /// Asset dimensions:
    /// - Background: 584x1167
    /// - Pawn Display: 256x442
    /// - Buttons: 178x171
    /// - Stat Tab: 543x271
    /// </summary>
    [StaticConstructorOnStartup]
    public class ITab_IsekaiStats : ITab
    {
        // Animation state
        private float openTime = -1f;
        private const float ANIMATION_DURATION = 0.25f;
        
        // Hover state tracking
        private Dictionary<string, float> hoverTimers = new Dictionary<string, float>();
        
        // Track which stat button was pressed and how many points
        private IsekaiStatType? pendingStatIncrease = null;
        private int pendingStatAmount = 1;
        
        // Cached textures for XP bar — lazy-init to stay on main thread
        private static Texture2D xpBarFillTex;
        private static Texture2D xpBarBgTex;
        private static void EnsureBarTextures()
        {
            if (xpBarFillTex == null) xpBarFillTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.75f, 0.25f));
            if (xpBarBgTex == null) xpBarBgTex = SolidColorMaterials.NewSolidColorTexture(new Color(0.2f, 0.2f, 0.2f));
        }
        
        // Colors matching the design
        private static readonly Color TextPrimary = new Color(0.92f, 0.88f, 0.82f);      // Warm cream text
        private static readonly Color TextSecondary = new Color(0.65f, 0.60f, 0.55f);    // Muted brown
        private static readonly Color TextMuted = new Color(0.45f, 0.42f, 0.40f);        // Dark muted
        private static readonly Color AccentGold = new Color(0.85f, 0.70f, 0.45f);       // Golden accent
        private static readonly Color AccentCopper = new Color(0.75f, 0.55f, 0.40f);     // Copper accent
        
        // Stat colors
        private static readonly Color StatSTR = new Color(0.95f, 0.45f, 0.4f);
        private static readonly Color StatVIT = new Color(0.95f, 0.65f, 0.35f);
        private static readonly Color StatDEX = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color StatINT = new Color(0.45f, 0.65f, 0.98f);
        private static readonly Color StatWIS = new Color(0.75f, 0.55f, 0.95f);
        private static readonly Color StatCHA = new Color(0.98f, 0.88f, 0.35f);
        
        // Layout constants (scaled from asset dimensions)
        // Original: 584x1167, we scale to fit ~350 width
        private const float SCALE = 0.6f;
        private const float TAB_WIDTH = 350f;   // 584 * 0.6
        private const float TAB_HEIGHT_MAX = 795f;  // ideal height on large screens
        
        // Pawn display: 256x442 scaled — base values, scaled down dynamically
        private const float PAWN_FRAME_WIDTH = 154f;   // 256 * 0.6
        private const float PAWN_FRAME_HEIGHT_BASE = 265f;  // 442 * 0.6
        
        // Buttons: 178x171 scaled — base values
        private const float BTN_WIDTH_BASE = 107f;   // 178 * 0.6
        private const float BTN_HEIGHT_BASE = 103f;  // 171 * 0.6
        
        // Stat tab: 543x271 scaled
        private const float STAT_TAB_WIDTH = 326f;   // 543 * 0.6
        private const float STAT_TAB_HEIGHT_BASE = 255f;  // extended: 163 + ~92 for gimmick section

        // Dynamic layout values — computed each frame
        private float tabHeight;
        private float pawnFrameHeight;
        private float btnWidth;
        private float btnHeight;
        private float statTabHeight;

        public override bool IsVisible
        {
            get
            {
                if (SelPawn == null) return false;
                if (!SelPawn.RaceProps.Humanlike) return false;
                if (SelPawn.Faction != Faction.OfPlayer) return false;
                return true;
            }
        }

        public ITab_IsekaiStats()
        {
            size = new Vector2(TAB_WIDTH, TAB_HEIGHT_MAX);
            labelKey = "TabStatus";
        }

        /// <summary>Compute effective tab height and element sizes based on available screen space.</summary>
        private void UpdateDynamicLayout()
        {
            // RimWorld bottom bar + inspect pane header takes ~175px
            float maxAvailable = Verse.UI.screenHeight - 175f;
            tabHeight = Mathf.Min(TAB_HEIGHT_MAX, maxAvailable);
            
            // Compute scale factor: 1.0 at full height, down to ~0.75 minimum
            float s = Mathf.Clamp(tabHeight / TAB_HEIGHT_MAX, 0.75f, 1f);
            
            pawnFrameHeight = PAWN_FRAME_HEIGHT_BASE * s;
            btnWidth  = BTN_WIDTH_BASE * s;
            btnHeight = BTN_HEIGHT_BASE * s;
            statTabHeight = STAT_TAB_HEIGHT_BASE * s;
            
            size = new Vector2(TAB_WIDTH, tabHeight);
        }

        protected override void FillTab()
        {
            if (!IsekaiLevelingSettings.UseIsekaiUI)
            {
                FillTabVanilla();
                return;
            }
            
            // Update layout for current screen size
            UpdateDynamicLayout();

            // Initialize open time for animation
            if (openTime < 0f)
                openTime = Time.realtimeSinceStartup;
            
            Pawn pawn = SelPawn;
            if (pawn == null) return;

            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null) return;

            Rect fullRect = new Rect(0f, 0f, size.x, size.y);
            
            // Calculate animation progress
            float elapsed = Time.realtimeSinceStartup - openTime;
            float animProgress = Mathf.Clamp01(elapsed / ANIMATION_DURATION);
            float easedProgress = 1f - Mathf.Pow(1f - animProgress, 3f);
            
            // Draw main background texture (covers default tab background)
            DrawBackground(fullRect, easedProgress);
            
            // Layout from bottom up:
            // Calculate positions starting from the bottom of the tab
            float bottomPadding = 12f;
            float spacing = 8f;
            
            // Stat panel at the bottom
            float statPanelY = fullRect.height - statTabHeight - bottomPadding;
            DrawStatPanel(fullRect, pawn, comp, statPanelY, easedProgress);
            
            // Navigation buttons above stat panel
            float buttonsY = statPanelY - btnHeight - spacing;
            DrawNavigationButtons(fullRect, pawn, buttonsY, easedProgress);
            
            // Pawn info (name + level) above buttons
            float pawnInfoHeight = 48f; // name + level text
            float pawnInfoY = buttonsY - pawnInfoHeight - spacing;
            DrawPawnInfo(fullRect, pawn, comp, pawnInfoY, easedProgress);
            
            // Pawn portrait above pawn info
            float pawnPortraitY = pawnInfoY - pawnFrameHeight - 2f;
            DrawPawnPortrait(fullRect, pawn, comp, pawnPortraitY, easedProgress);
            
            // Handle pending stat changes
            ProcessPendingStatChanges(comp, pawn);
            
            GUI.color = Color.white;
        }

        /// <summary>
        /// Vanilla-style FillTab using standard RimWorld UI patterns.
        /// No custom textures, gradients, or animations — just Widgets and Listing_Standard.
        /// </summary>
        private void FillTabVanilla()
        {
            size = new Vector2(TAB_WIDTH, Mathf.Min(500f, Verse.UI.screenHeight - 175f));
            
            Pawn pawn = SelPawn;
            if (pawn == null) return;

            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null) return;

            Rect fullRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            
            // Portrait at top with dark background
            Rect portraitRect = new Rect(fullRect.x + (fullRect.width - 100f) / 2f, fullRect.y, 100f, 140f);
            Rect portraitBgRect = new Rect(portraitRect.x - 6f, portraitRect.y - 4f, portraitRect.width + 12f, portraitRect.height + 8f);
            GUI.color = new Color(0.067f, 0.078f, 0.090f, 1f);
            GUI.DrawTexture(portraitBgRect, BaseContent.WhiteTex);
            Widgets.DrawBox(portraitBgRect);
            GUI.color = Color.white;
            try
            {
                RenderTexture portrait = PortraitsCache.Get(pawn, new Vector2(200f, 280f), Rot4.South, new Vector3(0f, 0f, 0.1f), 1.15f, true, true, true, true, null, null, false);
                if (portrait != null) GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                else Widgets.ThingIcon(portraitRect, pawn);
            }
            catch { Widgets.ThingIcon(portraitRect, pawn); }
            
            float curY = portraitRect.yMax + 6f;
            
            // Name and rank
            string rank = GetLevelRank(comp.Level);
            string rankTranslated = GetRankTranslated(rank);
            string title = GetRankTitle(rank);
            
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = TextPrimary;
            Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 24f), pawn.LabelShortCap);
            curY += 24f;
            
            Text.Font = GameFont.Small;
            GUI.color = GetRankColor(rank);
            Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 20f), $"{title} — {"Isekai_LevelRankDisplay".Translate(comp.Level, rankTranslated)}");
            GUI.color = Color.white;
            curY += 24f;
            Text.Anchor = TextAnchor.UpperLeft;
            
            // XP bar
            Rect xpLabelRect = new Rect(fullRect.x, curY, fullRect.width, 18f);
            float xpFill = Mathf.Clamp01((float)comp.currentXP / Mathf.Max(1, comp.XPToNextLevel));
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Widgets.Label(xpLabelRect, $"XP: {NumberFormatting.FormatNum(comp.currentXP)} / {NumberFormatting.FormatNum(comp.XPToNextLevel)}");
            GUI.color = Color.white;
            curY += 16f;
            Rect xpBarRect = new Rect(fullRect.x, curY, fullRect.width, 12f);
            EnsureBarTextures();
            Widgets.FillableBar(xpBarRect, xpFill, xpBarFillTex, xpBarBgTex, false);
            curY += 18f;
            
            // Available points
            if (comp.stats.availableStatPoints > 0)
            {
                GUI.color = AccentGold;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), "Isekai_StatPointsAvailable".Translate(comp.stats.availableStatPoints));
                GUI.color = Color.white;
                curY += 16f;
            }
            
            // Auto-distribute toggle (always visible so player can set it before leveling)
            {
                Text.Font = GameFont.Tiny;
                Rect toggleRect = new Rect(fullRect.x, curY, fullRect.width, 22f);
                bool prev = comp.autoDistributeStats;
                Widgets.CheckboxLabeled(toggleRect, "Isekai_AutoDistribute".Translate(), ref comp.autoDistributeStats);
                if (comp.autoDistributeStats != prev && comp.autoDistributeStats && comp.stats.availableStatPoints > 0)
                {
                    comp.AutoDistributeByClass();
                }
                TooltipHandler.TipRegion(toggleRect, "Isekai_AutoDistribute_Desc".Translate());
                curY += 20f;
            }
            
            curY += 4f;
            // Accent separator
            GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, 0.25f);
            GUI.DrawTexture(new Rect(fullRect.x, curY, fullRect.width, 1f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            curY += 6f;
            
            // Stats in 2 columns
            float colWidth = (fullRect.width - 10f) / 2f;
            var statEntries = new (string label, int value, Color color, IsekaiStatType type)[]
            {
                ("Isekai_Stat_STR".Translate(), comp.stats.strength, StatSTR, IsekaiStatType.Strength),
                ("Isekai_Stat_VIT".Translate(), comp.stats.vitality, StatVIT, IsekaiStatType.Vitality),
                ("Isekai_Stat_DEX".Translate(), comp.stats.dexterity, StatDEX, IsekaiStatType.Dexterity),
                ("Isekai_Stat_INT".Translate(), comp.stats.intelligence, StatINT, IsekaiStatType.Intelligence),
                ("Isekai_Stat_WIS".Translate(), comp.stats.wisdom, StatWIS, IsekaiStatType.Wisdom),
                ("Isekai_Stat_CHA".Translate(), comp.stats.charisma, StatCHA, IsekaiStatType.Charisma),
            };
            
            int maxStat = IsekaiStatAllocation.GetEffectiveMaxStat();
            for (int i = 0; i < statEntries.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = fullRect.x + col * (colWidth + 10f);
                float y = curY + row * 24f;
                
                Rect rowRect = new Rect(x, y, colWidth, 22f);
                
                // Color bar
                GUI.color = statEntries[i].color;
                GUI.DrawTexture(new Rect(x, y + 3f, 3f, 16f), BaseContent.WhiteTex);
                GUI.color = Color.white;
                
                // Label + value
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x + 6f, y, 35f, 22f), statEntries[i].label);
                GUI.color = TextPrimary;
                Text.Anchor = TextAnchor.MiddleRight;
                float valRight = comp.stats.availableStatPoints > 0 && statEntries[i].value < maxStat ? colWidth - 22f : colWidth;
                Widgets.Label(new Rect(x + 6f, y, valRight - 6f, 22f), statEntries[i].value.ToString());
                GUI.color = Color.white;
                
                // Plus button
                if (comp.stats.availableStatPoints > 0 && statEntries[i].value < maxStat)
                {
                    Rect plusRect = new Rect(x + colWidth - 20f, y + 1f, 20f, 20f);
                    if (Widgets.ButtonText(plusRect, "+", false, false, true))
                    {
                        pendingStatIncrease = statEntries[i].type;
                        pendingStatAmount = IsekaiStatAllocation.GetBulkAmount();
                    }
                }
                
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            curY += 3 * 24f + 6f;
            
            // Gimmick section
            GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, 0.25f);
            GUI.DrawTexture(new Rect(fullRect.x, curY, fullRect.width, 1f), BaseContent.WhiteTex);
            GUI.color = Color.white;
            curY += 6f;
            
            var activeGimmicks = comp.passiveTree?.GetActiveGimmicks();
            bool hasAnyGimmick = activeGimmicks != null && activeGimmicks.Count > 0;
            Text.Font = GameFont.Tiny;
            GUI.color = TextSecondary;
            Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), "CLASS PASSIVE");
            GUI.color = Color.white;
            curY += 16f;
            
            if (!hasAnyGimmick)
            {
                DrawVanillaGimmick(fullRect, pawn, comp, ClassGimmickType.None, ref curY);
            }
            else
            {
                foreach (var gimmick in activeGimmicks)
                {
                    DrawVanillaGimmick(fullRect, pawn, comp, gimmick, ref curY);
                }
            }
            
            curY += 8f;

            // Weapon Mastery section
            if (IsekaiLevelingSettings.EnableWeaponMastery && comp.weaponMastery != null && pawn.equipment?.Primary != null)
            {
                string wpnDefName = pawn.equipment.Primary.def.defName;
                int xp = comp.weaponMastery.GetXP(wpnDefName);
                var tier = comp.weaponMastery.GetMasteryTier(wpnDefName);

                if (xp > 0 || tier != Forge.MasteryTier.Novice)
                {
                    GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, 0.25f);
                    GUI.DrawTexture(new Rect(fullRect.x, curY, fullRect.width, 1f), BaseContent.WhiteTex);
                    GUI.color = Color.white;
                    curY += 6f;

                    Text.Font = GameFont.Tiny;
                    GUI.color = TextSecondary;
                    Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), "WEAPON MASTERY");
                    GUI.color = Color.white;
                    curY += 16f;

                    Text.Font = GameFont.Small;
                    string wpnLabel = pawn.equipment.Primary.def.label ?? wpnDefName;
                    GUI.color = AccentGold;
                    Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 20f), $"{wpnLabel}: {tier} ({xp} XP)");
                    GUI.color = Color.white;
                    curY += 22f;
                }
            }

            curY += 8f;
            
            // Navigation buttons
            float btnWidth = (fullRect.width - 10f) / 3f;
            if (Widgets.ButtonText(new Rect(fullRect.x, curY, btnWidth, 28f), "Isekai_Mastery".Translate()))
            {
                Find.WindowStack.Add(new Window_Mastery(pawn));
            }
            if (Widgets.ButtonText(new Rect(fullRect.x + btnWidth + 5f, curY, btnWidth, 28f), "Isekai_Stats".Translate()))
            {
                Find.WindowStack.Add(new Window_StatsAttribution(pawn));
            }
            if (Widgets.ButtonText(new Rect(fullRect.x + (btnWidth + 5f) * 2f, curY, btnWidth, 28f), "Isekai_SkillTree".Translate()))
            {
                Find.WindowStack.Add(new Window_SkillTree(pawn));
            }
            
            // Dev buttons
            if (Prefs.DevMode && DebugSettings.godMode)
            {
                curY += 32f;
                if (Widgets.ButtonText(new Rect(fullRect.x, curY, 60f, 22f), "+10 SP"))
                    comp.stats.availableStatPoints += 10;
                if (Widgets.ButtonText(new Rect(fullRect.x + 65f, curY, 55f, 22f), "+1 Lv"))
                    comp.DevAddLevel(1);
            }
            
            ProcessPendingStatChanges(comp, pawn);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawVanillaGimmick(Rect fullRect, Pawn pawn, IsekaiComponent comp, ClassGimmickType gimmick, ref float curY)
        {
            Text.Font = GameFont.Tiny;
            
            if (gimmick == ClassGimmickType.None)
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), "No class assigned");
                curY += 16f;
                Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), "Reach D rank (Lv11) to unlock");
                curY += 16f;
                GUI.color = Color.white;
                return;
            }
            
            int gimmickTier = comp.passiveTree?.GetGimmickTierFor(gimmick) ?? 0;
            if (gimmickTier <= 0)
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), $"{gimmick}  [Not unlocked]");
                curY += 16f;
                GUI.color = Color.white;
                return;
            }
            
            // Show gimmick name and current value
            string gimmickName = gimmick.ToString();
            float bonus = 0f;
            string bonusDesc = "";
            
            switch (gimmick)
            {
                case ClassGimmickType.WrathOfTheFallen:
                    bonus = PassiveTreeTracker.CalcWrathOfTheFallen(pawn, gimmickTier);
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Melee Damage" : "Inactive";
                    break;
                case ClassGimmickType.ArcaneOverflow:
                    bonus = PassiveTreeTracker.CalcArcaneOverflow(pawn, gimmickTier);
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Psychic Sensitivity" : "Inactive";
                    break;
                case ClassGimmickType.DivineRetribution:
                    bonus = comp.passiveTree.CalcDivineRetribution();
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} next strike" : $"{comp.passiveTree.retributionStoredDamage} dmg stored";
                    break;
                case ClassGimmickType.InnerCalm:
                    bonus = comp.passiveTree.CalcInnerCalm();
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Tend/Surgery" : "Building calm...";
                    break;
                case ClassGimmickType.PredatorFocus:
                    bonus = comp.passiveTree.CalcPredatorFocus();
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Shooting Accuracy" : $"{comp.passiveTree.huntMarkStacks} stacks";
                    break;
                case ClassGimmickType.CounterStrike:
                    bonus = comp.passiveTree.CalcCounterStrike();
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Melee Damage" : $"{comp.passiveTree.counterStrikeCharges} charges";
                    break;
                case ClassGimmickType.MasterworkInsight:
                    bonus = PassiveTreeTracker.CalcMasterworkInsight(pawn, gimmickTier);
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Work Speed" : "Inactive";
                    break;
                case ClassGimmickType.RallyingPresence:
                    bonus = PassiveTreeTracker.CalcRallyingPresence(pawn, gimmickTier);
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Social Impact" : "No colonists";
                    break;
                case ClassGimmickType.UnyieldingSpirit:
                    bonus = PassiveTreeTracker.CalcUnyieldingSpirit(pawn, gimmickTier);
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Immunity/Rest" : "Mood too high";
                    break;
                case ClassGimmickType.BloodFrenzy:
                    bonus = comp.passiveTree.CalcBloodFrenzy();
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Melee/Speed" : $"{comp.passiveTree.frenzyStacks} stacks";
                    break;
                case ClassGimmickType.EurekaSynthesis:
                    bonus = comp.passiveTree.CalcEurekaSynthesis();
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} TendQ" : $"{comp.passiveTree.eurekaInsightStacks} stacks";
                    break;
                case ClassGimmickType.PackAlpha:
                    bonus = PassiveTreeTracker.CalcPackAlpha(pawn, gimmickTier);
                    bonusDesc = bonus > 0f ? $"+{bonus:P0} Taming/Training" : "No bonded animals";
                    break;
            }
            
            GUI.color = bonus > 0f ? new Color(0.4f, 0.85f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(fullRect.x, curY, fullRect.width, 18f), $"{gimmickName}  [Tier {gimmickTier}/4] — {bonusDesc}");
            curY += 16f;
            GUI.color = Color.white;
        }

        protected override void CloseTab()
        {
            base.CloseTab();
            openTime = -1f;
        }

        private void DrawBackground(Rect rect, float alpha)
        {
            // Expand the background rect to cover the default RimWorld tab background/chrome
            // The default tab has margins that we need to paint over
            float expandAmount = 20f;
            Rect expandedRect = new Rect(
                rect.x - expandAmount,
                rect.y - expandAmount,
                rect.width + expandAmount * 2f,
                rect.height + expandAmount * 2f
            );
            
            // First, draw a solid color to completely cover any default tab background
            GUI.color = new Color(0.08f, 0.07f, 0.07f, 1f);
            GUI.DrawTexture(expandedRect, BaseContent.WhiteTex);
            
            // Then draw our custom background texture on top (use original rect for proper sizing)
            GUI.color = new Color(1f, 1f, 1f, alpha);
            
            if (IsekaiTextures.BackgroundTab != null)
            {
                GUI.DrawTexture(rect, IsekaiTextures.BackgroundTab, ScaleMode.StretchToFill);
            }
            else
            {
                // Fallback dark background
                GUI.color = new Color(0.12f, 0.10f, 0.10f, 0.98f * alpha);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
            }
        }

        private void DrawPawnPortrait(Rect fullRect, Pawn pawn, IsekaiComponent comp, float startY, float alpha)
        {
            float frameX = (fullRect.width - PAWN_FRAME_WIDTH) / 2f;
            float frameY = startY;
            
            // Draw rank title above the portrait
            string rank = GetLevelRank(comp.Level);
            string title = GetRankTitle(rank);
            Color rankColor = GetRankColor(rank);
            
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(rankColor.r, rankColor.g, rankColor.b, alpha);
            
            Rect titleRect = new Rect(0f, frameY - 28f, fullRect.width, 24f);
            Widgets.Label(titleRect, title);
            
            Text.Anchor = TextAnchor.UpperLeft;
            
            Rect frameRect = new Rect(frameX, frameY, PAWN_FRAME_WIDTH, pawnFrameHeight);
            
            // Draw the pawn display frame FIRST (as the background/border)
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (IsekaiTextures.PawnDisplay != null)
            {
                GUI.DrawTexture(frameRect, IsekaiTextures.PawnDisplay, ScaleMode.StretchToFill);
            }
            
            // Draw the pawn portrait ON TOP of the frame
            // Position the portrait to show full body within the diamond frame area
            float portraitWidth = PAWN_FRAME_WIDTH * 0.65f;
            float portraitHeight = pawnFrameHeight * 0.55f;
            float portraitX = frameX + (PAWN_FRAME_WIDTH - portraitWidth) / 2f;
            float portraitY = frameY + pawnFrameHeight * 0.18f;
            
            Rect portraitRect = new Rect(portraitX, portraitY, portraitWidth, portraitHeight);
            
            try
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                
                // Use RenderTexture for proper full-body portrait
                RenderTexture portrait = PortraitsCache.Get(
                    pawn, 
                    new Vector2(portraitWidth * 2f, portraitHeight * 2f), // Higher res for quality
                    Rot4.South,
                    new Vector3(0f, 0f, 0.1f), // Slight offset to show more body
                    1.15f, // Zoom out to show full body
                    true, true, true, true, null, null, false
                );
                
                if (portrait != null)
                {
                    GUI.DrawTexture(portraitRect, portrait, ScaleMode.ScaleToFit);
                }
                else
                {
                    // Fallback to ThingIcon
                    Widgets.ThingIcon(portraitRect, pawn);
                }
            }
            catch
            {
                // Fallback if portrait fails
                try
                {
                    GUI.color = new Color(1f, 1f, 1f, alpha);
                    Widgets.ThingIcon(portraitRect, pawn);
                }
                catch
                {
                    GUI.color = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, alpha * 0.5f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Medium;
                    Widgets.Label(portraitRect, pawn.LabelShortCap.Substring(0, 1));
                    Text.Anchor = TextAnchor.UpperLeft;
                }
            }
            
            GUI.color = Color.white;
        }

        private void DrawPawnInfo(Rect fullRect, Pawn pawn, IsekaiComponent comp, float startY, float alpha)
        {
            float curY = startY;
            
            // Pawn name - centered
            GUI.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            
            Rect nameRect = new Rect(0f, curY, fullRect.width, 24f);
            Widgets.Label(nameRect, pawn.LabelShortCap);
            curY += 22f;
            
            // Level and Rank - centered, smaller text
            string rank = GetLevelRank(comp.Level);
            string rankTranslated = GetRankTranslated(rank);
            string levelText = "Isekai_LevelRankDisplay".Translate(comp.Level, rankTranslated);
            
            GUI.color = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, alpha);
            Text.Font = GameFont.Small;
            
            Rect levelRect = new Rect(0f, curY, fullRect.width, 20f);
            Widgets.Label(levelRect, levelText);
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawNavigationButtons(Rect fullRect, Pawn pawn, float startY, float alpha)
        {
            float spacing = 8f;
            float totalWidth = btnWidth * 3 + spacing * 2;
            float startX = (fullRect.width - totalWidth) / 2f;
            
            // Button 1 - Mastery (left) - Opens Weapon Mastery Window
            Rect btn1Rect = new Rect(startX, startY, btnWidth, btnHeight);
            if (DrawNavButton(btn1Rect, "Isekai_Mastery".Translate(), false, alpha))
            {
                Find.WindowStack.Add(new Window_Mastery(pawn));
            }
            
            // Button 2 - Dashboard (center, primary style)
            Rect btn2Rect = new Rect(startX + btnWidth + spacing, startY, btnWidth, btnHeight);
            if (DrawNavButton(btn2Rect, "Isekai_Stats".Translate(), true, alpha))
            {
                // Open Stats Attribution Window (new themed version)
                Find.WindowStack.Add(new Window_StatsAttribution(pawn));
            }
            
            // Button 3 - Constellation (right) - Now active!
            Rect btn3Rect = new Rect(startX + (btnWidth + spacing) * 2, startY, btnWidth, btnHeight);
            if (DrawNavButton(btn3Rect, "Isekai_SkillTree".Translate(), false, alpha, IsekaiTextures.ConstellationButton))
            {
                Find.WindowStack.Add(new Window_SkillTree(pawn));
            }
        }
        
        private void DrawDisabledNavButton(Rect rect, string label, float alpha, string tooltip)
        {
            // Draw greyed out button with SecondaryButton2 texture
            GUI.color = new Color(0.5f, 0.5f, 0.5f, alpha * 0.7f);
            
            Texture2D btnTex = IsekaiTextures.SecondaryButton2;
            if (btnTex != null)
            {
                GUI.DrawTexture(rect, btnTex, ScaleMode.StretchToFill);
            }
            else
            {
                // Fallback
                GUI.color = new Color(0.15f, 0.14f, 0.13f, alpha * 0.7f);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
            }
            
            // Label - greyed out
            GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, alpha * 0.7f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, rect.y + rect.height * 0.5f, rect.width, rect.height * 0.4f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Tooltip on hover
            if (Mouse.IsOver(rect))
            {
                TooltipHandler.TipRegion(rect, tooltip);
            }
            
            GUI.color = Color.white;
        }

        private bool DrawNavButton(Rect rect, string label, bool isPrimary, float alpha, Texture2D texOverride = null)
        {
            string btnId = $"nav_{label}";
            bool isOver = Mouse.IsOver(rect);
            float hoverAmount = GetHoverAmount(btnId, isOver);
            float scale = GetScaleAmount(btnId, isOver, 1f, 1.08f);
            
            // Calculate scaled rect from center
            Vector2 center = rect.center;
            Rect scaledRect = new Rect(
                center.x - (rect.width * scale) / 2f,
                center.y - (rect.height * scale) / 2f,
                rect.width * scale,
                rect.height * scale
            );
            
            GUI.color = new Color(1f, 1f, 1f, alpha);
            
            // Draw button texture
            Texture2D btnTex = texOverride != null ? texOverride : (isPrimary ? IsekaiTextures.PrimaryButton : IsekaiTextures.SecondaryButton);
            
            if (btnTex != null)
            {
                // Brighten on hover
                float brightness = 1f + hoverAmount * 0.25f;
                GUI.color = new Color(brightness, brightness, brightness, alpha);
                GUI.DrawTexture(scaledRect, btnTex, ScaleMode.StretchToFill);
            }
            else
            {
                // Fallback button
                Color bgColor = isPrimary 
                    ? new Color(AccentCopper.r, AccentCopper.g, AccentCopper.b, alpha)
                    : new Color(0.2f, 0.18f, 0.17f, alpha);
                bgColor = Color.Lerp(bgColor, AccentGold, hoverAmount * 0.3f);
                GUI.color = bgColor;
                GUI.DrawTexture(scaledRect, BaseContent.WhiteTex);
            }
            
            // Label - positioned in lower portion of button
            Color textColor = Color.Lerp(TextSecondary, TextPrimary, hoverAmount);
            GUI.color = new Color(textColor.r, textColor.g, textColor.b, alpha);
            
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(scaledRect.x, scaledRect.y + scaledRect.height * 0.5f, scaledRect.width, scaledRect.height * 0.4f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            GUI.color = Color.white;
            
            return isOver && Event.current.type == EventType.MouseUp && Event.current.button == 0;
        }

        private void DrawStatPanel(Rect fullRect, Pawn pawn, IsekaiComponent comp, float curY, float alpha)
        {
            // Center the stat panel
            float panelX = (fullRect.width - STAT_TAB_WIDTH) / 2f;
            Rect panelRect = new Rect(panelX, curY, STAT_TAB_WIDTH, statTabHeight);
            
            // Draw stat panel background with custom color #3F2C2A
            GUI.color = new Color(0.247f, 0.173f, 0.165f, 0.95f * alpha);
            GUI.DrawTexture(panelRect, BaseContent.WhiteTex);
            
            // Content padding
            float padding = 10f;
            Rect contentRect = new Rect(panelRect.x + padding, panelRect.y + padding, 
                                         panelRect.width - padding * 2f, panelRect.height - padding * 2f);
            
            // Use the sexy IsekaiListing for content
            using (var listing = new IsekaiListing(contentRect, alpha))
            {
                // Points badge if available (shows how many points available, but can't allocate here)
                listing.PointsAvailable(comp.stats.availableStatPoints);
                
                // Stats in 2 columns - READ ONLY (no plus buttons, allocation only through Stats window)
                listing.BeginColumns(2, 12f);
                
                // Column 1 - Pass false for showPlus to disable allocation
                listing.StatRowCompact("Isekai_Stat_STR".Translate(), comp.stats.strength, IsekaiListing.StatSTR, false);
                listing.NextColumn();
                
                listing.StatRowCompact("Isekai_Stat_VIT".Translate(), comp.stats.vitality, IsekaiListing.StatVIT, false);
                listing.NextColumn();
                
                listing.StatRowCompact("Isekai_Stat_DEX".Translate(), comp.stats.dexterity, IsekaiListing.StatDEX, false);
                listing.NextColumn();
                
                listing.StatRowCompact("Isekai_Stat_INT".Translate(), comp.stats.intelligence, IsekaiListing.StatINT, false);
                listing.NextColumn();
                
                listing.StatRowCompact("Isekai_Stat_WIS".Translate(), comp.stats.wisdom, IsekaiListing.StatWIS, false);
                listing.NextColumn();
                
                listing.StatRowCompact("Isekai_Stat_CHA".Translate(), comp.stats.charisma, IsekaiListing.StatCHA, false);
                
                listing.EndColumns();
                
                listing.Gap(6f);
                
                // XP Bar
                listing.XPBar(comp.currentXP, comp.XPToNextLevel);

                // ── Class Passive (Gimmick) section ──────────────────────────
                listing.GapLine();
                listing.Label("CLASS PASSIVE", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));

                var activeGimmicks = comp.passiveTree?.GetActiveGimmicks();
                bool hasAnyGimmick = activeGimmicks != null && activeGimmicks.Count > 0;
                if (!hasAnyGimmick)
                {
                    listing.Label("No class assigned", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    listing.Label("Reach D rank (Lv11) to unlock", GameFont.Tiny, new Color(0.35f, 0.33f, 0.31f));
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.WrathOfTheFallen) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.WrathOfTheFallen);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Wrath of the Fallen  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Warlord's Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        float threshold, maxBonus;
                        switch (gimmickTier)
                        {
                            case 1: threshold = 0.50f; maxBonus = 0.25f; break;
                            case 2: threshold = 0.50f; maxBonus = 0.35f; break;
                            case 3: threshold = 0.50f; maxBonus = 0.50f; break;
                            default: threshold = 0.50f; maxBonus = 0.75f; break;
                        }

                        listing.Label($"Wrath of the Fallen  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.85f, 0.70f, 0.45f));

                        float wrathBonus = PassiveTreeTracker.CalcWrathOfTheFallen(pawn, gimmickTier);
                        float hpPct = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;

                        if (wrathBonus > 0f)
                        {
                            listing.Label($"ACTIVE: HP {hpPct:P0}  \u2192  +{wrathBonus:P0} Melee Damage", GameFont.Tiny, new Color(0.95f, 0.45f, 0.4f));
                        }
                        else
                        {
                            listing.Label($"Inactive — HP {hpPct:P0}  (triggers below {threshold:P0})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At threshold → up to +{maxBonus:P0} Melee Damage", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.ArcaneOverflow) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.ArcaneOverflow);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Arcane Overflow  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Archmage's Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        float threshold, maxBonus;
                        switch (gimmickTier)
                        {
                            case 1: threshold = 0.50f; maxBonus = 0.20f; break;
                            case 2: threshold = 0.40f; maxBonus = 0.35f; break;
                            case 3: threshold = 0.30f; maxBonus = 0.55f; break;
                            default: threshold = 0.20f; maxBonus = 0.80f; break;
                        }

                        listing.Label($"Arcane Overflow  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.5f, 0.75f, 1.0f));

                        float psyfocus    = pawn.psychicEntropy?.CurrentPsyfocus ?? 0f;
                        float arcaneBonus = PassiveTreeTracker.CalcArcaneOverflow(pawn, gimmickTier);

                        if (arcaneBonus > 0f)
                        {
                            listing.Label($"ACTIVE: Psyfocus {psyfocus:P0}  \u2192  +{arcaneBonus:P0} Psychic Sensitivity", GameFont.Tiny, new Color(0.4f, 0.75f, 1.0f));
                        }
                        else
                        {
                            listing.Label($"Inactive \u2014 Psyfocus {psyfocus:P0}  (triggers above {threshold:P0})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At full focus \u2192 up to +{maxBonus:P0} Psychic Sensitivity", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.DivineRetribution) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.DivineRetribution);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Divine Retribution  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Sanctuary's Vow nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        int cap;
                        float maxBonus;
                        switch (gimmickTier)
                        {
                            case 1: cap = 50;  maxBonus = 0.15f; break;
                            case 2: cap = 75;  maxBonus = 0.30f; break;
                            case 3: cap = 100; maxBonus = 0.45f; break;
                            default: cap = 150; maxBonus = 0.60f; break;
                        }

                        listing.Label($"Divine Retribution  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(1.0f, 0.85f, 0.35f));

                        int stored             = comp.passiveTree.retributionStoredDamage;
                        float retributionBonus = comp.passiveTree.CalcDivineRetribution();

                        if (retributionBonus > 0f)
                        {
                            listing.Label($"CHARGED: {stored}/{cap} dmg absorbed  \u2192  +{retributionBonus:P0} next strike", GameFont.Tiny, new Color(1.0f, 0.75f, 0.2f));
                        }
                        else
                        {
                            listing.Label($"No charge \u2014 take damage to build retribution ({stored}/{cap})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At full charge \u2192 up to +{maxBonus:P0} melee damage", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.InnerCalm) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.InnerCalm);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Inner Calm  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Ascetic Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        string[] calmCaps    = { "", "4 hours", "3 hours", "2 hours", "1 hour" };
                        string[] calmBonuses = { "", "20%", "35%", "55%", "80%" };

                        listing.Label($"Inner Calm  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.4f, 0.9f, 0.5f));

                        float calmBonus = comp.passiveTree.CalcInnerCalm();
                        int lastHit     = comp.passiveTree.lastHitTick;
                        bool neverHit   = lastHit < 0;

                        if (calmBonus > 0f)
                        {
                            listing.Label($"CALM: +{calmBonus:P0} Tend/Surgery, +{calmBonus * 0.5f:P0} Research", GameFont.Tiny,
                                calmBonus >= 0.99f * (gimmickTier == 1 ? 0.20f : gimmickTier == 2 ? 0.35f : gimmickTier == 3 ? 0.55f : 0.80f)
                                    ? new Color(0.35f, 1.0f, 0.45f)
                                    : new Color(0.4f, 0.85f, 0.5f));
                        }
                        else
                        {
                            listing.Label($"Just hit \u2014 calm is reset, building over {calmCaps[gimmickTier]}", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At full calm \u2192 up to +{calmBonuses[gimmickTier]} Tend / Surgery", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.PredatorFocus) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PredatorFocus);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Predator Focus  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Hawkeye Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        int maxStacks;
                        float perStack;
                        switch (gimmickTier)
                        {
                            case 1: maxStacks = 3; perStack = 0.04f; break;
                            case 2: maxStacks = 4; perStack = 0.05f; break;
                            case 3: maxStacks = 5; perStack = 0.06f; break;
                            default: maxStacks = 7; perStack = 0.07f; break;
                        }

                        listing.Label($"Predator Focus  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.9f, 0.6f, 0.2f));

                        int stacks       = comp.passiveTree.huntMarkStacks;
                        float focusBonus = comp.passiveTree.CalcPredatorFocus();

                        if (focusBonus > 0f)
                        {
                            listing.Label($"LOCKED ON: {stacks}/{maxStacks} stacks  \u2192  +{focusBonus:P0} Shooting Accuracy", GameFont.Tiny, new Color(1.0f, 0.65f, 0.15f));
                        }
                        else
                        {
                            listing.Label($"No target \u2014 land ranged hits to stack focus ({stacks}/{maxStacks})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At max stacks \u2192 up to +{maxStacks * perStack:P0} Shooting Accuracy", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.CounterStrike) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.CounterStrike);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Counter Strike  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Centerpoint Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        int maxCharges;
                        float perCharge;
                        switch (gimmickTier)
                        {
                            case 1: maxCharges = 3; perCharge = 0.07f; break;
                            case 2: maxCharges = 5; perCharge = 0.08f; break;
                            case 3: maxCharges = 7; perCharge = 0.09f; break;
                            default: maxCharges = 10; perCharge = 0.10f; break;
                        }

                        listing.Label($"Counter Strike  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.5f, 0.8f, 1.0f));

                        int charges         = comp.passiveTree.counterStrikeCharges;
                        float counterBonus  = comp.passiveTree.CalcCounterStrike();

                        if (counterBonus > 0f)
                        {
                            listing.Label($"CHARGED: {charges}/{maxCharges} charges  \u2192  +{counterBonus:P0} Melee Damage", GameFont.Tiny, new Color(0.4f, 0.75f, 1.0f));
                        }
                        else
                        {
                            listing.Label($"No charges \u2014 dodge melee attacks to store counters ({charges}/{maxCharges})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At max charges \u2192 up to +{maxCharges * perCharge:P0} Melee Damage", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.MasterworkInsight) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.MasterworkInsight);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Masterwork Insight  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Mastercraft Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        listing.Label($"Masterwork Insight  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.9f, 0.7f, 0.2f));

                        Pawn gPawn = comp.parent as Pawn;
                        float insightBonus = PassiveTreeTracker.CalcMasterworkInsight(gPawn, gimmickTier);

                        if (insightBonus > 0f)
                        {
                            int craftLevel = gPawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
                            listing.Label($"Crafting {craftLevel}/20 \u2192 +{insightBonus:P0} Work Speed", GameFont.Tiny, new Color(1.0f, 0.8f, 0.2f));
                        }
                        else
                        {
                            listing.Label("Inactive \u2014 Crafting skill at 0", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            string[] maxBonuses = { "", "15%", "30%", "50%", "75%" };
                            listing.Label($"At max Crafting \u2192 up to +{maxBonuses[gimmickTier]} Work Speed", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.RallyingPresence) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.RallyingPresence);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Rallying Presence  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Rally Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        listing.Label($"Rallying Presence  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.7f, 0.5f, 1.0f));

                        Pawn gPawn = comp.parent as Pawn;
                        float rallyBonus = PassiveTreeTracker.CalcRallyingPresence(gPawn, gimmickTier);

                        if (rallyBonus > 0f)
                        {
                            int colonists = PassiveTreeTracker.GetCachedColonistCount(gPawn?.Map);
                            listing.Label($"{colonists} colonists \u2192 +{rallyBonus:P0} Social Impact", GameFont.Tiny, new Color(0.8f, 0.6f, 1.0f));
                        }
                        else
                        {
                            listing.Label("No colonists on map \u2014 recruit allies to empower", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.UnyieldingSpirit) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.UnyieldingSpirit);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Unyielding Spirit  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Unyielding Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        listing.Label($"Unyielding Spirit  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.3f, 0.8f, 0.7f));

                        Pawn gPawn = comp.parent as Pawn;
                        float spiritBonus = PassiveTreeTracker.CalcUnyieldingSpirit(gPawn, gimmickTier);

                        if (spiritBonus > 0f)
                        {
                            float mood = gPawn?.needs?.mood?.CurLevel ?? 0.5f;
                            listing.Label($"Mood {mood * 100f:F0}% \u2192 +{spiritBonus:P0} Immunity/Rest", GameFont.Tiny, new Color(0.4f, 0.9f, 0.8f));
                        }
                        else
                        {
                            listing.Label("Mood above 50% \u2014 bonus activates when mood drops", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            string[] maxBonuses = { "", "20%", "35%", "55%", "80%" };
                            listing.Label($"At lowest mood \u2192 up to +{maxBonuses[gimmickTier]} Immunity/Rest", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.BloodFrenzy) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.BloodFrenzy);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Blood Frenzy  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Frenzy Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        int maxStacks;
                        float perStack;
                        switch (gimmickTier)
                        {
                            case 1: maxStacks = 3; perStack = 0.05f; break;
                            case 2: maxStacks = 5; perStack = 0.06f; break;
                            case 3: maxStacks = 7; perStack = 0.07f; break;
                            default: maxStacks = 10; perStack = 0.08f; break;
                        }

                        listing.Label($"Blood Frenzy  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.85f, 0.25f, 0.25f));

                        int stacks = comp.passiveTree.frenzyStacks;
                        float frenzyBonus = comp.passiveTree.CalcBloodFrenzy();

                        if (frenzyBonus > 0f)
                        {
                            listing.Label($"FRENZIED: {stacks}/{maxStacks} stacks  \u2192  +{frenzyBonus:P0} Melee/Speed", GameFont.Tiny, new Color(1.0f, 0.3f, 0.2f));
                        }
                        else
                        {
                            listing.Label($"No frenzy \u2014 kill enemies to stack ({stacks}/{maxStacks})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At max stacks \u2192 up to +{maxStacks * perStack:P0} Melee/Speed", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.EurekaSynthesis) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.EurekaSynthesis);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Eureka Synthesis  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Eureka Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        int maxStacks;
                        float perStack;
                        switch (gimmickTier)
                        {
                            case 1: maxStacks = 5; perStack = 0.03f; break;
                            case 2: maxStacks = 8; perStack = 0.04f; break;
                            case 3: maxStacks = 12; perStack = 0.05f; break;
                            default: maxStacks = 16; perStack = 0.06f; break;
                        }

                        listing.Label($"Eureka Synthesis  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.55f, 0.90f, 0.55f));

                        int stacks = comp.passiveTree.eurekaInsightStacks;
                        float eurekaBonus = comp.passiveTree.CalcEurekaSynthesis();

                        if (eurekaBonus > 0f)
                        {
                            listing.Label($"EUREKA: {stacks}/{maxStacks} stacks  \u2192  +{eurekaBonus:P0} TendQ, +{eurekaBonus * 0.5f:P0} WorkSpeed", GameFont.Tiny, new Color(0.5f, 1.0f, 0.5f));
                        }
                        else
                        {
                            listing.Label($"No insight \u2014 tend patients to build ({stacks}/{maxStacks})", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                            listing.Label($"At max stacks \u2192 up to +{maxStacks * perStack:P0} TendQ", GameFont.Tiny, new Color(0.45f, 0.42f, 0.4f));
                        }
                    }
                }
                if (comp.passiveTree?.HasGimmick(ClassGimmickType.PackAlpha) == true)
                {
                    int gimmickTier = comp.passiveTree.GetGimmickTierFor(ClassGimmickType.PackAlpha);
                    if (gimmickTier <= 0)
                    {
                        listing.Label("Pack Alpha  [Not unlocked]", GameFont.Tiny, new Color(0.5f, 0.47f, 0.44f));
                        listing.Label("Allocate Pack Path nodes to unlock", GameFont.Tiny, new Color(0.4f, 0.38f, 0.36f));
                    }
                    else
                    {
                        listing.Label($"Pack Alpha  [Tier {gimmickTier}/4]", GameFont.Tiny, new Color(0.80f, 0.60f, 0.30f));

                        Pawn gPawn = comp.parent as Pawn;
                        float packBonus = PassiveTreeTracker.CalcPackAlpha(gPawn, gimmickTier);

                        if (packBonus > 0f)
                        {
                            int bondCount = 0;
                            if (gPawn?.relations != null && gPawn.Map != null)
                            {
                                foreach (var rel in gPawn.relations.DirectRelations)
                                {
                                    if (rel.def == PawnRelationDefOf.Bond && rel.otherPawn != null
                                        && !rel.otherPawn.Dead && rel.otherPawn.Map == gPawn.Map)
                                        bondCount++;
                                }
                            }
                            listing.Label($"{bondCount} bonded animals \u2192 +{packBonus:P0} Taming/Training/Gather", GameFont.Tiny, new Color(0.9f, 0.7f, 0.3f));
                        }
                        else
                        {
                            listing.Label("No bonded animals on map \u2014 bond animals to empower", GameFont.Tiny, new Color(0.55f, 0.52f, 0.49f));
                        }
                    }
                }
                // ────────────────────────────────────────────────────────

                // Auto-distribute toggle
                listing.Gap(4f);
                {
                    bool prev = comp.autoDistributeStats;
                    listing.Checkbox("Isekai_AutoDistribute".Translate(), ref comp.autoDistributeStats,
                        "Isekai_AutoDistribute_Desc".Translate());
                    if (comp.autoDistributeStats != prev && comp.autoDistributeStats && comp.stats.availableStatPoints > 0)
                    {
                        comp.AutoDistributeByClass();
                    }
                }

                // Dev mode buttons
                if (Prefs.DevMode && DebugSettings.godMode)
                {
                    listing.Gap(4f);
                    if (listing.ButtonSmall("+10 SP", 55f))
                        comp.stats.availableStatPoints += 10;
                    if (listing.ButtonSmall("+1 Lv", 50f))
                        comp.DevAddLevel(1);
                    if (listing.ButtonSmall("Max Lv (100)", 80f))
                    {
                        int levelsToAdd = Mathf.Max(0, 100 - comp.currentLevel);
                        if (levelsToAdd > 0)
                        {
                            comp.DevAddLevel(levelsToAdd);
                        }
                        // Auto-allocate all available points evenly
                        while (comp.stats.availableStatPoints > 0)
                        {
                            comp.stats.strength++;
                            comp.stats.availableStatPoints--;
                            if (comp.stats.availableStatPoints <= 0) break;
                            comp.stats.vitality++;
                            comp.stats.availableStatPoints--;
                            if (comp.stats.availableStatPoints <= 0) break;
                            comp.stats.dexterity++;
                            comp.stats.availableStatPoints--;
                            if (comp.stats.availableStatPoints <= 0) break;
                            comp.stats.intelligence++;
                            comp.stats.availableStatPoints--;
                            if (comp.stats.availableStatPoints <= 0) break;
                            comp.stats.wisdom++;
                            comp.stats.availableStatPoints--;
                            if (comp.stats.availableStatPoints <= 0) break;
                            comp.stats.charisma++;
                            comp.stats.availableStatPoints--;
                        }
                    }
                }
            }
            
            GUI.color = Color.white;
        }

        private void DrawStatsContent(Rect rect, Pawn pawn, IsekaiComponent comp, float alpha)
        {
            float curY = rect.y;
            float rowHeight = 20f;
            float spacing = 2f;
            float colWidth = (rect.width - 10f) / 2f;
            
            // Stats in 2 columns to fit compact space
            var stats = new (string abbr, int value, Color color, IsekaiStatType type)[]
            {
                ("Isekai_Stat_STR".Translate(), comp.stats.strength, StatSTR, IsekaiStatType.Strength),
                ("Isekai_Stat_VIT".Translate(), comp.stats.vitality, StatVIT, IsekaiStatType.Vitality),
                ("Isekai_Stat_DEX".Translate(), comp.stats.dexterity, StatDEX, IsekaiStatType.Dexterity),
                ("Isekai_Stat_INT".Translate(), comp.stats.intelligence, StatINT, IsekaiStatType.Intelligence),
                ("Isekai_Stat_WIS".Translate(), comp.stats.wisdom, StatWIS, IsekaiStatType.Wisdom),
                ("Isekai_Stat_CHA".Translate(), comp.stats.charisma, StatCHA, IsekaiStatType.Charisma),
            };
            
            for (int i = 0; i < stats.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = rect.x + col * (colWidth + 10f);
                float y = curY + row * (rowHeight + spacing);
                
                DrawCompactStatRow(new Rect(x, y, colWidth, rowHeight), 
                    stats[i].abbr, stats[i].value, stats[i].color, 
                    stats[i].type, comp.stats.availableStatPoints > 0, alpha);
            }
            
            curY += 3 * (rowHeight + spacing) + 4f;
            
            // XP bar at bottom
            DrawCompactXPBar(new Rect(rect.x, curY, rect.width, 18f), comp, alpha);
            curY += 22f;
            
            // Available points indicator
            if (comp.stats.availableStatPoints > 0)
            {
                GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, alpha);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(rect.x, curY, rect.width, 16f),
                    "Isekai_StatPointsAvailable".Translate(comp.stats.availableStatPoints));
                curY += 16f;
                
                // Bulk allocation hint (only show when many points available)
                if (comp.stats.availableStatPoints >= 5)
                {
                    GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, 0.6f * alpha);
                    Widgets.Label(new Rect(rect.x, curY, rect.width, 14f),
                        "Isekai_BulkAllocHintShort".Translate());
                    curY += 14f;
                }
                
                Text.Anchor = TextAnchor.UpperLeft;
            }
            
            // Auto-distribute toggle
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Rect toggleRect = new Rect(rect.x, curY, rect.width, 18f);
                bool prev = comp.autoDistributeStats;
                Widgets.CheckboxLabeled(toggleRect, "Isekai_AutoDistribute".Translate(), ref comp.autoDistributeStats);
                if (comp.autoDistributeStats != prev && comp.autoDistributeStats && comp.stats.availableStatPoints > 0)
                {
                    comp.AutoDistributeByClass();
                }
                TooltipHandler.TipRegion(toggleRect, "Isekai_AutoDistribute_Desc".Translate());
                curY += 18f;
            }
            
            // Dev mode buttons (compact)
            if (Prefs.DevMode && DebugSettings.godMode)
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                float devY = rect.yMax - 18f;
                if (Widgets.ButtonText(new Rect(rect.x, devY, 45f, 16f), "+SP"))
                {
                    comp.stats.availableStatPoints += 10;
                }
                if (Widgets.ButtonText(new Rect(rect.x + 48f, devY, 45f, 16f), "+Lv"))
                {
                    comp.DevAddLevel(1);
                }
                if (Widgets.ButtonText(new Rect(rect.x + 96f, devY, 55f, 16f), "Max"))
                {
                    int levelsToAdd = Mathf.Max(0, 100 - comp.currentLevel);
                    if (levelsToAdd > 0)
                    {
                        comp.DevAddLevel(levelsToAdd);
                    }
                    // Auto-allocate all available points evenly
                    while (comp.stats.availableStatPoints > 0)
                    {
                        comp.stats.strength++;
                        comp.stats.availableStatPoints--;
                        if (comp.stats.availableStatPoints <= 0) break;
                        comp.stats.vitality++;
                        comp.stats.availableStatPoints--;
                        if (comp.stats.availableStatPoints <= 0) break;
                        comp.stats.dexterity++;
                        comp.stats.availableStatPoints--;
                        if (comp.stats.availableStatPoints <= 0) break;
                        comp.stats.intelligence++;
                        comp.stats.availableStatPoints--;
                        if (comp.stats.availableStatPoints <= 0) break;
                        comp.stats.wisdom++;
                        comp.stats.availableStatPoints--;
                        if (comp.stats.availableStatPoints <= 0) break;
                        comp.stats.charisma++;
                        comp.stats.availableStatPoints--;
                    }
                }
            }
            
            GUI.color = Color.white;
        }

        private void DrawCompactStatRow(Rect rect, string abbr, int value, Color color, 
                                         IsekaiStatType type, bool canAllocate, float alpha)
        {
            string hoverId = $"stat_{abbr}";
            float hoverAmount = GetHoverAmount(hoverId, Mouse.IsOver(rect));
            
            // Subtle background on hover
            if (hoverAmount > 0)
            {
                GUI.color = new Color(0.3f, 0.28f, 0.25f, 0.3f * hoverAmount * alpha);
                GUI.DrawTexture(rect, BaseContent.WhiteTex);
            }
            
            // Left accent bar
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.DrawTexture(new Rect(rect.x, rect.y + 2f, 2f, rect.height - 4f), BaseContent.WhiteTex);
            
            // Stat abbreviation
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x + 6f, rect.y, 30f, rect.height), abbr);
            
            // Stat value
            GUI.color = new Color(TextPrimary.r, TextPrimary.g, TextPrimary.b, alpha);
            Text.Anchor = TextAnchor.MiddleRight;
            
            float valueWidth = canAllocate ? rect.width - 56f : rect.width - 36f;
            Widgets.Label(new Rect(rect.x + 36f, rect.y, valueWidth, rect.height), value.ToString());
            
            // Plus button if points available
            int maxStat = IsekaiLeveling.IsekaiStatAllocation.GetEffectiveMaxStat();
            if (canAllocate && value < maxStat)
            {
                Rect plusRect = new Rect(rect.xMax - 18f, rect.y + 2f, 16f, rect.height - 4f);
                float btnHover = GetHoverAmount($"btn_{abbr}", Mouse.IsOver(plusRect));
                
                Color btnBg = Color.Lerp(
                    new Color(0.35f, 0.55f, 0.35f, 0.7f),
                    new Color(0.45f, 0.7f, 0.45f, 1f),
                    btnHover
                );
                GUI.color = new Color(btnBg.r, btnBg.g, btnBg.b, btnBg.a * alpha);
                GUI.DrawTexture(plusRect, BaseContent.WhiteTex);
                
                GUI.color = new Color(1f, 1f, 1f, alpha);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(plusRect, "+");
                
                if (Widgets.ButtonInvisible(plusRect))
                {
                    pendingStatIncrease = type;
                    pendingStatAmount = IsekaiStatAllocation.GetBulkAmount();
                }
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private void DrawCompactXPBar(Rect rect, IsekaiComponent comp, float alpha)
        {
            // Label and value on same line
            GUI.color = new Color(TextMuted.r, TextMuted.g, TextMuted.b, alpha);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(rect.x, rect.y, 25f, 14f), "Isekai_Panel_EXP".Translate());
            
            GUI.color = new Color(TextSecondary.r, TextSecondary.g, TextSecondary.b, alpha);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 14f), 
                $"{NumberFormatting.FormatNum(comp.currentXP)} / {NumberFormatting.FormatNum(comp.XPToNextLevel)}");
            
            // Bar
            Rect barRect = new Rect(rect.x, rect.y + 12f, rect.width, 5f);
            GUI.color = new Color(0.1f, 0.08f, 0.08f, alpha);
            GUI.DrawTexture(barRect, BaseContent.WhiteTex);
            
            float fill = Mathf.Clamp01((float)comp.currentXP / Mathf.Max(1, comp.XPToNextLevel));
            if (fill > 0)
            {
                Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * fill, barRect.height);
                GUI.color = new Color(AccentGold.r, AccentGold.g, AccentGold.b, alpha);
                GUI.DrawTexture(fillRect, BaseContent.WhiteTex);
            }
            
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private float GetHoverAmount(string elementId, bool isHovered)
        {
            if (!hoverTimers.ContainsKey(elementId))
                hoverTimers[elementId] = 0f;
            
            float target = isHovered ? 1f : 0f;
            float current = hoverTimers[elementId];
            hoverTimers[elementId] = Mathf.MoveTowards(current, target, Time.deltaTime * 8f);
            
            return hoverTimers[elementId];
        }
        
        private static Dictionary<string, float> scaleStates = new Dictionary<string, float>();
        
        private float GetScaleAmount(string elementId, bool isHovered, float minScale, float maxScale)
        {
            if (!scaleStates.ContainsKey(elementId))
                scaleStates[elementId] = minScale;
            
            float target = isHovered ? maxScale : minScale;
            float current = scaleStates[elementId];
            scaleStates[elementId] = Mathf.MoveTowards(current, target, Time.deltaTime * 6f);
            
            return scaleStates[elementId];
        }

        private void ProcessPendingStatChanges(IsekaiComponent comp, Pawn pawn)
        {
            if (pendingStatIncrease.HasValue)
            {
                if (pendingStatAmount > 1)
                {
                    comp.stats.AllocatePoints(pendingStatIncrease.Value, pendingStatAmount);
                }
                else
                {
                    comp.stats.AllocatePoint(pendingStatIncrease.Value);
                }
                pendingStatIncrease = null;
                pendingStatAmount = 1;
                PawnStatGenerator.UpdateRankTraitFromStats(pawn, comp);
            }
        }

        private string GetLevelRank(int level)
        {
            if (level >= 401) return "SSS";
            if (level >= 201) return "SS";
            if (level >= 101) return "S";
            if (level >= 51) return "A";
            if (level >= 26) return "B";
            if (level >= 18) return "C";
            if (level >= 11) return "D";
            if (level >= 6) return "E";
            return "F";
        }
        
        private string GetRankTitle(string rank)
        {
            switch (rank)
            {
                case "SSS": return "Isekai_Title_Overlord".Translate();
                case "SS": return "Isekai_Title_Monarch".Translate();
                case "S": return "Isekai_Title_Legend".Translate();
                case "A": return "Isekai_Title_Hero".Translate();
                case "B": return "Isekai_Title_Champion".Translate();
                case "C": return "Isekai_Title_Warrior".Translate();
                case "D": return "Isekai_Title_Adventurer".Translate();
                case "E": return "Isekai_Title_Apprentice".Translate();
                case "F": return "Isekai_Title_Awakened".Translate();
                default: return "";
            }
        }
        
        private string GetRankTranslated(string rank)
        {
            return ("Isekai_Rank_" + rank).Translate();
        }
        
        private string GetStatAbbr(string statKey)
        {
            return ("Isekai_Stat_" + statKey).Translate();
        }
        
        private Color GetRankColor(string rank)
        {
            switch (rank)
            {
                case "SSS": return new Color(1f, 0.85f, 0.3f);      // Golden
                case "SS": return new Color(0.9f, 0.5f, 0.9f);      // Magenta
                case "S": return new Color(1f, 0.6f, 0.2f);         // Orange
                case "A": return new Color(0.85f, 0.3f, 0.3f);      // Red
                case "B": return new Color(0.6f, 0.4f, 0.8f);       // Purple
                case "C": return new Color(0.3f, 0.6f, 0.9f);       // Blue
                case "D": return new Color(0.4f, 0.8f, 0.4f);       // Green
                case "E": return new Color(0.75f, 0.75f, 0.78f);    // Silver
                case "F": return new Color(0.6f, 0.55f, 0.5f);      // Bronze
                default: return Color.gray;
            }
        }
    }
    
    /// <summary>
    /// Window for detailed skills view - Sexy designer style
    /// </summary>
    public class Window_Skills : Window
    {
        private Pawn pawn;
        
        public override Vector2 InitialSize => new Vector2(420f, 500f);
        
        public Window_Skills(Pawn pawn)
        {
            this.pawn = pawn;
            this.doCloseButton = false;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = true;
            this.drawShadow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            // Dark themed background
            GUI.color = IsekaiListing.BgDark;
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            
            Rect contentRect = inRect.ContractedBy(16f);
            
            using (var listing = new IsekaiListing(contentRect))
            {
                listing.Header("Isekai_PawnSkills".Translate(pawn.LabelShortCap));
                listing.Gap(8f);

                // RimWorld skills with Isekai styling
                listing.Label("Isekai_PassiveBonuses".Translate(), GameFont.Small, IsekaiListing.AccentGold);
                listing.Gap(4f);
                listing.GapLine();

                var comp = pawn.GetComp<IsekaiComponent>();
                if (comp != null)
                {
                    // Show derived bonuses from stats
                    listing.StatRow("Isekai_Passive_MeleePower".Translate(), (int)(comp.stats.strength * 0.5f), IsekaiListing.StatSTR,
                        "Isekai_Passive_MeleePower_Desc".Translate());
                    listing.StatRow("Isekai_Passive_Toughness".Translate(), (int)(comp.stats.vitality * 0.3f), IsekaiListing.StatVIT,
                        "Isekai_Passive_Toughness_Desc".Translate());
                    listing.StatRow("Isekai_Passive_Evasion".Translate(), (int)(comp.stats.dexterity * 0.4f), IsekaiListing.StatDEX,
                        "Isekai_Passive_Evasion_Desc".Translate());
                    listing.StatRow("Isekai_Passive_Focus".Translate(), (int)(comp.stats.intelligence * 0.5f), IsekaiListing.StatINT,
                        "Isekai_Passive_Focus_Desc".Translate());
                    listing.StatRow("Isekai_Passive_Insight".Translate(), (int)(comp.stats.wisdom * 0.4f), IsekaiListing.StatWIS,
                        "Isekai_Passive_Insight_Desc".Translate());
                    listing.StatRow("Isekai_Passive_Presence".Translate(), (int)(comp.stats.charisma * 0.5f), IsekaiListing.StatCHA,
                        "Isekai_Passive_Presence_Desc".Translate());
                }

                listing.Gap(16f);
                listing.Label("Isekai_ActiveSkills".Translate(), GameFont.Small, IsekaiListing.AccentCopper);
                listing.Gap(4f);
                listing.GapLine();
                listing.Label("Isekai_ComingSoon".Translate(), GameFont.Tiny, IsekaiListing.TextMuted, TextAnchor.MiddleCenter);
            }
            
            GUI.color = Color.white;
        }
    }
    
    /// <summary>
    /// Window for detailed stats view - Sexy designer style
    /// </summary>
    public class Window_DetailedStats : Window
    {
        private Pawn pawn;
        
        public override Vector2 InitialSize => new Vector2(480f, 580f);
        
        public Window_DetailedStats(Pawn pawn)
        {
            this.pawn = pawn;
            this.doCloseButton = false;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = true;
            this.drawShadow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            var comp = pawn.GetComp<IsekaiComponent>();
            if (comp == null) return;
            
            // Dark themed background
            GUI.color = IsekaiListing.BgDark;
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            
            Rect contentRect = inRect.ContractedBy(16f);
            
            using (var listing = new IsekaiListing(contentRect))
            {
                listing.Header("Isekai_PawnFullStats".Translate(pawn.LabelShortCap));
                
                // Level & XP Section
                listing.Gap(4f);
                string rank = GetLevelRank(comp.Level);
                string rankTranslated = GetRankTranslated(rank);
                listing.LabelCentered("Isekai_LevelRankDisplayFull".Translate(comp.Level, rankTranslated), GameFont.Medium, IsekaiListing.TextPrimary);
                listing.Gap(6f);
                listing.XPBar(comp.currentXP, comp.XPToNextLevel);
                listing.PointsAvailable(comp.stats.availableStatPoints);
                
                listing.Gap(8f);
                listing.GapLine();
                listing.Gap(4f);
                
                // Core Stats with full names - translated
                listing.Label("Isekai_CoreAttributes".Translate(), GameFont.Small, IsekaiListing.AccentGold);
                listing.Gap(6f);
                
                listing.StatRow("Isekai_Stat_Strength".Translate(), comp.stats.strength, IsekaiListing.StatSTR, 
                    "Isekai_Desc_STR_Short".Translate());
                listing.StatRow("Isekai_Stat_Vitality".Translate(), comp.stats.vitality, IsekaiListing.StatVIT, 
                    "Isekai_Desc_VIT_Short".Translate());
                listing.StatRow("Isekai_Stat_Dexterity".Translate(), comp.stats.dexterity, IsekaiListing.StatDEX, 
                    "Isekai_Desc_DEX_Short".Translate());
                listing.StatRow("Isekai_Stat_Intelligence".Translate(), comp.stats.intelligence, IsekaiListing.StatINT, 
                    "Isekai_Desc_INT_Short".Translate());
                listing.StatRow("Isekai_Stat_Wisdom".Translate(), comp.stats.wisdom, IsekaiListing.StatWIS, 
                    "Isekai_Desc_WIS_Short".Translate());
                listing.StatRow("Isekai_Stat_Charisma".Translate(), comp.stats.charisma, IsekaiListing.StatCHA, 
                    "Isekai_Desc_CHA_Short".Translate());
                
                listing.Gap(12f);
                listing.GapLine();
                listing.Gap(4f);
                
                // Combat Stats
                listing.Label("Isekai_CombatPerformance".Translate(), GameFont.Small, IsekaiListing.AccentCopper);
                listing.Gap(6f);
                
                float meleeDmg = pawn.GetStatValue(StatDefOf.MeleeDamageFactor);
                float shootAcc = pawn.GetStatValue(StatDefOf.ShootingAccuracyPawn);
                float moveSpd = pawn.GetStatValue(StatDefOf.MoveSpeed);
                float workSpd = pawn.GetStatValue(StatDefOf.WorkSpeedGlobal);
                
                listing.BeginColumns(2, 16f);
                listing.StatRowCompact("Isekai_Combat_ATK".Translate(), (int)(meleeDmg * 100), IsekaiListing.StatSTR);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Combat_ACC".Translate(), (int)(shootAcc * 100), IsekaiListing.StatDEX);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Combat_SPD".Translate(), (int)(moveSpd * 20), IsekaiListing.StatDEX);
                listing.NextColumn();
                listing.StatRowCompact("Isekai_Combat_WRK".Translate(), (int)(workSpd * 100), IsekaiListing.StatINT);
                listing.EndColumns();
            }
            
            GUI.color = Color.white;
        }
        
        private string GetLevelRank(int level)
        {
            // Returns the rank letter - use GetRankTranslated for display
            if (level >= 401) return "SSS";
            if (level >= 201) return "SS";
            if (level >= 101) return "S";
            if (level >= 51) return "A";
            if (level >= 26) return "B";
            if (level >= 18) return "C";
            if (level >= 11) return "D";
            if (level >= 6) return "E";
            return "F";
        }
        
        private string GetRankTranslated(string rank)
        {
            return ("Isekai_Rank_" + rank).Translate();
        }
        
        private string GetStatAbbr(string statKey)
        {
            return ("Isekai_Stat_" + statKey).Translate();
        }
    }
    
    /// <summary>
    /// Window for abilities view - Sexy designer style
    /// </summary>
    public class Window_Abilities : Window
    {
        private Pawn pawn;
        
        public override Vector2 InitialSize => new Vector2(420f, 500f);
        
        public Window_Abilities(Pawn pawn)
        {
            this.pawn = pawn;
            this.doCloseButton = false;
            this.doCloseX = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = true;
            this.drawShadow = true;
        }
        
        public override void DoWindowContents(Rect inRect)
        {
            // Dark themed background
            GUI.color = IsekaiListing.BgDark;
            GUI.DrawTexture(inRect, BaseContent.WhiteTex);
            
            Rect contentRect = inRect.ContractedBy(16f);
            
            using (var listing = new IsekaiListing(contentRect))
            {
                listing.Header("Isekai_PawnAbilities".Translate(pawn.LabelShortCap));
                listing.Gap(8f);

                listing.Label("Isekai_ActiveAbilities".Translate(), GameFont.Small, IsekaiListing.AccentGold);
                listing.Gap(4f);
                listing.GapLine();

                // Placeholder for abilities
                listing.Gap(20f);
                listing.LabelCentered("Isekai_NoAbilitiesYet".Translate(), GameFont.Small, IsekaiListing.TextMuted);
                listing.Gap(8f);
                listing.LabelCentered("Isekai_AbilitiesHint1".Translate(), GameFont.Tiny, IsekaiListing.TextMuted);
                listing.LabelCentered("Isekai_AbilitiesHint2".Translate(), GameFont.Tiny, IsekaiListing.TextMuted);

                listing.Gap(24f);
                listing.Label("Isekai_MagicSpells".Translate(), GameFont.Small, IsekaiListing.AccentCopper);
                listing.Gap(4f);
                listing.GapLine();

                listing.Gap(20f);
                listing.LabelCentered("Isekai_ComingSoon".Translate(), GameFont.Small, IsekaiListing.TextMuted);
            }
            
            GUI.color = Color.white;
        }
    }
}
