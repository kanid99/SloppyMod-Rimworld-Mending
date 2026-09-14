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
                if (bill.recipe.workerClass != typeof(RecipeWorker_Mend))
                {
                    DevLog(thing, $"bill '{bill.Label}' workerClass is {bill.recipe.workerClass} not RecipeWorker_Mend");
                    continue;
                }

                if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                {
                    DevLog(thing, $"bill '{bill.Label}' ShouldDoNow={bill.ShouldDoNow()} PawnAllowedToStartAnew={bill.PawnAllowedToStartAnew(pawn)}");
                    continue;
                }

                List<ThingCount> chosen = new List<ThingCount>();
                if (!TryBuildMendIngredients(bill, pawn, thing, chosen))
                {
                    JobFailReason.Is("DynamicMending.NoIngredientsOrTarget".Translate());
                    continue;
                }

                Job job = JobMaker.MakeJob(MendingDefOf.DoMend, thing);
                job.bill = bill;
                job.targetQueueB = chosen.Select(tc => new LocalTargetInfo(tc.Thing)).ToList();
                job.countQueue = chosen.Select(tc => tc.Count).ToList();
                job.haulMode = HaulMode.ToCellStorage;
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
        private bool TryBuildMendIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            chosen.Clear();

            if (bill.recipe.ingredients.NullOrEmpty())
                return false;

            IngredientCount targetFilter = bill.recipe.ingredients[0];
            Thing mendTarget = GenClosest.ClosestThingReachable(
                billGiver.Position,
                billGiver.Map,
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                bill.ingredientSearchRadius,
                t => !t.IsForbidden(pawn) && pawn.CanReserve(t) && targetFilter.filter.Allows(t) && IsDamaged(t));

            if (mendTarget == null)
            {
                DevLog(billGiver, $"no reachable damaged item matching bill '{bill.Label}' within radius {bill.ingredientSearchRadius}");
                return false;
            }

            chosen.Add(new ThingCount(mendTarget, 1));

            // Two-pass verification: every material cost is located (read-only) before any of
            // it is added to `chosen`. Nothing is reserved or consumed until JobOnThing returns
            // a fully-populated job, so a failed search here leaves no partial state behind.
            foreach (ThingDefCountClass cost in MendingUtility.GetDynamicIngredientCosts(mendTarget))
            {
                List<ThingCount> found = new List<ThingCount>();
                if (!TryFindMaterial(cost.thingDef, cost.count, pawn, billGiver, bill.ingredientSearchRadius, found))
                {
                    DevLog(billGiver, $"found target {mendTarget.LabelShort} but missing material {cost.thingDef.defName} x{cost.count} within radius {bill.ingredientSearchRadius}");
                    return false;
                }

                chosen.AddRange(found);
            }

            DevLog(billGiver, $"job ready: mend {mendTarget.LabelShort}");
            return true;
        }

        private static bool IsDamaged(Thing t)
        {
            return t.def.useHitPoints && t.HitPoints < t.MaxHitPoints;
        }

        private static bool TryFindMaterial(ThingDef materialDef, int countNeeded, Pawn pawn, Thing billGiver, float searchRadius, List<ThingCount> found)
        {
            int remaining = countNeeded;

            foreach (Thing candidate in GenRadial.RadialDistinctThingsAround(billGiver.Position, billGiver.Map, searchRadius, true))
            {
                if (remaining <= 0)
                    break;

                if (candidate.def != materialDef)
                    continue;

                if (candidate.IsForbidden(pawn) || !pawn.CanReserve(candidate))
                    continue;

                int take = Mathf.Min(remaining, candidate.stackCount);
                found.Add(new ThingCount(candidate, take));
                remaining -= take;
            }

            return remaining <= 0;
        }
    }
}
