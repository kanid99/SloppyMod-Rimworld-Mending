using RimWorld;
using UnityEngine;
using Verse;

namespace FoodWasteMod
{
    public class CompProperties_SpoilageResidue : CompProperties
    {
        // Named rather than referenced so the comp degrades to a no-op if the item def is ever
        // missing, instead of throwing once per rotted stack.
        public string residueDefName = "SloppyFW_SpoiledFood";

        public CompProperties_SpoilageResidue()
        {
            compClass = typeof(CompSpoilageResidue);
        }
    }

    // Attached at startup to every food def that rots away to nothing - see FoodWasteDefPatches.
    // Doing it as a comp rather than a Harmony patch on CompRottable or Thing.Destroy is
    // deliberate, and matches the Sloppy mending mod's no-Harmony stance: it covers modded food
    // automatically, it stays off the extremely hot Thing.Destroy path, and it cannot be broken by
    // a change to the private rot-tick internals.
    public class CompSpoilageResidue : ThingComp
    {
        public CompProperties_SpoilageResidue Props => (CompProperties_SpoilageResidue)props;

        private static ThingDef residueDefCached;
        private static bool residueDefResolved;

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            if (FoodWasteModMain.Settings == null || !FoodWasteModMain.Settings.enabled)
                return;

            // Thing.Destroy reads Map before despawning, and Thing.Map is null for anything not
            // standing on the map, so a non-null previousMap is exactly "this was spawned". Food
            // that rots inside a pawn's inventory or a caravan pack leaves nothing, on purpose:
            // there is no sensible cell to drop a heap of mush onto, and a pawn shedding spoiled
            // food as they walk would be worse than the feature is worth.
            if (previousMap == null)
                return;

            CompRottable rot = parent.TryGetComp<CompRottable>();
            if (rot == null || rot.PropsRot == null || !rot.PropsRot.rotDestroys)
                return;

            // The load-bearing check. A rotDestroys item is destroyed in the same tick it first
            // reads Rotting, so this stage is reached on the rot path and essentially nowhere
            // else - food that burns or is deconstructed is still Fresh when it goes.
            if (rot.Stage != RotStage.Rotting)
                return;

            // Vanish is how CompRottable disposes of food, and this is defence in depth behind
            // the stage check rather than the main guard. It is called out separately in dev mode
            // because if the rot path ever stops using Vanish, the mod would otherwise go quiet
            // with nothing in the log to say why.
            if (mode != DestroyMode.Vanish)
            {
                if (Prefs.DevMode)
                {
                    Log.Warning($"[SloppyFoodWaste] {parent.def.defName} was destroyed while rotting "
                        + $"on mode {mode}, not Vanish, so it left nothing. If rotted food stopped "
                        + "leaving spoiled food, this is why.");
                }
                return;
            }

            ThingDef residueDef = ResidueDef(Props?.residueDefName);
            if (residueDef == null)
                return;

            int count = SpoilageYield.ResidueCountFor(parent);
            if (count <= 0)
                return;

            // DeSpawn does not clear positionInt, so the parent still knows where it was standing.
            IntVec3 cell = parent.Position;
            if (!cell.IsValid || !cell.InBounds(previousMap))
                return;

            Place(residueDef, count, cell, previousMap);
        }

        // ThingPlaceMode.Near merges into adjacent stacks, which is what keeps a freezer
        // breakdown - dozens of stacks expiring within the same few ticks - from carpeting the
        // room in single-unit piles.
        private static void Place(ThingDef residueDef, int count, IntVec3 cell, Map map)
        {
            int stackLimit = Mathf.Max(1, residueDef.stackLimit);

            // Bounded rather than while(count > 0): a modded food with an enormous stack limit
            // should not be able to turn one rotted stack into an unbounded placement loop.
            for (int i = 0; i < 16 && count > 0; i++)
            {
                int chunk = Mathf.Min(count, stackLimit);

                Thing residue = ThingMaker.MakeThing(residueDef);
                residue.stackCount = chunk;

                if (!GenPlace.TryPlaceThing(residue, cell, map, ThingPlaceMode.Near))
                    return;

                count -= chunk;
            }
        }

        // Every injected copy of this comp shares the same default props, so one cache is
        // enough; the lookup is by name so a missing def costs one warning rather than a throw on
        // every rotted stack for the rest of the session.
        private static ThingDef ResidueDef(string defName)
        {
            if (residueDefResolved)
                return residueDefCached;

            residueDefResolved = true;
            residueDefCached = DefDatabase<ThingDef>.GetNamedSilentFail(defName ?? "SloppyFW_SpoiledFood");

            if (residueDefCached == null)
                Log.Warning($"[SloppyFoodWaste] {defName} is missing; rotted food will leave nothing.");

            return residueDefCached;
        }
    }
}
