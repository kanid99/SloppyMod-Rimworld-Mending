using UnityEngine;
using Verse;

namespace RiimbaMod
{
    public class RiimbaModSettings : ModSettings
    {
        // Off means units clean without producing anything. The station's waste readout and dump
        // command disappear with it, rather than sitting at zero forever.
        public bool generateWaste = true;

        // Scales how much waste a given amount of filth packs down into.
        public float wasteMultiplier = 1.0f;

        // Scales charge drain only, not the gain. Set to 0 for units that never need to dock,
        // which is the "I just want a cleaning robot" setting.
        public float chargeDrainMultiplier = 1.0f;

        // Off means a unit works anywhere on the map its station can see, ignoring the radius.
        // The station still commands it and still takes its waste.
        public bool enforceRadius = true;

        // Field-by-field rather than replacing the settings object, because Mod.GetSettings hands
        // out a single instance that everything else already holds a reference to.
        public void ResetToDefaults()
        {
            RiimbaModSettings defaults = new RiimbaModSettings();

            generateWaste = defaults.generateWaste;
            wasteMultiplier = defaults.wasteMultiplier;
            chargeDrainMultiplier = defaults.chargeDrainMultiplier;
            enforceRadius = defaults.enforceRadius;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref generateWaste, "generateWaste", true);
            Scribe_Values.Look(ref wasteMultiplier, "wasteMultiplier", 1.0f);
            Scribe_Values.Look(ref chargeDrainMultiplier, "chargeDrainMultiplier", 1.0f);
            Scribe_Values.Look(ref enforceRadius, "enforceRadius", true);
        }
    }

    public class RiimbaModMain : Mod
    {
        public static RiimbaModSettings Settings;

        public RiimbaModMain(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RiimbaModSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            Header(listing, "Waste");

            listing.CheckboxLabeled(
                "Cleaning produces waste",
                ref Settings.generateWaste,
                "When enabled, everything a unit lifts off the floor is packed into its bin and "
                + "handed to its station, which puts it out as trash or wastepacks. Turn this off "
                + "and units simply clean, producing nothing.");

            if (Settings.generateWaste)
            {
                listing.Label($"    Waste per mess cleaned: {Settings.wasteMultiplier:F2}x");
                Settings.wasteMultiplier = listing.Slider(Settings.wasteMultiplier, 0f, 3f);
                listing.Label("    Scales both trash and wastepacks. Bin capacity is unchanged, so "
                            + "a higher setting also means more trips back to the station.");
            }

            listing.Gap();
            Header(listing, "Charge");

            listing.Label($"Charge drain: {Settings.chargeDrainMultiplier:F2}x");
            Settings.chargeDrainMultiplier = listing.Slider(Settings.chargeDrainMultiplier, 0f, 3f);
            listing.Label("At zero a unit never runs down and never needs a bay to charge at. It "
                        + "still docks to unload its bin.");

            listing.Gap();
            Header(listing, "Range");

            listing.CheckboxLabeled(
                "Units stay inside their station's radius",
                ref Settings.enforceRadius,
                "When enabled, a unit only cleans within its station's broadcast radius, which is "
                + "what the ring drawn around the station shows. Turn this off and a unit will "
                + "work anywhere it can reach on the map, while still docking at its station.");

            listing.Gap();
            if (listing.ButtonText("Reset to defaults"))
            {
                Settings.ResetToDefaults();
            }

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
            return "SloppyMods Riimba";
        }
    }
}
