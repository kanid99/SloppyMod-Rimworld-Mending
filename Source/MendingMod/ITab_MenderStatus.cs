using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // The repair center's status tab - the counterpart of VFE Factory's processor tab: what it is
    // doing, how hard it is being driven and what that costs, what is waiting on the item spot
    // and what it will need, and anything held for the reject chutes.
    public class ITab_MenderStatus : ITab
    {
        private Vector2 scroll;
        private float contentHeight;

        public ITab_MenderStatus()
        {
            size = new Vector2(420f, 480f);
            labelKey = "DynamicMending.TabStatus";
        }

        private Building_AutomatedMender Mender => SelThing as Building_AutomatedMender;

        protected override void FillTab()
        {
            Building_AutomatedMender mender = Mender;
            if (mender == null)
                return;

            Rect outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Rect inner = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(contentHeight, outer.height));

            Widgets.BeginScrollView(outer, ref scroll, inner);
            float y = 0f;
            Text.Font = GameFont.Small;
            foreach (string line in mender.StatusLines())
            {
                if (line.Length == 0)
                {
                    y += 8f;
                    continue;
                }

                float h = Text.CalcHeight(line, inner.width);
                Widgets.Label(new Rect(0f, y, inner.width, h), line);
                y += h;
            }

            contentHeight = y;
            Widgets.EndScrollView();
        }
    }
}
