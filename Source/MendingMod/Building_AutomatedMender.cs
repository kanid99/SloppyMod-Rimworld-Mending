using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // Tick-driven state machine: Idle scans adjacent hoppers for work, Working counts down a
    // dynamically-computed work duration on the swallowed item, then ejects it. Runs the same
    // MendingUtility formulas as the pawn path, but with a fixed skill level (functions as a
    // skill-10 worker per design) instead of reading a pawn's skill.
    public class Building_AutomatedMender : Building, IThingHolder
    {
        private const int FixedSkillLevel = 10;
        private const string ItemHopperDefName = "MendingItemHopper";
        private const string ResourceHopperDefName = "MendingResourceHopper";

        private enum MenderState : byte { Idle, Working }

        private ThingOwner<Thing> innerContainer;
        private Thing currentItem;
        private int workTicksRemaining;
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
            Scribe_Values.Look(ref state, "state");
        }

        protected override void Tick()
        {
            base.Tick();

            CompPowerTrader power = GetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn)
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

        private void TryStartMend()
        {
            Building_Storage itemHopper = FindAdjacentHopper(ItemHopperDefName);
            Thing damagedItem = itemHopper?.slotGroup?.HeldThings
                .FirstOrDefault(t => t.def.useHitPoints && t.HitPoints < t.MaxHitPoints);

            if (damagedItem == null)
                return;

            List<ThingDefCountClass> costs = MendingUtility.GetDynamicIngredientCosts(damagedItem);

            Building_Storage resourceHopper = FindAdjacentHopper(ResourceHopperDefName);
            if (resourceHopper?.slotGroup == null)
                return;

            // Pass 1: verify every material is available before touching anything.
            Dictionary<ThingDef, int> available = new Dictionary<ThingDef, int>();
            foreach (Thing t in resourceHopper.slotGroup.HeldThings)
            {
                available.TryGetValue(t.def, out int have);
                available[t.def] = have + t.stackCount;
            }

            foreach (ThingDefCountClass cost in costs)
            {
                if (!available.TryGetValue(cost.thingDef, out int have) || have < cost.count)
                    return;
            }

            // Pass 2: only now consume, since pass 1 guaranteed every cost can be met.
            foreach (ThingDefCountClass cost in costs)
            {
                int remaining = cost.count;
                foreach (Thing stack in resourceHopper.slotGroup.HeldThings.Where(t => t.def == cost.thingDef).ToList())
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

            workTicksRemaining = Mathf.Max(60, Mathf.RoundToInt(MendingUtility.GetDynamicWorkAmount(currentItem)));
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

            IntVec3 outputCell = GetValidDropCell(Position + Rotation.FacingCell);
            innerContainer.TryDrop(currentItem, outputCell, Map, ThingPlaceMode.Near, out _);

            currentItem = null;
            state = MenderState.Idle;
        }

        private IntVec3 GetValidDropCell(IntVec3 preferred)
        {
            return preferred.InBounds(Map) && preferred.Walkable(Map) ? preferred : Position;
        }

        private Building_Storage FindAdjacentHopper(string defName)
        {
            foreach (IntVec3 cell in GenAdj.CellsAdjacentCardinal(this))
            {
                if (cell.GetFirstBuilding(Map) is Building_Storage storage && storage.def.defName == defName)
                    return storage;
            }

            return null;
        }
    }
}
