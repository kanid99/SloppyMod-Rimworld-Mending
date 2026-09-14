using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    public struct MendResult
    {
        public bool success;
        public bool qualityDropped;
    }

    // Shared by RecipeWorker_Mend (pawn skill) and Building_AutomatedMender (fixed skill 10)
    // so the two repair paths can never drift into different odds for the same job.
    public static class MendingUtility
    {
        public static SkillDef GetRelevantWorkSkill(Thing item)
        {
            return item.def.recipeMaker?.workSkill ?? SkillDefOf.Crafting;
        }

        public static float GetMissingHpFraction(Thing item)
        {
            if (item.MaxHitPoints <= 0)
                return 0f;

            return Mathf.Clamp01(1f - (float)item.HitPoints / item.MaxHitPoints);
        }

        // recipeMaker.workAmount is the def's own base crafting time; scaling it by missing HP
        // and quality gives a rough "how much of this item are we redoing" estimate. Falls back
        // to the WorkToMake stat if the def has no recipeMaker block (e.g. it's stuff-generated).
        public static float GetDynamicWorkAmount(Thing item)
        {
            if (item == null)
                return 300f;

            float baseWork = item.def.recipeMaker != null
                ? item.def.recipeMaker.workAmount
                : item.def.GetStatValueAbstract(StatDefOf.WorkToMake, item.Stuff);

            if (baseWork <= 0f)
                baseWork = 300f;

            item.TryGetQuality(out QualityCategory quality);
            float qualityFactor = 1f + (int)quality * 0.15f;

            return baseWork * GetMissingHpFraction(item) * qualityFactor;
        }

        // Skill level at (or above) which the roll always succeeds. Scales up with the item's
        // quality (harder items are riskier to touch) and with how much HP is being restored.
        private static float RequiredSkillForMaxChance(Thing item, float missingHpFraction)
        {
            item.TryGetQuality(out QualityCategory quality);
            float qualityComponent = ((int)quality + 1) * 2f;
            float hpComponent = missingHpFraction * 10f;

            return (qualityComponent + hpComponent) * MendingModMain.Settings.degradationMultiplier;
        }

        public static MendResult ResolveRepair(Thing item, int skillLevel)
        {
            float missingHpFraction = GetMissingHpFraction(item);
            MendResult result = default;

            if (missingHpFraction <= 0f)
            {
                result.success = true;
                return result;
            }

            float requiredSkill = RequiredSkillForMaxChance(item, missingHpFraction);
            float successChance = requiredSkill <= 0f ? 1f : Mathf.Clamp01(skillLevel / requiredSkill);
            bool success = Rand.Chance(successChance);

            if (success)
            {
                item.HitPoints = item.MaxHitPoints;
            }
            else
            {
                float failSeverity = 1f - successChance;
                int hpLoss = Mathf.RoundToInt(item.MaxHitPoints * missingHpFraction * failSeverity * 0.5f * MendingModMain.Settings.degradationMultiplier);
                item.HitPoints = Mathf.Max(1, item.HitPoints - hpLoss);
            }

            float severityScale = success ? 1f : 2f;
            bool qualityDropped = TryRollQualityDrop(item, missingHpFraction, 1f - successChance, severityScale);

            result.success = success;
            result.qualityDropped = qualityDropped;
            return result;
        }

        private static bool TryRollQualityDrop(Thing item, float missingHpFraction, float skillFactor, float severityScale)
        {
            if (!item.TryGetQuality(out QualityCategory quality))
                return false;

            float qualityFactor = ((int)quality + 1) / 7f;
            float dropChance = Mathf.Clamp01(missingHpFraction * qualityFactor * skillFactor * severityScale * MendingModMain.Settings.degradationMultiplier);

            if (!Rand.Chance(dropChance))
                return false;

            CompQuality compQuality = item.TryGetComp<CompQuality>();
            if (compQuality == null)
                return false;

            QualityCategory degraded = (QualityCategory)Mathf.Max((int)QualityCategory.Awful, (int)quality - 1);
            compQuality.SetQuality(degraded, ArtGenerationContext.Colony);
            return true;
        }

        // CostListCalculator.CostListAdjusted only rescales for the item's actual Stuff (e.g.
        // plasteel vs steel); it knows nothing about damage, so the missing-HP fraction is
        // applied here on top. It's a static method on CostListCalculator, not an instance
        // method on ThingDef.
        public static List<ThingDefCountClass> GetDynamicIngredientCosts(Thing item)
        {
            List<ThingDefCountClass> result = new List<ThingDefCountClass>();
            float missingHpFraction = GetMissingHpFraction(item);
            float multiplier = MendingModMain.Settings.degradationMultiplier;

            if (MendingModMain.Settings.simpleMode)
            {
                int steelCount = Mathf.Max(1, Mathf.CeilToInt(MendingModMain.Settings.simpleModeSteelPerRepair * missingHpFraction * multiplier));
                result.Add(new ThingDefCountClass(ThingDefOf.Steel, steelCount));
                return result;
            }

            List<ThingDefCountClass> adjusted = CostListCalculator.CostListAdjusted(item);
            if (adjusted.NullOrEmpty())
                return result;

            foreach (ThingDefCountClass cost in adjusted)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(cost.count * missingHpFraction * multiplier));
                result.Add(new ThingDefCountClass(cost.thingDef, count));
            }

            return result;
        }

        public static void AwardSkillXp(Thing item, Pawn pawn, float xpAmount)
        {
            if (pawn?.skills == null)
                return;

            pawn.skills.Learn(GetRelevantWorkSkill(item), xpAmount);
        }
    }
}
