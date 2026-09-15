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

                // TryBuildMendIngredients reports its own reason - it knows whether the bench
                // found nothing at all or rejected what it found as too advanced.
                if (!TryBuildMendIngredients(bill, pawn, thing, chosen))
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
        private bool TryBuildMendIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen)
        {
            chosen.Clear();

            if (bill.recipe.ingredients.NullOrEmpty())
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
                ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                searchRadius,
                t =>
                {
                    if (t.IsForbidden(pawn) || !pawn.CanReserve(t) || !targetFilter.filter.Allows(t) || !IsDamaged(t))
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

                DevLog(billGiver, $"no reachable damaged item matching bill '{bill.Label}' within radius {searchRadius}");
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
                    DevLog(billGiver, $"found target {mendTarget.LabelShort} but missing material {cost.thingDef.defName} x{cost.count} within radius {searchRadius}");
                    return false;
                }

                chosen.AddRange(found);
            }

            DevLog(billGiver, "job ready: " + MendingUtility.DescribeCost(
                mendTarget, MendingUtility.GetSkillLevel(mendTarget, pawn)));
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
