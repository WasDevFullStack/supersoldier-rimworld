using Verse;

namespace MySuperStrengthMod
{
    public class CompProperties_SuperStrength : CompProperties
    {
        public int strengthLevel = 1;
        public CompProperties_SuperStrength() => this.compClass = typeof(CompSuperStrength);
    }

    public class CompSuperStrength : ThingComp
    {
        public CompProperties_SuperStrength Props => (CompProperties_SuperStrength)this.props;
        public int StrengthLevel => Props.strengthLevel;
    }

}
