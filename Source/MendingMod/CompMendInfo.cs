using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace MendingMod
{
    public class CompProperties_MendInfo : CompProperties
    {
        public CompProperties_MendInfo()
        {
            compClass = typeof(CompMendInfo);
        }
    }

    // What a repair would cost, shown in the inspect pane of damaged gear. The same numbers the
    // work giver charges - it calls the same MendingUtility methods - so the preview cannot drift
    // from the bill.
    //
    // Priced at the best crafter the colony has on that map, because that is who the player is
    // weighing it against; a flat "skill 10" would mislead a colony of novices in both directions.
    // Carries no data, so it adds nothing to a save and leaves nothing behind if the mod goes.
    public class CompMendInfo : ThingComp
    {
        private const int RecheckTicks = 250;

        private string cached;
        private int cachedHitPoints = -1;
        private int cachedTick = -99999;

        public override string CompInspectStringExtra()
        {
            if (!MendJobMaker.IsMendable(parent))
                return null;

            // An inspect string is rebuilt every frame the item is selected. The cost maths walks
            // the cost list and the colony's research, so it is kept until the item's condition
            // changes or a few seconds pass - skills and research move slowly.
            int now = Find.TickManager.TicksGame;
            if (cached != null && cachedHitPoints == parent.HitPoints && now - cachedTick < RecheckTicks)
                return cached;

            cached = Build();
            cachedHitPoints = parent.HitPoints;
            cachedTick = now;
            return cached;
        }

        private string Build()
        {
            int skill = BestCrafterSkill(parent.MapHeld);
            StringBuilder sb = new StringBuilder();

            if (!MendingModMain.Settings.requireResources)
            {
                sb.Append("DynamicMending.InfoCostFree".Translate());
            }
            else
            {
                List<ThingDefCountClass> costs = MendingUtility.GetDynamicIngredientCosts(parent, skill);
                string list = costs.NullOrEmpty()
                    ? "DynamicMending.InfoNothing".Translate().ToString()
                    : costs.Select(c => c.count + " " + c.thingDef.label).ToCommaList();

                sb.Append("DynamicMending.InfoCost".Translate(list, skill));
            }

            if (MendingUtility.IsUnfamiliar(parent))
                sb.Append("\n").Append("DynamicMending.InfoUnfamiliar".Translate());

            if (!MendingUtility.BelowAutoRepairThreshold(parent))
            {
                sb.Append("\n").Append("DynamicMending.InfoAboveThreshold".Translate(
                    MendingModMain.Settings.autoRepairBelow.ToStringPercent()));
            }

            return sb.ToString();
        }

        private static int BestCrafterSkill(Map map)
        {
            int best = -1;
            if (map != null)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.skills == null || pawn.WorkTagIsDisabled(WorkTags.Crafting))
                        continue;

                    int level = MendingUtility.GetSkillLevel(null, pawn);
                    if (level > best)
                        best = level;
                }
            }

            // No colonists here - a caravan's pack, an item on a map nobody is on. Price it at the
            // middle of the range rather than at zero, which would show the worst case as if it
            // were the normal one.
            return best < 0 ? 10 : best;
        }
    }

    // Attaches CompMendInfo to every def a mend recipe can take. Done here rather than by
    // patching XML so modded gear is covered without naming it: the test is the recipes' own
    // ingredient filter, the same one that decides what can be mended at all.
    [StaticConstructorOnStartup]
    public static class MendInfoInjector
    {
        static MendInfoInjector()
        {
            List<ThingFilter> filters = DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(r => r.workerClass != null
                            && typeof(RecipeWorker_Mend).IsAssignableFrom(r.workerClass)
                            && !r.ingredients.NullOrEmpty())
                .Select(r => r.ingredients[0].filter)
                .ToList();

            int added = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!def.useHitPoints || !typeof(ThingWithComps).IsAssignableFrom(def.thingClass))
                    continue;

                if (!filters.Any(f => f.Allows(def)))
                    continue;

                if (def.comps.Any(c => c is CompProperties_MendInfo))
                    continue;

                def.comps.Add(new CompProperties_MendInfo());
                added++;
            }

            if (Prefs.DevMode)
                Log.Message($"[DynamicMending] repair cost preview attached to {added} defs");
        }
    }
}
