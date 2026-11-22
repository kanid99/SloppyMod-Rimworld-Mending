using RimWorld;
using Verse;

namespace MendingMod
{
    /// <summary>
    /// "Made of cloth / leather / metal / wood" checkboxes for item filters (bench bills, the
    /// repair center's bills, stockpiles). An item is made of what MendingUtility.MainMaterial
    /// says: its stuff, or the main material in its build cost. Like vanilla's other "allow..."
    /// filters they start ticked; untick one to leave out gear made of that.
    /// </summary>
    public abstract class SpecialThingFilterWorker_MadeOf : SpecialThingFilterWorker
    {
        protected abstract StuffCategoryDef Category { get; }

        public override bool Matches(Thing t)
        {
            ThingDef material = MendingUtility.MainMaterial(t);
            return material != null && material.stuffProps != null && material.stuffProps.categories != null
                && material.stuffProps.categories.Contains(Category);
        }

        public override bool CanEverMatch(ThingDef def)
        {
            return def.IsApparel || def.IsWeapon;
        }
    }

    public class SpecialThingFilterWorker_MadeOfCloth : SpecialThingFilterWorker_MadeOf
    {
        protected override StuffCategoryDef Category => StuffCategoryDefOf.Fabric;
    }

    public class SpecialThingFilterWorker_MadeOfLeather : SpecialThingFilterWorker_MadeOf
    {
        protected override StuffCategoryDef Category => StuffCategoryDefOf.Leathery;
    }

    public class SpecialThingFilterWorker_MadeOfMetal : SpecialThingFilterWorker_MadeOf
    {
        protected override StuffCategoryDef Category => StuffCategoryDefOf.Metallic;
    }

    public class SpecialThingFilterWorker_MadeOfWood : SpecialThingFilterWorker_MadeOf
    {
        protected override StuffCategoryDef Category => StuffCategoryDefOf.Woody;
    }
}
