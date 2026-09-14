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
    // giver and the whole ingredient queue up front and handle the haul-to-bench toils, which
    // is what prevents a second pawn from grabbing a material this job already claimed. Only
    // the actual "do work" toil is replaced, so its duration can scale with the mend target's
    // missing HP/quality (MendingUtility.GetDynamicWorkAmount) instead of RecipeDef.workAmount.
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
            yield return work;

            yield return Toils_Recipe.FinishRecipeAndStartStoringProduct();
        }
    }
}
