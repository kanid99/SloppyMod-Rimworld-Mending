using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // Spot geometry, kept free of any Thing instance so the place worker can draw the same spots
    // from a hypothetical footprint while the building is still a ghost on the cursor.
    public static class MenderSpots
    {
        public const int ResourceSpots = 8;

        // The row of cells just outside one end of the footprint. Everything feeds in along the
        // back edge and leaves by the front, the way the vanilla-expanded factory machines are
        // laid out, so a belt can run into the back and away from the front.
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

        // Cells down the two long sides, paired and ordered from the back forwards, so taking the
        // first few keeps the spots bunched at the intake end and leaves the front clear.
        //
        // k starts at 1, not 0: the rear-most cell of each flank is the same corner cell the back
        // edge's outermost spot already serves, and drawing a side port there put it corner to
        // corner with the back row's, which is something the factory machines never do.
        private static List<IntVec3> SideCells(CellRect rect, Rot4 rot)
        {
            IntVec3 back = rot.Opposite.FacingCell;
            List<IntVec3> cells = new List<IntVec3>();

            if (back.x != 0)
            {
                int startX = back.x > 0 ? rect.maxX : rect.minX;
                int step = back.x > 0 ? -1 : 1;
                for (int k = 1; k < rect.Width; k++)
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
                for (int k = 1; k < rect.Height; k++)
                {
                    int z = startZ + step * k;
                    cells.Add(new IntVec3(rect.minX - 1, 0, z));
                    cells.Add(new IntVec3(rect.maxX + 1, 0, z));
                }
            }

            return cells;
        }

        public static IntVec3 ItemInputCell(CellRect rect, Rot4 rot)
        {
            List<IntVec3> back = EdgeCells(rect, rot, front: false);
            return back[back.Count / 2];
        }

        // The back edge either side of the item port, plus the rear-most cells down each flank.
        public static IEnumerable<IntVec3> ResourceInputCells(CellRect rect, Rot4 rot)
        {
            List<IntVec3> back = EdgeCells(rect, rot, front: false);
            int middle = back.Count / 2;
            return back.Where((cell, i) => i != middle)
                .Concat(SideCells(rect, rot).Take(ResourceSpots - (back.Count - 1)));
        }

        public static IntVec3 OutputCell(CellRect rect, Rot4 rot)
        {
            List<IntVec3> front = EdgeCells(rect, rot, front: true);
            return front[front.Count / 2];
        }

        public static void DrawRings(CellRect rect, Rot4 rot)
        {
            DrawRing(ItemInputCell(rect, rot), SimpleColor.Cyan);

            foreach (IntVec3 cell in ResourceInputCells(rect, rot))
                DrawRing(cell, SimpleColor.Green);

            DrawRing(OutputCell(rect, rot), SimpleColor.Orange);
        }

        private static void DrawRing(IntVec3 cell, SimpleColor colour)
        {
            Map map = Find.CurrentMap;
            if (map == null || !cell.InBounds(map))
                return;

            GenDraw.DrawCircleOutline(cell.ToVector3Shifted(), 0.44f, colour);
        }
    }

    // Draws the spots while the building is still on the cursor, so its orientation can be judged
    // before it is committed - matching how the factory machines show their layout on placement.
    public class PlaceWorker_MenderSpots : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            base.DrawGhost(def, center, rot, ghostCol, thing);
            MenderSpots.DrawRings(GenAdj.OccupiedRect(center, rot, def.Size), rot);
        }
    }
}
