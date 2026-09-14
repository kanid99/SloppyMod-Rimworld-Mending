using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DynamicMending
{
    // Pairs with WorkGiver_Mend: the mend target is identified as the only ingredient
    // with useHitPoints and current HitPoints < MaxHitPoints, so ordering of `ingredients`
    // doesn't matter.
    public class RecipeWorker_Mend : RecipeWorker
    {
        public override void ConsumeIngredients(List<Thing> ingredients, RecipeDef recipe, Map map)
        {
            Thing targetItem = FindMendTarget(ingredients);

            for (int i = 0; i < ingredients.Count; i++)
            {
                Thing ingredient = ingredients[i];
                if (ingredient == targetItem)
                    continue;

                if (!ingredient.Destroyed)
                    ingredient.Destroy(DestroyMode.Vanish);
            }
        }

        public override void Notify_IterationCompleted(Pawn actor, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(actor, ingredients);

            Thing targetItem = FindMendTarget(ingredients);
            if (targetItem == null || targetItem.MaxHitPoints <= 0)
                return;

            float missingHpFraction = Mathf.Clamp01(1f - (float)targetItem.HitPoints / targetItem.MaxHitPoints);

            Thing billGiverThing = actor.CurJob?.GetTarget(TargetIndex.A).Thing;
            billGiverThing?.TryGetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(targetItem);

            TryApplyQualityDegradeRisk(targetItem, actor, missingHpFraction);

            targetItem.HitPoints = targetItem.MaxHitPoints;
        }

        private static Thing FindMendTarget(List<Thing> ingredients)
        {
            return ingredients.FirstOrDefault(t => t.def.useHitPoints && t.HitPoints < t.MaxHitPoints);
        }

        private static void TryApplyQualityDegradeRisk(Thing item, Pawn pawn, float missingHpFraction)
        {
            if (!item.TryGetQuality(out QualityCategory currentQuality))
                return;

            SkillDef workSkill = item.def.recipeMaker?.workSkill ?? SkillDefOf.Crafting;
            int skillLevel = pawn.skills?.GetSkill(workSkill)?.Level ?? 0;

            float skillFactor = Mathf.Clamp01(1f - skillLevel / 20f);
            float qualityFactor = ((int)currentQuality + 1) / 7f;
            float degradeChance = Mathf.Clamp01(missingHpFraction * skillFactor * qualityFactor * MendingModMain.Settings.degradationMultiplier);

            if (!Rand.Chance(degradeChance))
                return;

            CompQuality compQuality = item.TryGetComp<CompQuality>();
            if (compQuality == null)
                return;

            QualityCategory degraded = (QualityCategory)Mathf.Max((int)QualityCategory.Awful, (int)currentQuality - 1);
            compQuality.SetQuality(degraded, ArtGenerationContext.Colony);
        }
    }
}
