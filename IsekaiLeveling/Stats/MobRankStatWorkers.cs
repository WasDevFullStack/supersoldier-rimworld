using RimWorld;
using UnityEngine;
using Verse;
using IsekaiLeveling.MobRanking;

namespace IsekaiLeveling.Stats
{
    /// <summary>
    /// StatWorker that displays creature rank as a letter (e.g. "A-Rank") instead of a number.
    /// Only shows for pawns that have a MobRankComponent.
    /// </summary>
    public class StatWorker_MobRank : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (!base.ShouldShowFor(req)) return false;
            if (!(req.Thing is Pawn pawn)) return false;
            var comp = pawn.TryGetComp<MobRankComponent>();
            return comp != null && comp.IsInitialized;
        }

        public override string ValueToString(float val, bool finalize, ToStringNumberSense numberSense = ToStringNumberSense.Absolute)
        {
            int tier = Mathf.RoundToInt(val);
            if (tier < 0) return "-";
            MobRankTier rank = (MobRankTier)Mathf.Clamp(tier, 0, 9);
            return MobRankUtility.GetRankString(rank) + "-Rank";
        }
    }

    /// <summary>
    /// StatPart that reads the creature's rank from MobRankComponent and sets the stat value.
    /// </summary>
    public class StatPart_MobRank : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!(req.Thing is Pawn pawn)) return;
                var comp = pawn.TryGetComp<MobRankComponent>();
                if (comp == null || !comp.IsInitialized) return;
                val = (float)comp.Rank;
            }
            catch { }
        }

        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!(req.Thing is Pawn pawn)) return null;
                var comp = pawn.TryGetComp<MobRankComponent>();
                if (comp == null || !comp.IsInitialized) return null;
                var rank = comp.Rank;
                string elite = comp.IsElite ? " (Elite)" : "";
                return MobRankUtility.GetRankString(rank) + "-Rank — " + MobRankUtility.GetRankTitle(rank) + elite;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// StatWorker that displays creature level as an integer.
    /// Only shows for pawns that have a MobRankComponent.
    /// </summary>
    public class StatWorker_MobLevel : StatWorker
    {
        public override bool ShouldShowFor(StatRequest req)
        {
            if (!base.ShouldShowFor(req)) return false;
            if (!(req.Thing is Pawn pawn)) return false;
            var comp = pawn.TryGetComp<MobRankComponent>();
            return comp != null && comp.IsInitialized;
        }

        public override string ValueToString(float val, bool finalize, ToStringNumberSense numberSense = ToStringNumberSense.Absolute)
        {
            int level = Mathf.RoundToInt(val);
            if (level < 0) return "-";
            return "Lv. " + level;
        }
    }

    /// <summary>
    /// StatPart that reads the creature's level from MobRankComponent.
    /// </summary>
    public class StatPart_MobLevel : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            try
            {
                if (!(req.Thing is Pawn pawn)) return;
                var comp = pawn.TryGetComp<MobRankComponent>();
                if (comp == null || !comp.IsInitialized) return;
                val = comp.currentLevel;
            }
            catch { }
        }

        public override string ExplanationPart(StatRequest req)
        {
            try
            {
                if (!(req.Thing is Pawn pawn)) return null;
                var comp = pawn.TryGetComp<MobRankComponent>();
                if (comp == null || !comp.IsInitialized) return null;
                return "Level " + comp.currentLevel;
            }
            catch { return null; }
        }
    }
}
