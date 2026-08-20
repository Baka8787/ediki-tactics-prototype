using System.Collections.Generic;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// Clone the state, execute each legal command, score what actually changed,
    /// take the best. One ply. No search, no enemy prediction, no lookahead.
    ///
    /// ⚠️ THIS IS AN INSTRUMENT, NOT A PLAYER. It exists to answer exactly one
    /// question — can a 1-ply evaluator distinguish commands using signals the
    /// State ALREADY carries? — and its win rate is evidence about that question
    /// and nothing else. It is not a claim that any skill is good, that decision
    /// depth increased, or that any character is better designed.
    ///
    /// It reads only: the pre-state, the post-state, and the effect log. It never
    /// sees the map name, the encounter id, the objective type as a special case,
    /// or any per-character branch. Adding one would make its output a statement
    /// about the branch instead of about the signal.
    /// </summary>
    public sealed class OnePlyTacticalStrategy : IPlayerStrategy
    {
        public string Name => "one-ply";

        // ------------------------------------------------------------- weights
        //
        // 🔴 EVERY WEIGHT BELOW IS A HEURISTIC ASSUMPTION unless marked otherwise.
        // The rule layer says nothing about how many HP a kill is worth or how
        // much a point of exposure costs, so these are exchange rates invented
        // here. A result that depends on one of them is a result about the
        // exchange rate, not about the signal — which is why every component is
        // reported separately.

        /// <summary>
        /// RULE-LAYER UNIT. Enemy HP removed from the board, minus own HP lost.
        /// A kill removes ALL of the target's remaining HP, so a killing blow is
        /// worth what it actually took off rather than what the swing rolled.
        ///
        /// That is deliberately the whole reason a hazard death scores well
        /// without anything here knowing what a hazard is: shoving a 180 HP unit
        /// into a pit removes 180 HP, and the evaluator sees 180.
        ///
        /// ASSUMPTION: own HP and enemy HP trade 1:1.
        /// </summary>
        public const int DamagePerHp = 1;

        /// <summary>
        /// ASSUMPTION. A dead unit stops acting, which no 1-ply view can price —
        /// pricing it properly needs exactly the lookahead this class refuses.
        /// Flat, so it cannot secretly encode "kill the big one".
        /// </summary>
        public const int KillBonus = 25;

        /// <summary>
        /// DELIBERATELY ZERO. A hazard death already scores through Damage (full
        /// remaining HP) and Kill. Giving it its own weight would be hard-coding
        /// "shoving things into pits is good", which is the exact thing this
        /// experiment is supposed to find out rather than assume.
        ///
        /// The component is still computed and logged, as an ATTRIBUTION counter:
        /// it answers "did this command's score come from a hazard?" without
        /// contributing to the score.
        /// </summary>
        public const int HazardWeight = 0;

        /// <summary>
        /// ASSUMPTION, and the weakest one here. DEF removed is a rule-layer
        /// quantity, but what it is WORTH depends entirely on who swings next —
        /// which is lookahead. 2 points per DEF makes a 20 DEF break worth about
        /// one Momotaro swing. That number is invented.
        /// </summary>
        public const int ArmorBreakPerDef = 2;

        /// <summary>
        /// Exposure itself is rule-layer (R-GRID-08: how many living enemies
        /// threaten this cell). The EXCHANGE RATE is an assumption: 10 HP per
        /// threatening enemy removed. Used as a DELTA, never as a threshold —
        /// the project has no decided semantics for "exposure 3 is dangerous".
        /// </summary>
        public const int ExposurePerThreat = 10;

        /// <summary>Terminal states are rule-layer. The magnitude is chosen large
        /// enough to dominate, which is the one place dominating is correct.</summary>
        public const int ObjectiveWeight = 10000;

        // --------------------------------------------------------------- score

        /// <summary>
        /// A decomposed evaluation. Every field is reported so the question
        /// "which signal moved this command?" always has an answer.
        /// </summary>
        public struct Score
        {
            public int Damage;
            public int Kill;
            public int Hazard;       // attribution only; HazardWeight is 0
            public int ArmorBreak;
            public int Exposure;
            public int Objective;

            /// <summary>Counters, not score. For the smoke test's attribution.</summary>
            public int HazardDeaths;
            public int EnemyKills;
            public int DefBroken;
            public int ExposureDelta;

            public int Total => Damage + Kill + Hazard + ArmorBreak + Exposure + Objective;

            public string Describe()
            {
                return "total " + Total
                     + " = dmg " + Damage
                     + " + kill " + Kill
                     + " + hazard " + Hazard
                     + " + break " + ArmorBreak
                     + " + expo " + Exposure
                     + " + obj " + Objective
                     + "   [kills " + EnemyKills + ", hazardDeaths " + HazardDeaths
                     + ", defBroken " + DefBroken + ", expoDelta " + ExposureDelta + "]";
            }
        }

        public sealed class Ranked
        {
            public ICommand Command;
            public Score Score;
            public bool Executed;      // false when the simulator rejected it
        }

        // ------------------------------------------------------------- decide

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            List<Ranked> ranked = Rank(state, unit);
            if (ranked.Count == 0) return null;

            // Strictly greater, so ties resolve to the earliest entry in
            // LegalCommands order. Determinism rule 2: the tie-break has to be a
            // property of the enumeration, not of whatever the comparer happened
            // to see first.
            Ranked best = ranked[0];
            for (int i = 1; i < ranked.Count; i++)
                if (ranked[i].Score.Total > best.Score.Total) best = ranked[i];

            return best.Command;
        }

        /// <summary>
        /// Every legal command with its decomposed score, in enumeration order.
        /// Public so the smoke test can inspect the ranking rather than infer it
        /// from behaviour.
        /// </summary>
        public static List<Ranked> Rank(BattleState state, UnitState unit)
        {
            List<Ranked> ranked = new List<Ranked>();
            if (unit == null || !unit.IsAlive) return ranked;

            List<ICommand> legal = LegalCommands.For(state, unit);
            if (legal.Count == 0) return ranked;

            Faction side = unit.Faction;
            int exposureBefore = ExposureSum(state, side);

            for (int i = 0; i < legal.Count; i++)
            {
                ExecuteResult result = BattleSimulator.Execute(state, legal[i]);

                if (!result.Ok)
                {
                    // Should not happen — LegalCommands filters — but a rejected
                    // command must never be selectable, and silently dropping it
                    // would hide a disagreement between the two.
                    ranked.Add(new Ranked { Command = legal[i], Executed = false, Score = Rejected() });
                    continue;
                }

                ranked.Add(new Ranked
                {
                    Command = legal[i],
                    Executed = true,
                    Score = Evaluate(state, result.State, result.Log, side, exposureBefore)
                });
            }

            return ranked;
        }

        private static Score Rejected()
        {
            Score s = new Score();
            s.Objective = int.MinValue / 4;   // never selectable, never overflows
            return s;
        }

        /// <summary>
        /// Scores one already-executed command from the pre-state, post-state and
        /// effect log. No knowledge of what the command WAS — only of what it did.
        /// That is the point: a verb earns its score through its consequences or
        /// it does not earn one at all.
        /// </summary>
        public static Score Evaluate(BattleState before, BattleState after, EffectLog log,
                                     Faction side, int exposureBefore)
        {
            Score s = new Score();

            // --- Damage: HP that left the board, from the STATES, not the log.
            // A killing blow logs only the damage it rolled; what actually left
            // the board is everything the target had. Reading the states counts
            // that correctly and counts hazard deaths without special-casing them.
            int enemyHpRemoved = HpTotal(before, side.Opponent()) - HpTotal(after, side.Opponent());
            int ownHpLost = HpTotal(before, side) - HpTotal(after, side);
            s.Damage = (enemyHpRemoved - ownHpLost) * DamagePerHp;

            // --- Kill / Hazard, from the log. Hazard deaths are counted out of
            // the kill tally so the two components stay independent and the
            // attribution question has one answer instead of two.
            for (int i = 0; i < log.Count; i++)
            {
                if (log[i] is UnitFellIntoHazard fell)
                {
                    UnitState victim = after.FindUnit(fell.UnitId);
                    if (victim != null && victim.Faction != side) s.HazardDeaths++;
                }
                else if (log[i] is UnitDied died)
                {
                    if (died.Faction != side) s.EnemyKills++;
                }
                else if (log[i] is ArmorBreakApplied broke)
                {
                    UnitState target = after.FindUnit(broke.UnitId);
                    if (target != null && target.Faction != side) s.DefBroken += broke.Amount;
                }
            }

            // A hazard death also emits UnitDied, so remove the overlap.
            int plainKills = s.EnemyKills - s.HazardDeaths;
            if (plainKills < 0) plainKills = 0;

            s.Kill = plainKills * KillBonus;
            s.Hazard = s.HazardDeaths * HazardWeight;
            s.ArmorBreak = s.DefBroken * ArmorBreakPerDef;

            // --- Exposure: fewer living enemies threatening my units is better.
            // Delta, not threshold — the project has decided no threshold.
            int exposureAfter = ExposureSum(after, side);
            s.ExposureDelta = exposureAfter - exposureBefore;
            s.Exposure = -s.ExposureDelta * ExposurePerThreat;

            // --- Objective: terminal states only. Nothing here reads the
            // objective KIND, so this is the same rule on every map.
            if (after.Outcome == BattleOutcome.Victory) s.Objective = ObjectiveWeight;
            else if (after.Outcome == BattleOutcome.Defeat) s.Objective = -ObjectiveWeight;

            return s;
        }

        private static int HpTotal(BattleState state, Faction faction)
        {
            int total = 0;
            IReadOnlyList<UnitState> units = state.Units;
            for (int i = 0; i < units.Count; i++)
                if (units[i].Faction == faction && units[i].Hp > 0) total += units[i].Hp;
            return total;
        }

        /// <summary>
        /// Total effective exposure across this side's living units.
        ///
        /// Threat ranges are computed once per enemy and reused across all of this
        /// side's units — BattleQueries.EffectiveExposure would recompute one per
        /// (unit, enemy) pair, which is the same answer at several times the cost,
        /// and this runs once per candidate command.
        /// </summary>
        private static int ExposureSum(BattleState state, Faction side)
        {
            List<HashSet<Coord>> threats = new List<HashSet<Coord>>();
            foreach (UnitState enemy in state.LivingUnitsOf(side.Opponent()))
                threats.Add(BattleQueries.ThreatRange(state, enemy));

            int total = 0;
            foreach (UnitState own in state.LivingUnitsOf(side))
                for (int i = 0; i < threats.Count; i++)
                    if (threats[i].Contains(own.Position)) total++;

            return total;
        }
    }
}
