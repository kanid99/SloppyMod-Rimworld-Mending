using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace MendingMod
{
    // What tech level the colony is actually operating at, judged by the BALANCE of what it has
    // researched rather than by its single most advanced project. Finishing one spacer project
    // does not make a colony spacer; finishing half the spacer tree does.
    public static class ColonyTech
    {
        // Share of a tier's projects that must be done before the colony counts as that tier.
        private const float TierShareNeeded = 0.5f;

        // Ascending, so the last tier that clears the bar wins.
        private static readonly TechLevel[] Tiers =
        {
            TechLevel.Animal, TechLevel.Neolithic, TechLevel.Medieval,
            TechLevel.Industrial, TechLevel.Spacer, TechLevel.Ultra, TechLevel.Archotech,
        };

        private static TechLevel cached = TechLevel.Undefined;
        private static bool valid;

        public static TechLevel Level
        {
            get
            {
                if (!valid)
                    Recompute();

                return cached;
            }
        }

        public static void Invalidate()
        {
            valid = false;
        }

        public static TechLevel Recompute()
        {
            Dictionary<TechLevel, int> total = new Dictionary<TechLevel, int>();
            Dictionary<TechLevel, int> done = new Dictionary<TechLevel, int>();

            foreach (ResearchProjectDef project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (!CountsTowardsTier(project))
                    continue;

                total.TryGetValue(project.techLevel, out int seen);
                total[project.techLevel] = seen + 1;

                if (project.IsFinished)
                {
                    done.TryGetValue(project.techLevel, out int finished);
                    done[project.techLevel] = finished + 1;
                }
            }

            // The scenario's own level is the floor: a crashlanded colony is industrial from the
            // first tick, before it has researched anything at all.
            TechLevel level = Faction.OfPlayer?.def?.techLevel ?? TechLevel.Undefined;

            foreach (TechLevel tier in Tiers)
            {
                if (tier <= level)
                    continue;

                if (!total.TryGetValue(tier, out int count) || count == 0)
                    continue;

                done.TryGetValue(tier, out int finished);
                if ((float)finished / count >= TierShareNeeded)
                    level = tier;
            }

            cached = level;
            valid = true;
            return level;
        }

        // The denominator is "projects a player could work through in this game": anything with a
        // real tech level, minus Anomaly's knowledge projects, which are not researched at a bench
        // and would drag their tier's ratio down permanently, and anything hidden from the tree.
        private static bool CountsTowardsTier(ResearchProjectDef project)
        {
            return project.techLevel != TechLevel.Undefined
                && project.knowledgeCategory == null
                && !project.IsHidden;
        }

        public static string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"colony tech {Level} (floor {Faction.OfPlayer?.def?.techLevel}, "
                    + $"threshold {TierShareNeeded:P0})");

            var counts = DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(CountsTowardsTier)
                .GroupBy(p => p.techLevel)
                .OrderBy(g => g.Key);

            foreach (var group in counts)
            {
                int finished = group.Count(p => p.IsFinished);
                sb.Append($" | {group.Key} {finished}/{group.Count()} ({(float)finished / group.Count():P0})");
            }

            return sb.ToString();
        }
    }

    // RimWorld exposes no "a project finished" event, and ResearchProjectDef.researchMods is
    // private, so without a Harmony patch - which this mod deliberately does not need - the tally
    // is recomputed on load and then polled. The poll is one dictionary lookup per research def
    // once an in-game hour, against research that takes many hours, and the cached value is only
    // replaced when the tally actually moved.
    public class MendingGameComponent : GameComponent
    {
        private const int RecheckInterval = 2500;   // one in-game hour

        public MendingGameComponent(Game game)
        {
        }

        public override void FinalizeInit()
        {
            ColonyTech.Invalidate();
            ColonyTech.Recompute();

            if (Prefs.DevMode)
                Log.Message($"[DynamicMending] {ColonyTech.Describe()}");
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % RecheckInterval != 0)
                return;

            TechLevel before = ColonyTech.Level;
            ColonyTech.Invalidate();
            TechLevel after = ColonyTech.Recompute();

            if (after != before && Prefs.DevMode)
            {
                Log.Message($"[DynamicMending] colony tech level {before} -> {after}. "
                          + ColonyTech.Describe());
            }
        }
    }
}
