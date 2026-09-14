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
                return null;

            if (!pawn.CanReserve(thing, 1, -1, null, forced) || thing.IsForbidden(pawn) || thing.IsBurning())
                return null;

            billGiver.BillStack.RemoveIncompletableBills();

            foreach (Bill bill in billGiver.BillStack)
            {
                if (bill.recipe.workerClass != typeof(RecipeWorker_Mend))
                    continue;

                if (!bill.ShouldDoNow() || !bill.PawnAllowedToStartAnew(pawn))
                    continue;

                List<ThingCount> chosen = new List<ThingCount>();
                if (!TryFindBestBillIngredients(bill, pawn, thing, chosen))
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

        // Hides, rather than overrides, WorkGiver_DoBill's static TryFindBestBillIngredients
        // (C# cannot override a static member) — JobOnThing above calls this version directly,
        // so the vanilla static helper is simply never consulted for this WorkGiver.
        protected new bool TryFindBestBillIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
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
                return false;

            chosen.Add(new ThingCount(mendTarget, 1));

            // Two-pass verification: every material cost is located (read-only) before any of
            // it is added to `chosen`. Nothing is reserved or consumed until JobOnThing returns
            // a fully-populated job, so a failed search here leaves no partial state behind.
            foreach (ThingDefCountClass cost in MendingUtility.GetDynamicIngredientCosts(mendTarget))
            {
                List<ThingCount> found = new List<ThingCount>();
                if (!TryFindMaterial(cost.thingDef, cost.count, pawn, billGiver, bill.ingredientSearchRadius, found))
                    return false;

                chosen.AddRange(found);
            }

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
