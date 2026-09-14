using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Subclasses JobDriver_DoBill (rather than JobDriver directly) specifically to reuse its
    // TryMakePreToilReservations and CollectIngredientsToils: those already reserve the bill
    // giver and the whole ingredient queue up front and handle the pathing/hauling toils that
    // get every ingredient (mend target + materials) physically carried to the bench - none of
    // that needed changing. What's deliberately NOT reused is
    // Toils_Recipe.FinishRecipeAndStartStoringProduct: it reads its ingredient list from
    // job.placedThings, which vanilla's Toils_Haul.PlaceHauledThingInCell only populates for a
    // hardcoded whitelist of JobDefs (JobDefOf.DoBill, RecolorApparel, RefuelAtomic,
    // RearmTurretAtomic - confirmed by decompiling the real method) that our own DoMend JobDef
    // isn't part of. Left as-is that means job.placedThings always stays empty: materials get
    // hauled to the bench and dropped there, but nothing ever consumes them and the item never
    // actually gets repaired, even though the job "succeeds" silently. FinishMendToil below
    // does the consumption/repair itself instead, without depending on job.placedThings at all.
    public class JobDriver_DoMend : JobDriver_DoBill
    {
        // BillGiverInd/IngredientInd/IngredientPlaceCellInd (A/B/C) are already declared as
        // protected consts on JobDriver_DoBill itself - reusing those instead of redeclaring.

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BillGiverInd);
            this.FailOnBurningImmobile(BillGiverInd);
            this.FailOn(() => job.bill == null || job.bill.suspended);

            Thing mendTarget = job.GetTargetQueue(IngredientInd)
                .Select(t => t.Thing)
                .FirstOrDefault(t => t != null && t.def.useHitPoints && t.HitPoints < t.MaxHitPoints);

            foreach (Toil toil in CollectIngredientsToils(IngredientInd, BillGiverInd, IngredientPlaceCellInd))
                yield return toil;

            Toil work = new Toil();
            work.initAction = () =>
            {
                float workAmount = mendTarget != null ? Mathf.Max(60f, MendingUtility.GetDynamicWorkAmount(mendTarget)) : 300f;
                work.actor.jobs.curDriver.ticksLeftThisToil = Mathf.RoundToInt(workAmount / pawn.GetStatValue(StatDefOf.GeneralLaborSpeed));
            };
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.WithProgressBarToilDelay(BillGiverInd);
            work.WithEffect(() => job.bill.recipe.effectWorking, BillGiverInd);
            work.PlaySustainerOrSound(() => job.bill.recipe.soundWorking);
            work.activeSkill = () => job.bill.recipe.workSkill;
            yield return work;

            yield return FinishMendToil(mendTarget);
        }

        private Toil FinishMendToil(Thing mendTarget)
        {
            Toil toil = new Toil();
            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                Thing billGiverThing = job.GetTarget(BillGiverInd).Thing;

                if (mendTarget == null || mendTarget.Destroyed)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                ConsumeMaterialsNearBillGiver(billGiverThing, mendTarget);
                RecipeWorker_Mend.CompleteMend(mendTarget, actor, billGiverThing);

                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            return toil;
        }

        // Vanilla's own ingredient-consumption bookkeeping never fires for our JobDef (see class
        // comment above), so instead of trusting job.placedThings we destroy the same ThingDef/
        // count list WorkGiver_Mend used to build this job - MendingUtility.GetDynamicIngredientCosts
        // is a pure function of the mend target's own state, so recomputing it now (same target,
        // same settings) reproduces the identical cost list - by looking for it in the same
        // handful of cells CollectIngredientsToils would have dropped it in: a bill giver's own
        // IngredientStackCells, tried first, with a small radius around its interaction cell as a
        // fallback for overflow (matching Toils_JobTransforms.IngredientPlaceCellsInOrder's own
        // search order).
        private static void ConsumeMaterialsNearBillGiver(Thing billGiverThing, Thing mendTarget)
        {
            List<IntVec3> searchCells = new List<IntVec3>();
            if (billGiverThing is IBillGiver billGiver)
                searchCells.AddRange(billGiver.IngredientStackCells);
            searchCells.AddRange(GenRadial.RadialCellsAround(billGiverThing.InteractionCell, 3f, true));

            foreach (ThingDefCountClass cost in MendingUtility.GetDynamicIngredientCosts(mendTarget))
            {
                int remaining = cost.count;
                foreach (IntVec3 cell in searchCells)
                {
                    if (remaining <= 0)
                        break;

                    if (!cell.InBounds(billGiverThing.Map))
                        continue;

                    foreach (Thing t in cell.GetThingList(billGiverThing.Map).ToList())
                    {
                        if (remaining <= 0)
                            break;

                        if (t.def != cost.thingDef || t == mendTarget)
                            continue;

                        int take = Mathf.Min(remaining, t.stackCount);
                        t.SplitOff(take).Destroy(DestroyMode.Vanish);
                        remaining -= take;
                    }
                }
            }
        }
    }
}
