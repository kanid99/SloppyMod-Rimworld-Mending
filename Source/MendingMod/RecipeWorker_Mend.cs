using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Pairs with WorkGiver_Mend/JobDriver_DoMend: the mend target is identified as the only
    // ingredient with useHitPoints and current HitPoints < MaxHitPoints, so ordering of
    // `ingredients` doesn't matter. XP is granted here (not by the vanilla per-recipe skill)
    // so it always lands on the mended item's own recipeMaker.workSkill, not a flat
    // Tailoring/Smithing value shared by the whole RecipeDef.
    public class RecipeWorker_Mend : RecipeWorker
    {
        private const float BaseXpPerMend = 40f;

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

        public override void Notify_IterationCompleted(Pawn actor, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(actor, ingredients);

            Thing targetItem = FindMendTarget(ingredients);
            if (targetItem == null)
                return;

            Thing billGiverThing = actor.CurJob?.GetTarget(TargetIndex.A).Thing;
            billGiverThing?.TryGetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(targetItem);

            int skillLevel = actor.skills?.GetSkill(MendingUtility.GetRelevantWorkSkill(targetItem))?.Level ?? 0;
            MendingUtility.ResolveRepair(targetItem, skillLevel);

            MendingUtility.AwardSkillXp(targetItem, actor, BaseXpPerMend);
        }

        private static Thing FindMendTarget(List<Thing> ingredients)
        {
            return ingredients.FirstOrDefault(t => t.def.useHitPoints && t.HitPoints < t.MaxHitPoints);
        }
    }
}
