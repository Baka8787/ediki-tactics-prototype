using System.Collections.Generic;

namespace Ediki.Core
{
    /// <summary>Result of one flood fill: reachable cells, their AP cost, and paths.</summary>
    public sealed class ReachabilityMap
    {
        private static readonly Coord[] NoSteps = new Coord[0];

        private readonly Dictionary<Coord, int> _cost = new Dictionary<Coord, int>();
        private readonly Dictionary<Coord, Coord[]> _path = new Dictionary<Coord, Coord[]>();
        private readonly List<Coord> _ordered = new List<Coord>();

        public readonly Coord Origin;

        internal ReachabilityMap(Coord origin)
        {
            Origin = origin;
            _cost[origin] = 0;
            _path[origin] = NoSteps;
        }

        internal void Set(Coord c, int cost, Coord[] path)
        {
            _cost[c] = cost;
            _path[c] = path;
        }

        internal void Seal()
        {
            _ordered.Clear();
            foreach (KeyValuePair<Coord, int> kv in _cost) _ordered.Add(kv.Key);
            // Sorted so callers never depend on Dictionary enumeration order (determinism rule 2).
            _ordered.Sort();
        }

        /// <summary>Deterministically ordered (row-major). Includes the origin.</summary>
        public IReadOnlyList<Coord> ReachableCells => _ordered;

        public bool CanReach(Coord c) => _cost.ContainsKey(c);

        /// <summary>
        /// AP to reach this cell, rounded up — the number the player is charged
        /// and the number the HUD shows. Internally costs are kept exact; only
        /// this accessor rounds.
        /// </summary>
        public int CostTo(Coord c)
        {
            int v;
            return _cost.TryGetValue(c, out v) ? TerrainDef.CeilToAp(v) : -1;
        }

        /// <summary>Exact cost in hundredths, for comparing two routes that round to the same AP.</summary>
        public int CostHundredthsTo(Coord c)
        {
            int v;
            return _cost.TryGetValue(c, out v) ? v : -1;
        }

        /// <summary>Path from origin to target, EXCLUDING the origin. Null if unreachable.</summary>
        public Coord[] PathTo(Coord target)
        {
            Coord[] p;
            return _path.TryGetValue(target, out p) ? p : null;
        }
    }

    /// <summary>
    /// Dijkstra flood fill over terrain cost.
    ///
    /// This is the ONLY reachability implementation. Movement range, threat range
    /// and AI pathing all go through it (R-THR-02) — two implementations would
    /// drift and the danger zone would stop matching what can actually reach you.
    ///
    /// SIMPLIFICATION (2026-08-13): SPEC v0.1 §6.5 asked for a separate A* for AI
    /// point-to-point pathing. On a 120-cell grid the flood fill already produces
    /// every path in microseconds, so A* is not implemented. Recorded rather than
    /// silently dropped — see docs/03-spec/SPEC-movement.md R-MOVE-09.
    /// </summary>
    public static class MovementCalculator
    {
        /// <summary>Extra hundredths-of-AP a slowed unit pays per cell (【遲滯】: +1 AP).</summary>
        public const int SlowSurchargeHundredths = 100;

        /// <summary>
        /// What entering this cell costs THIS unit, terrain plus any status.
        ///
        /// The surcharge is per CELL, not per move action, so slowing something
        /// costs it more the further it wants to come — which is the point. A flat
        /// per-turn tax would be worth the same whether the target was next door
        /// or across the map.
        /// </summary>
        public static int StepCostHundredths(BattleState state, UnitState unit, Coord cell)
        {
            int cost = state.Map.MovementCostHundredths(cell);
            if (state.IsSlowed(unit)) cost += SlowSurchargeHundredths;
            return cost;
        }

        /// <summary>
        /// Cells the unit can reach with the given AP budget.
        /// Blocking terrain (OD-02) and living units (OD-03) are impassable.
        /// <paramref name="maxSteps"/> optionally caps the number of cells entered
        /// (reserved for a future MOVE limit; pass -1 for no cap — current baseline).
        /// </summary>
        public static ReachabilityMap Compute(BattleState state, Coord origin, int apBudget, int maxSteps = -1)
            => Compute(state, null, origin, apBudget, maxSteps);

        /// <summary>
        /// Same, but charging <paramref name="mover"/>'s status surcharges. Callers
        /// that have the unit should use this (or ComputeFor); the overload above
        /// exists for map-only queries where no unit is moving.
        /// </summary>
        public static ReachabilityMap Compute(BattleState state, UnitState mover, Coord origin,
                                              int apBudget, int maxSteps = -1)
        {
            ReachabilityMap result = new ReachabilityMap(origin);
            int stepCap = maxSteps < 0 ? int.MaxValue : maxSteps;
            if (stepCap == 0 || apBudget < 0) { result.Seal(); return result; }

            // Search over (cell, stepsUsed), not just cell.
            //
            // The cheapest route to a cell is not always the one using the fewest
            // cells — going around a forest can cost less AP but more MOVE. With a
            // step cap, tracking cost alone would report a cell unreachable while
            // ValidatePath happily accepts a shorter, pricier path to it. Keeping
            // the two in agreement matters: the highlighted move range must be
            // exactly what the simulator will accept.
            //
            // Grid is small (a few hundred cells, step cap of ~4), so a linear-scan
            // Dijkstra over the expanded state space is plenty fast and trivially
            // deterministic. Ties break on cost, then coord order, then steps, so
            // expansion never depends on container internals (determinism rule 2).
            List<Node> frontier = new List<Node> { new Node(origin, 0) };
            Dictionary<Node, int> best = new Dictionary<Node, int> { { new Node(origin, 0), 0 } };
            Dictionary<Node, Node> prev = new Dictionary<Node, Node>();
            HashSet<Node> settled = new HashSet<Node>();

            while (frontier.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < frontier.Count; i++)
                    if (Better(frontier[i], frontier[pick], best)) pick = i;

                Node cur = frontier[pick];
                frontier.RemoveAt(pick);
                if (!settled.Add(cur)) continue;

                int curCost = best[cur];
                if (cur.Steps >= stepCap) continue;

                foreach (Coord n in state.Map.Topology.Neighbors(cur.Cell))
                {
                    if (!state.CanUnitEnter(n)) continue;   // terrain blocking + unit occupancy

                    // Lethal cells are passable — that is what lets a Push put
                    // somebody there — but nothing walks into one on its own.
                    // Excluded here rather than in the simulator because this is
                    // what LegalCommands enumerates from: leave suicide moves in
                    // the legal set and the batch runner's uniform noise sampler
                    // takes them, killing units at random on every hazard map and
                    // corrupting the measurements taken there.
                    if (state.Map.IsLethal(n)) continue;

                    // Accumulate exactly, charge rounded up: the AP a path costs
                    // is ceil(sum), so two 1.5 cells cost 3 rather than 4.
                    int next = curCost + StepCostHundredths(state, mover, n);
                    if (TerrainDef.CeilToAp(next) > apBudget) continue;

                    Node node = new Node(n, cur.Steps + 1);
                    if (settled.Contains(node)) continue;

                    int known;
                    if (best.TryGetValue(node, out known) && known <= next) continue;

                    best[node] = next;
                    prev[node] = cur;
                    frontier.Add(node);
                }
            }

            // Collapse (cell, steps) back to cell: cheapest route within the cap.
            Dictionary<Coord, Node> bestNodePerCell = new Dictionary<Coord, Node>();
            foreach (KeyValuePair<Node, int> kv in best)
            {
                Node node = kv.Key;
                if (node.Steps > stepCap || node.Cell == origin) continue;

                Node incumbent;
                if (bestNodePerCell.TryGetValue(node.Cell, out incumbent))
                {
                    int a = kv.Value, b = best[incumbent];
                    // Cheapest wins; then fewest steps; then lower step index for stability.
                    if (a > b || (a == b && node.Steps >= incumbent.Steps)) continue;
                }
                bestNodePerCell[node.Cell] = node;
            }

            foreach (KeyValuePair<Coord, Node> kv in bestNodePerCell)
                result.Set(kv.Key, best[kv.Value], BuildPath(kv.Value, prev, origin));

            result.Seal();
            return result;
        }

        private readonly struct Node : System.IEquatable<Node>
        {
            public readonly Coord Cell;
            public readonly int Steps;

            public Node(Coord cell, int steps) { Cell = cell; Steps = steps; }

            public bool Equals(Node other) => Cell == other.Cell && Steps == other.Steps;
            public override bool Equals(object obj) => obj is Node n && Equals(n);
            public override int GetHashCode() => (Cell.GetHashCode() * 397) ^ Steps;
        }

        private static bool Better(Node candidate, Node incumbent, Dictionary<Node, int> cost)
        {
            int a = cost[candidate], b = cost[incumbent];
            if (a != b) return a < b;

            int byCell = candidate.Cell.CompareTo(incumbent.Cell);
            if (byCell != 0) return byCell < 0;

            return candidate.Steps < incumbent.Steps;
        }

        private static Coord[] BuildPath(Node target, Dictionary<Node, Node> prev, Coord origin)
        {
            List<Coord> reversed = new List<Coord>();
            Node cur = target;
            while (!(cur.Cell == origin && cur.Steps == 0))
            {
                reversed.Add(cur.Cell);
                Node p;
                if (!prev.TryGetValue(cur, out p)) break;
                cur = p;
            }
            reversed.Reverse();
            return reversed.ToArray();
        }

        /// <summary>
        /// Where this unit can still get to: limited by remaining AP, terrain cost,
        /// unit occupancy, and the MOVE cells it has left THIS TURN.
        ///
        /// MOVE is a per-turn budget. Splitting a journey into several move actions
        /// must not buy extra distance, or the cap means nothing.
        /// </summary>
        public static ReachabilityMap ComputeFor(BattleState state, UnitState unit)
        {
            int stepsLeft = unit.Def.Move - unit.MoveUsedThisTurn;
            if (stepsLeft < 0) stepsLeft = 0;
            return Compute(state, unit, unit.Position, unit.Ap, stepsLeft);
        }

        /// <summary>
        /// Terrain-only distance from an origin to every reachable cell: no AP
        /// budget, no step cap, and units ignored because units move.
        ///
        /// This is "how far away is that really", which is what an AI choosing
        /// where to walk needs. Manhattan distance cannot see walls, so on a map
        /// with a chokepoint both sides will happily park against opposite faces
        /// of the same wall and never engage.
        /// </summary>
        public static Dictionary<Coord, int> TerrainDistanceField(BattleMap map, Coord origin)
        {
            Dictionary<Coord, int> best = new Dictionary<Coord, int>();
            if (!map.IsPassable(origin)) return best;

            best[origin] = 0;
            List<Coord> frontier = new List<Coord> { origin };
            HashSet<Coord> settled = new HashSet<Coord>();

            while (frontier.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < frontier.Count; i++)
                {
                    int ci = best[frontier[i]], cp = best[frontier[pick]];
                    if (ci < cp || (ci == cp && frontier[i].CompareTo(frontier[pick]) < 0)) pick = i;
                }

                Coord cur = frontier[pick];
                frontier.RemoveAt(pick);
                if (!settled.Add(cur)) continue;

                int curCost = best[cur];
                foreach (Coord n in map.Topology.Neighbors(cur))
                {
                    if (!map.IsPassable(n) || settled.Contains(n)) continue;

                    int next = curCost + map.MovementCostHundredths(n);
                    int known;
                    if (best.TryGetValue(n, out known) && known <= next) continue;

                    best[n] = next;
                    frontier.Add(n);
                }
            }

            return best;
        }

        /// <summary>
        /// Distance from the field's origin in AP, rounded up, or a large value
        /// when unreachable.
        ///
        /// The field itself stores exact hundredths so fractional terrain
        /// accumulates properly, but every caller weighs this against other AP
        /// quantities. Returning hundredths here would multiply their distance
        /// term by a hundred and silently rewrite the balance of every strategy
        /// and AI weight in the project.
        /// </summary>
        public static int DistanceIn(Dictionary<Coord, int> field, Coord cell)
        {
            int d;
            return field.TryGetValue(cell, out d) ? TerrainDef.CeilToAp(d) : 999999;
        }

        /// <summary>Exact distance in hundredths, for comparing routes that round alike.</summary>
        public static int DistanceHundredthsIn(Dictionary<Coord, int> field, Coord cell)
        {
            int d;
            return field.TryGetValue(cell, out d) ? d : 99999999;
        }

        /// <summary>
        /// Validates a path against the current state and returns its total AP cost.
        /// Returns -1 and a reason when illegal. Never trusts the caller (R-MOVE-06).
        /// </summary>
        public static int ValidatePath(BattleState state, UnitState unit, Coord[] path, out string reason)
        {
            reason = null;

            if (path == null || path.Length == 0)
            {
                reason = "Path is empty.";
                return -1;
            }

            Coord cur = unit.Position;
            int total = 0;

            for (int i = 0; i < path.Length; i++)
            {
                Coord step = path[i];

                if (!state.Map.Contains(step))
                {
                    reason = "Step " + i + " " + step + " is outside the map.";
                    return -1;
                }

                if (state.Map.Topology.Distance(cur, step) != 1)
                {
                    reason = "Step " + i + " " + step + " is not adjacent to " + cur + ".";
                    return -1;
                }

                if (!state.Map.IsPassable(step))
                {
                    reason = "Step " + i + " " + step + " is blocking terrain.";
                    return -1;
                }

                UnitState occupant = state.UnitAt(step);
                if (occupant != null && occupant.Id != unit.Id)
                {
                    reason = "Step " + i + " " + step + " is occupied by unit " + occupant.Id + ".";
                    return -1;
                }

                total += StepCostHundredths(state, unit, step);
                cur = step;
            }

            // One rounding, at the end of the whole path.
            total = TerrainDef.CeilToAp(total);

            if (total > unit.Ap)
            {
                reason = "Path costs " + total + " AP but unit has " + unit.Ap + ".";
                return -1;
            }

            // MOVE is a per-TURN budget. Chaining move actions must not buy extra
            // distance, otherwise the cap is decorative.
            int totalCells = unit.MoveUsedThisTurn + path.Length;
            if (totalCells > unit.Def.Move)
            {
                reason = "Path is " + path.Length + " cells and " + unit.MoveUsedThisTurn +
                         " were already moved this turn; MOVE is " + unit.Def.Move + ".";
                return -1;
            }

            return total;
        }
    }
}
