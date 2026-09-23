using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Everything behind the right-click "Repair this" order: pick a bench, prove the job can
    // actually be done, and say exactly what is missing when it cannot.
    //
    // It runs TWICE per interaction and the difference matters. The float menu calls it with
    // commit=false to decide whether the option is enabled and what reason to print; clicking
    // the option calls it again with commit=true. Only the committing pass is allowed to change
    // anything - without that split, merely opening a right-click menu would add a bill to a
    // bench, which is a side effect no player asked for.
    public static class MendJobMaker
    {
        // A mend recipe is one whose worker is a RecipeWorker_Mend, the same test WorkGiver_Mend
        // uses, so a recipe another mod adds is picked up here without naming it.
        private static List<RecipeDef> cachedMendRecipes;

        private static List<RecipeDef> MendRecipes
        {
            get
            {
                if (cachedMendRecipes == null)
                {
                    cachedMendRecipes = DefDatabase<RecipeDef>.AllDefsListForReading
                        .Where(r => r.workerClass != null
                                    && typeof(RecipeWorker_Mend).IsAssignableFrom(r.workerClass)
                                    && !r.ingredients.NullOrEmpty())
                        .ToList();
                }

                return cachedMendRecipes;
            }
        }

        public static bool IsMendRecipe(RecipeDef recipe)
        {
            return recipe != null && MendRecipes.Contains(recipe);
        }

        // Cheap enough to run on every thing under the cursor: no map queries, no pathing.
        public static bool IsMendable(Thing item)
        {
            if (item == null || !item.def.useHitPoints || item.HitPoints >= item.MaxHitPoints)
                return false;

            return RecipeFor(item) != null;
        }

        private static RecipeDef RecipeFor(Thing item)
        {
            foreach (RecipeDef recipe in MendRecipes)
            {
                // ingredients[0] is the item being mended - see WorkGiver_Mend's header. Its
                // filter carries the 0~0.99 hit points band, so it doubles as the damage test.
                if (recipe.ingredients[0].filter.Allows(item))
                    return recipe;
            }

            return null;
        }

        public static bool TryMakeRepairJob(Pawn pawn, Thing item, bool commit, out Job job, out string reason)
        {
            job = null;
            reason = null;

            RecipeDef recipe = RecipeFor(item);
            if (recipe == null)
                return Fail("DynamicMending.RepairNotDamaged".Translate(), out reason);

            // --- the colonist ----------------------------------------------------------------
            if (recipe.requiredGiverWorkType != null && pawn.WorkTypeIsDisabled(recipe.requiredGiverWorkType))
                return Fail("DynamicMending.RepairIncapable".Translate(
                    pawn.LabelShort, recipe.requiredGiverWorkType.gerundLabel), out reason);

            SkillRequirement unmet = recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
            if (unmet != null)
                return Fail("DynamicMending.RepairSkillTooLow".Translate(
                    unmet.skill.LabelCap, unmet.minLevel), out reason);

            // Gear the colonist is wearing or wielding is not on the map, so it cannot be
            // forbidden, reserved or pathed to - it goes where they go. The worn-gear order asks
            // this question before anything is taken off, so it has to be answerable for it.
            bool held = HeldBy(pawn, item);

            if (!held && item.IsForbidden(pawn))
                return Fail("DynamicMending.RepairForbidden".Translate(), out reason);

            if (!held && !pawn.CanReserveAndReach(item, PathEndMode.ClosestTouch, Danger.Deadly))
                return Fail("DynamicMending.RepairUnreachableItem".Translate(), out reason);

            // --- the bench -------------------------------------------------------------------
            // Ordered by distance from the ITEM rather than from the colonist: they collect it
            // first and carry it, so that leg is the one worth keeping short.
            IntVec3 from = item.PositionHeld;
            List<Thing> benches = BenchesFor(pawn, recipe)
                .OrderBy(b => (b.Position - from).LengthHorizontalSquared)
                .ToList();

            if (benches.Count == 0)
                return Fail("DynamicMending.RepairNoBench".Translate(), out reason);

            // The nearest bench that fails gives the reason, but a later one that WORKS wins -
            // an unpowered bench next door must not mask a working one across the base.
            string benchReason = null;
            Thing bench = null;
            foreach (Thing candidate in benches)
            {
                string why = WhyBenchCannot(pawn, candidate, item);
                if (why == null)
                {
                    bench = candidate;
                    break;
                }

                if (benchReason == null)
                    benchReason = why;
            }

            if (bench == null)
                return Fail(benchReason, out reason);

            // --- the materials ---------------------------------------------------------------
            List<ThingDefCountClass> costs =
                MendingUtility.GetDynamicIngredientCosts(item, MendingUtility.GetSkillLevel(item, pawn));

            float radius = Mathf.Min(ExistingBill(bench, recipe, item)?.ingredientSearchRadius ?? 999f, 200f);

            List<ThingCount> materials = new List<ThingCount>();
            if (!WorkGiver_Mend.TryFindMaterials(costs, pawn, bench, radius, materials))
                return Fail(MissingMaterialsReason(costs, pawn, bench, radius), out reason);

            // --- commit ----------------------------------------------------------------------
            // Nothing above this line has changed anything, so a dry run can stop here. A held
            // item can only ever be dry-run: a job cannot target something that is not on the
            // map, so the worn-gear driver takes it off first and asks again.
            if (held)
                return !commit || Fail("DynamicMending.RepairUnreachableItem".Translate(), out reason);

            Bill bill = commit ? EnsureBill(bench, recipe, item) : ExistingBill(bench, recipe, item);
            if (bill == null && !commit)
                return true;            // a bill WOULD be added; the order is still valid

            if (bill == null)
                return Fail("DynamicMending.RepairNoBench".Translate(), out reason);

            job = JobMaker.MakeJob(MendingDefOf.DoMend, bench);
            job.bill = bill;
            job.targetQueueB = new List<LocalTargetInfo> { item };
            job.countQueue = new List<int> { 1 };
            job.targetQueueB.AddRange(materials.Select(tc => new LocalTargetInfo(tc.Thing)));
            job.countQueue.AddRange(materials.Select(tc => tc.Count));
            return true;
        }

        public static bool HeldBy(Pawn pawn, Thing item)
        {
            return (item.ParentHolder is Pawn_ApparelTracker apparel && apparel.pawn == pawn)
                || (item.ParentHolder is Pawn_EquipmentTracker equipment && equipment.pawn == pawn);
        }

        private static bool Fail(string why, out string reason)
        {
            reason = why;
            return false;
        }

        // Keyed off ThingDef.AllRecipes rather than recipe.recipeUsers: AllRecipes merges BOTH
        // directions of the link - the recipe's recipeUsers and the bench's own <recipes> list -
        // so a bench another mod attaches a mend recipe to is found either way round.
        private static IEnumerable<Thing> BenchesFor(Pawn pawn, RecipeDef recipe)
        {
            foreach (Building building in pawn.Map.listerBuildings.allBuildingsColonist)
            {
                if (building is IBillGiver && building.def.AllRecipes.Contains(recipe))
                    yield return building;
            }
        }

        // Every reason a specific bench cannot take this specific item, in the order a player
        // would want to hear them: the ones they can fix by walking over first.
        private static string WhyBenchCannot(Pawn pawn, Thing bench, Thing item)
        {
            if (bench.IsForbidden(pawn) || bench.IsBurning())
                return "DynamicMending.RepairBenchForbidden".Translate();

            if (!pawn.CanReserveAndReach(bench, PathEndMode.InteractionCell, Danger.Deadly))
                return "DynamicMending.RepairBenchUnreachable".Translate();

            if (bench.TryGetComp<CompMenderWasteBuffer>()?.IsFull == true)
                return "DynamicMending.RepairWasteFull".Translate();

            // Covers unpowered, broken down and out of fuel in one call - the same check
            // WorkGiver_Mend makes, so the float menu and the work scan cannot disagree.
            if (!((IBillGiver)bench).CurrentlyUsableForBills())
                return "DynamicMending.RepairBenchUnusable".Translate(bench.LabelShort);

            if (!MendingUtility.CanBenchRepair(bench.def, item))
            {
                MendingTechLimitExtension limits = bench.def.GetModExtension<MendingTechLimitExtension>();
                if (limits != null && !limits.allowComponents && MendingUtility.NeedsComponents(item))
                    return "DynamicMending.NeedsPoweredBench".Translate();

                TechLevel cap = limits?.maxTechLevel ?? TechLevel.Archotech;
                return "DynamicMending.TooAdvancedForBench".Translate(cap.ToStringHuman());
            }

            return null;
        }

        // The combined search failed; run each cost on its own to find which one is actually
        // short, because "missing 12 steel" is worth a great deal more to a player than
        // "not enough materials". If every cost passes alone, the shortfall is an overlap - two
        // costs wanting the same stack - and there is nothing single to name.
        private static string MissingMaterialsReason(List<ThingDefCountClass> costs, Pawn pawn,
                                                     Thing bench, float radius)
        {
            List<string> missing = new List<string>();

            foreach (ThingDefCountClass cost in costs)
            {
                List<ThingCount> one = new List<ThingCount>();
                if (!WorkGiver_Mend.TryFindMaterials(new List<ThingDefCountClass> { cost }, pawn, bench, radius, one))
                    missing.Add(cost.count + " " + cost.thingDef.label);
            }

            return missing.Count > 0
                ? "DynamicMending.RepairMissingMaterials".Translate(missing.ToCommaList(true))
                : "DynamicMending.RepairNotEnoughMaterials".Translate();
        }

        // A bill can only carry THIS order if it would actually accept the item: the same three
        // tests the work scan makes. Without the last two, an order could be attached to a bill
        // the work giver then refuses the target for, and the job would sit there doing nothing.
        private static Bill ExistingBill(Thing bench, RecipeDef recipe, Thing item)
        {
            foreach (Bill bill in ((IBillGiver)bench).BillStack)
            {
                if (bill.recipe != recipe || bill.suspended)
                    continue;

                if (!bill.IsFixedOrAllowedIngredient(item))
                    continue;

                if (bill is Bill_Mend mendBill && !mendBill.AllowsStuffOf(item))
                    continue;

                return bill;
            }

            return null;
        }

        // Reuses a bill this order added earlier rather than stacking up one per click. A bill
        // the PLAYER set up is left exactly as it is - bumping someone's "do forever" bill or
        // rewriting their target count would be meddling. Only a spent count bill, which is what
        // this order leaves behind, gets wound back up to one.
        private static Bill EnsureBill(Thing bench, RecipeDef recipe, Thing item)
        {
            BillStack stack = ((IBillGiver)bench).BillStack;

            Bill existing = ExistingBill(bench, recipe, item);
            if (existing != null)
            {
                if (existing is Bill_Production production
                    && production.repeatMode == BillRepeatModeDefOf.RepeatCount
                    && production.repeatCount <= 0)
                {
                    production.repeatCount = 1;
                }

                return existing;
            }

            // Pinned to the item's own material, so the bill left behind stays scoped to the
            // gear the order was about. Unstuffed items get no pin at all.
            //
            // Where Material Filter is installed the pin goes through ITS filters, not through
            // onlyStuff, and that is deliberate: those are the checkboxes the player sees in the
            // bill dialog, so a pin written there is visible and can be changed. A pin held in
            // onlyStuff as well would silently override whatever they then ticked.
            Bill_Mend bill = new Bill_Mend(recipe);
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = 1;

            if (!MaterialFilters.TryRestrictTo(bill, item.Stuff))
                bill.onlyStuff = item.Stuff;

            stack.AddBill(bill);
            return bill;
        }
    }
}
