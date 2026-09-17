using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RiimbaMod
{
    // Vanilla prerequisites, build-cost ingredients and one comp removal are applied here rather
    // than named in XML. A defName that does not exist in a given install - a different RimWorld
    // version, a missing DLC, another mod that removed or renamed it - would raise a config error
    // every load if it were an XML reference; resolved here it is simply skipped.
    //
    // Runs at StaticConstructorOnStartup, which is after every def is loaded and resolved and
    // before any pawn or building has been made from one.
    [StaticConstructorOnStartup]
    public static class RiimbaDefPatches
    {
        static RiimbaDefPatches()
        {
            List<string> missing = new List<string>();

            // Drones are mech hardware built at a mech gestator, and the station is a fabricated
            // housing for a subcore, so the project sits behind both lines of work.
            AddPrerequisites("RiimbaCleaning", missing, "BasicMechtech", "Fabrication");

            // The subcore is the station's whole reason to exist: it is what does the thinking
            // the drones do not. Biotech is a hard dependency and the subcore comes from the same
            // Basic Mechtech work the research above requires, so it is always available by the
            // time this is buildable.
            AddBuildCost("RiimbaStation", 1, "basic subcore", missing, "SubcoreBasic", "BasicSubcore");

            StripOverseerSubject();

            if (Prefs.DevMode && missing.Count > 0)
            {
                Log.Message("[Riimba] Optional defs not present in this install, gating skipped for: "
                    + string.Join(", ", missing));
            }
        }

        // A Riimba is commanded by its control station, not by a mechanitor, and vanilla mech
        // control hangs entirely off this one comp: MechanitorUtility.EverControllable is
        // literally "mech.OverseerSubject != null", and Pawn.IsColonyMechPlayerControlled and
        // IsColonyMechRequiringMechanitor both return false without it. Removing it therefore
        // takes the unit out of control groups, out of the bandwidth tally, and out of the
        // "mech has gone feral for want of an overseer" machinery in a single stroke.
        //
        // It has to be removed rather than simply not added, because the comp comes from
        // BaseMechanoidWalker and XML inheritance offers no way to drop a parent's list entry.
        // Doing it in C# also means that if a future version stops putting the comp on that
        // parent, this quietly finds nothing and does nothing, instead of erroring.
        private static void StripOverseerSubject()
        {
            ThingDef riimba = DefDatabase<ThingDef>.GetNamedSilentFail("Riimba");
            if (riimba?.comps == null)
                return;

            int removed = riimba.comps.RemoveAll(c => c is CompProperties_OverseerSubject);

            if (Prefs.DevMode && removed > 0)
                Log.Message($"[Riimba] Removed {removed} overseer subject comp(s) from the Riimba def.");
        }

        private static void AddPrerequisites(string projectName, List<string> missing, params string[] candidates)
        {
            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(projectName);
            if (project == null)
                return;

            foreach (string candidate in candidates)
            {
                ResearchProjectDef prerequisite = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(candidate);
                if (prerequisite == null)
                {
                    missing.Add(candidate);
                    continue;
                }

                // A prerequisite that is itself downstream of this project would deadlock both.
                if (prerequisite == project || DependsOn(prerequisite, project))
                    continue;

                if (project.prerequisites == null)
                    project.prerequisites = new List<ResearchProjectDef>();

                if (!project.prerequisites.Contains(prerequisite))
                    project.prerequisites.Add(prerequisite);
            }
        }

        // Guards against a cycle when another mod has already made one of our candidates depend on
        // one of our own projects - without this the pair would become permanently unresearchable.
        private static bool DependsOn(ResearchProjectDef project, ResearchProjectDef possibleAncestor, int depth = 0)
        {
            if (project?.prerequisites == null || depth > 12)
                return false;

            foreach (ResearchProjectDef prerequisite in project.prerequisites)
            {
                if (prerequisite == possibleAncestor || DependsOn(prerequisite, possibleAncestor, depth + 1))
                    return true;
            }

            return false;
        }

        private static void AddBuildCost(string thingDefName, int count, string label, List<string> missing, params string[] candidates)
        {
            ThingDef building = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (building == null)
                return;

            ThingDef ingredient = ResolveIngredient(label, candidates);

            if (ingredient == null)
            {
                missing.Add(label);
                return;
            }

            if (building.costList == null)
                building.costList = new List<ThingDefCountClass>();

            if (building.costList.Any(c => c.thingDef == ingredient))
                return;

            building.costList.Add(new ThingDefCountClass(ingredient, count));
        }

        // defName first because it is exact and cheap, then the item's visible label as a fallback:
        // a def can be spelled SubcoreBasic or BasicSubcore depending on version, but what the
        // player sees in the build cost is the label, and matching on it survives either spelling.
        private static ThingDef ResolveIngredient(string label, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                ThingDef byName = DefDatabase<ThingDef>.GetNamedSilentFail(candidate);
                if (byName != null)
                    return byName;
            }

            return DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d =>
                d.category == ThingCategory.Item
                && d.label != null
                && d.label.Equals(label, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}
