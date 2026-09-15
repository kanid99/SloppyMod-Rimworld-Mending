using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MendingMod
{
    // Vanilla prerequisites and build-cost ingredients are attached here rather than named in XML.
    // A defName that does not exist in a given install - a different RimWorld version, a missing
    // DLC, another mod that removed or renamed it - would raise a config error every load if it
    // were an XML reference; resolved here it is simply skipped. Nothing this class adds is
    // load-bearing, so a skipped entry costs gating, never stability.
    //
    // Runs at StaticConstructorOnStartup, which is after every def is loaded and resolved.
    // ResearchProjectDef.PrerequisitesCompleted walks the list live on every call (confirmed by
    // decompiling it), so prerequisites added here gate exactly as XML-declared ones would.
    [StaticConstructorOnStartup]
    public static class MendingDefPatches
    {
        static MendingDefPatches()
        {
            List<string> missing = new List<string>();

            // Hand mending is tailoring and smithing applied to worn gear, so it sits behind both.
            AddPrerequisites("BasicMending", missing, "ComplexClothing", "Smithing");

            // A powered bench is a fabrication problem.
            AddPrerequisites("ElectricMending", missing, "Fabrication");

            // A machine that repairs unattended is late-game automation, and needs the mechanoid
            // work to build the control hardware.
            AddPrerequisites("AutomatedMending", missing, "AdvancedFabrication", "BasicMechtech");

            // A machine brain of some kind drives the repair line. The exact def varies by install,
            // so the first of these that exists is used and the rest ignored.
            AddBuildCost("AutomatedMender", 1, missing,
                "SubcoreBasic", "BasicSubcore", "Subcore", "SignalChip", "MachineBrain");

            if (Prefs.DevMode && missing.Count > 0)
            {
                Log.Message("[DynamicMending] Optional defs not present in this install, gating skipped for: "
                    + string.Join(", ", missing));
            }
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

        private static void AddBuildCost(string thingDefName, int count, List<string> missing, params string[] candidates)
        {
            ThingDef building = DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName);
            if (building == null)
                return;

            ThingDef ingredient = candidates
                .Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .FirstOrDefault(d => d != null);

            if (ingredient == null)
            {
                missing.Add(string.Join("/", candidates));
                return;
            }

            if (building.costList == null)
                building.costList = new List<ThingDefCountClass>();

            if (building.costList.Any(c => c.thingDef == ingredient))
                return;

            building.costList.Add(new ThingDefCountClass(ingredient, count));
        }
    }
}
