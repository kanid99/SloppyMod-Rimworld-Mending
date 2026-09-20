using RimWorld;
using Verse;

namespace MendingMod
{
    // A mend bill pinned to ONE material, for installs without Material Filter.
    //
    // WITH that mod this is not used, and should not be - see MaterialFilters. It generates a
    // SpecialThingFilterDef per material whose emitted worker compares Thing.Stuff, and
    // ThingFilter.Allows(Thing) evaluates those, so its checkboxes restrict a mend bill through
    // vanilla once the work scan honours the bill's ingredient filter. Those checkboxes are
    // visible and editable; a pin held here as well would silently override them.
    //
    // Without it there is no UI for materials at all, and this is the fallback: the same
    // restriction, carried on the bill and checked where the mend target is chosen. The label
    // says so, because a restriction with no UI behind it must at least be readable.
    public class Bill_Mend : Bill_Production
    {
        // null means no restriction, which is also what an unstuffed item gets - a plasteel knife
        // has stuff, a chemfuel-powered smokepop pack does not, and pinning the second to "no
        // material" would make its own bill refuse it.
        public ThingDef onlyStuff;

        // Scribe needs a parameterless one to construct into on load.
        public Bill_Mend()
        {
        }

        public Bill_Mend(RecipeDef recipe, Precept_ThingStyle precept = null)
            : base(recipe, precept)
        {
        }

        public bool AllowsStuffOf(Thing item)
        {
            return onlyStuff == null || item.Stuff == onlyStuff;
        }

        public override string Label
        {
            get
            {
                string label = base.Label;
                return onlyStuff == null ? label : label + " (" + onlyStuff.label + ")";
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref onlyStuff, "onlyStuff");
        }

        // Copying a bill in the UI goes through Clone, and a clone that quietly dropped the
        // material would repair the wrong things while still reading as restricted.
        public override Bill Clone()
        {
            Bill_Mend clone = (Bill_Mend)base.Clone();
            clone.onlyStuff = onlyStuff;
            return clone;
        }
    }
}
