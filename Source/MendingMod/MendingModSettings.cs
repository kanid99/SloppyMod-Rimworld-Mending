using UnityEngine;
using Verse;

namespace MendingMod
{
    // When a repair is allowed to knock an item down a quality tier. Failure-only is the middle
    // ground players usually want: a botched repair still has consequences, but handing good gear
    // to a competent crafter is no longer a gamble.
    public enum QualityLossMode : byte
    {
        Always,
        FailureOnly,
        Never,
    }

    public class MendingModSettings : ModSettings
    {
        public bool generateWaste = true;
        public float degradationMultiplier = 1.0f;
        public bool simpleMode = false;
        public float simpleModeSteelPerRepair = 10f;
        public float repairWorkMultiplier = 0.35f;
        public bool enforceTechLimits = true;
        public bool penaliseUnresearched = true;

        // Master switch for both tech gates below it. Off means no item is ever refused or
        // penalised for being beyond the colony's research.
        public bool enableTechRequirements = true;

        // Off means repairs consume nothing at all - items are simply mended.
        public bool requireResources = true;

        // Off means a failed repair wastes the work and the materials but leaves the item's hit
        // points where they were, instead of taking more off.
        public bool hpLossOnFailure = true;

        public QualityLossMode qualityLossMode = QualityLossMode.Always;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref generateWaste, "generateWaste", true);
            Scribe_Values.Look(ref degradationMultiplier, "degradationMultiplier", 1.0f);
            Scribe_Values.Look(ref simpleMode, "simpleMode", false);
            Scribe_Values.Look(ref simpleModeSteelPerRepair, "simpleModeSteelPerRepair", 10f);
            Scribe_Values.Look(ref repairWorkMultiplier, "repairWorkMultiplier", 0.35f);
            Scribe_Values.Look(ref enforceTechLimits, "enforceTechLimits", true);
            Scribe_Values.Look(ref penaliseUnresearched, "penaliseUnresearched", true);
            Scribe_Values.Look(ref enableTechRequirements, "enableTechRequirements", true);
            Scribe_Values.Look(ref requireResources, "requireResources", true);
            Scribe_Values.Look(ref hpLossOnFailure, "hpLossOnFailure", true);
            Scribe_Values.Look(ref qualityLossMode, "qualityLossMode", QualityLossMode.Always);
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
            // Two columns: the list has outgrown a single column at the settings window's height,
            // and a clipped last option reads as a missing feature.
            Listing_Standard listing = new Listing_Standard();
            listing.ColumnWidth = (inRect.width - 34f) / 2f;
            listing.Begin(inRect);

            Header(listing, "Repair cost");

            listing.CheckboxLabeled(
                "Repairs require resources",
                ref Settings.requireResources,
                "When enabled, a repair consumes a share of the item's own build materials, scaled "
                + "by how damaged it is and how skilled the crafter is. Turn this off to repair "
                + "items for free - work time and risk still apply, but nothing is consumed.");

            if (Settings.requireResources)
            {
                listing.CheckboxLabeled(
                    "Simple mode (steel only)",
                    ref Settings.simpleMode,
                    24f);

                if (Settings.simpleMode)
                {
                    listing.Label($"    Steel per full repair: {Settings.simpleModeSteelPerRepair:F0}");
                    Settings.simpleModeSteelPerRepair = listing.Slider(Settings.simpleModeSteelPerRepair, 1f, 50f);
                }
            }

            listing.Gap();
            Header(listing, "Repair time");

            listing.Label($"Repair time: {Settings.repairWorkMultiplier:F2}x the item's full build time");
            Settings.repairWorkMultiplier = listing.Slider(Settings.repairWorkMultiplier, 0.02f, 2.0f);

            listing.Gap();
            Header(listing, "Waste");

            listing.CheckboxLabeled(
                "Repairs produce waste",
                ref Settings.generateWaste,
                "When enabled, repairs fill a waste container on the building with wastepacks or "
                + "recyclable trash. A full container stops the building until a hauler empties it "
                + "or you use its dump command. Turn this off to remove waste entirely.");

            listing.NewColumn();

            Header(listing, "Repair outcome");

            listing.CheckboxLabeled(
                "Failed repairs lose hit points",
                ref Settings.hpLossOnFailure,
                "When enabled, a botched repair takes hit points off the item instead of restoring "
                + "them. Turn this off and a failure simply wastes the work and materials, leaving "
                + "the item no worse than it was.");

            listing.Gap(6f);
            listing.Label("Quality loss from repairs");

            if (listing.RadioButton("Can lose quality any repair", Settings.qualityLossMode == QualityLossMode.Always, 8f,
                    "A repair can knock the item down a quality tier whether it succeeds or fails, "
                    + "though failure makes it far likelier."))
            {
                Settings.qualityLossMode = QualityLossMode.Always;
            }

            if (listing.RadioButton("Only on failed repairs", Settings.qualityLossMode == QualityLossMode.FailureOnly, 8f,
                    "A successful repair never costs quality. Only a botched one can demote the item."))
            {
                Settings.qualityLossMode = QualityLossMode.FailureOnly;
            }

            if (listing.RadioButton("Never lose quality", Settings.qualityLossMode == QualityLossMode.Never, 8f,
                    "Repairs never change an item's quality."))
            {
                Settings.qualityLossMode = QualityLossMode.Never;
            }

            listing.Gap();
            Header(listing, "Tech requirements");

            listing.CheckboxLabeled(
                "Apply tech requirements",
                ref Settings.enableTechRequirements,
                "Master switch for both options below. Turn this off and any station repairs any "
                + "item, with no restriction and no penalty for gear your colony cannot build.");

            if (Settings.enableTechRequirements)
            {
                listing.CheckboxLabeled(
                    "Restrict benches by tech level",
                    ref Settings.enforceTechLimits,
                    24f);

                listing.CheckboxLabeled(
                    "Penalise repairs beyond your research",
                    ref Settings.penaliseUnresearched,
                    24f);
            }

            listing.Gap();
            Header(listing, "Severity");

            listing.Label($"Degradation multiplier: {Settings.degradationMultiplier:F2}");
            Settings.degradationMultiplier = listing.Slider(Settings.degradationMultiplier, 0.0f, 2.0f);
            listing.Label("Scales failure chance, how much a failure costs, and how fast waste builds up.");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        private static void Header(Listing_Standard listing, string label)
        {
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.72f, 0.82f, 0.86f);
            listing.Label(label);
            GUI.color = Color.white;
            listing.GapLine(2f);
        }

        public override string SettingsCategory()
        {
            return "SloppyMods Mending Solutions";
        }
    }
}
