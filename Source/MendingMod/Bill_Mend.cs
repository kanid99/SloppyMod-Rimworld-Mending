using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // Which damaged item a mend bill picks up next.
    public enum MendOrder : byte
    {
        Nearest,        // vanilla's behaviour, and what every bill did before this existed
        MostDamaged,
        MostValuable,
        LeastValuable,
    }

    // Every mend bill, however it was created. Player-added bills come through
    // Patch_BillUtility_MakeNewBill, which is the one Harmony patch in the mod: BillUtility.MakeNewBill
    // decompiles to a hardcoded chain that returns a plain Bill_Production for any recipe like
    // ours, with no def field or virtual to choose the class otherwise.
    //
    // It carries two things a plain bill cannot:
    //   * the ORDER it picks damaged items up in, shown and changed on the bill's own status line;
    //   * a material pin, used only where the Material Filter mod is absent - see MaterialFilters.
    //     With that mod, its own checkboxes carry the restriction, visibly, and this stays null.
    public class Bill_Mend : Bill_Production
    {
        public MendOrder order = MendOrder.Nearest;
        public ThingDef onlyStuff;

        // Scribe and Bill.Clone (Activator.CreateInstance(GetType())) both need this one.
        public Bill_Mend()
        {
        }

        public Bill_Mend(RecipeDef recipe, Precept_ThingStyle precept = null)
            : base(recipe, precept)
        {
        }

        public bool AllowsStuffOf(Thing item)
        {
            return onlyStuff == null || item.Stuff == onlyStuff;
        }

        public override string Label
        {
            get
            {
                string label = base.Label;
                return onlyStuff == null ? label : label + " (" + onlyStuff.label + ")";
            }
        }

        private string OrderLabel => ("DynamicMending.Order_" + order).Translate();

        // Bill.DoInterface only gives a bill a status line when this is non-empty, so returning
        // the order here is what makes room for the control below. The paused note Bill_Production
        // puts here is kept after it.
        protected override string StatusString
        {
            get
            {
                string mine = "DynamicMending.OrderStatus".Translate(OrderLabel);
                string theirs = base.StatusString;
                return theirs.NullOrEmpty() ? mine : mine + "  " + theirs;
            }
        }

        public override void DoStatusLineInterface(Rect rect)
        {
            // Bill_Production draws its "resume" button at the right-hand end when paused, so the
            // order control keeps to the left and leaves that end alone.
            base.DoStatusLineInterface(rect);

            Rect hit = new Rect(rect.x, rect.y, rect.width * 0.7f, rect.height);
            Widgets.DrawHighlightIfMouseover(hit);
            TooltipHandler.TipRegion(hit, "DynamicMending.OrderTip".Translate());

            if (!Widgets.ButtonInvisible(hit))
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (MendOrder choice in System.Enum.GetValues(typeof(MendOrder)))
            {
                MendOrder picked = choice;
                options.Add(new FloatMenuOption(("DynamicMending.Order_" + choice).Translate(), () => order = picked));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref order, "mendOrder", MendOrder.Nearest);
            Scribe_Defs.Look(ref onlyStuff, "onlyStuff");
        }

        // Copying a bill in the UI goes through Clone, and a clone that quietly dropped either
        // field would behave differently from the bill it was copied from while looking the same.
        public override Bill Clone()
        {
            Bill_Mend clone = (Bill_Mend)base.Clone();
            clone.order = order;
            clone.onlyStuff = onlyStuff;
            return clone;
        }
    }
}
