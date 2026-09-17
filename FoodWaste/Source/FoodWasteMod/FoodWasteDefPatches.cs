using System.Collections.Generic;
using RimWorld;
using Verse;

namespace FoodWasteMod
{
    // Attaches CompSpoilageResidue to every food def that rots away to nothing, at startup.
    //
    // Injecting the comp beats naming the defs in XML for the reason that matters most here:
    // there is no list of food defs to name. Vanilla has dozens, every food mod adds more, and a
    // patch that enumerates them is wrong the day after it ships. Asking the database "which defs
    // rot away and carry nutrition" answers the question for modded food nobody has written yet.
    //
    // Runs at StaticConstructorOnStartup, which is after every def is loaded and resolved but
    // before any game exists. Comps are instantiated per-Thing from def.comps when the thing is
    // made, so everything created from here on carries the comp.
    [StaticConstructorOnStartup]
    public static class FoodWasteDefPatches
    {
        static FoodWasteDefPatches()
        {
            List<string> patched = new List<string>();

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!QualifiesForResidue(def))
                    continue;

                def.comps.Add(new CompProperties_SpoilageResidue());
                patched.Add(def.defName);
            }

            if (patched.Count == 0)
            {
                Log.Warning("[SloppyFoodWaste] No rot-destroying food defs found. Rotted food will "
                    + "leave nothing - this is not expected on a normal install.");
            }
            else if (Prefs.DevMode)
            {
                Log.Message($"[SloppyFoodWaste] Spoilage residue attached to {patched.Count} food defs: "
                    + string.Join(", ", patched.ToArray()));
            }
        }

        private static bool QualifiesForResidue(ThingDef def)
        {
            // No comps at all means it cannot be rottable, and def.comps is null rather than empty
            // for most defs in the database.
            if (def.comps.NullOrEmpty())
                return false;

            // Corpses set rotDestroys false - they dessicate rather than vanish - so this is
            // belt and braces. It is worth keeping anyway: a mod that made corpses vanish would
            // otherwise turn every dead raider into a compost heap, which is a different feature.
            if (def.IsCorpse)
                return false;

            // Only things a pawn or animal could actually have eaten. This is what keeps drugs
            // with no nutrition, medicine and assorted rottable non-food out, and it is also the
            // guard that stops spoiled food feeding itself: it has no ingestible properties, so
            // the pile it leaves behind can never leave a pile of its own.
            if (def.ingestible == null)
                return false;

            if (def.GetStatValueAbstract(StatDefOf.Nutrition) <= 0f)
                return false;

            bool rotDestroys = false;

            foreach (CompProperties comp in def.comps)
            {
                // Idempotent, in case this ever runs twice or another mod has added the comp.
                if (comp is CompProperties_SpoilageResidue)
                    return false;

                if (comp is CompProperties_Rottable rottable && rottable.rotDestroys)
                    rotDestroys = true;
            }

            return rotDestroys;
        }
    }
}
