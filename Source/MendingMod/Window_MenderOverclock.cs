using UnityEngine;
using Verse;

namespace MendingMod
{
    // The repair center's overclock slider, laid out after Vanilla Furniture Expanded -
    // Factory's own overclock window, with what each setting costs and buys spelled out below
    // it: speed, how carefully it works, power draw and heat.
    public class Window_MenderOverclock : Window
    {
        private readonly Building_AutomatedMender mender;

        public override Vector2 InitialSize => new Vector2(480f, 270f);

        public Window_MenderOverclock(Building_AutomatedMender mender)
        {
            this.mender = mender;
            doCloseX = true;
            doCloseButton = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "DynamicMending.OverclockTitle".Translate());
            Text.Font = GameFont.Small;

            float max = mender.MaxOverclockNow;
            mender.Overclock = Widgets.HorizontalSlider(
                new Rect(0f, 44f, inRect.width, 32f), mender.Overclock,
                Building_AutomatedMender.MinOverclock, max, false,
                mender.Overclock.ToStringPercent(),
                Building_AutomatedMender.MinOverclock.ToStringPercent(), max.ToStringPercent(), 0.05f);

            float load = Building_AutomatedMender.LoadFactorAt(mender.Overclock);
            string text = "DynamicMending.OverclockSkill".Translate(mender.EffectiveSkill) + "\n"
                + "DynamicMending.OverclockPower".Translate(Mathf.RoundToInt(mender.BasePowerConsumption * load)) + "\n"
                + "DynamicMending.OverclockHeat".Translate((mender.BaseHeatPerSecond * load * mender.HeatFactor).ToString("0.#"));
            Widgets.Label(new Rect(0f, 88f, inRect.width, 80f), text);
        }
    }
}
