using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RiimbaMod
{
    // Drive to this unit's own bay, hand the bin over, and sit there charging.
    //
    // TargetA is the station and TargetB the bay cell, rather than the other way round, so the
    // job's report string names the building the player can see rather than a bare coordinate.
    public class JobDriver_RiimbaDock : JobDriver
    {
        private Building_RiimbaStation Station => job.GetTarget(TargetIndex.A).Thing as Building_RiimbaStation;

        private CompRiimbaUnit Unit => pawn.GetComp<CompRiimbaUnit>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            // Nothing to reserve. Bays are handed out by roster position, not claimed, so two
            // units can never be sent to the same one - see Building_RiimbaStation.BayCellFor.
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.EndOnDespawnedOrNull(TargetIndex.A);

            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            Toil dock = ToilMaker.MakeToil("RiimbaDock");
            dock.initAction = delegate
            {
                Unit?.EmptyBinInto(Station?.WasteBuffer);
            };
            dock.tickIntervalAction = delegate (int delta)
            {
                CompRiimbaUnit unit = Unit;
                Building_RiimbaStation station = Station;

                if (unit == null || station == null)
                {
                    ReadyForNextToil();
                    return;
                }

                // Retried rather than done once on arrival: the station may have been backed up
                // when the unit docked - output spot blocked, buffer at its cap - and clear a
                // moment later. Without the retry the unit would charge to full and drive off
                // still carrying a full bin, then immediately turn round and come back.
                //
                // Not gated behind an IsHashIntervalTick: tickIntervalAction is already called
                // at a throttled rate with the elapsed delta, and a hash interval nested inside
                // one can go unsatisfied indefinitely. With an empty bin this returns at once.
                unit.EmptyBinInto(station.WasteBuffer);

                // Charging is the station's job, not this toil's - CompRiimbaUnit does the
                // arithmetic because it owns the charge. This only decides when to leave.
                bool charged = unit.Charge >= 1f || !station.PowerOn;
                if (charged && unit.BinEmpty)
                    ReadyForNextToil();
            };
            dock.defaultCompleteMode = ToilCompleteMode.Never;
            dock.handlingFacing = false;

            yield return dock;
        }
    }

    // Walk to a station and sign on. One toil of actual work; the interest is all in the
    // re-checks, because the walk takes time and the slot may be gone by the time it arrives.
    public class JobDriver_RiimbaSignOn : JobDriver
    {
        private Building_RiimbaStation Station => job.GetTarget(TargetIndex.A).Thing as Building_RiimbaStation;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.EndOnDespawnedOrNull(TargetIndex.A);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil signOn = ToilMaker.MakeToil("RiimbaSignOn");
            signOn.initAction = delegate
            {
                Building_RiimbaStation station = Station;
                CompRiimbaUnit unit = pawn.GetComp<CompRiimbaUnit>();

                if (station == null || unit == null)
                    return;

                // Another unit may have taken the last slot during the walk over. Failing
                // quietly is right: the think tree will look for another station on the next
                // pass, and there is nothing here worth interrupting the player about.
                if (!station.HasFreeSlot)
                    return;

                unit.AssignStation(station);
            };
            signOn.defaultCompleteMode = ToilCompleteMode.Instant;

            yield return signOn;
        }
    }
}
