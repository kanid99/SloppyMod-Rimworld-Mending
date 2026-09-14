using RimWorld;

namespace MendingMod
{
    // Wired via RecipeDef.billClass. A distinct type mostly so WorkGiver_Mend/JobDriver_DoMend
    // can identify "this is one of ours" without string-matching recipe defNames; the dynamic
    // work-time override itself lives in JobDriver_DoMend, not here.
    public class Bill_Mend : Bill_Production
    {
    }
}
