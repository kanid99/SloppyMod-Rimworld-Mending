using Verse;

namespace MendingMod
{
    // Lights the repair center only while a job is running, the way Vanilla Expanded Framework's
    // CompGlowerOnProcess lights a VFE Factory machine only while its process runs. The building
    // calls UpdateLit when its working state changes; vanilla's power and flick checks still apply.
    public class CompGlowerMending : CompGlower
    {
        protected override bool ShouldBeLitNow =>
            base.ShouldBeLitNow && (!(parent is Building_AutomatedMender mender) || mender.IsRepairing);
    }
}
