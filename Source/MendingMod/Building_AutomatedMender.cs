using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MendingMod
{
    // What the repair centre does with tainted apparel that reaches its item spot.
    public enum TaintedMode : byte
    {
        Repair,
        Recycle,
        Reject,
    }

    // Tick-driven state machine: Idle scans its input spots for work, Working counts down a
    // dynamically-computed work duration on the swallowed item, then ejects it out the front.
    // Runs the same MendingUtility formulas as the pawn path, but with a fixed skill level
    // (functions as a skill-10 worker per design) instead of reading a pawn's skill.
    //
    // Gear and materials feed in along the back edge and finished items leave by a single port
    // at the front. Anything sitting in those cells counts, whether it's loose
    // on the ground, in a stockpile zone, or in a storage building - vanilla storage keeps its
    // contents spawned on its own cells, so a plain cell scan already sees shelves and the like.
    // Buildings that hold items in an internal container instead (deep-storage mods, and Vanilla
    // Furniture Expanded's conveyors) are read through IThingHolder, which is also what makes
    // conveyor feeding work with no dependency on that mod.
    [StaticConstructorOnStartup]
    public class Building_AutomatedMender : Building, IThingHolder, IBillGiver
    {
        // The skill it works at at 100% overclock, as it always did before overclocking existed.
        private const int FixedSkillLevel = 10;

        // Overclocking, after Vanilla Furniture Expanded - Factory's machines: the same 10%-200%
        // range, work time divided by the setting, and power and heat both scaled by
        // 0.25 + 0.75 x setting squared (PipeSystem's CompPowerTrader_Overclocked and
        // CompHeatPusherPowered_Overclocked). Unlike theirs it also trades speed for care, which
        // is what a repair is about: slowed right down it works like a master and almost never
        // botches a job; pushed hard it rushes like a novice.
        public const float MinOverclock = 0.1f;
        public const float MaxOverclock = 2f;
        private const float IdleLoadFraction = 0.25f;
        private const int SlowestSkill = 20;
        private const int FastestSkill = 5;

        // Built in a static constructor context so the materials are created after the graphics
        // system is up, which is what [StaticConstructorOnStartup] guarantees.
        // Vanilla Furniture Expanded - Factory's own overclock icon when that mod is loaded, so the
        // repair center's button matches the factory machines beside it; vanilla's otherwise.
        private static readonly Texture2D OverclockIcon =
            ContentFinder<Texture2D>.Get("UI/Gizmo/Factory_Overclock", false) ?? TexCommand.DesirePower;

        private static readonly Material BarFilledMat =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.24f, 0.79f, 0.85f));
        private static readonly Material BarUnfilledMat =
            SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.12f, 0.13f, 0.14f));

        // Gathering: the item is inside and the machine is collecting its materials from the
        // resource spots as they turn up; the job starts once it holds all of them. (Appended last
        // so saved states keep their numbers.)
        private enum MenderState : byte { Idle, Working, Ejecting, Gathering }

        private ThingOwner<Thing> innerContainer;
        private Thing currentItem;
        private int workTicksRemaining;
        private int workTicksTotal;
        private float overclock = 1f;

        // The working sound and glow. Not saved: a loaded machine picks them up on its next tick.
        private Sustainer workSustainer;
        private Thing workSoundItem;
        private bool glowLit;
        private int jobSkill = FixedSkillLevel;     // fixed when a job starts, so its cost and its result agree
        private MenderState state = MenderState.Idle;
        private string idleReason;

        // What the machine will take. Anything on the item spot it will not take - filtered out,
        // undamaged, above the auto-repair threshold, beyond its tech - goes out a reject chute
        // rather than sitting on the spot and stalling the belt behind it.
        // Legacy (before bills): loaded only to turn into bills, then cleared.
        private ThingFilter acceptFilter;
        private TaintedMode taintedMode = TaintedMode.Repair;

        // What the machine breaks down instead of repairing - the repair centre's answer to a
        // bench's recycle bill. Checked before the accept filter, so gear in both is recycled,
        // and damage does not matter, as it does not for a recycle bill. Starts empty, which
        // leaves a machine doing exactly what it did before this existed.
        private ThingFilter recycleFilter;

        // Things inside the machine waiting for a reject chute: rejected gear, and whatever a
        // recycle broke down into. Held here while both chutes are blocked, exactly as a repaired
        // item waits for the output spot.
        private List<Thing> rejectQueue = new List<Thing>();
        private bool recycling;
        private string lastRejected;

        // The materials the current repair costs. Held inside the machine as they are gathered
        // and only used up when the repair finishes, so cancelling gives them all back.
        private List<ThingDefCountClass> pendingCosts;

        // What to do with gear on the item spot: repair and recycle bills, worked down in order.
        private BillStack billStack;

        // The bill the current job came from, so "do X times" counts it when it finishes.
        private Bill_Mender currentBill;

        // Set once its first bills exist (new, or turned from a pre-bills machine's filters), so a
        // player who deletes every bill is not handed the default one back on the next load.
        private bool billsInitialized;

        // Its machine brain: 0 the basic subcore it is built with, up to MenderCores.MaxTier.
        // wantedCoreTier above coreTier asks a constructor to bring and install that core.
        private int coreTier;
        private int wantedCoreTier;

        public Building_AutomatedMender()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: false);
            billStack = new BillStack(this);
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
            Scribe_Values.Look(ref workTicksTotal, "workTicksTotal");
            Scribe_Values.Look(ref overclock, "overclock", 1f);
            Scribe_Values.Look(ref jobSkill, "jobSkill", FixedSkillLevel);
            Scribe_Values.Look(ref state, "state");
            Scribe_Deep.Look(ref billStack, "bills", this);
            Scribe_References.Look(ref currentBill, "currentBill");
            Scribe_Values.Look(ref billsInitialized, "billsInitialized", false);
            Scribe_Values.Look(ref coreTier, "coreTier", 0);
            Scribe_Values.Look(ref wantedCoreTier, "wantedCoreTier", 0);
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref acceptFilter, "acceptFilter");
                Scribe_Deep.Look(ref recycleFilter, "recycleFilter");
                Scribe_Values.Look(ref taintedMode, "taintedMode", TaintedMode.Repair);
            }
            Scribe_Collections.Look(ref rejectQueue, "rejectQueue", LookMode.Reference);
            Scribe_Values.Look(ref recycling, "recycling");
            Scribe_Collections.Look(ref pendingCosts, "pendingCosts", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (billStack == null)
                    billStack = new BillStack(this);
                if (!billsInitialized)
                    MigrateToBills();
                rejectQueue = rejectQueue ?? new List<Thing>();
                rejectQueue.RemoveAll(t => t == null);
            }
        }

        // ---- bills
        public BillStack BillStack => billStack;

        public IEnumerable<IntVec3> IngredientStackCells => Enumerable.Empty<IntVec3>();

        // Pawns never work these bills; the machine runs them itself.
        public bool CurrentlyUsableForBills() => false;

        public bool UsableForBillsAfterFueling() => false;

        public void Notify_BillDeleted(Bill bill)
        {
            if (bill == currentBill)
                currentBill = null;
        }

        private static SpecialThingFilterDef Special(string defName) => DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(defName);

        // A new machine starts with one bill: repair any gear, which is what it always did.
        // A machine from before bills turns its accepted-gear filter into a repair bill, its
        // recycled-gear filter (if anything was in it) into a recycle bill above it, and its
        // tainted-apparel setting into those bills' "deadman's apparel" checkboxes.
        private void MigrateToBills()
        {
            Bill_Mender repair = new Bill_Mender(MendingDefOf.MenderBill_Repair);
            if (acceptFilter != null)
                repair.ingredientFilter.CopyAllowancesFrom(acceptFilter);

            Bill_Mender recycle = null;
            if (recycleFilter != null && recycleFilter.AllowedDefCount > 0)
            {
                recycle = new Bill_Mender(MendingDefOf.MenderBill_Recycle);
                recycle.ingredientFilter.CopyAllowancesFrom(recycleFilter);
            }

            SpecialThingFilterDef tainted = Special("AllowDeadmansApparel");
            SpecialThingFilterDef clean = Special("AllowNonDeadmansApparel");
            if (taintedMode != TaintedMode.Repair && tainted != null)
            {
                repair.ingredientFilter.SetAllow(tainted, false);
                if (recycle != null)
                    recycle.ingredientFilter.SetAllow(tainted, false);
            }
            if (taintedMode == TaintedMode.Recycle && tainted != null && clean != null)
            {
                Bill_Mender taintedBill = new Bill_Mender(MendingDefOf.MenderBill_Recycle);
                taintedBill.ingredientFilter.SetAllow(ThingCategoryDefOf.Apparel, true);
                taintedBill.ingredientFilter.SetAllow(clean, false);
                billStack.AddBill(taintedBill);
            }

            if (recycle != null)
                billStack.AddBill(recycle);
            billStack.AddBill(repair);

            acceptFilter = null;
            recycleFilter = null;
            billsInitialized = true;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!billsInitialized)
                MigrateToBills();
        }

        private bool PowerOn
        {
            get
            {
                // Asked every tick; the comp list never changes after the building is made, so
                // the lookup is done once.
                if (!powerLooked)
                {
                    powerComp = GetComp<CompPowerTrader>();
                    powerLooked = true;
                }

                return powerComp == null || powerComp.PowerOn;
            }
        }

        private CompPowerTrader powerComp;
        private bool powerLooked;

        private CompPowerTrader PowerTrader
        {
            get
            {
                if (!powerLooked)
                {
                    powerComp = GetComp<CompPowerTrader>();
                    powerLooked = true;
                }

                return powerComp;
            }
        }

        // Checks in a row that found nothing on the item spot. Not saved: a loaded machine just
        // starts out attentive again.
        private int emptyScans;
        private bool spotWasEmpty;
        private const int EmptyScansBeforeSlowing = 8;
        private const int ScanInterval = 30;
        private const int SlowScanInterval = 90;

        // Uninstalling or destroying the machine comes through here - MinifyUtility.MakeMinified
        // deinstalls with DeSpawnOrDeselect before wrapping the building up - and anything still
        // inside would travel into the minified crate with it: unreachable while it sits there,
        // and destroyed outright if that crate ever is. So the item goes back on the floor first.
        // The state machine is reset too, or a reinstalled machine wakes up believing it is
        // mid-repair on something it no longer holds, and jams on "output blocked" forever.
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (Spawned && innerContainer.Count > 0)
                innerContainer.TryDropAll(Position, Map, ThingPlaceMode.Near);

            currentItem = null;
            workTicksRemaining = 0;
            workTicksTotal = 0;
            idleReason = null;
            state = MenderState.Idle;
            rejectQueue.Clear();         // dropped with everything else, just above
            recycling = false;
            pendingCosts = null;
            EndWorkSound();

            base.DeSpawn(mode);
        }

        protected override void Tick()
        {
            base.Tick();

            UpdateWorkEffects();

            if (!PowerOn)
                return;

            // Vanilla's heat pusher works in one-second steps too; CompPowerTrader resets its draw
            // to the def's whenever power comes back on, so it is re-applied rather than set once.
            if (this.IsHashIntervalTick(60))
                ApplyOverclockLoad();

            // Twice a second is plenty for noticing new input or a cleared output spot, and avoids
            // re-scanning the spots on every one of the 60 ticks per second. A machine whose item
            // spot has stayed empty for a few seconds looks less often still, until something
            // turns up; one waiting on materials, the waste container or a chute keeps the pace.
            if (state == MenderState.Idle)
            {
                if (this.IsHashIntervalTick(emptyScans >= EmptyScansBeforeSlowing ? SlowScanInterval : ScanInterval))
                {
                    spotWasEmpty = false;
                    TryStartMend();
                    if (!spotWasEmpty)
                        emptyScans = 0;
                    else if (emptyScans < EmptyScansBeforeSlowing)
                        emptyScans++;
                }
            }
            else if (state == MenderState.Ejecting)
            {
                if (this.IsHashIntervalTick(ScanInterval))
                    TryEject();
            }
            else if (state == MenderState.Gathering)
            {
                if (this.IsHashIntervalTick(ScanInterval))
                    Gather();
            }
            else
            {
                TickWork();
            }
        }

        private CellRect Footprint => this.OccupiedRect();

        public IntVec3 ItemInputCell => MenderSpots.ItemInputCell(Footprint, Rotation);

        public IEnumerable<IntVec3> ResourceInputCells => MenderSpots.ResourceInputCells(Footprint, Rotation);

        // ---- Logistics hook (found by name by SloppyMods Factory Logistics, no reference either way)
        //
        // A conveyor arm Feeder standing behind the item spot asks these: where to deliver, which
        // items the machine would take - gear its filters would repair or recycle, never one it
        // would only send back out as a reject - and how many it wants queued on the way.
        public IntVec3 LogisticsInputCell => ItemInputCell;

        public bool LogisticsAccepts(Thing thing)
        {
            return Spawned && thing != null && IsGear(thing) && !IsOffLimits(thing)
                && Decide(thing, out string _) != Intake.Reject;
        }

        public int LogisticsCapacity => 4;

        // The material spots, and what a Feeder at one of them should bring: what the repair being
        // gathered still lacks, less what is already lying on the other material spots. (What is
        // on the Feeder's own spot, and on its belts, the Feeder counts itself.)
        public IEnumerable<IntVec3> LogisticsResourceCells => ResourceInputCells;

        public List<ThingDefCountClass> LogisticsMaterialNeeds(IntVec3 cell)
        {
            List<ThingDefCountClass> needs = new List<ThingDefCountClass>();
            if (!Spawned || state != MenderState.Gathering || pendingCosts == null)
                return needs;
            List<Thing> elsewhere = ThingsOn(ResourceInputCells.Where(c => c != cell)).Where(s => !IsOffLimits(s)).ToList();
            foreach (ThingDefCountClass cost in pendingCosts)
            {
                int remaining = cost.count - HeldOf(cost.thingDef)
                    - elsewhere.Where(s => s.def == cost.thingDef).Sum(s => s.stackCount);
                if (remaining > 0)
                    needs.Add(new ThingDefCountClass(cost.thingDef, remaining));
            }
            return needs;
        }

        // Why it would not take an item, for the arm's inspect pane; null if it would.
        public string LogisticsRefusal(Thing thing)
        {
            if (thing == null)
                return null;
            if (!IsGear(thing))
                return "DynamicMending.RejectNotGear".Translate();
            if (IsOffLimits(thing))
                return "DynamicMending.RejectForbidden".Translate();
            return Decide(thing, out string why) == Intake.Reject ? why : null;
        }

        public IntVec3 OutputCell => MenderSpots.OutputCell(Footprint, Rotation);

        public List<IntVec3> RejectCells => MenderSpots.RejectCells(Footprint, Rotation);

        public bool IsRepairing => state == MenderState.Working;

        public float Overclock
        {
            get => overclock;
            set
            {
                float next = Mathf.Clamp(value, MinOverclock, MaxOverclockNow);
                if (Mathf.Approximately(next, overclock))
                    return;

                // A job already running speeds up or slows down from where it is, as VFE's
                // processes do when the slider moves; the skill it was started at stands.
                if (state == MenderState.Working)
                {
                    workTicksRemaining = Mathf.Max(1, Mathf.RoundToInt(workTicksRemaining * overclock / next));
                    workTicksTotal = Mathf.Max(1, Mathf.RoundToInt(workTicksTotal * overclock / next));
                }

                overclock = next;
            }
        }

        // 10 at 100%, rising to 20 at the slowest setting and falling to 5 at 200%.
        public static int SkillAt(float setting)
        {
            // Beyond 200% - reachable only with VFE Factory's boosters - it keeps getting worse at
            // the same rate, down to skill 0.
            float skill = setting <= 1f
                ? Mathf.Lerp(SlowestSkill, FixedSkillLevel, (setting - MinOverclock) / (1f - MinOverclock))
                : FixedSkillLevel - (FixedSkillLevel - FastestSkill) * (setting - 1f) / (MaxOverclock - 1f);
            return Mathf.Clamp(Mathf.RoundToInt(skill), 0, 20);
        }

        // Vanilla Expanded Framework's two factory stats, raised and lowered by VFE Factory's
        // booster and heatsink facilities once the compat patch links them to this building.
        // Looked up by name: without the framework they do not exist and both read as 1.
        // VEF_BuildingBreakdownFactor is the framework's third factory stat: breakdowns come that
        // many times as often. It is 1 unless something raises it.
        private enum FactoryStatKind { MaxOverclock, Heat, Breakdown }

        private static StatDef[] factoryStats;

        private float FactoryStat(FactoryStatKind kind)
        {
            if (factoryStats == null)
            {
                factoryStats = new[] { "VEF_BuildingMaxOverclockFactor", "VEF_FactoryHeatPerSecondFactor", "VEF_BuildingBreakdownFactor" }
                    .Select(n => DefDatabase<StatDef>.GetNamedSilentFail(n))
                    .ToArray();
            }

            StatDef stat = factoryStats[(int)kind];
            return stat == null ? 1f : this.GetStatValue(stat);
        }

        public float MaxOverclockNow => MaxOverclock * Mathf.Max(1f, FactoryStat(FactoryStatKind.MaxOverclock));

        public float HeatFactor => Mathf.Max(0f, FactoryStat(FactoryStatKind.Heat));

        // ------------------------------------------------------------------ breakdowns
        //
        // Vanilla's CompBreakdownable rolls Rand.MTBEventOccurs(13680000, 1, 1041) every time the
        // BreakdownManager checks it, every 1041 ticks. Patch_CompBreakdownable_CheckForBreakdown
        // hands the roll for this building here instead, so it can scale the risk with the
        // overclock, by the same 0.25 + 0.75 x setting squared as power and heat: about a quarter
        // of the usual rate at 10%, the usual rate at 100%, three and a quarter times it at 200%.
        private const float VanillaBreakdownMTBTicks = 13680000f;
        private const float BreakdownCheckIntervalTicks = 1041f;

        public float BreakdownFactor => LoadFactorAt(overclock) * Mathf.Max(0f, FactoryStat(FactoryStatKind.Breakdown));

        public void CheckForBreakdown(CompBreakdownable breakdown)
        {
            // Vanilla's own condition: not already broken, and powered.
            if (breakdown.BrokenDown || !PowerOn)
                return;

            float factor = BreakdownFactor;
            if (factor > 0f && Rand.MTBEventOccurs(VanillaBreakdownMTBTicks / factor, 1f, BreakdownCheckIntervalTicks))
                breakdown.DoBreakdown();
        }

        // ------------------------------------------------------------------ ideology
        //
        // Vanilla Memes Expanded's automation precepts, as Vanilla Expanded Framework applies them
        // to VFE Factory's machines (Process.GetFactoryAcceleration): work takes 0.75x as long
        // under the efficient one, 1.5x under the other. Looked up by name, so without that mod
        // (or without Ideology) neither exists and the factor is 1.
        private static PreceptDef automationFaster;
        private static PreceptDef automationSlower;
        private static bool preceptsLooked;

        public static float PreceptWorkFactor
        {
            get
            {
                if (!preceptsLooked)
                {
                    automationFaster = DefDatabase<PreceptDef>.GetNamedSilentFail("VME_AutomationEfficiency_Increased");
                    automationSlower = DefDatabase<PreceptDef>.GetNamedSilentFail("VME_AutomationEfficiency_Decreased");
                    preceptsLooked = true;
                }

                if (automationFaster == null && automationSlower == null)
                    return 1f;

                FactionIdeosTracker ideos = Faction.OfPlayer?.ideos;
                if (ideos == null)
                    return 1f;
                if (automationFaster != null && ideos.GetPrecept(automationFaster) != null)
                    return 0.75f;
                if (automationSlower != null && ideos.GetPrecept(automationSlower) != null)
                    return 1.5f;
                return 1f;
            }
        }

        // ------------------------------------------------------------------ sound and glow
        //
        // The working sound is the one the item's own crafting recipe plays - the tailor's sewing
        // machine for clothes, the smithy's hammering for blades and armor, machining for guns -
        // so it sounds like the job it is doing. Gear with no recipe of its own falls back by
        // material: anything metal gets the smithy, cloth and leather the sewing machine.
        private static SoundDef soundTailor;
        private static SoundDef soundSmith;
        private static bool soundsLooked;

        private static SoundDef WorkSoundFor(Thing item)
        {
            SoundDef own = item.def.recipeMaker?.soundWorking;
            if (own != null)
                return own;

            if (!soundsLooked)
            {
                soundTailor = DefDatabase<SoundDef>.GetNamedSilentFail("Recipe_Tailor");
                soundSmith = DefDatabase<SoundDef>.GetNamedSilentFail("Recipe_Smith");
                soundsLooked = true;
            }

            List<StuffCategoryDef> categories = item.Stuff?.stuffProps?.categories;
            if (categories != null)
            {
                if (categories.Contains(StuffCategoryDefOf.Metallic))
                    return soundSmith;
                if (categories.Contains(StuffCategoryDefOf.Fabric) || categories.Contains(StuffCategoryDefOf.Leathery))
                    return soundTailor;
            }

            return item.def.IsApparel ? soundTailor : soundSmith;
        }

        private void UpdateWorkEffects()
        {
            bool working = state == MenderState.Working && currentItem != null && PowerOn && Spawned;

            if (working)
            {
                if (workSustainer == null || workSustainer.Ended || workSoundItem != currentItem)
                {
                    EndWorkSound();
                    SoundDef sound = WorkSoundFor(currentItem);
                    workSoundItem = currentItem;
                    if (sound != null && sound.sustain)
                        workSustainer = sound.TrySpawnSustainer(SoundInfo.InMap(this, MaintenanceType.PerTick));
                }

                workSustainer?.Maintain();
            }
            else
            {
                EndWorkSound();
            }

            if (working != glowLit && Spawned)
            {
                glowLit = working;
                GetComp<CompGlowerMending>()?.UpdateLit(Map);
            }
        }

        private void EndWorkSound()
        {
            if (workSustainer != null && !workSustainer.Ended)
                workSustainer.End();

            workSustainer = null;
            workSoundItem = null;
        }

        public static float LoadFactorAt(float setting) => IdleLoadFraction + (1f - IdleLoadFraction) * setting * setting;

        public int EffectiveSkill => Mathf.Clamp(SkillAt(overclock) + CoreSkillBonus, 0, 20);

        // ---- cores
        public int CoreTier => coreTier;

        public int WantedCoreTier => wantedCoreTier;

        public bool CoreUpgradePending => wantedCoreTier > coreTier && MenderCores.DefFor(wantedCoreTier) != null;

        public int CoreSkillBonus => coreTier * MenderCores.SkillPerTier;

        // Work goes this much faster, and materials cost this fraction of normal.
        public float CoreSpeedFactor => 1f + coreTier * MenderCores.SpeedPerTier;

        public float CoreCostFactor => Mathf.Max(0f, 1f - coreTier * MenderCores.SavingPerTier);

        // What a repair of this item costs at this skill, after the core's saving.
        private List<ThingDefCountClass> CostsFor(Thing item, int skill)
        {
            float factor = CoreCostFactor;
            return MendingUtility.GetDynamicIngredientCosts(item, skill, ignoreResearch: true)
                .Where(c => c.thingDef != null && c.count > 0)
                .Select(c => new ThingDefCountClass(c.thingDef, Mathf.Max(1, Mathf.CeilToInt(c.count * factor))))
                .ToList();
        }

        // A constructor has brought the wanted core. The core it replaces comes back out, except
        // the basic subcore it was built with, which is part of the building (and is refunded with
        // it when it is deconstructed).
        public void InstallCore(Thing core)
        {
            int tier = -1;
            for (int i = 1; i <= MenderCores.MaxTier; i++)
            {
                if (MenderCores.DefFor(i) == core.def)
                    tier = i;
            }
            if (tier <= coreTier || !Spawned)
            {
                GenPlace.TryPlaceThing(core, Position, Map, ThingPlaceMode.Near);
                return;
            }

            if (coreTier > 0)
            {
                ThingDef oldDef = MenderCores.DefFor(coreTier);
                if (oldDef != null)
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(oldDef), Position, Map, ThingPlaceMode.Near);
            }
            core.Destroy(DestroyMode.Vanish);
            coreTier = tier;
            if (wantedCoreTier < coreTier)
                wantedCoreTier = coreTier;
            Messages.Message("DynamicMending.CoreInstalled".Translate(MenderCores.LabelFor(coreTier)), this, MessageTypeDefOf.PositiveEvent, false);
        }

        // Deconstructed: an upgraded core comes back out with the refund.
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (mode == DestroyMode.Deconstruct && Spawned && coreTier > 0)
            {
                ThingDef def = MenderCores.DefFor(coreTier);
                if (def != null)
                    GenPlace.TryPlaceThing(ThingMaker.MakeThing(def), Position, Map, ThingPlaceMode.Near);
                coreTier = 0;
            }
            base.Destroy(mode);
        }

        public float BasePowerConsumption => PowerTrader?.Props.PowerConsumption ?? 0f;

        public float BaseHeatPerSecond => GetComp<CompHeatPusherMending>()?.Props.heatPerSecond ?? 0f;

        // Power is drawn at the overclocked rate whenever the machine is on, working or not, as
        // on VFE's machines; heat only while a job runs (CompHeatPusherMending).
        private void ApplyOverclockLoad()
        {
            // A booster taken away lowers the ceiling; VFE pulls its own machines back the same way.
            if (overclock > MaxOverclockNow)
                Overclock = MaxOverclockNow;

            CompPowerTrader power = PowerTrader;
            if (power != null && power.PowerOn)
                power.PowerOutput = -power.Props.PowerConsumption * LoadFactorAt(overclock);

            if (IsRepairing)
                GetComp<CompHeatPusherMending>()?.PushHeat(LoadFactorAt(overclock) * HeatFactor);
        }

        private IEnumerable<Thing> ThingsOn(IntVec3 cell)
        {
            if (!cell.InBounds(Map))
                yield break;

            foreach (Thing thing in cell.GetThingList(Map))
            {
                if (thing.def.category == ThingCategory.Item)
                    yield return thing;

                if (thing is IThingHolder holder && thing != this)
                {
                    ThingOwner owner = holder.GetDirectlyHeldThings();
                    if (owner == null)
                        continue;

                    foreach (Thing held in owner)
                    {
                        if (held.def.category == ThingCategory.Item)
                            yield return held;
                    }
                }
            }
        }

        private IEnumerable<Thing> ThingsOn(IEnumerable<IntVec3> cells)
        {
            return cells.SelectMany(ThingsOn);
        }

        // Only apparel and weapons are mendable (matching the two recipes), and checking that
        // rather than useHitPoints alone keeps deteriorated resource stacks out of the input.
        //
        // Deliberately NOT gated on research. Once this machine is built it repairs anything put
        // in front of it; its one limitation is that it always works at skill 10, never better.
        // The bench tech cap is still consulted so a def that declares one is honoured, but this
        // building carries no cap by default.
        private bool IsMendable(Thing thing)
        {
            return IsGear(thing)
                && thing.HitPoints < thing.MaxHitPoints
                && !IsOffLimits(thing)
                && MendingUtility.CanBenchRepair(def, thing);
        }

        // Both pawn benches honour forbidding; the machine did not, so it would happily swallow a
        // stack the player had explicitly set aside. Forbidding is the natural way to say "not
        // this one", and it should mean the same thing on a spot as it does anywhere else.
        private bool IsOffLimits(Thing thing)
        {
            return thing.IsForbidden(Faction ?? Faction.OfPlayer);
        }

        // Any apparel or weapon, damaged or not: undamaged gear on the item spot is now something
        // to send back out, not something to ignore while it blocks the belt behind it.
        private static bool IsGear(Thing thing)
        {
            return (thing.def.IsApparel || thing.def.IsWeapon) && thing.def.useHitPoints;
        }

        private enum Intake : byte { Repair, Recycle, Reject }

        // What to do with one piece of gear on the item spot, and - for a reject - why, so the
        // inspect pane can say. The bills are worked down in order; the first one that takes the
        // item decides. A repair bill whose filter allows the item but that cannot repair it
        // (undamaged, above the threshold, too advanced) passes it on to the next bill, so a
        // recycle bill lower down can still catch it.
        private Intake Decide(Thing thing, out string why)
        {
            return Decide(thing, out why, out Bill_Mender _);
        }

        private Intake Decide(Thing thing, out string why, out Bill_Mender bill)
        {
            why = null;
            bill = null;
            string firstReason = null;

            foreach (Bill b in billStack)
            {
                if (!(b is Bill_Mender mb) || !mb.ShouldDoNow() || !mb.ingredientFilter.Allows(thing))
                    continue;

                if (mb.IsRecycle)
                {
                    if (MendingUtility.CanEverRecycle(thing))
                    {
                        bill = mb;
                        return Intake.Recycle;
                    }
                    firstReason = firstReason ?? "DynamicMending.RejectCannotRecycle".Translate().ToString();
                    continue;
                }

                string reason = null;
                if (thing.HitPoints >= thing.MaxHitPoints)
                    reason = "DynamicMending.RejectUndamaged".Translate();
                else if (!MendingUtility.BelowAutoRepairThreshold(thing))
                    reason = "DynamicMending.RejectThreshold".Translate(MendingModMain.Settings.autoRepairBelow.ToStringPercent());
                else if (!IsMendable(thing))
                    reason = "DynamicMending.RejectTooAdvanced".Translate();

                if (reason == null)
                {
                    bill = mb;
                    return Intake.Repair;
                }
                firstReason = firstReason ?? reason;
            }

            why = firstReason ?? "DynamicMending.RejectFiltered".Translate().ToString();
            return Intake.Reject;
        }

        private void TryStartMend()
        {
            // Rejects leave before anything new comes in. A machine that kept taking items while
            // both chutes were blocked would fill itself with things it cannot get rid of.
            if (!FlushRejects())
            {
                idleReason = "DynamicMending.RejectChuteBlocked".Translate(rejectQueue.Count);
                return;
            }

            // One item per scan, whichever way it goes, so a conveyor's worth of rejects leaves at
            // a steady rate rather than all in one tick.
            Thing damagedItem = null;
            foreach (Thing thing in ThingsOn(ItemInputCell).ToList())
            {
                if (!IsGear(thing) || IsOffLimits(thing))
                    continue;

                switch (Decide(thing, out string why, out Bill_Mender decidedBy))
                {
                    case Intake.Reject:
                        Reject(thing, why);
                        return;

                    case Intake.Recycle:
                        currentBill = decidedBy;
                        StartRecycle(thing);
                        return;
                }

                currentBill = decidedBy;

                damagedItem = thing;
                break;
            }

            if (damagedItem == null)
            {
                spotWasEmpty = true;
                idleReason = "DynamicMending.MenderNoItem".Translate();
                return;
            }

            // Only a repair fills the waste container, so only a repair waits for it - rejects
            // and recycling carry on while it is full.
            if (GetComp<CompMenderWasteBuffer>()?.IsFull == true)
            {
                idleReason = "DynamicMending.WasteFull".Translate();
                return;
            }

            int skill = EffectiveSkill;

            // Take the item in straight away and gather its materials as they turn up: whatever is
            // on the resource spots now, the rest when it arrives. The item and everything gathered
            // stay inside until the repair finishes, or until the job is cancelled.
            if (!TryTakeIn(damagedItem))
            {
                idleReason = "DynamicMending.MenderCannotTakeItem".Translate(damagedItem.LabelShortCap);
                return;
            }

            currentItem = damagedItem;
            jobSkill = skill;
            recycling = false;
            pendingCosts = CostsFor(damagedItem, skill);
            state = MenderState.Gathering;
            idleReason = null;
            Gather();
        }

        // How much of a material the machine already holds for the current repair.
        private int HeldOf(ThingDef def)
        {
            int count = 0;
            foreach (Thing thing in innerContainer)
            {
                if (thing != currentItem && thing.def == def)
                    count += thing.stackCount;
            }
            return count;
        }

        // Pulls what the current repair still lacks off the resource spots, and starts the repair
        // once nothing is missing.
        private void Gather()
        {
            if (currentItem == null || currentItem.Destroyed || !innerContainer.Contains(currentItem))
            {
                ResetJob();
                return;
            }

            string missing = null;
            if (pendingCosts != null)
            {
                foreach (ThingDefCountClass cost in pendingCosts)
                {
                    int remaining = cost.count - HeldOf(cost.thingDef);
                    if (remaining <= 0)
                        continue;

                    foreach (Thing stack in ThingsOn(ResourceInputCells)
                        .Where(s => s.def == cost.thingDef && !IsOffLimits(s)).ToList())
                    {
                        if (remaining <= 0)
                            break;
                        int take = Mathf.Min(remaining, stack.stackCount);
                        Thing part = stack.SplitOff(take);
                        if (part.Spawned)
                            part.DeSpawn();
                        else if (part.holdingOwner != null)
                            part.holdingOwner.Remove(part);
                        if (!innerContainer.TryAdd(part, canMergeWithExistingStacks: true))
                        {
                            GenPlace.TryPlaceThing(part, Position, Map, ThingPlaceMode.Near);
                            break;
                        }
                        remaining -= take;
                    }

                    if (remaining > 0 && missing == null)
                    {
                        // Naming the exact shortfall rather than a generic "idle" - otherwise the
                        // only way to find out why nothing is happening is to guess at the spots.
                        missing = "DynamicMending.MenderGathering".Translate(
                            currentItem.LabelShortCap, remaining, cost.thingDef.label, HeldOf(cost.thingDef), cost.count);
                    }
                }
            }

            if (missing != null)
            {
                idleReason = missing;
                return;
            }

            idleReason = null;
            workTicksTotal = Mathf.Max(60, Mathf.RoundToInt(
                MendingUtility.GetDynamicWorkAmount(currentItem, jobSkill, ignoreResearch: true) * PreceptWorkFactor / (overclock * CoreSpeedFactor)));
            workTicksRemaining = workTicksTotal;
            state = MenderState.Working;
        }

        // The repair is done: the gathered materials are used up now, not when it started.
        private void ConsumeGathered()
        {
            if (pendingCosts == null)
                return;
            foreach (ThingDefCountClass cost in pendingCosts)
            {
                int remaining = cost.count;
                foreach (Thing thing in innerContainer.Where(x => x != currentItem && x.def == cost.thingDef).ToList())
                {
                    if (remaining <= 0)
                        break;
                    int take = Mathf.Min(remaining, thing.stackCount);
                    thing.SplitOff(take).Destroy(DestroyMode.Vanish);
                    remaining -= take;
                }
            }
            pendingCosts = null;
        }

        private void ResetJob()
        {
            currentItem = null;
            workTicksRemaining = 0;
            workTicksTotal = 0;
            idleReason = null;
            state = MenderState.Idle;
            recycling = false;
            pendingCosts = null;
            currentBill = null;
            rejectQueue.Clear();
        }

        // The player's way out of a stuck job: the item, every material gathered for it and
        // anything waiting for a chute go on the floor beside the machine - never onto its own
        // spots, where it would be taken straight back in - and the machine goes idle. The item is
        // forbidden, so a belt or a hauler does not bring it straight back either.
        private void CancelJob()
        {
            if (!Spawned)
                return;
            HashSet<IntVec3> spots = new HashSet<IntVec3>(ResourceInputCells);
            spots.Add(ItemInputCell);
            spots.Add(OutputCell);
            foreach (IntVec3 c in RejectCells)
                spots.Add(c);
            CellRect footprint = Footprint;
            Map map = Map;

            Thing item = currentItem;
            foreach (Thing thing in innerContainer.ToList())
            {
                innerContainer.TryDrop(thing, Position, map, ThingPlaceMode.Near, out Thing dropped, null,
                    c => !spots.Contains(c) && !footprint.Contains(c));
                if (dropped != null && thing == item)
                    dropped.SetForbidden(true, false);
            }
            if (innerContainer.Count > 0)
                innerContainer.TryDropAll(Position, map, ThingPlaceMode.Near);

            ResetJob();
            EndWorkSound();
            Messages.Message("DynamicMending.MenderCancelled".Translate(), this, MessageTypeDefOf.NeutralEvent, false);
        }

        // Not ThingOwner.TryAddOrTransfer: a spawned thing belongs to the map's own ThingOwner
        // (Map.spawnedThings), and TryAddOrTransfer routes anything with a holdingOwner through
        // TryTransferToContainer, which refuses outright when either side is a Map - it logs
        // "Can't transfer items to or from Maps directly. They must be spawned or despawned
        // manually" and returns false. So it can never lift an item off the ground or off a
        // shelf, which is what left items sitting on the input spot. Despawning first and then
        // adding is what that warning asks for.
        private bool TryTakeIn(Thing item)
        {
            if (!item.Spawned)
            {
                if (item.holdingOwner != null)
                    return item.holdingOwner.TryTransferToContainer(item, innerContainer, item.stackCount, out _, false) > 0;

                return innerContainer.TryAdd(item, canMergeWithExistingStacks: false);
            }

            IntVec3 cell = item.Position;
            Map map = item.Map;
            item.DeSpawn();

            if (innerContainer.TryAdd(item, canMergeWithExistingStacks: false))
                return true;

            // Put it back rather than leaving it despawned and orphaned.
            GenPlace.TryPlaceThing(item, cell, map, ThingPlaceMode.Direct);
            return false;
        }

        private void Reject(Thing thing, string why)
        {
            if (!TryTakeIn(thing))
            {
                idleReason = "DynamicMending.MenderCannotTakeItem".Translate(thing.LabelShortCap);
                return;
            }

            lastRejected = "DynamicMending.LastRejected".Translate(thing.LabelShortCap, why);
            rejectQueue.Add(thing);
            idleReason = null;
            FlushRejects();
        }

        private void StartRecycle(Thing thing)
        {
            if (!TryTakeIn(thing))
            {
                idleReason = "DynamicMending.MenderCannotTakeItem".Translate(thing.LabelShortCap);
                return;
            }

            currentItem = thing;
            recycling = true;
            idleReason = null;
            jobSkill = EffectiveSkill;
            workTicksTotal = Mathf.Max(60, Mathf.RoundToInt(MendingUtility.RecycleWorkAmount(thing) * PreceptWorkFactor / (overclock * CoreSpeedFactor)));
            workTicksRemaining = workTicksTotal;
            state = MenderState.Working;
        }

        // Puts whatever is waiting onto a free reject chute. True once nothing is left waiting.
        //
        // Direct placement only, like the output port, so a belt laid on a chute collects from
        // it. Each chute takes one stack (more with storage or a belt on it), so a recycle that
        // breaks into several materials can take a moment to get out.
        private bool FlushRejects()
        {
            rejectQueue.RemoveAll(t => t == null || t.Destroyed || !innerContainer.Contains(t));
            if (rejectQueue.Count == 0)
            {
                return true;
            }

            List<IntVec3> chutes = RejectCells;
            foreach (Thing thing in rejectQueue.ToList())
            {
                foreach (IntVec3 cell in chutes)
                {
                    if (cell.InBounds(Map) && innerContainer.TryDrop(thing, cell, Map, ThingPlaceMode.Direct, out _))
                    {
                        rejectQueue.Remove(thing);
                        break;
                    }
                }
            }

            if (rejectQueue.Count == 0)
            {
                return true;
            }

            // Like Vanilla Furniture Expanded - Factory's machines, it simply waits for a chute
            // to clear, however long that takes, and takes nothing new in meanwhile. Nothing held
            // is ever lost: uninstalling or deconstructing the machine puts it all on the floor.
            return false;
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
            if (recycling)
            {
                FinishRecycle();
                return;
            }

            ConsumeGathered();
            currentBill?.NoteDone();
            currentBill = null;
            GetComp<CompMenderWasteBuffer>()?.Notify_ItemMended(currentItem, jobSkill);
            MendResult result = MendingUtility.ResolveRepair(currentItem, jobSkill, ignoreResearch: true);
            MendingUtility.ShowRepairResult(result, this);

            state = MenderState.Ejecting;
            TryEject();
        }

        // What it breaks down into goes out the reject chutes, not the output port: the orange
        // port is for repaired gear, and a belt feeding a stockpile of it should never find a
        // stack of steel on it.
        private void FinishRecycle()
        {
            currentBill?.NoteDone();
            currentBill = null;
            recycling = false;
            Thing item = currentItem;
            currentItem = null;
            state = MenderState.Idle;

            if (item == null || item.Destroyed)
                return;

            string label = item.LabelShortCap;
            List<Thing> products = MendingUtility.MakeRecycleProducts(item, jobSkill);
            foreach (Thing product in products)
            {
                if (innerContainer.TryAdd(product))
                    rejectQueue.Add(product);
            }

            MoteMaker.ThrowText(DrawPos, Map, products.Count == 0
                ? "DynamicMending.MoteRecycledNothing".Translate(label).ToString()
                : "DynamicMending.MoteRecycled".Translate(label).ToString());

            FlushRejects();
        }

        // Direct placement, so the item lands exactly on the output spot (a conveyor laid there
        // will collect it on its next tick). Only ever onto the spot itself, never scattered: a designated spot
        // that sometimes puts the item two tiles away is worse than one that visibly waits. If
        // the spot is blocked the item stays inside and this retries, so a conveyor or stockpile
        // clearing the spot gets the item on the next scan.
        private void TryEject()
        {
            if (currentItem == null || currentItem.Destroyed)
            {
                currentItem = null;
                idleReason = null;
                state = MenderState.Idle;
                return;
            }

            IntVec3 cell = OutputCell;

            if (cell.InBounds(Map)
                && innerContainer.Contains(currentItem)
                && innerContainer.TryDrop(currentItem, cell, Map, ThingPlaceMode.Direct, out _))
            {
                currentItem = null;
                idleReason = null;
                state = MenderState.Idle;
                return;
            }

            // Wait for the spot, however long that takes, as VFE Factory's machines do. Nothing
            // held is lost: uninstalling or deconstructing the machine puts it on the floor.
            idleReason = "DynamicMending.MenderOutputBlocked".Translate(currentItem.LabelShortCap);
        }

        private float WorkProgress => workTicksTotal > 0
            ? Mathf.Clamp01(1f - (float)workTicksRemaining / workTicksTotal)
            : 0f;

        // Live repair progress drawn across the machine, so you can read how far along a job is
        // without selecting the building and reading the inspect pane.
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);

            if (state != MenderState.Working)
                return;

            GenDraw.DrawFillableBar(new GenDraw.FillableBarRequest
            {
                center = drawLoc + Vector3.up * 0.1f,
                size = new Vector2(2.6f, 0.34f),
                fillPercent = WorkProgress,
                filledMat = BarFilledMat,
                unfilledMat = BarUnfilledMat,
                margin = 0.12f,
                rotation = Rot4.North,
            });
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            if (Map == null)
                return;

            // Same rings the place worker draws, so a built machine and one on the cursor
            // mark their spots identically.
            MenderSpots.DrawRings(Footprint, Rotation);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            if (state != MenderState.Idle || innerContainer.Count > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DynamicMending.CancelJobLabel".Translate(),
                    defaultDesc = "DynamicMending.CancelJobDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel"),
                    action = CancelJob,
                };
            }

            if (Faction == Faction.OfPlayer && Enumerable.Range(coreTier + 1, MenderCores.MaxTier - coreTier).Any(i => MenderCores.DefFor(i) != null))
            {
                yield return new Command_Action
                {
                    defaultLabel = CoreUpgradePending
                        ? "DynamicMending.CoreUpgradePendingLabel".Translate(MenderCores.LabelFor(wantedCoreTier))
                        : "DynamicMending.CoreUpgradeLabel".Translate(),
                    defaultDesc = "DynamicMending.CoreUpgradeDesc".Translate(MenderCores.LabelFor(coreTier)),
                    icon = MenderCores.DefFor(CoreUpgradePending ? wantedCoreTier : coreTier + 1)?.uiIcon ?? TexCommand.Install,
                    action = OpenCoreMenu,
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "DynamicMending.OverclockLabel".Translate(overclock.ToStringPercent()),
                defaultDesc = "DynamicMending.OverclockDesc".Translate(),
                icon = OverclockIcon,
                action = () => Find.WindowStack.Add(new Window_MenderOverclock(this)),
            };

        }

        private void OpenCoreMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = coreTier + 1; i <= MenderCores.MaxTier; i++)
            {
                ThingDef def = MenderCores.DefFor(i);
                if (def == null)
                    continue;
                int tier = i;
                options.Add(new FloatMenuOption("DynamicMending.CoreOption".Translate(def.LabelCap,
                    tier * MenderCores.SkillPerTier, (tier * MenderCores.SpeedPerTier).ToStringPercent(), (tier * MenderCores.SavingPerTier).ToStringPercent()),
                    () => wantedCoreTier = tier, def));
            }
            if (CoreUpgradePending)
                options.Add(new FloatMenuOption("DynamicMending.CoreCancel".Translate(), () => wantedCoreTier = coreTier));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string CoreLine()
        {
            string line = coreTier == 0
                ? "DynamicMending.CoreBasic".Translate(MenderCores.LabelFor(0)).ToString()
                : "DynamicMending.CoreUpgraded".Translate(MenderCores.LabelFor(coreTier), CoreSkillBonus,
                    (CoreSpeedFactor - 1f).ToStringPercent(), (1f - CoreCostFactor).ToStringPercent()).ToString();
            if (CoreUpgradePending)
                line += "\n" + "DynamicMending.CoreWaiting".Translate(MenderCores.LabelFor(wantedCoreTier));
            return line;
        }

        // ------------------------------------------------------------------ status tab
        //
        // Everything ITab_MenderStatus shows, worked out here so the tab and the machine cannot
        // disagree. Cached for half a second of game time (and redone when the overclock moves):
        // pricing the item on the spot runs the same cost calculation a job does, which is too
        // much to repeat every frame the tab is open.
        private List<string> statusCache;
        private int statusCacheTick = -1;
        private float statusCacheOverclock = -1f;

        public List<string> StatusLines()
        {
            int now = Find.TickManager.TicksGame;
            if (statusCache != null && now - statusCacheTick < 30 && Mathf.Approximately(statusCacheOverclock, overclock))
                return statusCache;

            List<string> lines = new List<string>();

            // What it is doing.
            lines.Add("DynamicMending.TabHeaderNow".Translate());
            if (!PowerOn)
            {
                lines.Add("DynamicMending.MenderNoPower".Translate());
            }
            else if (state == MenderState.Working && currentItem != null)
            {
                lines.Add((recycling ? "DynamicMending.MenderRecycling" : "DynamicMending.MenderRepairing")
                    .Translate(currentItem.LabelCap, WorkProgress.ToStringPercent()));
                lines.Add("DynamicMending.TabTimeLeft".Translate(workTicksRemaining.ToStringTicksToPeriod()));
                lines.Add("DynamicMending.TabJobSkill".Translate(jobSkill));
            }
            else
            {
                lines.Add(idleReason ?? "DynamicMending.MenderIdle".Translate().ToString());
            }

            // How hard it is being driven, and what that costs.
            float load = LoadFactorAt(overclock);
            lines.Add("");
            lines.Add("DynamicMending.TabHeaderOverclock".Translate(overclock.ToStringPercent()));
            lines.Add("DynamicMending.OverclockSkill".Translate(EffectiveSkill));
            lines.Add("DynamicMending.OverclockPower".Translate(Mathf.RoundToInt(BasePowerConsumption * load)));
            lines.Add("DynamicMending.OverclockHeat".Translate((BaseHeatPerSecond * load * HeatFactor).ToString("0.#")));
            lines.Add("DynamicMending.TabBreakdowns".Translate(BreakdownFactor.ToStringPercent()));
            float precept = PreceptWorkFactor;
            if (!Mathf.Approximately(precept, 1f))
                lines.Add("DynamicMending.TabPrecept".Translate(precept.ToStringPercent()));

            // What is waiting on the item spot, and what will happen to it.
            lines.Add("");
            lines.Add("DynamicMending.TabHeaderNext".Translate());
            Thing next = Spawned ? ThingsOn(ItemInputCell).FirstOrDefault(t => IsGear(t) && !IsOffLimits(t)) : null;
            if (next == null)
            {
                lines.Add("DynamicMending.TabNothingWaiting".Translate());
            }
            else
            {
                switch (Decide(next, out string why))
                {
                    case Intake.Reject:
                        lines.Add("DynamicMending.TabNextReject".Translate(next.LabelCap, why));
                        break;
                    case Intake.Recycle:
                        lines.Add("DynamicMending.TabNextRecycle".Translate(next.LabelCap));
                        break;
                    default:
                        lines.Add("DynamicMending.TabNextRepair".Translate(next.LabelCap));
                        Dictionary<ThingDef, int> stock = new Dictionary<ThingDef, int>();
                        foreach (Thing t in ThingsOn(ResourceInputCells))
                        {
                            stock.TryGetValue(t.def, out int had);
                            stock[t.def] = had + t.stackCount;
                        }

                        foreach (ThingDefCountClass cost in CostsFor(next, EffectiveSkill))
                        {
                            stock.TryGetValue(cost.thingDef, out int have);
                            lines.Add("DynamicMending.TabCostLine".Translate(cost.count, cost.thingDef.label, have));
                        }
                        break;
                }
            }

            // Anything held for the chutes, and the last thing turned away.
            if (rejectQueue.Count > 0)
            {
                lines.Add("");
                lines.Add("DynamicMending.TabHeaderHeld".Translate(rejectQueue.Count));
                foreach (Thing held in rejectQueue)
                    lines.Add("  " + held.LabelCap);
            }

            if (!lastRejected.NullOrEmpty())
            {
                lines.Add("");
                lines.Add(lastRejected);
            }

            statusCache = lines;
            statusCacheTick = now;
            statusCacheOverclock = overclock;
            return lines;
        }

        public override string GetInspectString()
        {
            StringBuilder sb = new StringBuilder(base.GetInspectString());

            if (sb.Length > 0)
                sb.AppendLine();

            if (!PowerOn)
            {
                sb.Append("DynamicMending.MenderNoPower".Translate());
            }
            else if (state == MenderState.Working && currentItem != null)
            {
                float progress = workTicksTotal > 0
                    ? 1f - (float)workTicksRemaining / workTicksTotal
                    : 0f;
                sb.Append((recycling ? "DynamicMending.MenderRecycling" : "DynamicMending.MenderRepairing")
                    .Translate(currentItem.LabelCap, progress.ToStringPercent()));
            }
            else
            {
                sb.Append(idleReason ?? "DynamicMending.MenderIdle".Translate().ToString());
            }

            if (!lastRejected.NullOrEmpty())
                sb.AppendLine().Append(lastRejected);

            sb.AppendLine();
            sb.Append("DynamicMending.OverclockInspect".Translate(overclock.ToStringPercent(), EffectiveSkill));
            sb.AppendLine();
            sb.Append(CoreLine());

            sb.AppendLine();
            sb.Append("DynamicMending.BillsInspect".Translate(billStack.Count));

            sb.AppendLine();
            sb.Append("DynamicMending.MenderSpotLegend".Translate());

            return sb.ToString();
        }
    }
}
