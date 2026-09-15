using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // A full mending building stops taking repairs until its waste is cleared. This puts that
    // clearing on the normal hauling queue so it happens without micromanagement; the gizmo on
    // the building is the manual alternative.
    public class WorkGiver_EmptyMenderWaste : WorkGiver_Scanner
    {
        private static List<ThingDef> wasteBufferDefs;

        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        // JobGiver_Work prefers this over the map-wide PotentialWorkThingRequest (confirmed by
        // decompiling it), so every pawn's work scan walks a handful of mending buildings rather
        // than every artificial building on the map.
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (wasteBufferDefs == null)
            {
                wasteBufferDefs = DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(d => d.comps != null && d.comps.Any(c => c is CompProperties_MenderWasteBuffer))
                    .ToList();
            }

            foreach (ThingDef def in wasteBufferDefs)
            {
                foreach (Thing thing in pawn.Map.listerThings.ThingsOfDef(def))
                    yield return thing;
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return !MendingModMain.Settings.generateWaste;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building building) || building.TryGetComp<CompMenderWasteBuffer>()?.IsFull != true)
                return false;

            if (t.IsForbidden(pawn) || t.IsBurning())
                return false;

            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return HasJobOnThing(pawn, t, forced)
                ? JobMaker.MakeJob(MendingDefOf.EmptyMenderWaste, t)
                : null;
        }
    }

    public class JobDriver_EmptyMenderWaste : JobDriver
    {
        private const TargetIndex BuildingInd = TargetIndex.A;
        private const int EmptyTicks = 180;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(BuildingInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(BuildingInd);
            this.FailOnBurningImmobile(BuildingInd);
            // Someone else may have emptied it, or the player may have hit the gizmo, between the
            // job being handed out and the pawn arriving.
            this.FailOn(() => Buffer?.IsFull != true);

            yield return Toils_Goto.GotoThing(BuildingInd, PathEndMode.Touch);

            Toil empty = new Toil
            {
                defaultDuration = EmptyTicks,
                defaultCompleteMode = ToilCompleteMode.Delay,
            };
            empty.WithProgressBarToilDelay(BuildingInd);
            empty.AddFinishAction(() => Buffer?.DumpBufferedWaste());
            yield return empty;
        }

        private CompMenderWasteBuffer Buffer =>
            job.GetTarget(BuildingInd).Thing?.TryGetComp<CompMenderWasteBuffer>();
    }
}
