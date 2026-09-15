using Verse;

namespace MendingMod
{
    // Vanilla's powered heat pusher runs whenever the building has power, which would have the
    // repair center cooking the room around the clock. This only pushes heat while it is actually
    // mending something, so cooling has to scale with throughput rather than with uptime.
    public class CompHeatPusherMending : CompHeatPusherPowered
    {
        public override bool ShouldPushHeatNow =>
            base.ShouldPushHeatNow && (!(parent is Building_AutomatedMender mender) || mender.IsRepairing);
    }
}
