using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Pairs with WorkGiver_Mend/JobDriver_DoMend: the mend target is identified as the only
    // ingredient with useHitPoints and current HitPoints < MaxHitPoints, so ordering of
    // `ingredients` doesn't matter. XP is granted here rather than by the vanilla per-recipe
    // path, so it lands on the mended item's own recipeMaker.workSkill. In practice that is
    // Crafting for vanilla apparel and weapons alike - RimWorld has no Tailoring or Smithing
    // SKILL, only work types of those names - but a modded item that declares something else
    // trains that instead.
    //
    // JobDriver_DoMend does NOT route through Toils_Recipe.FinishRecipeAndStartStoringProduct
    // (which is what would normally invoke ConsumeIngredient/Notify_IterationCompleted below) -
    // confirmed by decompiling real vanilla Toils_Haul.PlaceHauledThingInCell that the
    // HaulAIUtility.UpdateJobWithPlacedThings callback which populates job.placedThings (and
    // therefore what FinishRecipeAndStartStoringProduct treats as "ingredients") only fires for
    // a hardcoded whitelist of vanilla JobDefs that our own DoMend JobDef isn't part of - so
    // job.placedThings would always stay empty and nothing would ever actually happen. JobDriver_
    // DoMend calls CompleteMend directly from its own finish toil instead. ConsumeIngredient and
    // Notify_IterationCompleted are kept here only as a safety net for that (currently unused)
    // vanilla call path.
    public class RecipeWorker_Mend : RecipeWorker
    {
        public const float BaseXpPerMend = 40f;

        // RecipeWorker's real consumption hook is per-ingredient (ConsumeIngredient, singular),
        // called once for each Thing in the bill — there is no plural ConsumeIngredients virtual
        // to override. The item being mended is simply the one ingredient we don't destroy.
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            if (ingredient.def.useHitPoints && ingredient.HitPoints < ingredient.MaxHitPoints)
                return;

            if (!ingredient.Destroyed)
                ingredient.Destroy(DestroyMode.Vanish);
        }

        // Deliberately no Notify_IterationCompleted override. JobDriver_DoMend calls
        // Bill_Production.Notify_IterationCompleted (it is what decrements a "do X times" bill),
        // and that forwards to recipe.Worker.Notify_IterationCompleted - so doing the repair here
        // as well as in the driver's finish toil would apply it twice: two quality-loss rolls, two
        // XP grants and two batches of waste per repair.
        public static void CompleteMend(Thing targetItem, Pawn actor, Thing billGiverThing)
        {
            int skillLevel = MendingUtility.GetSkillLevel(targetItem, actor);

            // Waste is measured before ResolveRepair restores the hit points, or the missing-HP
            // fraction it reads would already be zero.
            billGiverThing?.TryGetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(targetItem, skillLevel);

            MendResult result = MendingUtility.ResolveRepair(targetItem, skillLevel);
            MendingUtility.ShowRepairResult(result, actor ?? billGiverThing);

            MendingUtility.AwardSkillXp(targetItem, actor, BaseXpPerMend);
        }

    }
}
