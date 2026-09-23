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

        // The row of cells just outside one end of the footprint.
        //
        // `intake` is the side the building FACES. That is the vanilla-expanded factory
        // convention - point one of their machines north and it takes material in along its
        // north edge - and it used to be the other way round here, so a repair centre dropped
        // into a belt line laid out for their machines ran backwards.
        public static List<IntVec3> EdgeCells(CellRect rect, Rot4 rot, bool intake)
        {
            IntVec3 dir = intake ? rot.FacingCell : rot.Opposite.FacingCell;
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
            IntVec3 back = rot.FacingCell;
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
            List<IntVec3> intake = EdgeCells(rect, rot, intake: true);
            return intake[intake.Count / 2];
        }

        // Two cells on the intake edge, one either side of the item port, and the rest spread down
        // the two flanks - three a side on a five-deep footprint.
        //
        // These used to fill the whole back edge first and only spill onto the flanks. That put
        // seven bays shoulder to shoulder along one edge and one on each flank, which is not a
        // layout any of the factory machines use: theirs are mirror-symmetric, with their ports
        // spread around the perimeter rather than crowded onto the intake side.
        public static IEnumerable<IntVec3> ResourceInputCells(CellRect rect, Rot4 rot)
        {
            List<IntVec3> intake = EdgeCells(rect, rot, intake: true);
            int middle = intake.Count / 2;
            List<IntVec3> cells = new List<IntVec3>();

            if (middle - 1 >= 0)
                cells.Add(intake[middle - 1]);
            if (middle + 1 < intake.Count)
                cells.Add(intake[middle + 1]);

            cells.AddRange(SideCells(rect, rot).Take(ResourceSpots - cells.Count));
            return cells;
        }

        public static IntVec3 OutputCell(CellRect rect, Rot4 rot)
        {
            List<IntVec3> far = EdgeCells(rect, rot, intake: false);
            return far[far.Count / 2];
        }

        // Two reject chutes, one either side of the output port, both doing the same job.
        //
        // Two rather than one because the east texture is MIRRORED for west - RimWorld's
        // Graphic_Multi does that when there is no _west texture - so a single off-centre port
        // would be drawn on one side of the output and read by this code on the other whenever
        // the machine faced west. A symmetric pair lands on the same two cells either way, keeps
        // the layout mirror-symmetric like every factory machine, and doubles the chute's capacity.
        public static List<IntVec3> RejectCells(CellRect rect, Rot4 rot)
        {
            List<IntVec3> far = EdgeCells(rect, rot, intake: false);
            int middle = far.Count / 2;
            List<IntVec3> cells = new List<IntVec3>();

            if (middle - 1 >= 0)
                cells.Add(far[middle - 1]);
            if (middle + 1 < far.Count)
                cells.Add(far[middle + 1]);

            return cells;
        }

        public static void DrawRings(CellRect rect, Rot4 rot)
        {
            DrawRing(ItemInputCell(rect, rot), SimpleColor.Cyan);

            foreach (IntVec3 cell in ResourceInputCells(rect, rot))
                DrawRing(cell, SimpleColor.Green);

            DrawRing(OutputCell(rect, rot), SimpleColor.Orange);

            foreach (IntVec3 cell in RejectCells(rect, rot))
                DrawRing(cell, SimpleColor.Red);
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
