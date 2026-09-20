using RimWorld;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // "Repair this" on the right-click menu, and - when it can't be done - why not.
    //
    // NO HARMONY. RimWorld 1.6 dropped FloatMenuMakerMap.AddHumanlikeOrders (the hook every
    // older mod patched) for this provider system, and FloatMenuMakerMap.Init was decompiled to
    // confirm what it does with it:
    //
    //     foreach (Type t in typeof(FloatMenuOptionProvider).AllSubclassesNonAbstract())
    //         providers.Add((FloatMenuOptionProvider)Activator.CreateInstance(t));
    //
    // Any non-abstract subclass in any loaded assembly is found and instantiated on its own.
    // There is no def to declare and nothing to patch - this class existing IS the registration.
    public class FloatMenuOptionProvider_Repair : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        // Carrying gear to a bench and working at it: both need hands.
        protected override bool RequiresManipulation => true;

        // Runs for every thing under the cursor on every right-click, so it stays cheap:
        // IsMendable does no map queries and no pathing, just hit points and a filter test.
        public override bool TargetThingValid(Thing thing, FloatMenuContext context)
        {
            return base.TargetThingValid(thing, context) && MendJobMaker.IsMendable(thing);
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null)
                return null;

            // A DRY RUN. It must not create the bill just because someone opened a menu - see
            // MendJobMaker's header. The bill is added by the click, below.
            if (!MendJobMaker.TryMakeRepairJob(pawn, clickedThing, false, out _, out string reason))
            {
                // Greyed out and carrying the reason, rather than absent: a player who wants to
                // know why they cannot repair something gets nothing at all from a missing menu
                // entry, which is the whole point of the feature.
                return new FloatMenuOption(
                    "DynamicMending.CannotRepair".Translate(clickedThing.LabelShort) + ": " + reason, null);
            }

            string label = "DynamicMending.RepairLabel".Translate(clickedThing.LabelShort);

            // Repairing gear the colony has never built is allowed but expensive - double
            // materials, 2.5x the risk of ruining it - so the option says so before it is taken
            // rather than after the item is wrecked.
            if (MendingUtility.IsUnfamiliar(clickedThing))
                label += " (" + "DynamicMending.RepairWorkingBlind".Translate() + ")";

            FloatMenuOption option = new FloatMenuOption(label, () =>
            {
                // Re-run with commit, and re-run in full: the world can change between the menu
                // being built and the option being clicked - the bench loses power, someone
                // hauls the last of the steel away - and acting on the dry run's conclusion
                // would queue a job that cannot be done.
                if (MendJobMaker.TryMakeRepairJob(pawn, clickedThing, true, out Job job, out string why))
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                else
                    Messages.Message("DynamicMending.CannotRepair".Translate(clickedThing.LabelShort)
                                     + ": " + why, clickedThing, MessageTypeDefOf.RejectInput, false);
            });

            return FloatMenuUtility.DecoratePrioritizedTask(option, pawn, clickedThing);
        }
    }
}
