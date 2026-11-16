using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // The repair centre's "what will you take" and "what will you break down" filters: vanilla's
    // own filter tree, so each has the same search box, hit-point and quality sliders, and
    // category checkboxes as a stockpile.
    //
    // Vanilla's two tainted-apparel checkboxes are hidden from it. Tainted gear has its own
    // repair / recycle / reject toggle on the machine, and two controls for one question can
    // contradict each other with no way for the player to tell which one won.
    public class Dialog_MenderAcceptFilter : Window
    {
        private readonly Building_AutomatedMender mender;
        private readonly ThingFilter filter;
        private readonly string titleKey;
        private readonly string hintKey;
        private readonly ThingFilterUI.UIState state = new ThingFilterUI.UIState();

        private static List<SpecialThingFilterDef> hiddenFilters;

        private static IEnumerable<SpecialThingFilterDef> HiddenFilters
        {
            get
            {
                if (hiddenFilters == null)
                {
                    hiddenFilters = new[] { "AllowDeadmansApparel", "AllowNonDeadmansApparel" }
                        .Select(n => DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(n))
                        .Where(d => d != null)
                        .ToList();
                }

                return hiddenFilters;
            }
        }

        public override Vector2 InitialSize => new Vector2(420f, 620f);

        public Dialog_MenderAcceptFilter(Building_AutomatedMender mender, ThingFilter filter, string titleKey, string hintKey)
        {
            this.mender = mender;
            this.filter = filter;
            this.titleKey = titleKey;
            this.hintKey = hintKey;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), titleKey.Translate());
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(0f, 36f, inRect.width, 44f), hintKey.Translate());

            Rect list = new Rect(0f, 84f, inRect.width, inRect.height - 84f - CloseButSize.y - 12f);
            ThingFilterUI.DoThingFilterConfigWindow(
                list, state, filter, Building_AutomatedMender.ParentFilter,
                openMask: 1, forceHiddenFilters: HiddenFilters, map: mender.Map);
        }
    }
}
