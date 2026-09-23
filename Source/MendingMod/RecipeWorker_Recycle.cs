using Verse;

namespace MendingMod
{
    // Marks a recipe as "break this gear down for materials". A separate type from
    // RecipeWorker_Mend on purpose, not a subclass: the right-click repair order, the inspect
    // preview and the bill class all find MEND recipes by type, and a recycle bill must never
    // be mistaken for one - a right-click "repair" that shredded the item would be a disaster.
    //
    // Like RecipeWorker_Mend it does no work itself. WorkGiver_Mend and JobDriver_DoMend run both
    // kinds of bill, and the driver's finish toil does the recycling - see the note on
    // RecipeWorker_Mend about why vanilla's recipe finish path never runs for these jobs.
    public class RecipeWorker_Recycle : RecipeWorker
    {
    }
}
