using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace MendingMod
{
    // count/defName fields are XML-tunable so a mending building can be configured
    // without recompiling, and so the trash defName can be corrected to match
    // whichever waste mod (or none) is actually loaded.
    public class CompProperties_MenderWasteBuffer : CompProperties
    {
        public float wasteBufferThreshold = 1f;
        public string toxicWasteDefName = "Wastepack";
        public List<string> trashWasteDefNames = new List<string> { "VRE_Trash", "VRE_TrashBag", "Trash" };

        public CompProperties_MenderWasteBuffer()
        {
            compClass = typeof(CompMenderWasteBuffer);
        }
    }

    public class CompMenderWasteBuffer : ThingComp
    {
        private float toxicBuffer;
        private float trashBuffer;

        private ThingDef toxicWasteDef;
        private ThingDef trashWasteDef;
        private bool wasteDefsResolved;

        public CompProperties_MenderWasteBuffer Props => (CompProperties_MenderWasteBuffer)props;

        private float Threshold => Mathf.Max(0.01f, Props?.wasteBufferThreshold ?? 1f);

        // Once either buffer reaches its threshold the building stops taking work until someone
        // empties it - otherwise "a pawn has to remove the waste" carries no weight.
        public bool IsFull => MendingModMain.Settings.generateWaste
            && ((toxicBuffer >= Threshold && HasDefFor(toxic: true))
                || (trashBuffer >= Threshold && HasDefFor(toxic: false)));

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!MendingModMain.Settings.generateWaste || parent.Map == null)
                yield break;

            if (toxicBuffer <= 0f && trashBuffer <= 0f)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "DynamicMending.DumpWasteLabel".Translate(),
                defaultDesc = "DynamicMending.DumpWasteDesc".Translate(),
                icon = TexCommand.Install,
                action = DumpBufferedWaste,
            };
        }

        // Rounds up to a whole item but subtracts the whole rounded amount, letting the buffer go
        // negative. Dumping early therefore borrows against later repairs rather than conjuring
        // waste from nothing, so it can't be cycled to farm recyclable trash.
        public void DumpBufferedWaste()
        {
            ResolveWasteDefsOnce();

            float threshold = Threshold;

            if (toxicBuffer > 0f && toxicWasteDef != null)
            {
                int count = Mathf.CeilToInt(toxicBuffer / threshold);
                if (SpawnWaste(toxicWasteDef, count))
                    toxicBuffer -= count * threshold;
            }

            if (trashBuffer > 0f && trashWasteDef != null)
            {
                int count = Mathf.CeilToInt(trashBuffer / threshold);
                if (SpawnWaste(trashWasteDef, count))
                    trashBuffer -= count * threshold;
            }
        }

        // Buffers fill per repair, not per tick, so there is no honest countdown to show - what
        // matters is how close each is to spilling and roughly how many more repairs that takes.
        public override string CompInspectStringExtra()
        {
            if (!MendingModMain.Settings.generateWaste)
                return null;

            ResolveWasteDefsOnce();

            float threshold = Threshold;
            List<string> parts = new List<string>();

            // Only what has actually accumulated. Listing every waste type at 0% was noise -
            // a hand bench turns away anything with components in it, so the toxic line sat at
            // zero forever unless someone mended a uranium weapon on it.
            if (toxicBuffer > 0f && toxicWasteDef != null)
                parts.Add(BufferReadout(toxicWasteDef.label, toxicBuffer, threshold));

            if (trashBuffer > 0f && trashWasteDef != null && trashWasteDef != toxicWasteDef)
                parts.Add(BufferReadout(trashWasteDef.label, trashBuffer, threshold));

            if (parts.Count == 0)
                return "DynamicMending.WasteEmpty".Translate();

            string line = "DynamicMending.WasteBuffered".Translate(string.Join(", ", parts)).ToString();

            // Without this the building just sits there doing nothing with no stated reason - the
            // work giver's fail reason is only visible on a right-click menu nobody thinks to open.
            if (IsFull)
                line += "\n" + "DynamicMending.WasteFull".Translate();

            return line;
        }

        private static string BufferReadout(string label, float buffer, float threshold)
        {
            return $"{label} {(buffer / threshold).ToStringPercent()}";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref toxicBuffer, "toxicBuffer", 0f);
            Scribe_Values.Look(ref trashBuffer, "trashBuffer", 0f);
        }

        // Called by the recipe/job logic that performs the actual mend, before the
        // item's hit points are restored, so the missing-HP fraction still reflects
        // the work that was just done.
        public void Notify_ItemMended(Thing item, int skillLevel)
        {
            if (item == null || !MendingModMain.Settings.generateWaste)
                return;

            float missingHpFraction = MendingUtility.GetMissingHpFraction(item);
            if (missingHpFraction <= 0f)
                return;

            // The materials a clumsy mender burns past what the repair actually needed have to go
            // somewhere, so waste rides the same skill curve as the material cost: half the
            // baseline at skill 20, normal at skill 10, and well over double down at skill 1.
            float wasteAmount = missingHpFraction
                * MendingUtility.SkillEffortFactor(skillLevel)
                * MendingModMain.Settings.degradationMultiplier;

            AddWaste(wasteAmount, UsesToxicIngredients(item.def, item.Stuff));
        }

        private static bool UsesToxicIngredients(ThingDef itemDef, ThingDef stuffDef)
        {
            if (IsToxicMaterial(stuffDef))
                return true;

            if (itemDef.costList != null)
            {
                foreach (ThingDefCountClass cost in itemDef.costList)
                {
                    if (IsToxicMaterial(cost.thingDef))
                        return true;
                }
            }

            return false;
        }

        private static bool IsToxicMaterial(ThingDef def)
        {
            return def == ThingDefOf.ComponentIndustrial
                || def == ThingDefOf.ComponentSpacer
                || def == ThingDefOf.Uranium;
        }

        // Reads the comp's own Props rather than a hardcoded list, which is what the XML fields
        // always claimed to do. Per instance, not static: two buildings may name different defs.
        private void ResolveWasteDefsOnce()
        {
            if (wasteDefsResolved)
                return;

            wasteDefsResolved = true;
            toxicWasteDef = DefDatabase<ThingDef>.GetNamedSilentFail(Props?.toxicWasteDefName ?? "Wastepack");

            foreach (string candidate in Props?.trashWasteDefNames ?? new List<string>())
            {
                trashWasteDef = DefDatabase<ThingDef>.GetNamedSilentFail(candidate);
                if (trashWasteDef != null)
                    break;
            }

            // Nothing in this install supplies a separate trash item, so ordinary repair waste
            // shares the toxic def. Leaving it unresolved was a soft-lock: waste still went into
            // the trash buffer, DumpBufferedWaste skipped it for having no def so it could never
            // drain, and IsFull eventually stopped the building with no readout to explain why.
            if (trashWasteDef == null)
                trashWasteDef = toxicWasteDef;
        }

        // Routes an amount to whichever buffer actually has somewhere to put it. A buffer with no
        // def accumulates forever and can never be emptied, so it is simply never filled.
        private void AddWaste(float amount, bool toxic)
        {
            ResolveWasteDefsOnce();

            ThingDef target = toxic ? toxicWasteDef : trashWasteDef;
            if (target == null)
                return;

            if (target == toxicWasteDef)
                toxicBuffer += amount;
            else
                trashBuffer += amount;
        }

        // Only a buffer that can actually be emptied counts towards stopping the building.
        private bool HasDefFor(bool toxic)
        {
            ResolveWasteDefsOnce();
            return (toxic ? toxicWasteDef : trashWasteDef) != null;
        }

        private bool SpawnWaste(ThingDef wasteDef, int count)
        {
            if (wasteDef == null || count <= 0 || parent.Map == null)
                return false;

            Thing waste = ThingMaker.MakeThing(wasteDef);
            waste.stackCount = Mathf.Min(count, wasteDef.stackLimit);

            GenPlace.TryPlaceThing(waste, GetWasteDropCell(), parent.Map, ThingPlaceMode.Near);
            return true;
        }

        private IntVec3 GetWasteDropCell()
        {
            IntVec3 dropCell = parent.Position + parent.Rotation.Opposite.FacingCell;

            if (dropCell.InBounds(parent.Map) && dropCell.Walkable(parent.Map))
                return dropCell;

            return parent.Position;
        }
    }
}
