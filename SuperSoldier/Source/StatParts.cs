using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace EnhancedCarryingCapacity
{
    public sealed class StatPart_MassModifier : StatPart
    {
        public override string ExplanationPart(StatRequest req)
        {
            return $"{"StatsReport_MassModifier".Translate()}: x{EnhancedCarryingCapacitySettings.Instance.MassMultiplier}" +
                $"\n{"StatsReport_MassOffset".Translate()}: +{EnhancedCarryingCapacitySettings.Instance.MassOffset}";
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            val *= EnhancedCarryingCapacitySettings.Instance.MassMultiplier;
            val += EnhancedCarryingCapacitySettings.Instance.MassOffset;
        }
    }

    public sealed class StatPart_BulkModifier : StatPart
    {
        public override string ExplanationPart(StatRequest req)
        {
            return $"{"StatsReport_BulkModifier".Translate()}: x{EnhancedCarryingCapacitySettings.Instance.BulkMultiplier}" +
                $"\n{"StatsReport_BulkOffset".Translate()}: +{EnhancedCarryingCapacitySettings.Instance.BulkOffset}";
        }

        public override void TransformValue(StatRequest req, ref float val)
        {
            val *= EnhancedCarryingCapacitySettings.Instance.BulkMultiplier;
            val += EnhancedCarryingCapacitySettings.Instance.BulkOffset;
        }
    }
}
