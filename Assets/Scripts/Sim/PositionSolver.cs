using System.Collections.Generic;
using System.Text;
using Ediki.Core;
using Ediki.Core.Ai;

namespace Ediki.Sim
{
    /// <summary>
    /// Answers the one question every remaining decision metric is blocked on:
    /// "what was the best thing to do here, and how much worse is everything else?"
    ///
    /// Regret is defined against an optimum. Near-optimal action ratio counts
    /// actions near an optimum. Horizon changes which action is optimal. None of
    /// the three exist without this, which is why they were all stuck together.
    ///
    /// Method: enumerate every legal command for one unit, play each one out
    /// deterministically to the horizon with a fixed policy, and score the result.
    /// One-step lookahead plus a policy rollout — not a full game-tree search, and
    /// the difference matters: the numbers below are "best FIRST action assuming
    /// the policy plays the rest", not "best play". A better policy would move
    /// every number.
    ///
    /// Objective: total damage taken by player combatants over the horizon, which
    /// is minimised. Damage has a natural zero, and the Metrics Framework is
    /// explicit that near-optimal ratios are meaningless on quantities that do not
    /// (§5.2). Losing a unit is not scored at all — it is a hard constraint and is
    /// reported separately, because folding "you died" into a damage number as a
    /// large penalty invents a rate of exchange nobody chose (§7 of the notes).
    /// </summary>
    public sealed class PositionSolver
    {
        public sealed class Options
        {
            /// <summary>Rounds to play out. 1 = only this round's damage counts.</summary>
            public int Horizon = 3;

            /// <summary>How the rest of the battle is played after the first action.</summary>
            public IPlayerStrategy Rollout = new CorridorHoldStrategy();

            public int MaxCommandsPerUnitTurn = 24;

            /// <summary>
            /// Add one round of whatever threat is still standing at the horizon.
            ///
            /// Without it the objective is degenerate: "minimise damage taken over
            /// N rounds" is perfectly satisfied by walking away, so every position
            /// scores 0 and nothing can be compared. The terminal term is the
            /// "future threat" half of the notes' two-part objective — it prices
            /// the enemies you chose not to deal with.
            ///
            /// It is evaluated with each enemy's ATK AT THE HORIZON, so a growth
            /// unit is priced at what it will have become, not what it is now.
            /// That is the entire mechanism the horizon experiment is looking for.
            /// </summary>
            public bool PriceUnfinishedThreat = true;
        }

        /// <summary>One legal action, played out and scored.</summary>
        public sealed class ActionValue
        {
            public ICommand Command;
            public string Kind;

            /// <summary>Damage the player side took over the horizon. Lower is better.</summary>
            public int DamageTaken;

            /// <summary>A player combatant died — a constraint violation, not a score.</summary>
            public bool Lost;

            /// <summary>
            /// How many legal actions collapsed into this one. Attacking either of
            /// two identical enemies is one decision described twice, and counting
            /// it twice is how near-optimal ratios get inflated (Framework §5.4).
            /// </summary>
            public int Weight = 1;
        }

        public sealed class Analysis
        {
            public readonly List<ActionValue> Actions = new List<ActionValue>();

            /// <summary>Legal commands before symmetric ones were merged.</summary>
            public int LegalActionCount;

            public int BestDamage;
            public int WorstDamage;
            public int LosingActions;

            /// <summary>Every line ends with a dead player unit: the position is lost already.</summary>
            public bool AllActionsLose;

            /// <summary>
            /// Share of DISTINCT actions whose damage is within tolerance of the
            /// best. Reported as a curve by the caller: a single tolerance hides
            /// whether the position has a cliff or a gentle slope.
            /// </summary>
            public int NearOptimalPercent(int tolerancePercent)
            {
                if (Actions.Count == 0) return 0;

                // Absolute band, so a best of zero still admits a neighbourhood.
                // A purely relative band divides by zero exactly when the position
                // is most interesting — the failure the Framework calls the most
                // dangerous one, because it returns "fine" instead of an error.
                int band = BestDamage * tolerancePercent / 100;
                if (band < 1) band = 1;

                int near = 0;
                for (int i = 0; i < Actions.Count; i++)
                    if (!Actions[i].Lost && Actions[i].DamageTaken <= BestDamage + band) near++;

                return near * 100 / Actions.Count;
            }

            /// <summary>Worst over best, x100. 100 = every action is equivalent.</summary>
            public int SpreadX100 => BestDamage <= 0
                ? (WorstDamage <= 0 ? 100 : 0)
                : WorstDamage * 100 / BestDamage;

            /// <summary>
            /// Regret of the i-th action, normalised by a budget (player max HP).
            /// Normalised rather than relative on purpose: relative regret is
            /// undefined when the best line takes no damage at all.
            /// </summary>
            public int RegretPercentOfBudget(int index, int budget)
            {
                if (budget <= 0) return 0;
                return (Actions[index].DamageTaken - BestDamage) * 100 / budget;
            }

            public int MedianDamage => Actions.Count == 0 ? 0 : Actions[Actions.Count / 2].DamageTaken;
        }

        private readonly EnemyAi _ai;

        public PositionSolver(EnemyAi ai)
        {
            _ai = ai;
        }

        /// <summary>
        /// Scores every legal command for <paramref name="unitId"/> in this position.
        /// The state is never modified; each line runs on its own clone.
        /// </summary>
        public Analysis Analyse(BattleState state, int unitId, Options options)
        {
            Analysis analysis = new Analysis();

            UnitState unit = state.FindUnit(unitId);
            if (unit == null || !unit.IsAlive) return analysis;

            List<ICommand> legal = LegalCommands.For(state, unit);
            analysis.LegalActionCount = legal.Count;

            Dictionary<string, ActionValue> merged = new Dictionary<string, ActionValue>();
            List<string> order = new List<string>();

            for (int i = 0; i < legal.Count; i++)
            {
                ExecuteResult first = BattleSimulator.Execute(state, legal[i]);
                if (!first.Ok) continue;

                int damage = TallyPlayerDamage(first.Log, first.State);
                bool lost;
                damage += PlayOut(first.State, options, out lost);

                string kind = Describe(legal[i]);

                // Same kind of action, same outcome, same verdict: one decision.
                string key = kind + "|" + damage + "|" + (lost ? "L" : "W");

                ActionValue existing;
                if (merged.TryGetValue(key, out existing)) { existing.Weight++; continue; }

                ActionValue value = new ActionValue
                {
                    Command = legal[i],
                    Kind = kind,
                    DamageTaken = damage,
                    Lost = lost
                };
                merged.Add(key, value);
                order.Add(key);
            }

            for (int i = 0; i < order.Count; i++) analysis.Actions.Add(merged[order[i]]);

            // Best-first, ties broken on description so the report is stable
            // (determinism rule 2 — this runs inside the same batch reports).
            analysis.Actions.Sort((a, b) =>
            {
                if (a.Lost != b.Lost) return a.Lost ? 1 : -1;
                int byDamage = a.DamageTaken.CompareTo(b.DamageTaken);
                if (byDamage != 0) return byDamage;
                return string.CompareOrdinal(a.Command.Describe(), b.Command.Describe());
            });

            for (int i = 0; i < analysis.Actions.Count; i++)
                if (analysis.Actions[i].Lost) analysis.LosingActions++;

            // Best and worst are read from the lines that survive. When NOTHING
            // survives the position is already lost and the damage numbers are the
            // only thing left to compare, so fall back to ranking the losses —
            // reporting "best 0" there would read as a flawless position.
            analysis.AllActionsLose = analysis.Actions.Count > 0
                                      && analysis.LosingActions == analysis.Actions.Count;

            analysis.BestDamage = int.MaxValue;
            for (int i = 0; i < analysis.Actions.Count; i++)
            {
                ActionValue v = analysis.Actions[i];
                if (v.Lost && !analysis.AllActionsLose) continue;
                if (v.DamageTaken < analysis.BestDamage) analysis.BestDamage = v.DamageTaken;
                if (v.DamageTaken > analysis.WorstDamage) analysis.WorstDamage = v.DamageTaken;
            }
            if (analysis.BestDamage == int.MaxValue) analysis.BestDamage = 0;

            return analysis;
        }

        // ------------------------------------------------------------ internals

        /// <summary>
        /// Finishes the current player phase with the policy, then plays whole
        /// rounds until the horizon runs out. Deterministic: no sampling noise,
        /// so two calls on the same position always agree.
        /// </summary>
        private int PlayOut(BattleState state, Options options, out bool lost)
        {
            int damage = 0;
            lost = false;

            DeterministicRandom rng = new DeterministicRandom(1);

            for (int round = 0; round < options.Horizon; round++)
            {
                if (state.Outcome != BattleOutcome.InProgress) break;

                if (state.CurrentFaction == Faction.Player)
                {
                    state = RunPlayerPhase(state, options, rng, ref damage);
                    if (state.Outcome != BattleOutcome.InProgress) break;

                    ExecuteResult endPlayer = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Player));
                    if (!endPlayer.Ok) break;
                    damage += TallyPlayerDamage(endPlayer.Log, endPlayer.State);
                    state = endPlayer.State;
                    if (state.Outcome != BattleOutcome.InProgress) break;
                }

                EffectLog enemyLog = new EffectLog();
                state = _ai.RunFactionTurn(state, Faction.Enemy, enemyLog,
                                           options.MaxCommandsPerUnitTurn).State;
                damage += TallyPlayerDamage(enemyLog, state);
                if (state.Outcome != BattleOutcome.InProgress) break;

                ExecuteResult endEnemy = BattleSimulator.Execute(state, new EndTurnCommand(Faction.Enemy));
                if (!endEnemy.Ok) break;
                damage += TallyPlayerDamage(endEnemy.Log, endEnemy.State);
                state = endEnemy.State;
            }

            lost = state.Outcome == BattleOutcome.Defeat;
            if (options.PriceUnfinishedThreat) damage += StandingThreat(state);
            return damage;
        }

        /// <summary>
        /// One round of damage from every enemy still alive, valued at the ATK it
        /// has by now. Enemies that are dead cost nothing, which is what makes
        /// killing them worth anything to the objective.
        /// </summary>
        private static int StandingThreat(BattleState state)
        {
            int worstPlayerDef = int.MaxValue;
            foreach (UnitState p in state.LivingUnitsOf(Faction.Player))
            {
                if (p.MustSurvive) continue;
                if (p.Def.Def < worstPlayerDef) worstPlayerDef = p.Def.Def;
            }
            if (worstPlayerDef == int.MaxValue) return 0;

            int threat = 0;
            foreach (UnitState e in state.LivingUnitsOf(Faction.Enemy))
            {
                int perHit = BattleRules.ComputeDamage(e.Def.AtkOnRound(state.TurnIndex),
                                                      worstPlayerDef, false, state.Rules.Damage);
                int attacks = e.Def.AttackApCost <= 0 ? 0 : e.Def.ApRegen / e.Def.AttackApCost;
                threat += perHit * attacks;
            }

            return threat;
        }

        private static BattleState RunPlayerPhase(BattleState state, Options options,
                                                  DeterministicRandom rng, ref int damage)
        {
            List<int> ids = new List<int>();
            foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
                if (!u.MustSurvive) ids.Add(u.Id);

            for (int i = 0; i < ids.Count; i++)
            {
                for (int step = 0; step < options.MaxCommandsPerUnitTurn; step++)
                {
                    if (state.Outcome != BattleOutcome.InProgress) return state;

                    UnitState unit = state.FindUnit(ids[i]);
                    if (unit == null || !unit.IsAlive || unit.HasEndedTurn) break;

                    ICommand command = options.Rollout.DecideNext(state, unit, rng);
                    if (command == null) break;

                    ExecuteResult r = BattleSimulator.Execute(state, command);
                    if (!r.Ok) break;

                    damage += TallyPlayerDamage(r.Log, r.State);
                    state = r.State;

                    if (command is WaitCommand || command is RestCommand) break;
                }
            }

            return state;
        }

        /// <summary>
        /// Damage the player side took in this log. Healing is not subtracted:
        /// the objective is damage suffered, and a Rest that undoes it is a
        /// separate decision with its own cost.
        /// </summary>
        private static int TallyPlayerDamage(EffectLog log, BattleState state)
        {
            int damage = 0;

            for (int i = 0; i < log.Count; i++)
            {
                HpChanged change = log[i] as HpChanged;
                if (change == null || change.Delta >= 0) continue;

                UnitState u = state.FindUnit(change.UnitId);
                if (u == null || u.Faction != Faction.Player || u.MustSurvive) continue;

                damage -= change.Delta;
            }

            return damage;
        }

        private static string Describe(ICommand command)
        {
            if (command is MoveCommand) return "Move";
            if (command is AttackCommand) return "Attack";
            if (command is GuardCommand) return "Guard";
            if (command is RestCommand) return "Rest";
            if (command is WaitCommand) return "Wait";
            return "Other";
        }

        // ------------------------------------------------------------ reporting

        public static string Describe(Analysis a, int budget)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("  legal ").Append(a.LegalActionCount)
              .Append(" -> ").Append(a.Actions.Count).Append(" distinct")
              .Append("   best ").Append(a.BestDamage)
              .Append("   worst ").Append(a.WorstDamage)
              .Append("   spread x").Append(a.SpreadX100 / 100).Append('.')
              .Append((a.SpreadX100 % 100).ToString("00"))
              .Append("   losing ").Append(a.LosingActions)
              .AppendLine(a.AllActionsLose ? "   ** every line loses — the position is already lost" : "");

            sb.Append("  NOAR  5% ").Append(a.NearOptimalPercent(5))
              .Append("%   10% ").Append(a.NearOptimalPercent(10))
              .Append("%   20% ").Append(a.NearOptimalPercent(20))
              .Append("%   50% ").Append(a.NearOptimalPercent(50)).AppendLine("%");

            sb.Append("  best action: ");
            if (a.Actions.Count > 0) sb.Append(a.Actions[0].Command.Describe())
                                       .Append("  (damage ").Append(a.Actions[0].DamageTaken).Append(')');
            sb.AppendLine();

            if (a.Actions.Count > 1)
            {
                ActionValue worst = a.Actions[a.Actions.Count - 1];
                sb.Append("  worst action: ").Append(worst.Command.Describe())
                  .Append("  regret ").Append(a.RegretPercentOfBudget(a.Actions.Count - 1, budget))
                  .AppendLine("% of HP budget");
            }

            return sb.ToString();
        }
    }
}
