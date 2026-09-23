using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Right-click a colonist ON THEMSELVES to repair what they are wearing or wielding.
    //
    // CanSelfTarget is what makes this reachable: FloatMenuOptionProvider.TargetPawnValid was
    // decompiled to refuse the selected pawn as a target unless it is set. Like the floor-item
    // order, no Harmony - see FloatMenuOptionProvider_Repair for why this class existing is
    // registration enough.
    public class FloatMenuOptionProvider_RepairWorn : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;
        protected override bool CanSelfTarget => true;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (pawn == null || clickedPawn != pawn)
                yield break;

            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    // Locked apparel - a slave collar, anything a ritual or ideoligion pins on -
                    // cannot be taken off, so there is nothing to offer.
                    if (!MendJobMaker.IsMendable(apparel) || pawn.apparel.IsLocked(apparel))
                        continue;

                    yield return OptionFor(pawn, apparel);
                }
            }

            ThingWithComps weapon = pawn.equipment?.Primary;
            if (weapon != null && MendJobMaker.IsMendable(weapon))
                yield return OptionFor(pawn, weapon);
        }

        private static FloatMenuOption OptionFor(Pawn pawn, Thing gear)
        {
            // A dry run: nothing is taken off to find out whether it can be repaired.
            if (!MendJobMaker.TryMakeRepairJob(pawn, gear, false, out _, out string reason))
            {
                return new FloatMenuOption(
                    "DynamicMending.CannotRepairWorn".Translate(gear.LabelShort) + ": " + reason, null);
            }

            string label = "DynamicMending.RepairWornLabel".Translate(gear.LabelShort);
            if (MendingUtility.IsUnfamiliar(gear))
                label += " (" + "DynamicMending.RepairWorkingBlind".Translate() + ")";

            return new FloatMenuOption(label, () =>
            {
                Job job = JobMaker.MakeJob(MendingDefOf.RepairWorn, gear);
                job.playerForced = true;
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }
    }
}
