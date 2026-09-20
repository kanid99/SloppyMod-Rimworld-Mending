using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MendingMod
{
    // Material Filter's per-material filters, looked up by name.
    //
    // That mod is NOT the UI shortcut it first looks like. On DefsLoaded it walks every stuff
    // ThingDef and builds a SpecialThingFilterDef called "MaterialFilter_allow<defName>" for
    // each, whose worker class is emitted at runtime with Reflection.Emit and whose Matches(Thing)
    // calls Thing.get_Stuff and compares. ThingFilter.Allows(Thing) evaluates disallowed special
    // filters through SpecialThingFilterDef.Worker.Matches, so those checkboxes are enforced by
    // VANILLA, on any bill, including ours - which is why WorkGiver_Mend honouring the bill's
    // ingredient filter is all it takes to make "only steel swords" work.
    //
    // Nothing here is reflection or a hard reference: a def lookup by name is enough, and it
    // comes back empty when the mod is absent.
    [StaticConstructorOnStartup]
    public static class MaterialFilters
    {
        private const string Prefix = "MaterialFilter_allow";

        private static readonly List<SpecialThingFilterDef> all =
            DefDatabase<SpecialThingFilterDef>.AllDefsListForReading
                .Where(d => d.defName.StartsWith(Prefix))
                .ToList();

        public static bool Installed => all.Count > 0;

        static MaterialFilters()
        {
            if (Prefs.DevMode)
                Log.Message($"[DynamicMending] Material Filter: {(Installed ? all.Count + " material filters found" : "not installed")}");
        }

        // "Only things made of this" expressed the way the player's own UI expresses it: every
        // other material's filter turned off. An unstuffed item matches none of them and so is
        // never excluded by any of them, which is the same as leaving the bill unrestricted.
        public static bool TryRestrictTo(Bill bill, ThingDef stuff)
        {
            if (!Installed || bill == null || stuff == null)
                return false;

            string keep = Prefix + stuff.defName;
            bool found = false;

            foreach (SpecialThingFilterDef filter in all)
            {
                bool allow = filter.defName == keep;
                bill.ingredientFilter.SetAllow(filter, allow);
                found |= allow;
            }

            return found;
        }
    }
}
