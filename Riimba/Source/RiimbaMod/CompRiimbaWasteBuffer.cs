using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RiimbaMod
{
    // count/defName fields are XML-tunable so the station can be configured without recompiling,
    // and so the waste defNames can be corrected to match whichever waste mods are actually
    // loaded - see Buildings_RiimbaStation.xml.
    public class CompProperties_RiimbaWasteBuffer : CompProperties
    {
        // Buffered filth per whole waste item produced.
        public float wasteBufferThreshold = 1f;

        // Beyond this many items' worth held back, the station stops accepting from units. It is
        // a backlog, not a black hole: units then sit full until the output spot clears.
        public float maxBufferedItems = 20f;

        public List<string> bioWasteDefNames = new List<string> { "Wastepack" };
        public string bioWasteLabel = "wastepack";

        public List<string> trashWasteDefNames =
            new List<string> { "VRecyclingE_Trash", "VRE_Trash", "VRE_TrashBag", "Trash" };
        public string trashWasteLabel = "trash";

        public CompProperties_RiimbaWasteBuffer()
        {
            compClass = typeof(CompRiimbaWasteBuffer);
        }
    }

    public class CompRiimbaWasteBuffer : ThingComp
    {
        private float bioBuffer;
        private float trashBuffer;

        private ThingDef bioWasteDef;
        private ThingDef trashWasteDef;
        private bool wasteDefsResolved;

        public CompProperties_RiimbaWasteBuffer Props => (CompProperties_RiimbaWasteBuffer)props;

        private float Threshold => Mathf.Max(0.01f, Props?.wasteBufferThreshold ?? 1f);

        private float MaxBuffer => Mathf.Max(Threshold, (Props?.maxBufferedItems ?? 20f) * Threshold);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref bioBuffer, "bioBuffer", 0f);
            Scribe_Values.Look(ref trashBuffer, "trashBuffer", 0f);
        }

        // Takes what it can and reports how much that was, so a unit handing over a full bin
        // keeps whatever the station could not take rather than losing it. Returning a count
        // instead of a bool is what makes a partial handover possible at all.
        public float AcceptWaste(float amount, bool biological)
        {
            if (amount <= 0f || !RiimbaModMain.Settings.generateWaste)
                return 0f;

            ResolveWasteDefsOnce();

            // A buffer with no def behind it can never be emptied, so it is never filled. The
            // alternative is waste that accumulates invisibly and jams the station permanently.
            if ((biological ? bioWasteDef : trashWasteDef) == null)
                return 0f;

            float current = biological ? bioBuffer : trashBuffer;
            float room = Mathf.Max(0f, MaxBuffer - current);
            float taken = Mathf.Min(room, amount);

            if (taken <= 0f)
                return 0f;

            if (biological)
                bioBuffer += taken;
            else
                trashBuffer += taken;

            return taken;
        }

        public bool IsFull => bioBuffer >= MaxBuffer || trashBuffer >= MaxBuffer;

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);

            if (parent.Map == null || !parent.Spawned)
                return;

            // Run on every interval tick rather than behind an IsHashIntervalTick gate.
            // CompTickInterval is itself throttled - the game calls it every UpdateRateTicks
            // ticks, not every tick - so a hash interval nested inside it can go unsatisfied
            // for long stretches, or never line up at all. The FloorToInt below is the real
            // guard: with no whole item accumulated this does nothing but two divisions.
            //
            // Putting waste out automatically rather than waiting for a player command is
            // deliberate: three units filling their bins is a steady trickle, and a station
            // that had to be emptied by hand every few minutes would be worse than sweeping
            // the floor yourself.
            SpawnWholeItems(ref bioBuffer, bioWasteDef);
            SpawnWholeItems(ref trashBuffer, trashWasteDef);
        }

        private void SpawnWholeItems(ref float buffer, ThingDef wasteDef)
        {
            if (wasteDef == null)
                return;

            float threshold = Threshold;
            int count = Mathf.FloorToInt(buffer / threshold);
            if (count <= 0)
                return;

            if (SpawnWaste(wasteDef, count))
                buffer -= count * threshold;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!RiimbaModMain.Settings.generateWaste || parent.Map == null)
                yield break;

            if (bioBuffer <= 0f && trashBuffer <= 0f)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "Riimba.DumpWasteLabel".Translate(),
                defaultDesc = "Riimba.DumpWasteDesc".Translate(),
                icon = TexCommand.Install,
                action = DumpBufferedWaste,
            };
        }

        // Rounds up to a whole item but subtracts the whole rounded amount, letting the buffer go
        // negative. Dumping early therefore borrows against later cleaning rather than conjuring
        // waste from nothing, so it cannot be cycled to farm trash.
        public void DumpBufferedWaste()
        {
            ResolveWasteDefsOnce();

            float threshold = Threshold;

            if (bioBuffer > 0f && bioWasteDef != null)
            {
                int count = Mathf.CeilToInt(bioBuffer / threshold);
                if (SpawnWaste(bioWasteDef, count))
                    bioBuffer -= count * threshold;
            }

            if (trashBuffer > 0f && trashWasteDef != null)
            {
                int count = Mathf.CeilToInt(trashBuffer / threshold);
                if (SpawnWaste(trashWasteDef, count))
                    trashBuffer -= count * threshold;
            }
        }

        // Only what has actually accumulated. Listing every waste type at 0% was noise on the
        // mending benches and would be noise here: a colony with no injuries never sees the bio
        // line at all until something bleeds.
        public override string CompInspectStringExtra()
        {
            if (!RiimbaModMain.Settings.generateWaste)
                return null;

            ResolveWasteDefsOnce();

            float threshold = Threshold;
            List<string> parts = new List<string>();

            if (trashBuffer > 0f && trashWasteDef != null)
                parts.Add(BufferReadout(trashWasteDef.label, trashBuffer, threshold));

            if (bioBuffer > 0f && bioWasteDef != null && bioWasteDef != trashWasteDef)
                parts.Add(BufferReadout(bioWasteDef.label, bioBuffer, threshold));

            if (parts.Count == 0)
                return null;

            string line = "Riimba.WasteBuffered".Translate(string.Join(", ", parts)).ToString();

            // Without this the station just sits there with units queueing up outside and no
            // stated reason: the backlog is only visible as a percentage that stopped moving.
            if (IsFull)
                line += "\n" + "Riimba.WasteBacklog".Translate();

            return line;
        }

        private static string BufferReadout(string label, float buffer, float threshold)
        {
            return $"{label} {(buffer / threshold).ToStringPercent()}";
        }

        // Reads the comp's own Props rather than a hardcoded list. Per instance, not static: two
        // stations may legitimately name different defs.
        private void ResolveWasteDefsOnce()
        {
            if (wasteDefsResolved)
                return;

            wasteDefsResolved = true;
            bioWasteDef = ResolveDef(Props?.bioWasteDefNames, Props?.bioWasteLabel);
            trashWasteDef = ResolveDef(Props?.trashWasteDefNames, Props?.trashWasteLabel);

            // Nothing in this install supplies a separate trash item, so ordinary cleaning waste
            // shares the bio def. Leaving it unresolved would be a slow jam: units would fill
            // their trash bins, the station would refuse every handover, and no readout would
            // explain why three drones were parked doing nothing.
            if (trashWasteDef == null)
                trashWasteDef = bioWasteDef;

            if (bioWasteDef == null)
                bioWasteDef = trashWasteDef;
        }

        // defName first because it is exact and cheap, then the item's visible label as a
        // fallback: a rename upstream should degrade to "find the item actually called trash",
        // not to silently producing the wrong kind of waste.
        private static ThingDef ResolveDef(List<string> candidates, string label)
        {
            foreach (string candidate in candidates ?? new List<string>())
            {
                ThingDef byName = DefDatabase<ThingDef>.GetNamedSilentFail(candidate);
                if (byName != null)
                    return byName;
            }

            if (label.NullOrEmpty())
                return null;

            return DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d =>
                d.category == ThingCategory.Item
                && d.label != null
                && d.label.Equals(label, StringComparison.OrdinalIgnoreCase));
        }

        private bool SpawnWaste(ThingDef wasteDef, int count)
        {
            if (wasteDef == null || count <= 0 || parent.Map == null)
                return false;

            // Spawn the whole amount, in as many stacks as the def's stack limit needs. Clamping
            // to one stack would bin the remainder while the buffer was debited for all of it.
            IntVec3 cell = GetWasteDropCell();
            int remaining = count;

            while (remaining > 0)
            {
                Thing waste = ThingMaker.MakeThing(wasteDef);
                waste.stackCount = Mathf.Min(remaining, wasteDef.stackLimit);
                remaining -= waste.stackCount;

                GenPlace.TryPlaceThing(waste, cell, parent.Map, ThingPlaceMode.Near);
            }

            return true;
        }

        private IntVec3 GetWasteDropCell()
        {
            if (parent is Building_RiimbaStation station)
            {
                IntVec3 output = station.WasteOutputCell;
                if (output.InBounds(parent.Map) && output.Walkable(parent.Map))
                    return output;
            }

            return parent.Position;
        }
    }
}
