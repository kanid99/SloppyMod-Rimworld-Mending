using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Assumes a RecipeDef using RecipeWorker_Mend with a single ingredients[0] entry whose
    // filter matches "damaged, useHitPoints things" (the item to mend) — material costs are
    // not declared in XML at all, they come from MendingUtility.GetDynamicIngredientCosts.
    public class WorkGiver_Mend : WorkGiver_DoBill
    {
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            if (!(thing is IBillGiver billGiver) || !ThingIsUsableBillGiver(thing))
            {
                DevLog(thing, "not a usable bill giver");
                return null;
            }

            // A full waste container stops the bench until someone empties it.
            if (thing.TryGetComp<CompMenderWasteBuffer>()?.IsFull == true)
            {
                JobFailReason.Is("DynamicMending.WasteFull".Translate());
                DevLog(thing, "waste container full");
                return null;
            }

            // Covers "the power is out" for the electric table, plus breakdown and refuelling -
            // without it a pawn will happily walk over and mend at a dead bench.
            if (!billGiver.CurrentlyUsableForBills())
            {
                DevLog(thing, "not currently usable for bills (unpowered / broken down?)");
                return null;
            }

            if (!pawn.CanReserve(thing, 1, -1, null, forced) || thing.IsForbidden(pawn) || thing.IsBurning())
            {
                DevLog(thing, "can't reserve / forbidden / burning");
                return null;
            }

            billGiver.BillStack.RemoveIncompletableBills();

            if (billGiver.BillStack.Count == 0)
                DevLog(thing, "bill stack is empty");

            foreach (Bill bill in billGiver.BillStack)
            {
                // IsAssignableFrom rather than exact type equality, so another mod can subclass
                // RecipeWorker_Mend to add its own mend recipe and still be picked up here.
                if (bill.recipe.workerClass == null
                    || !typeof(RecipeWorker_Mend).IsAssignableFrom(bill.recipe.workerClass))
                {
                    if (Prefs.DevMode)
                        DevLog(thing, $"bill '{bill.Label}' workerClass is {bill.recipe.workerClass} not a RecipeWorker_Mend");

                    continue;
                }

                // Vanilla's WorkGiver_DoBill honours this and we did not. Both mend recipes are
                // offered on the same benches but belong to different work types, so without it a
                // colonist allowed only Smithing would pick up apparel bills, and vice versa.
                if (bill.recipe.requiredGiverWorkType != null && bill.recipe.requiredGiverWorkType != def.workType)
                    continue;

                // Also vanilla's, and the reason recipe skillRequirements exist. Neither of our
                // recipes declares any, but a mod patching one in should have it respected.
                SkillRequirement unmet = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
                if (unmet != null)
                {
                    JobFailReason.Is("UnderRequiredSkill".Translate(unmet.minLevel), bill.Label);
                    continue;
                }

                // Both calls are kept, not repeated inside a log string: PawnAllowedToStartAnew
                // sets JobFailReason as a side effect, so calling it twice overwrote the reason
                // with a second evaluation, and it ran even with dev mode off.
                bool shouldDoNow = bill.ShouldDoNow();
                bool pawnAllowed = shouldDoNow && bill.PawnAllowedToStartAnew(pawn);
                if (!shouldDoNow || !pawnAllowed)
                {
                    if (Prefs.DevMode)
                        DevLog(thing, $"bill '{bill.Label}' ShouldDoNow={shouldDoNow} PawnAllowedToStartAnew={pawnAllowed}");

                    continue;
                }

                List<ThingCount> chosen = new List<ThingCount>();

                // TryBuildMendIngredients reports its own reason - it knows whether the bench
                // found nothing at all or rejected what it found as too advanced.
                if (!TryBuildMendIngredients(bill, pawn, thing, chosen, forced))
                    continue;

                Job job = JobMaker.MakeJob(MendingDefOf.DoMend, thing);
                job.bill = bill;
                job.targetQueueB = chosen.Select(tc => new LocalTargetInfo(tc.Thing)).ToList();
                job.countQueue = chosen.Select(tc => tc.Count).ToList();
                return job;
            }

            return null;
        }

        private static void DevLog(Thing thing, string message)
        {
            if (Prefs.DevMode)
                Log.Message($"[DynamicMending] {thing.LabelShort} @ {thing.Position}: {message}");
        }

        // Deliberately not named/shaped like WorkGiver_DoBill's own static
        // TryFindBestBillIngredients(Bill, Pawn, Thing, List<ThingCount>, List<IngredientCount>)
        // - that one searches recipe.ingredients filters, which this recipe doesn't declare
        // material entries for at all (the C# computes them dynamically instead).
        // Vanilla's own throttle for exactly this, which this work giver never used because it
        // replaced WorkGiver_DoBill.StartOrResumeBillJob wholesale. A bill with nothing to repair
        // is the normal resting state and also the most expensive one: ClosestThingReachable runs
        // a 30-region BFS and then, finding nothing, falls back to scanning the WHOLE group
        // globally - re-run for every bill on every pawn's work scan. Vanilla stamps a failed
        // bill with nextTickToSearchForIngredients and leaves it alone for 500-600 ticks; the
        // field is public, saves with the game, and a float-menu query bypasses it so right-click
        // never lies to the player.
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private bool TryBuildMendIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen, bool forced)
        {
            chosen.Clear();

            if (bill.recipe.ingredients.NullOrEmpty())
                return false;

            bool interactive = forced || FloatMenuMakerMap.makingFor == pawn;
            if (!interactive && Find.TickManager.TicksGame <= bill.nextTickToSearchForIngredients)
                return false;

            // GenRadial's cell lookup table is capped at radius 200 (confirmed by decompiling
            // GenRadial.NumCellsInRadius) - anything above that logs a real vanilla Log.Error
            // every time it's hit. bill.ingredientSearchRadius defaults to 999 ("unlimited" in
            // the bill's own UI), so it has to be clamped before being handed to either
            // GenClosest.ClosestThingReachable or GenRadial.RadialDistinctThingsAround below.
            float searchRadius = Mathf.Min(bill.ingredientSearchRadius, 200f);

            IngredientCount targetFilter = bill.recipe.ingredients[0];

            // Tracked so a bench that rejected everything on tech level can say so, rather than
            // reporting the same "nothing in range" as an empty stockpile would. Components are
            // tracked separately because "needs a powered bench" is a different fix for the player
            // than "this is beyond what hand tools reach".
            bool rejectedForTech = false;
            bool rejectedForComponents = false;

            Thing mendTarget = GenClosest.ClosestThingReachable(
                billGiver.Position,
                billGiver.Map,
                ThingRequest.ForGroup(TargetGroupFor(bill.recipe)),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                searchRadius,
                t =>
                {
                    // Ordered cheapest and most selective first: almost nothing on the map is
                    // damaged gear, so reject on that before paying for a reservation lookup.
                    if (!IsDamaged(t) || !targetFilter.filter.Allows(t))
                        return false;

                    if (t.IsForbidden(pawn) || !pawn.CanReserve(t))
                        return false;

                    if (!MendingUtility.CanBenchRepair(billGiver.def, t))
                    {
                        MendingTechLimitExtension limits = billGiver.def.GetModExtension<MendingTechLimitExtension>();
                        if (limits != null && !limits.allowComponents && MendingUtility.NeedsComponents(t))
                            rejectedForComponents = true;
                        else
                            rejectedForTech = true;

                        return false;
                    }

                    return true;
                });

            if (mendTarget == null)
            {
                if (rejectedForComponents)
                {
                    JobFailReason.Is("DynamicMending.NeedsPoweredBench".Translate());
                }
                else if (rejectedForTech)
                {
                    TechLevel cap = billGiver.def.GetModExtension<MendingTechLimitExtension>().maxTechLevel;
                    JobFailReason.Is("DynamicMending.TooAdvancedForBench".Translate(cap.ToStringHuman()));
                }
                else
                {
                    JobFailReason.Is("DynamicMending.NoIngredientsOrTarget".Translate());
                }

                if (Prefs.DevMode)
                    DevLog(billGiver, $"no reachable damaged item matching bill '{bill.Label}' within radius {searchRadius}");

                if (!interactive)
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;

                return false;
            }

            chosen.Add(new ThingCount(mendTarget, 1));

            // Two-pass verification: every material cost is located (read-only) before any of
            // it is added to `chosen`. Nothing is reserved or consumed until JobOnThing returns
            // a fully-populated job, so a failed search here leaves no partial state behind.
            foreach (ThingDefCountClass cost in MendingUtility.GetDynamicIngredientCosts(mendTarget, MendingUtility.GetSkillLevel(mendTarget, pawn)))
            {
                List<ThingCount> found = new List<ThingCount>();
                if (!TryFindMaterial(cost.thingDef, cost.count, pawn, billGiver, searchRadius, found))
                {
                    if (Prefs.DevMode)
                        DevLog(billGiver, $"found target {mendTarget.LabelShort} but missing material {cost.thingDef.defName} x{cost.count} within radius {searchRadius}");

                    if (!interactive)
                        bill.nextTickToSearchForIngredients =
                            Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;

                    return false;
                }

                chosen.AddRange(found);
            }

            // DescribeCost recomputes the whole cost breakdown and allocates its way through two
            // LINQ projections; without this guard it ran on every job created, with the result
            // thrown away unless dev mode was on.
            if (Prefs.DevMode)
            {
                DevLog(billGiver, "job ready: " + MendingUtility.DescribeCost(
                    mendTarget, MendingUtility.GetSkillLevel(mendTarget, pawn)));
            }

            return true;
        }

        // HaulableEver is every wood log, meal, chunk and steel bar on the map - thousands of
        // things on a developed colony, walked per bill, per bench, per pawn work scan. A mend
        // bill only ever wants apparel or a weapon, and both are region-indexed groups
        // (ThingRequestGroupUtility.StoreInRegion), so the search can be an order of magnitude
        // smaller AND still use the region traversal rather than a global sweep.
        private static readonly Dictionary<RecipeDef, ThingRequestGroup> targetGroupCache =
            new Dictionary<RecipeDef, ThingRequestGroup>();

        private static ThingRequestGroup TargetGroupFor(RecipeDef recipe)
        {
            if (targetGroupCache.TryGetValue(recipe, out ThingRequestGroup cached))
                return cached;

            bool anyApparel = false;
            bool anyWeapon = false;

            foreach (ThingDef def in recipe.fixedIngredientFilter.AllowedThingDefs)
            {
                if (def.IsApparel)
                    anyApparel = true;
                else if (def.IsWeapon)
                    anyWeapon = true;

                if (anyApparel && anyWeapon)
                    break;
            }

            // A filter that spans both, or matches neither, falls back to the broad group rather
            // than quietly missing valid targets.
            ThingRequestGroup group =
                anyApparel && !anyWeapon ? ThingRequestGroup.Apparel :
                anyWeapon && !anyApparel ? ThingRequestGroup.Weapon :
                ThingRequestGroup.HaulableEver;

            targetGroupCache[recipe] = group;
            return group;
        }

        private static bool IsDamaged(Thing t)
        {
            return t.def.useHitPoints && t.HitPoints < t.MaxHitPoints;
        }

        // Walks the map's index of this material instead of every cell around the bench.
        // GenRadial.RadialDistinctThingsAround, which this used, sweeps the whole disc: at the
        // bill's default "unlimited" radius that is the radius-200 clamp, about 125,000 cells -
        // per material, per bill, per scan, and the full sweep runs every time whenever the
        // colony is short of a material, because the search can only fail after visiting all of
        // them. ThingsOfDef visits the handful of stacks that actually exist.
        private static bool TryFindMaterial(ThingDef materialDef, int countNeeded, Pawn pawn, Thing billGiver, float searchRadius, List<ThingCount> found)
        {
            List<Thing> stacks = billGiver.Map.listerThings.ThingsOfDef(materialDef);
            if (stacks.NullOrEmpty())
                return false;

            float radiusSquared = searchRadius * searchRadius;
            IntVec3 origin = billGiver.Position;

            // Nearest first, matching what the radial sweep gave for free. The candidate list is
            // the stacks of one material on the map, so this is a short sort.
            List<Thing> reachable = new List<Thing>();
            foreach (Thing candidate in stacks)
            {
                if ((candidate.Position - origin).LengthHorizontalSquared > radiusSquared)
                    continue;

                if (candidate.IsForbidden(pawn) || !pawn.CanReserve(candidate))
                    continue;

                reachable.Add(candidate);
            }

            reachable.Sort((a, b) =>
                (a.Position - origin).LengthHorizontalSquared.CompareTo(
                (b.Position - origin).LengthHorizontalSquared));

            int remaining = countNeeded;
            foreach (Thing candidate in reachable)
            {
                if (remaining <= 0)
                    break;

                int take = Mathf.Min(remaining, candidate.stackCount);
                found.Add(new ThingCount(candidate, take));
                remaining -= take;
            }

            return remaining <= 0;
        }
    }
}
