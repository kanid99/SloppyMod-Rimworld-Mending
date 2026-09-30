using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MendingMod
{
    // Assumes a RecipeDef using RecipeWorker_Mend with a single ingredients[0] entry whose
    // filter matches "damaged, useHitPoints things" (the item to mend) — material costs are
    // not declared in XML at all, they come from MendingUtility.GetDynamicIngredientCosts.
    public class WorkGiver_Mend : WorkGiver_DoBill
    {
        // With more than one fixedBillGiverDef, vanilla's PotentialWorkThingRequest falls back to
        // the whole PotentialBillGiver group - every pawn, corpse, stove and fabrication bench on
        // the map (confirmed by decompiling WorkGiver_DoBill) - and so does its ShouldSkip.
        // JobGiver_Work prefers PotentialWorkThingsGlobal when it is non-null, so listing only
        // our benches here keeps every pawn's work scan down to a handful of things.
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return Benches(pawn.Map);
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            // A right-click order is exactly when a player wants to hear why nothing will
            // happen - a suspended bill included - so it always goes on to JobOnThing, which
            // says why. Skipping here drops the menu entry without a word.
            if (forced)
                return false;

            foreach (Thing thing in Benches(pawn.Map))
            {
                if (thing is IBillGiver billGiver && billGiver.BillStack.AnyShouldDoNow)
                    return false;
            }

            return true;
        }

        private IEnumerable<Thing> Benches(Map map)
        {
            if (def.fixedBillGiverDefs == null)
                yield break;

            foreach (ThingDef benchDef in def.fixedBillGiverDefs)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(benchDef))
                    yield return thing;
            }
        }

        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            // Right-click orders still arrive here for whatever was clicked, so only a forced
            // check is worth reporting - the scan above never offers anything else.
            if (!(thing is IBillGiver billGiver) || !ThingIsUsableBillGiver(thing))
            {
                if (forced)
                    DevLog(thing, "not a usable bill giver");

                return null;
            }

            Job job = TryMakeJob(pawn, thing, billGiver, forced, out string why);

            // The float menu only shows a greyed-out "Cannot mend..." entry when a reason has
            // been recorded; with none it drops the entry and a right-click on the bench does
            // nothing at all. Every refusal below therefore carries a reason, and it is only
            // reported here if this bench actually has a bill of ours - otherwise the weapon
            // work giver would complain about a bench that only has apparel bills on it.
            if (job == null && forced && why != null)
                JobFailReason.Is(why, "DynamicMending.MendAtBench".Translate(def.label, thing.LabelShort));

            return job;
        }

        // The bills on this bench that this work giver would run: ours, and of its work type.
        // Both mend recipes sit on the same benches but belong to different work types, and
        // vanilla's WorkGiver_DoBill honours requiredGiverWorkType - without it a colonist
        // allowed only Smithing would pick up apparel bills, and vice versa.
        private bool IsOurBill(Bill bill)
        {
            // IsAssignableFrom rather than exact type equality, so another mod can subclass
            // RecipeWorker_Mend to add its own mend recipe and still be picked up here.
            if (bill.recipe.workerClass == null
                || (!typeof(RecipeWorker_Mend).IsAssignableFrom(bill.recipe.workerClass)
                    && !MendJobMaker.IsRecycleRecipe(bill.recipe)))
                return false;

            return bill.recipe.requiredGiverWorkType == null || bill.recipe.requiredGiverWorkType == def.workType;
        }

        // why comes back null when there is nothing of ours on the bench to explain - no
        // bill for this work giver at all - and otherwise names the first reason it met, in
        // the order a player can act on them: the bench, then each bill in stack order.
        private Job TryMakeJob(Pawn pawn, Thing thing, IBillGiver billGiver, bool forced, out string why)
        {
            why = null;
            billGiver.BillStack.RemoveIncompletableBills();

            if (!billGiver.BillStack.Bills.Any(IsOurBill))
            {
                DevLog(thing, "no bill for " + def.defName);
                return null;
            }

            // A full waste container stops the bench until someone empties it.
            if (thing.TryGetComp<CompMenderWasteBuffer>()?.IsFull == true)
            {
                DevLog(thing, "waste container full");
                why = "DynamicMending.WasteFull".Translate();
                return null;
            }

            // Covers "the power is out" for the electric table, plus breakdown and refuelling -
            // without it a pawn will happily walk over and mend at a dead bench.
            if (!billGiver.CurrentlyUsableForBills())
            {
                DevLog(thing, "not currently usable for bills (unpowered / broken down?)");
                why = "DynamicMending.RepairBenchUnusable".Translate(thing.LabelShort);
                return null;
            }

            if (thing.IsForbidden(pawn) || thing.IsBurning())
            {
                DevLog(thing, "forbidden / burning");
                why = "DynamicMending.RepairBenchForbidden".Translate();
                return null;
            }

            if (!pawn.CanReserve(thing, 1, -1, null, forced))
            {
                DevLog(thing, "can't reserve");
                Pawn holder = pawn.Map.reservationManager.FirstRespectedReserver(thing, pawn);
                why = holder != null
                    ? "IsReservedBy".Translate(thing.LabelShort, holder.LabelShort).ToString()
                    : "DynamicMending.BenchInUse".Translate().ToString();
                return null;
            }

            foreach (Bill bill in billGiver.BillStack)
            {
                if (!IsOurBill(bill))
                    continue;

                // Only the first bill's reason is kept: it is the one the player put on top.
                // JobFailReason is also written as a side effect by the vanilla calls below, so
                // each is read back straight after its own check rather than at the end.
                string billWhy = null;
                Job job = TryBill(bill, pawn, thing, forced, ref billWhy);
                if (job != null)
                {
                    why = null;
                    return job;
                }

                if (why == null)
                    why = billWhy;
            }

            return null;
        }

        private Job TryBill(Bill bill, Pawn pawn, Thing thing, bool forced, ref string why)
        {
            // Also vanilla's, and the reason recipe skillRequirements exist. Neither of our
            // recipes declares any, but a mod patching one in should have it respected.
            SkillRequirement unmet = bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(pawn);
            if (unmet != null)
            {
                why = "UnderRequiredSkill".Translate(unmet.minLevel);
                return null;
            }

            if (!bill.ShouldDoNow())
            {
                if (Prefs.DevMode)
                    DevLog(thing, $"bill '{bill.Label}' ShouldDoNow=false");

                why = "DynamicMending.BillNotActive".Translate(bill.LabelCap);
                return null;
            }

            // PawnAllowedToStartAnew records its own reason (the bill's pawn or skill limits) as a
            // side effect, so it is cleared first and read back straight after.
            JobFailReason.Clear();
            if (!bill.PawnAllowedToStartAnew(pawn))
            {
                if (Prefs.DevMode)
                    DevLog(thing, $"bill '{bill.Label}' PawnAllowedToStartAnew=false");

                why = JobFailReason.HaveReason
                    ? JobFailReason.Reason
                    : "DynamicMending.BillPawnNotAllowed".Translate(pawn.LabelShort).ToString();
                return null;
            }

            List<ThingCount> chosen = new List<ThingCount>();

            // TryBuildMendIngredients says why it failed - nothing to repair, rejected as too
            // advanced, or found something but its materials are short, and which.
            if (!TryBuildMendIngredients(bill, pawn, thing, chosen, forced, out why))
                return null;

            Job job = JobMaker.MakeJob(MendingDefOf.DoMend, thing);
            job.bill = bill;
            job.targetQueueB = chosen.Select(tc => new LocalTargetInfo(tc.Thing)).ToList();
            job.countQueue = chosen.Select(tc => tc.Count).ToList();
            return job;
        }

        private static void DevLog(Thing thing, string message)
        {
            if (Prefs.DevMode)
                Log.Message($"[DynamicMending] {thing.LabelShort} @ {thing.Position}: {message}");
        }

        // Deliberately not named/shaped like WorkGiver_DoBill's own static
        // TryFindBestBillIngredients(Bill, Pawn, Thing, List<ThingCount>, List<IngredientCount>)
        // - that one searches recipe.ingredients filters, which this recipe doesn't declare
        // material entries for at all (the C# computes them dynamically instead).
        // Vanilla's own throttle for exactly this, which this work giver never used because it
        // replaced WorkGiver_DoBill.StartOrResumeBillJob wholesale. A bill with nothing to repair
        // is the normal resting state and also the most expensive one: ClosestThingReachable runs
        // a 30-region BFS and then, finding nothing, falls back to scanning the WHOLE group
        // globally - re-run for every bill on every pawn's work scan. Vanilla stamps a failed
        // bill with nextTickToSearchForIngredients and leaves it alone for 500-600 ticks; the
        // field is public, saves with the game, and a float-menu query bypasses it so right-click
        // never lies to the player.
        private static readonly IntRange ReCheckFailedBillTicksRange = new IntRange(500, 600);

        private bool TryBuildMendIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen,
                                             bool forced, out string why)
        {
            chosen.Clear();
            why = null;

            if (bill.recipe.ingredients.NullOrEmpty())
            {
                why = "DynamicMending.BillBroken".Translate(bill.LabelCap);
                return false;
            }

            bool interactive = forced || FloatMenuMakerMap.makingFor == pawn;
            if (!interactive && Find.TickManager.TicksGame <= bill.nextTickToSearchForIngredients)
                return false;

            // GenRadial's cell lookup table is capped at radius 200 (confirmed by decompiling
            // GenRadial.NumCellsInRadius) - anything above that logs a real vanilla Log.Error
            // every time it's hit. bill.ingredientSearchRadius defaults to 999 ("unlimited" in
            // the bill's own UI), so it has to be clamped before being handed to either
            // GenClosest.ClosestThingReachable below, and on to vanilla's ingredient search.
            float searchRadius = Mathf.Min(bill.ingredientSearchRadius, 200f);

            IngredientCount targetFilter = bill.recipe.ingredients[0];

            // A recycle bill runs through the same search, minus the parts that only make sense
            // for a repair: the item need not be damaged, there is no tech gate on breaking
            // something down, and there are no materials to gather.
            bool recycling = MendJobMaker.IsRecycleRecipe(bill.recipe);

            // Tracked so a bench that rejected everything on tech level can say so, rather than
            // reporting the same "nothing in range" as an empty stockpile would. Components are
            // tracked separately because "needs a powered bench" is a different fix for the player
            // than "this is beyond what hand tools reach".
            bool rejectedForTech = false;
            bool rejectedForComponents = false;

            System.Predicate<Thing> validator = t =>
                {
                    // Ordered cheapest and most selective first: almost nothing on the map is
                    // damaged gear, so reject on that before paying for a reservation lookup.
                    if ((!recycling && !IsDamaged(t)) || !targetFilter.filter.Allows(t))
                        return false;

                    // The BILL's filter as well as the recipe's. This read only the recipe's,
                    // which meant narrowing a mend bill in its own config dialog did nothing at
                    // all - every other workbench in the game honours this, via the same call.
                    if (!bill.IsFixedOrAllowedIngredient(t))
                        return false;

                    // And the material, if this bill is pinned to one. See Bill_Mend: no filter
                    // in the game can express this, so it is carried on the bill and checked here.
                    if (bill is Bill_Mend mendBill && !mendBill.AllowsStuffOf(t))
                        return false;

                    if (t.IsForbidden(pawn) || !pawn.CanReserve(t))
                        return false;

                    if (recycling)
                        return MendingUtility.CanEverRecycle(t);

                    if (!MendingUtility.CanBenchRepair(billGiver.def, t))
                    {
                        MendingTechLimitExtension limits = billGiver.def.GetModExtension<MendingTechLimitExtension>();
                        if (limits != null && !limits.allowComponents && MendingUtility.NeedsComponents(t))
                            rejectedForComponents = true;
                        else
                            rejectedForTech = true;

                        return false;
                    }

                    return true;
                };

            MendOrder order = (bill as Bill_Mend)?.order ?? MendOrder.Nearest;
            ThingRequestGroup group = TargetGroupFor(bill.recipe);

            // Nearest keeps vanilla's region-walking search, which is also the cheapest: it stops
            // at the first hit. Every other order has to see all the candidates to rank them.
            Thing first = order == MendOrder.Nearest
                ? GenClosest.ClosestThingReachable(
                    billGiver.Position,
                    billGiver.Map,
                    ThingRequest.ForGroup(group),
                    PathEndMode.ClosestTouch,
                    TraverseParms.For(pawn),
                    searchRadius,
                    validator)
                : RankedByOrder(order, group, pawn, billGiver, searchRadius, validator).FirstOrDefault();

            if (first == null)
            {
                if (rejectedForComponents)
                {
                    why = "DynamicMending.NeedsPoweredBench".Translate();
                }
                else if (rejectedForTech)
                {
                    TechLevel cap = billGiver.def.GetModExtension<MendingTechLimitExtension>().maxTechLevel;
                    why = "DynamicMending.TooAdvancedForBench".Translate(cap.ToStringHuman());
                }
                else if (recycling)
                {
                    why = "DynamicMending.NothingToRecycle".Translate(bill.LabelCap);
                }
                else
                {
                    float threshold = MendingModMain.Settings.autoRepairBelow;
                    why = threshold >= 0.995f
                        ? "DynamicMending.NothingToMend".Translate(bill.LabelCap).ToString()
                        : "DynamicMending.NothingToMendBelow".Translate(bill.LabelCap, threshold.ToStringPercent()).ToString();
                }

                if (Prefs.DevMode)
                    DevLog(billGiver, $"no reachable damaged item matching bill '{bill.Label}' within radius {searchRadius}");

                if (!interactive)
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;

                return false;
            }

            if (recycling)
            {
                chosen.Add(new ThingCount(first, 1));
                return true;
            }

            // The first target is the bill's own preference, but a missing material for it must
            // not sink the whole bill: this used to give up here, so one fur T-shirt with no fur
            // on the map stopped every other damaged item behind it from ever being mended. The
            // rest of the candidates are walked in the same order until one can be supplied.
            // Nearest falls back to straight-line distance for the tail, which only costs
            // anything on the rare pass where the first choice was short.
            IEnumerable<Thing> candidates = Enumerable.Repeat(first, 1).Concat(
                RankedByOrder(order, group, pawn, billGiver, searchRadius, validator).Where(t => t != first));

            // Each material search is a region walk of its own, so it is capped, and a cost that
            // already failed (say "8 heavy fur") is not searched for again on the next item.
            HashSet<string> failedCosts = new HashSet<string>();
            int searches = 0;
            Thing mendTarget = null;
            Thing firstShort = null;
            List<ThingDefCountClass> firstShortCosts = null;
            int shortItems = 0;
            List<ThingCount> materials = new List<ThingCount>();

            foreach (Thing candidate in candidates)
            {
                if (searches >= MaxMaterialSearches)
                    break;

                // Two-pass verification: every material cost is located (read-only) before any
                // of it is added to `chosen`. Nothing is reserved or consumed until JobOnThing
                // returns a fully-populated job, so a failed search leaves no partial state behind.
                List<ThingDefCountClass> costs =
                    MendingUtility.GetDynamicIngredientCosts(candidate, MendingUtility.GetSkillLevel(candidate, pawn));

                string costKey = string.Join(",", costs.Select(c => c.thingDef.defName + "x" + c.count));
                if (failedCosts.Contains(costKey))
                {
                    shortItems++;
                    continue;
                }

                searches++;
                if (TryFindMaterials(costs, pawn, billGiver, searchRadius, materials))
                {
                    mendTarget = candidate;
                    break;
                }

                failedCosts.Add(costKey);
                shortItems++;
                if (firstShort == null)
                {
                    firstShort = candidate;
                    firstShortCosts = costs;
                }

                if (Prefs.DevMode)
                    DevLog(billGiver, $"found target {candidate.LabelShort} but could not gather {costs.Count} material(s) within radius {searchRadius} - trying the next item");
            }

            if (mendTarget == null)
            {
                // Naming the exact shortfall re-runs the search once per material, so it is only
                // worked out when someone is looking at the menu; the work scan has no reader.
                if (interactive && firstShort != null)
                {
                    string missing = MendJobMaker.MissingMaterialsReason(firstShortCosts, pawn, billGiver, searchRadius);
                    why = shortItems > 1
                        ? "DynamicMending.ShortForAll".Translate(firstShort.LabelShort, missing, shortItems - 1).ToString()
                        : "DynamicMending.ShortFor".Translate(firstShort.LabelShort, missing).ToString();
                }
                else
                {
                    why = "DynamicMending.NoIngredientsOrTarget".Translate();
                }

                if (Prefs.DevMode)
                    DevLog(billGiver, $"bill '{bill.Label}': no damaged item in range has its repair materials available ({searches} checked)");

                if (!interactive)
                    bill.nextTickToSearchForIngredients =
                        Find.TickManager.TicksGame + ReCheckFailedBillTicksRange.RandomInRange;

                return false;
            }

            chosen.Add(new ThingCount(mendTarget, 1));
            chosen.AddRange(materials);

            // DescribeCost recomputes the whole cost breakdown and allocates its way through two
            // LINQ projections; without this guard it ran on every job created, with the result
            // thrown away unless dev mode was on.
            if (Prefs.DevMode)
            {
                DevLog(billGiver, "job ready: " + MendingUtility.DescribeCost(
                    mendTarget, MendingUtility.GetSkillLevel(mendTarget, pawn)));
            }

            return true;
        }

        // Distinct material searches one bill may try before giving up until its next re-check.
        private const int MaxMaterialSearches = 8;

        // Ranks every candidate in range, then yields, lazily and in rank order, the ones the
        // pawn can actually reach. The validator runs before any pathing and rejects nearly
        // everything - almost nothing on a map is damaged gear - so reachability, the expensive
        // part, is only ever asked of the few that survive it, and only as far as the caller reads.
        private static IEnumerable<Thing> RankedByOrder(MendOrder order, ThingRequestGroup group, Pawn pawn, Thing billGiver,
                                                        float radius, System.Predicate<Thing> validator)
        {
            float radiusSq = radius * radius;
            IntVec3 from = billGiver.Position;

            IEnumerable<Thing> candidates = billGiver.Map.listerThings.ThingsInGroup(group)
                .Where(t => t.Spawned
                            && (t.Position - from).LengthHorizontalSquared <= radiusSq
                            && validator(t));

            IOrderedEnumerable<Thing> ranked;
            switch (order)
            {
                case MendOrder.Nearest:
                    ranked = candidates.OrderBy(t => (t.Position - from).LengthHorizontalSquared);
                    break;
                case MendOrder.MostDamaged:
                    ranked = candidates.OrderBy(t => (float)t.HitPoints / t.MaxHitPoints);
                    break;
                case MendOrder.MostValuable:
                    ranked = candidates.OrderByDescending(t => t.MarketValue);
                    break;
                default:
                    ranked = candidates.OrderBy(t => t.MarketValue);
                    break;
            }

            // Distance breaks ties, so two identical parkas still go nearest-first.
            foreach (Thing t in ranked.ThenBy(t => (t.Position - from).LengthHorizontalSquared))
            {
                if (pawn.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly))
                    yield return t;
            }
        }

        // HaulableEver is every wood log, meal, chunk and steel bar on the map - thousands of
        // things on a developed colony, walked per bill, per bench, per pawn work scan. A mend
        // bill only ever wants apparel or a weapon, and both are region-indexed groups
        // (ThingRequestGroupUtility.StoreInRegion), so the search can be an order of magnitude
        // smaller AND still use the region traversal rather than a global sweep.
        private static readonly Dictionary<RecipeDef, ThingRequestGroup> targetGroupCache =
            new Dictionary<RecipeDef, ThingRequestGroup>();

        private static ThingRequestGroup TargetGroupFor(RecipeDef recipe)
        {
            if (targetGroupCache.TryGetValue(recipe, out ThingRequestGroup cached))
                return cached;

            bool anyApparel = false;
            bool anyWeapon = false;

            foreach (ThingDef def in recipe.fixedIngredientFilter.AllowedThingDefs)
            {
                if (def.IsApparel)
                    anyApparel = true;
                else if (def.IsWeapon)
                    anyWeapon = true;

                if (anyApparel && anyWeapon)
                    break;
            }

            // A filter that spans both, or matches neither, falls back to the broad group rather
            // than quietly missing valid targets.
            ThingRequestGroup group =
                anyApparel && !anyWeapon ? ThingRequestGroup.Apparel :
                anyWeapon && !anyApparel ? ThingRequestGroup.Weapon :
                ThingRequestGroup.HaulableEver;

            targetGroupCache[recipe] = group;
            return group;
        }

        // The auto-repair threshold, not just "any damage": this is the automatic scan. A
        // right-click order builds its own job and never comes through here.
        private static bool IsDamaged(Thing t)
        {
            return MendingUtility.BelowAutoRepairThreshold(t);
        }

        // Vanilla's own ingredient locator rather than a bespoke one. Hauling and storage mods
        // routinely patch this chain - TryFindBestFixedIngredients delegates to the same
        // TryFindBestIngredientsHelper and set-selection methods every workbench in the game uses
        // - so mend bills now inherit whatever those mods do instead of quietly opting out.
        //
        // It passes bill == null internally, which makes the selection read
        // IngredientCount.GetBaseCount() directly, so the dynamically computed amounts are used
        // as-is rather than re-derived from the recipe, which declares no materials at all.
        // internal rather than private: MendJobMaker runs the same search for the right-click
        // "repair this" order, and for the shortfall message behind it, so that the float menu
        // and the work scan can never disagree about whether the materials are there.
        internal static bool TryFindMaterials(List<ThingDefCountClass> costs, Pawn pawn, Thing billGiver,
                                              float searchRadius, List<ThingCount> found)
        {
            if (costs.NullOrEmpty())
                return true;

            List<IngredientCount> ingredients = new List<IngredientCount>(costs.Count);
            foreach (ThingDefCountClass cost in costs)
            {
                IngredientCount ingredient = new IngredientCount();
                ingredient.filter.SetAllow(cost.thingDef, true);
                ingredient.SetBaseCount(cost.count);
                ingredients.Add(ingredient);
            }

            // The helper clears whatever list it is handed, which is why this gets its own rather
            // than the one already holding the mend target.
            return TryFindBestFixedIngredients(ingredients, pawn, billGiver, found, searchRadius);
        }
    }
}
