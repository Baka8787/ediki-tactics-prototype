using System.Collections.Generic;

namespace Ediki.Core
{
    public readonly struct ExecuteResult
    {
        public readonly BattleState State;
        public readonly EffectLog Log;
        public readonly bool Ok;
        public readonly string RejectReason;

        public ExecuteResult(BattleState state, EffectLog log, bool ok, string rejectReason)
        {
            State = state;
            Log = log;
            Ok = ok;
            RejectReason = rejectReason;
        }

        public static ExecuteResult Reject(BattleState unchanged, string reason)
        {
            return new ExecuteResult(unchanged, EffectLog.Empty, false, reason);
        }

        public static ExecuteResult Accept(BattleState next, EffectLog log)
        {
            return new ExecuteResult(next, log, true, null);
        }
    }

    /// <summary>
    /// The single funnel: Command -> Validate -> Effect[] -> Apply (ADR-0002).
    ///
    /// Execute is a pure function. It never mutates the BattleState it is given;
    /// it returns a new one. A rejected command produces no effects and leaves the
    /// caller's state byte-identical (R-CMD-04 / A2).
    ///
    /// The AI goes through this same funnel. There is no second rule implementation.
    /// </summary>
    public static class BattleSimulator
    {
        /// <summary>Opens the battle: emits the first TurnStarted and runs initial perception.</summary>
        public static ExecuteResult Begin(BattleState state)
        {
            BattleState next = state.Clone();
            EffectLog log = new EffectLog();

            log.Add(new TurnStarted(next.TurnIndex, next.CurrentFaction));
            CheckActivations(next, log, -1);

            return ExecuteResult.Accept(next, log);
        }

        public static ExecuteResult Execute(BattleState state, ICommand command)
        {
            if (command == null) return ExecuteResult.Reject(state, "Command is null.");

            if (state.Outcome != BattleOutcome.InProgress)
                return ExecuteResult.Reject(state, "Battle has already ended (" + state.Outcome + ").");

            if (command is MoveCommand move) return ExecuteMove(state, move);
            if (command is AttackCommand attack) return ExecuteAttack(state, attack);
            if (command is GuardCommand guard) return ExecuteGuard(state, guard);
            if (command is TauntCommand taunt) return ExecuteTaunt(state, taunt);
            if (command is SlowCommand slow) return ExecuteSlow(state, slow);
            if (command is PushCommand push) return ExecutePush(state, push);
            if (command is ArmorBreakCommand armorBreak) return ExecuteArmorBreak(state, armorBreak);
            if (command is PurifyCommand purify) return ExecutePurify(state, purify);
            if (command is RestCommand rest) return ExecuteRest(state, rest);
            if (command is WaitCommand wait) return ExecuteWait(state, wait);
            if (command is EndTurnCommand endTurn) return ExecuteEndTurn(state, endTurn);

            return ExecuteResult.Reject(state, "Unknown command type " + command.GetType().Name + ".");
        }

        // ---------------------------------------------------------------- Move

        private static ExecuteResult ExecuteMove(BattleState state, MoveCommand cmd)
        {
            UnitState unit = state.FindUnit(cmd.UnitId);
            string reason = ValidateActor(state, unit);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            int cost = MovementCalculator.ValidatePath(state, unit, cmd.Path, out reason);
            if (cost < 0) return ExecuteResult.Reject(state, reason);

            BattleState next = state.Clone();
            UnitState u = next.FindUnit(cmd.UnitId);
            EffectLog log = new EffectLog();

            Coord from = u.Position;
            Coord to = cmd.Path[cmd.Path.Length - 1];

            u.Ap -= cost;
            log.Add(new ApSpent(u.Id, cost, u.Ap));

            u.Position = to;
            u.MoveUsedThisTurn += cmd.Path.Length;
            log.Add(new UnitMoved(u.Id, from, to, cmd.Path));

            // Walking into a pit kills exactly as being shoved into one does. In
            // practice nothing reaches this: MovementCalculator refuses lethal
            // cells as destinations, so no pathfinder and no legal-command list
            // ever offers the move. It is here because a hand-built MoveCommand
            // bypasses the pathfinder, and "the rule only holds when you asked
            // nicely" is not a rule.
            if (ResolveHazard(next, log, u))
                return ExecuteResult.Accept(next, log);

            // Perception happens after the move: activation is caused by the move.
            CheckActivations(next, log, u.Id);
            CheckReachObjective(next, log);

            return ExecuteResult.Accept(next, log);
        }

        // ------------------------------------------------------------- Purify

        private static ExecuteResult ExecutePurify(BattleState state, PurifyCommand cmd)
        {
            UnitState unit = state.FindUnit(cmd.UnitId);
            string reason = ValidateActor(state, unit);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (!unit.Def.CanPurify)
                return ExecuteResult.Reject(state, "Unit " + unit.Id + " cannot purify.");
            string budgetPurify = ValidateSkillBudget(state, unit, "purify");
            if (budgetPurify != null) return ExecuteResult.Reject(state, budgetPurify);

            if (unit.Ap < unit.Def.PurifyApCost)
                return ExecuteResult.Reject(state, "Purify costs " + unit.Def.PurifyApCost +
                                                   " AP but unit has " + unit.Ap + ".");

            BattleState next = state.Clone();
            UnitState u = next.FindUnit(cmd.UnitId);
            EffectLog log = new EffectLog();

            u.Ap -= u.Def.PurifyApCost;
            u.SkillUsesThisRound++;
            log.Add(new ApSpent(u.Id, u.Def.PurifyApCost, u.Ap));

            // Manhattan radius, so radius 2 is the thirteen-cell diamond the GDD
            // describes as "九宮格上下左右＋1格" rather than a 5x5 block.
            int radius = u.Def.PurifyRadius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                int span = radius - (dy < 0 ? -dy : dy);
                for (int dx = -span; dx <= span; dx++)
                {
                    Coord cell = new Coord(u.Position.X + dx, u.Position.Y + dy);
                    if (next.ContaminationAt(cell) <= 0) continue;

                    int level = next.AddContamination(cell, -1);
                    log.Add(new TerrainContaminated(cell, level, -1));
                }
            }

            return ExecuteResult.Accept(next, log);
        }

        /// <summary>
        /// 穢氣滲流: every contaminating unit thickens the ground around itself.
        /// Runs once per round, on the enemy end-of-turn, so a round is one tick
        /// no matter how many phases it contains.
        /// </summary>
        private static void SpreadContamination(BattleState state, EffectLog log)
        {
            if (state.Outcome != BattleOutcome.InProgress) return;

            foreach (UnitState u in state.Units)
            {
                if (!u.IsAlive || !u.Def.Contaminates) continue;

                int radius = u.Def.ContaminateRadius;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int span = radius - (dy < 0 ? -dy : dy);
                    for (int dx = -span; dx <= span; dx++)
                    {
                        Coord cell = new Coord(u.Position.X + dx, u.Position.Y + dy);
                        if (!state.Map.Contains(cell) || !state.Map.IsPassable(cell)) continue;

                        int before = state.ContaminationAt(cell);
                        int level = state.AddContamination(cell, u.Def.ContaminatePerTurn);
                        if (level != before) log.Add(new TerrainContaminated(cell, level, u.Id));
                    }
                }
            }
        }

        /// <summary>
        /// GDD: contamination weakens humans and strengthens oni. Implemented as a
        /// damage multiplier on both halves rather than as a status, because the
        /// prototype has no status system and inventing one here would be a much
        /// larger change than the mechanic being tested.
        ///
        /// PROTOTYPE BASELINE, needs ratifying: 10% per level, applied to the
        /// attacker when it is an enemy standing on contamination, and to the
        /// defender when it is a player standing on contamination.
        /// </summary>
        public const int ContaminationDamagePercentPerLevel = 10;

        private static int ApplyContamination(BattleState state, UnitState attacker, UnitState target, int damage)
        {
            int percent = 100;

            if (attacker.Faction == Faction.Enemy)
                percent += ContaminationDamagePercentPerLevel * state.ContaminationAt(attacker.Position);

            if (target.Faction == Faction.Player)
                percent += ContaminationDamagePercentPerLevel * state.ContaminationAt(target.Position);

            if (percent == 100) return damage;

            int scaled = damage * percent / 100;
            return scaled < 1 ? 1 : scaled;
        }

        // -------------------------------------------------------------- Attack

        private static ExecuteResult ExecuteAttack(BattleState state, AttackCommand cmd)
        {
            UnitState attacker = state.FindUnit(cmd.AttackerId);
            string reason = ValidateActor(state, attacker);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            UnitState target = state.FindUnit(cmd.TargetId);
            if (target == null) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " does not exist.");
            if (!target.IsAlive) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " is already dead.");
            if (target.Faction == attacker.Faction)
                return ExecuteResult.Reject(state, "Cannot attack a unit of the same faction.");

            int distance = state.Map.Topology.Distance(attacker.Position, target.Position);
            if (distance > attacker.Def.AttackRange)
                return ExecuteResult.Reject(state, "Target is " + distance + " away, attack range is " + attacker.Def.AttackRange + ".");

            int apCost = attacker.Def.AttackApCost;
            if (attacker.Ap < apCost)
                return ExecuteResult.Reject(state, "Attack costs " + apCost + " AP but unit has " + attacker.Ap + ".");

            // The cap AP alone cannot express: carry-over means a 5 AP attacker can
            // reach 10 AP and swing twice out of a cost priced for one.
            if (!state.CanAttackAgain(attacker))
                return ExecuteResult.Reject(state, "Unit " + attacker.Id + " has used its " +
                                                   attacker.Def.AttacksPerRound + " attack(s) this round.");

            BattleState next = state.Clone();
            UnitState a = next.FindUnit(cmd.AttackerId);
            UnitState t = next.FindUnit(cmd.TargetId);
            EffectLog log = new EffectLog();

            a.Ap -= apCost;
            a.AttacksThisRound++;
            log.Add(new ApSpent(a.Id, apCost, a.Ap));

            // OD-05 baseline: deterministic hit. Kept as an explicit effect so the
            // presentation layer already has the branch if RNG ever comes back.
            log.Add(new AttackResolved(a.Id, t.Id, true));

            // AtkOnRound is the base ATK unless the unit is a growth type, so a
            // roster with no growth resolves exactly as it always has.
            // EffectiveDef, not Def.Def: 破甲 lives on the target and this is the
            // only place the reduction can be honoured. Unbroken armour returns
            // Def.Def unchanged, so a battle without 破甲 resolves as it always did.
            int damage = BattleRules.ComputeDamage(next.EffectiveAtk(a), next.EffectiveDef(t),
                                                   t.IsGuarding, next.Rules.Damage);
            damage = ApplyContamination(next, a, t, damage);
            t.Hp -= damage;
            if (t.Hp < 0) t.Hp = 0;
            log.Add(new HpChanged(t.Id, -damage, t.Hp, HpChangeCause.Attack));

            if (!t.IsAlive)
            {
                log.Add(new UnitDied(t.Id, t.Faction));
                CheckBattleEnd(next, log);
                return ExecuteResult.Accept(next, log);
            }

            ResolveCounterAttack(next, log, a, t);

            return ExecuteResult.Accept(next, log);
        }

        // --------------------------------------------------------------- Guard

        private static ExecuteResult ExecuteGuard(BattleState state, GuardCommand cmd)
        {
            UnitState unit = state.FindUnit(cmd.UnitId);
            string reason = ValidateActor(state, unit);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (unit.IsGuarding) return ExecuteResult.Reject(state, "Unit is already guarding.");

            int apCost = unit.Def.GuardApCost;
            if (unit.Ap < apCost)
                return ExecuteResult.Reject(state, "Guard costs " + apCost + " AP but unit has " + unit.Ap + ".");

            BattleState next = state.Clone();
            UnitState u = next.FindUnit(cmd.UnitId);
            EffectLog log = new EffectLog();

            u.Ap -= apCost;
            log.Add(new ApSpent(u.Id, apCost, u.Ap));

            u.IsGuarding = true;
            log.Add(new GuardApplied(u.Id, BattleRules.GuardDamagePercent));

            return ExecuteResult.Accept(next, log);
        }

        // ------------------------------------------------------- Control kit

        // Every control status lasts exactly one phase of the side it affects —
        // see BattleState.StatusExpiryTurnFor. Longer durations are a second
        // variable and this round is only measuring whether the verb matters.

        private static ExecuteResult ExecuteTaunt(BattleState state, TauntCommand cmd)
        {
            UnitState unit = state.FindUnit(cmd.UnitId);
            string reason = ValidateActor(state, unit);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (!unit.Def.CanTaunt) return ExecuteResult.Reject(state, "Unit " + unit.Id + " cannot taunt.");
            if (state.IsTaunting(unit)) return ExecuteResult.Reject(state, "Unit " + unit.Id + " is already taunting.");
            string budgetTaunt = ValidateSkillBudget(state, unit, "taunt");
            if (budgetTaunt != null) return ExecuteResult.Reject(state, budgetTaunt);

            if (unit.Ap < unit.Def.TauntApCost)
                return ExecuteResult.Reject(state, "Taunt costs " + unit.Def.TauntApCost +
                                                   " AP but unit has " + unit.Ap + ".");

            BattleState next = state.Clone();
            UnitState u = next.FindUnit(cmd.UnitId);
            EffectLog log = new EffectLog();

            u.Ap -= u.Def.TauntApCost;
            u.SkillUsesThisRound++;
            log.Add(new ApSpent(u.Id, u.Def.TauntApCost, u.Ap));

            // The taunt acts on the OPPOSING side's target choice, so it has to
            // survive that side's next phase, not this unit's.
            u.TauntingUntilTurn = next.StatusExpiryTurnFor(u.Faction.Opponent());
            log.Add(new TauntApplied(u.Id, u.Def.TauntRadius, u.TauntingUntilTurn));

            return ExecuteResult.Accept(next, log);
        }

        private static ExecuteResult ExecuteSlow(BattleState state, SlowCommand cmd)
        {
            UnitState actor = state.FindUnit(cmd.ActorId);
            string reason = ValidateActor(state, actor);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (!actor.Def.CanSlow) return ExecuteResult.Reject(state, "Unit " + actor.Id + " cannot slow.");

            UnitState target = state.FindUnit(cmd.TargetId);
            if (target == null) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " does not exist.");
            if (!target.IsAlive) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " is already dead.");
            if (target.Faction == actor.Faction)
                return ExecuteResult.Reject(state, "Cannot slow a unit of the same faction.");

            // Refusing to re-apply matters: without it, spending every action on
            // slow would be a way to burn AP that looks productive, and the metric
            // for "did the player have something to do" would count it.
            if (state.IsSlowed(target)) return ExecuteResult.Reject(state, "Target " + target.Id + " is already slowed.");

            int distance = state.Map.Topology.Distance(actor.Position, target.Position);
            if (distance > actor.Def.SlowRange)
                return ExecuteResult.Reject(state, "Target is " + distance + " away, slow range is " +
                                                   actor.Def.SlowRange + ".");
            string budgetSlow = ValidateSkillBudget(state, actor, "slow");
            if (budgetSlow != null) return ExecuteResult.Reject(state, budgetSlow);

            if (actor.Ap < actor.Def.SlowApCost)
                return ExecuteResult.Reject(state, "Slow costs " + actor.Def.SlowApCost +
                                                   " AP but unit has " + actor.Ap + ".");

            BattleState next = state.Clone();
            UnitState a = next.FindUnit(cmd.ActorId);
            UnitState t = next.FindUnit(cmd.TargetId);
            EffectLog log = new EffectLog();

            a.Ap -= a.Def.SlowApCost;
            a.SkillUsesThisRound++;
            log.Add(new ApSpent(a.Id, a.Def.SlowApCost, a.Ap));

            t.SlowedUntilTurn = next.StatusExpiryTurnFor(t.Faction);
            log.Add(new SlowApplied(t.Id, t.SlowedUntilTurn));

            return ExecuteResult.Accept(next, log);
        }

        private static ExecuteResult ExecuteArmorBreak(BattleState state, ArmorBreakCommand cmd)
        {
            UnitState actor = state.FindUnit(cmd.ActorId);
            string reason = ValidateActor(state, actor);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (!actor.Def.CanArmorBreak)
                return ExecuteResult.Reject(state, "Unit " + actor.Id + " cannot armour-break.");

            UnitState target = state.FindUnit(cmd.TargetId);
            if (target == null) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " does not exist.");
            if (!target.IsAlive) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " is already dead.");
            if (target.Faction == actor.Faction)
                return ExecuteResult.Reject(state, "Cannot armour-break a unit of the same faction.");

            // No stacking, same as Slow: re-applying would be a way to spend AP
            // that reads as productive to the action-mix metric while buying
            // nothing, and "the player had something to do" would count it.
            if (state.IsArmorBroken(target))
                return ExecuteResult.Reject(state, "Target " + target.Id + " already has broken armour.");

            int distance = state.Map.Topology.Distance(actor.Position, target.Position);
            if (distance > actor.Def.ArmorBreakRange)
                return ExecuteResult.Reject(state, "Target is " + distance + " away, armour-break range is " +
                                                   actor.Def.ArmorBreakRange + ".");
            string budgetArmorBreak = ValidateSkillBudget(state, actor, "armour-break");
            if (budgetArmorBreak != null) return ExecuteResult.Reject(state, budgetArmorBreak);

            if (actor.Ap < actor.Def.ArmorBreakApCost)
                return ExecuteResult.Reject(state, "Armour-break costs " + actor.Def.ArmorBreakApCost +
                                                   " AP but unit has " + actor.Ap + ".");

            BattleState next = state.Clone();
            UnitState a = next.FindUnit(cmd.ActorId);
            UnitState t = next.FindUnit(cmd.TargetId);
            EffectLog log = new EffectLog();

            a.Ap -= a.Def.ArmorBreakApCost;
            a.SkillUsesThisRound++;
            log.Add(new ApSpent(a.Id, a.Def.ArmorBreakApCost, a.Ap));

            t.ArmorBrokenAmount = a.Def.ArmorBreakAmount;
            t.ArmorBrokenUntilTurn = next.StatusExpiryTurnFor(t.Faction);
            log.Add(new ArmorBreakApplied(t.Id, t.ArmorBrokenAmount, t.ArmorBrokenUntilTurn));

            return ExecuteResult.Accept(next, log);
        }

        /// <summary>
        /// Kills a unit that has come to rest on lethal terrain, if it has.
        /// Returns true when it did.
        ///
        /// Called after the unit's position is already final. The engine relocates
        /// in one step (intermediate path cells only price the move), so "where it
        /// ended up" is the only well-defined place to ask — checking every crossed
        /// cell would invent a step-by-step model the rest of the rule layer does
        /// not have.
        /// </summary>
        private static bool ResolveHazard(BattleState state, EffectLog log, UnitState unit)
        {
            if (unit == null || !unit.IsAlive) return false;
            if (!state.Map.IsLethal(unit.Position)) return false;

            log.Add(new UnitFellIntoHazard(unit.Id, unit.Position, state.Map.TerrainAt(unit.Position).Name));

            // Straight to zero rather than through ComputeDamage: a pit is not an
            // attack, it has no ATK, and routing it through the damage model would
            // let Guard halve it.
            int lost = unit.Hp;
            unit.Hp = 0;
            log.Add(new HpChanged(unit.Id, -lost, 0, HpChangeCause.Terrain));
            log.Add(new UnitDied(unit.Id, unit.Faction));

            CheckBattleEnd(state, log);
            return true;
        }

        private static ExecuteResult ExecutePush(BattleState state, PushCommand cmd)
        {
            UnitState actor = state.FindUnit(cmd.ActorId);
            string reason = ValidateActor(state, actor);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (!actor.Def.CanPush) return ExecuteResult.Reject(state, "Unit " + actor.Id + " cannot push.");

            UnitState target = state.FindUnit(cmd.TargetId);
            if (target == null) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " does not exist.");
            if (!target.IsAlive) return ExecuteResult.Reject(state, "Target " + cmd.TargetId + " is already dead.");
            if (target.Faction == actor.Faction)
                return ExecuteResult.Reject(state, "Cannot push a unit of the same faction.");
            if (target.Def.ImmuneToPush)
                return ExecuteResult.Reject(state, "Unit " + target.Id + " is immune to being pushed.");

            int distance = state.Map.Topology.Distance(actor.Position, target.Position);
            if (distance > actor.Def.PushRange)
                return ExecuteResult.Reject(state, "Target is " + distance + " away, push range is " +
                                                   actor.Def.PushRange + ".");
            string budgetPush = ValidateSkillBudget(state, actor, "push");
            if (budgetPush != null) return ExecuteResult.Reject(state, budgetPush);

            if (actor.Ap < actor.Def.PushApCost)
                return ExecuteResult.Reject(state, "Push costs " + actor.Def.PushApCost +
                                                   " AP but unit has " + actor.Ap + ".");

            Coord destination;
            if (!TryPushDestination(actor.Position, target.Position, out destination))
                return ExecuteResult.Reject(state, "Push has no straight line — actor and target share no axis.");

            // Blocked pushes are rejected rather than silently fizzling: spending
            // AP for no visible change is the worst possible feedback, and it would
            // also make "player had a legal action" true when it was useless.
            if (!state.CanUnitEnter(destination))
                return ExecuteResult.Reject(state, "Push destination " + destination + " is blocked.");

            BattleState next = state.Clone();
            UnitState a = next.FindUnit(cmd.ActorId);
            UnitState t = next.FindUnit(cmd.TargetId);
            EffectLog log = new EffectLog();

            a.Ap -= a.Def.PushApCost;
            a.SkillUsesThisRound++;
            log.Add(new ApSpent(a.Id, a.Def.PushApCost, a.Ap));

            Coord from = t.Position;
            t.Position = destination;
            log.Add(new UnitPushed(t.Id, from, destination, a.Id));

            // Shoving something into a pit is the entire reason lethal terrain is
            // passable. Resolved before activation checks: a unit that just died
            // has no perception to update, and CheckBattleEnd may already have
            // ended the battle.
            if (ResolveHazard(next, log, t))
                return ExecuteResult.Accept(next, log);

            // Being shoved into someone's reach wakes them, exactly as walking
            // there would. Perception is about where units ARE, not how they got there.
            CheckActivations(next, log, a.Id);

            return ExecuteResult.Accept(next, log);
        }

        /// <summary>
        /// One cell further along the actor-to-target line. Four-neighbour grid, so
        /// the two must share a row or a column; anything else has no "away".
        ///
        /// Public because callers that only want to know whether a push is worth
        /// offering (legal-command enumeration, strategies) must be able to ask
        /// without executing one.
        /// </summary>
        public static bool TryPushDestination(Coord from, Coord target, out Coord destination)
        {
            int dx = target.X - from.X;
            int dy = target.Y - from.Y;
            destination = default;

            if (dx != 0 && dy != 0) return false;
            if (dx == 0 && dy == 0) return false;

            int stepX = dx == 0 ? 0 : (dx > 0 ? 1 : -1);
            int stepY = dy == 0 ? 0 : (dy > 0 ? 1 : -1);
            destination = new Coord(target.X + stepX, target.Y + stepY);
            return true;
        }

        // ---------------------------------------------------------------- Rest

        private static ExecuteResult ExecuteRest(BattleState state, RestCommand cmd)
        {
            UnitState unit = state.FindUnit(cmd.UnitId);
            string reason = ValidateActor(state, unit);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            if (!unit.Def.CanRest) return ExecuteResult.Reject(state, "Unit " + unit.Id + " cannot rest.");

            int apCost = unit.Def.RestApCost;
            if (unit.Ap < apCost)
                return ExecuteResult.Reject(state, "Rest costs " + apCost + " AP but unit has " + unit.Ap + ".");

            BattleState next = state.Clone();
            UnitState u = next.FindUnit(cmd.UnitId);
            EffectLog log = new EffectLog();

            u.Ap -= apCost;
            log.Add(new ApSpent(u.Id, apCost, u.Ap));

            int healed = u.Def.RestHealAmount;
            if (u.Hp + healed > u.Def.MaxHp) healed = u.Def.MaxHp - u.Hp;
            if (healed > 0)
            {
                u.Hp += healed;
                log.Add(new HpChanged(u.Id, healed, u.Hp, HpChangeCause.Rest));
            }
            log.Add(new UnitRested(u.Id, healed));

            // Resting stands the unit down for the turn. That is the price — it is
            // not a free top-up between attacks.
            u.HasEndedTurn = true;
            log.Add(new UnitWaited(u.Id, u.Ap));

            return ExecuteResult.Accept(next, log);
        }

        // ---------------------------------------------------------------- Wait

        private static ExecuteResult ExecuteWait(BattleState state, WaitCommand cmd)
        {
            UnitState unit = state.FindUnit(cmd.UnitId);
            string reason = ValidateActor(state, unit);
            if (reason != null) return ExecuteResult.Reject(state, reason);

            BattleState next = state.Clone();
            UnitState u = next.FindUnit(cmd.UnitId);
            EffectLog log = new EffectLog();

            // AP is deliberately NOT zeroed: the unused amount is the M2 metric.
            u.HasEndedTurn = true;
            log.Add(new UnitWaited(u.Id, u.Ap));

            return ExecuteResult.Accept(next, log);
        }

        // ------------------------------------------------------------ End turn

        private static ExecuteResult ExecuteEndTurn(BattleState state, EndTurnCommand cmd)
        {
            if (cmd.Faction != state.CurrentFaction)
                return ExecuteResult.Reject(state, "It is " + state.CurrentFaction + "'s turn, not " + cmd.Faction + "'s.");

            BattleState next = state.Clone();
            EffectLog log = new EffectLog();

            log.Add(new TurnEnded(next.TurnIndex, next.CurrentFaction));

            // Status duration belongs to the affected faction's phase.
            DecrementStatuses(next, log, next.CurrentFaction);

            Faction upcoming = next.CurrentFaction.Opponent();
            if (upcoming == Faction.Player)
            {
                next.TurnIndex++;   // a full round is Player phase + Enemy phase

                // 穢氣滲流 ticks once per ROUND, on the boundary back to the
                // player. Ticking per phase would double its rate for no reason
                // the GDD states.
                SpreadContamination(next, log);
            }

            next.CurrentFaction = upcoming;

            // Start of the incoming faction's phase: guard expires, AP resets.
            for (int i = 0; i < next.Units.Count; i++)
            {
                UnitState u = next.Units[i];
                if (u.Faction != upcoming || !u.IsAlive) continue;

                TickStatusDamage(next, log, u);
                if (!u.IsAlive) continue;

                if (u.IsGuarding)
                {
                    u.IsGuarding = false;
                    log.Add(new GuardExpired(u.Id));
                }

                // Control statuses need no tick here: they are turn stamps compared
                // against TurnIndex, so they lapse on their own.

                // Unspent AP carries over, capped at MaxAp (OD-21).
                // Regen below the cap is what makes holding AP back meaningful.
                int before = u.Ap;
                int raw = before + u.Def.ApRegen;
                u.Ap = raw > u.Def.MaxAp ? u.Def.MaxAp : raw;

                u.HasEndedTurn = false;
                u.HasCounteredThisRound = false;
                u.MoveUsedThisTurn = 0;
                u.AttacksThisRound = 0;
                u.SkillUsesThisRound = 0;
                log.Add(new ApReset(u.Id, u.Ap, u.Ap - before, raw - u.Ap));
            }

            log.Add(new TurnStarted(next.TurnIndex, next.CurrentFaction));

            SpawnReinforcements(next, log, upcoming);
            CheckActivations(next, log, -1);
            CheckTimedObjective(next, log);

            return ExecuteResult.Accept(next, log);
        }

        // ------------------------------------------------------------- Helpers

        private static void TickStatusDamage(BattleState state, EffectLog log, UnitState unit)
        {
            if (unit.Statuses == null) return;
            int total = 0;
            for (int i = 0; i < unit.Statuses.Count; i++)
            {
                ActiveStatus status = unit.Statuses[i];
                if (status.Kind != StatusKind.Poison && status.Kind != StatusKind.Bleed) continue;
                int damage = unit.Def.MaxHp * status.Magnitude / 100;
                total += damage < 1 ? 1 : damage;
            }
            if (total <= 0) return;
            int before = unit.Hp;
            unit.Hp -= total;
            if (unit.Hp < 0) unit.Hp = 0;
            log.Add(new HpChanged(unit.Id, unit.Hp - before, unit.Hp, HpChangeCause.Status));
            if (!unit.IsAlive)
            {
                log.Add(new UnitDied(unit.Id, unit.Faction));
                CheckBattleEnd(state, log);
            }
        }

        private static void DecrementStatuses(BattleState state, EffectLog log, Faction faction)
        {
            for (int i = 0; i < state.Units.Count; i++)
            {
                UnitState unit = state.Units[i];
                if (unit.Faction != faction || unit.Statuses == null) continue;
                for (int s = unit.Statuses.Count - 1; s >= 0; s--)
                {
                    ActiveStatus old = unit.Statuses[s];
                    int remaining = old.RemainingPhases - 1;
                    if (remaining <= 0)
                    {
                        unit.Statuses.RemoveAt(s);
                        log.Add(new StatusExpired(unit.Id, old.Kind));
                    }
                    else unit.Statuses[s] = new ActiveStatus(old.Kind, remaining, old.Magnitude);
                }
                if (unit.Statuses.Count == 0) unit.Statuses = null;
            }
        }

        /// <summary>
        /// Null when this unit may still activate a skill this round, otherwise the
        /// rejection reason.
        ///
        /// One counter covers the whole kit, so a unit that ever carries two skills
        /// spends the same allowance on either. That is the intended reading of
        /// "activations per round": the limit is on how often a unit does something
        /// special, not on how many different special things it owns.
        /// </summary>
        private static string ValidateSkillBudget(BattleState state, UnitState unit, string verb)
        {
            if (state.CanUseSkillAgain(unit)) return null;
            return "Unit " + unit.Id + " has used its " + unit.Def.SkillUsesPerRound +
                   " skill activation(s) this round (" + verb + ").";
        }

        private static string ValidateActor(BattleState state, UnitState unit)
        {
            if (unit == null) return "Unit does not exist.";
            if (!unit.IsAlive) return "Unit " + unit.Id + " is dead.";
            if (unit.Faction != state.CurrentFaction)
                return "Unit " + unit.Id + " belongs to " + unit.Faction + " but it is " + state.CurrentFaction + "'s turn.";
            if (unit.HasEndedTurn) return "Unit " + unit.Id + " has already ended its turn.";
            return null;
        }

        /// <summary>
        /// Perception (OD-10): an enemy latches on once any player unit stands inside
        /// its threat range. Per-unit, not per-faction — see UnitActivated.
        /// </summary>
        private static void CheckActivations(BattleState state, EffectLog log, int triggeredByUnitId)
        {
            List<UnitState> pending = new List<UnitState>();
            for (int i = 0; i < state.Units.Count; i++)
            {
                UnitState u = state.Units[i];
                if (u.IsAlive && u.Faction == Faction.Enemy && !u.IsActivated) pending.Add(u);
            }
            if (pending.Count == 0) return;

            for (int i = 0; i < pending.Count; i++)
            {
                UnitState enemy = pending[i];
                HashSet<Coord> threat = BattleQueries.ThreatRange(state, enemy);

                foreach (UnitState player in state.LivingUnitsOf(Faction.Player))
                {
                    if (!threat.Contains(player.Position)) continue;
                    enemy.IsActivated = true;
                    log.Add(new UnitActivated(enemy.Id, triggeredByUnitId >= 0 ? triggeredByUnitId : player.Id));
                    break;
                }
            }
        }

        /// <summary>
        /// JA2-style reserve-AP counter (see UnitDef.CounterApCost).
        ///
        /// The defender strikes back if it kept enough AP unspent, the attacker is
        /// in its reach, and it has not already countered this round. Countering
        /// consumes the reserve, so it cannot be repeated for free.
        /// </summary>
        private static void ResolveCounterAttack(BattleState state, EffectLog log,
                                                 UnitState attacker, UnitState defender)
        {
            if (!defender.Def.CanCounter) return;
            if (defender.HasCounteredThisRound) return;
            if (defender.Ap < defender.Def.CounterApCost) return;
            if (state.Map.Topology.Distance(defender.Position, attacker.Position) > defender.Def.AttackRange) return;

            defender.Ap -= defender.Def.CounterApCost;
            defender.HasCounteredThisRound = true;
            log.Add(new ApSpent(defender.Id, defender.Def.CounterApCost, defender.Ap));
            log.Add(new CounterAttacked(defender.Id, attacker.Id));

            // EffectiveDef here too: a counter is an attack, and armour that is
            // broken is broken no matter which direction the blow comes from.
            int damage = BattleRules.ComputeDamage(defender.Def.AtkOnRound(state.TurnIndex),
                                                   state.EffectiveDef(attacker), attacker.IsGuarding,
                                                   state.Rules.Damage);
            damage = ApplyContamination(state, defender, attacker, damage);
            attacker.Hp -= damage;
            if (attacker.Hp < 0) attacker.Hp = 0;
            log.Add(new HpChanged(attacker.Id, -damage, attacker.Hp, HpChangeCause.Counter));

            if (!attacker.IsAlive)
            {
                log.Add(new UnitDied(attacker.Id, attacker.Faction));
                CheckBattleEnd(state, log);
            }
        }

        /// <summary>
        /// Wave spawns declared in encounter data.
        ///
        /// 🔴 A blocked spawn used to be a battle-ending defect, not an
        /// inconvenience. The old code skipped the wave when its cell was taken
        /// and only ever retried on `r.Turn == TurnIndex`, so a unit — either
        /// side's, `CanUnitEnter` does not care — standing on the cell for that
        /// one phase left `Spawned` false forever. `HasPendingReinforcements`
        /// only looks at `Spawned`, and victory is gated on it, so the player
        /// could kill everything and the battle would never end. R-WIN-04 says
        /// the rule layer has no round cap and no draw, so there was no way out.
        ///
        /// Two independent guards, because either alone leaves a hole:
        ///   1. RETRY on any later turn (`r.Turn > TurnIndex` skips, not `!=`).
        ///      This is what the old comment claimed the code did.
        ///   2. RELOCATE to the nearest free cell when the declared one is still
        ///      taken. Retrying alone still deadlocks against something that
        ///      never moves — a MOVE 0 unit, or a player parked on the spawn
        ///      point, which is a legal and rather obvious way to break a battle.
        /// </summary>
        private static void SpawnReinforcements(BattleState state, EffectLog log, Faction faction)
        {
            IReadOnlyList<PendingReinforcement> pending = state.Reinforcements;
            for (int i = 0; i < pending.Count; i++)
            {
                PendingReinforcement r = pending[i];

                // Not yet due. Anything already due stays due until it lands.
                if (r.Spawned || r.Faction != faction || r.Turn > state.TurnIndex) continue;

                Coord cell;
                if (!TryFindSpawnCell(state, r.Position, out cell)) continue;   // retried next round

                r.Spawned = true;
                UnitState unit = state.AddUnit(r.Def, r.Faction, cell);
                log.Add(new UnitSpawned(unit.Id, unit.Faction, unit.Position));
            }
        }

        /// <summary>
        /// The declared cell, or the nearest free one to it.
        ///
        /// Breadth-first over <see cref="IGridTopology.Neighbors"/>, whose order
        /// A6 pins, so the cell chosen is the same on every machine and every run
        /// (determinism rule 2). Lethal cells are excluded — arriving dead is not
        /// a reinforcement, and it would hand the defender free kills for standing
        /// in the right place.
        ///
        /// Bounded on purpose. A wave that cannot land anywhere near where it was
        /// declared should wait rather than teleport across the map: the encounter
        /// author put it there for a reason, and "somewhere else entirely" is a
        /// worse answer than "a round later".
        /// </summary>
        private static bool TryFindSpawnCell(BattleState state, Coord declared, out Coord cell)
        {
            const int MaxDisplacement = 2;

            cell = declared;
            if (state.CanUnitEnter(declared) && !state.Map.IsLethal(declared)) return true;

            List<Coord> frontier = new List<Coord> { declared };
            HashSet<Coord> seen = new HashSet<Coord> { declared };

            for (int ring = 0; ring < MaxDisplacement; ring++)
            {
                List<Coord> next = new List<Coord>();
                for (int i = 0; i < frontier.Count; i++)
                {
                    foreach (Coord n in state.Map.Topology.Neighbors(frontier[i]))
                    {
                        if (!seen.Add(n)) continue;
                        if (state.CanUnitEnter(n) && !state.Map.IsLethal(n))
                        {
                            cell = n;
                            return true;
                        }
                        next.Add(n);
                    }
                }
                frontier = next;
            }

            return false;
        }

        /// <summary>Reach objectives resolve the instant someone stands on the target cell.</summary>
        private static void CheckReachObjective(BattleState state, EffectLog log)
        {
            if (state.Outcome != BattleOutcome.InProgress) return;
            if (state.Objective.Kind != ObjectiveKind.Reach) return;

            foreach (UnitState u in state.LivingUnitsOf(Faction.Player))
            {
                if (u.MustSurvive || u.Position != state.Objective.Target) continue;
                state.Outcome = BattleOutcome.Victory;
                log.Add(new BattleEnded(BattleOutcome.Victory));
                return;
            }
        }

        /// <summary>
        /// Resolves the clock at the start of a player phase, i.e. once a full
        /// round has been played.
        /// </summary>
        private static void CheckTimedObjective(BattleState state, EffectLog log)
        {
            if (state.Outcome != BattleOutcome.InProgress) return;

            ObjectiveDef objective = state.Objective;
            if (!objective.HasTurnLimit) return;
            if (state.CurrentFaction != Faction.Player) return;
            if (state.TurnIndex <= objective.TurnLimit) return;

            switch (objective.Kind)
            {
                case ObjectiveKind.Survive:
                case ObjectiveKind.Defend:
                    // Held out long enough. Protected units are still alive, or the
                    // battle would already have ended.
                    state.Outcome = BattleOutcome.Victory;
                    log.Add(new BattleEnded(BattleOutcome.Victory));
                    break;

                default:
                    // Rout, Reach and Kill: ran out of time.
                    state.Outcome = BattleOutcome.Defeat;
                    log.Add(new BattleEnded(BattleOutcome.Defeat));
                    break;
            }
        }

        private static void CheckBattleEnd(BattleState state, EffectLog log)
        {
            if (state.Outcome != BattleOutcome.InProgress) return;

            // Losing something you were told to protect ends it immediately,
            // whatever else is going on.
            foreach (UnitState u in state.Units)
            {
                if (!u.MustSurvive || u.IsAlive || u.Faction != Faction.Player) continue;
                state.Outcome = BattleOutcome.Defeat;
                log.Add(new BattleEnded(BattleOutcome.Defeat));
                return;
            }

            if (state.CountLivingCombatants(Faction.Player) == 0)
            {
                state.Outcome = BattleOutcome.Defeat;
                log.Add(new BattleEnded(BattleOutcome.Defeat));
                return;
            }

            // The head came off. Checked before "field is clear" so a battle that
            // ends on the target's death reports that reason even when it happened
            // to be the last enemy standing.
            if (state.Objective.Kind == ObjectiveKind.Kill)
            {
                UnitState target = state.ObjectiveTarget();
                if (target != null && !target.IsAlive)
                {
                    state.Outcome = BattleOutcome.Victory;
                    log.Add(new BattleEnded(BattleOutcome.Victory));
                    return;
                }
            }

            // Clearing the field always wins, whatever the stated objective —
            // it keeps "kill them all" meaningful without making it the only path.
            if (state.CountLiving(Faction.Enemy) == 0 && !state.HasPendingReinforcements(Faction.Enemy))
            {
                state.Outcome = BattleOutcome.Victory;
                log.Add(new BattleEnded(BattleOutcome.Victory));
            }
        }
    }
}
