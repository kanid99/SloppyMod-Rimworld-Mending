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

        // Damage drives work time on a log curve rather than linearly: log10(1 + 9x) still maps
        // 0 -> 0 and 1 -> 1, but rises steeply at the start, so a barely-scratched item costs a
        // meaningful share of the full repair time instead of finishing almost instantly, while
        // a nearly-destroyed one doesn't balloon past rebuilding it outright.
        private static float DamageWorkCurve(float missingHpFraction)
        {
            return Mathf.Log10(1f + 9f * Mathf.Clamp01(missingHpFraction));
        }

        // WorkToMake read off the thing itself is the game's own "how long did this take to
        // build" number and accounts for the item's stuff. recipeMaker.workAmount is left unset
        // on most apparel and weapons, so preferring it (as this used to) meant almost every
        // repair fell through to a flat 300 ticks and finished in a couple of seconds. The
        // bench's own WorkTableWorkSpeedFactor is applied by the caller, not here, so the
        // automated mender and the two tables can differ.
        public static float GetDynamicWorkAmount(Thing item)
        {
            if (item == null)
                return 300f;

            float baseWork = item.GetStatValue(StatDefOf.WorkToMake);

            if (baseWork <= 0f && item.def.recipeMaker != null)
                baseWork = item.def.recipeMaker.workAmount;

            if (baseWork <= 0f)
                baseWork = 300f;

            item.TryGetQuality(out QualityCategory quality);
            float qualityFactor = 1f + (int)quality * 0.15f;

            return baseWork * DamageWorkCurve(GetMissingHpFraction(item)) * qualityFactor
                * MendingModMain.Settings.repairWorkMultiplier;
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
        // A repair costs half of what the item took to build for an unskilled mender, falling to
        // a quarter of it at skill 20.
        private static float SkillCostFactor(int skillLevel)
        {
            return Mathf.Lerp(0.5f, 0.25f, Mathf.Clamp01(skillLevel / 20f));
        }

        // How much of that band the damage actually claims. Deliberately floored at half rather
        // than running straight off the damage curve to zero, so patching a lightly scuffed item
        // still costs real materials instead of rounding down to a token amount.
        private static float DamageCostFactor(Thing item)
        {
            return Mathf.Lerp(0.5f, 1f, DamageWorkCurve(GetMissingHpFraction(item)));
        }

        public static int GetSkillLevel(Thing item, Pawn pawn)
        {
            return pawn?.skills?.GetSkill(GetRelevantWorkSkill(item))?.Level ?? 0;
        }

        // skillLevel has to come from whoever is actually doing the repair, and the work giver
        // and the job driver must pass the same pawn: the driver consumes exactly the list the
        // work giver priced, so a mismatch would leave materials behind or come up short.
        public static List<ThingDefCountClass> GetDynamicIngredientCosts(Thing item, int skillLevel)
        {
            List<ThingDefCountClass> result = new List<ThingDefCountClass>();
            float costFraction = SkillCostFactor(skillLevel) * DamageCostFactor(item)
                * MendingModMain.Settings.degradationMultiplier;

            if (MendingModMain.Settings.simpleMode)
            {
                int steelCount = Mathf.Max(1, Mathf.CeilToInt(MendingModMain.Settings.simpleModeSteelPerRepair * costFraction));
                result.Add(new ThingDefCountClass(ThingDefOf.Steel, steelCount));
                return result;
            }

            List<ThingDefCountClass> adjusted = CostListCalculator.CostListAdjusted(item);
            if (adjusted.NullOrEmpty())
                return result;

            foreach (ThingDefCountClass cost in adjusted)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt(cost.count * costFraction));
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
