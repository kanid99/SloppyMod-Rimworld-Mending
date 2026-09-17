using RimWorld;
using UnityEngine;
using Verse;

namespace FoodWasteMod
{
    // How much spoiled food a rotted stack leaves. Kept apart from the comp so the number can be
    // read, tested and argued about in one place rather than inline in a destroy handler.
    public static class SpoilageYield
    {
        // One unit of spoiled food is one nutrition of food that went bad.
        //
        // Nutrition, not stack count, is the whole point. Raw food is 0.05 nutrition a unit and a
        // meal is 0.9, so a rotted 75-stack of rice (3.75 nutrition) leaves about four units while
        // a rotted 10-stack of simple meals (9 nutrition) leaves nine. Counting items instead
        // would have made the rice worth eight times the meals, which is backwards - a stack limit
        // is a UI decision, not a statement about how much food is there.
        public const float NutritionPerResidueUnit = 1.0f;

        public static int ResidueCountFor(Thing food)
        {
            if (food == null || food.stackCount <= 0)
                return 0;

            float nutritionPerItem = food.GetStatValue(StatDefOf.Nutrition);
            if (nutritionPerItem <= 0f)
                return 0;

            // Guarded rather than assumed: this runs inside a destroy handler, and an NRE there
            // takes the whole tick with it.
            float multiplier = FoodWasteModMain.Settings?.yieldMultiplier ?? 1f;

            float units = nutritionPerItem * food.stackCount / NutritionPerResidueUnit * multiplier;

            // Rounded rather than floored, so a single rotted meal (0.9 nutrition) still leaves
            // something. Below half a unit it rounds away to nothing, which is what stops a colony
            // accruing a pile out of crumbs.
            return Mathf.Max(0, Mathf.RoundToInt(units));
        }
    }
}
