using UnityEngine;
using Verse;

namespace FoodWasteMod
{
    public class FoodWasteModSettings : ModSettings
    {
        public bool enabled = true;
        public float yieldMultiplier = 1.0f;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref yieldMultiplier, "yieldMultiplier", 1.0f);
        }
    }

    public class FoodWasteModMain : Mod
    {
        // Set in the constructor, which LoadedModManager runs while defs are still loading -
        // well before anything can rot - but every read is still null-guarded, because a settings
        // read that throws inside a destroy handler would take the tick with it.
        public static FoodWasteModSettings Settings;

        public FoodWasteModMain(ModContentPack content) : base(content)
        {
            Settings = GetSettings<FoodWasteModSettings>();
        }

        public override string SettingsCategory()
        {
            return "SloppyMods Food Waste";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(
                "Rotted food leaves spoiled food",
                ref Settings.enabled,
                "When off, food rots away exactly as it does in vanilla and nothing is left behind. "
                + "The item and its recipes stay in the game, so anything already in your stores "
                + "can still be processed.");

            listing.Gap();

            listing.Label(
                $"Spoiled food per nutrition lost: {Settings.yieldMultiplier:F2}x",
                tooltip: "One unit of spoiled food per nutrition of food that went bad, multiplied "
                    + "by this. A rotted 75-stack of rice is about four units at 1.00x; a rotted "
                    + "stack of ten simple meals is about nine.");
            Settings.yieldMultiplier = listing.Slider(Settings.yieldMultiplier, 0.1f, 3.0f);

            listing.Gap();
            listing.Label(
                "Spoiled food rots away by itself in four days unless kept cold, so leaving it "
                + "alone is always a valid answer. The recipes are at the biofuel refinery "
                + "(chemfuel) and at Vanilla Recycling Expanded's recycling benches (reclaimed "
                + "biopacks, wastepacks).");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}
