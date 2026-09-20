using RimWorld;
using Verse;

namespace MendingMod
{
    // A mend bill that can be pinned to ONE material.
    //
    // This exists because nothing else can do it. ThingFilter.Allows(Thing) was decompiled and it
    // tests exactly four things - the thing's def, its hit points, its quality and the special
    // filters - and never looks at Thing.Stuff. Bill.IsFixedOrAllowedIngredient is built on that
    // same call, so a bill's ingredient filter cannot express "steel ones only" either. The
    // Material Filter mod does not change this: its assembly is a UI shortcut that opens a window
    // of stuff defs and toggles them on the VANILLA filter via SetAllow(ThingDef, bool), storing
    // nothing of its own and patching no ingredient check. Those toggles only bite on recipes
    // where the stuff is itself consumed as an ingredient, and a mend bill's ingredient is the
    // damaged item, not what it is made of.
    //
    // So the restriction is carried here instead, on the bill, and enforced where the mend target
    // is chosen. It works with or without that mod installed.
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
