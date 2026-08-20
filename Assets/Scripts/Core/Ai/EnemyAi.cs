using System.Collections.Generic;

namespace Ediki.Core.Ai
{
    /// <summary>
    /// Goal / utility-lite enemy AI (OD-10).
    ///
    /// Four responsibilities, in this order:
    ///   Perception       -> is this unit awake at all?
    ///   Target selection -> who does it want dead?
    ///   Position select  -> where does it want to stand?
    ///   Action select    -> what does it do with the AP it has left?
    ///
    /// It emits ICommands and goes through BattleSimulator like the player does.
    /// There is no second rule implementation here.
    ///
    /// Explicitly NOT a behaviour tree, and not extensible by subclassing per
    /// enemy type — behaviour differences come from AiProfile data.
    /// </summary>
    public sealed class EnemyAi
    {
        private readonly Dictionary<int, AiProfile> _profileByUnitId;
        private readonly AiProfile _fallback;

        // Reinforcements only get a unit id when they walk in, so their profile is
        // resolved on first sight and cached.
        private readonly IReadOnlyList<PendingReinforcement> _waves;
        private readonly AiProfileCatalog _catalog;

        public EnemyAi(Dictionary<int, AiProfile> profileByUnitId, AiProfile fallback = null,
                       IReadOnlyList<PendingReinforcement> waves = null, AiProfileCatalog catalog = null)
        {
            _profileByUnitId = profileByUnitId ?? new Dictionary<int, AiProfile>();
            _fallback = fallback ?? AiProfile.Default;
            _waves = waves;
            _catalog = catalog;
        }

        public AiProfile ProfileFor(int unitId)
        {
            AiProfile p;
            return _profileByUnitId.TryGetValue(unitId, out p) ? p : _fallback;
        }

        private AiProfile ProfileFor(BattleState state, UnitState unit)
        {
            AiProfile p;
            if (_profileByUnitId.TryGetValue(unit.Id, out p)) return p;

            if (_waves != null && _catalog != null)
            {
                for (int i = 0; i < _waves.Count; i++)
                {
                    PendingReinforcement w = _waves[i];
                    if (!w.Spawned || w.Faction != unit.Faction || w.Def.Id != unit.Def.Id) continue;
                    p = _catalog.GetOrDefault(w.AiProfileId);
                    _profileByUnitId[unit.Id] = p;
                    return p;
                }
            }

            return _fallback;
        }

        /// <summary>
        /// Next command for this unit, or null when it has nothing left to do.
        /// Called repeatedly until it returns a WaitCommand or null.
        /// </summary>
        public ICommand DecideNext(BattleState state, UnitState unit)
        {
            if (unit == null || !unit.IsAlive || unit.HasEndedTurn) return null;
            if (unit.Faction != state.CurrentFaction) return null;

            // --- Perception -------------------------------------------------
            // OD-16 baseline: a unit with no attackable target must NOT idle
            // forever. It falls through to target selection and closes in.
            //
            // IsActivated is therefore an OBSERVABILITY LATCH ONLY — it records
            // that the unit has seen a player at least once, and drives the HUD.
            // It no longer gates behaviour. This deliberately supersedes
            // R-THR-03 for the prototype; see docs/OPEN-DECISIONS.md#od-16.

            AiProfile profile = ProfileFor(state, unit);

            // --- Target selection -------------------------------------------
            UnitState target = SelectTarget(state, unit, profile);
            if (target == null) return new WaitCommand(unit.Id);

            bool retreating = IsBelowPercent(unit, profile.RetreatHpPercent);

            // --- Action: attack if already able ------------------------------
            if (!retreating && CanAttack(state, unit, target))
                return new AttackCommand(unit.Id, target.Id);

            // --- Position selection -----------------------------------------
            Coord? destination = SelectPosition(state, unit, target, profile, retreating);
            if (destination.HasValue && destination.Value != unit.Position)
            {
                ReachabilityMap reach = MovementCalculator.ComputeFor(state, unit);
                Coord[] path = reach.PathTo(destination.Value);
                if (path != null && path.Length > 0) return new MoveCommand(unit.Id, path);
            }

            // --- Action: attack after moving --------------------------------
            if (!retreating && CanAttack(state, unit, target))
                return new AttackCommand(unit.Id, target.Id);

            // --- Action: guard ----------------------------------------------
            if (!unit.IsGuarding
                && unit.Ap >= unit.Def.GuardApCost
                && IsBelowPercent(unit, profile.GuardHpPercent))
            {
                return new GuardCommand(unit.Id);
            }

            return new WaitCommand(unit.Id);
        }

        /// <summary>
        /// Runs the whole enemy phase and returns the concatenated effect log.
        /// The caller owns the EndTurnCommand decision.
        /// </summary>
        public ExecuteResult RunFactionTurn(BattleState state, Faction faction, EffectLog into,
                                            int maxCommandsPerUnit = 16)
        {
            BattleState current = state;

            // Snapshot the ids first: the unit list is stable, but units can die mid-phase.
            List<int> unitIds = new List<int>();
            for (int i = 0; i < current.Units.Count; i++)
                if (current.Units[i].Faction == faction) unitIds.Add(current.Units[i].Id);

            for (int i = 0; i < unitIds.Count; i++)
            {
                for (int guard = 0; guard < maxCommandsPerUnit; guard++)
                {
                    if (current.Outcome != BattleOutcome.InProgress) return ExecuteResult.Accept(current, into);

                    UnitState unit = current.FindUnit(unitIds[i]);
                    ICommand cmd = DecideNext(current, unit);
                    if (cmd == null) break;

                    ExecuteResult r = BattleSimulator.Execute(current, cmd);
                    if (!r.Ok)
                    {
                        // A rejected AI command means the AI asked for something illegal.
                        // Stop this unit rather than spinning; the reason is surfaced for debugging.
                        break;
                    }

                    current = r.State;
                    for (int e = 0; e < r.Log.Count; e++) into.Add(r.Log[e]);

                    if (cmd is WaitCommand) break;
                }
            }

            return ExecuteResult.Accept(current, into);
        }

        // ------------------------------------------------------------ internals

        private static bool CanAttack(BattleState state, UnitState unit, UnitState target)
        {
            if (unit.Ap < unit.Def.AttackApCost) return false;
            return state.Map.Topology.Distance(unit.Position, target.Position) <= unit.Def.AttackRange;
        }

        private static bool IsBelowPercent(UnitState unit, int percent)
        {
            if (percent <= 0) return false;
            return unit.Hp * 100 <= unit.Def.MaxHp * percent;
        }

        private static UnitState SelectTarget(BattleState state, UnitState unit, AiProfile profile)
        {
            // 嘲諷 overrides the profile entirely. It is deliberately the only
            // thing that does: every other target rule is a preference expressed
            // in data, and a taunt that merely nudged the score would be worth an
            // amount nobody chose. Radius is the taunter's, so "get close enough
            // to be worth hitting" is the cost of using it.
            UnitState taunter = state.ActiveTaunter(unit.Faction.Opponent());
            if (taunter != null
                && state.Map.Topology.Distance(unit.Position, taunter.Position) <= taunter.Def.TauntRadius)
            {
                return taunter;
            }

            UnitState best = null;
            int bestScore = int.MinValue;

            // Path distance, for the same reason SelectPosition uses it (OD-18):
            // Manhattan cannot see walls, so "nearest" used to mean "nearest as the
            // crow flies" — a target one cell away through a wall outranked one two
            // cells away down an open corridor, and the unit would then walk the
            // long way round to reach the answer it had already committed to.
            //
            // Only computed for the preference that needs it. It is a Dijkstra pass
            // over the map and the other two preferences do not look at position at
            // all, so paying for it there would be pure waste.
            Dictionary<Coord, int> field = profile.TargetPreference == TargetPreference.Nearest
                ? MovementCalculator.TerrainDistanceField(state.Map, unit.Position)
                : null;

            // LivingUnitsOf yields in id order, so equal scores resolve deterministically.
            foreach (UnitState candidate in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int score;
                switch (profile.TargetPreference)
                {
                    case TargetPreference.LowestHp:
                        score = -candidate.Hp;
                        break;
                    case TargetPreference.LowestDefence:
                        score = -candidate.Def.Def;
                        break;
                    default:
                        // Unreachable targets score worst rather than throwing: a map
                        // can legally seal one off mid-battle (a push into a pocket),
                        // and the AI still has to pick something.
                        score = -MovementCalculator.DistanceIn(field, candidate.Position);
                        break;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Scores every reachable cell and picks the best.
        /// Utility-lite: distance-to-preferred dominates, own exposure is weighted
        /// by (100 - Aggression), movement cost is the final tie-break.
        /// </summary>
        private static Coord? SelectPosition(BattleState state, UnitState unit, UnitState target,
                                             AiProfile profile, bool retreating)
        {
            ReachabilityMap reach = MovementCalculator.ComputeFor(state, unit);
            IReadOnlyList<Coord> cells = reach.ReachableCells;   // already sorted

            // Path distance, not straight-line distance. Manhattan cannot see walls,
            // so on a map with a chokepoint both sides end up hugging opposite faces
            // of the same wall and the battle never resolves.
            Dictionary<Coord, int> field = MovementCalculator.TerrainDistanceField(state.Map, target.Position);

            Coord best = unit.Position;
            int bestScore = int.MinValue;
            int survivalWeight = 100 - profile.Aggression;
            if (survivalWeight < 0) survivalWeight = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                Coord cell = cells[i];
                int distance = MovementCalculator.DistanceIn(field, cell);

                int score;
                if (retreating)
                {
                    score = distance * 100;
                }
                else
                {
                    int off = distance - profile.PreferredDistance;
                    if (off < 0) off = -off;
                    score = -off * 100;
                }

                // Standing somewhere lots of things can reach is bad, in proportion
                // to how much this profile cares about surviving.
                int exposure = BattleQueries.StaticExposure(state.Map, cell);
                score -= exposure * survivalWeight / 10;

                // Cheaper is better, all else equal.
                score -= reach.CostTo(cell);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }

            return best;
        }
    }
}
