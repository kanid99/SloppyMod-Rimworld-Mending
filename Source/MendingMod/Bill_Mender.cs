using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    /// <summary>
    /// One of the industrial repair center's bills: repair or recycle the gear its filter allows.
    /// The machine runs it itself (pawns never do), so only the parts of a production bill that
    /// mean something for a machine are used: the filter (with its hit-point and quality sliders),
    /// suspend, and the repeat modes.
    /// </summary>
    public class Bill_Mender : Bill_Production
    {
        // Repair bills only: whether to take gear whose repair would cost components / advanced
        // components. Unticked, such gear is passed on to the next bill.
        public bool allowComponents = true;
        public bool allowAdvancedComponents = true;
        public Bill_Mender()
        {
        }

        public Bill_Mender(RecipeDef recipe) : base(recipe)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref allowComponents, "allowComponents", true);
            Scribe_Values.Look(ref allowAdvancedComponents, "allowAdvancedComponents", true);
        }

        /// <summary>Whether this bill's component settings let it pay a repair costing these materials.</summary>
        public bool AllowsCosts(List<ThingDefCountClass> costs)
        {
            foreach (ThingDefCountClass cost in costs)
            {
                if (!allowComponents && cost.thingDef == ThingDefOf.ComponentIndustrial)
                    return false;
                if (!allowAdvancedComponents && cost.thingDef == ThingDefOf.ComponentSpacer)
                    return false;
            }
            return true;
        }

        public bool IsRecycle
        {
            get { return recipe == MendingDefOf.MenderBill_Recycle; }
        }

        /// <summary>A job this bill decided has finished: counts down "do X times".</summary>
        public void NoteDone()
        {
            if (repeatMode == BillRepeatModeDefOf.RepeatCount && repeatCount > 0)
                repeatCount--;
        }

        protected override Window GetBillDialog()
        {
            return new Dialog_MenderBill(this);
        }

        public override Bill Clone()
        {
            Bill_Mender copy = (Bill_Mender)base.Clone();
            copy.allowComponents = allowComponents;
            copy.allowAdvancedComponents = allowAdvancedComponents;
            return copy;
        }
    }

    /// <summary>
    /// "Until you have X" for a repair bill: gear on the map its filter allows that does not need
    /// repairing - at or above the auto-repair threshold. (Recycle bills do not offer this mode.)
    /// </summary>
    public class RecipeWorkerCounter_MenderBill : RecipeWorkerCounter
    {
        public override bool CanCountProducts(Bill_Production bill)
        {
            return true;
        }

        public override string ProductsDescription(Bill_Production bill)
        {
            return "DynamicMending.BillCountDescription".Translate();
        }

        public override int CountProducts(Bill_Production bill)
        {
            IBillGiver giver = bill.billStack != null ? bill.billStack.billGiver : null;
            Map map = giver != null ? giver.Map : null;
            if (map == null)
                return 0;

            int count = 0;
            count += Count(map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel), bill);
            count += Count(map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon), bill);
            return count;
        }

        private static int Count(List<Thing> things, Bill_Production bill)
        {
            int count = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (!thing.Spawned || !bill.ingredientFilter.Allows(thing))
                    continue;
                if (MendingUtility.BelowAutoRepairThreshold(thing))
                    continue;
                count += thing.stackCount;
            }
            return count;
        }
    }

    public class ITab_MenderBills : ITab
    {
        private Vector2 scroll;
        private float viewHeight = 1000f;

        public ITab_MenderBills()
        {
            size = new Vector2(430f, 480f);
            labelKey = "TabBills";
        }

        private Building_AutomatedMender Mender
        {
            get { return SelThing as Building_AutomatedMender; }
        }

        protected override void FillTab()
        {
            Building_AutomatedMender mender = Mender;
            if (mender == null)
                return;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);

            // Paste a copied repair-center bill.
            Bill clip = BillUtility.Clipboard;
            if (clip is Bill_Mender && mender.BillStack.Count < BillStack.MaxCount)
            {
                Rect paste = new Rect(rect.xMax - 24f, rect.y, 24f, 24f);
                if (Widgets.ButtonImage(paste, TexButton.Paste))
                {
                    Bill copy = clip.Clone();
                    copy.InitializeAfterClone();
                    mender.BillStack.AddBill(copy);
                }
                TooltipHandler.TipRegion(paste, "PasteBillTip".Translate());
            }

            Text.Font = GameFont.Small;
            Rect listing = new Rect(rect.x, rect.y, rect.width, rect.height);
            mender.BillStack.DoListing(listing, delegate
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>();
                foreach (RecipeDef recipe in new[] { MendingDefOf.MenderBill_Repair, MendingDefOf.MenderBill_Recycle })
                {
                    RecipeDef r = recipe;
                    options.Add(new FloatMenuOption(r.LabelCap, delegate
                    {
                        mender.BillStack.AddBill(new Bill_Mender(r));
                    }));
                }
                return options;
            }, ref scroll, ref viewHeight);
        }
    }

    /// <summary>
    /// The bill window, trimmed to what a machine uses: what it does, how often, and the gear
    /// filter. (Vanilla's has ingredient radius, "store in" and worker restrictions, which mean
    /// nothing here.)
    /// </summary>
    public class Dialog_MenderBill : Window
    {
        private readonly Bill_Mender bill;
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();
        private string repeatBuffer;
        private string targetBuffer;
        private string unpauseBuffer;

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 620f); }
        }

        public Dialog_MenderBill(Bill_Mender bill)
        {
            this.bill = bill;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), bill.LabelCap);
            Text.Font = GameFont.Small;

            float top = 40f;
            float bottom = inRect.height - CloseButSize.y - 10f;
            Rect left = new Rect(0f, top, 300f, bottom - top);
            Rect right = new Rect(left.xMax + 17f, top, inRect.width - left.width - 17f, bottom - top);

            Listing_Standard list = new Listing_Standard();
            list.Begin(left);

            list.Label(bill.recipe.description);
            list.Gap(12f);

            // Repeat mode. Recycle bills have no "until you have X": counting what you have says
            // nothing about what should be broken down.
            if (list.ButtonText(bill.repeatMode.LabelCap))
            {
                List<FloatMenuOption> modes = new List<FloatMenuOption>();
                foreach (BillRepeatModeDef mode in DefDatabase<BillRepeatModeDef>.AllDefsListForReading)
                {
                    if (bill.IsRecycle && mode == BillRepeatModeDefOf.TargetCount)
                        continue;
                    BillRepeatModeDef m = mode;
                    modes.Add(new FloatMenuOption(m.LabelCap, delegate { bill.repeatMode = m; }));
                }
                Find.WindowStack.Add(new FloatMenu(modes));
            }
            list.Gap(6f);

            if (bill.repeatMode == BillRepeatModeDefOf.RepeatCount)
            {
                list.Label("RepeatCount".Translate(bill.repeatCount));
                list.IntEntry(ref bill.repeatCount, ref repeatBuffer);
            }
            else if (bill.repeatMode == BillRepeatModeDefOf.TargetCount)
            {
                list.Label("DynamicMending.BillTargetLabel".Translate(bill.recipe.WorkerCounter.CountProducts(bill), bill.targetCount));
                list.IntEntry(ref bill.targetCount, ref targetBuffer);
                list.CheckboxLabeled("PauseWhenSatisfied".Translate(), ref bill.pauseWhenSatisfied);
                if (bill.pauseWhenSatisfied)
                {
                    list.Label("UnpauseWhenYouHave".Translate() + ": " + bill.unpauseWhenYouHave);
                    list.IntEntry(ref bill.unpauseWhenYouHave, ref unpauseBuffer);
                    if (bill.unpauseWhenYouHave >= bill.targetCount)
                        bill.unpauseWhenYouHave = Mathf.Max(0, bill.targetCount - 1);
                }
            }

            if (!bill.IsRecycle)
            {
                list.Gap(12f);
                list.CheckboxLabeled("DynamicMending.BillAllowComponents".Translate(), ref bill.allowComponents,
                    "DynamicMending.BillAllowComponentsDesc".Translate());
                list.CheckboxLabeled("DynamicMending.BillAllowAdvancedComponents".Translate(), ref bill.allowAdvancedComponents,
                    "DynamicMending.BillAllowComponentsDesc".Translate());
            }

            list.Gap(12f);
            list.CheckboxLabeled("SuspendBill".Translate(), ref bill.suspended);
            list.Gap(12f);
            list.Label("DynamicMending.BillOrderHint".Translate());
            list.End();

            ThingFilterUI.DoThingFilterConfigWindow(right, filterState, bill.ingredientFilter, bill.recipe.fixedIngredientFilter,
                1, map: Find.CurrentMap);
        }
    }
}
