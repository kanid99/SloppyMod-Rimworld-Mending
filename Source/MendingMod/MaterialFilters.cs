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
            if (!Prefs.DevMode)
                return;

            Log.Message($"[DynamicMending] Material Filter: {(Installed ? all.Count + " material filters found" : "not installed")}");
            ReportMendRecipes();
        }

        // Why that mod's "Filter >>" button does or does not appear on a given mend bill.
        //
        // Its gate decompiles to: any ThingDef allowed by any of recipe.ingredients[*].filter
        // that is MadeFromStuff. A recipe whose ingredient filter resolves to nothing stuffed
        // gets no button, and the same filter is what the work scan picks targets from - so a
        // surprising number here is worth knowing about for its own sake, not just for the UI.
        private static void ReportMendRecipes()
        {
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefsListForReading)
            {
                if (recipe.workerClass == null
                    || !typeof(RecipeWorker_Mend).IsAssignableFrom(recipe.workerClass)
                    || recipe.ingredients.NullOrEmpty())
                {
                    continue;
                }

                List<ThingDef> allowed = recipe.ingredients[0].filter.AllowedThingDefs.ToList();
                List<ThingDef> stuffed = allowed.Where(d => d.MadeFromStuff).ToList();

                Log.Message($"[DynamicMending] {recipe.defName}: {allowed.Count} defs allowed, "
                            + $"{stuffed.Count} made from stuff"
                            + (stuffed.Count == 0 ? "  <- Material Filter shows no button for this recipe" : "")
                            + (allowed.Count == 0 ? "  <- AND NOTHING CAN BE REPAIRED BY IT" : "")
                            + (allowed.Count > 0
                                ? "\n    allowed e.g. " + allowed.Take(8).Select(d => d.defName).ToCommaList()
                                : "")
                            + (stuffed.Count > 0
                                ? "\n    stuffed e.g. " + stuffed.Take(8).Select(d => d.defName).ToCommaList()
                                : ""));
            }
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
