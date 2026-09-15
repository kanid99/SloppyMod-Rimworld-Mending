using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    public struct MendResult
    {
        public bool success;
        public bool qualityDropped;
        public QualityCategory newQuality;
    }

    // Shared by RecipeWorker_Mend (pawn skill) and Building_AutomatedMender (fixed skill 10)
    // so the two repair paths can never drift into different odds for the same job.
    public static class MendingUtility
    {
        // The vanilla spacer-tier materials. Used only as a fallback: an item that declares no
        // tech level of its own is judged by whether it actually needs any of these to build.
        private static readonly HashSet<string> SpacerMaterials = new HashSet<string>
        {
            "Plasteel", "Synthread", "Hyperweave", "ComponentSpacer"
        };

        // An item's own techLevel ignores what it's made of - a plasteel knife is a Neolithic
        // def carrying a spacer-tier material - so the stuff counts too, whichever is higher.
        // RimWorld only config-errors on weapons missing a tech level, so apparel (especially
        // modded) is routinely Undefined; that case falls through to the material check rather
        // than being treated as too advanced, which would quietly block a lot of modded gear.
        public static TechLevel GetEffectiveTechLevel(Thing item)
        {
            TechLevel tech = item.def.techLevel;

            if (item.Stuff != null && item.Stuff.techLevel > tech)
                tech = item.Stuff.techLevel;

            if (tech != TechLevel.Undefined)
                return tech;

            if (item.Stuff != null && SpacerMaterials.Contains(item.Stuff.defName))
                return TechLevel.Spacer;

            List<ThingDefCountClass> costs = CostListCalculator.CostListAdjusted(item);
            if (!costs.NullOrEmpty())
            {
                foreach (ThingDefCountClass cost in costs)
                {
                    if (SpacerMaterials.Contains(cost.thingDef.defName))
                        return TechLevel.Spacer;
                }
            }

            return TechLevel.Undefined;
        }

        // Working blind on gear the colony can't yet build: materials wasted, time taken, and the
        // odds of ruining it all scale by these.
        private const float UnfamiliarCostFactor = 2.0f;
        private const float UnfamiliarRiskFactor = 2.5f;
        private const float UnfamiliarTimeFactor = 1.5f;

        // Whether the colony has researched how to build this thing. Items no recipe produces -
        // quest rewards, mechanoid gear, trader-only goods - have no research gate at all and
        // count as known, so they repair at normal rates.
        public static bool ColonyHasTechFor(Thing item)
        {
            RecipeMakerProperties maker = item.def.recipeMaker;
            if (maker == null)
                return true;

            if (maker.researchPrerequisite != null && !maker.researchPrerequisite.IsFinished)
                return false;

            if (maker.researchPrerequisites != null)
            {
                foreach (ResearchProjectDef research in maker.researchPrerequisites)
                {
                    if (!research.IsFinished)
                        return false;
                }
            }

            return true;
        }

        public static bool IsUnfamiliar(Thing item)
        {
            return MendingModMain.Settings.enableTechRequirements
                && MendingModMain.Settings.penaliseUnresearched
                && !ColonyHasTechFor(item);
        }

        // The automated repair center passes ignoreResearch. Working blind is a statement about a
        // COLONIST - they have never built the thing and are guessing - and it does not transfer to
        // a machine that measures what is in front of it and works to a fixed tolerance. Once the
        // machine is built its only limit is that it always works at skill 10.
        private static bool Unfamiliar(Thing item, bool ignoreResearch)
        {
            return !ignoreResearch && IsUnfamiliar(item);
        }

        // Undefined means "nothing known and no spacer materials found", which stays allowed.
        public static bool CanBenchRepair(ThingDef benchDef, Thing item)
        {
            if (!MendingModMain.Settings.enableTechRequirements || !MendingModMain.Settings.enforceTechLimits)
                return true;

            MendingTechLimitExtension limits = benchDef?.GetModExtension<MendingTechLimitExtension>();

            // Separate from the tech-level cap on purpose: plenty of industrial-tier gear is built
            // around components, and a bench with no power has no way to rebuild one.
            if (limits != null && !limits.allowComponents && NeedsComponents(item))
                return false;

            TechLevel cap = limits?.maxTechLevel ?? TechLevel.Archotech;
            TechLevel tech = GetEffectiveTechLevel(item);

            return tech == TechLevel.Undefined || tech <= cap;
        }

        // Reads the stuff-adjusted cost list rather than the raw def, so a component only counts
        // when this particular item actually needed one.
        public static bool NeedsComponents(Thing item)
        {
            if (item.Stuff == ThingDefOf.ComponentIndustrial || item.Stuff == ThingDefOf.ComponentSpacer)
                return true;

            List<ThingDefCountClass> costs = CostListCalculator.CostListAdjusted(item);
            if (costs.NullOrEmpty())
                return false;

            foreach (ThingDefCountClass cost in costs)
            {
                if (cost.thingDef == ThingDefOf.ComponentIndustrial || cost.thingDef == ThingDefOf.ComponentSpacer)
                    return true;
            }

            return false;
        }

        // Which skill a repair TRAINS: the item's own, so a modded item that declares something
        // unusual grants XP there. Only used for the XP award.
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

        // How much a mender squanders relative to a perfect job. A skill 20 mender is the
        // no-waste baseline at 1.0: they spend exactly the fraction of the item that's missing.
        // Below that, waste climbs on a quadratic so it stays gentle through the mid skills and
        // bites hard at the bottom - 2.0 at skill 10 and 5.0 at skill 0, where a repair costs
        // the item's whole build cost and you may as well make a new one.
        public static float SkillWasteMultiplier(int skillLevel)
        {
            float shortfall = (20f - Mathf.Clamp(skillLevel, 0, 20)) / 20f;
            return 1f + 4f * shortfall * shortfall;
        }

        // The same curve normalised so skill 10 sits at 1.0, for the axes where skill 10 should
        // read as "normal" rather than as a penalty: time taken and waste produced.
        public static float SkillEffortFactor(int skillLevel)
        {
            return SkillWasteMultiplier(skillLevel) / 2f;
        }

        // WorkToMake read off the thing itself is the game's own "how long did this take to
        // build" number and accounts for the item's stuff. recipeMaker.workAmount is left unset
        // on most apparel and weapons, so preferring it (as this used to) meant almost every
        // repair fell through to a flat 300 ticks and finished in a couple of seconds. The
        // bench's own WorkTableWorkSpeedFactor is applied by the caller, not here, so the
        // automated mender and the two tables can differ.
        public static float GetDynamicWorkAmount(Thing item, int skillLevel, bool ignoreResearch = false)
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
                * SkillEffortFactor(skillLevel)
                * (Unfamiliar(item, ignoreResearch) ? UnfamiliarTimeFactor : 1f)
                * MendingModMain.Settings.repairWorkMultiplier;
        }

        // Skill is the dominant term, then how much of the item is being rebuilt, then how fine
        // it is. Tuned around skill 10 as the normal case: on a 20%-damaged normal-quality item
        // that's roughly 1% failure at skill 20, 13% at skill 10 and 25% at skill 1.
        private static float FailureChance(Thing item, int skillLevel, float missingHpFraction, bool ignoreResearch)
        {
            item.TryGetQuality(out QualityCategory quality);

            float skillChance = Mathf.Lerp(0.45f, 0.02f, Mathf.Clamp01(skillLevel / 20f));
            float damageFactor = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(missingHpFraction));
            float qualityFactor = 1f + (int)quality * 0.05f;

            return Mathf.Clamp01(skillChance * damageFactor * qualityFactor
                * (Unfamiliar(item, ignoreResearch) ? UnfamiliarRiskFactor : 1f)
                * MendingModMain.Settings.degradationMultiplier);
        }

        public static MendResult ResolveRepair(Thing item, int skillLevel, bool ignoreResearch = false)
        {
            float missingHpFraction = GetMissingHpFraction(item);
            MendResult result = default;

            if (missingHpFraction <= 0f)
            {
                result.success = true;
                return result;
            }

            float failChance = FailureChance(item, skillLevel, missingHpFraction, ignoreResearch);
            bool success = !Rand.Chance(failChance);

            if (success)
            {
                item.HitPoints = item.MaxHitPoints;
            }
            else if (MendingModMain.Settings.hpLossOnFailure)
            {
                int hpLoss = Mathf.RoundToInt(item.MaxHitPoints * missingHpFraction * failChance * 0.5f * MendingModMain.Settings.degradationMultiplier);
                item.HitPoints = Mathf.Max(1, item.HitPoints - hpLoss);
            }
            // Otherwise a failure costs the work and the materials but leaves the item as it was.

            bool qualityDropped = false;
            if (QualityLossAllowed(success))
            {
                float severityScale = success ? 1f : 2f;
                qualityDropped = TryRollQualityDrop(item, missingHpFraction, failChance, severityScale);
            }

            if (qualityDropped)
                item.TryGetQuality(out result.newQuality);

            result.success = success;
            result.qualityDropped = qualityDropped;
            return result;
        }

        private static bool QualityLossAllowed(bool success)
        {
            switch (MendingModMain.Settings.qualityLossMode)
            {
                case QualityLossMode.Never:
                    return false;
                case QualityLossMode.FailureOnly:
                    return !success;
                default:
                    return true;
            }
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
        // Which skill a repair is PRICED against. Deliberately Crafting, not the item's own
        // recipe skill, for two reasons.
        //
        // First, it has to be the skill the bill gates on, or a pawn can pass an "allowed skill
        // range" check and then be charged as though they were someone else.
        //
        // Second, GetRelevantWorkSkill can name a skill this pawn does not have - a modded item
        // declaring a modded skill - and Pawn_SkillTracker.GetSkill does NOT return null for
        // that. It logs an error and hands back skills[0], which is Shooting. A Crafting 11
        // colonist was then priced as though unskilled, and since the cost fraction is capped at
        // the item's whole build cost, a moderately damaged item cost exactly as much to repair
        // as to build new.
        public static int GetSkillLevel(Thing item, Pawn pawn)
        {
            return pawn?.skills?.GetSkill(SkillDefOf.Crafting)?.Level ?? 0;
        }

        // Dev-mode breakdown, so a surprising number can be read rather than guessed at.
        public static string DescribeCost(Thing item, int skillLevel, bool ignoreResearch = false)
        {
            float missing = GetMissingHpFraction(item);
            float skillMult = SkillWasteMultiplier(skillLevel);
            bool unfamiliar = Unfamiliar(item, ignoreResearch);
            float raw = missing * skillMult * (unfamiliar ? UnfamiliarCostFactor : 1f);
            float fraction = Mathf.Clamp01(raw) * MendingModMain.Settings.degradationMultiplier;

            List<ThingDefCountClass> baseCosts = CostListCalculator.CostListAdjusted(item);
            string baseList = baseCosts.NullOrEmpty()
                ? "none"
                : string.Join(", ", baseCosts.Select(c => $"{c.thingDef.defName} x{c.count}"));
            string finalList = string.Join(", ",
                GetDynamicIngredientCosts(item, skillLevel, ignoreResearch)
                    .Select(c => $"{c.thingDef.defName} x{c.count}"));

            return $"{item.LabelShortCap}: hp {item.HitPoints}/{item.MaxHitPoints} (missing {missing:P0}) "
                 + $"| skill {skillLevel} -> x{skillMult:F2} | unfamiliar {unfamiliar} "
                 + $"| raw {raw:F2} -> fraction {fraction:P0}{(raw > 1f ? " (CAPPED at build cost)" : "")} "
                 + $"| build cost [{baseList}] -> repair [{finalList}]";
        }

        // skillLevel has to come from whoever is actually doing the repair, and the work giver
        // and the job driver must pass the same pawn: the driver consumes exactly the list the
        // work giver priced, so a mismatch would leave materials behind or come up short.
        public static List<ThingDefCountClass> GetDynamicIngredientCosts(Thing item, int skillLevel, bool ignoreResearch = false)
        {
            List<ThingDefCountClass> result = new List<ThingDefCountClass>();

            // Free repairs: an empty cost list flows through every caller cleanly - the work giver
            // looks for no materials, the job driver consumes none, and the automated mender's
            // stock check passes trivially.
            if (!MendingModMain.Settings.requireResources)
                return result;

            // The fraction of the item that's actually missing is what a flawless repair costs;
            // everything above that is the mender's waste. Capped at the whole build cost, since
            // past that point rebuilding the item outright would be cheaper than repairing it.
            float costFraction = Mathf.Clamp01(GetMissingHpFraction(item)
                    * SkillWasteMultiplier(skillLevel)
                    * (Unfamiliar(item, ignoreResearch) ? UnfamiliarCostFactor : 1f))
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

        private static readonly Color MoteSuccess = new Color(0.45f, 0.85f, 0.45f);
        private static readonly Color MoteFailure = new Color(0.90f, 0.35f, 0.30f);
        private static readonly Color MoteQuality = new Color(0.95f, 0.70f, 0.25f);

        // Floating text over whoever did the work - the pawn at a bench, the machine itself for
        // the repair center. Without it a failed repair and a quality loss are both silent, and
        // the only way to notice is to check the item's hit points afterwards.
        public static void ShowRepairResult(MendResult result, Thing at)
        {
            if (at?.Map == null)
                return;

            string text;
            Color colour;

            if (!result.success && result.qualityDropped)
            {
                text = "DynamicMending.MoteFailedQuality".Translate(result.newQuality.GetLabel());
                colour = MoteFailure;
            }
            else if (!result.success)
            {
                text = "DynamicMending.MoteFailed".Translate();
                colour = MoteFailure;
            }
            else if (result.qualityDropped)
            {
                text = "DynamicMending.MoteQualityLost".Translate(result.newQuality.GetLabel());
                colour = MoteQuality;
            }
            else
            {
                text = "DynamicMending.MoteRepaired".Translate();
                colour = MoteSuccess;
            }

            MoteMaker.ThrowText(at.DrawPos + new Vector3(0f, 0f, 0.6f), at.Map, text, colour);
        }

        public static void AwardSkillXp(Thing item, Pawn pawn, float xpAmount)
        {
            if (pawn?.skills == null)
                return;

            SkillDef skill = GetRelevantWorkSkill(item);

            // Same trap as the pricing path: Pawn_SkillTracker.Learn routes through GetSkill,
            // which for a skill this pawn does not have logs an error and dumps the XP into
            // skills[0] - Shooting. A modded item naming a modded skill would quietly train the
            // wrong one and spam the log every repair, so fall back on purpose instead.
            if (!pawn.skills.skills.Any(record => record.def == skill))
                skill = SkillDefOf.Crafting;

            pawn.skills.Learn(skill, xpAmount);
        }
    }
}
