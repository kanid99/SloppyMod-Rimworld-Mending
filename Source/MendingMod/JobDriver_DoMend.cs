using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Reserves the bench and the whole ingredient queue up front (TryMakePreToilReservations)
    // before any hauling toil runs, so another pawn can never grab a material this job already
    // claimed on the same tick — that's what prevents the classic "two pawns fight over the
    // same steel pile" deadlock. Work duration is computed dynamically from the mend target's
    // missing HP/quality (MendingUtility.GetDynamicWorkAmount) instead of RecipeDef.workAmount.
    public class JobDriver_DoMend : JobDriver
    {
        private const TargetIndex BillGiverInd = TargetIndex.A;
        private const TargetIndex IngredientInd = TargetIndex.B;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.GetTarget(BillGiverInd), job, 1, -1, null, errorOnFailed))
                return false;

            return pawn.ReserveQueue(job.GetTargetQueue(IngredientInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BillGiverInd);
            this.FailOnBurningImmobile(BillGiverInd);
            this.FailOn(() => job.bill == null || job.bill.suspended);

            Thing mendTarget = job.GetTargetQueue(IngredientInd)
                .Select(t => t.Thing)
                .FirstOrDefault(t => t != null && t.def.useHitPoints && t.HitPoints < t.MaxHitPoints);

            int queueCount = job.GetTargetQueue(IngredientInd).Count;
            for (int i = 0; i < queueCount; i++)
            {
                yield return Toils_JobTransforms.ExtractNextTargetFromQueue(IngredientInd);
                yield return Toils_Goto.GotoThing(IngredientInd, PathEndMode.ClosestTouch);
                yield return Toils_Haul.StartCarryThing(IngredientInd);
                yield return Toils_Goto.GotoThing(BillGiverInd, PathEndMode.InteractionCell);
                yield return Toils_Haul.PlaceHauledThingInCell(BillGiverInd, null, false);
            }

            Toil work = new Toil();
            work.initAction = () =>
            {
                float workAmount = mendTarget != null ? Mathf.Max(60f, MendingUtility.GetDynamicWorkAmount(mendTarget)) : 300f;
                work.actor.jobs.curDriver.ticksLeftThisToil = Mathf.RoundToInt(workAmount / pawn.GetStatValue(StatDefOf.GeneralLaborSpeed));
            };
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.WithProgressBarToilDelay(BillGiverInd);
            work.FailOnCannotTouch(BillGiverInd, PathEndMode.InteractionCell);
            yield return work;

            yield return Toils_Recipe.FinishRecipeAndStartStoringProduct();
        }
    }
}
