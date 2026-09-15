using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
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
        private const int FixedSkillLevel = 10;
        private const int ResourceSpots = 8;

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
        private MenderState state = MenderState.Idle;
        private string idleReason;

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
            Scribe_Values.Look(ref state, "state");
        }

        private bool PowerOn
        {
            get
            {
                CompPowerTrader power = GetComp<CompPowerTrader>();
                return power == null || power.PowerOn;
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (!PowerOn)
                return;

            // Twice a second is plenty for noticing new input or a cleared output spot, and avoids
            // re-scanning the spots on every one of the 60 ticks per second.
            if (state == MenderState.Idle)
            {
                if (this.IsHashIntervalTick(30))
                    TryStartMend();
            }
            else if (state == MenderState.Ejecting)
            {
                if (this.IsHashIntervalTick(30))
                    TryEject();
            }
            else
            {
                TickWork();
            }
        }

        // The row of cells just outside one end of the footprint: everything feeds in along the
        // back edge and comes out of the front, the way the Vanilla Furniture Expanded factory
        // machines are laid out, so a belt can run into the back and away from the front.
        private List<IntVec3> EdgeCells(bool front)
        {
            CellRect rect = this.OccupiedRect();
            IntVec3 dir = front ? Rotation.FacingCell : Rotation.Opposite.FacingCell;
            List<IntVec3> cells = new List<IntVec3>();

            if (dir.x != 0)
            {
                int x = dir.x > 0 ? rect.maxX + 1 : rect.minX - 1;
                for (int z = rect.minZ; z <= rect.maxZ; z++)
                    cells.Add(new IntVec3(x, 0, z));
            }
            else
            {
                int z = dir.z > 0 ? rect.maxZ + 1 : rect.minZ - 1;
                for (int x = rect.minX; x <= rect.maxX; x++)
                    cells.Add(new IntVec3(x, 0, z));
            }

            return cells;
        }

        // Damaged gear goes on the middle of the back edge.
        public IntVec3 ItemInputCell
        {
            get
            {
                List<IntVec3> back = EdgeCells(front: false);
                return back[back.Count / 2];
            }
        }

        // The cells down the building's two long sides, paired up and ordered from the back
        // forwards, so taking the first N keeps the spots bunched at the intake end and leaves
        // the front clear for the output port.
        private List<IntVec3> SideCells()
        {
            CellRect rect = this.OccupiedRect();
            IntVec3 back = Rotation.Opposite.FacingCell;
            List<IntVec3> cells = new List<IntVec3>();

            if (back.x != 0)
            {
                int startX = back.x > 0 ? rect.maxX : rect.minX;
                int step = back.x > 0 ? -1 : 1;
                for (int k = 0; k < rect.Width; k++)
                {
                    int x = startX + step * k;
                    cells.Add(new IntVec3(x, 0, rect.minZ - 1));
                    cells.Add(new IntVec3(x, 0, rect.maxZ + 1));
                }
            }
            else
            {
                int startZ = back.z > 0 ? rect.maxZ : rect.minZ;
                int step = back.z > 0 ? -1 : 1;
                for (int k = 0; k < rect.Height; k++)
                {
                    int z = startZ + step * k;
                    cells.Add(new IntVec3(rect.minX - 1, 0, z));
                    cells.Add(new IntVec3(rect.maxX + 1, 0, z));
                }
            }

            return cells;
        }

        // Eight material spots: the back edge either side of the item port, plus the two
        // rear-most cells down each flank.
        public IEnumerable<IntVec3> ResourceInputCells
        {
            get
            {
                List<IntVec3> back = EdgeCells(front: false);
                int middle = back.Count / 2;
                return back.Where((cell, i) => i != middle)
                    .Concat(SideCells().Take(ResourceSpots - (back.Count - 1)));
            }
        }

        // Single output port at the middle of the front edge.
        public IntVec3 OutputCell
        {
            get
            {
                List<IntVec3> front = EdgeCells(front: true);
                return front[front.Count / 2];
            }
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
        // The tech cap is honoured here too, though this building carries no limit by default.
        private bool IsMendable(Thing thing)
        {
            return (thing.def.IsApparel || thing.def.IsWeapon)
                && thing.def.useHitPoints
                && thing.HitPoints < thing.MaxHitPoints
                && MendingUtility.CanBenchRepair(def, thing)
                && !MendingUtility.IsUnfamiliar(thing);
        }

        // Same test minus the tech gates, so a rejected item can be named as rejected rather
        // than reported as "no item present", which is what the player would otherwise see.
        private static bool IsDamagedGear(Thing thing)
        {
            return (thing.def.IsApparel || thing.def.IsWeapon)
                && thing.def.useHitPoints
                && thing.HitPoints < thing.MaxHitPoints;
        }

        private void TryStartMend()
        {
            Thing damagedItem = ThingsOn(ItemInputCell).FirstOrDefault(IsMendable);
            if (damagedItem == null)
            {
                Thing rejected = ThingsOn(ItemInputCell).FirstOrDefault(IsDamagedGear);
                idleReason = rejected != null
                    ? "DynamicMending.MenderUnknownTech".Translate(rejected.LabelShortCap)
                    : "DynamicMending.MenderNoItem".Translate();
                return;
            }

            List<Thing> available = ThingsOn(ResourceInputCells).Where(t => t != damagedItem).ToList();
            List<ThingDefCountClass> costs = MendingUtility.GetDynamicIngredientCosts(damagedItem, FixedSkillLevel);

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

            workTicksTotal = Mathf.Max(60, Mathf.RoundToInt(MendingUtility.GetDynamicWorkAmount(currentItem, FixedSkillLevel)));
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
            GetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(currentItem, FixedSkillLevel);
            MendingUtility.ResolveRepair(currentItem, FixedSkillLevel);

            state = MenderState.Ejecting;
            TryEject();
        }

        // Direct placement first so the item lands exactly on an output cell (a conveyor laid
        // there will collect it on its next tick); only if every output cell refuses does it fall
        // back to scattering the item nearby.
        // Only ever places onto the output spot itself, never scattered nearby: a designated spot
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

            // Rings rather than filled cell edges, matching how the vanilla-expanded factory
            // machines mark their own intake and output spots.
            DrawSpotRing(ItemInputCell, SimpleColor.Cyan);
            foreach (IntVec3 cell in ResourceInputCells)
                DrawSpotRing(cell, SimpleColor.Green);
            DrawSpotRing(OutputCell, SimpleColor.Orange);
        }

        private void DrawSpotRing(IntVec3 cell, SimpleColor colour)
        {
            if (!cell.InBounds(Map))
                return;

            GenDraw.DrawCircleOutline(cell.ToVector3Shifted(), 0.44f, colour);
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
                sb.Append("DynamicMending.MenderRepairing".Translate(currentItem.LabelCap, progress.ToStringPercent()));
            }
            else
            {
                sb.Append(idleReason ?? "DynamicMending.MenderIdle".Translate().ToString());
            }

            sb.AppendLine();
            sb.Append("DynamicMending.MenderSpotLegend".Translate());

            return sb.ToString();
        }
    }
}
