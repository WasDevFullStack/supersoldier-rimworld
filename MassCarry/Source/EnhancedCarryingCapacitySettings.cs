using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace EnhancedCarryingCapacity
{
    public sealed class EnhancedCarryingCapacitySettings : ModSettings
    {
        private const float DefaultMassMultiplier = 1f; // carrying capacity (vanilla) OR weight (CE)
        private const float DefaultMassOffset = 0f;
        private const float DefaultBulkMultiplier = 1f; // bulk (CE)
        private const float DefaultBulkOffset = 0f;

        private static EnhancedCarryingCapacitySettings instance;
        public static EnhancedCarryingCapacitySettings Instance
        {
            get
            {
                if (instance == default) instance = LoadedModManager.GetMod<EnhancedCarryingCapacity>().GetSettings<EnhancedCarryingCapacitySettings>();
                return instance;
            }
        }

        private string m_massMultiplierBuffer, m_massOffsetBuffer, m_bulkMultiplierBuffer, m_bulkOffsetBuffer;

        private float m_massMultiplier = DefaultMassMultiplier;
        public float MassMultiplier => m_massMultiplier;

        private float m_massOffset = DefaultMassOffset;
        public float MassOffset => m_massOffset;

        private float m_bulkMultiplier = DefaultBulkMultiplier;
        public float BulkMultiplier => m_bulkMultiplier;

        private float m_bulkOffset = DefaultBulkOffset;
        public float BulkOffset => m_bulkOffset;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref m_massMultiplier, "MassMultiplier", DefaultMassMultiplier);
            Scribe_Values.Look(ref m_massOffset, "MassOffset", DefaultMassOffset);
            Scribe_Values.Look(ref m_bulkMultiplier, "BulkMultiplier", DefaultBulkMultiplier);
            Scribe_Values.Look(ref m_bulkOffset, "BulkOffset", DefaultBulkOffset);
            base.ExposeData();
        }


        public void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.Label("EnhancedCarryingCapacity.MassMultiplier".Translate(), tooltip: "EnhancedCarryingCapacity.MassMultiplier.Tooltip".Translate());
            listingStandard.TextFieldNumeric(ref m_massMultiplier, ref m_massMultiplierBuffer);
            listingStandard.Label("EnhancedCarryingCapacity.MassOffset".Translate(), tooltip: "EnhancedCarryingCapacity.MassOffset.Tooltip".Translate());
            listingStandard.TextFieldNumeric(ref m_massOffset, ref m_massOffsetBuffer);
            listingStandard.Label("EnhancedCarryingCapacity.BulkMultiplier".Translate(), tooltip: "EnhancedCarryingCapacity.BulkMultiplier.Tooltip".Translate());
            listingStandard.TextFieldNumeric(ref m_bulkMultiplier, ref m_bulkMultiplierBuffer);
            listingStandard.Label("EnhancedCarryingCapacity.BulkOffset".Translate(), tooltip: "EnhancedCarryingCapacity.BulkOffset.Tooltip".Translate());
            listingStandard.TextFieldNumeric(ref m_bulkOffset, ref m_bulkOffsetBuffer);
            listingStandard.End();
        }
    }
}
