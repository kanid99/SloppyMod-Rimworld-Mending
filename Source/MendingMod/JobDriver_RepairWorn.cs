using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // "Repair my worn jacket": take it off, hand it to the ordinary repair job, put it back on.
    //
    // This driver only does the first part. A repair job cannot be built for gear on a pawn's
    // back - it targets things on the map - so the item is dropped here and the job is built
    // AFTER, by the same MendJobMaker a right-click on the floor uses. Everything that job does
    // (collecting materials, working, the waste, the result) is then exactly what it always is.
    //
    // The repair and the re-equip are queued behind this job rather than run inside it, so each
    // is a normal job the player can see in the queue and cancel like any other.
    public class JobDriver_RepairWorn : JobDriver
    {
        private Thing Gear => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        // Set the moment the gear leaves the pawn. The fail condition below is a GLOBAL one and
        // is re-checked when the driver advances past the last toil - without this it would see
        // the gear no longer held (because we just dropped it) and end the job as incompletable,
        // which is not how a job that did exactly what it was for should end.
        private bool handedOff;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref handedOff, "handedOff");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !handedOff && (Gear == null || !MendJobMaker.HeldBy(pawn, Gear)));

            // Vanilla's own taking-off time for apparel; a weapon is simply let go.
            Toil takeOff = Toils_General.Wait(TakeOffTicks(), TargetIndex.None);
            takeOff.WithProgressBarToilDelay(TargetIndex.None);
            yield return takeOff;

            Toil handOff = ToilMaker.MakeToil("RepairWornHandOff");
            handOff.initAction = HandOff;
            handOff.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return handOff;
        }

        private int TakeOffTicks()
        {
            return Gear is Apparel apparel
                ? (int)(apparel.GetStatValue(StatDefOf.EquipDelay) * GenTicks.TicksPerRealSecond)
                : 30;
        }

        private void HandOff()
        {
            Thing dropped = Drop(Gear);
            if (dropped == null)
                return;

            handedOff = true;

            Job putBack = dropped is Apparel
                ? JobMaker.MakeJob(JobDefOf.Wear, dropped)
                : JobMaker.MakeJob(JobDefOf.Equip, dropped);
            putBack.playerForced = true;

            // Asked again now that it is on the floor. The float menu already said yes, but that
            // was before the time spent taking it off - long enough for a bench to lose power or
            // the last of the steel to be hauled away - and a stale yes would leave them standing
            // there in their underwear with nothing queued.
            if (!MendJobMaker.TryMakeRepairJob(pawn, dropped, true, out Job mend, out string why))
            {
                Messages.Message("DynamicMending.CannotRepair".Translate(dropped.LabelShort) + ": " + why,
                                 dropped, MessageTypeDefOf.RejectInput, false);
                pawn.jobs.jobQueue.EnqueueFirst(putBack, JobTag.Misc);
                return;
            }

            mend.playerForced = true;

            // EnqueueFirst, so in reverse: the repair runs next, the re-equip after it.
            pawn.jobs.jobQueue.EnqueueFirst(putBack, JobTag.Misc);
            pawn.jobs.jobQueue.EnqueueFirst(mend, JobTag.Misc);
        }

        private Thing Drop(Thing gear)
        {
            IntVec3 at = pawn.PositionHeld;

            if (gear is Apparel apparel)
                return pawn.apparel.TryDrop(apparel, out Apparel result, at, false) ? result : null;

            if (gear is ThingWithComps weapon && pawn.equipment.Contains(weapon))
                return pawn.equipment.TryDropEquipment(weapon, out ThingWithComps result, at, false) ? result : null;

            return null;
        }
    }
}
