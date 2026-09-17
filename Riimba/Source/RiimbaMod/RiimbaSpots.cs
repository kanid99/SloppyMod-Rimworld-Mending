using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RiimbaMod
{
    // Spot geometry, kept free of any Thing instance so the place worker can draw the same spots
    // from a hypothetical footprint while the building is still a ghost on the cursor. The
    // building and the ghost therefore cannot disagree about where the bays are.
    public static class RiimbaSpots
    {
        // The row of cells just outside one end of the footprint. `front` is the side the
        // building FACES, matching the mending mod's convention so the two read the same way.
        public static List<IntVec3> EdgeCells(CellRect rect, Rot4 rot, bool front)
        {
            IntVec3 dir = front ? rot.FacingCell : rot.Opposite.FacingCell;
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

        // One bay per cell along the edge the station faces. A 3x1 station therefore has exactly
        // three, which is the same number as maxUnits - that is the point of the footprint.
        //
        // Deliberately one bay per unit rather than a single shared docking cell: three units
        // queueing for one tile is three units standing in each other's way, and a unit that
        // cannot reach the dock cannot charge.
        public static List<IntVec3> BayCells(CellRect rect, Rot4 rot)
        {
            return EdgeCells(rect, rot, front: true);
        }

        // Waste goes out the back, away from the bays, so a unit arriving to dock never has to
        // path through the pile the station just put down.
        public static IntVec3 WasteOutputCell(CellRect rect, Rot4 rot)
        {
            List<IntVec3> back = EdgeCells(rect, rot, front: false);
            return back[back.Count / 2];
        }

        public static void DrawSpots(CellRect rect, Rot4 rot, float radius)
        {
            foreach (IntVec3 cell in BayCells(rect, rot))
                DrawRing(cell, SimpleColor.Cyan);

            DrawRing(WasteOutputCell(rect, rot), SimpleColor.Orange);

            Map map = Find.CurrentMap;
            if (map != null && radius > 0f)
            {
                // The command radius is measured from the centre of the footprint, which is what
                // Building_RiimbaStation.InRadius tests against.
                GenDraw.DrawRadiusRing(rect.CenterCell, radius);
            }
        }

        private static void DrawRing(IntVec3 cell, SimpleColor colour)
        {
            Map map = Find.CurrentMap;
            if (map == null || !cell.InBounds(map))
                return;

            GenDraw.DrawCircleOutline(cell.ToVector3Shifted(), 0.44f, colour);
        }
    }

    // Draws the bays and the command radius while the building is still on the cursor, so its
    // orientation and reach can be judged before it is committed.
    public class PlaceWorker_RiimbaStation : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);

            RiimbaStationExtension ext = def.GetModExtension<RiimbaStationExtension>();
            RiimbaSpots.DrawSpots(GenAdj.OccupiedRect(center, rot, def.Size), rot, ext?.radius ?? 0f);
        }
    }
}
