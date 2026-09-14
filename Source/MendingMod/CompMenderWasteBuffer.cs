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

        private static ThingDef toxicWasteDefCache;
        private static ThingDef trashWasteDefCache;
        private static bool wasteDefsResolved;

        public CompProperties_MenderWasteBuffer Props => (CompProperties_MenderWasteBuffer)props;

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

            if (UsesToxicIngredients(item.def, item.Stuff))
            {
                toxicBuffer += wasteAmount;
            }
            else
            {
                trashBuffer += wasteAmount;
            }

            TryDropWaste();
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

        private void TryDropWaste()
        {
            ResolveWasteDefsOnce();

            float threshold = Props?.wasteBufferThreshold ?? 1f;

            if (toxicBuffer >= threshold && toxicWasteDefCache != null)
            {
                int count = Mathf.FloorToInt(toxicBuffer / threshold);
                if (SpawnWaste(toxicWasteDefCache, count))
                    toxicBuffer -= count * threshold;
            }

            if (trashBuffer >= threshold && trashWasteDefCache != null)
            {
                int count = Mathf.FloorToInt(trashBuffer / threshold);
                if (SpawnWaste(trashWasteDefCache, count))
                    trashBuffer -= count * threshold;
            }
        }

        private static void ResolveWasteDefsOnce()
        {
            if (wasteDefsResolved)
                return;

            wasteDefsResolved = true;
            toxicWasteDefCache = DefDatabase<ThingDef>.GetNamedSilentFail("Wastepack");

            foreach (string candidate in new[] { "VRE_Trash", "VRE_TrashBag", "Trash" })
            {
                trashWasteDefCache = DefDatabase<ThingDef>.GetNamedSilentFail(candidate);
                if (trashWasteDefCache != null)
                    break;
            }
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
