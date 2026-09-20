using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace MendingMod
{
    // Narrows a bill this mod creates so it only takes items made of the same material as the
    // one the player right-clicked.
    //
    // This CANNOT be done in vanilla, which is worth stating because it looks like it should be.
    // ThingFilter.Allows(Thing) was decompiled to check exactly four things - def membership,
    // hit points, quality and special filters - and no stuff axis at all. ThingFilter's
    // SetAllow(StuffCategoryDef) overload only matters where stuff is itself an ingredient, and
    // a mend bill's one ingredient is the damaged item, not what it is made of. Filtering a mend
    // bill by material is precisely the gap the Material Filter mod fills.
    //
    // So this is reflection against that mod and a no-op without it: a missing mod must cost the
    // narrowing and nothing else. The bill still works, it is just as broad as the player's own
    // mend bills are.
    [StaticConstructorOnStartup]
    public static class MaterialFilterCompat
    {
        private static readonly bool active;

        static MaterialFilterCompat()
        {
            active = Resolve();

            if (Prefs.DevMode)
                Log.Message("[DynamicMending] Material Filter integration: " + (active ? "active" : "not installed"));
        }

        private static bool Resolve()
        {
            // TODO: complete once the Material Filter assembly is in hand - the entry point it
            // exposes for "restrict this bill to these materials" has to be read off the real
            // thing rather than guessed at, and a wrong guess here would be a silent no-op that
            // looks like it is working.
            return false;
        }

        public static void RestrictToStuffOf(Bill_Production bill, Thing item)
        {
            if (!active || bill == null || item?.Stuff == null)
                return;
        }
    }
}
