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
    // Input spots are the cells adjacent to the building's two short edges (four cells for the
    // 3x2 footprint, in any rotation). Anything sitting in those cells counts, whether it's loose
    // on the ground, in a stockpile zone, or in a storage building - vanilla storage keeps its
    // contents spawned on its own cells, so a plain cell scan already sees shelves and the like.
    // Buildings that hold items in an internal container instead (deep-storage mods, and Vanilla
    // Furniture Expanded's conveyors) are read through IThingHolder, which is also what makes
    // conveyor feeding work with no dependency on that mod.
    public class Building_AutomatedMender : Building, IThingHolder
    {
        private const int FixedSkillLevel = 10;

        private enum MenderState : byte { Idle, Working }

        private ThingOwner<Thing> innerContainer;
        private Thing currentItem;
        private int workTicksRemaining;
        private int workTicksTotal;
        private MenderState state = MenderState.Idle;

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

            if (state == MenderState.Idle)
            {
                TryStartMend();
            }
            else
            {
                TickWork();
            }
        }

        // The two short edges of the footprint, whichever way the building is facing: for a 3x2
        // that's two cells on each of the 2-cell sides.
        public IEnumerable<IntVec3> InputCells
        {
            get
            {
                CellRect rect = this.OccupiedRect();

                if (rect.Height <= rect.Width)
                {
                    for (int z = rect.minZ; z <= rect.maxZ; z++)
                    {
                        yield return new IntVec3(rect.minX - 1, 0, z);
                        yield return new IntVec3(rect.maxX + 1, 0, z);
                    }
                }
                else
                {
                    for (int x = rect.minX; x <= rect.maxX; x++)
                    {
                        yield return new IntVec3(x, 0, rect.minZ - 1);
                        yield return new IntVec3(x, 0, rect.maxZ + 1);
                    }
                }
            }
        }

        // The cells just past the edge the building faces. Repaired items go here, which is what
        // lets a conveyor laid against the front pick them straight up.
        public IEnumerable<IntVec3> OutputCells
        {
            get
            {
                CellRect rect = this.OccupiedRect();
                IntVec3 facing = Rotation.FacingCell;

                if (facing.x != 0)
                {
                    int x = facing.x > 0 ? rect.maxX + 1 : rect.minX - 1;
                    for (int z = rect.minZ; z <= rect.maxZ; z++)
                        yield return new IntVec3(x, 0, z);
                }
                else
                {
                    int z = facing.z > 0 ? rect.maxZ + 1 : rect.minZ - 1;
                    for (int x = rect.minX; x <= rect.maxX; x++)
                        yield return new IntVec3(x, 0, z);
                }
            }
        }

        private IEnumerable<Thing> AvailableInputThings()
        {
            foreach (IntVec3 cell in InputCells)
            {
                if (!cell.InBounds(Map))
                    continue;

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
        }

        // Only apparel and weapons are mendable (matching the two recipes), and checking that
        // rather than useHitPoints alone keeps deteriorated resource stacks out of the input.
        private static bool IsMendable(Thing thing)
        {
            return (thing.def.IsApparel || thing.def.IsWeapon)
                && thing.def.useHitPoints
                && thing.HitPoints < thing.MaxHitPoints;
        }

        private void TryStartMend()
        {
            List<Thing> available = AvailableInputThings().ToList();

            Thing damagedItem = available.FirstOrDefault(IsMendable);
            if (damagedItem == null)
                return;

            List<ThingDefCountClass> costs = MendingUtility.GetDynamicIngredientCosts(damagedItem, FixedSkillLevel);

            // Pass 1: verify every material is available before touching anything.
            Dictionary<ThingDef, int> stock = new Dictionary<ThingDef, int>();
            foreach (Thing thing in available)
            {
                if (thing == damagedItem)
                    continue;

                stock.TryGetValue(thing.def, out int have);
                stock[thing.def] = have + thing.stackCount;
            }

            foreach (ThingDefCountClass cost in costs)
            {
                if (!stock.TryGetValue(cost.thingDef, out int have) || have < cost.count)
                    return;
            }

            // Pass 2: only now consume, since pass 1 guaranteed every cost can be met.
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
            innerContainer.TryAddOrTransfer(damagedItem);

            workTicksTotal = Mathf.Max(60, Mathf.RoundToInt(MendingUtility.GetDynamicWorkAmount(currentItem)));
            workTicksRemaining = workTicksTotal;
            state = MenderState.Working;
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
            GetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(currentItem);
            MendingUtility.ResolveRepair(currentItem, FixedSkillLevel);

            EjectItem(currentItem);

            currentItem = null;
            state = MenderState.Idle;
        }

        // Direct placement first so the item lands exactly on an output cell (a conveyor laid
        // there will collect it on its next tick); only if every output cell refuses does it fall
        // back to scattering the item nearby.
        private void EjectItem(Thing item)
        {
            foreach (IntVec3 cell in OutputCells)
            {
                if (!cell.InBounds(Map))
                    continue;

                if (innerContainer.TryDrop(item, cell, Map, ThingPlaceMode.Direct, out _))
                    return;
            }

            innerContainer.TryDrop(item, Position, Map, ThingPlaceMode.Near, out _);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            if (Map == null)
                return;

            GenDraw.DrawFieldEdges(InputCells.Where(c => c.InBounds(Map)).ToList(), Color.green);
            GenDraw.DrawFieldEdges(OutputCells.Where(c => c.InBounds(Map)).ToList(), Color.yellow);
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
                sb.Append("DynamicMending.MenderIdle".Translate());
            }

            return sb.ToString();
        }
    }
}
