using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace RiimbaMod
{
    // Shared checks, so the four nodes below cannot disagree about what a working unit is.
    internal static class RiimbaAI
    {
        // Vanilla's WorkGiver_CleanFilth ignores filth younger than this, so pawns do not chase
        // a puddle that is still spreading. A drone has no more business doing it than a pawn.
        public const int MinTicksSinceThickened = 600;

        public static CompRiimbaUnit UnitComp(Pawn pawn)
        {
            return pawn?.GetComp<CompRiimbaUnit>();
        }

        // A unit that is downed, dead or held in something is not taking orders. Checked in every
        // node rather than once at the root, because a think tree has no root condition and a
        // downed pawn handed a Goto job is an error every time it is asked to think.
        public static bool Operable(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && !pawn.Dead
                && !pawn.Downed
                && pawn.Map != null;
        }
    }

    // Sends a unit back to its bay. Listed TWICE in the think tree: once above the cleaning node
    // with idleReturn off, for the urgent cases that should interrupt work, and once below it
    // with idleReturn on, so a unit with nothing left to clean parks and tops up instead of
    // standing in the middle of the room.
    //
    // One class with a flag rather than two classes, because the arrival behaviour - hand over
    // the bin, then charge - is identical and duplicating it is how the two would drift.
    public class JobGiver_RiimbaDock : ThinkNode_JobGiver
    {
        public bool idleReturn;

        // ThinkNode.DeepCopy builds a fresh instance and copies only the fields it knows about,
        // so a custom one has to be carried across by hand or the copy silently reverts to the
        // default - which here would turn the idle node into a second urgent node.
        public override ThinkNode DeepCopy(bool resolve = true)
        {
            JobGiver_RiimbaDock copy = (JobGiver_RiimbaDock)base.DeepCopy(resolve);
            copy.idleReturn = idleReturn;
            return copy;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!RiimbaAI.Operable(pawn))
                return null;

            CompRiimbaUnit comp = RiimbaAI.UnitComp(pawn);
            Building_RiimbaStation station = comp?.Station;

            if (station == null || !station.Spawned || station.Map != pawn.Map)
                return null;

            IntVec3 bay = station.BayCellFor(pawn);
            if (!bay.IsValid || !bay.InBounds(pawn.Map))
                return null;

            // Already parked. Returning null lets the idle node take over, which keeps the unit
            // sitting on the bay rather than re-issuing a dock job every time it thinks.
            if (pawn.Position == bay && !NeedsDocking(comp))
                return null;

            if (!idleReturn && !NeedsDocking(comp))
                return null;

            // A flat unit cannot drive anywhere. It waits for the station's induction trickle to
            // give it enough to move - see CompRiimbaUnit.IsOnTrickle.
            if (comp.OutOfCharge)
                return null;

            if (!pawn.CanReach(bay, PathEndMode.OnCell, Danger.Deadly))
                return null;

            // A is the station and B the bay cell - see JobDriver_RiimbaDock for why that
            // order and not the other one.
            Job job = JobMaker.MakeJob(RiimbaDefOf.Riimba_Dock, station, bay);

            // A safety net, not the normal exit: the docking toil ends itself once the unit is
            // charged and empty. This is what gets a unit thinking again if its station loses
            // power while it sits there.
            job.expiryInterval = 2500;

            return job;
        }

        private static bool NeedsDocking(CompRiimbaUnit comp)
        {
            return comp.NeedsCharge || comp.BinFull;
        }
    }

    // How a freshly gestated unit gets from the gestator to somewhere useful without the player
    // having to escort it. Picks the nearest station with a free slot and walks there.
    public class JobGiver_RiimbaSeekStation : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!RiimbaAI.Operable(pawn))
                return null;

            CompRiimbaUnit comp = RiimbaAI.UnitComp(pawn);
            if (comp == null || comp.Station != null || comp.OutOfCharge)
                return null;

            Building_RiimbaStation best = null;
            float bestDistance = float.MaxValue;

            // ThingsOfDef rather than a full thing scan: this runs every time an unsigned unit
            // thinks, and there are only ever a handful of stations on a map.
            foreach (Thing thing in pawn.Map.listerThings.ThingsOfDef(RiimbaDefOf.RiimbaStation))
            {
                if (!(thing is Building_RiimbaStation station) || !station.HasFreeSlot)
                    continue;

                if (!pawn.CanReach(station, PathEndMode.Touch, Danger.Deadly))
                    continue;

                float distance = pawn.Position.DistanceToSquared(station.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = station;
                }
            }

            if (best == null)
                return null;

            return JobMaker.MakeJob(RiimbaDefOf.Riimba_SignOn, best);
        }
    }

    // The actual work. Finds the nearest mess this unit is allowed to touch and queues the ones
    // around it, the same way vanilla's cleaning work giver batches a room.
    public class JobGiver_RiimbaClean : ThinkNode_JobGiver
    {
        // How many messes to queue into one job. Vanilla uses 15 for a colonist; a drone that is
        // leashed to a ten tile radius rarely finds that many, and the cap mostly stops a single
        // job outliving the charge that was meant to pay for it.
        private const int MaxQueued = 15;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!RiimbaAI.Operable(pawn))
                return null;

            CompRiimbaUnit comp = RiimbaAI.UnitComp(pawn);
            if (comp == null || !comp.CanWorkNow || !comp.HasChargeToWork)
                return null;

            Building_RiimbaStation station = comp.Station;
            Map map = pawn.Map;

            // The home area is the map's own statement of "cleaning matters here", and
            // listerFilthInHomeArea is the index vanilla keeps for exactly this question - far
            // cheaper than sweeping the station's radius cell by cell. The station radius then
            // narrows it further.
            List<Thing> filthInHome = map.listerFilthInHomeArea.FilthInHomeArea;
            if (filthInHome.Count == 0)
                return null;

            Filth nearest = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < filthInHome.Count; i++)
            {
                if (!(filthInHome[i] is Filth filth) || !IsValidTarget(pawn, station, filth))
                    continue;

                float distance = pawn.Position.DistanceToSquared(filth.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = filth;
                }
            }

            if (nearest == null)
                return null;

            Job job = JobMaker.MakeJob(RiimbaDefOf.Riimba_Clean);
            job.AddQueuedTarget(TargetIndex.A, nearest);

            // Everything else in range, nearest first. Sorting by distance from the FIRST target
            // rather than from the pawn is what keeps the unit working outwards through a room
            // instead of criss-crossing it.
            List<Filth> extras = new List<Filth>();
            for (int i = 0; i < filthInHome.Count; i++)
            {
                if (!(filthInHome[i] is Filth filth) || filth == nearest)
                    continue;

                if (IsValidTarget(pawn, station, filth))
                    extras.Add(filth);
            }

            extras.Sort((a, b) => a.Position.DistanceToSquared(nearest.Position)
                .CompareTo(b.Position.DistanceToSquared(nearest.Position)));

            for (int i = 0; i < extras.Count && job.GetTargetQueue(TargetIndex.A).Count < MaxQueued; i++)
                job.AddQueuedTarget(TargetIndex.A, extras[i]);

            return job;
        }

        private static bool IsValidTarget(Pawn pawn, Building_RiimbaStation station, Filth filth)
        {
            if (filth == null || !filth.Spawned || filth.Destroyed)
                return false;

            if (!station.InRadius(filth.Position))
                return false;

            if (filth.TicksSinceThickened < RiimbaAI.MinTicksSinceThickened)
                return false;

            if (filth.Fogged())
                return false;

            // Reservations are why three units on one station never scrub the same puddle: each
            // one holds its queued targets for the life of the job.
            if (!pawn.CanReserve(filth))
                return false;

            return pawn.CanReach(filth, PathEndMode.Touch, Danger.Deadly);
        }
    }

    // The terminal node, and the reason the tree never returns NoJob. A pawn whose think tree
    // produces nothing logs an error every single time it is asked to think, which on a drone
    // parked overnight is thousands of lines.
    public class JobGiver_RiimbaIdle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            // Wait_MaintainPosture is the one wait that does not try to stand the pawn up, which
            // is what a downed unit needs.
            if (pawn.Downed || pawn.Dead)
                return JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);

            Job job = JobMaker.MakeJob(JobDefOf.Wait);

            // Short, so a unit standing idle notices new filth, a repowered station or a freshly
            // built one within a couple of seconds rather than whenever something else happens
            // to interrupt it.
            job.expiryInterval = 120;

            return job;
        }
    }
}
