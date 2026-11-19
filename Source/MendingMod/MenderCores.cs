using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MendingMod
{
    /// <summary>
    /// The machine brains the industrial repair center can run on. It is built with a basic
    /// subcore (tier 0); each tier above adds +4 skill, +20% speed and -20% materials.
    /// Tiers whose item is not in this game (no Biotech for the subcores) are simply not offered.
    /// </summary>
    public static class MenderCores
    {
        public const int MaxTier = 3;
        public const int SkillPerTier = 4;
        public const float SpeedPerTier = 0.2f;
        public const float SavingPerTier = 0.2f;

        private static readonly string[][] Candidates =
        {
            new[] { "SubcoreBasic", "BasicSubcore" },
            new[] { "SubcoreRegular", "RegularSubcore", "SubcoreStandard" },
            new[] { "SubcoreHigh", "HighSubcore" },
            new[] { "AIPersonaCore" },
        };

        private static readonly string[] Labels = { "basic subcore", "standard subcore", "high subcore", "persona core" };

        private static ThingDef[] defs;

        public static ThingDef DefFor(int tier)
        {
            if (defs == null)
            {
                defs = new ThingDef[MaxTier + 1];
                for (int i = 0; i <= MaxTier; i++)
                {
                    foreach (string name in Candidates[i])
                    {
                        defs[i] = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                        if (defs[i] != null)
                            break;
                    }
                    if (defs[i] == null)
                    {
                        string label = Labels[i];
                        defs[i] = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(d =>
                            d.category == ThingCategory.Item && d.label != null
                            && d.label.Equals(label, System.StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            return tier >= 0 && tier <= MaxTier ? defs[tier] : null;
        }

        public static string LabelFor(int tier)
        {
            ThingDef def = DefFor(tier);
            return def != null ? def.label : (tier >= 0 && tier <= MaxTier ? Labels[tier] : "?");
        }
    }

    /// <summary>Hauls the core the player asked for to a repair center and installs it.</summary>
    public class WorkGiver_InstallMenderCore : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            return pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_AutomatedMender>()
                .Where(m => m.CoreUpgradePending).Cast<Thing>();
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_AutomatedMender mender) || !mender.CoreUpgradePending)
                return false;
            if (t.IsForbidden(pawn) || t.IsBurning() || !pawn.CanReserve(t, 1, -1, null, forced))
                return false;
            return FindCore(pawn, mender) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building_AutomatedMender mender) || !HasJobOnThing(pawn, t, forced))
                return null;
            Thing core = FindCore(pawn, mender);
            if (core == null)
                return null;
            Job job = JobMaker.MakeJob(MendingDefOf.InstallMenderCore, t, core);
            job.count = 1;
            return job;
        }

        private static Thing FindCore(Pawn pawn, Building_AutomatedMender mender)
        {
            ThingDef def = MenderCores.DefFor(mender.WantedCoreTier);
            if (def == null)
                return null;
            return GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(def),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f,
                c => !c.IsForbidden(pawn) && pawn.CanReserve(c));
        }
    }

    public class JobDriver_InstallMenderCore : JobDriver
    {
        private const TargetIndex MenderInd = TargetIndex.A;
        private const TargetIndex CoreInd = TargetIndex.B;
        private const int InstallTicks = 600;

        private Building_AutomatedMender Mender => job.GetTarget(MenderInd).Thing as Building_AutomatedMender;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(MenderInd), job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(job.GetTarget(CoreInd), job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(MenderInd);
            this.FailOnBurningImmobile(MenderInd);
            this.FailOn(() => Mender == null || !Mender.CoreUpgradePending
                || MenderCores.DefFor(Mender.WantedCoreTier) != job.GetTarget(CoreInd).Thing?.def);

            yield return Toils_Goto.GotoThing(CoreInd, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(CoreInd);
            yield return Toils_Haul.StartCarryThing(CoreInd);
            yield return Toils_Goto.GotoThing(MenderInd, PathEndMode.Touch);

            Toil install = new Toil
            {
                defaultDuration = InstallTicks,
                defaultCompleteMode = ToilCompleteMode.Delay,
            };
            install.WithProgressBarToilDelay(MenderInd);
            install.FailOnDespawnedOrNull(MenderInd);
            yield return install;

            yield return new Toil
            {
                initAction = () =>
                {
                    Thing carried = pawn.carryTracker.CarriedThing;
                    if (carried == null || Mender == null)
                        return;
                    Thing one = carried.stackCount > 1 ? carried.SplitOff(1) : carried;
                    if (one == carried)
                        pawn.carryTracker.innerContainer.Remove(carried);
                    Mender.InstallCore(one);
                },
                defaultCompleteMode = ToilCompleteMode.Instant,
            };
        }
    }
}
