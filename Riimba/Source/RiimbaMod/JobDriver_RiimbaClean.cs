using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RiimbaMod
{
    // The same shape as vanilla's JobDriver_CleanFilth - a queue of targets, a goto, and a toil
    // that grinds thickness off one mess before jumping back for the next - with two additions:
    // what comes off the floor goes into the unit's bin, and the unit stops if its station stops
    // being able to command it.
    //
    // Not a subclass of the vanilla driver. Its cleaning toil is a local closure over private
    // fields with no hook anywhere in it, so there is nothing to override; reimplementing the
    // loop is the only way to put the collection step inside it.
    public class JobDriver_RiimbaClean : JobDriver
    {
        // Bin units added per point of thickness removed. At the default bin capacity of 6 this
        // is about eighteen points of filth per full bin, or six waste items - roughly a large
        // room's worth of dirt per trip back.
        private const float BinUnitsPerThickness = 0.34f;

        private float cleaningWorkDone;
        private float totalCleaningWorkDone;
        private float totalCleaningWorkRequired;

        private Filth Filth => (Filth)job.GetTarget(TargetIndex.A).Thing;

        private CompRiimbaUnit Unit => pawn.GetComp<CompRiimbaUnit>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // As many as it can get. The queue was built from what was reservable at the time,
            // but another unit may have taken some of it since.
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(TargetIndex.A), job);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Ends the job the moment the station can no longer command this unit - unpowered,
            // destroyed, or the unit signed off. Without it a unit would keep scrubbing on a
            // dead station, which is the one thing the whole design says it must not do.
            AddEndCondition(() => (Unit != null && Unit.CanWorkNow)
                ? JobCondition.Ongoing
                : JobCondition.Incompletable);

            Toil initExtractTargetFromQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(TargetIndex.A);
            yield return initExtractTargetFromQueue;
            yield return Toils_JobTransforms.SucceedOnNoTargetInQueue(TargetIndex.A);
            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
                .JumpIfDespawnedOrNullOrForbidden(TargetIndex.A, initExtractTargetFromQueue)
                .JumpIfOutsideHomeArea(TargetIndex.A, initExtractTargetFromQueue);

            Toil clean = ToilMaker.MakeToil("MakeNewToils");
            clean.initAction = delegate
            {
                cleaningWorkDone = 0f;
                totalCleaningWorkDone = 0f;
                totalCleaningWorkRequired = Filth.def.filth.cleaningWorkToReduceThickness * Filth.thickness;
            };
            clean.tickIntervalAction = delegate (int delta)
            {
                Filth filth = Filth;
                if (filth == null || filth.Destroyed)
                {
                    ReadyForNextToil();
                    return;
                }

                // Terrain slows cleaning the same way it does for a pawn - a drone on carpet is
                // no faster than a colonist on carpet.
                float terrainFactor = filth.Position.GetTerrain(filth.Map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
                float work = pawn.GetStatValue(StatDefOf.CleaningSpeed) * delta;
                if (terrainFactor != 0f)
                    work /= terrainFactor;

                cleaningWorkDone += work;
                totalCleaningWorkDone += work;

                if (cleaningWorkDone <= filth.def.filth.cleaningWorkToReduceThickness)
                    return;

                // Collect BEFORE thinning: once thickness hits zero the filth is destroyed and
                // its def is no longer reachable through the target.
                Unit?.AddCollected(filth.def, BinUnitsPerThickness);

                filth.ThinFilth();
                cleaningWorkDone = 0f;

                if (filth.Destroyed)
                {
                    clean.actor.records.Increment(RecordDefOf.MessesCleaned);
                    ReadyForNextToil();
                }
            };
            clean.defaultCompleteMode = ToilCompleteMode.Never;
            clean.WithEffect(EffecterDefOf.Clean, TargetIndex.A);
            clean.WithProgressBar(TargetIndex.A, () => totalCleaningWorkDone / totalCleaningWorkRequired, interpolateBetweenActorAndTarget: true);
            clean.PlaySustainerOrSound(delegate
            {
                ThingDef def = Filth.def;
                return (!def.filth.cleaningSound.NullOrUndefined()) ? def.filth.cleaningSound : SoundDefOf.Interact_CleanFilth;
            });
            clean.JumpIfDespawnedOrNullOrForbidden(TargetIndex.A, initExtractTargetFromQueue);
            clean.JumpIfOutsideHomeArea(TargetIndex.A, initExtractTargetFromQueue);
            clean.JumpIf(() => clean.actor.jobs.curJob.GetTarget(TargetIndex.A).Thing?.Destroyed ?? false, initExtractTargetFromQueue);
            yield return clean;

            yield return Toils_Jump.Jump(initExtractTargetFromQueue);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cleaningWorkDone, "cleaningWorkDone", 0f);
            Scribe_Values.Look(ref totalCleaningWorkDone, "totalCleaningWorkDone", 0f);
            Scribe_Values.Look(ref totalCleaningWorkRequired, "totalCleaningWorkRequired", 0f);
        }
    }
}
