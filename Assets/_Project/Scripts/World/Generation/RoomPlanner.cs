using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Plans one room from its recipe:
    ///   for each entry (in order) and each instance:
    ///     candidates (strategy) -> hard rules -> score (soft rules + bias + noise) -> best first
    ///     -> tentatively place -> connectivity check -> keep or undo
    ///   a required piece that can't be placed fails the attempt; the room is retried with a new sub-seed.
    /// Pure C#: no scene access, deterministic for a given seed.
    /// </summary>
    public sealed class RoomPlanner
    {
        public const float Tolerance = 1.6f;  // candidates this close to the best score are all "good" (picked at random)
        public const int MaxTries = 30;       // candidates that reach the connectivity check per instance

        private readonly List<IPlacementRule> _rules;
        private readonly List<IPlacementScorer> _scorers;
        private readonly CounterRunStrategy _counter = new CounterRunStrategy();

        public RoomPlanner(List<IPlacementRule> rules = null, List<IPlacementScorer> scorers = null)
        {
            _rules = rules ?? PlacementRules.Default();
            _scorers = scorers ?? PlacementScorers.Default();
        }

        public RoomPlan Plan(RoomArea area, RoomRecipe recipe, IList<Obstacle> obstacles, Dictionary<string, int> houseCounts,
            int seed, int maxAttempts, HousePlan stats, GenLayer maxLayer, float playerRadius, out RoomGrid finalGrid)
        {
            var plan = new RoomPlan { room = area.room };
            finalGrid = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                plan.attempts = attempt + 1;
                var counts = new Dictionary<string, int>(houseCounts);
                var rng = new System.Random(SeedMix(seed, attempt, (int)area.room));
                var ctx = new PlanContext(area, obstacles, counts, rng, stats, playerRadius);

                if (TryRecipe(ctx, recipe, maxLayer, out string failure))
                {
                    plan.placements.AddRange(ctx.placed);
                    foreach (var kv in counts) houseCounts[kv.Key] = kv.Value;
                    finalGrid = ctx.grid;
                    return plan;
                }

                plan.failure = failure;
            }

            return plan; // empty: caller falls back
        }

        private bool TryRecipe(PlanContext ctx, RoomRecipe recipe, GenLayer maxLayer, out string failure)
        {
            foreach (RecipeEntry e in recipe.entries)
            {
                if (e == null || e.Layer > maxLayer) continue;

                int n = e.min;
                for (int k = e.min; k < e.max; k++)
                {
                    if (ctx.rng.NextDouble() < e.chance) n++;
                }

                // required pieces positioned from this one (sofa from the TV, mirror from the sink)
                List<RecipeEntry> dependents = null;
                foreach (RecipeEntry d in recipe.entries)
                {
                    if (d != null && d.min > 0 && d.placement == PlacementKind.Relative && d.anchorKey == e.key && d.Layer <= maxLayer)
                        (dependents ??= new List<RecipeEntry>()).Add(d);
                }

                for (int inst = 0; inst < n; inst++)
                {
                    bool ok = e.placement == PlacementKind.CounterRun ? PlaceComposite(ctx, e) : PlaceOne(ctx, e, inst == 0 ? dependents : null);
                    if (ok) continue;
                    if (inst < e.min)
                    {
                        failure = $"required '{e.key}' #{inst + 1} did not fit";
                        return false;
                    }

                    break; // optional extras stop at the first one that doesn't fit
                }
            }

            failure = null;
            return true;
        }

        private bool PlaceOne(PlanContext ctx, RecipeEntry e, List<RecipeEntry> dependents = null)
        {
            IPlacementStrategy strategy = PlacementStrategies.For(e.placement);
            if (strategy == null || e.options == null || e.options.Length == 0) return false;

            // try the options in random order (one def per instance)
            var order = new List<FurnitureDef>(e.options);
            Shuffle(order, ctx.rng);
            foreach (FurnitureDef def in order)
            {
                if (def == null) continue;
                var scored = new List<(Placement p, float s)>();
                foreach (Placement p in strategy.Candidates(ctx, e, def))
                {
                    p.key = e.key;
                    p.entry = e;
                    p.interactable = p.hasClearance || def.hasHidingSpots;
                    if (!PassesRules(ctx, p)) continue;
                    scored.Add((p, Score(ctx, p)));
                }

                int tries = 0;
                foreach (var (p, _) in Satisfice(scored, ctx.rng))
                {
                    if (tries++ >= MaxTries) break;
                    byte[] snap = ctx.grid.Snapshot();
                    Apply(ctx, p);
                    bool connected = !NeedsConnectivity(p) || Connectivity.Check(ctx, out _) || Reject(ctx, "H8 connectivity", false);
                    if (connected && (DependentsFeasible(ctx, dependents) || Reject(ctx, "lookahead", false)))
                    {
                        return true;
                    }

                    Undo(ctx, p, snap);
                }
            }

            return false;
        }

        /// One-step lookahead: an anchor is only kept if every required piece positioned from it still has at
        /// least one rule-abiding spot (a TV the sofa can't face is not a TV position).
        private bool DependentsFeasible(PlanContext ctx, List<RecipeEntry> dependents)
        {
            if (dependents == null) return true;
            var relative = PlacementStrategies.For(PlacementKind.Relative);
            foreach (RecipeEntry d in dependents)
            {
                bool any = false;
                foreach (FurnitureDef def in d.options)
                {
                    if (def == null) continue;
                    foreach (Placement c in relative.Candidates(ctx, d, def))
                    {
                        c.key = d.key;
                        c.entry = d;
                        if (PassesRulesQuiet(ctx, c)) { any = true; break; }
                    }

                    if (any) break;
                }

                if (!any) return false;
            }

            return true;
        }

        private bool PassesRulesQuiet(PlanContext ctx, Placement p)
        {
            foreach (IPlacementRule rule in _rules)
            {
                if (!rule.Allows(ctx, p)) return false;
            }

            return true;
        }

        /// "Good enough, then random": every candidate within Tolerance of the best score is equally likely
        /// (shuffled first), the clearly worse ones follow as a backup in score order. Preferences decide what
        /// is sensible; randomness decides among the sensible options - so layouts vary without getting silly.
        private static List<(T item, float s)> Satisfice<T>(List<(T item, float s)> scored, System.Random rng)
        {
            if (scored.Count == 0) return scored;
            float best = float.MinValue;
            foreach (var x in scored) best = Mathf.Max(best, x.s);
            var good = new List<(T, float)>();
            var rest = new List<(T, float)>();
            foreach (var x in scored) (x.s >= best - Tolerance ? good : rest).Add(x);
            Shuffle(good, rng);
            rest.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            good.AddRange(rest);
            return good;
        }

        private bool PlaceComposite(PlanContext ctx, RecipeEntry e)
        {
            var runs = new List<(List<Placement> run, float s)>();
            foreach (List<Placement> run in _counter.Runs(ctx, e))
            {
                runs.Add((run, run[0].bias));
            }

            foreach (var (run, _) in Satisfice(runs, ctx.rng))
            {
                byte[] snap = ctx.grid.Snapshot();
                int placedBefore = ctx.placed.Count;
                var countsBefore = new Dictionary<string, int>(ctx.houseCounts);
                bool ok = true;
                foreach (Placement p in run)
                {
                    if (p.anchor != null && !ctx.placed.Contains(p.anchor))
                    {
                        if (p.optional) continue;
                        ok = false;
                        break;
                    }

                    if (!PassesRules(ctx, p))
                    {
                        if (p.optional) continue;
                        ok = false;
                        break;
                    }

                    Apply(ctx, p);
                }

                if (ok && (Connectivity.Check(ctx, out _) || Reject(ctx, "H8 connectivity", false))) return true;

                ctx.grid.Restore(snap);
                ctx.placed.RemoveRange(placedBefore, ctx.placed.Count - placedBefore);
                ctx.houseCounts.Clear();
                foreach (var kv in countsBefore) ctx.houseCounts[kv.Key] = kv.Value;
            }

            return false;
        }

        private bool PassesRules(PlanContext ctx, Placement p)
        {
            foreach (IPlacementRule rule in _rules)
            {
                if (!rule.Allows(ctx, p)) return Reject(ctx, rule.Name, false);
            }

            return true;
        }

        private static bool Reject(PlanContext ctx, string rule, bool result)
        {
            ctx.stats?.Reject(rule);
            return result;
        }

        private float Score(PlanContext ctx, Placement p)
        {
            float s = p.bias;
            foreach (IPlacementScorer scorer in _scorers) s += scorer.Score(ctx, p);
            return s;
        }

        private static bool NeedsConnectivity(Placement p) => p.low;

        private static void Apply(PlanContext ctx, Placement p)
        {
            if (p.rug) ctx.grid.Mark(p.body, Cell.Rug);
            if (p.def.isHazard) ctx.grid.Mark(p.body, Cell.Reserved); // nothing gets dumped on a puddle / the legos
            if (p.loose) ctx.grid.Mark(p.body, Cell.Prop);
            if (p.low) ctx.grid.Mark(p.body, Cell.Low);
            if (p.high) ctx.grid.Mark(p.body, Cell.High);
            if (p.hasClearance) ctx.grid.Mark(p.clearance, Cell.Clear);
            if (p.sideStrips != null)
            {
                // reserve every long side that is free right now (at least one is, see SideAccessRule)
                foreach (Rect s in p.sideStrips)
                {
                    if (SideAccessRule.IsFree(ctx, s)) ctx.grid.Mark(s, Cell.Clear);
                }
            }

            ctx.placed.Add(p);
            ctx.houseCounts[p.def.id] = ctx.CountOf(p.def.id) + 1;
        }

        private static void Undo(PlanContext ctx, Placement p, byte[] snap)
        {
            ctx.grid.Restore(snap);
            ctx.placed.Remove(p);
            ctx.houseCounts[p.def.id] = ctx.CountOf(p.def.id) - 1;
        }

        /// System.Random gives correlated streams for nearby seeds; hash the inputs first (murmur3 finalizer).
        public static int SeedMix(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return (int)(h & 0x7FFFFFFF);
            }
        }

        private static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
