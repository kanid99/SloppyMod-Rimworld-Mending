using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RiimbaMod
{
    // Which bin a mess goes into.
    //
    // There is no flag in the game that answers this. FilthProperties carries cleaning work,
    // thickness, whether rain washes it away and whether it sticks to pawns - nothing that says
    // "this came out of a body". So the split is made by name, against vanilla's own filth defs
    // first and a keyword sweep second, and the keyword sweep is what covers modded filth.
    //
    // Getting it wrong is cheap in both directions: a misfiled mess becomes the wrong kind of
    // waste item, not an error, so the fallback is biased towards trash - the harmless answer.
    public static class RiimbaFilth
    {
        // Every vanilla filth def that is a bodily product, across the base game and all DLC.
        // Named rather than pattern-matched because these are the ones worth being exact about.
        private static readonly HashSet<string> BioFilthDefNames = new HashSet<string>
        {
            // Core
            "Filth_Blood",
            "Filth_BloodInsect",
            "Filth_BloodSmear",
            "Filth_DriedBlood",
            "Filth_Vomit",
            "Filth_CorpseBile",
            "Filth_Slime",
            "Filth_PodSlime",
            "Filth_AnimalFilth",
            // Biotech
            "Filth_AmnioticFluid",
            "Filth_GestationFluid",
            // Anomaly
            "Filth_FlammableBile",
            "Filth_GrayFlesh",
            "Filth_GrayFleshNoticeable",
            "Filth_TwistedFlesh",
            "Filth_MetalhorrorDebris",
            "Filth_RevenantBloodPool",
            "Filth_RevenantSmear",
        };

        // For filth from mods this install has that the list above cannot know about. Matched
        // against the defName and the label, case-insensitively, as substrings.
        //
        // Deliberately excludes "hair": hair is shed off a body but it is dry, it sweeps up like
        // lint, and sealing a colony's haircuts into wastepacks would be absurd. It goes in the
        // trash with the dust.
        private static readonly string[] BioKeywords =
        {
            "blood", "vomit", "puke", "bile", "slime", "flesh", "gore", "viscera",
            "entrail", "guts", "ichor", "pus", "urine", "feces", "faeces", "dung",
            "excrement", "manure", "afterbirth", "amniotic",
        };

        private static readonly Dictionary<ThingDef, bool> cache = new Dictionary<ThingDef, bool>();

        // Cached per def, because this is asked once per unit of thickness removed - several
        // times per puddle, on up to three units at a time - and the answer for a given def
        // never changes within a run.
        public static bool IsBiological(ThingDef filthDef)
        {
            if (filthDef == null)
                return false;

            if (cache.TryGetValue(filthDef, out bool cached))
                return cached;

            bool result = Classify(filthDef);
            cache[filthDef] = result;
            return result;
        }

        private static bool Classify(ThingDef filthDef)
        {
            if (BioFilthDefNames.Contains(filthDef.defName))
                return true;

            string defName = filthDef.defName?.ToLowerInvariant() ?? string.Empty;
            string label = filthDef.label?.ToLowerInvariant() ?? string.Empty;

            foreach (string keyword in BioKeywords)
            {
                if (defName.Contains(keyword) || label.Contains(keyword))
                    return true;
            }

            return false;
        }
    }
}
