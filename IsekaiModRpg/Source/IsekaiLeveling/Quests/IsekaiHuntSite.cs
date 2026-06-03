using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using UnityEngine;

namespace IsekaiLeveling.Quests
{
    /// <summary>
    /// Custom WorldObject for Isekai Hunt sites with proper description and appearance
    /// </summary>
    public class IsekaiHuntSite : Site
    {
        public PawnKindDef targetCreature;
        public QuestRank rank;
        public float xpReward;
        public float silverReward;
        public Quest linkedQuest;
        
        public override string Label
        {
            get
            {
                if (targetCreature != null)
                    return "Isekai_Hunt_Site_Label".Translate(rank.ToString(), targetCreature.LabelCap);
                return base.Label;
            }
        }
        
        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder();
            
            // Guild header
            sb.AppendLine("Isekai_Hunt_Site_Header".Translate());
            
            // Target info
            if (targetCreature != null)
            {
                sb.AppendLine("Isekai_Hunt_Site_Target".Translate(targetCreature.LabelCap));
            }
            
            // Rank
            sb.AppendLine("Isekai_Hunt_Site_Rank".Translate(rank.ToString()));
            
            // Rewards
            sb.AppendLine("Isekai_Hunt_Site_Rewards".Translate(
                NumberFormatting.FormatNum(silverReward),
                NumberFormatting.FormatNum(xpReward)
            ));
            
            // Instructions
            sb.Append("Isekai_Hunt_Site_Instructions".Translate());
            
            return sb.ToString();
        }
        
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            
            // Add "View Quest" button if linked
            if (linkedQuest != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Isekai_Hunt_ViewQuest".Translate(),
                    defaultDesc = "Isekai_Hunt_ViewQuestDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ViewQuest", false) ?? BaseContent.BadTex,
                    action = delegate
                    {
                        Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Quests);
                        ((MainTabWindow_Quests)MainButtonDefOf.Quests.TabWindow).Select(linkedQuest);
                    }
                };
            }
        }
        
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref targetCreature, "targetCreature");
            Scribe_Values.Look(ref rank, "rank", QuestRank.F);
            Scribe_Values.Look(ref xpReward, "xpReward", 0f);
            Scribe_Values.Look(ref silverReward, "silverReward", 0f);
            Scribe_References.Look(ref linkedQuest, "linkedQuest");
        }
    }
}
