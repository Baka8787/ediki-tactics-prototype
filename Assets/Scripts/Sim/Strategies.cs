using System.Collections.Generic;
using Ediki.Core;

namespace Ediki.Sim
{
    /// <summary>
    /// A scripted way of playing the player side. Strategies only issue Commands
    /// through BattleSimulator — they cannot change rules.
    /// Return null to end the unit's activation.
    /// </summary>
    public interface IPlayerStrategy
    {
        string Name { get; }
        ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng);
    }

    /// <summary>
    /// Where the current objective wants the player to be, or null if the objective
    /// does not care about position.
    ///
    /// Strategies have to know this: against a Reach objective, "hold the safest
    /// cell" is a losing play, and a strategy that ignored the clock would make the
    /// objective comparison meaningless.
    /// </summary>
    public static class ObjectiveAnchor
    {
        public static Coord? For(BattleState state)
        {
            switch (state.Objective.Kind)
            {
                case ObjectiveKind.Reach:
                    return state.Objective.Target;

                case ObjectiveKind.Defend:
                    foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
                        if (u.MustSurvive) return u.Position;
                    return null;

                // Kill deliberately returns null. It is the one objective that does
                // NOT dictate where the player stands, and anchoring on the target
                // would hand it one: the anchor term outweighs exposure by 5x, so
                // every strategy would march at the target in a straight line and
                // the map would stop mattering. Kill changes WHO has to die, and
                // that belongs in target choice — see DecapitateStrategy.
                default:
                    return null;
            }
        }
    }

    /// <summary>Enumerates every command a unit could legally issue right now.</summary>
    public static class LegalCommands
    {
        public static List<ICommand> For(BattleState state, UnitState unit)
        {
            List<ICommand> result = new List<ICommand>();
            if (unit == null || !unit.IsAlive || unit.HasEndedTurn) return result;

            // Per-round caps are filtered here, not just rejected in the simulator.
            // This list is what the batch runner's 15% uniform noise samples from,
            // so an illegal command left in it does not merely fail — it burns the
            // unit's whole activation on a rejection and shows up in the telemetry
            // as a decision the strategy made.
            bool canAttack = state.CanAttackAgain(unit);
            bool canSkill = state.CanUseSkillAgain(unit);

            foreach (UnitState target in BattleQueries.AttackableTargets(state, unit))
                if (canAttack && unit.Ap >= unit.Def.AttackApCost)
                    result.Add(new AttackCommand(unit.Id, target.Id));

            ReachabilityMap reach = MovementCalculator.ComputeFor(state, unit);
            for (int i = 0; i < reach.ReachableCells.Count; i++)
            {
                Coord cell = reach.ReachableCells[i];
                if (cell == unit.Position) continue;
                Coord[] path = reach.PathTo(cell);
                if (path != null && path.Length > 0) result.Add(new MoveCommand(unit.Id, path));
            }

            if (!unit.IsGuarding && unit.Ap >= unit.Def.GuardApCost)
                result.Add(new GuardCommand(unit.Id));

            // Only offered where there is something to clean: purifying clean
            // ground burns 4 AP for nothing, and listing it would inflate every
            // near-optimal count with an action no one would ever take.
            if (canSkill && unit.Def.CanPurify && unit.Ap >= unit.Def.PurifyApCost
                && state.ContaminationAt(unit.Position) > 0)
                result.Add(new PurifyCommand(unit.Id));

            if (unit.Def.CanRest && unit.Ap >= unit.Def.RestApCost)
                result.Add(new RestCommand(unit.Id));

            // Control kit. Each is listed only where it would actually do
            // something — a slow on an already-slowed target and a push into a
            // wall are both rejected by the simulator, and listing them would
            // inflate every near-optimal count with actions that cannot happen.
            if (canSkill && unit.Def.CanTaunt && unit.Ap >= unit.Def.TauntApCost && !state.IsTaunting(unit))
                result.Add(new TauntCommand(unit.Id));

            foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int distance = state.Map.Topology.Distance(unit.Position, enemy.Position);

                if (canSkill && unit.Def.CanSlow && unit.Ap >= unit.Def.SlowApCost
                    && distance <= unit.Def.SlowRange && !state.IsSlowed(enemy))
                {
                    result.Add(new SlowCommand(unit.Id, enemy.Id));
                }

                if (canSkill && unit.Def.CanPush && unit.Ap >= unit.Def.PushApCost
                    && distance <= unit.Def.PushRange && !enemy.Def.ImmuneToPush
                    && PushLands(state, unit, enemy))
                {
                    result.Add(new PushCommand(unit.Id, enemy.Id));
                }

                // Listed only where it would change something, same rule as the
                // rest of the kit: already-broken armour is rejected by the
                // simulator, and a target whose DEF is already 0 has nothing to
                // strip — offering either would inflate the near-optimal counts
                // with actions that buy nothing.
                if (canSkill && unit.Def.CanArmorBreak && unit.Ap >= unit.Def.ArmorBreakApCost
                    && distance <= unit.Def.ArmorBreakRange && !state.IsArmorBroken(enemy)
                    && state.EffectiveDef(enemy) > 0)
                {
                    result.Add(new ArmorBreakCommand(unit.Id, enemy.Id));
                }
            }

            result.Add(new WaitCommand(unit.Id));
            return result;
        }

        /// <summary>True when a push from this actor would actually move the target.</summary>
        internal static bool PushLands(BattleState state, UnitState actor, UnitState target)
        {
            Coord destination;
            if (!BattleSimulator.TryPushDestination(actor.Position, target.Position, out destination))
                return false;
            return state.CanUnitEnter(destination);
        }
    }

    /// <summary>
    /// "Hold the corridor": stand where the fewest enemies can reach you, hit
    /// whatever comes into range, guard with the leftovers.
    /// This is the play the Exposure hypothesis predicts should win.
    /// </summary>
    public class CorridorHoldStrategy : IPlayerStrategy
    {
        public virtual string Name => "corridor-hold";

        /// <summary>
        /// Which of the reachable enemies to hit.
        ///
        /// The base answer is "the first one", i.e. spawn order — deliberately
        /// arbitrary, because this strategy is about WHERE to stand and any
        /// cleverness here would contaminate that measurement. Subclasses that
        /// exist to measure target choice override this and change nothing else.
        /// </summary>
        protected virtual UnitState ChooseTarget(BattleState state, UnitState unit, List<UnitState> targets)
            => targets[0];

        /// <summary>
        /// Extra score for standing <paramref name="enemyDistance"/> path-cells from
        /// the nearest enemy. Zero here — the base strategy has no opinion beyond
        /// "fewer threats, then closer".
        ///
        /// The hook exists so a subclass can express a FORMATION without touching
        /// the exposure term, in exactly the way ChooseTarget lets one express a
        /// target rule without touching movement. Anything that changed the
        /// exposure weighting instead would stop being attributable.
        /// </summary>
        protected virtual int PositionBonus(BattleState state, UnitState unit, int enemyDistance) => 0;

        public virtual ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            List<UnitState> targets = BattleQueries.AttackableTargets(state, unit);
            if (targets.Count > 0 && unit.Ap >= unit.Def.AttackApCost)
                return new AttackCommand(unit.Id, ChooseTarget(state, unit, targets).Id);

            ReachabilityMap reach = MovementCalculator.ComputeFor(state, unit);
            Coord? anchor = ObjectiveAnchor.For(state);

            // Path distance to the objective, for the same reason OD-18 put the
            // enemy term on path distance: Manhattan cannot see walls, so a
            // strategy scored that way walks at the target in a straight line and
            // parks against the wall face beside the gap it should have used.
            Dictionary<Coord, int> anchorField = anchor.HasValue
                ? MovementCalculator.TerrainDistanceField(state.Map, anchor.Value)
                : null;

            // Distance field seeded from every living enemy at once: one flood fill
            // answers "how far is the nearest threat, going round the walls".
            Dictionary<Coord, int> enemyField = NearestEnemyField(state, unit);
            Coord best = unit.Position;
            int bestScore = int.MinValue;

            for (int i = 0; i < reach.ReachableCells.Count; i++)
            {
                Coord cell = reach.ReachableCells[i];

                // Fewer threats is better; among equals, being closer to an enemy is
                // better (otherwise it retreats forever and never finishes the fight).
                int threats = BattleQueries.EffectiveExposure(state, cell, unit.Faction);
                int distance = MovementCalculator.DistanceIn(enemyField, cell);

                int score = -threats * 1000 - distance * 10 - reach.CostTo(cell)
                          + PositionBonus(state, unit, distance);

                // The objective outranks safety — a Reach objective makes camping a loss.
                if (anchorField != null)
                {
                    // DistanceIn reports a huge sentinel for unreachable cells;
                    // clamp before scaling or the multiply overflows and an
                    // unreachable cell scores as the best one on the map.
                    int toAnchor = MovementCalculator.DistanceIn(anchorField, cell);
                    if (toAnchor > 9999) toAnchor = 9999;
                    score -= toAnchor * 5000;
                }

                if (score > bestScore) { bestScore = score; best = cell; }
            }

            if (best != unit.Position)
            {
                Coord[] path = reach.PathTo(best);
                if (path != null && path.Length > 0) return new MoveCommand(unit.Id, path);
            }

            // Rest only where nothing can reach us: it heals, but it also ends the
            // turn, so doing it under threat throws away the Guard that would have
            // mattered more.
            bool safe = BattleQueries.EffectiveExposure(state, unit.Position, unit.Faction) == 0;
            bool hurt = unit.Hp * 100 < unit.Def.MaxHp * 70;
            if (safe && hurt && unit.Def.CanRest && unit.Ap >= unit.Def.RestApCost)
                return new RestCommand(unit.Id);

            if (!unit.IsGuarding && unit.Ap >= unit.Def.GuardApCost) return new GuardCommand(unit.Id);
            return new WaitCommand(unit.Id);
        }

        private static int NearestEnemyDistance(BattleState state, UnitState unit, Coord from)
        {
            int best = int.MaxValue;
            foreach (UnitState e in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int d = state.Map.Topology.Distance(from, e.Position);
                if (d < best) best = d;
            }
            return best == int.MaxValue ? 0 : best;
        }

        /// <summary>Path distance from every cell to the closest living enemy.</summary>
        private static Dictionary<Coord, int> NearestEnemyField(BattleState state, UnitState unit)
        {
            Dictionary<Coord, int> merged = new Dictionary<Coord, int>();
            foreach (UnitState e in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                Dictionary<Coord, int> field = MovementCalculator.TerrainDistanceField(state.Map, e.Position);
                foreach (KeyValuePair<Coord, int> kv in field)
                {
                    int known;
                    if (!merged.TryGetValue(kv.Key, out known) || kv.Value < known) merged[kv.Key] = kv.Value;
                }
            }
            return merged;
        }

        /// <summary>Path distance to the nearest living enemy, walls included.</summary>
        internal static int NearestEnemyPathDistance(BattleState state, UnitState unit, Coord from)
        {
            Dictionary<Coord, int> field = MovementCalculator.TerrainDistanceField(state.Map, from);
            int best = int.MaxValue;
            foreach (UnitState e in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int d = MovementCalculator.DistanceIn(field, e.Position);
                if (d < best) best = d;
            }
            return best == int.MaxValue ? 0 : best;
        }
    }

    /// <summary>
    /// "Threat priority": stand exactly where corridor-hold would stand, but pick
    /// the target with the highest threat removed per action.
    ///
    ///     TPA = damage it deals per turn / hits still needed to kill it
    ///
    /// This is Smith's Rule (WSPT, 1956) in tactics clothing, and the two research
    /// notes claim it is the optimal kill order whenever every enemy is already in
    /// reach, damage does not fall with HP, and nothing grows.
    ///
    /// It exists to make that claim FALSIFIABLE inside the running game. Without
    /// it, both scripted strategies pick targets in spawn order, so no measurement
    /// this project has taken so far says anything about target priority at all.
    ///
    /// Everything except the target choice is inherited unchanged, so any
    /// difference against corridor-hold is attributable to target choice alone.
    /// </summary>
    public class ThreatPriorityStrategy : CorridorHoldStrategy
    {
        public override string Name => "tpa-order";

        protected override UnitState ChooseTarget(BattleState state, UnitState unit, List<UnitState> targets)
        {
            UnitState best = targets[0];
            int bestScoreX100 = int.MinValue;

            for (int i = 0; i < targets.Count; i++)
            {
                int scoreX100 = ThreatPerActionX100(state, unit, targets[i]);

                // Ties break on unit id (targets arrive in id order), so the whole
                // strategy stays reproducible run to run.
                if (scoreX100 > bestScoreX100) { bestScoreX100 = scoreX100; best = targets[i]; }
            }

            return best;
        }

        /// <summary>
        /// Hits still needed to kill this target — remaining HP, not full HP.
        /// A target already half dead is cheaper to finish and any ordering that
        /// missed that would re-derive the same answer every turn.
        /// </summary>
        protected static int HitsLeft(BattleState state, UnitState attacker, UnitState target)
        {
            int perHit = BattleRules.ComputeDamage(attacker.Def.AtkOnRound(state.TurnIndex),
                                                  target.Def.Def, target.IsGuarding, state.Rules.Damage);
            if (perHit < 1) perHit = 1;
            int hits = (target.Hp + perHit - 1) / perHit;
            return hits < 1 ? 1 : hits;
        }

        /// <summary>Damage this target deals per turn, divided by hits still needed.</summary>
        protected static int ThreatPerActionX100(BattleState state, UnitState attacker, UnitState target)
        {
            int perHit = BattleRules.ComputeDamage(target.Def.AtkOnRound(state.TurnIndex),
                                                  attacker.Def.Def, false, state.Rules.Damage);
            int attacks = target.Def.AttackApCost <= 0
                ? 0 : target.Def.ApRegen / target.Def.AttackApCost;

            return perHit * attacks * 100 / HitsLeft(state, attacker, target);
        }
    }

    /// <summary>
    /// "Residue aware": the same threat ordering, restricted to targets this unit
    /// can actually FINISH with the actions it still has this turn.
    ///
    /// Why it exists. With two attacks a turn and a three-hit elite, the turn you
    /// open on the elite has one action that cannot possibly finish it. Threat per
    /// action still says "elite, elite" and ends the turn with nothing dead;
    /// spending that odd action on a one-hit minion removes a whole enemy instead.
    /// The paper model puts the gap at 12% and calls it the only thing a one-hit
    /// minion is irreplaceably good for
    /// (docs/NOTE-2026-08-14-敵人刀數與目標優先級數值模型.md §7.1).
    ///
    /// Nothing this project had could test that claim, because every strategy so
    /// far ordered targets without ever asking whether an action could complete a
    /// kill. This one asks, and changes nothing else — same movement, same threat
    /// ranking, so the difference against tpa-order is attributable to the
    /// finishability rule alone.
    ///
    /// When nothing is finishable it falls back to plain threat ordering: chip
    /// damage is not wasted, it just does not remove any threat this turn.
    /// </summary>
    public sealed class ResidueAwareStrategy : ThreatPriorityStrategy
    {
        public override string Name => "residue-aware";

        protected override UnitState ChooseTarget(BattleState state, UnitState unit, List<UnitState> targets)
        {
            int actionsLeft = unit.Def.AttackApCost <= 0 ? 0 : unit.Ap / unit.Def.AttackApCost;
            int actionsNextTurn = unit.Def.AttackApCost <= 0 ? 0 : unit.Def.ApRegen / unit.Def.AttackApCost;

            UnitState top = base.ChooseTarget(state, unit, targets);
            int hitsOnTop = HitsLeft(state, unit, top);

            // Finish it now if the budget reaches.
            if (hitsOnTop <= actionsLeft) return top;

            // It cannot die this turn. Keep hitting it only while it still needs
            // more hits than NEXT turn alone can supply — that is the part of this
            // turn that is genuinely load-bearing. Once the leftover is down to
            // what next turn can finish anyway, further hits on it buy nothing,
            // and THAT is the residue the whole strategy is named after.
            if (hitsOnTop > actionsNextTurn) return top;

            UnitState bestFinishable = null;
            int bestFinishableScore = int.MinValue;

            for (int i = 0; i < targets.Count; i++)
            {
                UnitState candidate = targets[i];
                if (candidate.Id == top.Id) continue;
                if (HitsLeft(state, unit, candidate) > actionsLeft) continue;

                int score = ThreatPerActionX100(state, unit, candidate);
                if (score > bestFinishableScore) { bestFinishableScore = score; bestFinishable = candidate; }
            }

            return bestFinishable ?? top;
        }
    }

    /// <summary>
    /// "Decapitate": stand exactly where corridor-hold would stand, but hit the
    /// unit the objective names whenever it is in reach.
    ///
    /// Under a Rout objective every enemy has to die, so "which one first" is a
    /// scheduling question and tpa-order already prices it. Under a Kill objective
    /// the other enemies do not have to die at all, and the question changes shape
    /// entirely: clearing a minion is now an INVESTMENT that only pays back if the
    /// target outlives the payback period. The paper model puts the crossover at
    ///
    ///     clearing minion j is worth it  &lt;=&gt;  DPT_j x (rounds the target has left)
    ///                                          &gt;  hits_j x target DPT
    ///
    /// (docs/NOTE-2026-08-15-目標類型如何改變決策.md §3), which predicts that a
    /// HARD target favours clearing first and a soft one favours rushing.
    ///
    /// This strategy is the "always rush" end of that. corridor-hold, which hits
    /// whatever is in reach in spawn order, is close to the "clear first" end. The
    /// gap between them is the price of going for the head — the same construction
    /// tpa-order uses, and for the same reason: only ChooseTarget differs, so the
    /// difference cannot be coming from movement.
    ///
    /// Known limit, stated rather than smuggled in: it does not TRAVEL to the
    /// target. Standing still comes from corridor-hold, so this measures target
    /// choice alone. A strategy that also crosses the map for the head is a
    /// different instrument and would confound the two.
    /// </summary>
    public sealed class DecapitateStrategy : CorridorHoldStrategy
    {
        public override string Name => "decapitate";

        protected override UnitState ChooseTarget(BattleState state, UnitState unit, List<UnitState> targets)
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].IsObjectiveTarget) return targets[i];

            // Nothing marked is in reach — or the encounter marks nobody at all,
            // in which case this is corridor-hold exactly. That equality is worth
            // having: on a rout map the two strategies must produce bit-identical
            // batches, which is the cheapest possible proof the instrument is wired
            // to the objective and not to something else.
            return base.ChooseTarget(state, unit, targets);
        }
    }

    /// <summary>
    /// Corridor-hold that also cleans the ground it is standing on.
    ///
    /// Purification is a mechanic with no user: the command is legal, but no
    /// scripted strategy ever chooses it, so a batch would report that it does
    /// nothing — the same trap the counter-attack mechanic fell into, where 200
    /// battles fired it 13 times and the numbers looked like a verdict.
    ///
    /// The rule is deliberately the simplest one that can be called honest:
    /// clean the ground under your feet before doing anything else, whenever it
    /// is dirty and the AP is there. A cleverer version would purify where it is
    /// about to STAND rather than where it is, which needs lookahead this
    /// strategy does not have — recorded as a limit, not smuggled in.
    /// </summary>
    public sealed class PurifyingHoldStrategy : CorridorHoldStrategy
    {
        public override string Name => "purify-hold";

        public override ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            if (unit.Def.CanPurify
                && unit.Ap >= unit.Def.PurifyApCost
                && state.ContaminationAt(unit.Position) > 0)
            {
                return new PurifyCommand(unit.Id);
            }

            return base.DecideNext(state, unit, rng);
        }
    }

    /// <summary>
    /// "Control hold": corridor-hold that also uses taunt / slow / push.
    ///
    /// It exists for the same reason purify-hold does — a mechanic with no user
    /// measures as having no effect, and that reads like a verdict when it is
    /// actually an absence. Counter-attack has been in this project since the
    /// objectives round and STILL has no user, which is exactly the trap.
    ///
    /// The rule is the simplest one that can be called honest, in priority order:
    ///
    ///   1. Taunt when at least two enemies are close enough to be redirected.
    ///      Fewer than two and it is not doing anything a single guard would not.
    ///   2. Otherwise attack, if an attack can FINISH something this turn. A kill
    ///      removes a whole threat; control only delays one.
    ///   3. Otherwise slow the nearest unslowed enemy in range — the action that
    ///      could not have finished anything anyway.
    ///   4. Otherwise push an adjacent enemy, but only when the shove takes it out
    ///      of its own reach of everyone on our side. A push that leaves it still
    ///      able to attack has bought nothing.
    ///   5. Otherwise fall through to corridor-hold unchanged.
    ///
    /// A unit with none of the three skills therefore plays corridor-hold exactly,
    /// which is what makes the comparison attributable.
    /// </summary>
    public sealed class ControlHoldStrategy : CorridorHoldStrategy
    {
        public override string Name => "control-hold";

        public override ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            ICommand control = BestControlAction(state, unit);
            if (control != null) return control;
            return base.DecideNext(state, unit, rng);
        }

        /// <summary>
        /// A control action, but only where it beats swinging.
        ///
        /// The first version of this gated on "can I finish something this turn",
        /// and it cost 15 points against plain corridor-hold. The reason is worth
        /// keeping: Zhengshou needs 7-10 hits to kill anything and Kagemaru 4-6,
        /// so "cannot finish" is permanently TRUE for both of them and they spent
        /// every single action on control while the party's damage collapsed.
        ///
        /// So each skill now has to justify itself situationally instead:
        /// </summary>
        private static ICommand BestControlAction(BattleState state, UnitState unit)
        {
            // Taunt: two or more enemies close enough to redirect, AND this unit
            // is the one that survives being hit. Redirecting onto the squishiest
            // member is worse than not taunting at all.
            if (unit.Def.CanTaunt && unit.Ap >= unit.Def.TauntApCost && !state.IsTaunting(unit)
                && CountEnemiesWithin(state, unit, unit.Def.TauntRadius) >= 2
                && IsSturdiest(state, unit))
            {
                return new TauntCommand(unit.Id);
            }

            UnitState slowTarget = null;
            UnitState pushTarget = null;
            int bestSlowDistance = int.MaxValue;

            // Enemies arrive in id order, so ties resolve identically every run.
            foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int distance = state.Map.Topology.Distance(unit.Position, enemy.Position);

                // Slow: only something not yet in contact. 遲滯 buys time before
                // an enemy arrives; spending it on one already swinging buys
                // nothing at all, and that was most of what the first version did.
                if (unit.Def.CanSlow && unit.Ap >= unit.Def.SlowApCost
                    && distance <= unit.Def.SlowRange && !state.IsSlowed(enemy)
                    && !IsInContact(state, enemy)
                    && distance < bestSlowDistance)
                {
                    bestSlowDistance = distance;
                    slowTarget = enemy;
                }

                if (pushTarget == null && unit.Def.CanPush && unit.Ap >= unit.Def.PushApCost
                    && distance <= unit.Def.PushRange && !enemy.Def.ImmuneToPush
                    && PushBreaksContact(state, unit, enemy))
                {
                    pushTarget = enemy;
                }
            }

            if (slowTarget != null) return new SlowCommand(unit.Id, slowTarget.Id);
            if (pushTarget != null) return new PushCommand(unit.Id, pushTarget.Id);
            return null;
        }

        /// <summary>Is this enemy already able to hit someone on our side?</summary>
        private static bool IsInContact(BattleState state, UnitState enemy)
        {
            foreach (UnitState friendly in state.LivingUnitsOf(enemy.Faction.Opponent()))
            {
                if (friendly.MustSurvive) continue;
                if (state.Map.Topology.Distance(enemy.Position, friendly.Position) <= enemy.Def.AttackRange)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Does this unit take the fewest hits to kill, of everyone on our side?
        /// Measured in HITS rather than HP, because a 435 HP wall behind DEF 70 is
        /// a different proposition from 435 HP behind DEF 30.
        /// </summary>
        private static bool IsSturdiest(BattleState state, UnitState unit)
        {
            int mine = HitsToKill(state, unit);
            foreach (UnitState friendly in state.LivingUnitsOf(unit.Faction))
            {
                if (friendly.MustSurvive || friendly.Id == unit.Id) continue;
                if (HitsToKill(state, friendly) > mine) return false;
            }
            return true;
        }

        /// <summary>Hits the hardest-hitting living enemy needs to remove this unit.</summary>
        private static int HitsToKill(BattleState state, UnitState unit)
        {
            int worst = 1;
            foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int perHit = BattleRules.ComputeDamage(enemy.Def.AtkOnRound(state.TurnIndex),
                                                       unit.Def.Def, false, state.Rules.Damage);
                if (perHit > worst) worst = perHit;
            }
            return (unit.Hp + worst - 1) / worst;
        }

        /// <summary>
        /// Would this push put the target out of range of everyone on our side?
        ///
        /// Range only — it deliberately ignores that the target can walk back next
        /// turn. Buying one turn IS the effect being measured; pricing it against
        /// a full lookahead would be a different strategy.
        /// </summary>
        private static bool PushBreaksContact(BattleState state, UnitState pusher, UnitState target)
        {
            Coord destination;
            if (!BattleSimulator.TryPushDestination(pusher.Position, target.Position, out destination))
                return false;
            if (!state.CanUnitEnter(destination)) return false;

            foreach (UnitState friendly in state.LivingUnitsOf(pusher.Faction))
            {
                if (friendly.MustSurvive) continue;
                if (state.Map.Topology.Distance(destination, friendly.Position) <= target.Def.AttackRange)
                    return false;
            }
            return true;
        }

        private static int CountEnemiesWithin(BattleState state, UnitState unit, int radius)
        {
            int n = 0;
            foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
                if (state.Map.Topology.Distance(unit.Position, enemy.Position) <= radius) n++;
            return n;
        }
    }

    /// <summary>
    /// "Shield wall": corridor-hold that deliberately puts the sturdiest unit in
    /// front and keeps everyone else behind it.
    ///
    /// This is the line the project owner played and won with — 「站位輪流扛傷」—
    /// and no scripted strategy has ever played it. corridor-hold scores every
    /// unit independently, so the party arrives in a blob and the enemy AI, which
    /// targets whoever is NEAREST for five of the six units on gym-opening, hits
    /// whatever happened to be closest. A human puts Zhengshou there on purpose.
    ///
    /// Why it should work, from §18.4: routing every hit into DEF 70 / HP 435
    /// means 30 damage a hit and 14.5 hits to kill, against 65-70 damage and 3
    /// hits for the two fragile members. Party effective HP goes up roughly
    /// four to five times, for free, using only movement.
    ///
    /// It exists to make that measurable. Without it, tightening the routing rule
    /// cannot be evaluated at all — the baseline strategy does not exploit the
    /// thing being tightened, so the fix would appear to do nothing.
    /// </summary>
    public sealed class ShieldWallStrategy : CorridorHoldStrategy
    {
        public override string Name => "shield-wall";

        /// <summary>
        /// The tank wants to be nearest; everyone else wants to be behind it.
        ///
        /// The magnitudes sit either side of corridor-hold's own distance term
        /// (-10 per cell) so the formation actually overrides the default pull
        /// toward the enemy, but stay well under the exposure term (-1000 per
        /// threat) so nobody walks into a crossfire to hold formation.
        /// </summary>
        protected override int PositionBonus(BattleState state, UnitState unit, int enemyDistance)
        {
            if (!IsSturdiest(state, unit))
            {
                // Hang back, but with a ceiling: without it the fragile members
                // would retreat off the map and the battle would never resolve.
                int held = enemyDistance > 6 ? 6 : enemyDistance;
                return held * 25;
            }

            return -enemyDistance * 40;
        }

        /// <summary>
        /// Fewest hits to kill, of everyone on our side. Measured in HITS rather
        /// than raw HP because 435 HP behind DEF 70 is a different proposition
        /// from 435 HP behind DEF 30, and it is the hits that decide who should
        /// be standing in front.
        /// </summary>
        private static bool IsSturdiest(BattleState state, UnitState unit)
        {
            int mine = HitsToKill(state, unit);
            foreach (UnitState friendly in state.LivingUnitsOf(unit.Faction))
            {
                if (friendly.MustSurvive || friendly.Id == unit.Id) continue;
                if (HitsToKill(state, friendly) > mine) return false;
            }
            return true;
        }

        private static int HitsToKill(BattleState state, UnitState unit)
        {
            int worst = 1;
            foreach (UnitState enemy in state.LivingUnitsOf(unit.Faction.Opponent()))
            {
                int perHit = BattleRules.ComputeDamage(enemy.Def.AtkOnRound(state.TurnIndex),
                                                       unit.Def.Def, false, state.Rules.Damage);
                if (perHit > worst) worst = perHit;
            }
            return (unit.Hp + worst - 1) / worst;
        }
    }

    /// <summary>
    /// "Sustain hold": corridor-hold that spends its LEFTOVER AP on Guard instead
    /// of on a second attack.
    ///
    /// It exists because a human found this line in one sitting and no scripted
    /// strategy in this project has ever played it. corridor-hold attacks while it
    /// can afford to — with 8 AP that is twice, leaving nothing — and only guards
    /// when there was nothing to hit. So every win rate this project has published
    /// was measured by a strategy that never once attacked and guarded in the same
    /// turn, which is the first thing a person does.
    ///
    /// The arithmetic it is chasing, on the current roster:
    ///
    ///     move 1 (1 AP) + attack (4 AP) + guard (3 AP) = 8 AP = exactly one turn
    ///
    /// and guard halves everything incoming, which against two attackers is worth
    /// 100-140 HP for 3 AP, where the second attack it replaces was worth 40
    /// damage for 4 AP. Guard is roughly three times the AP efficiency and it does
    /// not compete with anything.
    ///
    /// The gate is "I can pay for guard, and I have not got a full bar's worth of
    /// AP left" — which is stateless, and correctly stops paying when guard is
    /// priced above what one turn's leftovers can cover. That second half matters:
    /// it is what makes the guard-cost sweep measure anything.
    /// </summary>
    public sealed class SustainHoldStrategy : CorridorHoldStrategy
    {
        public override string Name => "sustain-hold";

        public override ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            if (!unit.IsGuarding
                && unit.Def.GuardApCost > 0
                && unit.Ap >= unit.Def.GuardApCost
                && unit.Ap <= unit.Def.MaxAp - unit.Def.AttackApCost)
            {
                return new GuardCommand(unit.Id);
            }

            return base.DecideNext(state, unit, rng);
        }
    }

    /// <summary>
    /// "Counter reserve": attack only while the riposte stays affordable.
    ///
    /// AN EXPERIMENT INSTRUMENT, NOT AN AI. It exists to price ONE thing: what
    /// holding counterCost AP back actually costs. Every other strategy here
    /// spends its bar down and ends the turn on nothing, so the counter-attack
    /// has never been a choice anyone made — the activations measured so far are
    /// a by-product of rounds with no target in reach.
    ///
    /// The rule is deliberately the smallest one that can be called honest:
    ///
    ///   attack  only when Ap - attackCost >= counterCost
    ///   else    end the activation, keeping the rest
    ///
    /// It does NOT guard, because guard costs 3 AP and the reserve is 3 AP —
    /// guarding would spend exactly the thing being measured, which is how the
    /// 5 AP variant ended up with a LOWER counter rate under corridor-hold.
    ///
    /// It does NOT move either. That is a real limitation and it is the point of
    /// keeping the instrument minimal: on a map where the player must close, this
    /// strategy would never arrive. It is only valid on encounters where the enemy
    /// comes to you (OD-16), which is what the counter experiment uses.
    ///
    /// A unit that cannot counter has counterCost 0, so the test degrades to
    /// "attack while affordable" with no special case.
    /// </summary>
    public sealed class CounterReserveStrategy : IPlayerStrategy
    {
        public string Name => "counter-reserve";

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            List<UnitState> targets = BattleQueries.AttackableTargets(state, unit);

            if (targets.Count > 0 && unit.Ap >= unit.Def.AttackApCost
                && unit.Ap - unit.Def.AttackApCost >= unit.Def.CounterApCost)
            {
                // Spawn order, like the base strategy: this instrument is about
                // the AP economy, and any cleverness about WHO to hit would
                // contaminate the only thing it is measuring.
                return new AttackCommand(unit.Id, targets[0].Id);
            }

            // Wait keeps the unspent AP visible and ends the activation. AP resets
            // only for the incoming faction, so what is kept here is still held
            // when the enemy phase arrives.
            return new WaitCommand(unit.Id);
        }
    }

    /// <summary>
    /// "Charge": walk at the nearest enemy and swing, ignoring exposure entirely.
    /// The control group for M5.
    /// </summary>
    public sealed class AggressiveStrategy : IPlayerStrategy
    {
        public string Name => "charge";

        public ICommand DecideNext(BattleState state, UnitState unit, DeterministicRandom rng)
        {
            List<UnitState> targets = BattleQueries.AttackableTargets(state, unit);
            if (targets.Count > 0 && unit.Ap >= unit.Def.AttackApCost)
                return new AttackCommand(unit.Id, targets[0].Id);

            // A Reach objective replaces "nearest enemy" as the thing to run at;
            // otherwise this strategy would ignore the clock and always lose.
            Coord? anchor = ObjectiveAnchor.For(state);
            Coord goal;

            if (anchor.HasValue)
            {
                goal = anchor.Value;
            }
            else
            {
                UnitState nearest = null;
                int bestDistance = int.MaxValue;
                foreach (UnitState e in state.LivingUnitsOf(unit.Faction.Opponent()))
                {
                    int d = state.Map.Topology.Distance(unit.Position, e.Position);
                    if (d < bestDistance) { bestDistance = d; nearest = e; }
                }
                if (nearest == null) return new WaitCommand(unit.Id);
                goal = nearest.Position;
            }

            ReachabilityMap reach = MovementCalculator.ComputeFor(state, unit);
            // Path distance, so a wall between us and the goal is actually noticed.
            Dictionary<Coord, int> field = MovementCalculator.TerrainDistanceField(state.Map, goal);

            Coord best = unit.Position;
            int bestScore = int.MaxValue;

            for (int i = 0; i < reach.ReachableCells.Count; i++)
            {
                Coord cell = reach.ReachableCells[i];
                int score = MovementCalculator.DistanceIn(field, cell) * 10 + reach.CostTo(cell);
                if (score < bestScore) { bestScore = score; best = cell; }
            }

            if (best != unit.Position)
            {
                Coord[] path = reach.PathTo(best);
                if (path != null && path.Length > 0) return new MoveCommand(unit.Id, path);
            }

            return new WaitCommand(unit.Id);
        }
    }
}
