using System.Collections.Generic;

namespace Ediki.Core
{
    /// <summary>
    /// Read-only spatial queries the UI and the AI both need.
    ///
    /// These are pure functions over a BattleState (R-THR-11). The presentation
    /// layer calls them and receives values — it never holds a mutable BattleState
    /// and never recomputes these itself (R-THR-12 / A7).
    /// </summary>
    public static class BattleQueries
    {
        /// <summary>
        /// Static Exposure (R-GRID-05): how many neighbouring cells an enemy could
        /// physically stand on, judged by terrain geometry alone.
        /// A property of the map, independent of where units currently are.
        /// </summary>
        public static int StaticExposure(BattleMap map, Coord c)
        {
            int n = 0;
            foreach (Coord adj in map.Topology.Neighbors(c))
                if (map.IsPassable(adj)) n++;
            return n;
        }

        /// <summary>
        /// Cells a unit threatens this turn: everywhere it could move to on a full
        /// AP bar minus one attack, expanded by its attack range (R-THR-01).
        /// Uses the same flood fill as real movement (R-THR-02).
        /// </summary>
        public static HashSet<Coord> ThreatRange(BattleState state, UnitState unit)
        {
            HashSet<Coord> threatened = new HashSet<Coord>();
            if (!unit.IsAlive) return threatened;

            int moveBudget = unit.Def.MaxAp - unit.Def.AttackApCost;
            if (moveBudget < 0) moveBudget = 0;

            // One move action then one attack — the conventional danger-zone
            // reading, and what R-THR-01 describes.
            //
            // KNOWN LIMITATION: our AP system lets a unit chain several move
            // actions in a turn, so a determined unit can end up further than
            // this. The danger zone therefore UNDER-reports for chaining units.
            // Recorded as a prototype finding, not silently diverged from:
            // see docs/OPEN-DECISIONS.md#od-17.
            ReachabilityMap reach = MovementCalculator.Compute(
                state, unit.Position, moveBudget, unit.Def.Move);

            for (int i = 0; i < reach.ReachableCells.Count; i++)
            {
                Coord from = reach.ReachableCells[i];
                CollectCellsInRange(state.Map, from, unit.Def.AttackRange, threatened);
            }

            return threatened;
        }

        /// <summary>
        /// Cells this unit can actually attack from its current state, including
        /// moving first while preserving the AP required for one attack.
        /// Unlike ThreatRange this uses remaining AP and remaining MOVE, so the
        /// overlay never promises reach that has already been spent this phase.
        /// </summary>
        public static HashSet<Coord> CurrentThreatRange(BattleState state, UnitState unit)
        {
            HashSet<Coord> threatened = new HashSet<Coord>();
            if (unit == null || !unit.IsAlive || unit.HasEndedTurn || !state.CanAttackAgain(unit)) return threatened;

            int moveBudget = unit.Ap - unit.Def.AttackApCost;
            if (moveBudget < 0) return threatened;
            int steps = unit.Def.Move - unit.MoveUsedThisTurn;
            if (steps < 0) steps = 0;

            ReachabilityMap reach = MovementCalculator.Compute(state, unit, unit.Position, moveBudget, steps);
            CollectCellsInRange(state.Map, unit.Position, unit.Def.AttackRange, threatened);
            for (int i = 0; i < reach.ReachableCells.Count; i++)
                CollectCellsInRange(state.Map, reach.ReachableCells[i], unit.Def.AttackRange, threatened);
            return threatened;
        }

        /// <summary>
        /// Cells this unit can STAND on this turn — movement only, no attack
        /// expansion.
        ///
        /// Separate from ThreatRange on purpose. "Where can it get to" and "where
        /// can it hit" are different questions and the answers differ by the
        /// attack range, which is exactly the thing a player needs to see: a
        /// range-2 archer threatens a ring two cells wider than it can walk, and a
        /// single merged overlay hides that completely.
        ///
        /// <paramref name="fullBar"/> asks what it could do on a fresh turn rather
        /// than with the AP it has left. That is the right question for an ENEMY
        /// (it will have a full bar when its phase comes round) and the wrong one
        /// for the unit you are currently moving.
        /// </summary>
        public static HashSet<Coord> MoveRange(BattleState state, UnitState unit, bool fullBar)
        {
            HashSet<Coord> cells = new HashSet<Coord>();
            if (!unit.IsAlive) return cells;

            int ap = fullBar ? unit.Def.MaxAp : unit.Ap;
            int steps = fullBar ? unit.Def.Move : unit.Def.Move - unit.MoveUsedThisTurn;
            if (steps < 0) steps = 0;

            ReachabilityMap reach = MovementCalculator.Compute(state, unit, unit.Position, ap, steps);
            for (int i = 0; i < reach.ReachableCells.Count; i++) cells.Add(reach.ReachableCells[i]);
            return cells;
        }

        /// <summary>Cells this unit can hit WITHOUT moving.</summary>
        public static HashSet<Coord> StrikeRange(BattleState state, UnitState unit)
        {
            HashSet<Coord> cells = new HashSet<Coord>();
            if (!unit.IsAlive) return cells;
            CollectCellsInRange(state.Map, unit.Position, unit.Def.AttackRange, cells);
            return cells;
        }

        /// <summary>Union of every living enemy's MOVE range (not their threat).</summary>
        public static HashSet<Coord> EnemyMoveZone(BattleState state, Faction forFaction)
        {
            HashSet<Coord> zone = new HashSet<Coord>();
            foreach (UnitState enemy in state.LivingUnitsOf(forFaction.Opponent()))
                zone.UnionWith(MoveRange(state, enemy, fullBar: true));
            return zone;
        }

        /// <summary>
        /// Effective Exposure (R-GRID-08 / R-THR-10): how many LIVING enemies of
        /// <paramref name="forFaction"/> currently threaten this cell.
        /// This is the number the player actually cares about.
        /// </summary>
        public static int EffectiveExposure(BattleState state, Coord cell, Faction forFaction)
        {
            int count = 0;
            foreach (UnitState enemy in state.LivingUnitsOf(forFaction.Opponent()))
            {
                if (ThreatRange(state, enemy).Contains(cell)) count++;
            }
            return count;
        }

        /// <summary>Union of every living enemy's threat range — the "danger zone" (R-THR-05).</summary>
        public static HashSet<Coord> DangerZone(BattleState state, Faction forFaction)
        {
            HashSet<Coord> zone = new HashSet<Coord>();
            foreach (UnitState enemy in state.LivingUnitsOf(forFaction.Opponent()))
                zone.UnionWith(ThreatRange(state, enemy));
            return zone;
        }

        /// <summary>Targets the attacker may legally hit right now.</summary>
        public static List<UnitState> AttackableTargets(BattleState state, UnitState attacker)
        {
            List<UnitState> result = new List<UnitState>();
            if (!attacker.IsAlive) return result;

            foreach (UnitState other in state.LivingUnitsOf(attacker.Faction.Opponent()))
            {
                if (state.Map.Topology.Distance(attacker.Position, other.Position) <= attacker.Def.AttackRange)
                    result.Add(other);
            }
            // Units are id-ordered already; keep it that way for determinism.
            return result;
        }

        /// <summary>
        /// Passable cells within <paramref name="range"/> of a cell.
        ///
        /// The "what could I hit from THERE" question, asked about a cell nobody
        /// is standing on yet. Public because the board previews a move before it
        /// is made, and the presentation layer must not grow its own copy of the
        /// range shape (R-THR-12 / A7) — a preview that disagrees with the attack
        /// it is previewing is worse than showing nothing at all.
        /// </summary>
        public static HashSet<Coord> CellsInRange(BattleMap map, Coord center, int range)
        {
            HashSet<Coord> cells = new HashSet<Coord>();
            CollectCellsInRange(map, center, range, cells);
            return cells;
        }

        private static void CollectCellsInRange(BattleMap map, Coord center, int range, HashSet<Coord> into)
        {
            for (int dy = -range; dy <= range; dy++)
            {
                int span = range - (dy < 0 ? -dy : dy);
                for (int dx = -span; dx <= span; dx++)
                {
                    Coord c = new Coord(center.X + dx, center.Y + dy);

                    // Only cells something could actually stand on. A wall inside an
                    // attack radius is not a threatened cell, and painting it red in
                    // the danger zone would just be noise.
                    if (map.IsPassable(c)) into.Add(c);
                }
            }
        }
    }
}
