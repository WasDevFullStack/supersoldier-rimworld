using Verse;
using RimWorld;
using HarmonyLib;

namespace SuperSoldier
{
    /// <summary>
    /// Component para ajustar stats do Super_Human_Prototype.
    /// Modifica CarryingCapacity através de um patch no ThingDef.
    /// </summary>
    public class SuperHumanPrototypeComponent : ThingComp
    {
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            // Se for Super_Human_Prototype, ajusta o carrying capacity
            if (parent is Pawn pawn && pawn.def?.defName == "Super_Human_Prototype")
            {
                AdjustCarryingCapacity(pawn);
            }
        }

        /// <summary>
        /// Ajusta o CarryingCapacity do pawn através de um StatModifier.
        /// </summary>
        private void AdjustCarryingCapacity(Pawn pawn)
        {
            if (pawn?.def?.statBases == null) return;

            // Encontra e modifica o stat de CarryingCapacity
            var carryingStat = pawn.def.statBases.Find(s => s.stat == StatDefOf.CarryingCapacity);
            
            if (carryingStat != null)
            {
                carryingStat.value = 1000f;
            }
        }
    }
}
