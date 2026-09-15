using UnityEngine;
using Verse;

namespace MendingMod
{
    public class MendingModSettings : ModSettings
    {
        public bool generateWaste = true;
        public float degradationMultiplier = 1.0f;
        public bool simpleMode = false;
        public float simpleModeSteelPerRepair = 10f;
        public float repairWorkMultiplier = 0.35f;
        public bool enforceTechLimits = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref generateWaste, "generateWaste", true);
            Scribe_Values.Look(ref degradationMultiplier, "degradationMultiplier", 1.0f);
            Scribe_Values.Look(ref simpleMode, "simpleMode", false);
            Scribe_Values.Look(ref simpleModeSteelPerRepair, "simpleModeSteelPerRepair", 10f);
            Scribe_Values.Look(ref repairWorkMultiplier, "repairWorkMultiplier", 0.35f);
            Scribe_Values.Look(ref enforceTechLimits, "enforceTechLimits", true);
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

            listing.Gap();

            listing.Label($"Repair time: {Settings.repairWorkMultiplier:F2}x the item's full build time");
            Settings.repairWorkMultiplier = listing.Slider(Settings.repairWorkMultiplier, 0.02f, 2.0f);

            listing.Gap();

            listing.CheckboxLabeled(
                "Restrict benches by tech level",
                ref Settings.enforceTechLimits,
                "When enabled, the hand mending station can only repair industrial-tier gear or "
                + "lower. Items that declare no tech level are judged by their materials - needing "
                + "plasteel, synthread, hyperweave or advanced components counts as spacer-tier. "
                + "Turn this off to let every station repair anything.");

            listing.Gap();

            listing.CheckboxLabeled(
                "Simple mode",
                ref Settings.simpleMode,
                "When enabled, every repair costs only steel (scaled by missing HP) instead of materials computed from the item's own composition.");

            if (Settings.simpleMode)
            {
                listing.Label($"Steel per full repair: {Settings.simpleModeSteelPerRepair:F0}");
                Settings.simpleModeSteelPerRepair = listing.Slider(Settings.simpleModeSteelPerRepair, 1f, 50f);
            }

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Dynamic Mending";
        }
    }
}
