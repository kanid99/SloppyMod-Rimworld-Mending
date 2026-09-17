using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace RiimbaMod
{
    // The thing that makes a Riimba more than a mech: it holds the roster, it draws the boundary
    // the units work inside, it charges them, and it takes what they collect.
    //
    // There is no AI here. The units ask this building questions - am I signed on, is my bay
    // free, is that mess inside your radius - and the answers come from plain state. Keeping the
    // decisions in the units' think tree and the facts in the station is what stops the two
    // drifting apart.
    public class Building_RiimbaStation : Building
    {
        // Saved as references, so a unit that is destroyed while the game is not loaded comes
        // back as null and is cleaned out on the next tick rather than resurrecting a dead entry.
        private List<Pawn> units = new List<Pawn>();

        private CompPowerTrader power;
        private CompRiimbaWasteBuffer wasteBuffer;
        private RiimbaStationExtension extension;

        private RiimbaStationExtension Extension =>
            extension ?? (extension = def.GetModExtension<RiimbaStationExtension>() ?? new RiimbaStationExtension());

        public CompPowerTrader Power => power ?? (power = GetComp<CompPowerTrader>());

        public CompRiimbaWasteBuffer WasteBuffer =>
            wasteBuffer ?? (wasteBuffer = GetComp<CompRiimbaWasteBuffer>());

        // A station with no power comp at all still counts as powered, so the def can drop the
        // comp for a debug or no-power variant without the whole building going inert.
        public bool PowerOn => Power == null || Power.PowerOn;

        public int MaxUnits => Extension.maxUnits;

        public float Radius => Extension.radius;

        public bool HasFreeSlot => units.Count < MaxUnits;

        public List<Pawn> Units => units;

        private CellRect Footprint => this.OccupiedRect();

        public List<IntVec3> BayCells => RiimbaSpots.BayCells(Footprint, Rotation);

        public IntVec3 WasteOutputCell => RiimbaSpots.WasteOutputCell(Footprint, Rotation);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref units, "units", LookMode.Reference);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && units == null)
                units = new List<Pawn>();
        }

        // Uninstalling or destroying the station has to release its units, or they stay pointing
        // at a building inside a minified crate: CompRiimbaUnit would see a despawned station,
        // and the unit would sit idle forever holding a slot on something that no longer exists.
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            for (int i = units.Count - 1; i >= 0; i--)
                units[i]?.GetComp<CompRiimbaUnit>()?.ClearStation();

            units.Clear();

            base.DeSpawn(mode);
        }

        public bool InRadius(IntVec3 cell)
        {
            if (!RiimbaModMain.Settings.enforceRadius)
                return true;

            // Measured from the centre of the footprint, which is what the placement ring and the
            // selection ring both draw.
            return cell.InHorDistOf(Footprint.CenterCell, Radius);
        }

        public bool IsUnitSignedOn(Pawn unit)
        {
            return unit != null && units.Contains(unit);
        }

        public void Notify_UnitSignedOn(Pawn unit)
        {
            if (unit == null || units.Contains(unit))
                return;

            // Guarded here as well as at every call site: this is the one place that can be sure
            // the roster is not about to go over strength.
            if (units.Count >= MaxUnits)
                return;

            units.Add(unit);
        }

        public void Notify_UnitSignedOff(Pawn unit)
        {
            units.Remove(unit);
        }

        // Which bay a given unit owns, by roster position rather than by "first free cell".
        //
        // Position, not identity: signing a unit off shifts everyone behind it down one, so the
        // others may end up on a different bay than before. That is fine and it is why bays are
        // not reserved - what matters is only that no two units are ever sent to the SAME bay,
        // which indexing a shared list guarantees by construction. A "first free cell" search
        // would not: two units deciding to dock on the same tick would both see the same cell
        // free and both drive to it.
        public IntVec3 BayCellFor(Pawn unit)
        {
            int index = units.IndexOf(unit);
            List<IntVec3> bays = BayCells;

            if (index < 0 || bays.Count == 0)
                return IntVec3.Invalid;

            return bays[index % bays.Count];
        }

        public bool IsUnitOnBay(Pawn unit)
        {
            IntVec3 bay = BayCellFor(unit);
            return bay.IsValid && unit != null && unit.Spawned && unit.Position == bay;
        }

        protected override void Tick()
        {
            base.Tick();

            // Once a second is plenty for roster hygiene and power bookkeeping, and avoids
            // walking the unit list sixty times a second to learn nothing.
            if (!this.IsHashIntervalTick(60))
                return;

            PruneUnits();
            UpdatePowerDraw();
        }

        // Entries go stale in ways no notification covers: a unit killed on another map, one
        // destroyed while this station was itself despawned, or a save loaded after the unit's
        // def was removed by a mod change. A null or dead entry would otherwise hold a slot
        // forever and quietly cap the station below its own maximum.
        private void PruneUnits()
        {
            for (int i = units.Count - 1; i >= 0; i--)
            {
                Pawn unit = units[i];

                if (unit == null || unit.Destroyed || unit.Dead)
                {
                    units.RemoveAt(i);
                    continue;
                }

                // A unit that has wandered onto another map is not this station's to command.
                if (unit.Spawned && unit.Map != Map)
                {
                    units.RemoveAt(i);
                    unit.GetComp<CompRiimbaUnit>()?.ClearStation();
                }
            }
        }

        // Charging costs real power, so a station with three units on its bays draws far more
        // than an idle one. Without this the building would cost the same whether it was doing
        // anything or not, which is the kind of free lunch that makes automation a no-brainer.
        private void UpdatePowerDraw()
        {
            if (Power == null)
                return;

            int charging = 0;
            foreach (Pawn unit in units)
            {
                if (unit != null && IsUnitOnBay(unit))
                {
                    CompRiimbaUnit comp = unit.GetComp<CompRiimbaUnit>();
                    if (comp != null && comp.Charge < 1f)
                        charging++;
                }
            }

            Power.PowerOutput = -(Power.Props.PowerConsumption + charging * Extension.powerPerChargingUnit);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            if (Map == null)
                return;

            // The same rings the place worker draws, so a built station and one on the cursor
            // mark themselves identically.
            RiimbaSpots.DrawSpots(Footprint, Rotation, RiimbaModMain.Settings.enforceRadius ? Radius : 0f);
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());

            if (sb.Length > 0)
                sb.AppendLine();

            if (!PowerOn)
            {
                sb.Append("Riimba.StationNoPower".Translate());
            }
            else
            {
                sb.Append("Riimba.StationUnits".Translate(units.Count, MaxUnits));

                int docked = 0;
                foreach (Pawn unit in units)
                {
                    if (unit != null && IsUnitOnBay(unit))
                        docked++;
                }

                if (docked > 0)
                {
                    sb.AppendLine();
                    sb.Append("Riimba.StationDocked".Translate(docked));
                }
            }

            sb.AppendLine();
            sb.Append("Riimba.StationLegend".Translate());

            return sb.ToString();
        }
    }
}
