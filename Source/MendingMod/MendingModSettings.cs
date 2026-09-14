using UnityEngine;
using Verse;

namespace MendingMod
{
    public class MendingModSettings : ModSettings
    {
        public bool generateWaste = true;
        public float degradationMultiplier = 1.0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref generateWaste, "generateWaste", true);
            Scribe_Values.Look(ref degradationMultiplier, "degradationMultiplier", 1.0f);
        }
    }

    public class MendingModMain : Mod
    {
        public static MendingModSettings Settings;

        public MendingModMain(ModContentPack content) : base(content)
        {
            Settings = GetSettings<MendingModSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(
                "Generate waste from mending",
                ref Settings.generateWaste,
                "When enabled, mending damaged items produces toxic waste or trash proportional to the repair work done.");

            listing.Gap();

            listing.Label($"Degradation multiplier: {Settings.degradationMultiplier:F2}");
            Settings.degradationMultiplier = listing.Slider(Settings.degradationMultiplier, 0.0f, 2.0f);

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Dynamic Mending";
        }
    }
}
