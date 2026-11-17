using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // What the repair centre does with tainted apparel that reaches its item spot.
    public enum TaintedMode : byte
    {
        Repair,
        Recycle,
        Reject,
    }

    // Tick-driven state machine: Idle scans its input spots for work, Working counts down a
    // dynamically-computed work duration on the swallowed item, then ejects it out the front.
    // Runs the same MendingUtility formulas as the pawn path, but with a fixed skill level
    // (functions as a skill-10 worker per design) instead of reading a pawn's skill.
    //
    // Gear and materials feed in along the back edge and finished items leave by a single port
    // at the front. Anything sitting in those cells counts, whether it's loose
    // on the ground, in a stockpile zone, or in a storage building - vanilla storage keeps its
    // contents spawned on its own cells, so a plain cell scan already sees shelves and the like.
    // Buildings that hold items in an internal container instead (deep-storage mods, and Vanilla
    // Furniture Expanded's conveyors) are read through IThingHolder, which is also what makes
    // conveyor feeding work with no dependency on that mod.
    [StaticConstructorOnStartup]
    public class Building_AutomatedMender : Building, IThingHolder
    {
        // The skill it works at at 100% overclock, as it always did before overclocking existed.
        private const int FixedSkillLevel = 10;

        // Overclocking, after Vanilla Furniture Expanded - Factory's machines: the same 10%-200%
        // range, work time divided by the setting, and power and heat both scaled by
        // 0.25 + 0.75 x setting squared (PipeSystem's CompPowerTrader_Overclocked and
        // CompHeatPusherPowered_Overclocked). Unlike theirs it also trades speed for care, which
        // is what a repair is about: slowed right down it works like a master and almost never
        // botches a job; pushed hard it rushes like a novice.
        public const float MinOverclock = 0.1f;
        public const float MaxOverclock = 2f;
        private const float IdleLoadFraction = 0.25f;
        private const int SlowestSkill = 20;
        private const int FastestSkill = 5;

        // Built in a static constructor context so the materials are created after the graphics
        // system is up, which is what [StaticConstructorOnStartup] guarantees.
        private static readonly Material BarFilledMat =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.24f, 0.79f, 0.85f));
        private static readonly Material BarUnfilledMat =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.12f, 0.13f, 0.14f));

        private enum MenderState : byte { Idle, Working, Ejecting }

        private ThingOwner<Thing> innerContainer;
        private Thing currentItem;
        private int workTicksRemaining;
        private int workTicksTotal;
        private float overclock = 1f;
        private int jobSkill = FixedSkillLevel;     // fixed when a job starts, so its cost and its result agree
        private MenderState state = MenderState.Idle;
        private string idleReason;

        // What the machine will take. Anything on the item spot it will not take - filtered out,
        // undamaged, above the auto-repair threshold, beyond its tech - goes out a reject chute
        // rather than sitting on the spot and stalling the belt behind it.
        private ThingFilter acceptFilter;
        private TaintedMode taintedMode = TaintedMode.Repair;

        // What the machine breaks down instead of repairing - the repair centre's answer to a
        // bench's recycle bill. Checked before the accept filter, so gear in both is recycled,
        // and damage does not matter, as it does not for a recycle bill. Starts empty, which
        // leaves a machine doing exactly what it did before this existed.
        private ThingFilter recycleFilter;

        // Things inside the machine waiting for a reject chute: rejected gear, and whatever a
        // recycle broke down into. Held here while both chutes are blocked, exactly as a repaired
        // item waits for the output spot.
        private List<Thing> rejectQueue = new List<Thing>();
        private bool recycling;
        private string lastRejected;

        public Building_AutomatedMender()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return innerContainer;
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_References.Look(ref currentItem, "currentItem");
            Scribe_Values.Look(ref workTicksRemaining, "workTicksRemaining");
            Scribe_Values.Look(ref workTicksTotal, "workTicksTotal");
            Scribe_Values.Look(ref overclock, "overclock", 1f);
            Scribe_Values.Look(ref jobSkill, "jobSkill", FixedSkillLevel);
            Scribe_Values.Look(ref state, "state");
            Scribe_Deep.Look(ref acceptFilter, "acceptFilter");
            Scribe_Deep.Look(ref recycleFilter, "recycleFilter");
            Scribe_Values.Look(ref taintedMode, "taintedMode", TaintedMode.Repair);
            Scribe_Collections.Look(ref rejectQueue, "rejectQueue", LookMode.Reference);
            Scribe_Values.Look(ref recycling, "recycling");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // A machine built before these existed loads with neither.
                acceptFilter = acceptFilter ?? DefaultAcceptFilter();
                recycleFilter = recycleFilter ?? new ThingFilter();
                rejectQueue = rejectQueue ?? new List<Thing>();
                rejectQueue.RemoveAll(t => t == null);
            }
        }

        // The tree the accept-filter window shows, and everything a new machine accepts: all
        // apparel and all weapons, which is what the two mend recipes cover.
        private static ThingFilter parentFilter;

        public static ThingFilter ParentFilter
        {
            get
            {
                if (parentFilter == null)
                {
                    parentFilter = new ThingFilter();
                    parentFilter.SetAllow(ThingCategoryDefOf.Apparel, true);
                    parentFilter.SetAllow(ThingCategoryDefOf.Weapons, true);
                }

                return parentFilter;
            }
        }

        private static ThingFilter DefaultAcceptFilter()
        {
            ThingFilter filter = new ThingFilter();
            filter.CopyAllowancesFrom(ParentFilter);
            return filter;
        }

        public ThingFilter AcceptFilter => acceptFilter ?? (acceptFilter = DefaultAcceptFilter());

        public ThingFilter RecycleFilter => recycleFilter ?? (recycleFilter = new ThingFilter());

        private bool PowerOn
        {
            get
            {
                // Asked every tick; the comp list never changes after the building is made, so
                // the lookup is done once.
                if (!powerLooked)
                {
                    powerComp = GetComp<CompPowerTrader>();
                    powerLooked = true;
                }

                return powerComp == null || powerComp.PowerOn;
            }
        }

        private CompPowerTrader powerComp;
        private bool powerLooked;

        private CompPowerTrader PowerTrader
        {
            get
            {
                if (!powerLooked)
                {
                    powerComp = GetComp<CompPowerTrader>();
                    powerLooked = true;
                }

                return powerComp;
            }
        }

        // Checks in a row that found nothing on the item spot. Not saved: a loaded machine just
        // starts out attentive again.
        private int emptyScans;
        private bool spotWasEmpty;
        private const int EmptyScansBeforeSlowing = 8;
        private const int ScanInterval = 30;
        private const int SlowScanInterval = 90;

        // Uninstalling or destroying the machine comes through here - MinifyUtility.MakeMinified
        // deinstalls with DeSpawnOrDeselect before wrapping the building up - and anything still
        // inside would travel into the minified crate with it: unreachable while it sits there,
        // and destroyed outright if that crate ever is. So the item goes back on the floor first.
        // The state machine is reset too, or a reinstalled machine wakes up believing it is
        // mid-repair on something it no longer holds, and jams on "output blocked" forever.
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned && innerContainer.Count > 0)
                innerContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);

            currentItem = null;
            workTicksRemaining = 0;
            workTicksTotal = 0;
            idleReason = null;
            state = MenderState.Idle;
            rejectQueue.Clear();         // dropped with everything else, just above
            recycling = false;

            base.DeSpawn(mode);
        }

        protected override void Tick()
        {
            base.Tick();

            if (!PowerOn)
                return;

            // Vanilla's heat pusher works in one-second steps too; CompPowerTrader resets its draw
            // to the def's whenever power comes back on, so it is re-applied rather than set once.
            if (this.IsHashIntervalTick(60))
                ApplyOverclockLoad();

            // Twice a second is plenty for noticing new input or a cleared output spot, and avoids
            // re-scanning the spots on every one of the 60 ticks per second. A machine whose item
            // spot has stayed empty for a few seconds looks less often still, until something
            // turns up; one waiting on materials, the waste container or a chute keeps the pace.
            if (state == MenderState.Idle)
            {
                if (this.IsHashIntervalTick(emptyScans >= EmptyScansBeforeSlowing ? SlowScanInterval : ScanInterval))
                {
                    spotWasEmpty = false;
                    TryStartMend();
                    if (!spotWasEmpty)
                        emptyScans = 0;
                    else if (emptyScans < EmptyScansBeforeSlowing)
                        emptyScans++;
                }
            }
            else if (state == MenderState.Ejecting)
            {
                if (this.IsHashIntervalTick(ScanInterval))
                    TryEject();
            }
            else
            {
                TickWork();
            }
        }

        private CellRect Footprint => this.OccupiedRect();

        public IntVec3 ItemInputCell => MenderSpots.ItemInputCell(Footprint, Rotation);

        public IEnumerable<IntVec3> ResourceInputCells => MenderSpots.ResourceInputCells(Footprint, Rotation);

        public IntVec3 OutputCell => MenderSpots.OutputCell(Footprint, Rotation);

        public List<IntVec3> RejectCells => MenderSpots.RejectCells(Footprint, Rotation);

        public bool IsRepairing => state == MenderState.Working;

        public float Overclock
        {
            get => overclock;
            set
            {
                float next = Mathf.Clamp(value, MinOverclock, MaxOverclockNow);
                if (Mathf.Approximately(next, overclock))
                    return;

                // A job already running speeds up or slows down from where it is, as VFE's
                // processes do when the slider moves; the skill it was started at stands.
                if (state == MenderState.Working)
                {
                    workTicksRemaining = Mathf.Max(1, Mathf.RoundToInt(workTicksRemaining * overclock / next));
                    workTicksTotal = Mathf.Max(1, Mathf.RoundToInt(workTicksTotal * overclock / next));
                }

                overclock = next;
            }
        }

        // 10 at 100%, rising to 20 at the slowest setting and falling to 5 at 200%.
        public static int SkillAt(float setting)
        {
            // Beyond 200% - reachable only with VFE Factory's boosters - it keeps getting worse at
            // the same rate, down to skill 0.
            float skill = setting <= 1f
                ? Mathf.Lerp(SlowestSkill, FixedSkillLevel, (setting - MinOverclock) / (1f - MinOverclock))
                : FixedSkillLevel - (FixedSkillLevel - FastestSkill) * (setting - 1f) / (MaxOverclock - 1f);
            return Mathf.Clamp(Mathf.RoundToInt(skill), 0, 20);
        }

        // Vanilla Expanded Framework's two factory stats, raised and lowered by VFE Factory's
        // booster and heatsink facilities once the compat patch links them to this building.
        // Looked up by name: without the framework they do not exist and both read as 1.
        private static StatDef maxOverclockStat;
        private static StatDef heatFactorStat;
        private static bool statsLooked;

        private float FactoryStat(bool overclockStat)
        {
            if (!statsLooked)
            {
                maxOverclockStat = DefDatabase<StatDef>.GetNamedSilentFail("VEF_BuildingMaxOverclockFactor");
                heatFactorStat = DefDatabase<StatDef>.GetNamedSilentFail("VEF_FactoryHeatPerSecondFactor");
                statsLooked = true;
            }

            StatDef stat = overclockStat ? maxOverclockStat : heatFactorStat;
            return stat == null ? 1f : this.GetStatValue(stat);
        }

        public float MaxOverclockNow => MaxOverclock * Mathf.Max(1f, FactoryStat(true));

        public float HeatFactor => Mathf.Max(0f, FactoryStat(false));

        public static float LoadFactorAt(float setting) => IdleLoadFraction + (1f - IdleLoadFraction) * setting * setting;

        public int EffectiveSkill => SkillAt(overclock);

        public float BasePowerConsumption => PowerTrader?.Props.PowerConsumption ?? 0f;

        public float BaseHeatPerSecond => GetComp<CompHeatPusherMending>()?.Props.heatPerSecond ?? 0f;

        // Power is drawn at the overclocked rate whenever the machine is on, working or not, as
        // on VFE's machines; heat only while a job runs (CompHeatPusherMending).
        private void ApplyOverclockLoad()
        {
            // A booster taken away lowers the ceiling; VFE pulls its own machines back the same way.
            if (overclock > MaxOverclockNow)
                Overclock = MaxOverclockNow;

            CompPowerTrader power = PowerTrader;
            if (power != null && power.PowerOn)
                power.PowerOutput = -power.Props.PowerConsumption * LoadFactorAt(overclock);

            if (IsRepairing)
                GetComp<CompHeatPusherMending>()?.PushHeat(LoadFactorAt(overclock) * HeatFactor);
        }

        private IEnumerable<Thing> ThingsOn(IntVec3 cell)
        {
            if (!cell.InBounds(Map))
                yield break;

            foreach (Thing thing in cell.GetThingList(Map))
            {
                if (thing.def.category == ThingCategory.Item)
                    yield return thing;

                if (thing is IThingHolder holder && thing != this)
                {
                    ThingOwner owner = holder.GetDirectlyHeldThings();
                    if (owner == null)
                        continue;

                    foreach (Thing held in owner)
                    {
                        if (held.def.category == ThingCategory.Item)
                            yield return held;
                    }
                }
            }
        }

        private IEnumerable<Thing> ThingsOn(IEnumerable<IntVec3> cells)
        {
            return cells.SelectMany(ThingsOn);
        }

        // Only apparel and weapons are mendable (matching the two recipes), and checking that
        // rather than useHitPoints alone keeps deteriorated resource stacks out of the input.
        //
        // Deliberately NOT gated on research. Once this machine is built it repairs anything put
        // in front of it; its one limitation is that it always works at skill 10, never better.
        // The bench tech cap is still consulted so a def that declares one is honoured, but this
        // building carries no cap by default.
        private bool IsMendable(Thing thing)
        {
            return IsGear(thing)
                && thing.HitPoints < thing.MaxHitPoints
                && !IsOffLimits(thing)
                && MendingUtility.CanBenchRepair(def, thing);
        }

        // Both pawn benches honour forbidding; the machine did not, so it would happily swallow a
        // stack the player had explicitly set aside. Forbidding is the natural way to say "not
        // this one", and it should mean the same thing on a spot as it does anywhere else.
        private bool IsOffLimits(Thing thing)
        {
            return thing.IsForbidden(Faction ?? Faction.OfPlayer);
        }

        // Any apparel or weapon, damaged or not: undamaged gear on the item spot is now something
        // to send back out, not something to ignore while it blocks the belt behind it.
        private static bool IsGear(Thing thing)
        {
            return (thing.def.IsApparel || thing.def.IsWeapon) && thing.def.useHitPoints;
        }

        private enum Intake : byte { Repair, Recycle, Reject }

        // What to do with one piece of gear on the item spot, and - for a reject - why, so the
        // inspect pane can say. Order matters: tainted handling first, because "recycle it" and
        // "reject it" should apply whatever else is true of the item.
        private Intake Decide(Thing thing, out string why)
        {
            why = null;

            if (thing is Apparel apparel && apparel.WornByCorpse && taintedMode != TaintedMode.Repair)
            {
                if (taintedMode == TaintedMode.Recycle && MendingUtility.CanEverRecycle(thing))
                    return Intake.Recycle;

                why = "DynamicMending.RejectTainted".Translate();
                return Intake.Reject;
            }

            if (RecycleFilter.Allows(thing) && MendingUtility.CanEverRecycle(thing))
                return Intake.Recycle;

            if (!AcceptFilter.Allows(thing))
                why = "DynamicMending.RejectFiltered".Translate();
            else if (thing.HitPoints >= thing.MaxHitPoints)
                why = "DynamicMending.RejectUndamaged".Translate();
            else if (!MendingUtility.BelowAutoRepairThreshold(thing))
                why = "DynamicMending.RejectThreshold".Translate(MendingModMain.Settings.autoRepairBelow.ToStringPercent());
            else if (!IsMendable(thing))
                why = "DynamicMending.RejectTooAdvanced".Translate();

            return why == null ? Intake.Repair : Intake.Reject;
        }

        private void TryStartMend()
        {
            // Rejects leave before anything new comes in. A machine that kept taking items while
            // both chutes were blocked would fill itself with things it cannot get rid of.
            if (!FlushRejects())
            {
                idleReason = "DynamicMending.RejectChuteBlocked".Translate(rejectQueue.Count);
                return;
            }

            // One item per scan, whichever way it goes, so a conveyor's worth of rejects leaves at
            // a steady rate rather than all in one tick.
            Thing damagedItem = null;
            foreach (Thing thing in ThingsOn(ItemInputCell).ToList())
            {
                if (!IsGear(thing) || IsOffLimits(thing))
                    continue;

                switch (Decide(thing, out string why))
                {
                    case Intake.Reject:
                        Reject(thing, why);
                        return;

                    case Intake.Recycle:
                        StartRecycle(thing);
                        return;
                }

                damagedItem = thing;
                break;
            }

            if (damagedItem == null)
            {
                spotWasEmpty = true;
                idleReason = "DynamicMending.MenderNoItem".Translate();
                return;
            }

            // Only a repair fills the waste container, so only a repair waits for it - rejects
            // and recycling carry on while it is full.
            if (GetComp<CompMenderWasteBuffer>()?.IsFull == true)
            {
                idleReason = "DynamicMending.WasteFull".Translate();
                return;
            }

            List<Thing> available = ThingsOn(ResourceInputCells)
                .Where(t => t != damagedItem && !IsOffLimits(t))
                .ToList();
            int skill = EffectiveSkill;
            List<ThingDefCountClass> costs = MendingUtility.GetDynamicIngredientCosts(damagedItem, skill, ignoreResearch: true);

            // Pass 1: verify every material is available before touching anything.
            Dictionary<ThingDef, int> stock = new Dictionary<ThingDef, int>();
            foreach (Thing thing in available)
            {
                stock.TryGetValue(thing.def, out int have);
                stock[thing.def] = have + thing.stackCount;
            }

            foreach (ThingDefCountClass cost in costs)
            {
                if (!stock.TryGetValue(cost.thingDef, out int have) || have < cost.count)
                {
                    // Naming the exact shortfall rather than a generic "idle" - otherwise the only
                    // way to find out why nothing is happening is to guess at the input spots.
                    idleReason = "DynamicMending.MenderNeedsMaterial".Translate(
                        damagedItem.LabelShortCap, cost.count, cost.thingDef.label, have);
                    return;
                }
            }

            idleReason = null;

            // Draw the item in BEFORE paying for it. Consuming first and then failing to take the
            // item would destroy the materials and leave the item sitting on the spot - and since
            // the scan retries twice a second, it would eat every fresh stack put down after it.
            if (!TryTakeIn(damagedItem))
            {
                idleReason = "DynamicMending.MenderCannotTakeItem".Translate(damagedItem.LabelShortCap);
                return;
            }

            // Pass 2: only now consume, since pass 1 guaranteed every cost can be met and the
            // item is safely inside.
            foreach (ThingDefCountClass cost in costs)
            {
                int remaining = cost.count;
                foreach (Thing stack in available.Where(t => t != damagedItem && t.def == cost.thingDef).ToList())
                {
                    if (remaining <= 0)
                        break;

                    int take = Mathf.Min(remaining, stack.stackCount);
                    stack.SplitOff(take).Destroy(DestroyMode.Vanish);
                    remaining -= take;
                }
            }

            currentItem = damagedItem;
            jobSkill = skill;

            workTicksTotal = Mathf.Max(60, Mathf.RoundToInt(
                MendingUtility.GetDynamicWorkAmount(currentItem, skill, ignoreResearch: true) / overclock));
            workTicksRemaining = workTicksTotal;
            state = MenderState.Working;
        }

        // Not ThingOwner.TryAddOrTransfer: a spawned thing belongs to the map's own ThingOwner
        // (Map.spawnedThings), and TryAddOrTransfer routes anything with a holdingOwner through
        // TryTransferToContainer, which refuses outright when either side is a Map - it logs
        // "Can't transfer items to or from Maps directly. They must be spawned or despawned
        // manually" and returns false. So it can never lift an item off the ground or off a
        // shelf, which is what left items sitting on the input spot. Despawning first and then
        // adding is what that warning asks for.
        private bool TryTakeIn(Thing item)
        {
            if (!item.Spawned)
            {
                if (item.holdingOwner != null)
                    return item.holdingOwner.TryTransferToContainer(item, innerContainer, item.stackCount, out _, false) > 0;

                return innerContainer.TryAdd(item, canMergeWithExistingStacks: false);
            }

            IntVec3 cell = item.Position;
            Map map = item.Map;
            item.DeSpawn();

            if (innerContainer.TryAdd(item, canMergeWithExistingStacks: false))
                return true;

            // Put it back rather than leaving it despawned and orphaned.
            GenPlace.TryPlaceThing(item, cell, map, ThingPlaceMode.Direct);
            return false;
        }

        private void Reject(Thing thing, string why)
        {
            if (!TryTakeIn(thing))
            {
                idleReason = "DynamicMending.MenderCannotTakeItem".Translate(thing.LabelShortCap);
                return;
            }

            lastRejected = "DynamicMending.LastRejected".Translate(thing.LabelShortCap, why);
            rejectQueue.Add(thing);
            idleReason = null;
            FlushRejects();
        }

        private void StartRecycle(Thing thing)
        {
            if (!TryTakeIn(thing))
            {
                idleReason = "DynamicMending.MenderCannotTakeItem".Translate(thing.LabelShortCap);
                return;
            }

            currentItem = thing;
            recycling = true;
            idleReason = null;
            jobSkill = EffectiveSkill;
            workTicksTotal = Mathf.Max(60, Mathf.RoundToInt(MendingUtility.RecycleWorkAmount(thing) / overclock));
            workTicksRemaining = workTicksTotal;
            state = MenderState.Working;
        }

        // Puts whatever is waiting onto a free reject chute. True once nothing is left waiting.
        //
        // Direct placement only, like the output port, so a belt laid on a chute collects from
        // it. Each chute takes one stack (more with storage or a belt on it), so a recycle that
        // breaks into several materials can take a moment to get out.
        private bool FlushRejects()
        {
            rejectQueue.RemoveAll(t => t == null || t.Destroyed || !innerContainer.Contains(t));
            if (rejectQueue.Count == 0)
            {
                return true;
            }

            List<IntVec3> chutes = RejectCells;
            foreach (Thing thing in rejectQueue.ToList())
            {
                foreach (IntVec3 cell in chutes)
                {
                    if (cell.InBounds(Map) && innerContainer.TryDrop(thing, cell, Map, ThingPlaceMode.Direct, out _))
                    {
                        rejectQueue.Remove(thing);
                        break;
                    }
                }
            }

            if (rejectQueue.Count == 0)
            {
                return true;
            }

            // Like Vanilla Furniture Expanded - Factory's machines, it simply waits for a chute
            // to clear, however long that takes, and takes nothing new in meanwhile. Nothing held
            // is ever lost: uninstalling or deconstructing the machine puts it all on the floor.
            return false;
        }

        private void TickWork()
        {
            if (currentItem == null || currentItem.Destroyed)
            {
                state = MenderState.Idle;
                return;
            }

            workTicksRemaining--;
            if (workTicksRemaining > 0)
                return;

            FinishMend();
        }

        private void FinishMend()
        {
            if (recycling)
            {
                FinishRecycle();
                return;
            }

            GetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(currentItem, jobSkill);
            MendResult result = MendingUtility.ResolveRepair(currentItem, jobSkill, ignoreResearch: true);
            MendingUtility.ShowRepairResult(result, this);

            state = MenderState.Ejecting;
            TryEject();
        }

        // What it breaks down into goes out the reject chutes, not the output port: the orange
        // port is for repaired gear, and a belt feeding a stockpile of it should never find a
        // stack of steel on it.
        private void FinishRecycle()
        {
            recycling = false;
            Thing item = currentItem;
            currentItem = null;
            state = MenderState.Idle;

            if (item == null || item.Destroyed)
                return;

            string label = item.LabelShortCap;
            List<Thing> products = MendingUtility.MakeRecycleProducts(item, jobSkill);
            foreach (Thing product in products)
            {
                if (innerContainer.TryAdd(product))
                    rejectQueue.Add(product);
            }

            MoteMaker.ThrowText(DrawPos, Map, products.Count == 0
                ? "DynamicMending.MoteRecycledNothing".Translate(label).ToString()
                : "DynamicMending.MoteRecycled".Translate(label).ToString());

            FlushRejects();
        }

        // Direct placement, so the item lands exactly on the output spot (a conveyor laid there
        // will collect it on its next tick). Only ever onto the spot itself, never scattered: a designated spot
        // that sometimes puts the item two tiles away is worse than one that visibly waits. If
        // the spot is blocked the item stays inside and this retries, so a conveyor or stockpile
        // clearing the spot gets the item on the next scan.
        private void TryEject()
        {
            if (currentItem == null || currentItem.Destroyed)
            {
                currentItem = null;
                idleReason = null;
                state = MenderState.Idle;
                return;
            }

            IntVec3 cell = OutputCell;

            if (cell.InBounds(Map)
                && innerContainer.Contains(currentItem)
                && innerContainer.TryDrop(currentItem, cell, Map, ThingPlaceMode.Direct, out _))
            {
                currentItem = null;
                idleReason = null;
                state = MenderState.Idle;
                return;
            }

            // Wait for the spot, however long that takes, as VFE Factory's machines do. Nothing
            // held is lost: uninstalling or deconstructing the machine puts it on the floor.
            idleReason = "DynamicMending.MenderOutputBlocked".Translate(currentItem.LabelShortCap);
        }

        private float WorkProgress => workTicksTotal > 0
            ? Mathf.Clamp01(1f - (float)workTicksRemaining / workTicksTotal)
            : 0f;

        // Live repair progress drawn across the machine, so you can read how far along a job is
        // without selecting the building and reading the inspect pane.
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

            if (state != MenderState.Working)
                return;

            GenDraw.DrawFillableBar(new GenDraw.FillableBarRequest
            {
                center = drawLoc + Vector3.up * 0.1f,
                size = new Vector2(2.6f, 0.34f),
                fillPercent = WorkProgress,
                filledMat = BarFilledMat,
                unfilledMat = BarUnfilledMat,
                margin = 0.12f,
                rotation = Rot4.North,
            });
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            if (Map == null)
                return;

            // Same rings the place worker draws, so a built machine and one on the cursor
            // mark their spots identically.
            MenderSpots.DrawRings(Footprint, Rotation);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            yield return new Command_Action
            {
                defaultLabel = "DynamicMending.AcceptFilterLabel".Translate(),
                defaultDesc = "DynamicMending.AcceptFilterDesc".Translate(),
                icon = TexCommand.SelectShelf,
                action = () => Find.WindowStack.Add(new Dialog_MenderAcceptFilter(
                    this, AcceptFilter, "DynamicMending.AcceptFilterLabel", "DynamicMending.AcceptFilterHint")),
            };

            yield return new Command_Action
            {
                defaultLabel = "DynamicMending.RecycleFilterLabel".Translate(),
                defaultDesc = "DynamicMending.RecycleFilterDesc".Translate(),
                icon = TexCommand.Replant,
                action = () => Find.WindowStack.Add(new Dialog_MenderAcceptFilter(
                    this, RecycleFilter, "DynamicMending.RecycleFilterLabel", "DynamicMending.RecycleFilterHint")),
            };

            yield return new Command_Action
            {
                defaultLabel = "DynamicMending.OverclockLabel".Translate(overclock.ToStringPercent()),
                defaultDesc = "DynamicMending.OverclockDesc".Translate(),
                icon = TexCommand.DesirePower,
                action = () => Find.WindowStack.Add(new Window_MenderOverclock(this)),
            };

            // Cycles rather than opening a menu, so it behaves with several machines selected:
            // each one steps on from its own setting.
            yield return new Command_Action
            {
                defaultLabel = ("DynamicMending.Tainted_" + taintedMode).Translate(),
                defaultDesc = "DynamicMending.TaintedDesc".Translate(),
                icon = taintedMode == TaintedMode.Reject ? TexCommand.ForbidOn
                     : taintedMode == TaintedMode.Recycle ? TexCommand.Replant
                     : TexCommand.ForbidOff,
                action = () => taintedMode = (TaintedMode)(((int)taintedMode + 1) % 3),
            };
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());

            if (sb.Length > 0)
                sb.AppendLine();

            if (!PowerOn)
            {
                sb.Append("DynamicMending.MenderNoPower".Translate());
            }
            else if (state == MenderState.Working && currentItem != null)
            {
                float progress = workTicksTotal > 0
                    ? 1f - (float)workTicksRemaining / workTicksTotal
                    : 0f;
                sb.Append((recycling ? "DynamicMending.MenderRecycling" : "DynamicMending.MenderRepairing")
                    .Translate(currentItem.LabelCap, progress.ToStringPercent()));
            }
            else
            {
                sb.Append(idleReason ?? "DynamicMending.MenderIdle".Translate().ToString());
            }

            if (!lastRejected.NullOrEmpty())
                sb.AppendLine().Append(lastRejected);

            sb.AppendLine();
            sb.Append("DynamicMending.OverclockInspect".Translate(overclock.ToStringPercent(), EffectiveSkill));

            sb.AppendLine();
            sb.Append(("DynamicMending.Tainted_" + taintedMode).Translate());

            sb.AppendLine();
            sb.Append("DynamicMending.MenderSpotLegend".Translate());

            return sb.ToString();
        }
    }
}
