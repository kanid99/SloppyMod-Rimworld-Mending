using RimWorld;
using Verse;

namespace MendingMod
{
    // Vanilla's powered heat pusher runs whenever the building has power, which would have the
    // repair center cooking the room around the clock. This one never pushes on its own: the
    // repair center calls PushHeat once a second while a job runs, scaled by its overclock, so
    // cooling has to scale with throughput and with how hard the machine is driven.
    public class CompHeatPusherMending : CompHeatPusherPowered
    {
        public override bool ShouldPushHeatNow => false;

        // One second's heat. base.ShouldPushHeatNow keeps vanilla's power and temperature-cap
        // checks, so the machine still stops heating a room already at its maximum.
        public void PushHeat(float factor)
        {
            if (!base.ShouldPushHeatNow || factor <= 0f)
                return;

            GenTemperature.PushHeat(parent.PositionHeld, parent.MapHeld, Props.heatPerSecond * factor);
        }
    }
}
