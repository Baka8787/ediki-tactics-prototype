using System.Collections.Generic;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// Shared moves for the measurement instruments.
    ///
    /// Instruments are NOT AI. Each one answers a single question — "if this unit
    /// deliberately spends its AP on THIS action, what does the encounter give
    /// back?" — so they need a way to reach the enemy and a way to swing, and
    /// nothing else. No exposure scoring, no target ranking, no guarding: every
    /// one of those would be a second variable inside a one-variable measurement.
    ///
    /// None of them is registered in the standard metrics batch.
    /// </summary>
    internal static class InstrumentCore
    {
        /// <summary>Hit whatever is in reach, in spawn order. Null when nothing is.</summary>
        public static ICommand Attack(BattleState state, UnitState unit)
        {
            if (unit.Ap < unit.Def.AttackApCost) return null;

            List<UnitState> targets = BattleQueries.AttackableTargets(state, unit);
            return targets.Count == 0 ? null : new AttackCommand(unit.Id, targets[0].Id);
        }

        /// <summary>
        /// Step toward the nearest enemy by PATH distance, so a wall is noticed.
        ///
        /// Same shape as the charge control's approach. It is duplicated rather
        /// than shared because AggressiveStrategy is a published control whose
        /// behaviour must not move, and reaching into it would couple the two.
        /// </summary>
        public static ICommand Approach(BattleState state, UnitState unit)
        {
            UnitState nearest = Nearest(state, unit);
            if (nearest == null) return null;

            ReachabilityMap reach = MovementCalculator.ComputeFor(state, unit);
            Dictionary<Coord, int> field =
                MovementCalculator.TerrainDistanceField(state.Map, nearest.Position);

            Coord best = unit.Position;
            int bestScore = int.MaxValue;

            for (int i = 0; i < reach.ReachableCells.Count; i++)
            {
                Coord cell = reach.ReachableCells[i];
                int score = MovementCalculator.DistanceIn(field, cell) * 10 + reach.CostTo(cell);
                if (score < bestScore) { bestScore = score; best = cell; }
            }

            if (best == unit.Position) return null;

            Coord[] path = reach.PathTo(best);
            return path == null || path.Length == 0 ? null : new MoveCommand(unit.Id, path);
        }

        public static UnitState Nearest(BattleState state, UnitState unit)
        {
            UnitState nearest = null;
            int best = int.MaxValue;

            // Enemies arrive in id order, so ties resolve identically every run.
            foreach (UnitState e in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int d = state.Map.Topology.Distance(unit.Position, e.Position);
                if (d < best) { best = d; nearest = e; }
            }
            return nearest;
        }

        /// <summary>Attack, else close, else stand down keeping the AP visible.</summary>
        public static ICommand AttackOrApproach(BattleState state, UnitState unit)
        {
            ICommand attack = Attack(state, unit);
            if (attack != null) return attack;

            ICommand approach = Approach(state, unit);
            if (approach != null) return approach;

            return new WaitCommand(unit.Id);
        }
    }

    /// <summary>
    /// The damage floor: swing whenever possible, otherwise walk at the enemy.
    ///
    /// It is the control every other instrument is read against — the value of
    /// spending AP on a skill only means anything next to what the same AP buys
    /// as a plain attack.
    /// </summary>
    public sealed class AttackOnlyStrategy : IPlayerStrategy
    {
        public string Name => "attack-only";

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng) =>
            InstrumentCore.AttackOrApproach(state, unit);
    }

    /// <summary>
    /// Spend on Push first, then behave like attack-only.
    ///
    /// Push moves the target one cell directly away, so it always increases the
    /// distance the enemy has to re-cross. That delay IS the hypothesis — this
    /// project has no lethal terrain and a blocked push is rejected outright, so
    /// there is no environmental kill to measure and none is claimed.
    ///
    /// Deliberately pushes whatever is adjacent rather than choosing cleverly:
    /// the question is what the ACTION is worth here, not what a good pusher
    /// would do with it.
    /// </summary>
    public sealed class PushInstrumentStrategy : IPlayerStrategy
    {
        public string Name => "push-instrument";

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            if (unit.Def.CanPush && unit.Ap >= unit.Def.PushApCost)
            {
                foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
                {
                    if (state.Map.Topology.Distance(unit.Position, enemy.Position) > unit.Def.PushRange) continue;
                    if (enemy.Def.ImmuneToPush) continue;
                    if (!LegalCommands.PushLands(state, unit, enemy)) continue;

                    return new PushCommand(unit.Id, enemy.Id);
                }
            }

            return InstrumentCore.AttackOrApproach(state, unit);
        }
    }

    /// <summary>
    /// Spend on Slow first, then behave like attack-only.
    ///
    /// Slow adds 1 AP to every cell the target enters for a round, so its whole
    /// claim is on WHEN the enemy arrives rather than on damage. Only unslowed
    /// targets are considered, because the simulator refuses a second stack and a
    /// refused command would show up as an action that bought nothing.
    /// </summary>
    public sealed class SlowInstrumentStrategy : IPlayerStrategy
    {
        public string Name => "slow-instrument";

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            if (unit.Def.CanSlow && unit.Ap >= unit.Def.SlowApCost)
            {
                foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
                {
                    if (state.Map.Topology.Distance(unit.Position, enemy.Position) > unit.Def.SlowRange) continue;
                    if (state.IsSlowed(enemy)) continue;

                    return new SlowCommand(unit.Id, enemy.Id);
                }
            }

            return InstrumentCore.AttackOrApproach(state, unit);
        }
    }

    /// <summary>
    /// Spend on Taunt first, then behave like attack-only.
    ///
    /// Taunt is the only thing in this rule set that overrides enemy target
    /// selection outright, so what it buys is a redistribution of damage — not a
    /// front line. There is no ZOC here and nothing physically blocks a path.
    /// </summary>
    public sealed class TauntInstrumentStrategy : IPlayerStrategy
    {
        public string Name => "taunt-instrument";

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            if (unit.Def.CanTaunt && unit.Ap >= unit.Def.TauntApCost && !state.IsTaunting(unit))
                return new TauntCommand(unit.Id);

            return InstrumentCore.AttackOrApproach(state, unit);
        }
    }
}
